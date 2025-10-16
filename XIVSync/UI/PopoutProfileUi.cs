using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Enum;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto.Group;
using XIVSync.MareConfiguration;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;

namespace XIVSync.UI;

public class PopoutProfileUi : WindowMediatorSubscriberBase
{
	private readonly MareProfileManager _mareProfileManager;

	private readonly PairManager _pairManager;

	private readonly ServerConfigurationManager _serverManager;

	private readonly UiSharedService _uiSharedService;

	private Vector2 _lastMainPos = Vector2.Zero;

	private Vector2 _lastMainSize = Vector2.Zero;

	private byte[] _lastProfilePicture = Array.Empty<byte>();

	private byte[] _lastSupporterPicture = Array.Empty<byte>();

	private Pair? _pair;

	private IDalamudTextureWrap? _supporterTextureWrap;

	private IDalamudTextureWrap? _textureWrap;

	public PopoutProfileUi(ILogger<PopoutProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder, ServerConfigurationManager serverManager, MareConfigService mareConfigService, MareProfileManager mareProfileManager, PairManager pairManager, PerformanceCollectorService performanceCollectorService) : base(logger, mediator, "###XIVSyncProfileUI", performanceCollectorService)
	{
		PopoutProfileUi popoutProfileUi = this;
		_uiSharedService = uiBuilder;
		_serverManager = serverManager;
		_mareProfileManager = mareProfileManager;
		_pairManager = pairManager;
		base.Flags = ImGuiWindowFlags.NoDecoration;
		base.Mediator.Subscribe(this, delegate(ProfilePopoutToggle msg)
		{
			popoutProfileUi.IsOpen = msg.Pair != null;
			popoutProfileUi._pair = msg.Pair;
			popoutProfileUi._lastProfilePicture = Array.Empty<byte>();
			popoutProfileUi._lastSupporterPicture = Array.Empty<byte>();
			popoutProfileUi._textureWrap?.Dispose();
			popoutProfileUi._textureWrap = null;
			popoutProfileUi._supporterTextureWrap?.Dispose();
			popoutProfileUi._supporterTextureWrap = null;
		});
		base.Mediator.Subscribe(this, delegate(CompactUiChange msg)
		{
			if (msg.Size != Vector2.Zero)
			{
				float windowBorderSize = ImGui.GetStyle().WindowBorderSize;
				Vector2 windowPadding = ImGui.GetStyle().WindowPadding;
				popoutProfileUi.Size = new Vector2(256f + windowPadding.X * 2f + windowBorderSize, msg.Size.Y / ImGuiHelpers.GlobalScale);
				popoutProfileUi._lastMainSize = msg.Size;
			}
			Vector2 vector = ((msg.Position == Vector2.Zero) ? popoutProfileUi._lastMainPos : msg.Position);
			if (mareConfigService.Current.ProfilePopoutRight)
			{
				popoutProfileUi.Position = new Vector2(vector.X + popoutProfileUi._lastMainSize.X * ImGuiHelpers.GlobalScale, vector.Y);
			}
			else
			{
				popoutProfileUi.Position = new Vector2(vector.X - popoutProfileUi.Size.Value.X * ImGuiHelpers.GlobalScale, vector.Y);
			}
			if (msg.Position != Vector2.Zero)
			{
				popoutProfileUi._lastMainPos = msg.Position;
			}
		});
		base.IsOpen = false;
	}

	protected override void DrawInternal()
	{
		if (_pair == null)
		{
			return;
		}
		try
		{
			Vector2 spacing = ImGui.GetStyle().ItemSpacing;
			MareProfileData mareProfile = _mareProfileManager.GetMareProfile(_pair.UserData);
			if (_textureWrap == null || !mareProfile.ImageData.Value.SequenceEqual(_lastProfilePicture))
			{
				_textureWrap?.Dispose();
				_lastProfilePicture = mareProfile.ImageData.Value;
				_textureWrap = _uiSharedService.LoadImage(_lastProfilePicture);
			}
			if (_supporterTextureWrap == null || !mareProfile.SupporterImageData.Value.SequenceEqual(_lastSupporterPicture))
			{
				_supporterTextureWrap?.Dispose();
				_supporterTextureWrap = null;
				if (!string.IsNullOrEmpty(mareProfile.Base64SupporterPicture))
				{
					_lastSupporterPicture = mareProfile.SupporterImageData.Value;
					_supporterTextureWrap = _uiSharedService.LoadImage(_lastSupporterPicture);
				}
			}
			ImDrawListPtr drawList = ImGui.GetWindowDrawList();
			Vector2 rectMin = drawList.GetClipRectMin();
			Vector2 rectMax = drawList.GetClipRectMax();
			using (_uiSharedService.UidFont.Push())
			{
				UiSharedService.ColorText(_pair.UserData.AliasOrUID, ImGuiColors.HealerGreen);
			}
			ImGuiHelpers.ScaledDummy(spacing.Y, spacing.Y);
			float textPos = ImGui.GetCursorPosY();
			ImGui.Separator();
			Vector2 imagePos = ImGui.GetCursorPos();
			ImGuiHelpers.ScaledDummy(256f, 256f * ImGuiHelpers.GlobalScale + spacing.Y);
			string note = _serverManager.GetNoteForUid(_pair.UserData.UID);
			if (!string.IsNullOrEmpty(note))
			{
				UiSharedService.ColorText(note, ImGuiColors.DalamudGrey);
			}
			UiSharedService.ColorText(_pair.IsVisible ? "Visible" : (_pair.IsOnline ? "Online" : "Offline"), (_pair.IsVisible || _pair.IsOnline) ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
			if (_pair.IsVisible)
			{
				ImGui.SameLine();
				ImU8String text = new ImU8String(2, 1);
				text.AppendLiteral("(");
				text.AppendFormatted(_pair.PlayerName);
				text.AppendLiteral(")");
				ImGui.TextUnformatted(text);
			}
			if (_pair.UserPair.IndividualPairStatus == IndividualPairStatus.Bidirectional)
			{
				ImGui.TextUnformatted("Directly paired");
				if (_pair.UserPair.OwnPermissions.IsPaused())
				{
					ImGui.SameLine();
					UiSharedService.ColorText("You: paused", ImGuiColors.DalamudYellow);
				}
				if (_pair.UserPair.OtherPermissions.IsPaused())
				{
					ImGui.SameLine();
					UiSharedService.ColorText("They: paused", ImGuiColors.DalamudYellow);
				}
			}
			if (_pair.UserPair.Groups.Any())
			{
				ImGui.TextUnformatted("Paired through Syncshells:");
				foreach (string group in _pair.UserPair.Groups)
				{
					string groupNote = _serverManager.GetNoteForGid(group);
					string groupName = _pairManager.GroupPairs.First<KeyValuePair<GroupFullInfoDto, List<Pair>>>((KeyValuePair<GroupFullInfoDto, List<Pair>> f) => string.Equals(f.Key.GID, group, StringComparison.Ordinal)).Key.GroupAliasOrGID;
					string groupString = (string.IsNullOrEmpty(groupNote) ? groupName : (groupNote + " (" + groupName + ")"));
					ImGui.TextUnformatted("- " + groupString);
				}
			}
			ImGui.Separator();
			IDisposable font = _uiSharedService.GameFont.Push();
			float remaining = ImGui.GetWindowContentRegionMax().Y - ImGui.GetCursorPosY();
			string descText = mareProfile.Description;
			Vector2 textSize = ImGui.CalcTextSize(descText, hideTextAfterDoubleHash: false, 256f * ImGuiHelpers.GlobalScale);
			bool trimmed = textSize.Y > remaining;
			while (textSize.Y > remaining && descText.Contains(' '))
			{
				descText = descText.Substring(0, descText.LastIndexOf(' ')).TrimEnd();
				textSize = ImGui.CalcTextSize(descText + "..." + Environment.NewLine + "[Open Full Profile for complete description]", hideTextAfterDoubleHash: false, 256f * ImGuiHelpers.GlobalScale);
			}
			UiSharedService.TextWrapped(trimmed ? (descText + "..." + Environment.NewLine + "[Open Full Profile for complete description]") : mareProfile.Description);
			font.Dispose();
			float padding = ImGui.GetStyle().WindowPadding.X / 2f;
			float stretchFactor = ((_textureWrap.Height >= _textureWrap.Width) ? (256f * ImGuiHelpers.GlobalScale / (float)_textureWrap.Height) : (256f * ImGuiHelpers.GlobalScale / (float)_textureWrap.Width));
			float newWidth = (float)_textureWrap.Width * stretchFactor;
			float newHeight = (float)_textureWrap.Height * stretchFactor;
			float remainingWidth = (256f * ImGuiHelpers.GlobalScale - newWidth) / 2f;
			float remainingHeight = (256f * ImGuiHelpers.GlobalScale - newHeight) / 2f;
			drawList.AddImage(_textureWrap.Handle, new Vector2(rectMin.X + padding + remainingWidth, rectMin.Y + spacing.Y + imagePos.Y + remainingHeight), new Vector2(rectMin.X + padding + remainingWidth + newWidth, rectMin.Y + spacing.Y + imagePos.Y + remainingHeight + newHeight));
			if (_supporterTextureWrap != null)
			{
				drawList.AddImage(_supporterTextureWrap.Handle, new Vector2(rectMax.X - 38f - spacing.X, rectMin.Y + textPos / 2f - 19f), new Vector2(rectMax.X - spacing.X, rectMin.Y + 38f + textPos / 2f - 19f));
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Error during draw tooltip");
		}
	}
}
