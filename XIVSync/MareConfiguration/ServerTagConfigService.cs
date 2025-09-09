using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public class ServerTagConfigService : ConfigurationServiceBase<ServerTagConfig>
{
	public const string ConfigName = "servertags.json";

	public override string ConfigurationName => "servertags.json";

	public ServerTagConfigService(string configDir)
		: base(configDir)
	{
	}
}
