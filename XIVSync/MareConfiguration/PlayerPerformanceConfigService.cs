using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public class PlayerPerformanceConfigService : ConfigurationServiceBase<PlayerPerformanceConfig>
{
	public const string ConfigName = "playerperformance.json";

	public override string ConfigurationName => "playerperformance.json";

	public PlayerPerformanceConfigService(string configDir)
		: base(configDir)
	{
	}
}
