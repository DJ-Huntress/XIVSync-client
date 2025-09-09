using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public class NotesConfigService : ConfigurationServiceBase<UidNotesConfig>
{
	public const string ConfigName = "notes.json";

	public override string ConfigurationName => "notes.json";

	public NotesConfigService(string configDir)
		: base(configDir)
	{
	}
}
