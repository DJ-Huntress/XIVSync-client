using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Enum;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto.Group;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.WebAPI;

namespace XIVSync.UI.Components.Popup;

public class SyncshellAdminUI : WindowMediatorSubscriberBase
{
	private readonly ApiController _apiController;

	private readonly bool _isModerator;

	private readonly bool _isOwner;

	private readonly List<string> _oneTimeInvites = new List<string>();

	private readonly PairManager _pairManager;

	private readonly UiSharedService _uiSharedService;

	private List<BannedGroupUserDto> _bannedUsers = new List<BannedGroupUserDto>();

	private int _multiInvites;

	private string _newPassword;

	private bool _pwChangeSuccess;

	private Task<int>? _pruneTestTask;

	private Task<int>? _pruneTask;

	private int _pruneDays = 14;

	public GroupFullInfoDto GroupFullInfo { get; private set; }

	public SyncshellAdminUI(ILogger<SyncshellAdminUI> logger, MareMediator mediator, ApiController apiController, UiSharedService uiSharedService, PairManager pairManager, GroupFullInfoDto groupFullInfo, PerformanceCollectorService performanceCollectorService)
		: base(logger, mediator, "Syncshell Admin Panel (" + groupFullInfo.GroupAliasOrGID + ")", performanceCollectorService)
	{
		GroupFullInfo = groupFullInfo;
		_apiController = apiController;
		_uiSharedService = uiSharedService;
		_pairManager = pairManager;
		_isOwner = string.Equals(GroupFullInfo.OwnerUID, _apiController.UID, StringComparison.Ordinal);
		_isModerator = GroupFullInfo.GroupUserInfo.IsModerator();
		_newPassword = string.Empty;
		_multiInvites = 30;
		_pwChangeSuccess = true;
		base.IsOpen = true;
		base.SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(700f, 500f),
			MaximumSize = new Vector2(700f, 2000f)
		};
	}

	protected override void DrawInternal()
	{
		if (!_isModerator && !_isOwner)
		{
			return;
		}
		GroupFullInfo = _pairManager.Groups[GroupFullInfo.Group];
		using (ImRaii.PushId("syncshell_admin_" + GroupFullInfo.GID))
		{
			using (_uiSharedService.UidFont.Push())
			{
				ImGui.TextUnformatted(GroupFullInfo.GroupAliasOrGID + " Administrative Panel");
			}
			ImGui.Separator();
			GroupPermissions perm = GroupFullInfo.GroupPermissions;
			using ImRaii.IEndObject tabbar = ImRaii.TabBar("syncshell_tab_" + GroupFullInfo.GID);
			if (!ImRaii.IEndObject.op_True(tabbar))
			{
				return;
			}
			ImRaii.IEndObject inviteTab = ImRaii.TabItem("Invites");
			if (inviteTab)
			{
				bool isInvitesDisabled = perm.IsDisableInvites();
				if (_uiSharedService.IconTextButton(isInvitesDisabled ? FontAwesomeIcon.Unlock : FontAwesomeIcon.Lock, isInvitesDisabled ? "Unlock Syncshell" : "Lock Syncshell"))
				{
					perm.SetDisableInvites(!isInvitesDisabled);
					_apiController.GroupChangeGroupPermissionState(new GroupPermissionDto(GroupFullInfo.Group, perm));
				}
				ImGuiHelpers.ScaledDummy(2f);
				UiSharedService.TextWrapped("One-time invites work as single-use passwords. Use those if you do not want to distribute your Syncshell password.");
				if (_uiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Single one-time invite"))
				{
					ImGui.SetClipboardText(_apiController.GroupCreateTempInvite(new GroupDto(GroupFullInfo.Group), 1).Result.FirstOrDefault() ?? string.Empty);
				}
				UiSharedService.AttachToolTip("Creates a single-use password for joining the syncshell which is valid for 24h and copies it to the clipboard.");
				ImGui.InputInt("##amountofinvites", ref _multiInvites);
				ImGui.SameLine();
				using (ImRaii.Disabled(_multiInvites <= 1 || _multiInvites > 100))
				{
					if (_uiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Generate " + _multiInvites + " one-time invites"))
					{
						_oneTimeInvites.AddRange(_apiController.GroupCreateTempInvite(new GroupDto(GroupFullInfo.Group), _multiInvites).Result);
					}
				}
				if (_oneTimeInvites.Any())
				{
					string invites = string.Join(Environment.NewLine, _oneTimeInvites);
					ImGui.InputTextMultiline("Generated Multi Invites", ref invites, 5000, new Vector2(0f, 0f), ImGuiInputTextFlags.ReadOnly);
					if (_uiSharedService.IconTextButton(FontAwesomeIcon.Copy, "Copy Invites to clipboard"))
					{
						ImGui.SetClipboardText(invites);
					}
				}
			}
			inviteTab.Dispose();
			ImRaii.IEndObject mgmtTab = ImRaii.TabItem("User Management");
			if (mgmtTab)
			{
				ImRaii.IEndObject userNode = ImRaii.TreeNode("User List & Administration");
				if (userNode)
				{
					if (!_pairManager.GroupPairs.TryGetValue(GroupFullInfo, out List<Pair> pairs))
					{
						UiSharedService.ColorTextWrapped("No users found in this Syncshell", ImGuiColors.DalamudYellow);
					}
					else
					{
						using ImRaii.IEndObject table = ImRaii.Table("userList#" + GroupFullInfo.Group.GID, 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY);
						if (table)
						{
							ImGui.TableSetupColumn("Alias/UID/Note", ImGuiTableColumnFlags.None, 3f);
							ImGui.TableSetupColumn("Online/Name", ImGuiTableColumnFlags.None, 2f);
							ImGui.TableSetupColumn("Flags", ImGuiTableColumnFlags.None, 1f);
							ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 2f);
							ImGui.TableHeadersRow();
							GroupPairUserInfo value;
							foreach (KeyValuePair<Pair, GroupPairUserInfo?> pair in new Dictionary<Pair, GroupPairUserInfo?>(pairs.Select((Pair p) => new KeyValuePair<Pair, GroupPairUserInfo?>(p, GroupFullInfo.GroupPairUserInfos.TryGetValue(p.UserData.UID, out value) ? new GroupPairUserInfo?(value) : null))).OrderBy(delegate(KeyValuePair<Pair, GroupPairUserInfo?> p)
							{
								if (!p.Value.HasValue)
								{
									return 10;
								}
								if (p.Value.Value.IsModerator())
								{
									return 0;
								}
								return p.Value.Value.IsPinned() ? 1 : 10;
							}).ThenBy<KeyValuePair<Pair, GroupPairUserInfo?>, string>((KeyValuePair<Pair, GroupPairUserInfo?> p) => p.Key.GetNote() ?? p.Key.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase))
							{
								using (ImRaii.PushId("userTable_" + pair.Key.UserData.UID))
								{
									ImGui.TableNextColumn();
									string note = pair.Key.GetNote();
									string obj = ((note == null) ? pair.Key.UserData.AliasOrUID : (note + " (" + pair.Key.UserData.AliasOrUID + ")"));
									ImGui.AlignTextToFramePadding();
									ImGui.TextUnformatted(obj);
									ImGui.TableNextColumn();
									string onlineText = (pair.Key.IsOnline ? "Online" : "Offline");
									if (!string.IsNullOrEmpty(pair.Key.PlayerName))
									{
										onlineText = onlineText + " (" + pair.Key.PlayerName + ")";
									}
									Vector4 boolcolor = UiSharedService.GetBoolColor(pair.Key.IsOnline);
									ImGui.AlignTextToFramePadding();
									UiSharedService.ColorText(onlineText, boolcolor);
									ImGui.TableNextColumn();
									if (pair.Value.HasValue && (pair.Value.Value.IsModerator() || pair.Value.Value.IsPinned()))
									{
										if (pair.Value.Value.IsModerator())
										{
											_uiSharedService.IconText(FontAwesomeIcon.UserShield);
											UiSharedService.AttachToolTip("Moderator");
										}
										if (pair.Value.Value.IsPinned())
										{
											_uiSharedService.IconText(FontAwesomeIcon.Thumbtack);
											UiSharedService.AttachToolTip("Pinned");
										}
									}
									else
									{
										_uiSharedService.IconText(FontAwesomeIcon.None);
									}
									ImGui.TableNextColumn();
									if (_isOwner)
									{
										if (_uiSharedService.IconButton(FontAwesomeIcon.UserShield))
										{
											GroupPairUserInfo userInfo = pair.Value.GetValueOrDefault();
											userInfo.SetModerator(!userInfo.IsModerator());
											_apiController.GroupSetUserInfo(new GroupPairUserInfoDto(GroupFullInfo.Group, pair.Key.UserData, userInfo));
										}
										UiSharedService.AttachToolTip((pair.Value.HasValue && pair.Value.Value.IsModerator()) ? "Demod user" : "Mod user");
										ImGui.SameLine();
									}
									if (!_isOwner && pair.Value.HasValue && (!pair.Value.HasValue || pair.Value.Value.IsModerator()))
									{
										continue;
									}
									if (_uiSharedService.IconButton(FontAwesomeIcon.Thumbtack))
									{
										GroupPairUserInfo userInfo = pair.Value.GetValueOrDefault();
										userInfo.SetPinned(!userInfo.IsPinned());
										_apiController.GroupSetUserInfo(new GroupPairUserInfoDto(GroupFullInfo.Group, pair.Key.UserData, userInfo));
									}
									UiSharedService.AttachToolTip((pair.Value.HasValue && pair.Value.Value.IsPinned()) ? "Unpin user" : "Pin user");
									ImGui.SameLine();
									using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
									{
										if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
										{
											_apiController.GroupRemoveUser(new GroupPairDto(GroupFullInfo.Group, pair.Key.UserData));
										}
									}
									UiSharedService.AttachToolTip("Remove user from Syncshell--SEP--Hold CTRL to enable this button");
									ImGui.SameLine();
									using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
									{
										if (_uiSharedService.IconButton(FontAwesomeIcon.Ban))
										{
											base.Mediator.Publish(new OpenBanUserPopupMessage(pair.Key, GroupFullInfo));
										}
									}
									UiSharedService.AttachToolTip("Ban user from Syncshell--SEP--Hold CTRL to enable this button");
								}
							}
						}
					}
				}
				userNode.Dispose();
				ImRaii.IEndObject clearNode = ImRaii.TreeNode("Mass Cleanup");
				if (clearNode)
				{
					using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
					{
						if (_uiSharedService.IconTextButton(FontAwesomeIcon.Broom, "Clear Syncshell"))
						{
							_apiController.GroupClear(new GroupDto(GroupFullInfo.Group));
						}
					}
					UiSharedService.AttachToolTip("This will remove all non-pinned, non-moderator users from the Syncshell.--SEP--Hold CTRL to enable this button");
					ImGuiHelpers.ScaledDummy(2f);
					ImGui.Separator();
					ImGuiHelpers.ScaledDummy(2f);
					if (_uiSharedService.IconTextButton(FontAwesomeIcon.Unlink, "Check for Inactive Users"))
					{
						_pruneTestTask = _apiController.GroupPrune(new GroupDto(GroupFullInfo.Group), _pruneDays, execute: false);
						_pruneTask = null;
					}
					UiSharedService.AttachToolTip($"This will start the prune process for this Syncshell of inactive Mare users that have not logged in in the past {_pruneDays} days." + Environment.NewLine + "You will be able to review the amount of inactive users before executing the prune.--SEP--Note: this check excludes pinned users and moderators of this Syncshell.");
					ImGui.SameLine();
					ImGui.SetNextItemWidth(150f);
                    _uiSharedService.DrawCombo("Days of inactivity", [7, 14, 30, 90], (count) =>
                    {
                        return count + " days";
                    });
                    if (_pruneTestTask != null)
					{
						if (!_pruneTestTask.IsCompleted)
						{
							UiSharedService.ColorTextWrapped("Calculating inactive users...", ImGuiColors.DalamudYellow);
						}
						else
						{
							ImGui.AlignTextToFramePadding();
							UiSharedService.TextWrapped($"Found {_pruneTestTask.Result} user(s) that have not logged into Mare in the past {_pruneDays} days.");
							if (_pruneTestTask.Result > 0)
							{
								using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
								{
									if (_uiSharedService.IconTextButton(FontAwesomeIcon.Broom, "Prune Inactive Users"))
									{
										_pruneTask = _apiController.GroupPrune(new GroupDto(GroupFullInfo.Group), _pruneDays, execute: true);
										_pruneTestTask = null;
									}
								}
								UiSharedService.AttachToolTip($"Pruning will remove {_pruneTestTask?.Result ?? 0} inactive user(s)." + "--SEP--Hold CTRL to enable this button");
							}
						}
					}
					if (_pruneTask != null)
					{
						if (!_pruneTask.IsCompleted)
						{
							UiSharedService.ColorTextWrapped("Pruning Syncshell...", ImGuiColors.DalamudYellow);
						}
						else
						{
							UiSharedService.TextWrapped($"Syncshell was pruned and {_pruneTask.Result} inactive user(s) have been removed.");
						}
					}
				}
				clearNode.Dispose();
				ImRaii.IEndObject banNode = ImRaii.TreeNode("User Bans");
				if (banNode)
				{
					if (_uiSharedService.IconTextButton(FontAwesomeIcon.Retweet, "Refresh Banlist from Server"))
					{
						_bannedUsers = _apiController.GroupGetBannedUsers(new GroupDto(GroupFullInfo.Group)).Result;
					}
					if (ImGui.BeginTable("bannedusertable" + GroupFullInfo.GID, 6, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
					{
						ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.None, 1f);
						ImGui.TableSetupColumn("Alias", ImGuiTableColumnFlags.None, 1f);
						ImGui.TableSetupColumn("By", ImGuiTableColumnFlags.None, 1f);
						ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.None, 2f);
						ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 3f);
						ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 1f);
						ImGui.TableHeadersRow();
						foreach (BannedGroupUserDto bannedUser in _bannedUsers.ToList())
						{
							ImGui.TableNextColumn();
							ImGui.TextUnformatted(bannedUser.UID);
							ImGui.TableNextColumn();
							ImGui.TextUnformatted(bannedUser.UserAlias ?? string.Empty);
							ImGui.TableNextColumn();
							ImGui.TextUnformatted(bannedUser.BannedBy);
							ImGui.TableNextColumn();
							ImGui.TextUnformatted(bannedUser.BannedOn.ToLocalTime().ToString(CultureInfo.CurrentCulture));
							ImGui.TableNextColumn();
							UiSharedService.TextWrapped(bannedUser.Reason);
							ImGui.TableNextColumn();
							using (ImRaii.PushId(bannedUser.UID))
							{
								if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Unban"))
								{
									Task.Run(() => _apiController.GroupUnbanUser(bannedUser));
									_bannedUsers.RemoveAll((BannedGroupUserDto b) => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
								}
							}
						}
						ImGui.EndTable();
					}
				}
				banNode.Dispose();
			}
			mgmtTab.Dispose();
			ImRaii.IEndObject endObject2 = ImRaii.TabItem("Permissions");
			if (endObject2)
			{
				bool isDisableAnimations = perm.IsPreferDisableAnimations();
				bool isDisableSounds = perm.IsPreferDisableSounds();
				bool isDisableVfx = perm.IsPreferDisableVFX();
				ImGui.AlignTextToFramePadding();
				ImGui.Text("Suggest Sound Sync");
				_uiSharedService.BooleanToColoredIcon(!isDisableSounds);
				ImGui.SameLine(230f);
				if (_uiSharedService.IconTextButton(isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute, isDisableSounds ? "Suggest to enable sound sync" : "Suggest to disable sound sync"))
				{
					perm.SetPreferDisableSounds(!perm.IsPreferDisableSounds());
					_apiController.GroupChangeGroupPermissionState(new GroupPermissionDto(GroupFullInfo.Group, perm));
				}
				ImGui.AlignTextToFramePadding();
				ImGui.Text("Suggest Animation Sync");
				_uiSharedService.BooleanToColoredIcon(!isDisableAnimations);
				ImGui.SameLine(230f);
				if (_uiSharedService.IconTextButton(isDisableAnimations ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop, isDisableAnimations ? "Suggest to enable animation sync" : "Suggest to disable animation sync"))
				{
					perm.SetPreferDisableAnimations(!perm.IsPreferDisableAnimations());
					_apiController.GroupChangeGroupPermissionState(new GroupPermissionDto(GroupFullInfo.Group, perm));
				}
				ImGui.AlignTextToFramePadding();
				ImGui.Text("Suggest VFX Sync");
				_uiSharedService.BooleanToColoredIcon(!isDisableVfx);
				ImGui.SameLine(230f);
				if (_uiSharedService.IconTextButton(isDisableVfx ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle, isDisableVfx ? "Suggest to enable vfx sync" : "Suggest to disable vfx sync"))
				{
					perm.SetPreferDisableVFX(!perm.IsPreferDisableVFX());
					_apiController.GroupChangeGroupPermissionState(new GroupPermissionDto(GroupFullInfo.Group, perm));
				}
				UiSharedService.TextWrapped("Note: those suggested permissions will be shown to users on joining the Syncshell.");
			}
			endObject2.Dispose();
			if (!_isOwner)
			{
				return;
			}
			ImRaii.IEndObject ownerTab = ImRaii.TabItem("Owner Settings");
			if (ownerTab)
			{
				ImGui.AlignTextToFramePadding();
				ImGui.TextUnformatted("New Password");
				float num = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
				float buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Passport, "Change Password");
				float textSize = ImGui.CalcTextSize("New Password").X;
				float spacing = ImGui.GetStyle().ItemSpacing.X;
				ImGui.SameLine();
				ImGui.SetNextItemWidth(num - buttonSize - textSize - spacing * 2f);
				ImGui.InputTextWithHint("##changepw", "Min 10 characters", ref _newPassword, 50);
				ImGui.SameLine();
				using (ImRaii.Disabled(_newPassword.Length < 10))
				{
					if (_uiSharedService.IconTextButton(FontAwesomeIcon.Passport, "Change Password"))
					{
						_pwChangeSuccess = _apiController.GroupChangePassword(new GroupPasswordDto(GroupFullInfo.Group, _newPassword)).Result;
						_newPassword = string.Empty;
					}
				}
				UiSharedService.AttachToolTip("Password requires to be at least 10 characters long. This action is irreversible.");
				if (!_pwChangeSuccess)
				{
					UiSharedService.ColorTextWrapped("Failed to change the password. Password requires to be at least 10 characters long.", ImGuiColors.DalamudYellow);
				}
				if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Syncshell") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
				{
					base.IsOpen = false;
					_apiController.GroupDelete(new GroupDto(GroupFullInfo.Group));
				}
				UiSharedService.AttachToolTip("Hold CTRL and Shift and click to delete this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible.");
			}
			ownerTab.Dispose();
		}
	}

	public override void OnClose()
	{
		base.Mediator.Publish(new RemoveWindowMessage(this));
	}
}
