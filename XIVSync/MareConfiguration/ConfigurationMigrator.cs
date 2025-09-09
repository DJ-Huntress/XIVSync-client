using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration.Models;

namespace XIVSync.MareConfiguration;

public class ConfigurationMigrator : IHostedService
{
	[CompilerGenerated]
	private TransientConfigService _003CtransientConfigService_003EP;

	[CompilerGenerated]
	private ServerConfigService _003CserverConfigService_003EP;

	private readonly ILogger<ConfigurationMigrator> _logger;

	public ConfigurationMigrator(ILogger<ConfigurationMigrator> logger, TransientConfigService transientConfigService, ServerConfigService serverConfigService)
	{
		_003CtransientConfigService_003EP = transientConfigService;
		_003CserverConfigService_003EP = serverConfigService;
		_logger = logger;
		base._002Ector();
	}

	public void Migrate()
	{
		if (_003CtransientConfigService_003EP.Current.Version == 0)
		{
			_logger.LogInformation("Migrating Transient Config V0 => V1");
			_003CtransientConfigService_003EP.Current.TransientConfigs.Clear();
			_003CtransientConfigService_003EP.Current.Version = 1;
			_003CtransientConfigService_003EP.Save();
		}
		if (_003CserverConfigService_003EP.Current.Version == 1)
		{
			_logger.LogInformation("Migrating Server Config V1 => V2");
			ServerStorage centralServer = _003CserverConfigService_003EP.Current.ServerStorage.Find((ServerStorage f) => f.ServerName.Equals("Lunae Crescere Incipientis (Central Server EU)", StringComparison.Ordinal));
			if (centralServer != null)
			{
				centralServer.ServerName = "XIVSync Central Server";
			}
			_003CserverConfigService_003EP.Current.Version = 2;
			_003CserverConfigService_003EP.Save();
		}
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		Migrate();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
