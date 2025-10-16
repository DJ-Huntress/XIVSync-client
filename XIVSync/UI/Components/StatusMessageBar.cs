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
using XIVSync.UI.Theming;
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

	private float _pulsePhase;

	private float _hoverTransition;

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
		float deltaTime = ImGui.GetIO().DeltaTime;
		_pulsePhase += deltaTime * 2f;
		UpdateCurrentStatus();
		float windowWidth = ImGui.GetWindowWidth();
		float padding = 8f;
		float availableWidth = windowWidth - padding * 2f;
		float barHeight = ImGui.GetFrameHeight() + 12f;
		DrawCyberpunkStatusBar(theme, availableWidth, barHeight);
		using ImRaii.IEndObject child = ImRaii.Child("StatusMessageBar", new Vector2(availableWidth, barHeight), border: false, ImGuiWindowFlags.NoScrollbar);
		if (!child)
		{
			return;
		}
		using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8f, 4f)))
		{
			ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12f);
			ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4f);
			ImGui.AlignTextToFramePadding();
			Vector4 iconColor = theme.Accent;
			using ImRaii.Color iconCol = ImRaii.PushColor(ImGuiCol.Text, iconColor);
			_uiSharedService.IconText(FontAwesomeIcon.Comment);
			iconCol.Dispose();
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

	private void DrawCyberpunkStatusBar(ThemePalette theme, float width, float height)
	{
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		Vector2 cursorPos = ImGui.GetCursorScreenPos();
		Vector2 barMin = cursorPos;
		Vector2 barMax = new Vector2(cursorPos.X + width, cursorPos.Y + height);
		float pulseIntensity = 0.5f + MathF.Sin(_pulsePhase) * 0.15f;
		Vector4 bgGradient1 = new Vector4(theme.PanelBg.X + 0.02f * pulseIntensity, theme.PanelBg.Y + 0.05f * pulseIntensity, theme.PanelBg.Z + 0.1f * pulseIntensity, 0.3f);
		Vector4 bgGradient2 = new Vector4(theme.PanelBg.X, theme.PanelBg.Y, theme.PanelBg.Z, 0.2f);
		drawList.AddRectFilledMultiColor(barMin, barMax, ImGui.ColorConvertFloat4ToU32(bgGradient1), ImGui.ColorConvertFloat4ToU32(bgGradient2), ImGui.ColorConvertFloat4ToU32(bgGradient2), ImGui.ColorConvertFloat4ToU32(bgGradient1));
		drawList.AddRect(col: ImGui.ColorConvertFloat4ToU32(new Vector4(theme.Accent.X, theme.Accent.Y, theme.Accent.Z, 0.4f + pulseIntensity * 0.3f)), pMin: barMin, pMax: barMax, rounding: 2f, flags: ImDrawFlags.None, thickness: 1.5f);
		drawList.AddRectFilled(col: ImGui.ColorConvertFloat4ToU32(new Vector4(theme.Accent.X, theme.Accent.Y, theme.Accent.Z, 0.8f * pulseIntensity)), pMin: new Vector2(barMin.X + 2f, barMin.Y + 2f), pMax: new Vector2(barMin.X + 4f, barMax.Y - 2f));
	}

	private void DrawDisplayMode(ThemePalette theme, float availableWidth)
	{
		string statusText = (string.IsNullOrEmpty(_currentStatusMessage) ? "Click to set your status message..." : _currentStatusMessage);
		bool num = string.IsNullOrEmpty(_currentStatusMessage);
		float iconWidth = _uiSharedService.GetIconSize(FontAwesomeIcon.Comment).X;
		float textWidth = availableWidth - iconWidth - ImGui.GetStyle().ItemSpacing.X - 24f;
		Vector2 cursorPos = ImGui.GetCursorPos();
		ImGui.InvisibleButton("StatusClickArea", new Vector2(textWidth, ImGui.GetFrameHeight()));
		bool isHovered = ImGui.IsItemHovered();
		float deltaTime = ImGui.GetIO().DeltaTime;
		float targetHover = (isHovered ? 1f : 0f);
		_hoverTransition += (targetHover - _hoverTransition) * deltaTime * 8f;
		_hoverTransition = Math.Clamp(_hoverTransition, 0f, 1f);
		if (ImGui.IsItemClicked())
		{
			StartEditing();
		}
		ImGui.SetCursorPos(new Vector2(cursorPos.X, cursorPos.Y + 5f));
		Vector4 textColor = ((!num) ? new Vector4(theme.TextPrimary.X + (theme.Accent.X - theme.TextPrimary.X) * _hoverTransition, theme.TextPrimary.Y + (theme.Accent.Y - theme.TextPrimary.Y) * _hoverTransition, theme.TextPrimary.Z + (theme.Accent.Z - theme.TextPrimary.Z) * _hoverTransition, theme.TextPrimary.W) : new Vector4(theme.TextSecondary.X + (theme.Accent.X - theme.TextSecondary.X) * _hoverTransition * 0.3f, theme.TextSecondary.Y + (theme.Accent.Y - theme.TextSecondary.Y) * _hoverTransition * 0.3f, theme.TextSecondary.Z + (theme.Accent.Z - theme.TextSecondary.Z) * _hoverTransition * 0.3f, theme.TextSecondary.W));
		using (ImRaii.PushColor(ImGuiCol.Text, textColor))
		{
			ImGui.TextUnformatted(statusText);
			if (isHovered)
			{
				ImGui.SetTooltip("Click to edit your status message");
			}
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
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to update status message");
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
