using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using XIVSync.API.Data.Enum;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto.User;
using XIVSync.PlayerData.Pairs;
using XIVSync.UI.Handlers;
using XIVSync.WebAPI;

namespace XIVSync.UI.Components;

public class DrawFolderTag : DrawFolderBase
{
	private readonly ApiController _apiController;

	private readonly SelectPairForTagUi _selectPairForTagUi;

	protected override bool RenderIfEmpty => _id switch
	{
		"Mare_Unpaired" => false, 
		"Mare_Online" => false, 
		"Mare_Offline" => false, 
		"Mare_Visible" => false, 
		"Mare_All" => true, 
		"Mare_OfflineSyncshell" => false, 
		_ => true, 
	};

	protected override bool RenderMenu => _id switch
	{
		"Mare_Unpaired" => false, 
		"Mare_Online" => false, 
		"Mare_Offline" => false, 
		"Mare_Visible" => false, 
		"Mare_All" => false, 
		"Mare_OfflineSyncshell" => false, 
		_ => true, 
	};

	private bool RenderPause
	{
		get
		{
			if (_id switch
			{
				"Mare_Unpaired" => false, 
				"Mare_Online" => false, 
				"Mare_Offline" => false, 
				"Mare_Visible" => false, 
				"Mare_All" => false, 
				"Mare_OfflineSyncshell" => false, 
				_ => true, 
			})
			{
				return _allPairs.Any();
			}
			return false;
		}
	}

	private bool RenderCount => _id switch
	{
		"Mare_Unpaired" => false, 
		"Mare_Online" => false, 
		"Mare_Offline" => false, 
		"Mare_Visible" => false, 
		"Mare_All" => false, 
		"Mare_OfflineSyncshell" => false, 
		_ => true, 
	};

	public DrawFolderTag(string id, IImmutableList<DrawUserPair> drawPairs, IImmutableList<Pair> allPairs, TagHandler tagHandler, ApiController apiController, SelectPairForTagUi selectPairForTagUi, UiSharedService uiSharedService)
		: base(id, drawPairs, allPairs, tagHandler, uiSharedService)
	{
		_apiController = apiController;
		_selectPairForTagUi = selectPairForTagUi;
	}

	protected override float DrawIcon()
	{
		FontAwesomeIcon icon = _id switch
		{
			"Mare_Unpaired" => FontAwesomeIcon.ArrowsLeftRight, 
			"Mare_Online" => FontAwesomeIcon.Link, 
			"Mare_Offline" => FontAwesomeIcon.Unlink, 
			"Mare_OfflineSyncshell" => FontAwesomeIcon.Unlink, 
			"Mare_Visible" => FontAwesomeIcon.Eye, 
			"Mare_All" => FontAwesomeIcon.User, 
			_ => FontAwesomeIcon.Folder, 
		};
		ImGui.AlignTextToFramePadding();
		_uiSharedService.IconText(icon);
		if (RenderCount)
		{
			Vector2 itemSpacing = ImGui.GetStyle().ItemSpacing;
			itemSpacing.X = ImGui.GetStyle().ItemSpacing.X / 2f;
			using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, itemSpacing))
			{
				ImGui.SameLine();
				ImGui.AlignTextToFramePadding();
				ImGui.TextUnformatted("[" + base.OnlinePairs + "]");
			}
			UiSharedService.AttachToolTip(base.OnlinePairs + " online" + Environment.NewLine + base.TotalPairs + " total");
		}
		ImGui.SameLine();
		return ImGui.GetCursorPosX();
	}

	protected override void DrawMenu(float menuWidth)
	{
		ImGui.TextUnformatted("Group Menu");
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Users, "Select Pairs", menuWidth, isInPopup: true))
		{
			_selectPairForTagUi.Open(_id);
		}
		UiSharedService.AttachToolTip("Select Individual Pairs for this Pair Group");
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Pair Group", menuWidth, isInPopup: true) && UiSharedService.CtrlPressed())
		{
			_tagHandler.RemoveTag(_id);
		}
		UiSharedService.AttachToolTip("Hold CTRL to remove this Group permanently." + Environment.NewLine + "Note: this will not unpair with users in this Group.");
	}

	protected override void DrawName(float width)
	{
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted(_id switch
		{
			"Mare_Unpaired" => "One-sided Individual Pairs", 
			"Mare_Online" => "Online / Paused by you", 
			"Mare_Offline" => "Offline / Paused by other", 
			"Mare_OfflineSyncshell" => "Offline Syncshell Users", 
			"Mare_Visible" => "Visible", 
			"Mare_All" => "Users", 
			_ => _id, 
		});
	}

	protected override float DrawRightSide(float currentRightSideX)
	{
		if (!RenderPause)
		{
			return currentRightSideX;
		}
		bool allArePaused = _allPairs.All((Pair pair) => pair.UserPair.OwnPermissions.IsPaused());
		FontAwesomeIcon pauseButton = (allArePaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause);
		float pauseButtonX = _uiSharedService.GetIconButtonSize(pauseButton).X;
		ImGui.SameLine(currentRightSideX - pauseButtonX);
		if (_uiSharedService.IconButton(pauseButton))
		{
			if (allArePaused)
			{
				ResumeAllPairs(_allPairs);
			}
			else
			{
				PauseRemainingPairs(_allPairs);
			}
		}
		if (allArePaused)
		{
			UiSharedService.AttachToolTip("Resume pairing with all pairs in " + _id);
		}
		else
		{
			UiSharedService.AttachToolTip("Pause pairing with all pairs in " + _id);
		}
		return currentRightSideX;
	}

	private void PauseRemainingPairs(IEnumerable<Pair> availablePairs)
	{
		_apiController.SetBulkPermissions(new BulkPermissionsDto(availablePairs.ToDictionary<Pair, string, UserPermissions>((Pair g) => g.UserData.UID, delegate(Pair g)
		{
			UserPermissions perm = g.UserPair.OwnPermissions;
			perm.SetPaused(paused: true);
			return perm;
		}, StringComparer.Ordinal), new Dictionary<string, GroupUserPreferredPermissions>(StringComparer.Ordinal))).ConfigureAwait(continueOnCapturedContext: false);
	}

	private void ResumeAllPairs(IEnumerable<Pair> availablePairs)
	{
		_apiController.SetBulkPermissions(new BulkPermissionsDto(availablePairs.ToDictionary<Pair, string, UserPermissions>((Pair g) => g.UserData.UID, delegate(Pair g)
		{
			UserPermissions perm = g.UserPair.OwnPermissions;
			perm.SetPaused(paused: false);
			return perm;
		}, StringComparer.Ordinal), new Dictionary<string, GroupUserPreferredPermissions>(StringComparer.Ordinal))).ConfigureAwait(continueOnCapturedContext: false);
	}
}
