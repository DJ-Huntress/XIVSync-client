using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Dto.CharaData;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.CharaData;
using XIVSync.Services.CharaData.Models;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.Utils;

namespace XIVSync.UI;

internal sealed class CharaDataHubUi : WindowMediatorSubscriberBase
{
	private const int maxPoses = 10;

	private readonly CharaDataManager _charaDataManager;

	private readonly CharaDataNearbyManager _charaDataNearbyManager;

	private readonly CharaDataConfigService _configService;

	private readonly DalamudUtilService _dalamudUtilService;

	private readonly FileDialogManager _fileDialogManager;

	private readonly PairManager _pairManager;

	private readonly CharaDataGposeTogetherManager _charaDataGposeTogetherManager;

	private readonly ServerConfigurationManager _serverConfigurationManager;

	private readonly UiSharedService _uiSharedService;

	private CancellationTokenSource _closalCts = new CancellationTokenSource();

	private bool _disableUI;

	private CancellationTokenSource _disposalCts = new CancellationTokenSource();

	private string _exportDescription = string.Empty;

	private string _filterCodeNote = string.Empty;

	private string _filterDescription = string.Empty;

	private Dictionary<string, List<CharaDataMetaInfoExtendedDto>>? _filteredDict;

	private Dictionary<string, (CharaDataFavorite Favorite, CharaDataMetaInfoExtendedDto? MetaInfo, bool DownloadedMetaInfo)> _filteredFavorites = new Dictionary<string, (CharaDataFavorite, CharaDataMetaInfoExtendedDto, bool)>();

	private bool _filterPoseOnly;

	private bool _filterWorldOnly;

	private string _gposeTarget = string.Empty;

	private bool _hasValidGposeTarget;

	private string _importCode = string.Empty;

	private bool _isHandlingSelf;

	private DateTime _lastFavoriteUpdateTime = DateTime.UtcNow;

	private PoseEntryExtended? _nearbyHovered;

	private bool _openMcdOnlineOnNextRun;

	private bool _readExport;

	private string _selectedDtoId = string.Empty;

	private string _selectedSpecificUserIndividual = string.Empty;

	private string _selectedSpecificGroupIndividual = string.Empty;

	private string _sharedWithYouDescriptionFilter = string.Empty;

	private bool _sharedWithYouDownloadableFilter;

	private string _sharedWithYouOwnerFilter = string.Empty;

	private string _specificIndividualAdd = string.Empty;

	private string _specificGroupAdd = string.Empty;

	private bool _abbreviateCharaName;

	private string? _openComboHybridId;

	private (string Id, string? Alias, string AliasOrId, string? Note)[]? _openComboHybridEntries;

	private bool _comboHybridUsedLastFrame;

	private bool _openDataApplicationShared;

	private string _joinLobbyId = string.Empty;

	private string _createDescFilter = string.Empty;

	private string _createCodeFilter = string.Empty;

	private bool _createOnlyShowFav;

	private bool _createOnlyShowNotDownloadable;

	private bool _selectNewEntry;

	private int _dataEntries;

	private string SelectedDtoId
	{
		get
		{
			return _selectedDtoId;
		}
		set
		{
			if (!string.Equals(_selectedDtoId, value, StringComparison.Ordinal))
			{
				_charaDataManager.UploadTask = null;
				_selectedDtoId = value;
			}
		}
	}

	public CharaDataHubUi(ILogger<CharaDataHubUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService, CharaDataManager charaDataManager, CharaDataNearbyManager charaDataNearbyManager, CharaDataConfigService configService, UiSharedService uiSharedService, ServerConfigurationManager serverConfigurationManager, DalamudUtilService dalamudUtilService, FileDialogManager fileDialogManager, PairManager pairManager, CharaDataGposeTogetherManager charaDataGposeTogetherManager)
		: base(logger, mediator, "XIVSync Character Data Hub###XIVSyncCharaDataUI", performanceCollectorService)
	{
		SetWindowSizeConstraints();
		_charaDataManager = charaDataManager;
		_charaDataNearbyManager = charaDataNearbyManager;
		_configService = configService;
		_uiSharedService = uiSharedService;
		_serverConfigurationManager = serverConfigurationManager;
		_dalamudUtilService = dalamudUtilService;
		_fileDialogManager = fileDialogManager;
		_pairManager = pairManager;
		_charaDataGposeTogetherManager = charaDataGposeTogetherManager;
		base.Mediator.Subscribe<GposeStartMessage>(this, delegate
		{
			base.IsOpen |= _configService.Current.OpenMareHubOnGposeStart;
		});
		base.Mediator.Subscribe(this, delegate(OpenCharaDataHubWithFilterMessage msg)
		{
			base.IsOpen = true;
			_openDataApplicationShared = true;
			_sharedWithYouOwnerFilter = msg.UserData.AliasOrUID;
			UpdateFilteredItems();
		});
	}

	public string CharaName(string name)
	{
		if (_abbreviateCharaName)
		{
			string[] split = name.Split(" ");
			char reference = split[0].First();
			ReadOnlySpan<char> readOnlySpan = new ReadOnlySpan<char>(ref reference);
			ReadOnlySpan<char> readOnlySpan2 = ". ";
			char reference2 = split[1].First();
			ReadOnlySpan<char> readOnlySpan3 = new ReadOnlySpan<char>(ref reference2);
			char reference3 = '.';
			return string.Concat(readOnlySpan, readOnlySpan2, readOnlySpan3, new ReadOnlySpan<char>(ref reference3));
		}
		return name;
	}

	public override void OnClose()
	{
		if (_disableUI)
		{
			base.IsOpen = true;
			return;
		}
		_closalCts.Cancel();
		SelectedDtoId = string.Empty;
		_filteredDict = null;
		_sharedWithYouOwnerFilter = string.Empty;
		_importCode = string.Empty;
		_charaDataNearbyManager.ComputeNearbyData = false;
		_openComboHybridId = null;
		_openComboHybridEntries = null;
	}

	public override void OnOpen()
	{
		_closalCts = _closalCts.CancelRecreate();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_closalCts.CancelDispose();
			_disposalCts.CancelDispose();
		}
		base.Dispose(disposing);
	}

	protected override void DrawInternal()
	{
		if (!_comboHybridUsedLastFrame)
		{
			_openComboHybridId = null;
			_openComboHybridEntries = null;
		}
		_comboHybridUsedLastFrame = false;
		_disableUI = !(_charaDataManager.UiBlockingComputation?.IsCompleted ?? true);
		if (DateTime.UtcNow.Subtract(_lastFavoriteUpdateTime).TotalSeconds > 2.0)
		{
			_lastFavoriteUpdateTime = DateTime.UtcNow;
			UpdateFilteredFavorites();
		}
		(_hasValidGposeTarget, _gposeTarget) = _charaDataManager.CanApplyInGpose().GetAwaiter().GetResult();
		if (!_charaDataManager.BrioAvailable)
		{
			ImGuiHelpers.ScaledDummy(3f);
			UiSharedService.DrawGroupedCenteredColorText("To utilize any features related to posing or spawning characters you require to have Brio installed.", ImGuiColors.DalamudRed);
			UiSharedService.DistanceSeparator();
		}
		using (ImRaii.Disabled(_disableUI))
		{
			DisableDisabled(delegate
			{
				if (_charaDataManager.DataApplicationTask != null)
				{
					ImGui.AlignTextToFramePadding();
					ImGui.TextUnformatted("Applying Data to Actor");
					ImGui.SameLine();
					if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Cancel Application"))
					{
						_charaDataManager.CancelDataApplication();
					}
				}
				if (!string.IsNullOrEmpty(_charaDataManager.DataApplicationProgress))
				{
					UiSharedService.ColorTextWrapped(_charaDataManager.DataApplicationProgress, ImGuiColors.DalamudYellow);
				}
				if (_charaDataManager.DataApplicationTask != null)
				{
					UiSharedService.ColorTextWrapped("WARNING: During the data application avoid interacting with this actor to prevent potential crashes.", ImGuiColors.DalamudRed);
					ImGuiHelpers.ScaledDummy(5f);
					ImGui.Separator();
				}
			});
			using (ImRaii.TabBar("TabsTopLevel"))
			{
				bool smallUi = false;
				_isHandlingSelf = _charaDataManager.HandledCharaData.Any((HandledCharaDataEntry c) => c.IsSelf);
				if (_isHandlingSelf)
				{
					_openMcdOnlineOnNextRun = false;
				}
				using (ImRaii.IEndObject gposeTogetherTabItem = ImRaii.TabItem("GPose Together"))
				{
					if (gposeTogetherTabItem)
					{
						smallUi = true;
						DrawGposeTogether();
					}
				}
				using (ImRaii.IEndObject applicationTabItem = ImRaii.TabItem("Data Application", _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
				{
					if (applicationTabItem)
					{
						smallUi = true;
						using (ImRaii.TabBar("TabsApplicationLevel"))
						{
							using (ImRaii.Disabled(!_uiSharedService.IsInGpose))
							{
								using ImRaii.IEndObject endObject = ImRaii.TabItem("GPose Actors");
								if (endObject)
								{
									using (ImRaii.PushId("gposeControls"))
									{
										DrawGposeControls();
									}
								}
							}
							if (!_uiSharedService.IsInGpose)
							{
								UiSharedService.AttachToolTip("Only available in GPose");
							}
							using (ImRaii.IEndObject nearbyPosesTabItem = ImRaii.TabItem("Poses Nearby"))
							{
								if (nearbyPosesTabItem)
								{
									using (ImRaii.PushId("nearbyPoseControls"))
									{
										_charaDataNearbyManager.ComputeNearbyData = true;
										DrawNearbyPoses();
									}
								}
								else
								{
									_charaDataNearbyManager.ComputeNearbyData = false;
								}
							}
							using ImRaii.IEndObject gposeTabItem = ImRaii.TabItem("Apply Data", _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None);
							if (gposeTabItem)
							{
								smallUi = smallUi || true;
								using (ImRaii.PushId("applyData"))
								{
									DrawDataApplication();
								}
							}
						}
					}
					else
					{
						_charaDataNearbyManager.ComputeNearbyData = false;
					}
				}
				using (ImRaii.Disabled(_isHandlingSelf))
				{
					ImGuiTabItemFlags flagsTopLevel = ImGuiTabItemFlags.None;
					if (_openMcdOnlineOnNextRun)
					{
						flagsTopLevel = ImGuiTabItemFlags.SetSelected;
						_openMcdOnlineOnNextRun = false;
					}
					using ImRaii.IEndObject creationTabItem = ImRaii.TabItem("Data Creation", flagsTopLevel);
					if (creationTabItem)
					{
						using (ImRaii.TabBar("TabsCreationLevel"))
						{
							ImGuiTabItemFlags flags = ImGuiTabItemFlags.None;
							if (_openMcdOnlineOnNextRun)
							{
								flags = ImGuiTabItemFlags.SetSelected;
								_openMcdOnlineOnNextRun = false;
							}
							using (ImRaii.IEndObject mcdOnlineTabItem = ImRaii.TabItem("MCD Online", flags))
							{
								if (mcdOnlineTabItem)
								{
									using (ImRaii.PushId("mcdOnline"))
									{
										DrawMcdOnline();
									}
								}
							}
							using ImRaii.IEndObject mcdfTabItem = ImRaii.TabItem("MCDF Export");
							if (mcdfTabItem)
							{
								using (ImRaii.PushId("mcdfExport"))
								{
									DrawMcdfExport();
								}
							}
						}
					}
				}
				if (_isHandlingSelf)
				{
					UiSharedService.AttachToolTip("Cannot use creation tools while having Character Data applied to self.");
				}
				using (ImRaii.IEndObject settingsTabItem = ImRaii.TabItem("Settings"))
				{
					if (settingsTabItem)
					{
						using (ImRaii.PushId("settings"))
						{
							DrawSettings();
						}
					}
				}
				SetWindowSizeConstraints(smallUi);
			}
		}
	}

	private void DrawAddOrRemoveFavorite(CharaDataFullDto dto)
	{
		DrawFavorite(dto.Uploader.UID + ":" + dto.Id);
	}

	private void DrawAddOrRemoveFavorite(CharaDataMetaInfoExtendedDto? dto)
	{
		if (!(dto == null))
		{
			DrawFavorite(dto.FullId);
		}
	}

	private void DrawFavorite(string id)
	{
		CharaDataFavorite favorite;
		bool isFavorite = _configService.Current.FavoriteCodes.TryGetValue(id, out favorite);
		if (_configService.Current.FavoriteCodes.ContainsKey(id))
		{
			_uiSharedService.IconText(FontAwesomeIcon.Star, ImGuiColors.ParsedGold);
			UiSharedService.AttachToolTip("Custom Description: " + (favorite?.CustomDescription ?? string.Empty) + "--SEP--Click to remove from Favorites");
		}
		else
		{
			_uiSharedService.IconText(FontAwesomeIcon.Star, ImGuiColors.DalamudGrey);
			UiSharedService.AttachToolTip("Click to add to Favorites");
		}
		if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
		{
			if (isFavorite)
			{
				_configService.Current.FavoriteCodes.Remove(id);
			}
			else
			{
				_configService.Current.FavoriteCodes[id] = new CharaDataFavorite();
			}
			_configService.Save();
		}
	}

	private unsafe void DrawGposeControls()
	{
		_uiSharedService.BigText("GPose Actors");
		ImGuiHelpers.ScaledDummy(5f);
		using (ImRaii.PushIndent(10f))
		{
			foreach (ICharacter actor in _dalamudUtilService.GetGposeCharactersFromObjectTable())
			{
				if (actor == null)
				{
					continue;
				}
				using (ImRaii.PushId(actor.Name.TextValue))
				{
					UiSharedService.DrawGrouped(delegate
					{
						if (_uiSharedService.IconButton(FontAwesomeIcon.Crosshairs))
						{
							_dalamudUtilService.GposeTarget = (GameObject*)actor.Address;
						}
						ImGui.SameLine();
						UiSharedService.AttachToolTip("Target the GPose Character " + CharaName(actor.Name.TextValue));
						ImGui.AlignTextToFramePadding();
						float cursorPosX = ImGui.GetCursorPosX();
						using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, actor.Address == (_dalamudUtilService.GetGposeTargetGameObjectAsync().GetAwaiter().GetResult()?.Address ?? IntPtr.Zero)))
						{
							ImGui.TextUnformatted(CharaName(actor.Name.TextValue));
						}
						ImGui.SameLine(250f);
						HandledCharaDataEntry handledCharaDataEntry = _charaDataManager.HandledCharaData.FirstOrDefault((HandledCharaDataEntry c) => string.Equals(c.Name, actor.Name.TextValue, StringComparison.Ordinal));
						using (ImRaii.Disabled(handledCharaDataEntry == null))
						{
							_uiSharedService.IconText(FontAwesomeIcon.InfoCircle);
							string text = ((!string.IsNullOrEmpty(handledCharaDataEntry?.MetaInfo.Uploader.UID)) ? handledCharaDataEntry.MetaInfo.FullId : handledCharaDataEntry?.MetaInfo.Id);
							UiSharedService.AttachToolTip("Applied Data: " + (text ?? "No data applied"));
							ImGui.SameLine();
							using (ImRaii.Disabled(!actor.Name.TextValue.StartsWith("Brio ", StringComparison.Ordinal)))
							{
								if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
								{
									_charaDataManager.RemoveChara(actor.Name.TextValue);
								}
								UiSharedService.AttachToolTip("Remove character " + CharaName(actor.Name.TextValue));
							}
							ImGui.SameLine();
							if (_uiSharedService.IconButton(FontAwesomeIcon.Undo))
							{
								_charaDataManager.RevertChara(handledCharaDataEntry);
							}
							UiSharedService.AttachToolTip("Revert applied data from " + CharaName(actor.Name.TextValue));
							ImGui.SetCursorPosX(cursorPosX);
							DrawPoseData(handledCharaDataEntry?.MetaInfo, actor.Name.TextValue, hasValidGposeTarget: true);
						}
					});
					ImGuiHelpers.ScaledDummy(2f);
				}
			}
		}
	}

	private void DrawDataApplication()
	{
		_uiSharedService.BigText("Apply Character Appearance");
		ImGuiHelpers.ScaledDummy(5f);
		if (_uiSharedService.IsInGpose)
		{
			ImGui.TextUnformatted("GPose Target");
			ImGui.SameLine(200f);
			UiSharedService.ColorText(CharaName(_gposeTarget), UiSharedService.GetBoolColor(_hasValidGposeTarget));
		}
		if (!_hasValidGposeTarget)
		{
			ImGuiHelpers.ScaledDummy(3f);
			UiSharedService.DrawGroupedCenteredColorText("Applying data is only available in GPose with a valid selected GPose target.", ImGuiColors.DalamudYellow, 350f);
		}
		ImGuiHelpers.ScaledDummy(10f);
		using (ImRaii.TabBar("Tabs"))
		{
			using (ImRaii.IEndObject byFavoriteTabItem = ImRaii.TabItem("Favorites"))
			{
				if (byFavoriteTabItem)
				{
					using (ImRaii.PushId("byFavorite"))
					{
						ImGuiHelpers.ScaledDummy(5f);
						Vector2 max = ImGui.GetWindowContentRegionMax();
						UiSharedService.DrawTree("Filters", delegate
						{
							Vector2 windowContentRegionMax = ImGui.GetWindowContentRegionMax();
							ImGui.SetNextItemWidth(windowContentRegionMax.X - ImGui.GetCursorPosX());
							ImGui.InputTextWithHint("##ownFilter", "Code/Owner Filter", ref _filterCodeNote, 100);
							ImGui.SetNextItemWidth(windowContentRegionMax.X - ImGui.GetCursorPosX());
							ImGui.InputTextWithHint("##descFilter", "Custom Description Filter", ref _filterDescription, 100);
							ImGui.Checkbox("Only show entries with pose data", ref _filterPoseOnly);
							ImGui.Checkbox("Only show entries with world data", ref _filterWorldOnly);
							if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Reset Filter"))
							{
								_filterCodeNote = string.Empty;
								_filterDescription = string.Empty;
								_filterPoseOnly = false;
								_filterWorldOnly = false;
							}
						});
						ImGuiHelpers.ScaledDummy(5f);
						ImGui.Separator();
						using (ImRaii.Child("favorite"))
						{
							ImGuiHelpers.ScaledDummy(5f);
							using (ImRaii.PushIndent(5f))
							{
								Vector2 cursorPos = ImGui.GetCursorPos();
								max = ImGui.GetWindowContentRegionMax();
								foreach (KeyValuePair<string, (CharaDataFavorite, CharaDataMetaInfoExtendedDto, bool)> favorite in _filteredFavorites.OrderByDescending<KeyValuePair<string, (CharaDataFavorite, CharaDataMetaInfoExtendedDto, bool)>, DateTime>((KeyValuePair<string, (CharaDataFavorite Favorite, CharaDataMetaInfoExtendedDto MetaInfo, bool DownloadedMetaInfo)> k) => k.Value.Favorite.LastDownloaded))
								{
									UiSharedService.DrawGrouped(delegate
									{
										using (ImRaii.PushId(favorite.Key))
										{
											ImGui.AlignTextToFramePadding();
											DrawFavorite(favorite.Key);
											using (ImRaii.PushIndent(25f))
											{
												ImGui.SameLine();
												float cursorPosX = ImGui.GetCursorPosX();
												float num = max.X - cursorPos.X;
												bool item = favorite.Value.Item3;
												CharaDataMetaInfoExtendedDto metaInfo = favorite.Value.Item2;
												ImGui.AlignTextToFramePadding();
												using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey, !item))
												{
													using (ImRaii.PushColor(ImGuiCol.Text, UiSharedService.GetBoolColor(metaInfo != null), item))
													{
														ImGui.TextUnformatted(favorite.Key);
													}
												}
												Vector2 iconSize = _uiSharedService.GetIconSize(FontAwesomeIcon.Check);
												Vector2 iconButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowsSpin);
												Vector2 iconButtonSize2 = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight);
												Vector2 iconButtonSize3 = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus);
												float cursorPosX2 = num - (iconSize.X + iconButtonSize.X + iconButtonSize2.X + iconButtonSize3.X + ImGui.GetStyle().ItemSpacing.X * 3.5f);
												ImGui.SameLine();
												ImGui.SetCursorPosX(cursorPosX2);
												if (item)
												{
													_uiSharedService.BooleanToColoredIcon(metaInfo != null, inline: false);
													if (metaInfo != null)
													{
														UiSharedService.AttachToolTip("Metainfo present--SEP--" + $"Last Updated: {metaInfo.UpdatedDate}" + Environment.NewLine + "Description: " + metaInfo.Description + Environment.NewLine + $"Poses: {metaInfo.PoseData.Count}");
													}
													else
													{
														UiSharedService.AttachToolTip("Metainfo could not be downloaded.--SEP--The data associated with the code is either not present on the server anymore or you have no access to it");
													}
												}
												else
												{
													_uiSharedService.IconText(FontAwesomeIcon.QuestionCircle, ImGuiColors.DalamudGrey);
													UiSharedService.AttachToolTip("Unknown accessibility state. Click the button on the right to refresh.");
												}
												ImGui.SameLine();
												bool flag = _charaDataManager.IsInTimeout(favorite.Key);
												using (ImRaii.Disabled(flag))
												{
													if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowsSpin))
													{
														_charaDataManager.DownloadMetaInfo(favorite.Key, store: false);
														UpdateFilteredItems();
													}
												}
												UiSharedService.AttachToolTip(flag ? "Timeout for refreshing active, please wait before refreshing again." : "Refresh data for this entry from the Server.");
												ImGui.SameLine();
												GposeMetaInfoAction(delegate
												{
													if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight))
													{
														_charaDataManager.ApplyCharaDataToGposeTarget(metaInfo);
													}
												}, "Apply Character Data to GPose Target", metaInfo, _hasValidGposeTarget, isSpawning: false);
												ImGui.SameLine();
												GposeMetaInfoAction(delegate(CharaDataMetaInfoExtendedDto? meta)
												{
													if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
													{
														_charaDataManager.SpawnAndApplyData(meta);
													}
												}, "Spawn Actor with Brio and apply Character Data", metaInfo, _hasValidGposeTarget, isSpawning: true);
												string empty = string.Empty;
												string text2 = favorite.Key.Split(":")[0];
												empty = ((!(metaInfo != null)) ? text2 : metaInfo.Uploader.AliasOrUID);
												string noteForUid2 = _serverConfigurationManager.GetNoteForUid(text2);
												if (noteForUid2 != null)
												{
													empty = noteForUid2 + " (" + empty + ")";
												}
												ImGui.TextUnformatted(empty);
												ImGui.TextUnformatted("Last Use: ");
												ImGui.SameLine();
												ImGui.TextUnformatted((favorite.Value.Item1.LastDownloaded == DateTime.MaxValue) ? "Never" : favorite.Value.Item1.LastDownloaded.ToString());
												string buf = favorite.Value.Item1.CustomDescription;
												ImGui.SetNextItemWidth(num - cursorPosX);
												if (ImGui.InputTextWithHint("##desc", "Custom Description for Favorite", ref buf, 100))
												{
													favorite.Value.Item1.CustomDescription = buf;
													_configService.Save();
												}
												DrawPoseData(metaInfo, _gposeTarget, _hasValidGposeTarget);
											}
										}
									});
									ImGuiHelpers.ScaledDummy(5f);
								}
								if (_configService.Current.FavoriteCodes.Count == 0)
								{
									UiSharedService.ColorTextWrapped("You have no favorites added. Add Favorites through the other tabs before you can use this tab.", ImGuiColors.DalamudYellow);
								}
							}
						}
					}
				}
			}
			using (ImRaii.IEndObject byCodeTabItem = ImRaii.TabItem("Code"))
			{
				using (ImRaii.PushId("byCodeTab"))
				{
					if (byCodeTabItem)
					{
						using (ImRaii.Child("sharedWithYouByCode", new Vector2(0f, 0f), border: false, ImGuiWindowFlags.AlwaysAutoResize))
						{
							DrawHelpFoldout("You can apply character data you have a code for in this tab. Provide the code in it's given format \"OwnerUID:DataId\" into the field below and click on \"Get Info from Code\". This will provide you basic information about the data behind the code. Afterwards select an actor in GPose and press on \"Download and apply to <actor>\"." + Environment.NewLine + Environment.NewLine + "Description: as set by the owner of the code to give you more or additional information of what this code may contain." + Environment.NewLine + "Last Update: the date and time the owner of the code has last updated the data." + Environment.NewLine + "Is Downloadable: whether or not the code is downloadable and applicable. If the code is not downloadable, contact the owner so they can attempt to fix it." + Environment.NewLine + Environment.NewLine + "To download a code the code requires correct access permissions to be set by the owner. If getting info from the code fails, contact the owner to make sure they set their Access Permissions for the code correctly.");
							ImGuiHelpers.ScaledDummy(5f);
							ImGui.InputTextWithHint("##importCode", "Enter Data Code", ref _importCode, 100);
							using (ImRaii.Disabled(string.IsNullOrEmpty(_importCode)))
							{
								if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Get Info from Code"))
								{
									_charaDataManager.DownloadMetaInfo(_importCode);
								}
							}
							GposeMetaInfoAction(delegate(CharaDataMetaInfoExtendedDto? meta)
							{
								if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Download and Apply"))
								{
									_charaDataManager.ApplyCharaDataToGposeTarget(meta);
								}
							}, "Apply this Character Data to the current GPose actor", _charaDataManager.LastDownloadedMetaInfo, _hasValidGposeTarget, isSpawning: false);
							ImGui.SameLine();
							GposeMetaInfoAction(delegate(CharaDataMetaInfoExtendedDto? meta)
							{
								if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Download and Spawn"))
								{
									_charaDataManager.SpawnAndApplyData(meta);
								}
							}, "Spawn a new Brio actor and apply this Character Data", _charaDataManager.LastDownloadedMetaInfo, _hasValidGposeTarget, isSpawning: true);
							ImGui.SameLine();
							ImGui.AlignTextToFramePadding();
							DrawAddOrRemoveFavorite(_charaDataManager.LastDownloadedMetaInfo);
							ImGui.NewLine();
							Task<(string Result, bool Success)>? downloadMetaInfoTask = _charaDataManager.DownloadMetaInfoTask;
							if (downloadMetaInfoTask != null && !downloadMetaInfoTask.IsCompleted)
							{
								UiSharedService.ColorTextWrapped("Downloading meta info. Please wait.", ImGuiColors.DalamudYellow);
							}
							Task<(string Result, bool Success)>? downloadMetaInfoTask2 = _charaDataManager.DownloadMetaInfoTask;
							if (downloadMetaInfoTask2 != null && downloadMetaInfoTask2.IsCompleted && !_charaDataManager.DownloadMetaInfoTask.Result.Success)
							{
								UiSharedService.ColorTextWrapped(_charaDataManager.DownloadMetaInfoTask.Result.Result, ImGuiColors.DalamudRed);
							}
							using (ImRaii.Disabled(_charaDataManager.LastDownloadedMetaInfo == null))
							{
								ImGuiHelpers.ScaledDummy(5f);
								CharaDataMetaInfoExtendedDto metaInfo = _charaDataManager.LastDownloadedMetaInfo;
								ImGui.TextUnformatted("Description");
								ImGui.SameLine(150f);
								UiSharedService.TextWrapped(string.IsNullOrEmpty(metaInfo?.Description) ? "-" : metaInfo.Description);
								ImGui.TextUnformatted("Last Update");
								ImGui.SameLine(150f);
								ImGui.TextUnformatted(metaInfo?.UpdatedDate.ToLocalTime().ToString() ?? "-");
								ImGui.TextUnformatted("Is Downloadable");
								ImGui.SameLine(150f);
								_uiSharedService.BooleanToColoredIcon(metaInfo?.CanBeDownloaded ?? false, inline: false);
								ImGui.TextUnformatted("Poses");
								ImGui.SameLine(150f);
								if ((object)metaInfo != null && metaInfo.HasPoses)
								{
									DrawPoseData(metaInfo, _gposeTarget, _hasValidGposeTarget);
								}
								else
								{
									_uiSharedService.BooleanToColoredIcon(value: false, inline: false);
								}
							}
						}
					}
				}
			}
			using (ImRaii.IEndObject yourOwnTabItem = ImRaii.TabItem("Your Own"))
			{
				using (ImRaii.PushId("yourOwnTab"))
				{
					if (yourOwnTabItem)
					{
						DrawHelpFoldout("You can apply character data you created yourself in this tab. If the list is not populated press on \"Download your Character Data\"." + Environment.NewLine + Environment.NewLine + "To create new and edit your existing character data use the \"MCD Online\" tab.");
						ImGuiHelpers.ScaledDummy(5f);
						using (ImRaii.Disabled(_charaDataManager.GetAllDataTask != null || (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)))
						{
							if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Download your Character Data"))
							{
								_charaDataManager.GetAllData(_disposalCts.Token);
							}
						}
						if (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)
						{
							UiSharedService.AttachToolTip("You can only refresh all character data from server every minute. Please wait.");
						}
						ImGuiHelpers.ScaledDummy(5f);
						ImGui.Separator();
						using (ImRaii.Child("ownDataChild", new Vector2(0f, 0f), border: false, ImGuiWindowFlags.AlwaysAutoResize))
						{
							using (ImRaii.PushIndent(10f))
							{
								foreach (CharaDataFullExtendedDto data in _charaDataManager.OwnCharaData.Values)
								{
									if (_charaDataManager.TryGetMetaInfo(data.FullId, out CharaDataMetaInfoExtendedDto metaInfo))
									{
										DrawMetaInfoData(_gposeTarget, _hasValidGposeTarget, metaInfo, canOpen: true);
									}
								}
								ImGuiHelpers.ScaledDummy(5f);
							}
						}
					}
				}
			}
			using (ImRaii.IEndObject sharedWithYouTabItem = ImRaii.TabItem("Shared With You", _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
			{
				using (ImRaii.PushId("sharedWithYouTab"))
				{
					if (sharedWithYouTabItem)
					{
						DrawHelpFoldout("You can apply character data shared with you implicitly in this tab. Shared Character Data are Character Data entries that have \"Sharing\" set to \"Shared\" and you have access through those by meeting the access restrictions, i.e. you were specified by your UID to gain access or are paired with the other user according to the Access Restrictions setting." + Environment.NewLine + Environment.NewLine + "Filter if needed to find a specific entry, then just press on \"Apply to <actor>\" and it will download and apply the Character Data to the currently targeted GPose actor." + Environment.NewLine + Environment.NewLine + "Note: Shared Data of Pairs you have paused will not be shown here.");
						ImGuiHelpers.ScaledDummy(5f);
						DrawUpdateSharedDataButton();
						int activeFilters = 0;
						if (!string.IsNullOrEmpty(_sharedWithYouOwnerFilter))
						{
							activeFilters++;
						}
						if (!string.IsNullOrEmpty(_sharedWithYouDescriptionFilter))
						{
							activeFilters++;
						}
						if (_sharedWithYouDownloadableFilter)
						{
							activeFilters++;
						}
						UiSharedService.DrawTree(((activeFilters == 0) ? "Filters" : $"Filters ({activeFilters} active)") + "##filters", delegate
						{
							float nextItemWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
							ImGui.SetNextItemWidth(nextItemWidth);
							if (ImGui.InputTextWithHint("##filter", "Filter by UID/Note", ref _sharedWithYouOwnerFilter, 30))
							{
								UpdateFilteredItems();
							}
							ImGui.SetNextItemWidth(nextItemWidth);
							if (ImGui.InputTextWithHint("##filterDesc", "Filter by Description", ref _sharedWithYouDescriptionFilter, 50))
							{
								UpdateFilteredItems();
							}
							if (ImGui.Checkbox("Only show downloadable", ref _sharedWithYouDownloadableFilter))
							{
								UpdateFilteredItems();
							}
						});
						if (_filteredDict == null && _charaDataManager.GetSharedWithYouTask == null)
						{
							_filteredDict = (from k in _charaDataManager.SharedWithYouData.ToDictionary<KeyValuePair<UserData, List<CharaDataMetaInfoExtendedDto>>, string, List<CharaDataMetaInfoExtendedDto>>(delegate(KeyValuePair<UserData, List<CharaDataMetaInfoExtendedDto>> k)
								{
									string noteForUid = _serverConfigurationManager.GetNoteForUid(k.Key.UID);
									return (noteForUid == null) ? k.Key.AliasOrUID : (noteForUid + " (" + k.Key.AliasOrUID + ")");
								}, (KeyValuePair<UserData, List<CharaDataMetaInfoExtendedDto>> k) => k.Value, StringComparer.OrdinalIgnoreCase)
								where string.IsNullOrEmpty(_sharedWithYouOwnerFilter) || k.Key.Contains(_sharedWithYouOwnerFilter)
								select k).OrderBy<KeyValuePair<string, List<CharaDataMetaInfoExtendedDto>>, string>((KeyValuePair<string, List<CharaDataMetaInfoExtendedDto>> k) => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary();
						}
						ImGuiHelpers.ScaledDummy(5f);
						ImGui.Separator();
						using (ImRaii.Child("sharedWithYouChild", new Vector2(0f, 0f), border: false, ImGuiWindowFlags.AlwaysAutoResize))
						{
							ImGuiHelpers.ScaledDummy(5f);
							foreach (KeyValuePair<string, List<CharaDataMetaInfoExtendedDto>> entry in _filteredDict ?? new Dictionary<string, List<CharaDataMetaInfoExtendedDto>>())
							{
								bool isFilteredAndHasToBeOpened = entry.Key.Contains(_sharedWithYouOwnerFilter) && _openDataApplicationShared;
								if (isFilteredAndHasToBeOpened)
								{
									ImGui.SetNextItemOpen(isFilteredAndHasToBeOpened);
								}
								UiSharedService.DrawTree($"{entry.Key} - [{entry.Value.Count} Character Data Sets]##{entry.Key}", delegate
								{
									foreach (CharaDataMetaInfoExtendedDto current in entry.Value)
									{
										DrawMetaInfoData(_gposeTarget, _hasValidGposeTarget, current);
									}
									ImGuiHelpers.ScaledDummy(5f);
								});
								if (isFilteredAndHasToBeOpened)
								{
									_openDataApplicationShared = false;
								}
							}
						}
					}
				}
			}
			using ImRaii.IEndObject mcdfTabItem = ImRaii.TabItem("From MCDF");
			using (ImRaii.PushId("applyMcdfTab"))
			{
				if (!ImRaii.IEndObject.op_True(mcdfTabItem))
				{
					return;
				}
				using (ImRaii.Child("applyMcdf", new Vector2(0f, 0f), border: false, ImGuiWindowFlags.AlwaysAutoResize))
				{
					DrawHelpFoldout("You can apply character data shared with you using a MCDF file in this tab." + Environment.NewLine + Environment.NewLine + "Load the MCDF first via the \"Load MCDF\" button which will give you the basic description that the owner has set during export." + Environment.NewLine + "You can then apply it to any handled GPose actor." + Environment.NewLine + Environment.NewLine + "MCDF to share with others can be generated using the \"MCDF Export\" tab at the top.");
					ImGuiHelpers.ScaledDummy(5f);
					if (_charaDataManager.LoadedMcdfHeader == null || _charaDataManager.LoadedMcdfHeader.IsCompleted)
					{
						if (_uiSharedService.IconTextButton(FontAwesomeIcon.FolderOpen, "Load MCDF"))
						{
							_fileDialogManager.OpenFileDialog("Pick MCDF file", ".mcdf", delegate(bool success, List<string> paths)
							{
								if (success)
								{
									string text = paths.FirstOrDefault();
									if (text != null)
									{
										_configService.Current.LastSavedCharaDataLocation = Path.GetDirectoryName(text) ?? string.Empty;
										_configService.Save();
										_charaDataManager.LoadMcdf(text);
									}
								}
							}, 1, Directory.Exists(_configService.Current.LastSavedCharaDataLocation) ? _configService.Current.LastSavedCharaDataLocation : null);
						}
						UiSharedService.AttachToolTip("Load MCDF Metadata into memory");
						Task<(MareCharaFileHeader LoadedFile, long ExpectedLength)>? loadedMcdfHeader = _charaDataManager.LoadedMcdfHeader;
						if (loadedMcdfHeader != null && loadedMcdfHeader.IsCompleted)
						{
							ImGui.TextUnformatted("Loaded file");
							ImGui.SameLine(200f);
							UiSharedService.TextWrapped(_charaDataManager.LoadedMcdfHeader.Result.LoadedFile.FilePath);
							ImGui.Text("Description");
							ImGui.SameLine(200f);
							UiSharedService.TextWrapped(_charaDataManager.LoadedMcdfHeader.Result.LoadedFile.CharaFileData.Description);
							ImGuiHelpers.ScaledDummy(5f);
							using (ImRaii.Disabled(!_hasValidGposeTarget))
							{
								if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Apply"))
								{
									_charaDataManager.McdfApplyToGposeTarget();
								}
								UiSharedService.AttachToolTip("Apply to " + _gposeTarget);
								ImGui.SameLine();
								using (ImRaii.Disabled(!_charaDataManager.BrioAvailable))
								{
									if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Spawn Actor and Apply"))
									{
										_charaDataManager.McdfSpawnApplyToGposeTarget();
									}
								}
							}
						}
						Task<(MareCharaFileHeader LoadedFile, long ExpectedLength)>? loadedMcdfHeader2 = _charaDataManager.LoadedMcdfHeader;
						if (loadedMcdfHeader2 == null || !loadedMcdfHeader2.IsFaulted)
						{
							Task? mcdfApplicationTask = _charaDataManager.McdfApplicationTask;
							if (mcdfApplicationTask == null || !mcdfApplicationTask.IsFaulted)
							{
								return;
							}
						}
						UiSharedService.ColorTextWrapped("Failure to read MCDF file. MCDF file is possibly corrupt. Re-export the MCDF file and try again.", ImGuiColors.DalamudRed);
						UiSharedService.ColorTextWrapped("Note: if this is your MCDF, try redrawing yourself, wait and re-export the file. If you received it from someone else have them do the same.", ImGuiColors.DalamudYellow);
					}
					else
					{
						UiSharedService.ColorTextWrapped("Loading Character...", ImGuiColors.DalamudYellow);
					}
				}
			}
		}
	}

	private void DrawMcdfExport()
	{
		_uiSharedService.BigText("Mare Character Data File Export");
		DrawHelpFoldout("This feature allows you to pack your character into a MCDF file and manually send it to other people. MCDF files can officially only be imported during GPose through Mare. Be aware that the possibility exists that people write unofficial custom exporters to extract the containing data.");
		ImGuiHelpers.ScaledDummy(5f);
		ImGui.Checkbox("##readExport", ref _readExport);
		ImGui.SameLine();
		UiSharedService.TextWrapped("I understand that by exporting my character data into a file and sending it to other people I am giving away my current character appearance irrevocably. People I am sharing my data with have the ability to share it with other people without limitations.");
		if (!_readExport)
		{
			return;
		}
		ImGui.Indent();
		ImGui.InputTextWithHint("Export Descriptor", "This description will be shown on loading the data", ref _exportDescription, 255);
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Export Character as MCDF"))
		{
			string defaultFileName = (string.IsNullOrEmpty(_exportDescription) ? "export.mcdf" : string.Join('_', (_exportDescription + ".mcdf").Split(Path.GetInvalidFileNameChars())));
			_uiSharedService.FileDialogManager.SaveFileDialog("Export Character to file", ".mcdf", defaultFileName, ".mcdf", delegate(bool success, string path)
			{
				if (success)
				{
					_configService.Current.LastSavedCharaDataLocation = Path.GetDirectoryName(path) ?? string.Empty;
					_configService.Save();
					_charaDataManager.SaveMareCharaFile(_exportDescription, path);
					_exportDescription = string.Empty;
				}
			}, Directory.Exists(_configService.Current.LastSavedCharaDataLocation) ? _configService.Current.LastSavedCharaDataLocation : null);
		}
		UiSharedService.ColorTextWrapped("Note: For best results make sure you have everything you want to be shared as well as the correct character appearance equipped and redraw your character before exporting.", ImGuiColors.DalamudYellow);
		ImGui.Unindent();
	}

	private void DrawMetaInfoData(string selectedGposeActor, bool hasValidGposeTarget, CharaDataMetaInfoExtendedDto data, bool canOpen = false)
	{
		ImGuiHelpers.ScaledDummy(5f);
		using (ImRaii.PushId(data.FullId))
		{
			float startPos = ImGui.GetCursorPosX();
			float maxPos = ImGui.GetWindowContentRegionMax().X;
			float availableWidth = maxPos - startPos;
			UiSharedService.DrawGrouped(delegate
			{
				ImGui.AlignTextToFramePadding();
				DrawAddOrRemoveFavorite(data);
				ImGui.SameLine();
				float cursorPosX = ImGui.GetCursorPosX();
				ImGui.AlignTextToFramePadding();
				UiSharedService.ColorText(data.FullId, UiSharedService.GetBoolColor(data.CanBeDownloaded));
				if (!data.CanBeDownloaded)
				{
					UiSharedService.AttachToolTip("This data is incomplete on the server and cannot be downloaded. Contact the owner so they can fix it. If you are the owner, review the data in the MCD Online tab.");
				}
				float cursorPosX2 = availableWidth - _uiSharedService.GetIconSize(FontAwesomeIcon.Calendar).X - _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight).X - _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus).X - ImGui.GetStyle().ItemSpacing.X * 2f;
				ImGui.SameLine();
				ImGui.SetCursorPosX(cursorPosX2);
				_uiSharedService.IconText(FontAwesomeIcon.Calendar);
				UiSharedService.AttachToolTip($"Last Update: {data.UpdatedDate}");
				ImGui.SameLine();
				GposeMetaInfoAction(delegate(CharaDataMetaInfoExtendedDto? meta)
				{
					if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight))
					{
						_charaDataManager.ApplyCharaDataToGposeTarget(meta);
					}
				}, "Apply Character data to " + CharaName(selectedGposeActor), data, hasValidGposeTarget, isSpawning: false);
				ImGui.SameLine();
				GposeMetaInfoAction(delegate(CharaDataMetaInfoExtendedDto? meta)
				{
					if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
					{
						_charaDataManager.SpawnAndApplyData(meta);
					}
				}, "Spawn and Apply Character data", data, hasValidGposeTarget, isSpawning: true);
				using (ImRaii.PushIndent(cursorPosX - startPos))
				{
					if (canOpen)
					{
						using (ImRaii.Disabled(_isHandlingSelf))
						{
							if (_uiSharedService.IconTextButton(FontAwesomeIcon.Edit, "Open in MCD Online Editor"))
							{
								SelectedDtoId = data.Id;
								_openMcdOnlineOnNextRun = true;
							}
						}
						if (_isHandlingSelf)
						{
							UiSharedService.AttachToolTip("Cannot use MCD Online while having Character Data applied to self.");
						}
					}
					if (string.IsNullOrEmpty(data.Description))
					{
						UiSharedService.ColorTextWrapped("No description set", ImGuiColors.DalamudGrey, availableWidth);
					}
					else
					{
						UiSharedService.TextWrapped(data.Description, availableWidth);
					}
					DrawPoseData(data, selectedGposeActor, hasValidGposeTarget);
				}
			});
		}
	}

	private void DrawPoseData(CharaDataMetaInfoExtendedDto? metaInfo, string actor, bool hasValidGposeTarget)
	{
		if (metaInfo == null || !metaInfo.HasPoses)
		{
			return;
		}
		bool isInGpose = _uiSharedService.IsInGpose;
		float start = ImGui.GetCursorPosX();
		foreach (PoseEntryExtended item2 in metaInfo.PoseExtended)
		{
			PoseEntryExtended item = item2;
			if (!item.HasPoseData)
			{
				continue;
			}
			string tooltip = (string.IsNullOrEmpty(item.Description) ? "No description set" : ("Pose Description: " + item.Description));
			if (!isInGpose)
			{
				start = DrawIcon(start);
				UiSharedService.AttachToolTip(tooltip + "--SEP--" + (item.HasWorldData ? (GetWorldDataTooltipText(item) + "--SEP--Click to show on Map") : string.Empty));
				if (item.HasWorldData && ImGui.IsItemClicked(ImGuiMouseButton.Left))
				{
					_dalamudUtilService.SetMarkerAndOpenMap(item.Position, item.Map);
				}
				continue;
			}
			tooltip = tooltip + "--SEP--Left Click: Apply this pose to " + CharaName(actor);
			if (item.HasWorldData)
			{
				tooltip = tooltip + Environment.NewLine + "CTRL+Right Click: Apply world position to " + CharaName(actor) + ".--SEP--!!! CAUTION: Applying world position will likely yeet this actor into nirvana. Use at your own risk !!!";
			}
			GposePoseAction(delegate
			{
				start = DrawIcon(start);
				if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
				{
					_charaDataManager.ApplyPoseData(item, actor);
				}
				if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && UiSharedService.CtrlPressed())
				{
					_charaDataManager.ApplyWorldDataToTarget(item, actor);
				}
			}, tooltip, hasValidGposeTarget);
			ImGui.SameLine();
			float DrawIcon(float s)
			{
				ImGui.SetCursorPosX(s);
				float posX = ImGui.GetCursorPosX();
				_uiSharedService.IconText(item.HasWorldData ? FontAwesomeIcon.Circle : FontAwesomeIcon.Running);
				if (item.HasWorldData)
				{
					ImGui.SameLine();
					ImGui.SetCursorPosX(posX);
					using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.WindowBg)))
					{
						_uiSharedService.IconText(FontAwesomeIcon.Running);
						ImGui.SameLine();
						ImGui.SetCursorPosX(posX);
						_uiSharedService.IconText(FontAwesomeIcon.Running);
					}
				}
				ImGui.SameLine();
				return ImGui.GetCursorPosX();
			}
		}
		if (metaInfo.PoseExtended.Any())
		{
			ImGui.NewLine();
		}
	}

	private void DrawSettings()
	{
		ImGuiHelpers.ScaledDummy(5f);
		_uiSharedService.BigText("Settings");
		ImGuiHelpers.ScaledDummy(5f);
		bool openInGpose = _configService.Current.OpenMareHubOnGposeStart;
		if (ImGui.Checkbox("Open Character Data Hub when GPose loads", ref openInGpose))
		{
			_configService.Current.OpenMareHubOnGposeStart = openInGpose;
			_configService.Save();
		}
		_uiSharedService.DrawHelpText("This will automatically open the import menu when loading into Gpose. If unchecked you can open the menu manually with /mare gpose");
		bool downloadDataOnConnection = _configService.Current.DownloadMcdDataOnConnection;
		if (ImGui.Checkbox("Download MCD Online Data on connecting", ref downloadDataOnConnection))
		{
			_configService.Current.DownloadMcdDataOnConnection = downloadDataOnConnection;
			_configService.Save();
		}
		_uiSharedService.DrawHelpText("This will automatically download MCD Online data (Your Own and Shared with You) once a connection is established to the server.");
		bool showHelpTexts = _configService.Current.ShowHelpTexts;
		if (ImGui.Checkbox("Show \"What is this? (Explanation / Help)\" foldouts", ref showHelpTexts))
		{
			_configService.Current.ShowHelpTexts = showHelpTexts;
			_configService.Save();
		}
		ImGui.Checkbox("Abbreviate Chara Names", ref _abbreviateCharaName);
		_uiSharedService.DrawHelpText("This setting will abbreviate displayed names. This setting is not persistent and will reset between restarts.");
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("Last Export Folder");
		ImGui.SameLine(300f);
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted(string.IsNullOrEmpty(_configService.Current.LastSavedCharaDataLocation) ? "Not set" : _configService.Current.LastSavedCharaDataLocation);
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Clear Last Export Folder"))
		{
			_configService.Current.LastSavedCharaDataLocation = string.Empty;
			_configService.Save();
		}
		_uiSharedService.DrawHelpText("Use this if the Load or Save MCDF file dialog does not open");
	}

	private void DrawHelpFoldout(string text)
	{
		if (_configService.Current.ShowHelpTexts)
		{
			ImGuiHelpers.ScaledDummy(5f);
			UiSharedService.DrawTree("What is this? (Explanation / Help)", delegate
			{
				UiSharedService.TextWrapped(text);
			});
		}
	}

	private void DisableDisabled(Action drawAction)
	{
		if (_disableUI)
		{
			ImGui.EndDisabled();
		}
		drawAction();
		if (_disableUI)
		{
			ImGui.BeginDisabled();
		}
	}

	private static string GetAccessTypeString(AccessTypeDto dto)
	{
		switch (dto)
		{
		case AccessTypeDto.AllPairs:
			return "All Pairs";
		case AccessTypeDto.ClosePairs:
			return "Direct Pairs";
		case AccessTypeDto.Individuals:
			return "Specified";
		case AccessTypeDto.Public:
			return "Everyone";
		}
	}

	private static string GetShareTypeString(ShareTypeDto dto)
	{
		switch (dto)
		{
		case ShareTypeDto.Private:
			return "Code Only";
		case ShareTypeDto.Shared:
			return "Shared";
		default:
		{
			global::_003CPrivateImplementationDetails_003E.ThrowSwitchExpressionException(dto);
			string result = default(string);
			return result;
		}
		}
	}

	private static string GetWorldDataTooltipText(PoseEntryExtended poseEntry)
	{
		if (!poseEntry.HasWorldData)
		{
			return "This Pose has no world data attached.";
		}
		return poseEntry.WorldDataDescriptor;
	}

	private void GposeMetaInfoAction(Action<CharaDataMetaInfoExtendedDto?> gposeActionDraw, string actionDescription, CharaDataMetaInfoExtendedDto? dto, bool hasValidGposeTarget, bool isSpawning)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine(actionDescription);
		bool isDisabled = false;
		if (dto == null)
		{
			if (!isDisabled)
			{
				AddErrorStart(sb);
			}
			sb.AppendLine("- No metainfo present");
			isDisabled = true;
		}
		if ((object)dto != null && !dto.CanBeDownloaded)
		{
			if (!isDisabled)
			{
				AddErrorStart(sb);
			}
			sb.AppendLine("- Character is not downloadable");
			isDisabled = true;
		}
		if (!_uiSharedService.IsInGpose)
		{
			if (!isDisabled)
			{
				AddErrorStart(sb);
			}
			sb.AppendLine("- Requires to be in GPose");
			isDisabled = true;
		}
		if (!hasValidGposeTarget && !isSpawning)
		{
			if (!isDisabled)
			{
				AddErrorStart(sb);
			}
			sb.AppendLine("- Requires a valid GPose target");
			isDisabled = true;
		}
		if (isSpawning && !_charaDataManager.BrioAvailable)
		{
			if (!isDisabled)
			{
				AddErrorStart(sb);
			}
			sb.AppendLine("- Requires Brio to be installed.");
			isDisabled = true;
		}
		using (ImRaii.Group())
		{
			using (ImRaii.Disabled(isDisabled))
			{
				gposeActionDraw(dto);
			}
		}
		if (sb.Length > 0)
		{
			UiSharedService.AttachToolTip(sb.ToString());
		}
		static void AddErrorStart(StringBuilder sb)
		{
			sb.Append("--SEP--");
			sb.AppendLine("Cannot execute:");
		}
	}

	private void GposePoseAction(Action poseActionDraw, string poseDescription, bool hasValidGposeTarget)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine(poseDescription);
		bool isDisabled = false;
		if (!_uiSharedService.IsInGpose)
		{
			if (!isDisabled)
			{
				AddErrorStart(sb);
			}
			sb.AppendLine("- Requires to be in GPose");
			isDisabled = true;
		}
		if (!hasValidGposeTarget)
		{
			if (!isDisabled)
			{
				AddErrorStart(sb);
			}
			sb.AppendLine("- Requires a valid GPose target");
			isDisabled = true;
		}
		if (!_charaDataManager.BrioAvailable)
		{
			if (!isDisabled)
			{
				AddErrorStart(sb);
			}
			sb.AppendLine("- Requires Brio to be installed.");
			isDisabled = true;
		}
		using (ImRaii.Group())
		{
			using (ImRaii.Disabled(isDisabled))
			{
				poseActionDraw();
			}
		}
		if (sb.Length > 0)
		{
			UiSharedService.AttachToolTip(sb.ToString());
		}
		static void AddErrorStart(StringBuilder sb)
		{
			sb.Append("--SEP--");
			sb.AppendLine("Cannot execute:");
		}
	}

	private void SetWindowSizeConstraints(bool? inGposeTab = null)
	{
		base.SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(inGposeTab.GetValueOrDefault() ? 400 : 1000, 500f),
			MaximumSize = new Vector2(inGposeTab.GetValueOrDefault() ? 400 : 1000, 2000f)
		};
	}

	private void UpdateFilteredFavorites()
	{
		Task.Run(async delegate
		{
			if (_charaDataManager.DownloadMetaInfoTask != null)
			{
				await _charaDataManager.DownloadMetaInfoTask.ConfigureAwait(continueOnCapturedContext: false);
			}
			Dictionary<string, (CharaDataFavorite, CharaDataMetaInfoExtendedDto, bool)> newFiltered = new Dictionary<string, (CharaDataFavorite, CharaDataMetaInfoExtendedDto, bool)>();
			foreach (KeyValuePair<string, CharaDataFavorite> favorite in _configService.Current.FavoriteCodes)
			{
				string uid = favorite.Key.Split(":")[0];
				string note = _serverConfigurationManager.GetNoteForUid(uid) ?? string.Empty;
				CharaDataMetaInfoExtendedDto metaInfo;
				bool hasMetaInfo = _charaDataManager.TryGetMetaInfo(favorite.Key, out metaInfo);
				if ((string.IsNullOrEmpty(_filterCodeNote) || note.Contains(_filterCodeNote, StringComparison.OrdinalIgnoreCase) || uid.Contains(_filterCodeNote, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(_filterDescription) || favorite.Value.CustomDescription.Contains(_filterDescription, StringComparison.OrdinalIgnoreCase) || (metaInfo != null && metaInfo.Description.Contains(_filterDescription, StringComparison.OrdinalIgnoreCase))) && (!_filterPoseOnly || (metaInfo != null && metaInfo.HasPoses)) && (!_filterWorldOnly || (metaInfo != null && metaInfo.HasWorldData)))
				{
					newFiltered[favorite.Key] = (favorite.Value, metaInfo, hasMetaInfo);
				}
			}
			_filteredFavorites = newFiltered;
		});
	}

	private void UpdateFilteredItems()
	{
		if (_charaDataManager.GetSharedWithYouTask == null)
		{
			_filteredDict = (from k in (from k in _charaDataManager.SharedWithYouData.SelectMany<KeyValuePair<UserData, List<CharaDataMetaInfoExtendedDto>>, CharaDataMetaInfoExtendedDto>((KeyValuePair<UserData, List<CharaDataMetaInfoExtendedDto>> k) => k.Value)
					where (!_sharedWithYouDownloadableFilter || k.CanBeDownloaded) && (string.IsNullOrEmpty(_sharedWithYouDescriptionFilter) || k.Description.Contains(_sharedWithYouDescriptionFilter, StringComparison.OrdinalIgnoreCase))
					group k by k.Uploader).ToDictionary<IGrouping<UserData, CharaDataMetaInfoExtendedDto>, string, List<CharaDataMetaInfoExtendedDto>>(delegate(IGrouping<UserData, CharaDataMetaInfoExtendedDto> k)
				{
					string noteForUid = _serverConfigurationManager.GetNoteForUid(k.Key.UID);
					return (noteForUid == null) ? k.Key.AliasOrUID : (noteForUid + " (" + k.Key.AliasOrUID + ")");
				}, (IGrouping<UserData, CharaDataMetaInfoExtendedDto> k) => k.ToList(), StringComparer.OrdinalIgnoreCase)
				where string.IsNullOrEmpty(_sharedWithYouOwnerFilter) || k.Key.Contains(_sharedWithYouOwnerFilter, StringComparison.OrdinalIgnoreCase)
				select k).OrderBy<KeyValuePair<string, List<CharaDataMetaInfoExtendedDto>>, string>((KeyValuePair<string, List<CharaDataMetaInfoExtendedDto>> k) => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary();
		}
	}

	private void DrawGposeTogether()
	{
		if (!_charaDataManager.BrioAvailable)
		{
			ImGuiHelpers.ScaledDummy(5f);
			UiSharedService.DrawGroupedCenteredColorText("BRIO IS MANDATORY FOR GPOSE TOGETHER.", ImGuiColors.DalamudRed);
			ImGuiHelpers.ScaledDummy(5f);
		}
		if (!_uiSharedService.ApiController.IsConnected)
		{
			ImGuiHelpers.ScaledDummy(5f);
			UiSharedService.DrawGroupedCenteredColorText("CANNOT USE GPOSE TOGETHER WHILE DISCONNECTED FROM THE SERVER.", ImGuiColors.DalamudRed);
			ImGuiHelpers.ScaledDummy(5f);
		}
		_uiSharedService.BigText("GPose Together");
		DrawHelpFoldout("GPose together is a way to do multiplayer GPose sessions and collaborations." + UiSharedService.DoubleNewLine + "GPose together requires Brio to function. Only Brio is also supported for the actual posing interactions. Attempting to pose using other tools will lead to conflicts and exploding characters." + UiSharedService.DoubleNewLine + "To use GPose together you either create or join a GPose Together Lobby. After you and other people have joined, make sure that everyone is on the same map. It is not required for you to be on the same server, DC or instance. Users that are on the same map will be drawn as moving purple wisps in the overworld, so you can easily find each other." + UiSharedService.DoubleNewLine + "Once you are close to each other you can initiate GPose. You must either assign or spawn characters for each of the lobby users. Their own poses and positions to their character will be automatically applied." + Environment.NewLine + "Pose and location data during GPose are updated approximately every 10-20s.");
		using (ImRaii.Disabled(!_charaDataManager.BrioAvailable || !_uiSharedService.ApiController.IsConnected))
		{
			UiSharedService.DistanceSeparator();
			_uiSharedService.BigText("Lobby Controls");
			if (string.IsNullOrEmpty(_charaDataGposeTogetherManager.CurrentGPoseLobbyId))
			{
				if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Create New GPose Together Lobby"))
				{
					_charaDataGposeTogetherManager.CreateNewLobby();
				}
				ImGuiHelpers.ScaledDummy(5f);
				UiSharedService.ScaledNextItemWidth(250f);
				ImGui.InputTextWithHint("##lobbyId", "GPose Lobby Id", ref _joinLobbyId, 30);
				if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Join GPose Together Lobby"))
				{
					_charaDataGposeTogetherManager.JoinGPoseLobby(_joinLobbyId);
					_joinLobbyId = string.Empty;
				}
				if (!string.IsNullOrEmpty(_charaDataGposeTogetherManager.LastGPoseLobbyId) && _uiSharedService.IconTextButton(FontAwesomeIcon.LongArrowAltRight, "Rejoin Last Lobby " + _charaDataGposeTogetherManager.LastGPoseLobbyId))
				{
					_charaDataGposeTogetherManager.JoinGPoseLobby(_charaDataGposeTogetherManager.LastGPoseLobbyId);
				}
			}
			else
			{
				ImGui.AlignTextToFramePadding();
				ImGui.TextUnformatted("GPose Lobby");
				ImGui.SameLine();
				UiSharedService.ColorTextWrapped(_charaDataGposeTogetherManager.CurrentGPoseLobbyId, ImGuiColors.ParsedGreen);
				ImGui.SameLine();
				if (_uiSharedService.IconButton(FontAwesomeIcon.Clipboard))
				{
					ImGui.SetClipboardText(_charaDataGposeTogetherManager.CurrentGPoseLobbyId);
				}
				UiSharedService.AttachToolTip("Copy Lobby ID to clipboard.");
				using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
				{
					if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowLeft, "Leave GPose Lobby"))
					{
						_charaDataGposeTogetherManager.LeaveGPoseLobby();
					}
				}
				UiSharedService.AttachToolTip("Leave the current GPose lobby.--SEP--Hold CTRL and click to leave.");
			}
			UiSharedService.DistanceSeparator();
			using (ImRaii.Disabled(string.IsNullOrEmpty(_charaDataGposeTogetherManager.CurrentGPoseLobbyId)))
			{
				if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowUp, "Send Updated Character Data"))
				{
					_charaDataGposeTogetherManager.PushCharacterDownloadDto();
				}
				UiSharedService.AttachToolTip("This will send your current appearance, pose and world data to all users in the lobby.");
				if (!_uiSharedService.IsInGpose)
				{
					ImGuiHelpers.ScaledDummy(5f);
					UiSharedService.DrawGroupedCenteredColorText("Assigning users to characters is only available in GPose.", ImGuiColors.DalamudYellow, 300f);
				}
				UiSharedService.DistanceSeparator();
				ImGui.TextUnformatted("Users In Lobby");
				IEnumerable<ICharacter> gposeCharas = _dalamudUtilService.GetGposeCharactersFromObjectTable();
				IPlayerCharacter self = _dalamudUtilService.GetPlayerCharacter();
				gposeCharas = gposeCharas.Where((ICharacter c) => c != null && !string.Equals(c.Name.TextValue, self.Name.TextValue, StringComparison.Ordinal)).ToList();
				using (ImRaii.Child("charaChild", new Vector2(0f, 0f), border: false, ImGuiWindowFlags.AlwaysAutoResize))
				{
					ImGuiHelpers.ScaledDummy(3f);
					if (!_charaDataGposeTogetherManager.UsersInLobby.Any() && !string.IsNullOrEmpty(_charaDataGposeTogetherManager.CurrentGPoseLobbyId))
					{
						UiSharedService.DrawGroupedCenteredColorText("No other users in current GPose lobby", ImGuiColors.DalamudYellow);
						return;
					}
					foreach (GposeLobbyUserData user in _charaDataGposeTogetherManager.UsersInLobby)
					{
						DrawLobbyUser(user, gposeCharas);
					}
				}
			}
		}
	}

	private void DrawLobbyUser(GposeLobbyUserData user, IEnumerable<ICharacter?> gposeCharas)
	{
		using (ImRaii.PushId(user.UserData.UID))
		{
			using (ImRaii.PushIndent(5f))
			{
				(bool SameMap, bool SameServer, bool SameEverything) sameMapAndServer = _charaDataGposeTogetherManager.IsOnSameMapAndServer(user);
				float width = ImGui.GetContentRegionAvail().X - 5f;
				UiSharedService.DrawGrouped(delegate
				{
					float x = ImGui.GetContentRegionAvail().X;
					ImGui.AlignTextToFramePadding();
					string noteForUid = _serverConfigurationManager.GetNoteForUid(user.UserData.UID);
					UiSharedService.ColorText((noteForUid == null) ? user.UserData.AliasOrUID : (noteForUid + " (" + user.UserData.AliasOrUID + ")"), ImGuiColors.ParsedGreen);
					float x2 = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight).X;
					float x3 = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus).X;
					ImGui.SameLine();
					ImGui.SetCursorPosX(x - (x2 + x3 + ImGui.GetStyle().ItemSpacing.X));
					using (ImRaii.Disabled(!_uiSharedService.IsInGpose || user.CharaData == null || user.Address == IntPtr.Zero))
					{
						if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight))
						{
							_charaDataGposeTogetherManager.ApplyCharaData(user);
						}
					}
					UiSharedService.AttachToolTip("Apply newly received character data to selected actor.--SEP--Note: If the button is grayed out, the latest data has already been applied.");
					ImGui.SameLine();
					using (ImRaii.Disabled(!_uiSharedService.IsInGpose || user.CharaData == null || sameMapAndServer.SameEverything))
					{
						if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
						{
							_charaDataGposeTogetherManager.SpawnAndApplyData(user);
						}
					}
					UiSharedService.AttachToolTip("Spawn new actor, apply character data and and assign it to this user.--SEP--Note: If the button is grayed out, the user has not sent any character data or you are on the same map, server and instance. If the latter is the case, join a group with that user and assign the character to them.");
					using (ImRaii.Group())
					{
						UiSharedService.ColorText("Map Info", ImGuiColors.DalamudGrey);
						ImGui.SameLine();
						_uiSharedService.IconText(FontAwesomeIcon.ExternalLinkSquareAlt, ImGuiColors.DalamudGrey);
					}
					UiSharedService.AttachToolTip(user.WorldDataDescriptor + "--SEP--");
					ImGui.SameLine();
					_uiSharedService.IconText(FontAwesomeIcon.Map, sameMapAndServer.SameMap ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed);
					if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && user.WorldData.HasValue)
					{
						_dalamudUtilService.SetMarkerAndOpenMap(new Vector3(user.WorldData.Value.PositionX, user.WorldData.Value.PositionY, user.WorldData.Value.PositionZ), user.Map);
					}
					UiSharedService.AttachToolTip((sameMapAndServer.SameMap ? "You are on the same map." : "You are not on the same map.") + "--SEP--Note: Click to open the users location on your map." + Environment.NewLine + "Note: For GPose synchronization to work properly, you must be on the same map.");
					ImGui.SameLine();
					_uiSharedService.IconText(FontAwesomeIcon.Globe, sameMapAndServer.SameServer ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed);
					UiSharedService.AttachToolTip((sameMapAndServer.SameMap ? "You are on the same server." : "You are not on the same server.") + "--SEP--Note: GPose synchronization is not dependent on the current server, but you will have to spawn a character for the other lobby users.");
					ImGui.SameLine();
					_uiSharedService.IconText(FontAwesomeIcon.Running, sameMapAndServer.SameEverything ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed);
					UiSharedService.AttachToolTip(sameMapAndServer.SameEverything ? "You are in the same instanced area." : ("You are not the same instanced area.--SEP--Note: Users not in your instance, but on the same map, will be drawn as floating wisps." + Environment.NewLine + "Note: GPose synchronization is not dependent on the current instance, but you will have to spawn a character for the other lobby users."));
					using (ImRaii.Disabled(!_uiSharedService.IsInGpose))
					{
						UiSharedService.ScaledNextItemWidth(200f);
						using (ImRaii.IEndObject endObject2 = ImRaii.Combo("##character", string.IsNullOrEmpty(user.AssociatedCharaName) ? "No character assigned" : CharaName(user.AssociatedCharaName)))
						{
							if (endObject2)
							{
								foreach (ICharacter current in gposeCharas)
								{
									if (current != null && ImGui.Selectable(CharaName(current.Name.TextValue), current.Address == user.Address))
									{
										user.AssociatedCharaName = current.Name.TextValue;
										user.Address = current.Address;
									}
								}
							}
						}
						ImGui.SameLine();
						using (ImRaii.Disabled(user.Address == IntPtr.Zero))
						{
							if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
							{
								user.AssociatedCharaName = string.Empty;
								user.Address = IntPtr.Zero;
							}
						}
						UiSharedService.AttachToolTip("Unassign Actor for this user");
						if (_uiSharedService.IsInGpose && user.Address == IntPtr.Zero)
						{
							ImGui.SameLine();
							_uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudRed);
							UiSharedService.AttachToolTip("No valid character assigned for this user. Pose data will not be applied.");
						}
					}
				}, 5f, width);
				ImGuiHelpers.ScaledDummy(5f);
			}
		}
	}

	private void DrawEditCharaData(CharaDataFullExtendedDto? dataDto)
	{
		using (ImRaii.PushId(dataDto?.Id ?? "NoData"))
		{
			if (dataDto == null)
			{
				ImGuiHelpers.ScaledDummy(5f);
				UiSharedService.DrawGroupedCenteredColorText("Select an entry above to edit its data.", ImGuiColors.DalamudYellow);
				return;
			}
			CharaDataExtendedUpdateDto updateDto = _charaDataManager.GetUpdateDto(dataDto.Id);
			if (updateDto == null)
			{
				UiSharedService.DrawGroupedCenteredColorText("Something went awfully wrong and there's no update DTO. Try updating Character Data via the button above.", ImGuiColors.DalamudYellow);
				return;
			}
			int otherUpdates = 0;
			foreach (CharaDataFullExtendedDto item in _charaDataManager.OwnCharaData.Values.Where((CharaDataFullExtendedDto v) => !string.Equals(v.Id, dataDto.Id, StringComparison.Ordinal)))
			{
				CharaDataExtendedUpdateDto? updateDto2 = _charaDataManager.GetUpdateDto(item.Id);
				if ((object)updateDto2 != null && updateDto2.HasChanges)
				{
					otherUpdates++;
				}
			}
			bool canUpdate = updateDto.HasChanges;
			if (!canUpdate && otherUpdates <= 0)
			{
				Task? charaUpdateTask = _charaDataManager.CharaUpdateTask;
				if (charaUpdateTask == null || charaUpdateTask.IsCompleted)
				{
					goto IL_0197;
				}
			}
			ImGuiHelpers.ScaledDummy(5f);
			goto IL_0197;
			IL_0197:
			ImRaii.Indent indent = ImRaii.PushIndent(10f);
			if (canUpdate || _charaDataManager.UploadTask != null)
			{
				ImGuiHelpers.ScaledDummy(5f);
				UiSharedService.DrawGrouped(delegate
				{
					if (canUpdate)
					{
						ImGui.AlignTextToFramePadding();
						UiSharedService.ColorTextWrapped("Warning: You have unsaved changes!", ImGuiColors.DalamudRed);
						ImGui.SameLine();
						using (ImRaii.Disabled(_charaDataManager.CharaUpdateTask != null && !_charaDataManager.CharaUpdateTask.IsCompleted))
						{
							if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleUp, "Save to Server"))
							{
								_charaDataManager.UploadCharaData(dataDto.Id);
							}
							ImGui.SameLine();
							if (_uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "Undo all changes"))
							{
								updateDto.UndoChanges();
							}
						}
						if (_charaDataManager.CharaUpdateTask != null && !_charaDataManager.CharaUpdateTask.IsCompleted)
						{
							UiSharedService.ColorTextWrapped("Updating data on server, please wait.", ImGuiColors.DalamudYellow);
						}
					}
					Task<(string Output, bool Success)>? uploadTask = _charaDataManager.UploadTask;
					if (uploadTask != null && !uploadTask.IsCompleted)
					{
						DisableDisabled(delegate
						{
							if (_charaDataManager.UploadProgress != null)
							{
								UiSharedService.ColorTextWrapped(_charaDataManager.UploadProgress.Value ?? string.Empty, ImGuiColors.DalamudYellow);
							}
							Task<(string Output, bool Success)>? uploadTask3 = _charaDataManager.UploadTask;
							if (uploadTask3 != null && !uploadTask3.IsCompleted && _uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Cancel Upload"))
							{
								_charaDataManager.CancelUpload();
							}
							else
							{
								Task<(string Output, bool Success)>? uploadTask4 = _charaDataManager.UploadTask;
								if (uploadTask4 != null && uploadTask4.IsCompleted)
								{
									Vector4 boolColor2 = UiSharedService.GetBoolColor(_charaDataManager.UploadTask.Result.Success);
									UiSharedService.ColorTextWrapped(_charaDataManager.UploadTask.Result.Output, boolColor2);
								}
							}
						});
					}
					else
					{
						Task<(string Output, bool Success)>? uploadTask2 = _charaDataManager.UploadTask;
						if (uploadTask2 != null && uploadTask2.IsCompleted)
						{
							Vector4 boolColor = UiSharedService.GetBoolColor(_charaDataManager.UploadTask.Result.Success);
							UiSharedService.ColorTextWrapped(_charaDataManager.UploadTask.Result.Output, boolColor);
						}
					}
				});
			}
			if (otherUpdates > 0)
			{
				ImGuiHelpers.ScaledDummy(5f);
				UiSharedService.DrawGrouped(delegate
				{
					ImGui.AlignTextToFramePadding();
					UiSharedService.ColorTextWrapped($"You have {otherUpdates} other entries with unsaved changes.", ImGuiColors.DalamudYellow);
					ImGui.SameLine();
					using (ImRaii.Disabled(_charaDataManager.CharaUpdateTask != null && !_charaDataManager.CharaUpdateTask.IsCompleted))
					{
						if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowAltCircleUp, "Save all to server"))
						{
							_charaDataManager.UploadAllCharaData();
						}
					}
				});
			}
			indent.Dispose();
			if (!canUpdate && otherUpdates <= 0)
			{
				Task? charaUpdateTask2 = _charaDataManager.CharaUpdateTask;
				if (charaUpdateTask2 == null || charaUpdateTask2.IsCompleted)
				{
					goto IL_024f;
				}
			}
			ImGuiHelpers.ScaledDummy(5f);
			goto IL_024f;
			IL_024f:
			using (ImRaii.Child("editChild", new Vector2(0f, 0f), border: false, ImGuiWindowFlags.AlwaysAutoResize))
			{
				DrawEditCharaDataGeneral(dataDto, updateDto);
				ImGuiHelpers.ScaledDummy(5f);
				DrawEditCharaDataAccessAndSharing(updateDto);
				ImGuiHelpers.ScaledDummy(5f);
				DrawEditCharaDataAppearance(dataDto, updateDto);
				ImGuiHelpers.ScaledDummy(5f);
				DrawEditCharaDataPoses(updateDto);
			}
		}
	}

	private void DrawEditCharaDataAccessAndSharing(CharaDataExtendedUpdateDto updateDto)
	{
		_uiSharedService.BigText("Access and Sharing");
		UiSharedService.ScaledNextItemWidth(200f);
		AccessTypeDto dtoAccessType = updateDto.AccessType;
		if (ImGui.BeginCombo("Access Restrictions", GetAccessTypeString(dtoAccessType)))
		{
			foreach (AccessTypeDto accessType in Enum.GetValues(typeof(AccessTypeDto)).Cast<AccessTypeDto>())
			{
				if (ImGui.Selectable(GetAccessTypeString(accessType), accessType == dtoAccessType))
				{
					updateDto.AccessType = accessType;
				}
			}
			ImGui.EndCombo();
		}
		_uiSharedService.DrawHelpText("You can control who has access to your character data based on the access restrictions.--SEP--Specified: Only people and syncshells you directly specify in 'Specific Individuals / Syncshells' can access this character data" + Environment.NewLine + "Direct Pairs: Only people you have directly paired can access this character data" + Environment.NewLine + "All Pairs: All people you have paired can access this character data" + Environment.NewLine + "Everyone: Everyone can access this character data--SEP--Note: To access your character data the person in question requires to have the code. Exceptions for 'Shared' data, see 'Sharing' below." + Environment.NewLine + "Note: For 'Direct' and 'All Pairs' the pause state plays a role. Paused people will not be able to access your character data." + Environment.NewLine + "Note: Directly specified Individuals or Syncshells in the 'Specific Individuals / Syncshells' list will be able to access your character data regardless of pause or pair state.");
		DrawSpecific(updateDto);
		UiSharedService.ScaledNextItemWidth(200f);
		ShareTypeDto dtoShareType = updateDto.ShareType;
		if (ImGui.BeginCombo("Sharing", GetShareTypeString(dtoShareType)))
		{
			foreach (ShareTypeDto shareType in Enum.GetValues(typeof(ShareTypeDto)).Cast<ShareTypeDto>())
			{
				if (ImGui.Selectable(GetShareTypeString(shareType), shareType == dtoShareType))
				{
					updateDto.ShareType = shareType;
				}
			}
			ImGui.EndCombo();
		}
		_uiSharedService.DrawHelpText("This regulates how you want to distribute this character data.--SEP--Code Only: People require to have the code to download this character data" + Environment.NewLine + "Shared: People that are allowed through 'Access Restrictions' will have this character data entry displayed in 'Shared with You' (it can also be accessed through the code)--SEP--Note: Shared with Access Restriction 'Everyone' is the same as shared with Access Restriction 'All Pairs', it will not show up for everyone but just your pairs.");
		ImGuiHelpers.ScaledDummy(10f);
	}

	private void DrawEditCharaDataAppearance(CharaDataFullExtendedDto dataDto, CharaDataExtendedUpdateDto updateDto)
	{
		_uiSharedService.BigText("Appearance");
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Set Appearance to Current Appearance"))
		{
			_charaDataManager.SetAppearanceData(dataDto.Id);
		}
		_uiSharedService.DrawHelpText("This will overwrite the appearance data currently stored in this Character Data entry with your current appearance.");
		ImGui.SameLine();
		using (ImRaii.Disabled(dataDto.HasMissingFiles || !updateDto.IsAppearanceEqual || _charaDataManager.DataApplicationTask != null))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.CheckCircle, "Preview Saved Apperance on Self"))
			{
				_charaDataManager.ApplyDataToSelf(dataDto);
			}
		}
		_uiSharedService.DrawHelpText("This will download and apply the saved character data to yourself. Once loaded it will automatically revert itself within 15 seconds.--SEP--Note: Weapons will not be displayed correctly unless using the same job as the saved data.");
		ImGui.TextUnformatted("Contains Glamourer Data");
		ImGui.SameLine();
		bool hasGlamourerdata = !string.IsNullOrEmpty(updateDto.GlamourerData);
		UiSharedService.ScaledSameLine(200f);
		_uiSharedService.BooleanToColoredIcon(hasGlamourerdata, inline: false);
		ImGui.TextUnformatted("Contains Files");
		bool hasFiles = (updateDto.FileGamePaths ?? new List<GamePathEntry>()).Any() || dataDto.OriginalFiles.Any();
		UiSharedService.ScaledSameLine(200f);
		_uiSharedService.BooleanToColoredIcon(hasFiles, inline: false);
		if (hasFiles && updateDto.IsAppearanceEqual)
		{
			ImGui.SameLine();
			ImGuiHelpers.ScaledDummy(20f, 1f);
			ImGui.SameLine();
			float pos = ImGui.GetCursorPosX();
			ImGui.NewLine();
			ImGui.SameLine(pos);
			ImU8String text = new ImU8String(51, 2);
			text.AppendFormatted(dataDto.FileGamePaths.DistinctBy((GamePathEntry k) => k.HashOrFileSwap).Count());
			text.AppendLiteral(" unique file hashes (original upload: ");
			text.AppendFormatted(dataDto.OriginalFiles.DistinctBy((GamePathEntry k) => k.HashOrFileSwap).Count());
			text.AppendLiteral(" file hashes)");
			ImGui.TextUnformatted(text);
			ImGui.NewLine();
			ImGui.SameLine(pos);
			text = new ImU8String(22, 1);
			text.AppendFormatted(dataDto.FileGamePaths.Count);
			text.AppendLiteral(" associated game paths");
			ImGui.TextUnformatted(text);
			ImGui.NewLine();
			ImGui.SameLine(pos);
			text = new ImU8String(11, 1);
			text.AppendFormatted(dataDto.FileSwaps.Count);
			text.AppendLiteral(" file swaps");
			ImGui.TextUnformatted(text);
			ImGui.NewLine();
			ImGui.SameLine(pos);
			if (!dataDto.HasMissingFiles)
			{
				UiSharedService.ColorTextWrapped("All files to download this character data are present on the server", ImGuiColors.HealerGreen);
			}
			else
			{
				UiSharedService.ColorTextWrapped($"{dataDto.MissingFiles.DistinctBy((GamePathEntry k) => k.HashOrFileSwap).Count()} files to download this character data are missing on the server", ImGuiColors.DalamudRed);
				ImGui.NewLine();
				ImGui.SameLine(pos);
				if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleUp, "Attempt to upload missing files and restore Character Data"))
				{
					_charaDataManager.UploadMissingFiles(dataDto.Id);
				}
			}
		}
		else if (hasFiles && !updateDto.IsAppearanceEqual)
		{
			ImGui.SameLine();
			ImGuiHelpers.ScaledDummy(20f, 1f);
			ImGui.SameLine();
			UiSharedService.ColorTextWrapped("New data was set. It may contain files that require to be uploaded (will happen on Saving to server)", ImGuiColors.DalamudYellow);
		}
		ImGui.TextUnformatted("Contains Manipulation Data");
		bool hasManipData = !string.IsNullOrEmpty(updateDto.ManipulationData);
		UiSharedService.ScaledSameLine(200f);
		_uiSharedService.BooleanToColoredIcon(hasManipData, inline: false);
		ImGui.TextUnformatted("Contains Customize+ Data");
		ImGui.SameLine();
		bool hasCustomizeData = !string.IsNullOrEmpty(updateDto.CustomizeData);
		UiSharedService.ScaledSameLine(200f);
		_uiSharedService.BooleanToColoredIcon(hasCustomizeData, inline: false);
	}

	private void DrawEditCharaDataGeneral(CharaDataFullExtendedDto dataDto, CharaDataExtendedUpdateDto updateDto)
	{
		_uiSharedService.BigText("General");
		string code = dataDto.FullId;
		using (ImRaii.Disabled())
		{
			UiSharedService.ScaledNextItemWidth(200f);
			ImGui.InputText("##CharaDataCode", ref code, 255, ImGuiInputTextFlags.ReadOnly);
		}
		ImGui.SameLine();
		ImGui.TextUnformatted("Chara Data Code");
		ImGui.SameLine();
		if (_uiSharedService.IconButton(FontAwesomeIcon.Copy))
		{
			ImGui.SetClipboardText(code);
		}
		UiSharedService.AttachToolTip("Copy Code to Clipboard");
		string creationTime = dataDto.CreatedDate.ToLocalTime().ToString();
		string updateTime = dataDto.UpdatedDate.ToLocalTime().ToString();
		string downloadCount = dataDto.DownloadCount.ToString();
		using (ImRaii.Disabled())
		{
			UiSharedService.ScaledNextItemWidth(200f);
			ImGui.InputText("##CreationDate", ref creationTime, 255, ImGuiInputTextFlags.ReadOnly);
		}
		ImGui.SameLine();
		ImGui.TextUnformatted("Creation Date");
		ImGui.SameLine();
		ImGuiHelpers.ScaledDummy(20f);
		ImGui.SameLine();
		using (ImRaii.Disabled())
		{
			UiSharedService.ScaledNextItemWidth(200f);
			ImGui.InputText("##LastUpdate", ref updateTime, 255, ImGuiInputTextFlags.ReadOnly);
		}
		ImGui.SameLine();
		ImGui.TextUnformatted("Last Update Date");
		ImGui.SameLine();
		ImGuiHelpers.ScaledDummy(23f);
		ImGui.SameLine();
		using (ImRaii.Disabled())
		{
			UiSharedService.ScaledNextItemWidth(50f);
			ImGui.InputText("##DlCount", ref downloadCount, 255, ImGuiInputTextFlags.ReadOnly);
		}
		ImGui.SameLine();
		ImGui.TextUnformatted("Download Count");
		string description = updateDto.Description;
		UiSharedService.ScaledNextItemWidth(735f);
		if (ImGui.InputText("##Description", ref description, 200))
		{
			updateDto.Description = description;
		}
		ImGui.SameLine();
		ImGui.TextUnformatted("Description");
		_uiSharedService.DrawHelpText("Description for this Character Data.--SEP--Note: the description will be visible to anyone who can access this character data. See 'Access Restrictions' and 'Sharing' below.");
		DateTime expiryDate = updateDto.ExpiryDate;
		bool isExpiring = expiryDate != DateTime.MaxValue;
		if (ImGui.Checkbox("Expires", ref isExpiring))
		{
			updateDto.SetExpiry(isExpiring);
		}
		_uiSharedService.DrawHelpText("If expiration is enabled, the uploaded character data will be automatically deleted from the server at the specified date.");
		using (ImRaii.Disabled(!isExpiring))
		{
			ImGui.SameLine();
			UiSharedService.ScaledNextItemWidth(100f);
			if (ImGui.BeginCombo("Year", expiryDate.Year.ToString()))
			{
				for (int year = DateTime.UtcNow.Year; year < DateTime.UtcNow.Year + 4; year++)
				{
					if (ImGui.Selectable(year.ToString(), year == expiryDate.Year))
					{
						updateDto.SetExpiry(year, expiryDate.Month, expiryDate.Day);
					}
				}
				ImGui.EndCombo();
			}
			ImGui.SameLine();
			int daysInMonth = DateTime.DaysInMonth(expiryDate.Year, expiryDate.Month);
			UiSharedService.ScaledNextItemWidth(100f);
			if (ImGui.BeginCombo("Month", expiryDate.Month.ToString()))
			{
				for (int month = 1; month <= 12; month++)
				{
					if (ImGui.Selectable(month.ToString(), month == expiryDate.Month))
					{
						updateDto.SetExpiry(expiryDate.Year, month, expiryDate.Day);
					}
				}
				ImGui.EndCombo();
			}
			ImGui.SameLine();
			UiSharedService.ScaledNextItemWidth(100f);
			if (ImGui.BeginCombo("Day", expiryDate.Day.ToString()))
			{
				for (int day = 1; day <= daysInMonth; day++)
				{
					if (ImGui.Selectable(day.ToString(), day == expiryDate.Day))
					{
						updateDto.SetExpiry(expiryDate.Year, expiryDate.Month, day);
					}
				}
				ImGui.EndCombo();
			}
		}
		ImGuiHelpers.ScaledDummy(5f);
		using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Character Data"))
			{
				_charaDataManager.DeleteCharaData(dataDto);
				SelectedDtoId = string.Empty;
			}
		}
		if (!UiSharedService.CtrlPressed())
		{
			UiSharedService.AttachToolTip("Hold CTRL and click to delete the current data. This operation is irreversible.");
		}
	}

	private void DrawEditCharaDataPoses(CharaDataExtendedUpdateDto updateDto)
	{
		_uiSharedService.BigText("Poses");
		int poseCount = updateDto.PoseList.Count();
		using (ImRaii.Disabled(poseCount >= 10))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Add new Pose"))
			{
				updateDto.AddPose();
			}
		}
		ImGui.SameLine();
		using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow, poseCount == 10))
		{
			ImU8String text = new ImU8String(16, 2);
			text.AppendFormatted(poseCount);
			text.AppendLiteral("/");
			text.AppendFormatted(10);
			text.AppendLiteral(" poses attached");
			ImGui.TextUnformatted(text);
		}
		ImGuiHelpers.ScaledDummy(5f);
		using (ImRaii.PushIndent(10f))
		{
			int poseNumber = 1;
			if (!_uiSharedService.IsInGpose && _charaDataManager.BrioAvailable)
			{
				ImGuiHelpers.ScaledDummy(5f);
				UiSharedService.DrawGroupedCenteredColorText("To attach pose and world data you need to be in GPose.", ImGuiColors.DalamudYellow);
				ImGuiHelpers.ScaledDummy(5f);
			}
			else if (!_charaDataManager.BrioAvailable)
			{
				ImGuiHelpers.ScaledDummy(5f);
				UiSharedService.DrawGroupedCenteredColorText("To attach pose and world data Brio requires to be installed.", ImGuiColors.DalamudRed);
				ImGuiHelpers.ScaledDummy(5f);
			}
			foreach (PoseEntry pose in updateDto.PoseList)
			{
				ImGui.AlignTextToFramePadding();
				using (ImRaii.PushId("pose" + poseNumber))
				{
					ImGui.TextUnformatted(poseNumber.ToString());
					if (!pose.Id.HasValue)
					{
						UiSharedService.ScaledSameLine(50f);
						_uiSharedService.IconText(FontAwesomeIcon.Plus, ImGuiColors.DalamudYellow);
						UiSharedService.AttachToolTip("This pose has not been added to the server yet. Save changes to upload this Pose data.");
					}
					bool poseHasChanges = updateDto.PoseHasChanges(pose);
					if (poseHasChanges)
					{
						UiSharedService.ScaledSameLine(50f);
						_uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow);
						UiSharedService.AttachToolTip("This pose has changes that have not been saved to the server yet.");
					}
					UiSharedService.ScaledSameLine(75f);
					if (pose.Description == null && !pose.WorldData.HasValue && pose.PoseData == null)
					{
						UiSharedService.ColorText("Pose scheduled for deletion", ImGuiColors.DalamudYellow);
						goto IL_069f;
					}
					string desc = pose.Description;
					if (ImGui.InputTextWithHint("##description", "Description", ref desc, 100))
					{
						pose.Description = desc;
						updateDto.UpdatePoseList();
					}
					ImGui.SameLine();
					if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete"))
					{
						updateDto.RemovePose(pose);
					}
					ImGui.SameLine();
					ImGuiHelpers.ScaledDummy(10f, 1f);
					ImGui.SameLine();
					bool hasPoseData = !string.IsNullOrEmpty(pose.PoseData);
					_uiSharedService.IconText(FontAwesomeIcon.Running, UiSharedService.GetBoolColor(hasPoseData));
					UiSharedService.AttachToolTip(hasPoseData ? "This Pose entry has pose data attached" : "This Pose entry has no pose data attached");
					ImGui.SameLine();
					int disabled;
					if (_uiSharedService.IsInGpose)
					{
						Task? attachingPoseTask = _charaDataManager.AttachingPoseTask;
						if (attachingPoseTask == null || attachingPoseTask.IsCompleted)
						{
							disabled = ((!_charaDataManager.BrioAvailable) ? 1 : 0);
							goto IL_0371;
						}
					}
					disabled = 1;
					goto IL_0371;
					IL_05a1:
					int disabled2;
					using (ImRaii.Disabled((byte)disabled2 != 0))
					{
						using (ImRaii.PushId("worldSet" + poseNumber))
						{
							if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
							{
								_charaDataManager.AttachWorldData(pose, updateDto);
							}
							UiSharedService.AttachToolTip("Apply current world position data to pose");
						}
					}
					ImGui.SameLine();
					bool hasWorldData;
					using (ImRaii.Disabled(!hasWorldData))
					{
						using (ImRaii.PushId("worldDelete" + poseNumber))
						{
							if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
							{
								pose.WorldData = default(WorldData);
								updateDto.UpdatePoseList();
							}
							UiSharedService.AttachToolTip("Delete current world position data from pose");
						}
					}
					goto IL_069f;
					IL_069f:
					if (poseHasChanges)
					{
						ImGui.SameLine();
						if (_uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "Undo"))
						{
							updateDto.RevertDeletion(pose);
						}
					}
					poseNumber++;
					goto end_IL_01a3;
					IL_0371:
					using (ImRaii.Disabled((byte)disabled != 0))
					{
						using (ImRaii.PushId("poseSet" + poseNumber))
						{
							if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
							{
								_charaDataManager.AttachPoseData(pose, updateDto);
							}
							UiSharedService.AttachToolTip("Apply current pose data to pose");
						}
					}
					ImGui.SameLine();
					using (ImRaii.Disabled(!hasPoseData))
					{
						using (ImRaii.PushId("poseDelete" + poseNumber))
						{
							if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
							{
								pose.PoseData = string.Empty;
								updateDto.UpdatePoseList();
							}
							UiSharedService.AttachToolTip("Delete current pose data from pose");
						}
					}
					ImGui.SameLine();
					ImGuiHelpers.ScaledDummy(10f, 1f);
					ImGui.SameLine();
					WorldData? worldData = pose.WorldData;
					hasWorldData = worldData.GetValueOrDefault() != default(WorldData);
					_uiSharedService.IconText(FontAwesomeIcon.Globe, UiSharedService.GetBoolColor(hasWorldData));
					string tooltipText = ((!hasWorldData) ? "This Pose has no world data attached." : "This Pose has world data attached.");
					if (hasWorldData)
					{
						tooltipText += "--SEP--Click to show location on map";
					}
					UiSharedService.AttachToolTip(tooltipText);
					if (hasWorldData && ImGui.IsItemClicked(ImGuiMouseButton.Left))
					{
						_dalamudUtilService.SetMarkerAndOpenMap(new Vector3(worldData.Value.PositionX, worldData.Value.PositionY, worldData.Value.PositionZ), _dalamudUtilService.MapData.Value[worldData.Value.LocationInfo.MapId].Map);
					}
					ImGui.SameLine();
					if (_uiSharedService.IsInGpose)
					{
						Task? attachingPoseTask2 = _charaDataManager.AttachingPoseTask;
						if (attachingPoseTask2 == null || attachingPoseTask2.IsCompleted)
						{
							disabled2 = ((!_charaDataManager.BrioAvailable) ? 1 : 0);
							goto IL_05a1;
						}
					}
					disabled2 = 1;
					goto IL_05a1;
					end_IL_01a3:;
				}
			}
		}
	}

	private void DrawMcdOnline()
	{
		_uiSharedService.BigText("Mare Character Data Online");
		DrawHelpFoldout("In this tab you can create, view and edit your own Mare Character Data that is stored on the server." + Environment.NewLine + Environment.NewLine + "Mare Character Data Online functions similar to the previous MCDF standard for exporting your character, except that you do not have to send a file to the other person but solely a code." + Environment.NewLine + Environment.NewLine + "There would be a bit too much to explain here on what you can do here in its entirety, however, all elements in this tab have help texts attached what they are used for. Please review them carefully." + Environment.NewLine + Environment.NewLine + "Be mindful that when you share your Character Data with other people there is a chance that, with the help of unsanctioned 3rd party plugins, your appearance could be stolen irreversibly, just like when using MCDF.");
		ImGuiHelpers.ScaledDummy(5f);
		Task<List<CharaDataFullExtendedDto>>? getAllDataTask = _charaDataManager.GetAllDataTask;
		using (ImRaii.Disabled((getAllDataTask != null && !getAllDataTask.IsCompleted) || (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Download your Character Data from Server"))
			{
				_charaDataManager.GetAllData(_disposalCts.Token);
			}
		}
		if (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)
		{
			UiSharedService.AttachToolTip("You can only refresh all character data from server every minute. Please wait.");
		}
		using (ImRaii.IEndObject table = ImRaii.Table("Own Character Data", 12, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY, new Vector2(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X, 140f * ImGuiHelpers.GlobalScale)))
		{
			if (table)
			{
				ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 18f * ImGuiHelpers.GlobalScale);
				ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 18f * ImGuiHelpers.GlobalScale);
				ImGui.TableSetupColumn("Code");
				ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
				ImGui.TableSetupColumn("Created");
				ImGui.TableSetupColumn("Updated");
				ImGui.TableSetupColumn("Download Count", ImGuiTableColumnFlags.WidthFixed, 18f * ImGuiHelpers.GlobalScale);
				ImGui.TableSetupColumn("Downloadable", ImGuiTableColumnFlags.WidthFixed, 18f * ImGuiHelpers.GlobalScale);
				ImGui.TableSetupColumn("Files", ImGuiTableColumnFlags.WidthFixed, 32f * ImGuiHelpers.GlobalScale);
				ImGui.TableSetupColumn("Glamourer", ImGuiTableColumnFlags.WidthFixed, 18f * ImGuiHelpers.GlobalScale);
				ImGui.TableSetupColumn("Customize+", ImGuiTableColumnFlags.WidthFixed, 18f * ImGuiHelpers.GlobalScale);
				ImGui.TableSetupColumn("Expires", ImGuiTableColumnFlags.WidthFixed, 18f * ImGuiHelpers.GlobalScale);
				ImGui.TableSetupScrollFreeze(0, 2);
				ImGui.TableHeadersRow();
				ImGui.TableNextColumn();
				ImGui.Dummy(new Vector2(0f, 0f));
				ImGui.TableNextColumn();
				ImGui.Checkbox("###createOnlyShowfav", ref _createOnlyShowFav);
				UiSharedService.AttachToolTip("Filter by favorites");
				ImGui.TableNextColumn();
				ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
				ImGui.InputTextWithHint("###createFilterCode", "Filter by code", ref _createCodeFilter, 200);
				ImGui.TableNextColumn();
				ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
				ImGui.InputTextWithHint("###createFilterDesc", "Filter by description", ref _createDescFilter, 200);
				ImGui.TableNextColumn();
				ImGui.Dummy(new Vector2(0f, 0f));
				ImGui.TableNextColumn();
				ImGui.Dummy(new Vector2(0f, 0f));
				ImGui.TableNextColumn();
				ImGui.Dummy(new Vector2(0f, 0f));
				ImGui.TableNextColumn();
				ImGui.Checkbox("###createShowNotDl", ref _createOnlyShowNotDownloadable);
				UiSharedService.AttachToolTip("Filter by not downloadable");
				ImGui.TableNextColumn();
				ImGui.Dummy(new Vector2(0f, 0f));
				ImGui.TableNextColumn();
				ImGui.Dummy(new Vector2(0f, 0f));
				ImGui.TableNextColumn();
				ImGui.Dummy(new Vector2(0f, 0f));
				ImGui.TableNextColumn();
				ImGui.Dummy(new Vector2(0f, 0f));
				foreach (CharaDataFullExtendedDto entry in from b in _charaDataManager.OwnCharaData.Values.Where(delegate(CharaDataFullExtendedDto v)
					{
						bool flag = true;
						if (!string.IsNullOrWhiteSpace(_createCodeFilter))
						{
							flag &= v.FullId.Contains(_createCodeFilter, StringComparison.OrdinalIgnoreCase);
						}
						if (!string.IsNullOrWhiteSpace(_createDescFilter))
						{
							flag &= v.Description.Contains(_createDescFilter, StringComparison.OrdinalIgnoreCase);
						}
						if (_createOnlyShowFav)
						{
							flag &= _configService.Current.FavoriteCodes.ContainsKey(v.FullId);
						}
						if (_createOnlyShowNotDownloadable)
						{
							flag &= v.HasMissingFiles || string.IsNullOrEmpty(v.GlamourerData);
						}
						return flag;
					})
					orderby b.CreatedDate
					select b)
				{
					CharaDataExtendedUpdateDto? updateDto = _charaDataManager.GetUpdateDto(entry.Id);
					ImGui.TableNextColumn();
					if (string.Equals(entry.Id, SelectedDtoId, StringComparison.Ordinal))
					{
						_uiSharedService.IconText(FontAwesomeIcon.CaretRight);
					}
					ImGui.TableNextColumn();
					DrawAddOrRemoveFavorite(entry);
					ImGui.TableNextColumn();
					string idText = entry.FullId;
					if ((object)updateDto != null && updateDto.HasChanges)
					{
						UiSharedService.ColorText(idText, ImGuiColors.DalamudYellow);
						UiSharedService.AttachToolTip("This entry has unsaved changes");
					}
					else
					{
						ImGui.TextUnformatted(idText);
					}
					if (ImGui.IsItemClicked())
					{
						SelectedDtoId = entry.Id;
					}
					ImGui.TableNextColumn();
					ImGui.TextUnformatted(entry.Description);
					if (ImGui.IsItemClicked())
					{
						SelectedDtoId = entry.Id;
					}
					UiSharedService.AttachToolTip(entry.Description);
					ImGui.TableNextColumn();
					ImGui.TextUnformatted(entry.CreatedDate.ToLocalTime().ToString());
					if (ImGui.IsItemClicked())
					{
						SelectedDtoId = entry.Id;
					}
					ImGui.TableNextColumn();
					ImGui.TextUnformatted(entry.UpdatedDate.ToLocalTime().ToString());
					if (ImGui.IsItemClicked())
					{
						SelectedDtoId = entry.Id;
					}
					ImGui.TableNextColumn();
					ImGui.TextUnformatted(entry.DownloadCount.ToString());
					if (ImGui.IsItemClicked())
					{
						SelectedDtoId = entry.Id;
					}
					ImGui.TableNextColumn();
					bool isDownloadable = !entry.HasMissingFiles && !string.IsNullOrEmpty(entry.GlamourerData);
					_uiSharedService.BooleanToColoredIcon(isDownloadable, inline: false);
					if (ImGui.IsItemClicked())
					{
						SelectedDtoId = entry.Id;
					}
					UiSharedService.AttachToolTip(isDownloadable ? "Can be downloaded by others" : "Cannot be downloaded: Has missing files or data, please review this entry manually");
					ImGui.TableNextColumn();
					int count = entry.FileGamePaths.Concat(entry.FileSwaps).Count();
					ImGui.TextUnformatted(count.ToString());
					if (ImGui.IsItemClicked())
					{
						SelectedDtoId = entry.Id;
					}
					UiSharedService.AttachToolTip((count == 0) ? "No File data attached" : "Has File data attached");
					ImGui.TableNextColumn();
					bool hasGlamourerData = !string.IsNullOrEmpty(entry.GlamourerData);
					_uiSharedService.BooleanToColoredIcon(hasGlamourerData, inline: false);
					if (ImGui.IsItemClicked())
					{
						SelectedDtoId = entry.Id;
					}
					UiSharedService.AttachToolTip(string.IsNullOrEmpty(entry.GlamourerData) ? "No Glamourer data attached" : "Has Glamourer data attached");
					ImGui.TableNextColumn();
					bool hasCustomizeData = !string.IsNullOrEmpty(entry.CustomizeData);
					_uiSharedService.BooleanToColoredIcon(hasCustomizeData, inline: false);
					if (ImGui.IsItemClicked())
					{
						SelectedDtoId = entry.Id;
					}
					UiSharedService.AttachToolTip(string.IsNullOrEmpty(entry.CustomizeData) ? "No Customize+ data attached" : "Has Customize+ data attached");
					ImGui.TableNextColumn();
					FontAwesomeIcon eIcon = FontAwesomeIcon.None;
					if (!object.Equals(DateTime.MaxValue, entry.ExpiryDate))
					{
						eIcon = FontAwesomeIcon.Clock;
					}
					_uiSharedService.IconText(eIcon, ImGuiColors.DalamudYellow);
					if (ImGui.IsItemClicked())
					{
						SelectedDtoId = entry.Id;
					}
					if (eIcon != 0)
					{
						UiSharedService.AttachToolTip($"This entry will expire on {entry.ExpiryDate.ToLocalTime()}");
					}
				}
			}
		}
		using (ImRaii.Disabled(!_charaDataManager.Initialized || _charaDataManager.DataCreationTask != null || _charaDataManager.OwnCharaData.Count == _charaDataManager.MaxCreatableCharaData))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "New Character Data Entry"))
			{
				_charaDataManager.CreateCharaDataEntry(_closalCts.Token);
				_selectNewEntry = true;
			}
		}
		if (_charaDataManager.DataCreationTask != null)
		{
			UiSharedService.AttachToolTip("You can only create new character data every few seconds. Please wait.");
		}
		if (!_charaDataManager.Initialized)
		{
			UiSharedService.AttachToolTip("Please use the button \"Get Own Chara Data\" once before you can add new data entries.");
		}
		if (_charaDataManager.Initialized)
		{
			ImGui.SameLine();
			ImGui.AlignTextToFramePadding();
			UiSharedService.TextWrapped($"Chara Data Entries on Server: {_charaDataManager.OwnCharaData.Count}/{_charaDataManager.MaxCreatableCharaData}");
			if (_charaDataManager.OwnCharaData.Count == _charaDataManager.MaxCreatableCharaData)
			{
				ImGui.AlignTextToFramePadding();
				UiSharedService.ColorTextWrapped("You have reached the maximum Character Data entries and cannot create more.", ImGuiColors.DalamudYellow);
			}
		}
		if (_charaDataManager.DataCreationTask != null && !_charaDataManager.DataCreationTask.IsCompleted)
		{
			UiSharedService.ColorTextWrapped("Creating new character data entry on server...", ImGuiColors.DalamudYellow);
		}
		else if (_charaDataManager.DataCreationTask != null && _charaDataManager.DataCreationTask.IsCompleted)
		{
			Vector4 color = (_charaDataManager.DataCreationTask.Result.Success ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
			UiSharedService.ColorTextWrapped(_charaDataManager.DataCreationTask.Result.Output, color);
		}
		ImGuiHelpers.ScaledDummy(10f);
		ImGui.Separator();
		if (_charaDataManager.OwnCharaData.Count != _dataEntries && _selectNewEntry && _charaDataManager.OwnCharaData.Any())
		{
			SelectedDtoId = _charaDataManager.OwnCharaData.OrderBy<KeyValuePair<string, CharaDataFullExtendedDto>, DateTime>((KeyValuePair<string, CharaDataFullExtendedDto> o) => o.Value.CreatedDate).Last().Value.Id;
			_selectNewEntry = false;
		}
		_dataEntries = _charaDataManager.OwnCharaData.Count;
		_charaDataManager.OwnCharaData.TryGetValue(SelectedDtoId, out CharaDataFullExtendedDto dto);
		DrawEditCharaData(dto);
	}

	private void DrawSpecific(CharaDataExtendedUpdateDto updateDto)
	{
		UiSharedService.DrawTree("Access for Specific Individuals / Syncshells", delegate
		{
			using (ImRaii.PushId("user"))
			{
				using (ImRaii.Group())
				{
					InputComboHybrid("##AliasToAdd", "##AliasToAddPicker", ref _specificIndividualAdd, _pairManager.PairsWithGroups.Keys, (Pair pair) => (Id: pair.UserData.UID, Alias: pair.UserData.Alias, AliasOrId: pair.UserData.AliasOrUID, Note: pair.GetNote()));
					ImGui.SameLine();
					using (ImRaii.Disabled(string.IsNullOrEmpty(_specificIndividualAdd) || updateDto.UserList.Any((UserData f) => string.Equals(f.UID, _specificIndividualAdd, StringComparison.Ordinal) || string.Equals(f.Alias, _specificIndividualAdd, StringComparison.Ordinal))))
					{
						if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
						{
							updateDto.AddUserToList(_specificIndividualAdd);
							_specificIndividualAdd = string.Empty;
						}
					}
					ImGui.SameLine();
					ImGui.TextUnformatted("UID/Vanity UID to Add");
					_uiSharedService.DrawHelpText("Users added to this list will be able to access this character data regardless of your pause or pair state with them.--SEP--Note: Mistyped entries will be automatically removed on updating data to server.");
					using (ImRaii.ListBox("Allowed Individuals", new Vector2(200f * ImGuiHelpers.GlobalScale, 200f * ImGuiHelpers.GlobalScale)))
					{
						foreach (UserData current in updateDto.UserList)
						{
							if (ImGui.Selectable(string.IsNullOrEmpty(current.Alias) ? current.UID : (current.Alias + " (" + current.UID + ")"), string.Equals(current.UID, _selectedSpecificUserIndividual, StringComparison.Ordinal)))
							{
								_selectedSpecificUserIndividual = current.UID;
							}
						}
					}
					using (ImRaii.Disabled(string.IsNullOrEmpty(_selectedSpecificUserIndividual)))
					{
						if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Remove selected User"))
						{
							updateDto.RemoveUserFromList(_selectedSpecificUserIndividual);
							_selectedSpecificUserIndividual = string.Empty;
						}
					}
					using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
					{
						if (_uiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, "Apply current Allowed Individuals to all MCDO entries"))
						{
							foreach (CharaDataFullExtendedDto current2 in _charaDataManager.OwnCharaData.Values.Where((CharaDataFullExtendedDto k) => !string.Equals(k.Id, updateDto.Id, StringComparison.Ordinal)))
							{
								CharaDataExtendedUpdateDto updateDto2 = _charaDataManager.GetUpdateDto(current2.Id);
								if (!(updateDto2 == null))
								{
									foreach (string current3 in updateDto2.UserList.Select((UserData k) => k.UID).Concat(updateDto2.AllowedUsers ?? new List<string>()).Distinct<string>(StringComparer.Ordinal)
										.ToList())
									{
										updateDto2.RemoveUserFromList(current3);
									}
									foreach (string current4 in updateDto.UserList.Select((UserData k) => k.UID).Concat(updateDto.AllowedUsers ?? new List<string>()).Distinct<string>(StringComparer.Ordinal)
										.ToList())
									{
										updateDto2.AddUserToList(current4);
									}
								}
							}
						}
					}
					UiSharedService.AttachToolTip("This will apply the current list of allowed specific individuals to ALL of your MCDO entries.--SEP--Hold CTRL to enable.");
				}
			}
			ImGui.SameLine();
			ImGuiHelpers.ScaledDummy(20f);
			ImGui.SameLine();
			using (ImRaii.PushId("group"))
			{
				using (ImRaii.Group())
				{
					InputComboHybrid("##GroupAliasToAdd", "##GroupAliasToAddPicker", ref _specificGroupAdd, _pairManager.Groups.Keys, (GroupData group) => (Id: group.GID, Alias: group.Alias, AliasOrId: group.AliasOrGID, Note: _serverConfigurationManager.GetNoteForGid(group.GID)));
					ImGui.SameLine();
					using (ImRaii.Disabled(string.IsNullOrEmpty(_specificGroupAdd) || updateDto.GroupList.Any((GroupData f) => string.Equals(f.GID, _specificGroupAdd, StringComparison.Ordinal) || string.Equals(f.Alias, _specificGroupAdd, StringComparison.Ordinal))))
					{
						if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
						{
							updateDto.AddGroupToList(_specificGroupAdd);
							_specificGroupAdd = string.Empty;
						}
					}
					ImGui.SameLine();
					ImGui.TextUnformatted("GID/Vanity GID to Add");
					_uiSharedService.DrawHelpText("Users in Syncshells added to this list will be able to access this character data regardless of your pause or pair state with them.--SEP--Note: Mistyped entries will be automatically removed on updating data to server.");
					using (ImRaii.ListBox("Allowed Syncshells", new Vector2(200f * ImGuiHelpers.GlobalScale, 200f * ImGuiHelpers.GlobalScale)))
					{
						foreach (GroupData current5 in updateDto.GroupList)
						{
							if (ImGui.Selectable(string.IsNullOrEmpty(current5.Alias) ? current5.GID : (current5.Alias + " (" + current5.GID + ")"), string.Equals(current5.GID, _selectedSpecificGroupIndividual, StringComparison.Ordinal)))
							{
								_selectedSpecificGroupIndividual = current5.GID;
							}
						}
					}
					using (ImRaii.Disabled(string.IsNullOrEmpty(_selectedSpecificGroupIndividual)))
					{
						if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Remove selected Syncshell"))
						{
							updateDto.RemoveGroupFromList(_selectedSpecificGroupIndividual);
							_selectedSpecificGroupIndividual = string.Empty;
						}
					}
					using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
					{
						if (_uiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, "Apply current Allowed Syncshells to all MCDO entries"))
						{
							foreach (CharaDataFullExtendedDto current6 in _charaDataManager.OwnCharaData.Values.Where((CharaDataFullExtendedDto k) => !string.Equals(k.Id, updateDto.Id, StringComparison.Ordinal)))
							{
								CharaDataExtendedUpdateDto updateDto3 = _charaDataManager.GetUpdateDto(current6.Id);
								if (!(updateDto3 == null))
								{
									foreach (string current7 in updateDto3.GroupList.Select((GroupData k) => k.GID).Concat(updateDto3.AllowedGroups ?? new List<string>()).Distinct<string>(StringComparer.Ordinal)
										.ToList())
									{
										updateDto3.RemoveGroupFromList(current7);
									}
									foreach (string current8 in updateDto.GroupList.Select((GroupData k) => k.GID).Concat(updateDto.AllowedGroups ?? new List<string>()).Distinct<string>(StringComparer.Ordinal)
										.ToList())
									{
										updateDto3.AddGroupToList(current8);
									}
								}
							}
						}
					}
					UiSharedService.AttachToolTip("This will apply the current list of allowed specific syncshells to ALL of your MCDO entries.--SEP--Hold CTRL to enable.");
				}
			}
			ImGui.Separator();
			ImGuiHelpers.ScaledDummy(5f);
		});
	}

	private void InputComboHybrid<T>(string inputId, string comboId, ref string value, IEnumerable<T> comboEntries, Func<T, (string Id, string? Alias, string AliasOrId, string? Note)> parseEntry)
	{
		UiSharedService.ScaledNextItemWidth(200f - ImGui.GetFrameHeight());
		ImGui.InputText(inputId, ref value, 20);
		ImGui.SameLine(0f, 0f);
		using ImRaii.IEndObject combo = ImRaii.Combo(comboId, string.Empty, ImGuiComboFlags.PopupAlignLeft | ImGuiComboFlags.NoPreview);
		if (!combo)
		{
			return;
		}
		if (_openComboHybridEntries == null || !string.Equals(_openComboHybridId, comboId, StringComparison.Ordinal))
		{
			string valueSnapshot = value;
			_openComboHybridEntries = (from entry in comboEntries.Select(parseEntry)
				where entry.Id.Contains(valueSnapshot, StringComparison.OrdinalIgnoreCase) || (entry.Alias != null && entry.Alias.Contains(valueSnapshot, StringComparison.OrdinalIgnoreCase)) || (entry.Note != null && entry.Note.Contains(valueSnapshot, StringComparison.OrdinalIgnoreCase))
				select entry).OrderBy<(string, string, string, string), string>(((string Id, string Alias, string AliasOrId, string Note) entry) => (entry.Note != null) ? (entry.Note + " (" + entry.AliasOrId + ")") : entry.AliasOrId, StringComparer.OrdinalIgnoreCase).ToArray();
			_openComboHybridId = comboId;
		}
		_comboHybridUsedLastFrame = true;
		float width = 200f - 2f * ImGui.GetStyle().FramePadding.X - ((_openComboHybridEntries.Length > 8) ? ImGui.GetStyle().ScrollbarSize : 0f);
		(string, string, string, string)[] openComboHybridEntries = _openComboHybridEntries;
		for (int i = 0; i < openComboHybridEntries.Length; i++)
		{
			(string, string, string, string) tuple = openComboHybridEntries[i];
			string id = tuple.Item1;
			string alias = tuple.Item2;
			string aliasOrId = tuple.Item3;
			string note = tuple.Item4;
			bool selected = !string.IsNullOrEmpty(value) && (string.Equals(id, value, StringComparison.Ordinal) || string.Equals(alias, value, StringComparison.Ordinal));
			using (ImRaii.PushFont(UiBuilder.MonoFont, note == null))
			{
				if (ImGui.Selectable((note == null) ? aliasOrId : (note + " (" + aliasOrId + ")"), selected, ImGuiSelectableFlags.None, new Vector2(width, 0f)))
				{
					value = aliasOrId;
				}
			}
		}
	}

	private void DrawNearbyPoses()
	{
		_uiSharedService.BigText("Poses Nearby");
		DrawHelpFoldout("This tab will show you all Shared World Poses nearby you." + Environment.NewLine + Environment.NewLine + "Shared World Poses are poses in character data that have world data attached to them and are set to shared. This means that all data that is in 'Shared with You' that has a pose with world data attached to it will be shown here if you are nearby." + Environment.NewLine + "By default all poses that are shared will be shown. Poses taken in housing areas will by default only be shown on the correct server and location." + Environment.NewLine + Environment.NewLine + "Shared World Poses will appear in the world as floating wisps, as well as in the list below. You can mouse over a Shared World Pose in the list for it to get highlighted in the world." + Environment.NewLine + Environment.NewLine + "You can apply Shared World Poses to yourself or spawn the associated character to pose with them." + Environment.NewLine + Environment.NewLine + "You can adjust the filter and change further settings in the 'Settings & Filter' foldout.");
		UiSharedService.DrawTree("Settings & Filters", delegate
		{
			string buf = _charaDataNearbyManager.UserNoteFilter;
			if (ImGui.InputTextWithHint("##filterbyuser", "Filter by User", ref buf, 50))
			{
				_charaDataNearbyManager.UserNoteFilter = buf;
			}
			bool v2 = _configService.Current.NearbyOwnServerOnly;
			if (ImGui.Checkbox("Only show Poses on current server", ref v2))
			{
				_configService.Current.NearbyOwnServerOnly = v2;
				_configService.Save();
			}
			_uiSharedService.DrawHelpText("Toggling this off will show you the location of all shared Poses with World Data from all Servers");
			bool v3 = _configService.Current.NearbyShowOwnData;
			if (ImGui.Checkbox("Also show your own data", ref v3))
			{
				_configService.Current.NearbyShowOwnData = v3;
				_configService.Save();
			}
			_uiSharedService.DrawHelpText("Toggling this on will also show you the location of your own Poses");
			bool v4 = _configService.Current.NearbyIgnoreHousingLimitations;
			if (ImGui.Checkbox("Ignore Housing Limitations", ref v4))
			{
				_configService.Current.NearbyIgnoreHousingLimitations = v4;
				_configService.Save();
			}
			_uiSharedService.DrawHelpText("This will display all poses in their location regardless of housing limitations. (Ignoring Ward, Plot, Room)--SEP--Note: Poses that utilize housing props, furniture, etc. will not be displayed correctly if not spawned in the right location.");
			bool v5 = _configService.Current.NearbyDrawWisps;
			if (ImGui.Checkbox("Show Pose Wisps in the overworld", ref v5))
			{
				_configService.Current.NearbyDrawWisps = v5;
				_configService.Save();
			}
			_uiSharedService.DrawHelpText("When enabled, Mare will draw floating wisps where other's poses are in the world.");
			int v6 = _configService.Current.NearbyDistanceFilter;
			UiSharedService.ScaledNextItemWidth(100f);
			if (ImGui.SliderInt("Detection Distance", ref v6, 5, 1000))
			{
				_configService.Current.NearbyDistanceFilter = v6;
				_configService.Save();
			}
			_uiSharedService.DrawHelpText("This setting allows you to change the maximum distance in which poses will be shown. Set it to the maximum if you want to see all poses on the current map.");
			bool v7 = _configService.Current.NearbyShowAlways;
			if (ImGui.Checkbox("Keep active outside Poses Nearby tab", ref v7))
			{
				_configService.Current.NearbyShowAlways = v7;
				_configService.Save();
			}
			_uiSharedService.DrawHelpText("This will allow Mare to continue the calculation of position of wisps etc. active outside of the 'Poses Nearby' tab.--SEP--Note: The wisps etc. will disappear during combat and performing.");
		});
		if (!_uiSharedService.IsInGpose)
		{
			ImGuiHelpers.ScaledDummy(5f);
			UiSharedService.DrawGroupedCenteredColorText("Spawning and applying pose data is only available in GPose.", ImGuiColors.DalamudYellow);
			ImGuiHelpers.ScaledDummy(5f);
		}
		DrawUpdateSharedDataButton();
		UiSharedService.DistanceSeparator();
		using (ImRaii.Child("nearbyPosesChild", new Vector2(0f, 0f), border: false, ImGuiWindowFlags.AlwaysAutoResize))
		{
			ImGuiHelpers.ScaledDummy(3f);
			using (ImRaii.PushIndent(5f))
			{
				if (_charaDataNearbyManager.NearbyData.Count == 0)
				{
					UiSharedService.DrawGroupedCenteredColorText("No Shared World Poses found nearby.", ImGuiColors.DalamudYellow);
				}
				bool wasAnythingHovered = false;
				int i = 0;
				foreach (KeyValuePair<PoseEntryExtended, CharaDataNearbyManager.NearbyCharaDataEntry> pose in _charaDataNearbyManager.NearbyData.OrderBy<KeyValuePair<PoseEntryExtended, CharaDataNearbyManager.NearbyCharaDataEntry>, float>((KeyValuePair<PoseEntryExtended, CharaDataNearbyManager.NearbyCharaDataEntry> v) => v.Value.Distance))
				{
					using (ImRaii.PushId("nearbyPose" + i++))
					{
						Vector2 pos = ImGui.GetCursorPos();
						float circleDiameter = 60f;
						float circleOriginX = ImGui.GetWindowContentRegionMax().X - circleDiameter - pos.X;
						float circleOffsetY = 0f;
						UiSharedService.DrawGrouped(delegate
						{
							string noteForUid = _serverConfigurationManager.GetNoteForUid(pose.Key.MetaInfo.Uploader.UID);
							string text = (pose.Key.MetaInfo.IsOwnData ? "YOU" : ((noteForUid == null) ? pose.Key.MetaInfo.Uploader.AliasOrUID : (noteForUid + " (" + pose.Key.MetaInfo.Uploader.AliasOrUID + ")")));
							ImGui.TextUnformatted("Pose by");
							ImGui.SameLine();
							UiSharedService.ColorText(text, ImGuiColors.ParsedGreen);
							using (ImRaii.Group())
							{
								UiSharedService.ColorText("Character Data Description", ImGuiColors.DalamudGrey);
								ImGui.SameLine();
								_uiSharedService.IconText(FontAwesomeIcon.ExternalLinkAlt, ImGuiColors.DalamudGrey);
							}
							UiSharedService.AttachToolTip(pose.Key.MetaInfo.Description);
							UiSharedService.ColorText("Description", ImGuiColors.DalamudGrey);
							ImGui.SameLine();
							UiSharedService.TextWrapped(pose.Key.Description ?? "No Pose Description was set", circleOriginX);
							Vector2 cursorPos = ImGui.GetCursorPos();
							float num = (cursorPos.Y - pos.Y) / 2f;
							circleOffsetY = num - circleDiameter / 2f;
							if (circleOffsetY < 0f)
							{
								circleOffsetY = 0f;
							}
							ImGui.SetCursorPos(new Vector2(circleOriginX, pos.Y));
							ImGui.Dummy(new Vector2(circleDiameter, circleDiameter));
							UiSharedService.AttachToolTip("Click to open corresponding map and set map marker--SEP--" + pose.Key.WorldDataDescriptor);
							if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
							{
								_dalamudUtilService.SetMarkerAndOpenMap(pose.Key.Position, pose.Key.Map);
							}
							ImGui.SetCursorPos(cursorPos);
							if (_uiSharedService.IsInGpose)
							{
								GposePoseAction(delegate
								{
									if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Apply Pose"))
									{
										_charaDataManager.ApplyFullPoseDataToGposeTarget(pose.Key);
									}
								}, "Apply pose and position to " + CharaName(_gposeTarget), _hasValidGposeTarget);
								ImGui.SameLine();
								GposeMetaInfoAction(delegate
								{
									if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Spawn and Pose"))
									{
										_charaDataManager.SpawnAndApplyWorldTransform(pose.Key.MetaInfo, pose.Key);
									}
								}, "Spawn actor and apply pose and position", pose.Key.MetaInfo, _hasValidGposeTarget, isSpawning: true);
							}
						});
						if (ImGui.IsItemHovered())
						{
							wasAnythingHovered = true;
							_nearbyHovered = pose.Key;
						}
						ImDrawListPtr drawList = ImGui.GetWindowDrawList();
						float circleRadius = circleDiameter / 2f;
						Vector2 windowPos = ImGui.GetWindowPos();
						float scrollX = ImGui.GetScrollX();
						float scrollY = ImGui.GetScrollY();
						Vector2 circleCenter = new Vector2(windowPos.X + circleOriginX + circleRadius - scrollX, windowPos.Y + pos.Y + circleRadius + circleOffsetY - scrollY);
						double rads = (double)pose.Value.Direction * (Math.PI / 180.0);
						float halfConeAngleRadians = (float)Math.PI / 12f;
						Vector2 baseDir1 = new Vector2((float)Math.Sin(rads - (double)halfConeAngleRadians), 0f - (float)Math.Cos(rads - (double)halfConeAngleRadians));
						Vector2 baseDir2 = new Vector2((float)Math.Sin(rads + (double)halfConeAngleRadians), 0f - (float)Math.Cos(rads + (double)halfConeAngleRadians));
						Vector2 coneBase1 = circleCenter + baseDir1 * circleRadius;
						Vector2 coneBase2 = circleCenter + baseDir2 * circleRadius;
						drawList.AddTriangleFilled(circleCenter, coneBase1, coneBase2, UiSharedService.Color(ImGuiColors.ParsedGreen));
						drawList.AddCircle(circleCenter, circleDiameter / 2f, UiSharedService.Color(ImGuiColors.DalamudWhite), 360, 2f);
						string distance = pose.Value.Distance.ToString("0.0") + "y";
						Vector2 textSize = ImGui.CalcTextSize(distance);
						drawList.AddText(new Vector2(circleCenter.X - textSize.X / 2f, circleCenter.Y + textSize.Y / 3f), UiSharedService.Color(ImGuiColors.DalamudWhite), distance);
						ImGuiHelpers.ScaledDummy(3f);
					}
				}
				if (!wasAnythingHovered)
				{
					_nearbyHovered = null;
				}
				_charaDataNearbyManager.SetHoveredVfx(_nearbyHovered);
			}
		}
	}

	private void DrawUpdateSharedDataButton()
	{
		using (ImRaii.Disabled(_charaDataManager.GetAllDataTask != null || (_charaDataManager.GetSharedWithYouTimeoutTask != null && !_charaDataManager.GetSharedWithYouTimeoutTask.IsCompleted)))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Update Data Shared With You"))
			{
				_charaDataManager.GetAllSharedData(_disposalCts.Token).ContinueWith(delegate
				{
					UpdateFilteredItems();
				});
			}
		}
		if (_charaDataManager.GetSharedWithYouTimeoutTask != null && !_charaDataManager.GetSharedWithYouTimeoutTask.IsCompleted)
		{
			UiSharedService.AttachToolTip("You can only refresh all character data from server every minute. Please wait.");
		}
	}
}
