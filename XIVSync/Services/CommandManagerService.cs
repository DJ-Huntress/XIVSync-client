using System;
using System.Globalization;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using XIVSync.FileCache;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.UI;
using XIVSync.WebAPI;
using XIVSync.WebAPI.SignalR.Utils;

namespace XIVSync.Services;

public sealed class CommandManagerService : IDisposable
{
	private const string _commandName = "/xiv";

	private readonly ApiController _apiController;

	private readonly ICommandManager _commandManager;

	private readonly MareMediator _mediator;

	private readonly MareConfigService _mareConfigService;

	private readonly PerformanceCollectorService _performanceCollectorService;

	private readonly CacheMonitor _cacheMonitor;

	private readonly ServerConfigurationManager _serverConfigurationManager;

	public CommandManagerService(ICommandManager commandManager, PerformanceCollectorService performanceCollectorService, ServerConfigurationManager serverConfigurationManager, CacheMonitor periodicFileScanner, ApiController apiController, MareMediator mediator, MareConfigService mareConfigService)
	{
		_commandManager = commandManager;
		_performanceCollectorService = performanceCollectorService;
		_serverConfigurationManager = serverConfigurationManager;
		_cacheMonitor = periodicFileScanner;
		_apiController = apiController;
		_mediator = mediator;
		_mareConfigService = mareConfigService;
		_commandManager.AddHandler("/xiv", new CommandInfo(OnCommand)
		{
			HelpMessage = "Opens the XIVSync UI" + Environment.NewLine + Environment.NewLine + "Additionally possible commands:" + Environment.NewLine + "\t /xiv toggle - Disconnects from XIVSync, if connected. Connects to XIVSync, if disconnected" + Environment.NewLine + "\t /xiv toggle on|off - Connects or disconnects to XIVSync respectively" + Environment.NewLine + "\t /xiv gpose - Opens the XIVSync Character Data Hub window" + Environment.NewLine + "\t /xiv analyze - Opens the XIVSync Character Data Analysis window" + Environment.NewLine + "\t /xiv settings - Opens the XIVSync Settings window"
		});
	}

	public void Dispose()
	{
		_commandManager.RemoveHandler("/xiv");
	}

	private void OnCommand(string command, string args)
	{
		string[] splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);
		if (splitArgs.Length == 0)
		{
			if (_mareConfigService.Current.HasValidSetup())
			{
				_mediator.Publish(new UiToggleMessage(typeof(CompactUi)));
			}
			else
			{
				_mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
			}
		}
		else
		{
			if (!_mareConfigService.Current.HasValidSetup())
			{
				return;
			}
			if (string.Equals(splitArgs[0], "toggle", StringComparison.OrdinalIgnoreCase))
			{
				if (_apiController.ServerState == ServerState.Disconnecting)
				{
					_mediator.Publish(new NotificationMessage("Mare disconnecting", "Cannot use /toggle while XIVSync is still disconnecting", NotificationType.Error));
				}
				if (_serverConfigurationManager.CurrentServer != null)
				{
					bool flag2;
					if (splitArgs.Length > 1)
					{
						string text = splitArgs[1];
						bool flag = !(text == "on") && (text == "off" || !_serverConfigurationManager.CurrentServer.FullPause);
						flag2 = flag;
					}
					else
					{
						flag2 = !_serverConfigurationManager.CurrentServer.FullPause;
					}
					bool fullPause = flag2;
					if (fullPause != _serverConfigurationManager.CurrentServer.FullPause)
					{
						_serverConfigurationManager.CurrentServer.FullPause = fullPause;
						_serverConfigurationManager.Save();
						_apiController.CreateConnectionsAsync();
					}
				}
			}
			else if (string.Equals(splitArgs[0], "gpose", StringComparison.OrdinalIgnoreCase))
			{
				_mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
			}
			else if (string.Equals(splitArgs[0], "rescan", StringComparison.OrdinalIgnoreCase))
			{
				_cacheMonitor.InvokeScan();
			}
			else if (string.Equals(splitArgs[0], "perf", StringComparison.OrdinalIgnoreCase))
			{
				if (splitArgs.Length > 1 && int.TryParse(splitArgs[1], CultureInfo.InvariantCulture, out var limitBySeconds))
				{
					_performanceCollectorService.PrintPerformanceStats(limitBySeconds);
				}
				else
				{
					_performanceCollectorService.PrintPerformanceStats();
				}
			}
			else if (string.Equals(splitArgs[0], "medi", StringComparison.OrdinalIgnoreCase))
			{
				_mediator.PrintSubscriberInfo();
			}
			else if (string.Equals(splitArgs[0], "analyze", StringComparison.OrdinalIgnoreCase))
			{
				_mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
			}
			else if (string.Equals(splitArgs[0], "settings", StringComparison.OrdinalIgnoreCase))
			{
				_mediator.Publish(new UiToggleMessage(typeof(ModernSettingsUi)));
			}
		}
	}
}
