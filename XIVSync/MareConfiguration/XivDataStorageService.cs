using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public class XivDataStorageService : ConfigurationServiceBase<XivDataStorageConfig>
{
	public const string ConfigName = "xivdatastorage.json";

	public override string ConfigurationName => "xivdatastorage.json";

	public XivDataStorageService(string configDir)
		: base(configDir)
	{
	}
}
