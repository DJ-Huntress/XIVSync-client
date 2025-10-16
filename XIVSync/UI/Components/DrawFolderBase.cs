using System;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using XIVSync.PlayerData.Pairs;
using XIVSync.UI.Handlers;
using XIVSync.UI.Theming;

namespace XIVSync.UI.Components;

public abstract class DrawFolderBase : IDrawFolder
{
	protected readonly string _id;

	protected readonly IImmutableList<Pair> _allPairs;

	protected readonly TagHandler _tagHandler;

	protected readonly UiSharedService _uiSharedService;

	private float _menuWidth = -1f;

	private bool _wasHovered;

	private float _folderHoverTransition;

	private float _folderPulsePhase;

	public IImmutableList<DrawUserPair> DrawPairs { get; init; }

	public int OnlinePairs => DrawPairs.Count((DrawUserPair u) => u.Pair.IsOnline);

	public int TotalPairs => _allPairs.Count;

	protected abstract bool RenderIfEmpty { get; }

	protected abstract bool RenderMenu { get; }

	protected DrawFolderBase(string id, IImmutableList<DrawUserPair> drawPairs, IImmutableList<Pair> allPairs, TagHandler tagHandler, UiSharedService uiSharedService)
	{
		_id = id;
		DrawPairs = drawPairs;
		_allPairs = allPairs;
		_tagHandler = tagHandler;
		_uiSharedService = uiSharedService;
	}

	public void Draw()
	{
		if (!RenderIfEmpty && !DrawPairs.Any())
		{
			return;
		}
		using (ImRaii.PushId("folder_" + _id))
		{
			float deltaTime = ImGui.GetIO().DeltaTime;
			_folderPulsePhase += deltaTime * 2f;
			float windowWidth = UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
			float folderHeight = ImGui.GetFrameHeight() + 6f;
			DrawCyberpunkFolderHeader(folderHeight, windowWidth);
			using (ImRaii.Child("folder__" + _id, new Vector2(windowWidth, folderHeight), border: false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground))
			{
				ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12f);
				ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3f);
				FontAwesomeIcon icon = (_tagHandler.IsTagOpen(_id) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight);
				ImGui.AlignTextToFramePadding();
				_uiSharedService.IconText(icon);
				if (ImGui.IsItemClicked())
				{
					_tagHandler.SetTagOpen(_id, !_tagHandler.IsTagOpen(_id));
				}
				ImGui.SameLine();
				float leftSideEnd = DrawIcon();
				ImGui.SameLine();
				float rightSideEnd = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
				ImGui.SameLine(leftSideEnd);
				DrawName(rightSideEnd - leftSideEnd - ImGui.GetStyle().ItemSpacing.X);
			}
			bool isHovered = (_wasHovered = ImGui.IsItemHovered());
			if (RenderMenu && isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
			{
				ImGui.OpenPopup("Folder Context Menu");
			}
			using (ImRaii.IEndObject popup = ImRaii.Popup("Folder Context Menu"))
			{
				if (popup)
				{
					DrawMenu(_menuWidth);
				}
			}
			float targetHover = (isHovered ? 1f : 0f);
			_folderHoverTransition += (targetHover - _folderHoverTransition) * deltaTime * 8f;
			_folderHoverTransition = Math.Clamp(_folderHoverTransition, 0f, 1f);
			if (!_tagHandler.IsTagOpen(_id))
			{
				return;
			}
			using (ImRaii.PushIndent(_uiSharedService.GetIconSize(FontAwesomeIcon.EllipsisV).X + ImGui.GetStyle().ItemSpacing.X, scaled: false))
			{
				if (DrawPairs.Any())
				{
					Vector4 folderColor = GetFolderColor();
					{
						foreach (DrawUserPair drawPair in DrawPairs)
						{
							drawPair.DrawPairedClient(folderColor);
						}
						return;
					}
				}
				ImGui.TextUnformatted("No users (online)");
			}
		}
	}

	private void DrawCyberpunkFolderHeader(float height, float width)
	{
		ImDrawListPtr drawList = ImGui.GetWindowDrawList();
		Vector2 cursorPos = ImGui.GetCursorScreenPos();
		Vector2 headerMin = cursorPos;
		Vector2 headerMax = new Vector2(cursorPos.X + width, cursorPos.Y + height);
		Vector4 folderColor = GetFolderColor();
		float pulseIntensity = 0.5f + MathF.Sin(_folderPulsePhase) * 0.15f;
		if (_folderHoverTransition > 0.01f)
		{
			Vector4 gradientStart = new Vector4(folderColor.X * 0.15f * pulseIntensity, folderColor.Y * 0.15f * pulseIntensity, folderColor.Z * 0.15f * pulseIntensity, 0.15f * _folderHoverTransition);
			Vector4 gradientEnd = new Vector4(folderColor.X * 0.05f, folderColor.Y * 0.05f, folderColor.Z * 0.05f, 0.08f * _folderHoverTransition);
			drawList.AddRectFilledMultiColor(headerMin, headerMax, ImGui.ColorConvertFloat4ToU32(gradientStart), ImGui.ColorConvertFloat4ToU32(gradientEnd), ImGui.ColorConvertFloat4ToU32(gradientEnd), ImGui.ColorConvertFloat4ToU32(gradientStart));
		}
		float borderAlpha = 0.4f + _folderHoverTransition * 0.4f * pulseIntensity;
		Vector4 borderColor = new Vector4(folderColor.X, folderColor.Y, folderColor.Z, borderAlpha);
		if (_folderHoverTransition > 0.01f)
		{
			drawList.AddRect(col: ImGui.ColorConvertFloat4ToU32(new Vector4(folderColor.X, folderColor.Y, folderColor.Z, 0.15f * _folderHoverTransition)), pMin: new Vector2(headerMin.X - 1f, headerMin.Y - 1f), pMax: new Vector2(headerMax.X + 1f, headerMax.Y + 1f), rounding: 2f, flags: ImDrawFlags.None, thickness: 2f);
		}
		drawList.AddRect(headerMin, headerMax, ImGui.ColorConvertFloat4ToU32(borderColor), 2f, ImDrawFlags.None, 1.2f);
		drawList.AddRectFilled(col: ImGui.ColorConvertFloat4ToU32(new Vector4(folderColor.X * pulseIntensity, folderColor.Y * pulseIntensity, folderColor.Z * pulseIntensity, 0.9f)), pMin: new Vector2(headerMin.X + 2f, headerMin.Y + 2f), pMax: new Vector2(headerMin.X + 5f, headerMax.Y - 2f));
	}

	private Vector4 GetFolderColor()
	{
		ThemePalette theme = _uiSharedService.GetCurrentTheme();
		if (theme == null)
		{
			return _id switch
			{
				"Mare_Visible" => new Vector4(0f, 1f, 0.5f, 1f), 
				"Mare_Online" => new Vector4(0f, 0.9f, 1f, 1f), 
				"Mare_Offline" => new Vector4(0.9f, 0.2f, 0.2f, 1f), 
				"Mare_OfflineSyncshell" => new Vector4(0.9f, 0.2f, 0.2f, 1f), 
				"Mare_Unpaired" => new Vector4(1f, 0.5f, 0f, 1f), 
				"Mare_All" => new Vector4(0.6f, 0.4f, 1f, 1f), 
				_ => new Vector4(1f, 0f, 0.8f, 1f), 
			};
		}
		return _id switch
		{
			"Mare_Visible" => theme.FolderVisible, 
			"Mare_Online" => theme.FolderOnline, 
			"Mare_Offline" => theme.FolderOffline, 
			"Mare_OfflineSyncshell" => theme.FolderOfflineSyncshell, 
			"Mare_Unpaired" => theme.FolderUnpaired, 
			"Mare_All" => theme.FolderAll, 
			_ => theme.FolderCustom, 
		};
	}

	protected abstract float DrawIcon();

	protected abstract void DrawMenu(float menuWidth);

	protected abstract void DrawName(float width);
}
