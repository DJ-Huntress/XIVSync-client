using System.Collections.Generic;
using XIVSync.MareConfiguration.Models;

namespace XIVSync.MareConfiguration.Configurations;

public class CharaDataConfig : IMareConfiguration
{
	public bool OpenMareHubOnGposeStart { get; set; }

	public string LastSavedCharaDataLocation { get; set; } = string.Empty;


	public Dictionary<string, CharaDataFavorite> FavoriteCodes { get; set; } = new Dictionary<string, CharaDataFavorite>();


	public bool DownloadMcdDataOnConnection { get; set; } = true;


	public int Version { get; set; }

	public bool NearbyOwnServerOnly { get; set; }

	public bool NearbyIgnoreHousingLimitations { get; set; }

	public bool NearbyDrawWisps { get; set; } = true;


	public int NearbyDistanceFilter { get; set; } = 100;


	public bool NearbyShowOwnData { get; set; }

	public bool ShowHelpTexts { get; set; } = true;


	public bool NearbyShowAlways { get; set; }
}
