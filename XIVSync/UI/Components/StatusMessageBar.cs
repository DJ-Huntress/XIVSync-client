using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Dto.User;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.WebAPI;

namespace XIVSync.UI.Components;

public class StatusMessageBar : IMediatorSubscriber
{
	private readonly ApiController _apiController;

	private readonly ILogger _logger;

	private readonly MareMediator _mediator;

	private readonly MareProfileManager _profileManager;

	private readonly UiSharedService _uiSharedService;

	private bool _isEditing;

	private string _editingText = string.Empty;

	private string _currentStatusMessage = string.Empty;

	private bool _isSaving;

	public MareMediator Mediator => _mediator;

	public StatusMessageBar(ApiController apiController, ILogger logger, MareMediator mediator, MareProfileManager profileManager, UiSharedService uiSharedService)
	{
		_apiController = apiController;
		_logger = logger;
		_mediator = mediator;
		_profileManager = profileManager;
		_uiSharedService = uiSharedService;
		_mediator.Subscribe<ClearProfileDataMessage>(this, OnProfileUpdated);
	}

	public void Draw(ThemePalette theme)
	{
		if (string.IsNullOrEmpty(_apiController.UID))
		{
			_logger.LogDebug("Status bar not drawn - no UID available");
			return;
		}
		UpdateCurrentStatus();
		float windowWidth = ImGui.GetWindowWidth();
		float padding = 8f;
		float availableWidth = windowWidth - padding * 2f;
		using ImRaii.IEndObject child = ImRaii.Child("StatusMessageBar", new Vector2(availableWidth, ImGui.GetFrameHeight() + 8f), border: false, ImGuiWindowFlags.NoScrollbar);
		if (!child)
		{
			return;
		}
		using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8f, 4f)))
		{
			using (ImRaii.PushColor(ImGuiCol.ChildBg, theme.PanelBg))
			{
				using (ImRaii.PushColor(ImGuiCol.Border, theme.PanelBorder))
				{
					ImGui.AlignTextToFramePadding();
					_uiSharedService.IconText(FontAwesomeIcon.Comment);
					ImGui.SameLine();
					if (_isEditing)
					{
						DrawEditingMode(theme, availableWidth);
					}
					else
					{
						DrawDisplayMode(theme, availableWidth);
					}
				}
			}
		}
	}

	private void DrawDisplayMode(ThemePalette theme, float availableWidth)
	{
		string statusText = (string.IsNullOrEmpty(_currentStatusMessage) ? "Click to set your status message..." : _currentStatusMessage);
		Vector4 textColor = (string.IsNullOrEmpty(_currentStatusMessage) ? theme.TextSecondary : theme.TextPrimary);
		using (ImRaii.PushColor(ImGuiCol.Text, textColor))
		{
			float iconWidth = _uiSharedService.GetIconSize(FontAwesomeIcon.Comment).X;
			float textWidth = availableWidth - iconWidth - ImGui.GetStyle().ItemSpacing.X - 16f;
			ImGui.CalcTextSize(statusText);
			Vector2 cursorPos = ImGui.GetCursorPos();
			ImGui.InvisibleButton("StatusClickArea", new Vector2(textWidth, ImGui.GetFrameHeight()));
			if (ImGui.IsItemClicked())
			{
				StartEditing();
			}
			if (ImGui.IsItemHovered())
			{
				using (ImRaii.PushColor(ImGuiCol.Text, theme.Accent))
				{
					ImGui.SetCursorPos(cursorPos);
					ImGui.TextUnformatted(statusText);
					ImGui.SetTooltip("Click to edit your status message");
					return;
				}
			}
			ImGui.SetCursorPos(cursorPos);
			ImGui.TextUnformatted(statusText);
		}
	}

	private void DrawEditingMode(ThemePalette theme, float availableWidth)
	{
		float saveButtonWidth = ImGui.CalcTextSize(_isSaving ? "Saving..." : "Save").X + ImGui.GetStyle().FramePadding.X * 2f + 8f;
		float cancelButtonWidth = ImGui.CalcTextSize("Cancel").X + ImGui.GetStyle().FramePadding.X * 2f + 8f;
		float buttonSpacing = ImGui.GetStyle().ItemSpacing.X;
		ImGui.SetNextItemWidth(availableWidth - saveButtonWidth - cancelButtonWidth - buttonSpacing * 2f - 16f);
		if (ImGui.InputText("##StatusEdit", ref _editingText, 200, ImGuiInputTextFlags.EnterReturnsTrue) && !_isSaving)
		{
			SaveStatusMessage();
		}
		if (ImGui.IsKeyPressed(ImGuiKey.Escape))
		{
			CancelEditing();
		}
		ImGui.SameLine();
		if (_isSaving)
		{
			ImGui.Button("Saving...", new Vector2(saveButtonWidth, 0f));
		}
		else if (ImGui.Button("Save", new Vector2(saveButtonWidth, 0f)))
		{
			SaveStatusMessage();
		}
		ImGui.SameLine();
		if (ImGui.Button("Cancel", new Vector2(cancelButtonWidth, 0f)))
		{
			CancelEditing();
		}
	}

	private void UpdateCurrentStatus()
	{
		if (string.IsNullOrEmpty(_apiController.UID))
		{
			return;
		}
		UserData userData = new UserData(_apiController.UID);
		string description = _profileManager.GetMareProfile(userData).Description;
		if (description == "Loading Data from server..." || description == "-- User has no description set --")
		{
			_currentStatusMessage = string.Empty;
			return;
		}
		string[] lines = description.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		if (lines.Length != 0)
		{
			string firstLine = lines[0].Trim();
			if (firstLine.StartsWith("STATUS:"))
			{
				_currentStatusMessage = firstLine.Substring(7).Trim();
			}
			else
			{
				_currentStatusMessage = string.Empty;
			}
		}
		else
		{
			_currentStatusMessage = string.Empty;
		}
	}

	private void StartEditing()
	{
		_isEditing = true;
		_editingText = _currentStatusMessage;
		_isSaving = false;
	}

	private void CancelEditing()
	{
		_isEditing = false;
		_editingText = _currentStatusMessage;
		_isSaving = false;
	}

	private async Task SaveStatusMessage()
	{
		if (_isSaving)
		{
			return;
		}
		_isSaving = true;
		try
		{
			if (string.IsNullOrEmpty(_apiController.UID))
			{
				_logger.LogWarning("Cannot save status - no UID available");
				return;
			}
			UserData userData = new UserData(_apiController.UID);
			MareProfileData currentProfile = _profileManager.GetMareProfile(userData);
			string newDescription = BuildNewDescription(_editingText.Trim(), currentProfile.Description);
			UserProfileDto profileDto = new UserProfileDto(userData, Disabled: false, currentProfile.IsNSFW, currentProfile.Base64ProfilePicture, newDescription);
			await _apiController.UserSetProfile(profileDto);
			_currentStatusMessage = _editingText.Trim();
			_isEditing = false;
			_mediator.Publish(new ClearProfileDataMessage(userData));
			_mediator.Publish(new RefreshUiMessage());
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to update status message");
		}
		finally
		{
			_isSaving = false;
		}
	}

	private string BuildNewDescription(string statusMessage, string currentDescription)
	{
		string[] lines = currentDescription.Split(new char[2] { '\r', '\n' }, StringSplitOptions.None);
		List<string> restOfDescription = new List<string>();
		int descriptionStartIndex = 0;
		if (lines.Length != 0 && lines[0].Trim().StartsWith("STATUS:"))
		{
			descriptionStartIndex = 1;
			if (lines.Length > 1 && lines[1].Trim() == "---")
			{
				descriptionStartIndex = 2;
			}
		}
		for (int i = descriptionStartIndex; i < lines.Length; i++)
		{
			restOfDescription.Add(lines[i]);
		}
		List<string> newDescriptionParts = new List<string>();
		if (!string.IsNullOrEmpty(statusMessage))
		{
			newDescriptionParts.Add("STATUS: " + statusMessage);
			if (restOfDescription.Any((string line) => !string.IsNullOrWhiteSpace(line)))
			{
				newDescriptionParts.Add("---");
			}
		}
		newDescriptionParts.AddRange(restOfDescription);
		string result = string.Join("\n", newDescriptionParts).Trim();
		if (!string.IsNullOrEmpty(result))
		{
			return result;
		}
		return "-- User has no description set --";
	}

	private void OnProfileUpdated(ClearProfileDataMessage message)
	{
		if (message.UserData?.UID == _apiController.UID)
		{
			UpdateCurrentStatus();
		}
	}
}
