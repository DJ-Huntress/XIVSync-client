using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;
using XIVSync.Services.Mediator;

namespace XIVSync.Services;

public class ChangelogService : IHostedService
{
	private readonly ILogger<ChangelogService> _logger;

	private readonly MareConfigService _configService;

	private readonly MareMediator _mediator;

	public ChangelogService(ILogger<ChangelogService> logger, MareConfigService configService, MareMediator mediator)
	{
		_logger = logger;
		_configService = configService;
		_mediator = mediator;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		CheckForVersionChange();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	private void CheckForVersionChange()
	{
		try
		{
			string currentVersion = GetCurrentVersion();
			string lastSeenVersion = _configService.Current.LastSeenVersion;
			_logger.LogDebug("Current version: {CurrentVersion}, Last seen version: {LastSeenVersion}", currentVersion, lastSeenVersion);
			if (string.IsNullOrEmpty(lastSeenVersion) || !string.Equals(currentVersion, lastSeenVersion, StringComparison.Ordinal))
			{
				_logger.LogInformation("Version change detected, showing changelog. Current: {CurrentVersion}, Last seen: {LastSeenVersion}", currentVersion, lastSeenVersion);
				Task.Delay(2000, CancellationToken.None).ContinueWith(delegate
				{
					_mediator.Publish(new OpenChangelogPopupMessage(currentVersion));
				}, TaskScheduler.Default);
			}
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Error checking for version change");
		}
	}

	private string GetCurrentVersion()
	{
		Version version = Assembly.GetExecutingAssembly().GetName().Version;
		if (version == null)
		{
			return "Unknown";
		}
		return $"{version.Major}.{version.Minor}.{version.Build}";
	}
}
