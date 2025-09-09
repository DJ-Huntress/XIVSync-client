using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using XIVSync.API.Dto.Group;
using XIVSync.MareConfiguration;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;

namespace XIVSync.UI.Handlers;

public class IdDisplayHandler
{
	private readonly MareConfigService _mareConfigService;

	private readonly MareMediator _mediator;

	private readonly MareProfileManager _profileManager;

	private readonly ServerConfigurationManager _serverManager;

	private readonly Dictionary<string, bool> _showIdForEntry = new Dictionary<string, bool>(StringComparer.Ordinal);

	private string _editComment = string.Empty;

	private string _editEntry = string.Empty;

	private bool _editIsUid;

	private string _lastMouseOverUid = string.Empty;

	private bool _popupShown;

	private DateTime? _popupTime;

	public IdDisplayHandler(MareMediator mediator, ServerConfigurationManager serverManager, MareConfigService mareConfigService, MareProfileManager profileManager)
	{
		_mediator = mediator;
		_serverManager = serverManager;
		_mareConfigService = mareConfigService;
		_profileManager = profileManager;
	}

	public void DrawGroupText(string id, GroupFullInfoDto group, float textPosX, Func<float> editBoxWidth)
	{
		ImGui.SameLine(textPosX);
		var (textIsUid, playerText) = GetGroupText(group);
		if (!string.Equals(_editEntry, group.GID, StringComparison.Ordinal))
		{
			ImGui.AlignTextToFramePadding();
			using (ImRaii.PushFont(UiBuilder.MonoFont, textIsUid))
			{
				ImGui.TextUnformatted(playerText);
			}
			if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
			{
				bool prevState = textIsUid;
				if (_showIdForEntry.ContainsKey(group.GID))
				{
					prevState = _showIdForEntry[group.GID];
				}
				_showIdForEntry[group.GID] = !prevState;
			}
			if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
			{
				if (_editIsUid)
				{
					_serverManager.SetNoteForUid(_editEntry, _editComment);
				}
				else
				{
					_serverManager.SetNoteForGid(_editEntry, _editComment);
				}
				_editComment = _serverManager.GetNoteForGid(group.GID) ?? string.Empty;
				_editEntry = group.GID;
				_editIsUid = false;
			}
		}
		else
		{
			ImGui.AlignTextToFramePadding();
			ImGui.SetNextItemWidth(editBoxWidth());
			if (ImGui.InputTextWithHint("", "Name/Notes", ref _editComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
			{
				_serverManager.SetNoteForGid(group.GID, _editComment);
				_editEntry = string.Empty;
			}
			if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
			{
				_editEntry = string.Empty;
			}
			UiSharedService.AttachToolTip("Hit ENTER to save\nRight click to cancel");
		}
	}

	public void DrawPairText(string id, Pair pair, float textPosX, Func<float> editBoxWidth)
	{
		ImGui.SameLine(textPosX);
		(bool isUid, string text) playerText = GetPlayerText(pair);
		bool textIsUid = playerText.isUid;
		string playerText2 = playerText.text;
		string statusMessage = GetStatusMessage(pair);
		if (!string.Equals(_editEntry, pair.UserData.UID, StringComparison.Ordinal))
		{
			ImGui.AlignTextToFramePadding();
			using (ImRaii.PushFont(UiBuilder.MonoFont, textIsUid))
			{
				ImGui.TextUnformatted(playerText2);
			}
			if (!string.IsNullOrEmpty(statusMessage) && _mareConfigService.Current.ShowProfileStatusInPairList)
			{
				ImGui.SetCursorPosX(textPosX);
				using (ImRaii.PushColor(ImGuiCol.Text, 4287137928u))
				{
					using (ImRaii.PushFont(UiBuilder.DefaultFont))
					{
						ImGui.TextUnformatted(statusMessage);
					}
				}
			}
			if (ImGui.IsItemHovered())
			{
				if (!string.Equals(_lastMouseOverUid, id))
				{
					_popupTime = DateTime.UtcNow.AddSeconds(_mareConfigService.Current.ProfileDelay);
				}
				_lastMouseOverUid = id;
				if (_popupTime > DateTime.UtcNow || !_mareConfigService.Current.ProfilesShow)
				{
					ImGui.SetTooltip("Left click to switch between UID display and nick" + Environment.NewLine + "Right click to change nick for " + pair.UserData.AliasOrUID + Environment.NewLine + "Middle Mouse Button to open their profile in a separate window");
				}
				else
				{
					DateTime? popupTime = _popupTime;
					DateTime utcNow = DateTime.UtcNow;
					if (popupTime.HasValue && popupTime.GetValueOrDefault() < utcNow && !_popupShown)
					{
						_popupShown = true;
						_mediator.Publish(new ProfilePopoutToggle(pair));
					}
				}
			}
			else if (string.Equals(_lastMouseOverUid, id))
			{
				_mediator.Publish(new ProfilePopoutToggle(null));
				_lastMouseOverUid = string.Empty;
				_popupShown = false;
			}
			if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
			{
				bool prevState = textIsUid;
				if (_showIdForEntry.ContainsKey(pair.UserData.UID))
				{
					prevState = _showIdForEntry[pair.UserData.UID];
				}
				_showIdForEntry[pair.UserData.UID] = !prevState;
			}
			if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
			{
				if (_editIsUid)
				{
					_serverManager.SetNoteForUid(_editEntry, _editComment);
				}
				else
				{
					_serverManager.SetNoteForGid(_editEntry, _editComment);
				}
				_editComment = pair.GetNote() ?? string.Empty;
				_editEntry = pair.UserData.UID;
				_editIsUid = true;
			}
			if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
			{
				_mediator.Publish(new ProfileOpenStandaloneMessage(pair));
			}
		}
		else
		{
			ImGui.AlignTextToFramePadding();
			ImGui.SetNextItemWidth(editBoxWidth());
			if (ImGui.InputTextWithHint("##" + pair.UserData.UID, "Nick/Notes", ref _editComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
			{
				_serverManager.SetNoteForUid(pair.UserData.UID, _editComment);
				_serverManager.SaveNotes();
				_editEntry = string.Empty;
			}
			if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
			{
				_editEntry = string.Empty;
			}
			UiSharedService.AttachToolTip("Hit ENTER to save\nRight click to cancel");
		}
	}

	public (bool isGid, string text) GetGroupText(GroupFullInfoDto group)
	{
		bool textIsGid = true;
		bool num = ShowGidInsteadOfName(group);
		string groupText = _serverManager.GetNoteForGid(group.GID);
		if (!num && groupText != null)
		{
			if (string.IsNullOrEmpty(groupText))
			{
				groupText = group.GroupAliasOrGID;
			}
			else
			{
				textIsGid = false;
			}
		}
		else
		{
			groupText = group.GroupAliasOrGID;
		}
		return (isGid: textIsGid, text: groupText);
	}

	public (bool isUid, string text) GetPlayerText(Pair pair)
	{
		bool textIsUid = true;
		bool showUidInsteadOfName = ShowUidInsteadOfName(pair);
		string playerText = _serverManager.GetNoteForUid(pair.UserData.UID);
		if (!showUidInsteadOfName && playerText != null)
		{
			if (string.IsNullOrEmpty(playerText))
			{
				playerText = pair.UserData.AliasOrUID;
			}
			else
			{
				textIsUid = false;
			}
		}
		else
		{
			playerText = pair.UserData.AliasOrUID;
		}
		if (_mareConfigService.Current.ShowCharacterNameInsteadOfNotesForVisible && pair.IsVisible && !showUidInsteadOfName)
		{
			playerText = pair.PlayerName;
			textIsUid = false;
			if (_mareConfigService.Current.PreferNotesOverNamesForVisible)
			{
				string note = pair.GetNote();
				if (note != null)
				{
					playerText = note;
				}
			}
		}
		return (isUid: textIsUid, text: playerText);
	}

	internal void Clear()
	{
		_editEntry = string.Empty;
		_editComment = string.Empty;
	}

	internal void OpenProfile(Pair entry)
	{
		_mediator.Publish(new ProfileOpenStandaloneMessage(entry));
	}

	private string? GetStatusMessage(Pair pair)
	{
		if (!_mareConfigService.Current.ShowProfileStatusInPairList)
		{
			return null;
		}
		MareProfileData profile = _profileManager.GetMareProfile(pair.UserData);
		if (profile.Description == "Loading Data from server..." || profile.Description == "-- User has no description set --")
		{
			return null;
		}
		string[] lines = profile.Description.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		if (lines.Length == 0)
		{
			return null;
		}
		string firstLine = lines[0].Trim();
		if (string.IsNullOrEmpty(firstLine))
		{
			return null;
		}
		if (firstLine.StartsWith("STATUS:"))
		{
			string statusMessage = firstLine.Substring(7).Trim();
			if (string.IsNullOrEmpty(statusMessage))
			{
				return null;
			}
			int maxLength = _mareConfigService.Current.ProfileStatusMaxLength;
			if (statusMessage.Length > maxLength)
			{
				statusMessage = statusMessage.Substring(0, maxLength - 3) + "...";
			}
			return statusMessage;
		}
		return null;
	}

	private bool ShowGidInsteadOfName(GroupFullInfoDto group)
	{
		_showIdForEntry.TryGetValue(group.GID, out var showidInsteadOfName);
		return showidInsteadOfName;
	}

	private bool ShowUidInsteadOfName(Pair pair)
	{
		_showIdForEntry.TryGetValue(pair.UserData.UID, out var showidInsteadOfName);
		return showidInsteadOfName;
	}
}
