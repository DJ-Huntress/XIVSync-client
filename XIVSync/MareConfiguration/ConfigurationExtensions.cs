using System.IO;
using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public static class ConfigurationExtensions
{
	public static bool HasValidSetup(this MareConfig configuration)
	{
		if (configuration.AcceptedAgreement && configuration.InitialScanComplete && !string.IsNullOrEmpty(configuration.CacheFolder))
		{
			return Directory.Exists(configuration.CacheFolder);
		}
		return false;
	}
}
