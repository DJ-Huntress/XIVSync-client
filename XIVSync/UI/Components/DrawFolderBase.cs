using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using XIVSync.PlayerData.Pairs;
using XIVSync.UI.Handlers;

namespace XIVSync.UI.Components;

public abstract class DrawFolderBase : IDrawFolder
{
	protected readonly string _id;

	protected readonly IImmutableList<Pair> _allPairs;

	protected readonly TagHandler _tagHandler;

	protected readonly UiSharedService _uiSharedService;

	private float _menuWidth = -1f;

	private bool _wasHovered;

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
			using (ImRaii.Child("folder__" + _id, new Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight())))
			{
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
				float rightSideStart = DrawRightSideInternal();
				ImGui.SameLine(leftSideEnd);
				DrawName(rightSideStart - leftSideEnd);
			}
			_wasHovered = ImGui.IsItemHovered();
			ImGui.Separator();
			if (!_tagHandler.IsTagOpen(_id))
			{
				return;
			}
			using (ImRaii.PushIndent(_uiSharedService.GetIconSize(FontAwesomeIcon.EllipsisV).X + ImGui.GetStyle().ItemSpacing.X, scaled: false))
			{
				if (DrawPairs.Any())
				{
					foreach (DrawUserPair drawPair in DrawPairs)
					{
						drawPair.DrawPairedClient();
					}
				}
				else
				{
					ImGui.TextUnformatted("No users (online)");
				}
				ImGui.Separator();
			}
		}
	}

	protected abstract float DrawIcon();

	protected abstract void DrawMenu(float menuWidth);

	protected abstract void DrawName(float width);

	protected abstract float DrawRightSide(float currentRightSideX);

	private float DrawRightSideInternal()
	{
		Vector2 barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.EllipsisV);
		float spacingX = ImGui.GetStyle().ItemSpacing.X;
		float windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
		float rightSideStart = windowEndX - (RenderMenu ? (barButtonSize.X + spacingX) : spacingX);
		if (RenderMenu)
		{
			ImGui.SameLine(windowEndX - barButtonSize.X);
			if (_uiSharedService.IconButton(FontAwesomeIcon.EllipsisV))
			{
				ImGui.OpenPopup("User Flyout Menu");
			}
			if (ImGui.BeginPopup("User Flyout Menu"))
			{
				ImU8String id = new ImU8String(8, 1);
				id.AppendLiteral("buttons-");
				id.AppendFormatted(_id);
				using (ImRaii.PushId(id))
				{
					DrawMenu(_menuWidth);
				}
				_menuWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
				ImGui.EndPopup();
			}
			else
			{
				_menuWidth = 0f;
			}
		}
		return DrawRightSide(rightSideStart);
	}
}
