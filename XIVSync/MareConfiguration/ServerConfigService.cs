using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public class ServerConfigService : ConfigurationServiceBase<ServerConfig>
{
	public const string ConfigName = "server.json";

	public override string ConfigurationName => "server.json";

	public ServerConfigService(string configDir)
		: base(configDir)
	{
	}
}
