using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto.Group;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;

namespace XIVSync.UI;

public class StandaloneProfileUi : WindowMediatorSubscriberBase
{
	private readonly MareProfileManager _mareProfileManager;

	private readonly PairManager _pairManager;

	private readonly ServerConfigurationManager _serverManager;

	private readonly UiSharedService _uiSharedService;

	private bool _adjustedForScrollBars;

	private byte[] _lastProfilePicture = Array.Empty<byte>();

	private byte[] _lastSupporterPicture = Array.Empty<byte>();

	private IDalamudTextureWrap? _supporterTextureWrap;

	private IDalamudTextureWrap? _textureWrap;

	public Pair Pair { get; init; }

	public StandaloneProfileUi(ILogger<StandaloneProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder, ServerConfigurationManager serverManager, MareProfileManager mareProfileManager, PairManager pairManager, Pair pair, PerformanceCollectorService performanceCollector)
		: base(logger, mediator, "Mare Profile of " + pair.UserData.AliasOrUID + "##XIVSyncStandaloneProfileUI" + pair.UserData.AliasOrUID, performanceCollector)
	{
		_uiSharedService = uiBuilder;
		_serverManager = serverManager;
		_mareProfileManager = mareProfileManager;
		Pair = pair;
		_pairManager = pairManager;
		base.Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize;
		Vector2 spacing = ImGui.GetStyle().ItemSpacing;
		base.Size = new Vector2(512f + spacing.X * 3f + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 512f);
		base.IsOpen = true;
	}

	protected override void DrawInternal()
	{
		try
		{
			Vector2 spacing = ImGui.GetStyle().ItemSpacing;
			MareProfileData mareProfile = _mareProfileManager.GetMareProfile(Pair.UserData);
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
			float headerSize = ImGui.GetCursorPosY() - ImGui.GetStyle().WindowPadding.Y;
			using (_uiSharedService.UidFont.Push())
			{
				UiSharedService.ColorText(Pair.UserData.AliasOrUID, ImGuiColors.HealerGreen);
			}
			ImGuiHelpers.ScaledDummy(new Vector2(spacing.Y, spacing.Y));
			float textPos = ImGui.GetCursorPosY() - headerSize;
			ImGui.Separator();
			Vector2 cursorPos = ImGui.GetCursorPos();
			cursorPos.Y = ImGui.GetCursorPosY() - headerSize;
			Vector2 pos = cursorPos;
			ImGuiHelpers.ScaledDummy(new Vector2(256f, 256f + spacing.Y));
			float postDummy = ImGui.GetCursorPosY();
			ImGui.SameLine();
			Vector2 descriptionTextSize = ImGui.CalcTextSize(MareProfileManager.FilterStatusFromDescription(mareProfile.Description), hideTextAfterDoubleHash: false, 256f);
			float descriptionChildHeight = rectMax.Y - pos.Y - rectMin.Y - spacing.Y * 2f;
			if (descriptionTextSize.Y > descriptionChildHeight && !_adjustedForScrollBars)
			{
				cursorPos = base.Size.Value;
				cursorPos.X = base.Size.Value.X + ImGui.GetStyle().ScrollbarSize;
				base.Size = cursorPos;
				_adjustedForScrollBars = true;
			}
			else if (descriptionTextSize.Y < descriptionChildHeight && _adjustedForScrollBars)
			{
				cursorPos = base.Size.Value;
				cursorPos.X = base.Size.Value.X - ImGui.GetStyle().ScrollbarSize;
				base.Size = cursorPos;
				_adjustedForScrollBars = false;
			}
			Vector2 childFrame = ImGuiHelpers.ScaledVector2(256f + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, descriptionChildHeight);
			cursorPos = childFrame;
			cursorPos.X = childFrame.X + (_adjustedForScrollBars ? ImGui.GetStyle().ScrollbarSize : 0f);
			cursorPos.Y = childFrame.Y / ImGuiHelpers.GlobalScale;
			childFrame = cursorPos;
			if (ImGui.BeginChildFrame(1000u, childFrame))
			{
				using (_uiSharedService.GameFont.Push())
				{
					ImGui.TextWrapped(MareProfileManager.FilterStatusFromDescription(mareProfile.Description));
				}
			}
			ImGui.EndChildFrame();
			ImGui.SetCursorPosY(postDummy);
			string note = _serverManager.GetNoteForUid(Pair.UserData.UID);
			if (!string.IsNullOrEmpty(note))
			{
				UiSharedService.ColorText(note, ImGuiColors.DalamudGrey);
			}
			UiSharedService.ColorText(Pair.IsVisible ? "Visible" : (Pair.IsOnline ? "Online" : "Offline"), (Pair.IsVisible || Pair.IsOnline) ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
			if (Pair.IsVisible)
			{
				ImGui.SameLine();
				ImU8String text = new ImU8String(2, 1);
				text.AppendLiteral("(");
				text.AppendFormatted(Pair.PlayerName);
				text.AppendLiteral(")");
				ImGui.TextUnformatted(text);
			}
			if (Pair.UserPair != null)
			{
				ImGui.TextUnformatted("Directly paired");
				if (Pair.UserPair.OwnPermissions.IsPaused())
				{
					ImGui.SameLine();
					UiSharedService.ColorText("You: paused", ImGuiColors.DalamudYellow);
				}
				if (Pair.UserPair.OtherPermissions.IsPaused())
				{
					ImGui.SameLine();
					UiSharedService.ColorText("They: paused", ImGuiColors.DalamudYellow);
				}
			}
			if (Pair.UserPair.Groups.Any())
			{
				ImGui.TextUnformatted("Paired through Syncshells:");
				foreach (string group in Pair.UserPair.Groups)
				{
					string groupNote = _serverManager.GetNoteForGid(group);
					string groupName = _pairManager.GroupPairs.First<KeyValuePair<GroupFullInfoDto, List<Pair>>>((KeyValuePair<GroupFullInfoDto, List<Pair>> f) => string.Equals(f.Key.GID, group, StringComparison.Ordinal)).Key.GroupAliasOrGID;
					string groupString = (string.IsNullOrEmpty(groupNote) ? groupName : (groupNote + " (" + groupName + ")"));
					ImGui.TextUnformatted("- " + groupString);
				}
			}
			float padding = ImGui.GetStyle().WindowPadding.X / 2f;
			float stretchFactor = ((_textureWrap.Height >= _textureWrap.Width) ? (256f * ImGuiHelpers.GlobalScale / (float)_textureWrap.Height) : (256f * ImGuiHelpers.GlobalScale / (float)_textureWrap.Width));
			float newWidth = (float)_textureWrap.Width * stretchFactor;
			float newHeight = (float)_textureWrap.Height * stretchFactor;
			float remainingWidth = (256f * ImGuiHelpers.GlobalScale - newWidth) / 2f;
			float remainingHeight = (256f * ImGuiHelpers.GlobalScale - newHeight) / 2f;
			drawList.AddImage(_textureWrap.Handle, new Vector2(rectMin.X + padding + remainingWidth, rectMin.Y + spacing.Y + pos.Y + remainingHeight), new Vector2(rectMin.X + padding + remainingWidth + newWidth, rectMin.Y + spacing.Y + pos.Y + remainingHeight + newHeight));
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

	public override void OnClose()
	{
		base.Mediator.Publish(new RemoveWindowMessage(this));
	}
}
