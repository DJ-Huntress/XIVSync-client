using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto.Group;
using XIVSync.Interop.Ipc;
using XIVSync.MareConfiguration;
using XIVSync.PlayerData.Handlers;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.UI.Components;
using XIVSync.UI.Handlers;
using XIVSync.UI.Theming;
using XIVSync.WebAPI;
using XIVSync.WebAPI.Files.Models;
using XIVSync.WebAPI.SignalR.Utils;

namespace XIVSync.UI;

public class CompactUi : WindowMediatorSubscriberBase
{
	private readonly ApiController _apiController;

	private readonly MareConfigService _configService;

	private readonly DrawEntityFactory _drawEntityFactory;

	private readonly IpcManager _ipcManager;

	private readonly PairManager _pairManager;

	private readonly SelectTagForPairUi _selectGroupForPairUi;

	private readonly SelectPairForTagUi _selectPairsForGroupUi;

	private readonly ServerConfigurationManager _serverManager;

	private readonly TagHandler _tagHandler;

	private readonly ThemeManager _themeManager;

	private readonly UiSharedService _uiSharedService;

	private readonly StatusMessageBar _statusMessageBar;

	private readonly TopTabMenu _tabMenu;

	private ThemeEditor? _themeEditor;

	private List<IDrawFolder> _drawFolders;

	private readonly string _titleBarText;

	private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>>();

	private bool _collapsed;

	private bool _userManuallyClosed;

	private bool _wasOpen;

	private Vector2 _lastPosition = Vector2.One;

	private Vector2 _lastSize = Vector2.One;

	private float _windowContentWidth;

	private float _transferPartHeight;

	private Pair? _lastAddedUser;

	private string _lastAddedUserComment = string.Empty;

	private bool _showModalForUserAddition;

	private float _gradientShift;

	private float _particleTime;

	private float _cardHoverScale = 1f;

	private ThemePalette _theme;

	private bool _showThemeInline;

	public void ToggleThemeInline()
	{
		_showThemeInline = !_showThemeInline;
		if (!_showThemeInline)
		{
			return;
		}
		if (_themeEditor == null)
		{
			_themeEditor = new ThemeEditor(_logger, _uiSharedService, _themeManager);
			_themeEditor.OnClosed = delegate
			{
				_showThemeInline = false;
				_theme = _themeManager.Current.Clone();
			};
		}
		_themeEditor.OnOpened();
	}

	public CompactUi(ILogger<CompactUi> logger, UiSharedService uiShared, MareConfigService configService, ApiController apiController, PairManager pairManager, ServerConfigurationManager serverManager, MareMediator mediator, TagHandler tagHandler, DrawEntityFactory drawEntityFactory, SelectTagForPairUi selectTagForPairUi, SelectPairForTagUi selectPairForTagUi, PerformanceCollectorService performanceCollectorService, IpcManager ipcManager, MareProfileManager profileManager, ThemeManager themeManager)
		: base(logger, mediator, "###MareSynchronosMainUI", performanceCollectorService)
	{
		Version ver = Assembly.GetExecutingAssembly().GetName().Version;
		_titleBarText = $"XIVSync ({ver.Major}.{ver.Minor}.{ver.Build})";
		_uiSharedService = uiShared;
		_configService = configService;
		_apiController = apiController;
		_pairManager = pairManager;
		_serverManager = serverManager;
		_tagHandler = tagHandler;
		_drawEntityFactory = drawEntityFactory;
		_selectGroupForPairUi = selectTagForPairUi;
		_selectPairsForGroupUi = selectPairForTagUi;
		_ipcManager = ipcManager;
		_themeManager = themeManager;
		_statusMessageBar = new StatusMessageBar(_apiController, logger, base.Mediator, profileManager, _uiSharedService);
		_tabMenu = new TopTabMenu(_logger, base.Mediator, _apiController, _pairManager, _uiSharedService, _configService, this);
		_theme = _themeManager.Current.Clone();
		_apiController.OnlineUsersChanged += delegate
		{
			base.Mediator.Publish(new RefreshUiMessage());
		};
		base.AllowPinning = false;
		base.AllowClickthrough = false;
		base.TitleBarButtons = new List<TitleBarButton>
		{
			new TitleBarButton
			{
				Icon = FontAwesomeIcon.Cog,
				Click = delegate
				{
					base.Mediator.Publish(new UiToggleMessage(typeof(ModernSettingsUi)));
				},
				IconOffset = new Vector2(2f, 1f),
				ShowTooltip = delegate
				{
					ImGui.BeginTooltip();
					Vector4 col2 = _theme.BtnText;
					ImGui.TextColored(in col2, "Open Mare Settings");
					ImGui.EndTooltip();
				}
			},
			new TitleBarButton
			{
				Icon = FontAwesomeIcon.Book,
				Click = delegate
				{
					base.Mediator.Publish(new UiToggleMessage(typeof(EventViewerUI)));
				},
				IconOffset = new Vector2(2f, 1f),
				ShowTooltip = delegate
				{
					ImGui.BeginTooltip();
					Vector4 col = _theme.BtnText;
					ImGui.TextColored(in col, "Open Mare Event Viewer");
					ImGui.EndTooltip();
				}
			}
		};
		_drawFolders = GetDrawFolders().ToList();
		base.Mediator.Subscribe<SwitchToMainUiMessage>(this, delegate
		{
			if (!_userManuallyClosed)
			{
				base.IsOpen = true;
			}
		});
		base.Mediator.Subscribe<SwitchToIntroUiMessage>(this, delegate
		{
			base.IsOpen = false;
		});
		base.Mediator.Subscribe<CutsceneStartMessage>(this, delegate
		{
			UiSharedService_GposeStart();
		});
		base.Mediator.Subscribe<CutsceneEndMessage>(this, delegate
		{
			UiSharedService_GposeEnd();
		});
		base.Mediator.Subscribe(this, delegate(DownloadStartedMessage msg)
		{
			_currentDownloads[msg.DownloadId] = msg.DownloadStatus;
		});
		base.Mediator.Subscribe(this, delegate(DownloadFinishedMessage msg)
		{
			_currentDownloads.TryRemove(msg.DownloadId, out Dictionary<string, FileDownloadStatus> _);
		});
		base.Mediator.Subscribe<RefreshUiMessage>(this, delegate
		{
			_drawFolders = GetDrawFolders().ToList();
		});
	}

	protected override void DrawInternal()
	{
		float deltaTime = ImGui.GetIO().DeltaTime;
		_gradientShift = (_gradientShift + deltaTime * 0.2f) % 1f;
		_particleTime += deltaTime;
		float targetScale = (ImGui.IsWindowHovered() ? 1.02f : 1f);
		_cardHoverScale += (targetScale - _cardHoverScale) * deltaTime * 5f;
		float headerHeight = 30f * ImGuiHelpers.GlobalScale;
		if (_collapsed)
		{
			base.Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground;
			if (base.AllowPinning)
			{
				base.Flags |= ImGuiWindowFlags.NoMove;
			}
			if (base.AllowClickthrough)
			{
				base.Flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing;
			}
			base.SizeConstraints = new WindowSizeConstraints
			{
				MinimumSize = new Vector2(375f, 40f),
				MaximumSize = new Vector2(375f, 40f)
			};
			DrawCustomTitleBarOverlay(headerHeight);
			return;
		}
		using (_themeManager.PushTheme())
		{
			base.Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoBringToFrontOnFocus;
			if (base.AllowPinning)
			{
				base.Flags |= ImGuiWindowFlags.NoMove;
			}
			if (base.AllowClickthrough)
			{
				base.Flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing;
			}
			base.SizeConstraints = new WindowSizeConstraints
			{
				MinimumSize = new Vector2(375f, 700f),
				MaximumSize = new Vector2(375f, 2000f)
			};
			using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 1f))
			{
				ImGuiWindowFlags childFlags = ImGuiWindowFlags.NoScrollbar;
				if (base.AllowClickthrough)
				{
					childFlags |= ImGuiWindowFlags.NoInputs;
				}
				ImGui.BeginChild("root-surface", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - 15f), border: true, childFlags);
				_windowContentWidth = UiSharedService.GetWindowContentRegionWidth();
				if (!_apiController.IsCurrentVersion)
				{
					Version ver = _apiController.CurrentClientVersion;
					string unsupported = "UNSUPPORTED VERSION";
					using (_uiSharedService.UidFont.Push())
					{
						Vector2 uidTextSize = ImGui.CalcTextSize(unsupported);
						ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2f - uidTextSize.X / 2f);
						ImGui.AlignTextToFramePadding();
						Vector4 col = ImGuiColors.DalamudRed;
						ImGui.TextColored(in col, unsupported);
					}
					UiSharedService.ColorTextWrapped($"Your XIVSync installation is out of date, the current version is {ver.Major}.{ver.Minor}.{ver.Build}. " + "It is highly recommended to keep XIVSync up to date. Open /xlplugins and update the plugin.", ImGuiColors.DalamudRed);
				}
				if (!_ipcManager.Initialized)
				{
					string unsupported = "MISSING ESSENTIAL PLUGINS";
					using (_uiSharedService.UidFont.Push())
					{
						Vector2 uidTextSize = ImGui.CalcTextSize(unsupported);
						ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2f - uidTextSize.X / 2f);
						ImGui.AlignTextToFramePadding();
						Vector4 col = ImGuiColors.DalamudRed;
						ImGui.TextColored(in col, unsupported);
					}
					bool penumAvailable = _ipcManager.Penumbra.APIAvailable;
					bool glamAvailable = _ipcManager.Glamourer.APIAvailable;
					UiSharedService.ColorTextWrapped("One or more Plugins essential for Mare operation are unavailable. Enable or update following plugins:", ImGuiColors.DalamudRed);
					using (ImRaii.PushIndent(10f))
					{
						if (!penumAvailable)
						{
							UiSharedService.TextWrapped("Penumbra");
							_uiSharedService.BooleanToColoredIcon(penumAvailable);
						}
						if (!glamAvailable)
						{
							UiSharedService.TextWrapped("Glamourer");
							_uiSharedService.BooleanToColoredIcon(glamAvailable);
						}
						ImGui.Separator();
					}
				}
				using (ImRaii.PushId("header"))
				{
					DrawUIDHeader();
				}
				ImGui.Separator();
				if (_apiController.ServerState == ServerState.Connected)
				{
					if (_showThemeInline)
					{
						using (ImRaii.PushId("theme-inline"))
						{
							DrawThemeInline();
						}
					}
					else
					{
						using (ImRaii.PushId("global-topmenu"))
						{
							_tabMenu.Draw(_theme);
						}
						using (ImRaii.PushId("status-message-bar"))
						{
							_statusMessageBar.Draw(_theme);
						}
						using (ImRaii.PushId("pairlist"))
						{
							DrawPairs();
						}
						float pairlistEnd = ImGui.GetCursorPosY();
						_transferPartHeight = ImGui.GetCursorPosY() - pairlistEnd - ImGui.GetTextLineHeight();
						using (ImRaii.PushId("group-user-popup"))
						{
							_selectPairsForGroupUi.Draw(_pairManager.DirectPairs);
						}
						using (ImRaii.PushId("grouping-popup"))
						{
							_selectGroupForPairUi.Draw();
						}
					}
				}
				if (_configService.Current.OpenPopupOnAdd && _pairManager.LastAddedUser != null)
				{
					_lastAddedUser = _pairManager.LastAddedUser;
					_pairManager.LastAddedUser = null;
					ImGui.OpenPopup("Set Notes for New User");
					_showModalForUserAddition = true;
					_lastAddedUserComment = string.Empty;
				}
				if (ImGui.BeginPopupModal("Set Notes for New User", ref _showModalForUserAddition, UiSharedService.PopupWindowFlags))
				{
					if (_lastAddedUser == null)
					{
						_showModalForUserAddition = false;
					}
					else
					{
						UiSharedService.TextWrapped("You have successfully added " + _lastAddedUser.UserData.AliasOrUID + ". Set a local note for the user in the field below:");
						ImU8String label = "##noteforuser";
						ImU8String hint = new ImU8String(9, 1);
						hint.AppendLiteral("Note for ");
						hint.AppendFormatted(_lastAddedUser.UserData.AliasOrUID);
						ImGui.InputTextWithHint(label, hint, ref _lastAddedUserComment, 100);
						using (ImRaii.PushColor(ImGuiCol.Text, _theme.BtnText))
						{
							if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save Note"))
							{
								_serverManager.SetNoteForUid(_lastAddedUser.UserData.UID, _lastAddedUserComment);
								_lastAddedUser = null;
								_lastAddedUserComment = string.Empty;
								_showModalForUserAddition = false;
							}
						}
					}
					UiSharedService.SetScaledWindowSize(275f);
					ImGui.EndPopup();
				}
				Vector2 pos = ImGui.GetWindowPos();
				Vector2 size = ImGui.GetWindowSize();
				if (_lastSize != size || _lastPosition != pos)
				{
					_lastSize = size;
					_lastPosition = pos;
					base.Mediator.Publish(new CompactUiChange(_lastSize, _lastPosition));
				}
				DrawUserCountOverlay();
				ImGui.EndChild();
				DrawCustomTitleBarOverlay(headerHeight);
			}
		}
	}

	private void DrawPairs()
	{
		float availY = ImGui.GetContentRegionAvail().Y - 20f;
		float transfersH = ((_transferPartHeight > 0f) ? _transferPartHeight : EstimateTransfersHeightFromStyle());
		float spacingY = ImGui.GetStyle().ItemSpacing.Y;
		float bottomReserve = 64f * ImGuiHelpers.GlobalScale;
		float listH = MathF.Max(1f, availY - transfersH - spacingY - bottomReserve);
		listH *= 1.2f;
		Vector2 rowFramePadding = new Vector2(8f, 6f);
		Vector2 rowItemSpacing = new Vector2(6f, 4f);
		Vector4 header = _theme.Btn;
		Vector4 headerHovered = _theme.BtnHovered;
		Vector4 headerActive = _theme.BtnActive;
		ImGui.BeginChild("list", new Vector2(_windowContentWidth, listH), border: false, ImGuiWindowFlags.NoBackground);
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, rowFramePadding);
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, rowItemSpacing);
		ImGui.PushStyleColor(ImGuiCol.Header, transparent);
		ImGui.PushStyleColor(ImGuiCol.HeaderHovered, transparent);
		ImGui.PushStyleColor(ImGuiCol.HeaderActive, transparent);
		ImGui.PushStyleColor(ImGuiCol.ChildBg, transparent);
		foreach (IDrawFolder drawFolder in _drawFolders)
		{
			drawFolder.Draw();
		}
		ImGui.Spacing();
		ImGui.Spacing();
		ImGui.Dummy(new Vector2(0f, 10f));
		ImGui.PopStyleColor(4);
		ImGui.PopStyleVar(2);
		ImGui.EndChild();
	}

	private static float EstimateTransfersHeightFromStyle()
	{
		ImGuiStylePtr style = ImGui.GetStyle();
		float textLineHeightWithSpacing = ImGui.GetTextLineHeightWithSpacing();
		float padding = style.ItemInnerSpacing.Y + style.ItemSpacing.Y;
		return textLineHeightWithSpacing * 2f + padding;
	}

	private void DrawUIDHeader()
	{
		string uidText = GetUidText();
		float availableWidth = ImGui.GetContentRegionAvail().X - 20f;
		Vector2 textSize;
		using (_uiSharedService.UidFont.Push())
		{
			Vector4 col = _theme.Accent;
			ImGui.TextColored(in col, uidText);
		}
		if (_apiController.ServerState == ServerState.Connected)
		{
			using (_uiSharedService.UidFont.Push())
			{
				ImGui.TextColored(_theme.Accent, uidText);
			}
			ThemedToolTip("Click to copy");
			if (!string.Equals(_apiController.DisplayName, _apiController.UID, StringComparison.Ordinal))
			{
				Vector4 col = _theme.Accent;
				ImGui.TextColored(in col, _apiController.UID);
				if (ImGui.IsItemClicked())
				{
					ImGui.SetClipboardText(_apiController.UID);
				}
				ThemedToolTip("Click to copy");
			}
		}
		else
		{
			UiSharedService.ColorTextWrapped(GetServerError(), GetUidColor());
		}
	}

	private IEnumerable<IDrawFolder> GetDrawFolders()
	{
		List<IDrawFolder> drawFolders = new List<IDrawFolder>();
		Dictionary<Pair, List<GroupFullInfoDto>> allPairs = _pairManager.PairsWithGroups.ToDictionary<KeyValuePair<Pair, List<GroupFullInfoDto>>, Pair, List<GroupFullInfoDto>>((KeyValuePair<Pair, List<GroupFullInfoDto>> k) => k.Key, (KeyValuePair<Pair, List<GroupFullInfoDto>> k) => k.Value);
		Dictionary<Pair, List<GroupFullInfoDto>> filteredPairs = allPairs.Where(delegate(KeyValuePair<Pair, List<GroupFullInfoDto>> p)
		{
			if (_tabMenu.Filter.IsNullOrEmpty())
			{
				return true;
			}
			if (!p.Key.UserData.AliasOrUID.Contains(_tabMenu.Filter, StringComparison.OrdinalIgnoreCase))
			{
				string? note2 = p.Key.GetNote();
				if (note2 == null || !note2.Contains(_tabMenu.Filter, StringComparison.OrdinalIgnoreCase))
				{
					return p.Key.PlayerName?.Contains(_tabMenu.Filter, StringComparison.OrdinalIgnoreCase) ?? false;
				}
			}
			return true;
		}).ToDictionary((KeyValuePair<Pair, List<GroupFullInfoDto>> k) => k.Key, (KeyValuePair<Pair, List<GroupFullInfoDto>> k) => k.Value);
		if (_configService.Current.ShowVisibleUsersSeparately)
		{
			ImmutableList<Pair> allVisiblePairs = ImmutablePairList(allPairs.Where(FilterVisibleUsers));
			Dictionary<Pair, List<GroupFullInfoDto>> filteredVisiblePairs = BasicSortedDictionary(filteredPairs.Where(FilterVisibleUsers));
			drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder("Mare_Visible", filteredVisiblePairs, allVisiblePairs));
		}
		List<IDrawFolder> groupFolders = new List<IDrawFolder>();
		foreach (GroupFullInfoDto group in _pairManager.GroupPairs.Select<KeyValuePair<GroupFullInfoDto, List<Pair>>, GroupFullInfoDto>((KeyValuePair<GroupFullInfoDto, List<Pair>> g) => g.Key).OrderBy<GroupFullInfoDto, string>((GroupFullInfoDto g) => g.GroupAliasOrGID, StringComparer.OrdinalIgnoreCase))
		{
			ImmutableList<Pair> allGroupPairs = ImmutablePairList(allPairs.Where((KeyValuePair<Pair, List<GroupFullInfoDto>> u) => FilterGroupUsers(u, group)));
			Dictionary<Pair, List<GroupFullInfoDto>> filteredGroupPairs = (from u in filteredPairs
				where FilterGroupUsers(u, @group) && FilterOnlineOrPausedSelf(u)
				orderby u.Key.IsOnline descending
				select u).ThenBy(delegate(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
			{
				if (string.Equals(u.Key.UserData.UID, group.OwnerUID, StringComparison.Ordinal))
				{
					return 0;
				}
				if (group.GroupPairUserInfos.TryGetValue(u.Key.UserData.UID, out var value))
				{
					if (value.IsModerator())
					{
						return 1;
					}
					if (value.IsPinned())
					{
						return 2;
					}
				}
				return (!u.Key.IsVisible) ? 4 : 3;
			}).ThenBy<KeyValuePair<Pair, List<GroupFullInfoDto>>, string>(AlphabeticalSort, StringComparer.OrdinalIgnoreCase).ToDictionary((KeyValuePair<Pair, List<GroupFullInfoDto>> k) => k.Key, (KeyValuePair<Pair, List<GroupFullInfoDto>> k) => k.Value);
			groupFolders.Add(_drawEntityFactory.CreateDrawGroupFolder(group, filteredGroupPairs, allGroupPairs));
		}
		if (_configService.Current.GroupUpSyncshells)
		{
			drawFolders.Add(new DrawGroupedGroupFolder(groupFolders, _tagHandler, _uiSharedService));
		}
		else
		{
			drawFolders.AddRange(groupFolders);
		}
		foreach (string tag in _tagHandler.GetAllTagsSorted())
		{
			ImmutableList<Pair> allTagPairs = ImmutablePairList(allPairs.Where((KeyValuePair<Pair, List<GroupFullInfoDto>> u) => FilterTagusers(u, tag)));
			Dictionary<Pair, List<GroupFullInfoDto>> filteredTagPairs = BasicSortedDictionary(filteredPairs.Where((KeyValuePair<Pair, List<GroupFullInfoDto>> u) => FilterTagusers(u, tag) && FilterOnlineOrPausedSelf(u)));
			drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder(tag, filteredTagPairs, allTagPairs));
		}
		ImmutableList<Pair> allOnlineNotTaggedPairs = ImmutablePairList(allPairs.Where(FilterNotTaggedUsers));
		Dictionary<Pair, List<GroupFullInfoDto>> onlineNotTaggedPairs = BasicSortedDictionary(filteredPairs.Where((KeyValuePair<Pair, List<GroupFullInfoDto>> u) => FilterNotTaggedUsers(u) && FilterOnlineOrPausedSelf(u)));
		drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder(_configService.Current.ShowOfflineUsersSeparately ? "Mare_Online" : "Mare_All", onlineNotTaggedPairs, allOnlineNotTaggedPairs));
		if (_configService.Current.ShowOfflineUsersSeparately)
		{
			ImmutableList<Pair> allOfflinePairs = ImmutablePairList(allPairs.Where(FilterOfflineUsers));
			Dictionary<Pair, List<GroupFullInfoDto>> filteredOfflinePairs = BasicSortedDictionary(filteredPairs.Where(FilterOfflineUsers));
			drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder("Mare_Offline", filteredOfflinePairs, allOfflinePairs));
			if (_configService.Current.ShowSyncshellOfflineUsersSeparately)
			{
				ImmutableList<Pair> allOfflineSyncshellUsers = ImmutablePairList(allPairs.Where(FilterOfflineSyncshellUsers));
				Dictionary<Pair, List<GroupFullInfoDto>> filteredOfflineSyncshellUsers = BasicSortedDictionary(filteredPairs.Where(FilterOfflineSyncshellUsers));
				drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder("Mare_OfflineSyncshell", filteredOfflineSyncshellUsers, allOfflineSyncshellUsers));
			}
		}
		drawFolders.Add(_drawEntityFactory.CreateDrawTagFolder("Mare_Unpaired", BasicSortedDictionary(filteredPairs.Where((KeyValuePair<Pair, List<GroupFullInfoDto>> u) => u.Key.IsOneSidedPair)), ImmutablePairList(allPairs.Where((KeyValuePair<Pair, List<GroupFullInfoDto>> u) => u.Key.IsOneSidedPair))));
		return drawFolders;
		string? AlphabeticalSort(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
		{
			string? note;
			if (!_configService.Current.ShowCharacterNameInsteadOfNotesForVisible || string.IsNullOrEmpty(u.Key.PlayerName))
			{
				note = u.Key.GetNote();
				if (note == null)
				{
					return u.Key.UserData.AliasOrUID;
				}
			}
			else
			{
				if (!_configService.Current.PreferNotesOverNamesForVisible)
				{
					return u.Key.PlayerName;
				}
				note = u.Key.GetNote();
			}
			return note;
		}
        Dictionary<Pair, List<GroupFullInfoDto>> BasicSortedDictionary(IEnumerable<KeyValuePair<Pair, List<GroupFullInfoDto>>> u)
            => u.OrderByDescending(u => u.Key.IsVisible)
                .ThenByDescending(u => u.Key.IsOnline)
                .ThenBy(AlphabeticalSort, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(u => u.Key, u => u.Value);
        static bool FilterGroupUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u, GroupFullInfoDto group)
		{
			return u.Value.Exists((GroupFullInfoDto g) => string.Equals(g.GID, group.GID, StringComparison.Ordinal));
		}
		bool FilterNotTaggedUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
		{
			if (u.Key.IsDirectlyPaired && !u.Key.IsOneSidedPair)
			{
				return !_tagHandler.HasAnyTag(u.Key.UserData.UID);
			}
			return false;
		}
		static bool FilterOfflineSyncshellUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
		{
			if (!u.Key.IsDirectlyPaired && !u.Key.IsOnline)
			{
				return !u.Key.UserPair.OwnPermissions.IsPaused();
			}
			return false;
		}
		bool FilterOfflineUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
		{
			if (((u.Key.IsDirectlyPaired && _configService.Current.ShowSyncshellOfflineUsersSeparately) || !_configService.Current.ShowSyncshellOfflineUsersSeparately) && (!u.Key.IsOneSidedPair || u.Value.Any()) && !u.Key.IsOnline)
			{
				return !u.Key.UserPair.OwnPermissions.IsPaused();
			}
			return false;
		}
		bool FilterOnlineOrPausedSelf(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
		{
			if (!u.Key.IsOnline && (u.Key.IsOnline || _configService.Current.ShowOfflineUsersSeparately))
			{
				return u.Key.UserPair.OwnPermissions.IsPaused();
			}
			return true;
		}
		bool FilterTagusers(KeyValuePair<Pair, List<GroupFullInfoDto>> u, string tag)
		{
			if (u.Key.IsDirectlyPaired && !u.Key.IsOneSidedPair)
			{
				return _tagHandler.HasTag(u.Key.UserData.UID, tag);
			}
			return false;
		}
		bool FilterVisibleUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u)
		{
			if (u.Key.IsVisible)
			{
				if (!_configService.Current.ShowSyncshellUsersInVisible)
				{
					if (!_configService.Current.ShowSyncshellUsersInVisible)
					{
						return u.Key.IsDirectlyPaired;
					}
					return true;
				}
				return true;
			}
			return false;
		}
		static ImmutableList<Pair> ImmutablePairList(IEnumerable<KeyValuePair<Pair, List<GroupFullInfoDto>>> u)
		{
			return u.Select<KeyValuePair<Pair, List<GroupFullInfoDto>>, Pair>((KeyValuePair<Pair, List<GroupFullInfoDto>> k) => k.Key).ToImmutableList();
		}
	}

	private string GetServerError()
	{
		return _apiController.ServerState switch
		{
			ServerState.Connecting => "Attempting to connect to the server.", 
			ServerState.Reconnecting => "Connection to server interrupted, attempting to reconnect to the server.", 
			ServerState.Disconnected => "You are currently disconnected from the XIVSync server.", 
			ServerState.Disconnecting => "Disconnecting from the server", 
			ServerState.Unauthorized => "Server Response: " + _apiController.AuthFailureMessage, 
			ServerState.Offline => "Your selected XIVSync server is currently offline.", 
			ServerState.VersionMisMatch => "Your plugin or the server you are connecting to is out of date. Please update your plugin now. If you already did so, contact the server provider to update their server to the latest version.", 
			ServerState.RateLimited => "You are rate limited for (re)connecting too often. Disconnect, wait 10 minutes and try again.", 
			ServerState.Connected => string.Empty, 
			ServerState.NoSecretKey => "You have no secret key set for this current character. Open Settings -> Service Settings and set a secret key for the current character. You can reuse the same secret key for multiple characters.", 
			ServerState.MultiChara => "Your Character Configuration has multiple characters configured with same name and world. You will not be able to connect until you fix this issue. Remove the duplicates from the configuration in Settings -> Service Settings -> Character Management and reconnect manually after.", 
			ServerState.OAuthMisconfigured => "OAuth2 is enabled but not fully configured, verify in the Settings -> Service Settings that you have OAuth2 connected and, importantly, a UID assigned to your current character.", 
			ServerState.OAuthLoginTokenStale => "Your OAuth2 login token is stale and cannot be used to renew. Go to the Settings -> Service Settings and unlink then relink your OAuth2 configuration.", 
			ServerState.NoAutoLogon => "This character has automatic login into Mare disabled. Press the connect button to connect to Mare.", 
			_ => string.Empty, 
		};
	}

	private Vector4 GetUidColor()
	{
		Vector4 success = ThemePalette.GetSemanticColorFromAccent(_theme, 0.33f);
		Vector4 warning = ThemePalette.GetSemanticColorFromAccent(_theme, 0.12f);
		Vector4 danger = ThemePalette.GetSemanticColorFromAccent(_theme, 0f);
		return _apiController.ServerState switch
		{
			ServerState.Connected => success, 
			ServerState.Connecting => warning, 
			ServerState.Reconnecting => warning, 
			ServerState.Disconnected => warning, 
			ServerState.Disconnecting => warning, 
			ServerState.RateLimited => warning, 
			ServerState.NoSecretKey => warning, 
			ServerState.MultiChara => warning, 
			ServerState.NoAutoLogon => warning, 
			ServerState.Unauthorized => danger, 
			ServerState.VersionMisMatch => danger, 
			ServerState.Offline => danger, 
			ServerState.OAuthMisconfigured => danger, 
			ServerState.OAuthLoginTokenStale => danger, 
			_ => danger, 
		};
	}

	private string GetUidText()
	{
		return _apiController.ServerState switch
		{
			ServerState.Reconnecting => "Reconnecting", 
			ServerState.Connecting => "Connecting", 
			ServerState.Disconnected => "Disconnected", 
			ServerState.Disconnecting => "Disconnecting", 
			ServerState.Unauthorized => "Unauthorized", 
			ServerState.VersionMisMatch => "Version mismatch", 
			ServerState.Offline => "Unavailable", 
			ServerState.RateLimited => "Rate Limited", 
			ServerState.NoSecretKey => "No Secret Key", 
			ServerState.MultiChara => "Duplicate Characters", 
			ServerState.OAuthMisconfigured => "Misconfigured OAuth2", 
			ServerState.OAuthLoginTokenStale => "Stale OAuth2", 
			ServerState.NoAutoLogon => "Auto Login disabled", 
			ServerState.Connected => _apiController.DisplayName, 
			_ => string.Empty, 
		};
	}

	private void UiSharedService_GposeEnd()
	{
		base.IsOpen = _wasOpen;
	}

	private void UiSharedService_GposeStart()
	{
		_wasOpen = base.IsOpen;
		base.IsOpen = false;
	}

	private void DrawCustomTitleBarOverlay(float headerH)
	{
		ServerState serverState = _apiController.ServerState;
		bool flag = (((uint)(serverState - 1) <= 1u || serverState == ServerState.Connected) ? true : false);
		bool isConnectingOrConnected = flag;
		serverState = _apiController.ServerState;
		flag = (uint)(serverState - 2) <= 1u;
		bool isBusy = flag;
		Vector4 linkColor = UiSharedService.GetBoolColor(!isConnectingOrConnected);
		if (_collapsed)
		{
			DrawCollapsedToolbar(headerH, isConnectingOrConnected, isBusy, linkColor);
		}
		else
		{
			DrawExpandedToolbar(isConnectingOrConnected, isBusy);
		}
	}

	private void DrawCollapsedToolbar(float headerH, bool isConnectingOrConnected, bool isBusy, Vector4 linkColor)
	{
		float spacing = 6f * ImGuiHelpers.GlobalScale;
		float btnSide = 22f * ImGuiHelpers.GlobalScale;
		float leftPad = 10f * ImGuiHelpers.GlobalScale;
		float rightPad = ImGui.GetStyle().WindowPadding.X + 25f * ImGuiHelpers.GlobalScale;
		Vector2 winPos = ImGui.GetWindowPos();
		Vector2 crMin = ImGui.GetWindowContentRegionMin();
		float contentW = ImGui.GetWindowContentRegionMax().X - crMin.X;
		Vector2 headerMin = new Vector2(winPos.X + crMin.X, ImGui.GetCursorScreenPos().Y);
		Vector2 headerMax = new Vector2(headerMin.X + contentW, headerMin.Y + headerH);
		ImGui.GetWindowDrawList().AddRectFilled(headerMin, headerMax, ImGui.ColorConvertFloat4ToU32(_theme.HeaderBg), 10f * ImGuiHelpers.GlobalScale);
		int buttonCount = 1;
		float buttonsW = btnSide * (float)buttonCount + spacing * (float)(buttonCount - 1);
		float num = headerMax.X - rightPad - buttonsW;
		float btnStartY = headerMin.Y + (headerH - btnSide) * 0.5f;
		Vector2 tSz = ImGui.CalcTextSize(_titleBarText);
		float num2 = headerMin.X + leftPad;
		float tY = headerMin.Y + (headerH - tSz.Y) * 0.5f;
		ImGui.SetCursorScreenPos(new Vector2(num2, tY));
		Vector4 col = _theme.TextPrimary;
		ImGui.TextColored(in col, _titleBarText);
		float dragLeft = num2 + tSz.X + spacing;
		float dragRight = num - spacing;
		float dragW = MathF.Max(0f, dragRight - dragLeft);
		ImGui.SetCursorScreenPos(new Vector2(dragLeft, headerMin.Y));
		ImGui.InvisibleButton("##dragzone_titlebar", new Vector2(dragW, headerH));
		if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
		{
			ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
		}
		ImGui.PushStyleColor(ImGuiCol.Button, _theme.Btn);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _theme.BtnHovered);
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, _theme.BtnActive);
		ImGui.PushStyleColor(ImGuiCol.Text, _theme.BtnText);
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f * ImGuiHelpers.GlobalScale);
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f * ImGuiHelpers.GlobalScale, 3f * ImGuiHelpers.GlobalScale));
		ImGui.SetCursorScreenPos(new Vector2(num, btnStartY));
		using (ImRaii.PushColor(ImGuiCol.Text, _theme.BtnText))
		{
			if (_uiSharedService.IconButton(FontAwesomeIcon.Bars))
			{
				ImGui.OpenPopup("##hamburger_menu_collapsed");
			}
		}
		ThemedToolTip("Menu");
		DrawHamburgerMenuPopup(isConnectingOrConnected, isBusy, linkColor);
		ImGui.PopStyleVar(2);
		ImGui.PopStyleColor(4);
	}

	private void DrawExpandedToolbar(bool isConnectingOrConnected, bool isBusy)
	{
		float spacing = 6f * ImGuiHelpers.GlobalScale;
		float btnSide = 28f * ImGuiHelpers.GlobalScale;
		ImGuiStylePtr style = ImGui.GetStyle();
		float topOffset = style.FramePadding.Y + style.ItemSpacing.Y + ImGuiHelpers.GlobalScale;
		Vector2 windowPos = ImGui.GetWindowPos();
		Vector2 crMin = ImGui.GetWindowContentRegionMin();
		Vector2 crMax = ImGui.GetWindowContentRegionMax();
		int buttonCount = 5;
		float stripWidth = btnSide * (float)buttonCount + spacing * (float)(buttonCount - 1);
		float overlayX = windowPos.X + crMax.X - stripWidth - spacing - 15f;
		float overlayY = windowPos.Y + crMin.Y + topOffset;
		ImGui.SetNextWindowPos(new Vector2(overlayX, overlayY), ImGuiCond.Always);
		ImGui.SetNextWindowBgAlpha(0f);
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0f));
		ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0f, 0f, 0f, 0f));
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f, 0f));
		ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
		ImGuiWindowFlags floatingFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking;
		ImGui.Begin("##xivsync-floating-controls", floatingFlags);
		ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0f, 0f, 0f, 0f));
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0f, 0f, 0f, 0.1f));
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 12f * ImGuiHelpers.GlobalScale);
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f * ImGuiHelpers.GlobalScale, 8f * ImGuiHelpers.GlobalScale));
		Vector2 buttonSize = new Vector2(btnSide, btnSide);
		float currentX = overlayX;
		DrawModernToolbarButton(FontAwesomeIcon.Cog, "Open Mare Settings", delegate
		{
			base.Mediator.Publish(new UiToggleMessage(typeof(ModernSettingsUi)));
		}, new Vector2(currentX, overlayY), buttonSize);
		currentX += btnSide + spacing;
		DrawModernToolbarButton(FontAwesomeIcon.Palette, "Customize Theme", delegate
		{
			ToggleThemeInline();
		}, new Vector2(currentX, overlayY), buttonSize);
		currentX += btnSide + spacing;
		FontAwesomeIcon disconnectIcon = (isConnectingOrConnected ? FontAwesomeIcon.Unlink : FontAwesomeIcon.Link);
		string disconnectTooltip = (isConnectingOrConnected ? ("Disconnect from " + _serverManager.CurrentServer.ServerName) : ("Connect to " + _serverManager.CurrentServer.ServerName));
		DrawModernToolbarButton(disconnectIcon, disconnectTooltip, delegate
		{
			if (!isBusy)
			{
				if (isConnectingOrConnected && !_serverManager.CurrentServer.FullPause)
				{
					_serverManager.CurrentServer.FullPause = true;
					_serverManager.Save();
				}
				else if (!isConnectingOrConnected && _serverManager.CurrentServer.FullPause)
				{
					_serverManager.CurrentServer.FullPause = false;
					_serverManager.Save();
				}
				_apiController.CreateConnectionsAsync();
			}
		}, new Vector2(currentX, overlayY), buttonSize);
		currentX += btnSide + spacing;
		DrawModernToolbarButton(FontAwesomeIcon.Times, "Close", delegate
		{
			_userManuallyClosed = true;
			base.IsOpen = false;
		}, new Vector2(currentX, overlayY), buttonSize);
		currentX += btnSide + spacing;
		DrawModernToolbarButton(FontAwesomeIcon.ChevronUp, "Collapse", delegate
		{
			_collapsed = !_collapsed;
		}, new Vector2(currentX, overlayY), buttonSize);
		ImGui.PopStyleVar(2);
		ImGui.PopStyleColor(3);
		ImGui.PopStyleVar(3);
		ImGui.PopStyleColor(2);
		ImGui.End();
		if (_apiController.ServerState == ServerState.Connected && base.AllowClickthrough)
		{
			DrawResetButton(overlayX, overlayY, spacing);
		}
	}

	private void DrawHamburgerMenuPopup(bool isConnectingOrConnected, bool isBusy, Vector4 linkColor)
	{
		using (ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, 8f))
		{
			using (ImRaii.PushColor(ImGuiCol.PopupBg, _theme.Btn))
			{
				using (ImRaii.PushColor(ImGuiCol.Border, _theme.PanelBorder))
				{
					using (ImRaii.PushColor(ImGuiCol.Text, _theme.BtnText))
					{
						using (ImRaii.PushColor(ImGuiCol.HeaderHovered, _theme.BtnHovered))
						{
							using (ImRaii.PushColor(ImGuiCol.HeaderActive, _theme.BtnActive))
							{
								if (!ImGui.BeginPopup("##hamburger_menu_collapsed"))
								{
									return;
								}
								string collapseText = (_collapsed ? "Expand" : "Collapse");
								FontAwesomeIcon collapseIcon = (_collapsed ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronUp);
								ImU8String label = new ImU8String(2, 2);
								label.AppendFormatted(collapseIcon.ToIconString());
								label.AppendLiteral("  ");
								label.AppendFormatted(collapseText);
								if (ImGui.MenuItem(label))
								{
									_collapsed = !_collapsed;
								}
								ImGui.Separator();
								string connectText = (isConnectingOrConnected ? ("Disconnect from " + _serverManager.CurrentServer.ServerName) : ("Connect to " + _serverManager.CurrentServer.ServerName));
								FontAwesomeIcon connectIcon = (isConnectingOrConnected ? FontAwesomeIcon.Unlink : FontAwesomeIcon.Link);
								using (ImRaii.PushColor(ImGuiCol.Text, linkColor))
								{
									if (isBusy)
									{
										ImGui.BeginDisabled();
									}
									label = new ImU8String(2, 2);
									label.AppendFormatted(connectIcon.ToIconString());
									label.AppendLiteral("  ");
									label.AppendFormatted(connectText);
									if (ImGui.MenuItem(label))
									{
										if (isConnectingOrConnected && !_serverManager.CurrentServer.FullPause)
										{
											_serverManager.CurrentServer.FullPause = true;
											_serverManager.Save();
										}
										else if (!isConnectingOrConnected && _serverManager.CurrentServer.FullPause)
										{
											_serverManager.CurrentServer.FullPause = false;
											_serverManager.Save();
										}
										_apiController.CreateConnectionsAsync();
									}
									if (isBusy)
									{
										ImGui.EndDisabled();
									}
								}
								ImGui.Separator();
								label = new ImU8String(10, 1);
								label.AppendFormatted(FontAwesomeIcon.Cog.ToIconString());
								label.AppendLiteral("  Settings");
								if (ImGui.MenuItem(label))
								{
									base.Mediator.Publish(new UiToggleMessage(typeof(ModernSettingsUi)));
								}
								label = new ImU8String(17, 1);
								label.AppendFormatted(FontAwesomeIcon.Palette.ToIconString());
								label.AppendLiteral("  Customize Theme");
								if (ImGui.MenuItem(label))
								{
									ToggleThemeInline();
								}
								ImGui.Separator();
								label = new ImU8String(19, 1);
								label.AppendFormatted(FontAwesomeIcon.UserCircle.ToIconString());
								label.AppendLiteral("  Edit Mare Profile");
								if (ImGui.MenuItem(label))
								{
									base.Mediator.Publish(new UiToggleMessage(typeof(EditProfileUi)));
								}
								label = new ImU8String(25, 1);
								label.AppendFormatted(FontAwesomeIcon.PersonCircleQuestion.ToIconString());
								label.AppendLiteral("  Character Data Analysis");
								if (ImGui.MenuItem(label))
								{
									base.Mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
								}
								label = new ImU8String(20, 1);
								label.AppendFormatted(FontAwesomeIcon.Running.ToIconString());
								label.AppendLiteral("  Character Data Hub");
								if (ImGui.MenuItem(label))
								{
									base.Mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
								}
								ImGui.Separator();
								bool isPinned = base.AllowPinning;
								label = new ImU8String(12, 1);
								label.AppendFormatted(FontAwesomeIcon.Thumbtack.ToIconString());
								label.AppendLiteral("  Pin Window");
								if (ImGui.MenuItem(label, "", isPinned))
								{
									base.AllowPinning = !base.AllowPinning;
								}
								bool isClickThrough = base.AllowClickthrough;
								label = new ImU8String(15, 1);
								label.AppendFormatted(FontAwesomeIcon.MousePointer.ToIconString());
								label.AppendLiteral("  Click Through");
								if (ImGui.MenuItem(label, "", isClickThrough))
								{
									base.AllowClickthrough = !base.AllowClickthrough;
									if (base.AllowClickthrough)
									{
										base.AllowPinning = true;
									}
								}
								ImGui.Separator();
								label = new ImU8String(7, 1);
								label.AppendFormatted(FontAwesomeIcon.Times.ToIconString());
								label.AppendLiteral("  Close");
								if (ImGui.MenuItem(label))
								{
									base.IsOpen = false;
								}
								ImGui.EndPopup();
							}
						}
					}
				}
			}
		}
	}

	private void DrawModernToolbarButton(FontAwesomeIcon icon, string tooltip, Action onClick, Vector2 position, Vector2 buttonSize)
	{
		ImGui.SetCursorScreenPos(position);
		ImU8String strId = new ImU8String(10, 1);
		strId.AppendLiteral("##toolbar_");
		strId.AppendFormatted(icon);
		bool clicked = ImGui.InvisibleButton(strId, buttonSize);
		bool hovered = ImGui.IsItemHovered();
		ImDrawListPtr fgDrawList = ImGui.GetForegroundDrawList();
		if (hovered)
		{
			Vector4 rgbColor = ThemeEffects.GetAnimatedAccentColor(_theme, _particleTime);
			float glowIntensity = 0.7f + MathF.Sin(_particleTime * 0.5f) * 0.3f;
			Vector4 glowColor = new Vector4(rgbColor.X * glowIntensity, rgbColor.Y * glowIntensity, rgbColor.Z * glowIntensity, 0.35f);
			float glowSize = 6f * ImGuiHelpers.GlobalScale;
			fgDrawList.AddRectFilled(new Vector2(position.X - glowSize, position.Y - glowSize), new Vector2(position.X + buttonSize.X + glowSize, position.Y + buttonSize.Y + glowSize), ImGui.ColorConvertFloat4ToU32(glowColor), 10f * ImGuiHelpers.GlobalScale);
		}
		using (_uiSharedService.IconFont.Push())
		{
			string iconText = icon.ToIconString();
			Vector2 iconSize = ImGui.CalcTextSize(iconText);
			Vector2 iconPos = new Vector2(position.X + (buttonSize.X - iconSize.X) * 0.5f, position.Y + (buttonSize.Y - iconSize.Y) * 0.5f);
			Vector4 textColor = (hovered ? _theme.BtnTextHovered : _theme.BtnText);
			fgDrawList.AddText(iconPos, ImGui.ColorConvertFloat4ToU32(textColor), iconText);
		}
		if (hovered)
		{
			ImGui.BeginTooltip();
			Vector4 col = _theme.BtnText;
			ImGui.TextColored(in col, tooltip);
			ImGui.EndTooltip();
		}
		if (clicked)
		{
			onClick();
		}
	}

	private void DrawResetButton(float overlayX, float overlayY, float spacing)
	{
		ImGui.SetNextWindowPos(new Vector2(overlayX - _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Undo).X - spacing, overlayY));
		ImGui.SetNextWindowBgAlpha(0f);
		ImGuiWindowFlags floatingFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking;
		if (ImGui.Begin("##xivsync-reset-button", floatingFlags))
		{
			ImGui.PushStyleColor(ImGuiCol.Button, _theme.Btn);
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _theme.BtnHovered);
			ImGui.PushStyleColor(ImGuiCol.ButtonActive, _theme.BtnActive);
			ImGui.PushStyleColor(ImGuiCol.Text, _theme.BtnText);
			ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f * ImGuiHelpers.GlobalScale);
			ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f * ImGuiHelpers.GlobalScale, 3f * ImGuiHelpers.GlobalScale));
			if (_uiSharedService.IconButton(FontAwesomeIcon.Undo))
			{
				base.AllowClickthrough = false;
				base.AllowPinning = false;
				_tabMenu.ClearUserFilter();
			}
			ThemedToolTip("Reset window settings");
			ImGui.PopStyleVar(2);
			ImGui.PopStyleColor(4);
		}
		ImGui.End();
	}

	private void DrawThemeInline()
	{
		if (_themeEditor != null)
		{
			_themeEditor.Draw(_theme);
			_theme = _themeManager.Current.Clone();
		}
	}

	private void ThemedToolTip(string text)
	{
		UiSharedService.AttachThemedToolTip(text, _theme);
	}

	private void DrawUserCountOverlay()
	{
		if (_apiController.ServerState == ServerState.Connected && !_showThemeInline)
		{
			Version ver = Assembly.GetExecutingAssembly().GetName().Version;
			string versionText = $"{ver.Major}.{ver.Minor}.{ver.Build}";
			string userCount = _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture);
			Vector2 textSize = ImGui.CalcTextSize(versionText + " | " + userCount + " Users Online");
			Vector2 childPos = ImGui.GetWindowPos();
			Vector2 childSize = ImGui.GetWindowSize();
			Vector2 textPos = new Vector2(childPos.X + (childSize.X - textSize.X) / 2f, childPos.Y + childSize.Y - textSize.Y - 15f);
			ImDrawListPtr drawList = ImGui.GetWindowDrawList();
			uint secondaryColor = ImGui.ColorConvertFloat4ToU32(_theme.TextSecondary);
			Vector2 versionSize = ImGui.CalcTextSize(versionText);
			drawList.AddText(textPos, secondaryColor, versionText);
			Vector2 currentPos = new Vector2(textPos.X + versionSize.X, textPos.Y);
			Vector2 separatorSize = ImGui.CalcTextSize(" | ");
			drawList.AddText(currentPos, secondaryColor, " | ");
			uint accentColor = ImGui.ColorConvertFloat4ToU32(_theme.Accent);
			currentPos = new Vector2(currentPos.X + separatorSize.X, textPos.Y);
			Vector2 userCountSize = ImGui.CalcTextSize(userCount);
			drawList.AddText(currentPos, accentColor, userCount);
			uint textColor = ImGui.ColorConvertFloat4ToU32(_theme.TextPrimary);
			currentPos = new Vector2(currentPos.X + userCountSize.X, textPos.Y);
			drawList.AddText(currentPos, textColor, " Users Online");
		}
	}

	public override void OnClose()
	{
		_userManuallyClosed = true;
	}

	public new void Toggle()
	{
		if (!base.IsOpen)
		{
			_userManuallyClosed = false;
		}
		base.Toggle();
	}
}
