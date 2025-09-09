using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Configurations;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services.Mediator;
using XIVSync.WebAPI;

namespace XIVSync.UI;

public sealed class DtrEntry : IDisposable, IHostedService
{
	public readonly record struct Colors(uint Foreground = 0u, uint Glow = 0u);

	private readonly ApiController _apiController;

	private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

	private readonly ConfigurationServiceBase<MareConfig> _configService;

	private readonly IDtrBar _dtrBar;

	private readonly Lazy<IDtrBarEntry> _entry;

	private readonly ILogger<DtrEntry> _logger;

	private readonly MareMediator _mareMediator;

	private readonly PairManager _pairManager;

	private Task? _runTask;

	private string? _text;

	private string? _tooltip;

	private Colors _colors;

	private const byte _colorTypeForeground = 19;

	private const byte _colorTypeGlow = 20;

	public DtrEntry(ILogger<DtrEntry> logger, IDtrBar dtrBar, ConfigurationServiceBase<MareConfig> configService, MareMediator mareMediator, PairManager pairManager, ApiController apiController)
	{
		_logger = logger;
		_dtrBar = dtrBar;
		_entry = new Lazy<IDtrBarEntry>(CreateEntry);
		_configService = configService;
		_mareMediator = mareMediator;
		_pairManager = pairManager;
		_apiController = apiController;
	}

	public void Dispose()
	{
		if (_entry.IsValueCreated)
		{
			_logger.LogDebug("Disposing DtrEntry");
			Clear();
			_entry.Value.Remove();
		}
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting DtrEntry");
		_runTask = Task.Run((Func<Task?>)RunAsync, _cancellationTokenSource.Token);
		_logger.LogInformation("Started DtrEntry");
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		await _cancellationTokenSource.CancelAsync();
		try
		{
			await _runTask.ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			_cancellationTokenSource.Dispose();
		}
	}

	private void Clear()
	{
		if (_entry.IsValueCreated)
		{
			_logger.LogInformation("Clearing entry");
			_text = null;
			_tooltip = null;
			_colors = default(Colors);
			_entry.Value.Shown = false;
		}
	}

	private IDtrBarEntry CreateEntry()
	{
		_logger.LogTrace("Creating new DtrBar entry");
		IDtrBarEntry dtrBarEntry = _dtrBar.Get("XIVSync");
		dtrBarEntry.OnClick = delegate
		{
			_mareMediator.Publish(new UiToggleMessage(typeof(CompactUi)));
		};
		return dtrBarEntry;
	}

	private async Task RunAsync()
	{
		while (!_cancellationTokenSource.IsCancellationRequested)
		{
			await Task.Delay(1000, _cancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
			Update();
		}
	}

	private void Update()
	{
		if (!_configService.Current.EnableDtrEntry || !_configService.Current.HasValidSetup())
		{
			if (_entry.IsValueCreated && _entry.Value.Shown)
			{
				_logger.LogInformation("Disabling entry");
				Clear();
			}
			return;
		}
		if (!_entry.Value.Shown)
		{
			_logger.LogInformation("Showing entry");
			_entry.Value.Shown = true;
		}
		string text;
		string tooltip;
		Colors colors;
		if (_apiController.IsConnected)
		{
			int pairCount = _pairManager.GetVisibleUserCount();
			text = $"\ue044 {pairCount}";
			if (pairCount > 0)
			{
				IEnumerable<string> visiblePairs = ((!_configService.Current.ShowUidInDtrTooltip) ? (from x in _pairManager.GetOnlineUserPairs()
					where x.IsVisible
					select $"{(_configService.Current.PreferNoteInDtrTooltip ? (x.GetNote() ?? x.PlayerName) : x.PlayerName)}") : (from x in _pairManager.GetOnlineUserPairs()
					where x.IsVisible
					select $"{(_configService.Current.PreferNoteInDtrTooltip ? (x.GetNote() ?? x.PlayerName) : x.PlayerName)} ({x.UserData.AliasOrUID})"));
				tooltip = $"XIVSync: Connected{Environment.NewLine}----------{Environment.NewLine}{string.Join(Environment.NewLine, visiblePairs)}";
				colors = _configService.Current.DtrColorsPairsInRange;
			}
			else
			{
				tooltip = "XIVSync: Connected";
				colors = _configService.Current.DtrColorsDefault;
			}
		}
		else
		{
			text = "\ue044 \ue04c";
			tooltip = "XIVSync: Not Connected";
			colors = _configService.Current.DtrColorsNotConnected;
		}
		if (!_configService.Current.UseColorsInDtr)
		{
			colors = default(Colors);
		}
		if (!string.Equals(text, _text, StringComparison.Ordinal) || !string.Equals(tooltip, _tooltip, StringComparison.Ordinal) || colors != _colors)
		{
			_text = text;
			_tooltip = tooltip;
			_colors = colors;
			_entry.Value.Text = BuildColoredSeString(text, colors);
			_entry.Value.Tooltip = tooltip;
		}
	}

	private static SeString BuildColoredSeString(string text, Colors colors)
	{
		SeStringBuilder ssb = new SeStringBuilder();
		if (colors.Foreground != 0)
		{
			ssb.Add(BuildColorStartPayload(19, colors.Foreground));
		}
		if (colors.Glow != 0)
		{
			ssb.Add(BuildColorStartPayload(20, colors.Glow));
		}
		ssb.AddText(text);
		if (colors.Glow != 0)
		{
			ssb.Add(BuildColorEndPayload(20));
		}
		if (colors.Foreground != 0)
		{
			ssb.Add(BuildColorEndPayload(19));
		}
		return ssb.Build();
	}

	private static RawPayload BuildColorStartPayload(byte colorType, uint color)
	{
		byte[] obj = new byte[8] { 2, 0, 5, 246, 0, 0, 0, 3 };
		obj[1] = colorType;
		obj[4] = byte.Max((byte)color, 1);
		obj[5] = byte.Max((byte)(color >> 8), 1);
		obj[6] = byte.Max((byte)(color >> 16), 1);
		return new RawPayload(obj);
	}

	private static RawPayload BuildColorEndPayload(byte colorType)
	{
		byte[] obj = new byte[5] { 2, 0, 2, 236, 3 };
		obj[1] = colorType;
		return new RawPayload(obj);
	}
}
