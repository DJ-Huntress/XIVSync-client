using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using XIVSync.PlayerData.Pairs;
using XIVSync.UI.Handlers;

namespace XIVSync.UI.Components;

public class SelectTagForPairUi
{
	private readonly TagHandler _tagHandler;

	private readonly IdDisplayHandler _uidDisplayHandler;

	private readonly UiSharedService _uiSharedService;

	private Pair? _pair;

	private bool _show;

	private string _tagNameToAdd = "";

	public SelectTagForPairUi(TagHandler tagHandler, IdDisplayHandler uidDisplayHandler, UiSharedService uiSharedService)
	{
		_show = false;
		_pair = null;
		_tagHandler = tagHandler;
		_uidDisplayHandler = uidDisplayHandler;
		_uiSharedService = uiSharedService;
	}

	public void Draw()
	{
		if (_pair == null)
		{
			return;
		}
		string name = PairName(_pair);
		string popupName = "Choose Groups for " + name;
		if (_show)
		{
			ImGui.OpenPopup(popupName);
			_show = false;
		}
		if (!ImGui.BeginPopup(popupName))
		{
			return;
		}
		List<string> tags = _tagHandler.GetAllTagsSorted();
		int childHeight = ((tags.Count == 0) ? 1 : (tags.Count * 25));
		Vector2 childSize = new Vector2(0f, (childHeight > 100) ? 100 : childHeight) * ImGuiHelpers.GlobalScale;
		ImU8String text = new ImU8String(37, 1);
		text.AppendLiteral("Select the groups you want ");
		text.AppendFormatted(name);
		text.AppendLiteral(" to be in.");
		ImGui.TextUnformatted(text);
		if (ImGui.BeginChild(name + "##listGroups", childSize))
		{
			foreach (string tag in tags)
			{
				text = new ImU8String(13, 2);
				text.AppendLiteral("groups-pair-");
				text.AppendFormatted(_pair.UserData.UID);
				text.AppendLiteral("-");
				text.AppendFormatted(tag);
				using (ImRaii.PushId(text))
				{
					DrawGroupName(_pair, tag);
				}
			}
			ImGui.EndChild();
		}
		ImGui.Separator();
		text = new ImU8String(24, 1);
		text.AppendLiteral("Create a new group for ");
		text.AppendFormatted(name);
		text.AppendLiteral(".");
		ImGui.TextUnformatted(text);
		if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
		{
			HandleAddTag();
		}
		ImGui.SameLine();
		ImGui.InputTextWithHint("##category_name", "New Group", ref _tagNameToAdd, 40);
		if (ImGui.IsKeyDown(ImGuiKey.Enter))
		{
			HandleAddTag();
		}
		ImGui.EndPopup();
	}

	public void Open(Pair pair)
	{
		_pair = pair;
		_show = true;
	}

	private void DrawGroupName(Pair pair, string name)
	{
		bool hasTag = _tagHandler.HasTag(pair.UserData.UID, name);
		if (ImGui.Checkbox(name, ref hasTag))
		{
			if (hasTag)
			{
				_tagHandler.AddTagToPairedUid(pair.UserData.UID, name);
			}
			else
			{
				_tagHandler.RemoveTagFromPairedUid(pair.UserData.UID, name);
			}
		}
	}

	private void HandleAddTag()
	{
		bool flag = !_tagNameToAdd.IsNullOrWhitespace();
		if (flag)
		{
			bool flag2;
			switch (_tagNameToAdd)
			{
			case "Mare_Offline":
			case "Mare_Online":
			case "Mare_Visible":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = !flag2;
		}
		if (flag)
		{
			_tagHandler.AddTag(_tagNameToAdd);
			if (_pair != null)
			{
				_tagHandler.AddTagToPairedUid(_pair.UserData.UID, _tagNameToAdd);
			}
			_tagNameToAdd = string.Empty;
		}
		else
		{
			_tagNameToAdd = string.Empty;
		}
	}

	private string PairName(Pair pair)
	{
		return _uidDisplayHandler.GetPlayerText(pair).text;
	}
}
