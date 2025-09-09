using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public class MareConfigService : ConfigurationServiceBase<MareConfig>
{
	public const string ConfigName = "config.json";

	public override string ConfigurationName => "config.json";

	public MareConfigService(string configDir)
		: base(configDir)
	{
	}
}
