using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using XIVSync.UI.Handlers;

namespace XIVSync.UI.Components;

public class DrawGroupedGroupFolder : IDrawFolder
{
	private readonly IEnumerable<IDrawFolder> _groups;

	private readonly TagHandler _tagHandler;

	private readonly UiSharedService _uiSharedService;

	private bool _wasHovered;

	public IImmutableList<DrawUserPair> DrawPairs
	{
		get
		{
			throw new NotSupportedException();
		}
	}

	public int OnlinePairs => (from g in _groups.SelectMany((IDrawFolder g) => g.DrawPairs)
		where g.Pair.IsOnline
		select g).DistinctBy((DrawUserPair g) => g.Pair.UserData.UID).Count();

	public int TotalPairs => _groups.Sum((IDrawFolder g) => g.TotalPairs);

	public DrawGroupedGroupFolder(IEnumerable<IDrawFolder> groups, TagHandler tagHandler, UiSharedService uiSharedService)
	{
		_groups = groups;
		_tagHandler = tagHandler;
		_uiSharedService = uiSharedService;
	}

	public void Draw()
	{
		if (!_groups.Any())
		{
			return;
		}
		string _id = "__folder_syncshells";
		using (ImRaii.PushId(_id))
		{
			using (ImRaii.Child("folder__" + _id, new Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight())))
			{
				ImGui.Dummy(new Vector2(0f, ImGui.GetFrameHeight()));
				using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 0f)))
				{
					ImGui.SameLine();
				}
				FontAwesomeIcon icon = (_tagHandler.IsTagOpen(_id) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight);
				ImGui.AlignTextToFramePadding();
				_uiSharedService.IconText(icon);
				if (ImGui.IsItemClicked())
				{
					_tagHandler.SetTagOpen(_id, !_tagHandler.IsTagOpen(_id));
				}
				ImGui.SameLine();
				ImGui.AlignTextToFramePadding();
				_uiSharedService.IconText(FontAwesomeIcon.UsersRectangle);
				Vector2 itemSpacing = ImGui.GetStyle().ItemSpacing;
				itemSpacing.X = ImGui.GetStyle().ItemSpacing.X / 2f;
				using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, itemSpacing))
				{
					ImGui.SameLine();
					ImGui.AlignTextToFramePadding();
					ImGui.TextUnformatted("[" + OnlinePairs + "]");
				}
				UiSharedService.AttachToolTip(OnlinePairs + " online in all of your joined syncshells" + Environment.NewLine + TotalPairs + " pairs combined in all of your joined syncshells");
				ImGui.SameLine();
				ImGui.AlignTextToFramePadding();
				ImGui.TextUnformatted("All Syncshells");
			}
			_wasHovered = ImGui.IsItemHovered();
			ImGui.Separator();
			if (!_tagHandler.IsTagOpen(_id))
			{
				return;
			}
			using (ImRaii.PushIndent(20f))
			{
				foreach (IDrawFolder group in _groups)
				{
					group.Draw();
				}
			}
		}
	}
}
