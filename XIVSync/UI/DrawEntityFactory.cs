using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Extensions.Logging;
using XIVSync.API.Dto.Group;
using XIVSync.MareConfiguration;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.UI.Components;
using XIVSync.UI.Handlers;
using XIVSync.WebAPI;

namespace XIVSync.UI;

public class DrawEntityFactory
{
	private readonly ILogger<DrawEntityFactory> _logger;

	private readonly ApiController _apiController;

	private readonly MareMediator _mediator;

	private readonly SelectPairForTagUi _selectPairForTagUi;

	private readonly ServerConfigurationManager _serverConfigurationManager;

	private readonly UiSharedService _uiSharedService;

	private readonly PlayerPerformanceConfigService _playerPerformanceConfigService;

	private readonly CharaDataManager _charaDataManager;

	private readonly SelectTagForPairUi _selectTagForPairUi;

	private readonly TagHandler _tagHandler;

	private readonly IdDisplayHandler _uidDisplayHandler;

	private readonly MareConfigService _configService;

	private readonly MareProfileManager _profileManager;

	public DrawEntityFactory(ILogger<DrawEntityFactory> logger, ApiController apiController, IdDisplayHandler uidDisplayHandler, SelectTagForPairUi selectTagForPairUi, MareMediator mediator, TagHandler tagHandler, SelectPairForTagUi selectPairForTagUi, ServerConfigurationManager serverConfigurationManager, UiSharedService uiSharedService, PlayerPerformanceConfigService playerPerformanceConfigService, CharaDataManager charaDataManager, MareConfigService configService, MareProfileManager profileManager)
	{
		_logger = logger;
		_apiController = apiController;
		_uidDisplayHandler = uidDisplayHandler;
		_selectTagForPairUi = selectTagForPairUi;
		_mediator = mediator;
		_tagHandler = tagHandler;
		_selectPairForTagUi = selectPairForTagUi;
		_serverConfigurationManager = serverConfigurationManager;
		_uiSharedService = uiSharedService;
		_playerPerformanceConfigService = playerPerformanceConfigService;
		_charaDataManager = charaDataManager;
		_configService = configService;
		_profileManager = profileManager;
	}

	public DrawFolderGroup CreateDrawGroupFolder(GroupFullInfoDto groupFullInfoDto, Dictionary<Pair, List<GroupFullInfoDto>> filteredPairs, IImmutableList<Pair> allPairs)
	{
		return new DrawFolderGroup(groupFullInfoDto.Group.GID, groupFullInfoDto, _apiController, filteredPairs.Select<KeyValuePair<Pair, List<GroupFullInfoDto>>, DrawUserPair>((KeyValuePair<Pair, List<GroupFullInfoDto>> p) => CreateDrawPair(groupFullInfoDto.Group.GID + p.Key.UserData.UID, p.Key, p.Value, groupFullInfoDto)).ToImmutableList(), allPairs, _tagHandler, _uidDisplayHandler, _mediator, _uiSharedService);
	}

	public DrawFolderTag CreateDrawTagFolder(string tag, Dictionary<Pair, List<GroupFullInfoDto>> filteredPairs, IImmutableList<Pair> allPairs)
	{
		return new DrawFolderTag(tag, filteredPairs.Select<KeyValuePair<Pair, List<GroupFullInfoDto>>, DrawUserPair>((KeyValuePair<Pair, List<GroupFullInfoDto>> u) => CreateDrawPair(tag, u.Key, u.Value, null)).ToImmutableList(), allPairs, _tagHandler, _apiController, _selectPairForTagUi, _uiSharedService);
	}

	public DrawUserPair CreateDrawPair(string id, Pair user, List<GroupFullInfoDto> groups, GroupFullInfoDto? currentGroup)
	{
		return new DrawUserPair(id + user.UserData.UID, user, groups, currentGroup, _apiController, _uidDisplayHandler, _mediator, _selectTagForPairUi, _serverConfigurationManager, _uiSharedService, _playerPerformanceConfigService, _charaDataManager, _configService, _profileManager);
	}
}
