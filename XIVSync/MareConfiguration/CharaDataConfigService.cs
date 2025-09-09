using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public class CharaDataConfigService : ConfigurationServiceBase<CharaDataConfig>
{
	public const string ConfigName = "charadata.json";

	public override string ConfigurationName => "charadata.json";

	public CharaDataConfigService(string configDir)
		: base(configDir)
	{
	}
}
