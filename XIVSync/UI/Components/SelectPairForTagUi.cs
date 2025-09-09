using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using XIVSync.PlayerData.Pairs;
using XIVSync.UI.Handlers;

namespace XIVSync.UI.Components;

public class SelectPairForTagUi
{
	private readonly TagHandler _tagHandler;

	private readonly IdDisplayHandler _uidDisplayHandler;

	private string _filter = string.Empty;

	private bool _opened;

	private HashSet<string> _peopleInGroup = new HashSet<string>(StringComparer.Ordinal);

	private bool _show;

	private string _tag = string.Empty;

	public SelectPairForTagUi(TagHandler tagHandler, IdDisplayHandler uidDisplayHandler)
	{
		_tagHandler = tagHandler;
		_uidDisplayHandler = uidDisplayHandler;
	}

	public void Draw(List<Pair> pairs)
	{
		float workHeight = ImGui.GetMainViewport().WorkSize.Y / ImGuiHelpers.GlobalScale;
		Vector2 minSize = new Vector2(300f, (workHeight < 400f) ? workHeight : 400f) * ImGuiHelpers.GlobalScale;
		Vector2 maxSize = new Vector2(300f, 1000f) * ImGuiHelpers.GlobalScale;
		string popupName = "Choose Users for Group " + _tag;
		if (!_show)
		{
			_opened = false;
		}
		if (_show && !_opened)
		{
			ImGui.SetNextWindowSize(minSize);
			UiSharedService.CenterNextWindow(minSize.X, minSize.Y, ImGuiCond.Always);
			ImGui.OpenPopup(popupName);
			_opened = true;
		}
		ImGui.SetNextWindowSizeConstraints(minSize, maxSize);
		if (ImGui.BeginPopupModal(popupName, ref _show, ImGuiWindowFlags.Popup | ImGuiWindowFlags.Modal))
		{
			ImU8String text = new ImU8String(23, 1);
			text.AppendLiteral("Select users for group ");
			text.AppendFormatted(_tag);
			ImGui.TextUnformatted(text);
			ImGui.InputTextWithHint("##filter", "Filter", ref _filter, 255);
			foreach (Pair item in pairs.Where((Pair p) => string.IsNullOrEmpty(_filter) || PairName(p).Contains(_filter, StringComparison.OrdinalIgnoreCase)).OrderBy<Pair, string>((Pair p) => PairName(p), StringComparer.OrdinalIgnoreCase).ToList())
			{
				bool isInGroup = _peopleInGroup.Contains(item.UserData.UID);
				if (ImGui.Checkbox(PairName(item), ref isInGroup))
				{
					if (isInGroup)
					{
						_tagHandler.AddTagToPairedUid(item.UserData.UID, _tag);
						_peopleInGroup.Add(item.UserData.UID);
					}
					else
					{
						_tagHandler.RemoveTagFromPairedUid(item.UserData.UID, _tag);
						_peopleInGroup.Remove(item.UserData.UID);
					}
				}
			}
			ImGui.EndPopup();
		}
		else
		{
			_filter = string.Empty;
			_show = false;
		}
	}

	public void Open(string tag)
	{
		_peopleInGroup = _tagHandler.GetOtherUidsForTag(tag);
		_tag = tag;
		_show = true;
	}

	private string PairName(Pair pair)
	{
		return _uidDisplayHandler.GetPlayerText(pair).text;
	}
}
