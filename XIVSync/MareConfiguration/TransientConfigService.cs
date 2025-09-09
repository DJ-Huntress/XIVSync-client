using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public class TransientConfigService : ConfigurationServiceBase<TransientConfig>
{
	public const string ConfigName = "transient.json";

	public override string ConfigurationName => "transient.json";

	public TransientConfigService(string configDir)
		: base(configDir)
	{
	}
}
