using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.FileCache;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.PlayerData.Pairs;
using XIVSync.PlayerData.Services;
using XIVSync.Services;
using XIVSync.Services.Events;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;

namespace XIVSync;

public class MarePlugin : MediatorSubscriberBase, IHostedService
{
	private readonly DalamudUtilService _dalamudUtil;

	private readonly MareConfigService _mareConfigService;

	private readonly ServerConfigurationManager _serverConfigurationManager;

	private readonly IServiceScopeFactory _serviceScopeFactory;

	private IServiceScope? _runtimeServiceScope;

	private Task? _launchTask;

	public MarePlugin(ILogger<MarePlugin> logger, MareConfigService mareConfigService, ServerConfigurationManager serverConfigurationManager, DalamudUtilService dalamudUtil, IServiceScopeFactory serviceScopeFactory, MareMediator mediator)
		: base(logger, mediator)
	{
		_mareConfigService = mareConfigService;
		_serverConfigurationManager = serverConfigurationManager;
		_dalamudUtil = dalamudUtil;
		_serviceScopeFactory = serviceScopeFactory;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		Version version = Assembly.GetExecutingAssembly().GetName().Version;
		base.Logger.LogInformation("Launching {name} {major}.{minor}.{build}", "XIVSync", version.Major, version.Minor, version.Build);
		base.Mediator.Publish(new EventMessage(new Event("MarePlugin", EventSeverity.Informational, $"Starting XIVSync {version.Major}.{version.Minor}.{version.Build}")));
		base.Mediator.Subscribe<SwitchToMainUiMessage>(this, delegate
		{
			if (_launchTask == null || _launchTask.IsCompleted)
			{
				_launchTask = Task.Run((Func<Task?>)WaitForPlayerAndLaunchCharacterManager);
			}
		});
		base.Mediator.Subscribe<DalamudLoginMessage>(this, delegate
		{
			DalamudUtilOnLogIn();
		});
		base.Mediator.Subscribe<DalamudLogoutMessage>(this, delegate
		{
			DalamudUtilOnLogOut();
		});
		base.Mediator.StartQueueProcessing();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		UnsubscribeAll();
		DalamudUtilOnLogOut();
		base.Logger.LogDebug("Halting MarePlugin");
		return Task.CompletedTask;
	}

	private void DalamudUtilOnLogIn()
	{
		base.Logger?.LogDebug("Client login");
		if (_launchTask == null || _launchTask.IsCompleted)
		{
			_launchTask = Task.Run((Func<Task?>)WaitForPlayerAndLaunchCharacterManager);
		}
	}

	private void DalamudUtilOnLogOut()
	{
		base.Logger?.LogDebug("Client logout");
		_runtimeServiceScope?.Dispose();
	}

	private async Task WaitForPlayerAndLaunchCharacterManager()
	{
		while (!(await _dalamudUtil.GetIsPlayerPresentAsync().ConfigureAwait(continueOnCapturedContext: false)))
		{
			await Task.Delay(100).ConfigureAwait(continueOnCapturedContext: false);
		}
		try
		{
			base.Logger?.LogDebug("Launching Managers");
			_runtimeServiceScope?.Dispose();
			_runtimeServiceScope = _serviceScopeFactory.CreateScope();
			_runtimeServiceScope.ServiceProvider.GetRequiredService<UiService>();
			_runtimeServiceScope.ServiceProvider.GetRequiredService<CommandManagerService>();
			if (!_mareConfigService.Current.HasValidSetup() || !_serverConfigurationManager.HasValidConfig())
			{
				base.Mediator.Publish(new SwitchToIntroUiMessage());
				return;
			}
			_runtimeServiceScope.ServiceProvider.GetRequiredService<CacheCreationService>();
			_runtimeServiceScope.ServiceProvider.GetRequiredService<TransientResourceManager>();
			_runtimeServiceScope.ServiceProvider.GetRequiredService<VisibleUserDataDistributor>();
			_runtimeServiceScope.ServiceProvider.GetRequiredService<NotificationService>();
			if (_mareConfigService.Current.LogLevel != LogLevel.Information)
			{
				base.Mediator.Publish(new NotificationMessage("Abnormal Log Level", $"Your log level is set to '{_mareConfigService.Current.LogLevel}' which is not recommended for normal usage. Set it to '{2}' in \"Mare Settings -> Debug\" unless instructed otherwise.", NotificationType.Error, TimeSpan.FromSeconds(15000L)));
			}
		}
		catch (Exception ex)
		{
			base.Logger?.LogCritical(ex, "Error during launch of managers");
		}
	}
}
