using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using XIVSync.API.Dto.Group;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services.Mediator;
using XIVSync.WebAPI;

namespace XIVSync.UI.Components.Popup;

public class BanUserPopupHandler : IPopupHandler
{
	private readonly ApiController _apiController;

	private readonly UiSharedService _uiSharedService;

	private string _banReason = string.Empty;

	private GroupFullInfoDto _group;

	private Pair _reportedPair;

	public Vector2 PopupSize => new Vector2(500f, 250f);

	public bool ShowClose => true;

	public BanUserPopupHandler(ApiController apiController, UiSharedService uiSharedService)
	{
		_apiController = apiController;
		_uiSharedService = uiSharedService;
	}

	public void DrawContent()
	{
		UiSharedService.TextWrapped("User " + _reportedPair.UserData.AliasOrUID + " will be banned and removed from this Syncshell.");
		ImGui.InputTextWithHint("##banreason", "Ban Reason", ref _banReason, 255);
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserSlash, "Ban User"))
		{
			ImGui.CloseCurrentPopup();
			string reason = _banReason;
			_apiController.GroupBanUser(new GroupPairDto(_group.Group, _reportedPair.UserData), reason);
			_banReason = string.Empty;
		}
		UiSharedService.TextWrapped("The reason will be displayed in the banlist. The current server-side alias if present (Vanity ID) will automatically be attached to the reason.");
	}

	public void Open(OpenBanUserPopupMessage message)
	{
		_reportedPair = message.PairToBan;
		_group = message.GroupFullInfoDto;
		_banReason = string.Empty;
	}
}
