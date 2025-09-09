using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Enum;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto;
using XIVSync.API.Dto.User;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Utils;
using XIVSync.WebAPI;

namespace XIVSync.UI;

public class PermissionWindowUI : WindowMediatorSubscriberBase
{
	private readonly UiSharedService _uiSharedService;

	private readonly ApiController _apiController;

	private UserPermissions _ownPermissions;

	public Pair Pair { get; init; }

	public PermissionWindowUI(ILogger<PermissionWindowUI> logger, Pair pair, MareMediator mediator, UiSharedService uiSharedService, ApiController apiController, PerformanceCollectorService performanceCollectorService)
		: base(logger, mediator, "Permissions for " + pair.UserData.AliasOrUID + "###XIVSyncPermissions" + pair.UserData.UID, performanceCollectorService)
	{
		Pair = pair;
		_uiSharedService = uiSharedService;
		_apiController = apiController;
		_ownPermissions = pair.UserPair.OwnPermissions.DeepClone();
		base.Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar;
		base.SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(450f, 100f),
			MaximumSize = new Vector2(450f, 500f)
		};
		base.IsOpen = true;
	}

	protected override void DrawInternal()
	{
		bool sticky = _ownPermissions.IsSticky();
		bool paused = _ownPermissions.IsPaused();
		bool disableSounds = _ownPermissions.IsDisableSounds();
		bool disableAnimations = _ownPermissions.IsDisableAnimations();
		bool disableVfx = _ownPermissions.IsDisableVFX();
		ImGuiStylePtr style = ImGui.GetStyle();
		float indentSize = ImGui.GetFrameHeight() + style.ItemSpacing.X;
		_uiSharedService.BigText("Permissions for " + Pair.UserData.AliasOrUID);
		ImGuiHelpers.ScaledDummy(1f);
		if (ImGui.Checkbox("Preferred Permissions", ref sticky))
		{
			_ownPermissions.SetSticky(sticky);
		}
		_uiSharedService.DrawHelpText("Preferred Permissions, when enabled, will exclude this user from any permission changes on any syncshells you share with this user.");
		ImGuiHelpers.ScaledDummy(1f);
		if (ImGui.Checkbox("Pause Sync", ref paused))
		{
			_ownPermissions.SetPaused(paused);
		}
		_uiSharedService.DrawHelpText("Pausing will completely cease any sync with this user.--SEP--Note: this is bidirectional, either user pausing will cease sync completely.");
		UserPermissions otherPermissions = Pair.UserPair.OtherPermissions;
		bool otherIsPaused = otherPermissions.IsPaused();
		bool otherDisableSounds = otherPermissions.IsDisableSounds();
		bool otherDisableAnimations = otherPermissions.IsDisableAnimations();
		bool otherDisableVFX = otherPermissions.IsDisableVFX();
		using (ImRaii.PushIndent(indentSize, scaled: false))
		{
			_uiSharedService.BooleanToColoredIcon(!otherIsPaused, inline: false);
			ImGui.SameLine();
			ImGui.AlignTextToFramePadding();
			ImGui.Text(Pair.UserData.AliasOrUID + " has " + ((!otherIsPaused) ? "not " : string.Empty) + "paused you");
		}
		ImGuiHelpers.ScaledDummy(0.5f);
		ImGui.Separator();
		ImGuiHelpers.ScaledDummy(0.5f);
		if (ImGui.Checkbox("Disable Sounds", ref disableSounds))
		{
			_ownPermissions.SetDisableSounds(disableSounds);
		}
		_uiSharedService.DrawHelpText("Disabling sounds will remove all sounds synced with this user on both sides.--SEP--Note: this is bidirectional, either user disabling sound sync will stop sound sync on both sides.");
		using (ImRaii.PushIndent(indentSize, scaled: false))
		{
			_uiSharedService.BooleanToColoredIcon(!otherDisableSounds, inline: false);
			ImGui.SameLine();
			ImGui.AlignTextToFramePadding();
			ImGui.Text(Pair.UserData.AliasOrUID + " has " + ((!otherDisableSounds) ? "not " : string.Empty) + "disabled sound sync with you");
		}
		if (ImGui.Checkbox("Disable Animations", ref disableAnimations))
		{
			_ownPermissions.SetDisableAnimations(disableAnimations);
		}
		_uiSharedService.DrawHelpText("Disabling sounds will remove all animations synced with this user on both sides.--SEP--Note: this is bidirectional, either user disabling animation sync will stop animation sync on both sides.");
		using (ImRaii.PushIndent(indentSize, scaled: false))
		{
			_uiSharedService.BooleanToColoredIcon(!otherDisableAnimations, inline: false);
			ImGui.SameLine();
			ImGui.AlignTextToFramePadding();
			ImGui.Text(Pair.UserData.AliasOrUID + " has " + ((!otherDisableAnimations) ? "not " : string.Empty) + "disabled animation sync with you");
		}
		if (ImGui.Checkbox("Disable VFX", ref disableVfx))
		{
			_ownPermissions.SetDisableVFX(disableVfx);
		}
		_uiSharedService.DrawHelpText("Disabling sounds will remove all VFX synced with this user on both sides.--SEP--Note: this is bidirectional, either user disabling VFX sync will stop VFX sync on both sides.");
		using (ImRaii.PushIndent(indentSize, scaled: false))
		{
			_uiSharedService.BooleanToColoredIcon(!otherDisableVFX, inline: false);
			ImGui.SameLine();
			ImGui.AlignTextToFramePadding();
			ImGui.Text(Pair.UserData.AliasOrUID + " has " + ((!otherDisableVFX) ? "not " : string.Empty) + "disabled VFX sync with you");
		}
		ImGuiHelpers.ScaledDummy(0.5f);
		ImGui.Separator();
		ImGuiHelpers.ScaledDummy(0.5f);
		bool hasChanges = _ownPermissions != Pair.UserPair.OwnPermissions;
		using (ImRaii.Disabled(!hasChanges))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save"))
			{
				_apiController.SetBulkPermissions(new BulkPermissionsDto(new Dictionary<string, UserPermissions>(StringComparer.Ordinal) { 
				{
					Pair.UserData.UID,
					_ownPermissions
				} }, new Dictionary<string, GroupUserPreferredPermissions>(StringComparer.Ordinal)));
			}
		}
		UiSharedService.AttachToolTip("Save and apply all changes");
		float rightSideButtons = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Undo, "Revert") + _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.ArrowsSpin, "Reset to Default");
		ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X - rightSideButtons);
		using (ImRaii.Disabled(!hasChanges))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "Revert"))
			{
				_ownPermissions = Pair.UserPair.OwnPermissions.DeepClone();
			}
		}
		UiSharedService.AttachToolTip("Revert all changes");
		ImGui.SameLine();
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowsSpin, "Reset to Default"))
		{
			DefaultPermissionsDto defaultPermissions = _apiController.DefaultPermissions;
			_ownPermissions.SetSticky(Pair.IsDirectlyPaired || defaultPermissions.IndividualIsSticky);
			_ownPermissions.SetPaused(paused: false);
			_ownPermissions.SetDisableVFX(Pair.IsDirectlyPaired ? defaultPermissions.DisableIndividualVFX : defaultPermissions.DisableGroupVFX);
			_ownPermissions.SetDisableSounds(Pair.IsDirectlyPaired ? defaultPermissions.DisableIndividualSounds : defaultPermissions.DisableGroupSounds);
			_ownPermissions.SetDisableAnimations(Pair.IsDirectlyPaired ? defaultPermissions.DisableIndividualAnimations : defaultPermissions.DisableGroupAnimations);
			_apiController.SetBulkPermissions(new BulkPermissionsDto(new Dictionary<string, UserPermissions>(StringComparer.Ordinal) { 
			{
				Pair.UserData.UID,
				_ownPermissions
			} }, new Dictionary<string, GroupUserPreferredPermissions>(StringComparer.Ordinal)));
		}
		UiSharedService.AttachToolTip("This will set all permissions to your defined default permissions in the Mare Settings");
		float ySize = ImGui.GetCursorPosY() + style.FramePadding.Y * ImGuiHelpers.GlobalScale + style.FrameBorderSize;
		ImGui.SetWindowSize(new Vector2(400f, ySize));
	}

	public override void OnClose()
	{
		base.Mediator.Publish(new RemoveWindowMessage(this));
	}
}
