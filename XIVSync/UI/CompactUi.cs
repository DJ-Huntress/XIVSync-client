using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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
using XIVSync.WebAPI.Files;
using XIVSync.WebAPI.Files.Models;
using XIVSync.WebAPI.SignalR.Utils;

namespace XIVSync.UI;

public class CompactUi : WindowMediatorSubscriberBase
{
	private enum SurfaceBg
	{
		Popup,
		Window
	}

	private static class H
	{
		public const float Red = 0f;

		public const float Yellow = 0.12f;

		public const float Green = 0.33f;

		public const float Blue = 0.58f;
	}

	private sealed class ThemedWindowScope : IDisposable
	{
		private readonly int _colorCount;

		private readonly int _styleCount;

		public ThemedWindowScope(ThemePalette theme, SurfaceBg bgKind, float rounding = 8f, float borderSize = 1f)
		{
			if (bgKind == SurfaceBg.Popup)
			{
				ImGui.PushStyleColor(ImGuiCol.PopupBg, theme.PanelBg);
			}
			else
			{
				ImGui.PushStyleColor(ImGuiCol.WindowBg, theme.PanelBg);
			}
			ImGui.PushStyleColor(ImGuiCol.Border, theme.PanelBorder);
			ImGui.PushStyleColor(ImGuiCol.TitleBg, theme.HeaderBg);
			ImGui.PushStyleColor(ImGuiCol.TitleBgActive, theme.HeaderBg);
			ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, theme.HeaderBg);
			ImGui.PushStyleColor(ImGuiCol.Text, theme.TextPrimary);
			ImGui.PushStyleColor(ImGuiCol.TextDisabled, theme.TextDisabled);
			ImGui.PushStyleColor(ImGuiCol.Header, theme.Btn);
			ImGui.PushStyleColor(ImGuiCol.HeaderHovered, theme.BtnHovered);
			ImGui.PushStyleColor(ImGuiCol.HeaderActive, theme.BtnActive);
			ImGui.PushStyleColor(ImGuiCol.CheckMark, theme.Accent);
			ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, theme.Btn);
			ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, theme.BtnHovered);
			ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, theme.Accent);
			ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, theme.BtnActive);
			ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(theme.PanelBg.X, theme.PanelBg.Y, theme.PanelBg.Z, 0.5f));
			_colorCount = 18;
			ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, rounding);
			ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, borderSize);
			_styleCount = 2;
		}

		public void Dispose()
		{
			ImGui.PopStyleVar(_styleCount);
			ImGui.PopStyleColor(_colorCount);
		}
	}

	private sealed class GlobalThemeScope : IDisposable
	{
		private readonly int _c;

		private readonly int _v;

		public GlobalThemeScope(ThemePalette t)
		{
			ImGui.PushStyleColor(ImGuiCol.Separator, t.PanelBorder);
			ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, t.Accent);
			ImGui.PushStyleColor(ImGuiCol.SeparatorActive, t.Accent);
			ImGui.PushStyleColor(ImGuiCol.Header, t.Btn);
			ImGui.PushStyleColor(ImGuiCol.HeaderHovered, t.BtnHovered);
			ImGui.PushStyleColor(ImGuiCol.HeaderActive, t.BtnActive);
			ImGui.PushStyleColor(ImGuiCol.CheckMark, t.Accent);
			ImGui.PushStyleColor(ImGuiCol.FrameBg, t.Btn);
			ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, t.BtnHovered);
			ImGui.PushStyleColor(ImGuiCol.FrameBgActive, t.BtnActive);
			ImGui.PushStyleColor(ImGuiCol.SliderGrab, t.Accent);
			ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, t.BtnActive);
			ImGui.PushStyleColor(ImGuiCol.Tab, t.Btn);
			ImGui.PushStyleColor(ImGuiCol.TabHovered, t.BtnHovered);
			ImGui.PushStyleColor(ImGuiCol.TabActive, t.BtnActive);
			_c = 13;
			ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
			ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));
			_v = 2;
		}

		public void Dispose()
		{
			ImGui.PopStyleVar(_v);
			ImGui.PopStyleColor(_c);
		}
	}

	private readonly ApiController _apiController;

	private readonly MareConfigService _configService;

	private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>>();

	private readonly DrawEntityFactory _drawEntityFactory;

	private readonly FileUploadManager _fileTransferManager;

	private readonly PairManager _pairManager;

	private readonly SelectTagForPairUi _selectGroupForPairUi;

	private readonly SelectPairForTagUi _selectPairsForGroupUi;

	private readonly IpcManager _ipcManager;

	private readonly ServerConfigurationManager _serverManager;

	private readonly StatusMessageBar _statusMessageBar;

	private readonly TopTabMenu _tabMenu;

	private readonly TagHandler _tagHandler;

	private readonly UiSharedService _uiSharedService;

	private List<IDrawFolder> _drawFolders;

	private Pair? _lastAddedUser;

	private string _lastAddedUserComment = string.Empty;

	private Vector2 _lastPosition = Vector2.One;

	private Vector2 _lastSize = Vector2.One;

	private bool _showModalForUserAddition;

	private float _transferPartHeight;

	private bool _wasOpen;

	private float _windowContentWidth;

	private readonly string _titleBarText;

	private bool _collapsed;

	private bool _userManuallyClosed;

	private ThemePalette _theme = new ThemePalette();

	private ThemePalette _themeWorking = new ThemePalette();

	private string _selectedPreset = "Blue";

	private string _lastSelectedPreset = "Blue";

	private bool _showThemeInline;

	private bool _voiceEnabled;

	private bool _voiceMuted;

	private bool _voiceDeafened;

	private float _voicePartHeight = -1f;

	private static readonly bool _isAuthorized = CheckAuthorization();

	public void ToggleThemeInline()
	{
		_showThemeInline = !_showThemeInline;
		if (_showThemeInline)
		{
			_selectedPreset = _lastSelectedPreset;
			_themeWorking = Clone(_theme);
		}
	}

	private bool IsThemeCustomized()
	{
		foreach (ThemePalette preset in ThemePresets.Presets.Values)
		{
			if (ThemesEqual(_theme, preset))
			{
				return false;
			}
		}
		return true;
	}

	private bool ThemesEqual(ThemePalette a, ThemePalette b)
	{
		if (a.PanelBg.Equals(b.PanelBg) && a.PanelBorder.Equals(b.PanelBorder) && a.HeaderBg.Equals(b.HeaderBg) && a.Accent.Equals(b.Accent) && a.TextPrimary.Equals(b.TextPrimary) && a.TextSecondary.Equals(b.TextSecondary) && a.TextDisabled.Equals(b.TextDisabled) && a.Link.Equals(b.Link) && a.LinkHover.Equals(b.LinkHover) && a.Btn.Equals(b.Btn) && a.BtnHovered.Equals(b.BtnHovered) && a.BtnActive.Equals(b.BtnActive) && a.BtnText.Equals(b.BtnText) && a.BtnTextHovered.Equals(b.BtnTextHovered) && a.BtnTextActive.Equals(b.BtnTextActive) && a.TooltipBg.Equals(b.TooltipBg))
		{
			return a.TooltipText.Equals(b.TooltipText);
		}
		return false;
	}

	private void DetectCurrentPreset()
	{
		foreach (KeyValuePair<string, ThemePalette> kv in ThemePresets.Presets)
		{
			if (ThemesEqual(_theme, kv.Value))
			{
				_selectedPreset = kv.Key;
				_lastSelectedPreset = kv.Key;
				break;
			}
		}
	}

	private static bool CheckAuthorization()
	{
		object obj = Assembly.GetExecutingAssembly().GetName().Name;
		string location = Assembly.GetExecutingAssembly().Location;
		if (obj == null)
		{
			obj = "";
		}
		uint h1 = ComputeHash((string)obj);
		bool locationValid = new char[7] { 'x', 'i', 'v', 's', 'y', 'n', 'c' }.All((char c) => location.ToLowerInvariant().Contains(c));
		return (h1 ^ 0x12345678) == 1847150823 && locationValid;
	}

	private static uint ComputeHash(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return 0u;
		}
		uint hash = 2166136261u;
		foreach (char c in input)
		{
			hash ^= c;
			hash *= 16777619;
		}
		return hash;
	}

	private static Vector4 ScrambleColor(Vector4 original)
	{
		if (_isAuthorized)
		{
			return original;
		}
		int hashCode = original.GetHashCode();
		float r = (float)(hashCode & 0xFF) / 255f;
		float g = (float)((hashCode >> 8) & 0xFF) / 255f;
		float b = (float)((hashCode >> 16) & 0xFF) / 255f;
		return new Vector4(r, g, b, original.W);
	}

	private static float ScrambleSpacing(float original)
	{
		if (_isAuthorized)
		{
			return original;
		}
		return original * 1.3f + 2f;
	}

	private static Vector2 ScrambleSize(Vector2 original)
	{
		if (_isAuthorized)
		{
			return original;
		}
		return new Vector2(original.X * 0.9f, original.Y * 1.1f);
	}

	private static string ScrambleText(string original)
	{
		if (_isAuthorized)
		{
			return original;
		}
		if (string.IsNullOrEmpty(original))
		{
			return original;
		}
		char[] chars = original.ToCharArray();
		for (int i = 0; i < chars.Length; i += 3)
		{
			if (char.IsLetter(chars[i]))
			{
				chars[i] = (char.IsUpper(chars[i]) ? 'X' : 'x');
			}
		}
		return new string(chars);
	}

	public CompactUi(ILogger<CompactUi> logger, UiSharedService uiShared, MareConfigService configService, ApiController apiController, PairManager pairManager, ServerConfigurationManager serverManager, MareMediator mediator, FileUploadManager fileTransferManager, TagHandler tagHandler, DrawEntityFactory drawEntityFactory, SelectTagForPairUi selectTagForPairUi, SelectPairForTagUi selectPairForTagUi, PerformanceCollectorService performanceCollectorService, IpcManager ipcManager, MareProfileManager profileManager)
		: base(logger, mediator, "###MareSynchronosMainUI", performanceCollectorService)
	{
		Version ver = Assembly.GetExecutingAssembly().GetName().Version;
		_titleBarText = ((Assembly.GetExecutingAssembly().GetName().Name?.Contains("XIVSync") ?? false) ? $"XIVSync ({ver.Major}.{ver.Minor}.{ver.Build})" : ScrambleText($"Unauthorized Copy ({ver.Major}.{ver.Minor}.{ver.Build})"));
		_uiSharedService = uiShared;
		_configService = configService;
		_apiController = apiController;
		_pairManager = pairManager;
		_serverManager = serverManager;
		_fileTransferManager = fileTransferManager;
		_tagHandler = tagHandler;
		_drawEntityFactory = drawEntityFactory;
		_selectGroupForPairUi = selectTagForPairUi;
		_selectPairsForGroupUi = selectPairForTagUi;
		_ipcManager = ipcManager;
		_statusMessageBar = new StatusMessageBar(_apiController, logger, base.Mediator, profileManager, _uiSharedService);
		_tabMenu = new TopTabMenu(_logger, base.Mediator, _apiController, _pairManager, _uiSharedService, _configService, this);
		if (_configService.Current?.Theme != null)
		{
			_theme = Clone(_configService.Current.Theme);
			DetectCurrentPreset();
		}
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
					using (new ThemedWindowScope(_theme, SurfaceBg.Popup))
					{
						ImGui.BeginTooltip();
						ImGui.TextColored(_theme.BtnText, "Open Mare Settings");
						ImGui.EndTooltip();
					}
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
					using (new ThemedWindowScope(_theme, SurfaceBg.Popup))
					{
						ImGui.BeginTooltip();
						ImGui.TextColored(_theme.BtnText, "Open Mare Event Viewer");
						ImGui.EndTooltip();
					}
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
				MinimumSize = ScrambleSize(new Vector2(375f, 40f)),
				MaximumSize = ScrambleSize(new Vector2(375f, 40f))
			};
			DrawCustomTitleBarOverlay(headerHeight);
			return;
		}
		using (new GlobalThemeScope(_theme))
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
				MinimumSize = ScrambleSize(new Vector2(375f, 700f)),
				MaximumSize = ScrambleSize(new Vector2(375f, 2000f))
			};
			DrawCustomTitleBarOverlay(headerHeight);
			using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 10f))
			{
				using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 1f))
				{
					using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8f, 8f)))
					{
						using (ImRaii.PushColor(ImGuiCol.ChildBg, _theme.PanelBg))
						{
							using (ImRaii.PushColor(ImGuiCol.Border, _theme.PanelBorder))
							{
								using (ImRaii.PushColor(ImGuiCol.Button, _theme.Btn))
								{
									using (ImRaii.PushColor(ImGuiCol.ButtonHovered, _theme.BtnHovered))
									{
										using (ImRaii.PushColor(ImGuiCol.ButtonActive, _theme.BtnActive))
										{
											using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6f))
											{
												using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f)))
												{
													using (ImRaii.PushColor(ImGuiCol.Text, _theme.TextPrimary))
													{
														using (ImRaii.PushColor(ImGuiCol.TextDisabled, _theme.TextDisabled))
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
																	ImGui.TextColored(ImGuiColors.DalamudRed, unsupported);
																}
																UiSharedService.ColorTextWrapped($"Your XIVSync installation is out of date, the current version is {ver.Major}.{ver.Minor}.{ver.Build}. " + "It is highly recommended to keep XIVSync up to date. Open /xlplugins and update the plugin.", ImGuiColors.DalamudRed);
															}
															if (!_ipcManager.Initialized)
															{
																string unsupported2 = "MISSING ESSENTIAL PLUGINS";
																using (_uiSharedService.UidFont.Push())
																{
																	Vector2 uidTextSize2 = ImGui.CalcTextSize(unsupported2);
																	ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2f - uidTextSize2.X / 2f);
																	ImGui.AlignTextToFramePadding();
																	ImGui.TextColored(ImGuiColors.DalamudRed, unsupported2);
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
																	ImGui.Separator();
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
															using (new ThemedWindowScope(_theme, SurfaceBg.Window))
															{
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
														}
													}
												}
											}
										}
									}
								}
							}
						}
					}
				}
			}
		}
	}

	private void DrawPairs()
	{
		float availY = ImGui.GetContentRegionAvail().Y;
		float transfersH = ((_transferPartHeight > 0f) ? _transferPartHeight : EstimateTransfersHeightFromStyle());
		float voiceH = ((_voicePartHeight > 0f) ? _voicePartHeight : EstimateVoiceHeightFromStyle());
		float spacingY = ImGui.GetStyle().ItemSpacing.Y;
		float listH = MathF.Max(1f, availY - transfersH - voiceH - spacingY);
		listH *= 1.25f;
		Vector2 rowFramePadding = new Vector2(8f, 6f);
		Vector2 rowItemSpacing = new Vector2(6f, 4f);
		Vector4 header = _theme.Btn;
		Vector4 headerHovered = _theme.BtnHovered;
		Vector4 headerActive = _theme.BtnActive;
		ImGui.BeginChild("list", new Vector2(_windowContentWidth, listH));
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, rowFramePadding);
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, rowItemSpacing);
		ImGui.PushStyleColor(ImGuiCol.Header, header);
		ImGui.PushStyleColor(ImGuiCol.HeaderHovered, headerHovered);
		ImGui.PushStyleColor(ImGuiCol.HeaderActive, headerActive);
		foreach (IDrawFolder drawFolder in _drawFolders)
		{
			drawFolder.Draw();
		}
		ImGui.PopStyleColor(3);
		ImGui.PopStyleVar(2);
		ImGui.EndChild();
	}

	private static float EstimateVoiceHeightFromStyle()
	{
		ImGuiStylePtr style = ImGui.GetStyle();
		return ImGui.GetTextLineHeightWithSpacing() * 2f + style.FramePadding.Y * 2f + style.ItemSpacing.Y;
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
		using (_uiSharedService.UidFont.Push())
		{
			ImGui.TextColored(_theme.Accent, uidText);
		}
		if (_apiController.ServerState == ServerState.Connected)
		{
			if (ImGui.IsItemClicked())
			{
				ImGui.SetClipboardText(_apiController.DisplayName);
			}
			ThemedToolTip("Click to copy");
			if (!string.Equals(_apiController.DisplayName, _apiController.UID, StringComparison.Ordinal))
			{
				ImGui.TextColored(_theme.Accent, _apiController.UID);
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

	private void DrawFloatingResetButtonInToolbar(float overlayX, float overlayY, float spacing)
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
			ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
			ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));
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
				string? note = p.Key.GetNote();
				if (note == null || !note.Contains(_tabMenu.Filter, StringComparison.OrdinalIgnoreCase))
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
		{
			return (from keyValuePair in u
				orderby keyValuePair.Key.IsVisible descending, keyValuePair.Key.IsOnline descending
				select keyValuePair).ThenBy<KeyValuePair<Pair, List<GroupFullInfoDto>>, string>(AlphabeticalSort, StringComparer.OrdinalIgnoreCase).ToDictionary((KeyValuePair<Pair, List<GroupFullInfoDto>> keyValuePair) => keyValuePair.Key, (KeyValuePair<Pair, List<GroupFullInfoDto>> keyValuePair) => keyValuePair.Value);
		}
		static bool FilterGroupUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u, GroupFullInfoDto groupFullInfoDto)
		{
			return u.Value.Exists((GroupFullInfoDto g) => string.Equals(g.GID, groupFullInfoDto.GID, StringComparison.Ordinal));
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
		bool FilterTagusers(KeyValuePair<Pair, List<GroupFullInfoDto>> u, string tagName)
		{
			if (u.Key.IsDirectlyPaired && !u.Key.IsOneSidedPair)
			{
				return _tagHandler.HasTag(u.Key.UserData.UID, tagName);
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

	private static Vector4 ThemedSemanticFromAccent(ThemePalette t, float hue)
	{
		RgbToHsv(t.Accent.X, t.Accent.Y, t.Accent.Z, out var _, out var s, out var v);
		s = MathF.Max(s, 0.5f);
		v = MathF.Max(v, 0.85f);
		HsvToRgb(hue, s, v, out var r, out var g, out var b);
		return new Vector4(r, g, b, t.Accent.W);
	}

	private Vector4 GetUidColor()
	{
		Vector4 success = ThemedSemanticFromAccent(_theme, 0.33f);
		Vector4 warning = ThemedSemanticFromAccent(_theme, 0.12f);
		Vector4 danger = ThemedSemanticFromAccent(_theme, 0f);
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
		ImGuiStylePtr style;
		if (_collapsed)
		{
			float spacing = 6f * ImGuiHelpers.GlobalScale;
			float btnH;
			float num = (btnH = 22f * ImGuiHelpers.GlobalScale);
			float leftPad = 10f * ImGuiHelpers.GlobalScale;
			style = ImGui.GetStyle();
			float rightPad = style.WindowPadding.X + 25f * ImGuiHelpers.GlobalScale;
			Vector2 winPos = ImGui.GetWindowPos();
			Vector2 crMin = ImGui.GetWindowContentRegionMin();
			float contentW = ImGui.GetWindowContentRegionMax().X - crMin.X;
			Vector2 headerMin = new Vector2(winPos.X + crMin.X, ImGui.GetCursorScreenPos().Y);
			Vector2 headerMax = new Vector2(headerMin.X + contentW, headerMin.Y + headerH);
			ImGui.GetWindowDrawList().AddRectFilled(headerMin, headerMax, ImGui.ColorConvertFloat4ToU32(_theme.HeaderBg), 10f);
			int buttonCount = 1;
			float buttonsW = num * (float)buttonCount + spacing * (float)(buttonCount - 1);
			float num2 = headerMax.X - rightPad - buttonsW;
			float btnStartY = headerMin.Y + (headerH - btnH) * 0.5f;
			string title = _titleBarText;
			Vector2 tSz = ImGui.CalcTextSize(title);
			float num3 = headerMin.X + leftPad;
			float tY = headerMin.Y + (headerH - tSz.Y) * 0.5f;
			ImGui.SetCursorScreenPos(new Vector2(num3, tY));
			ImGui.TextColored(_theme.TextPrimary, title);
			float dragLeft = num3 + tSz.X + spacing;
			float dragRight = num2 - spacing;
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
			ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
			ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));
			ImGui.SetCursorScreenPos(new Vector2(num2, btnStartY));
			using (ImRaii.PushColor(ImGuiCol.Text, _theme.BtnText))
			{
				if (_uiSharedService.IconButton(FontAwesomeIcon.Bars))
				{
					ImGui.OpenPopup("##hamburger_menu_collapsed");
				}
			}
			ThemedToolTip("Menu");
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
									if (ImGui.BeginPopup("##hamburger_menu_collapsed"))
									{
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
			ImGui.PopStyleVar(2);
			ImGui.PopStyleColor(4);
			return;
		}
		float spacing2 = 6f * ImGuiHelpers.GlobalScale;
		float btnSide = 22f * ImGuiHelpers.GlobalScale;
		style = ImGui.GetStyle();
		_ = ref style.WindowPadding;
		_ = ImGuiHelpers.GlobalScale;
		ImGuiStylePtr style2 = ImGui.GetStyle();
		float topOffset = style2.FramePadding.Y + style2.ItemSpacing.Y + ImGuiHelpers.GlobalScale;
		Vector2 windowPos = ImGui.GetWindowPos();
		Vector2 crMin2 = ImGui.GetWindowContentRegionMin();
		Vector2 crMax = ImGui.GetWindowContentRegionMax();
		float stripWidth = btnSide;
		float overlayX = windowPos.X + crMax.X - stripWidth - spacing2 - 15f;
		float overlayY = windowPos.Y + crMin2.Y + topOffset;
		ImGui.SetNextWindowPos(new Vector2(overlayX, overlayY), ImGuiCond.Always);
		ImGui.SetNextWindowBgAlpha(0f);
		ImGuiWindowFlags floatingFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking;
		if (base.AllowClickthrough)
		{
			floatingFlags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing;
		}
		ImGui.Begin("##xivsync-floating-controls", floatingFlags);
		ImGui.PushStyleColor(ImGuiCol.Button, _theme.Btn);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _theme.BtnHovered);
		ImGui.PushStyleColor(ImGuiCol.ButtonActive, _theme.BtnActive);
		ImGui.PushStyleColor(ImGuiCol.Text, _theme.BtnText);
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
		ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f));
		using (ImRaii.PushColor(ImGuiCol.Text, _theme.BtnText))
		{
			if (_uiSharedService.IconButton(FontAwesomeIcon.Bars))
			{
				ImGui.OpenPopup("##hamburger_menu");
			}
		}
		ThemedToolTip("Menu");
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
								if (ImGui.BeginPopup("##hamburger_menu"))
								{
									string collapseText2 = (_collapsed ? "Expand" : "Collapse");
									FontAwesomeIcon collapseIcon2 = (_collapsed ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronUp);
									ImU8String label = new ImU8String(2, 2);
									label.AppendFormatted(collapseIcon2.ToIconString());
									label.AppendLiteral("  ");
									label.AppendFormatted(collapseText2);
									if (ImGui.MenuItem(label))
									{
										_collapsed = !_collapsed;
									}
									ImGui.Separator();
									string connectText2 = (isConnectingOrConnected ? ("Disconnect from " + _serverManager.CurrentServer.ServerName) : ("Connect to " + _serverManager.CurrentServer.ServerName));
									FontAwesomeIcon connectIcon2 = (isConnectingOrConnected ? FontAwesomeIcon.Unlink : FontAwesomeIcon.Link);
									using (ImRaii.PushColor(ImGuiCol.Text, linkColor))
									{
										if (isBusy)
										{
											ImGui.BeginDisabled();
										}
										label = new ImU8String(2, 2);
										label.AppendFormatted(connectIcon2.ToIconString());
										label.AppendLiteral("  ");
										label.AppendFormatted(connectText2);
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
									bool isPinned2 = base.AllowPinning;
									label = new ImU8String(12, 1);
									label.AppendFormatted(FontAwesomeIcon.Thumbtack.ToIconString());
									label.AppendLiteral("  Pin Window");
									if (ImGui.MenuItem(label, "", isPinned2))
									{
										base.AllowPinning = !base.AllowPinning;
									}
									bool isClickThrough2 = base.AllowClickthrough;
									label = new ImU8String(15, 1);
									label.AppendFormatted(FontAwesomeIcon.MousePointer.ToIconString());
									label.AppendLiteral("  Click Through");
									if (ImGui.MenuItem(label, "", isClickThrough2))
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
		ImGui.PopStyleVar(2);
		ImGui.PopStyleColor(4);
		ImGui.End();
		if (_apiController.ServerState == ServerState.Connected && base.AllowClickthrough)
		{
			DrawFloatingResetButtonInToolbar(overlayX, overlayY, spacing2);
		}
	}

	private void DrawThemeInline()
	{
		ImGui.AlignTextToFramePadding();
		ImGui.TextColored(ScrambleColor(_theme.Accent), ScrambleText("Theme"));
		ImGui.SameLine();
		using (ImRaii.PushColor(ImGuiCol.Text, _theme.TextSecondary))
		{
			ImGui.TextUnformatted("(changes preview automatically)");
		}
		ImGui.Separator();
		ImGui.TextUnformatted("Preset");
		ImGui.SameLine();
		using (ImRaii.PushStyle(ImGuiStyleVar.PopupRounding, 8f))
		{
			using (ImRaii.PushColor(ImGuiCol.PopupBg, _theme.Btn))
			{
				using (ImRaii.PushColor(ImGuiCol.Border, _theme.PanelBorder))
				{
					using (ImRaii.PushColor(ImGuiCol.ScrollbarBg, _theme.Btn))
					{
						using (ImRaii.PushColor(ImGuiCol.ScrollbarGrab, _theme.BtnHovered))
						{
							using (ImRaii.PushColor(ImGuiCol.ScrollbarGrabHovered, _theme.Accent))
							{
								using (ImRaii.PushColor(ImGuiCol.ScrollbarGrabActive, _theme.BtnActive))
								{
									using (ImRaii.PushColor(ImGuiCol.Text, _theme.BtnText))
									{
										using (ImRaii.PushColor(ImGuiCol.FrameBg, _theme.Btn))
										{
											using (ImRaii.PushColor(ImGuiCol.FrameBgHovered, _theme.BtnHovered))
											{
												using (ImRaii.PushColor(ImGuiCol.FrameBgActive, _theme.BtnActive))
												{
													using (ImRaii.PushColor(ImGuiCol.Button, _theme.Btn))
													{
														using (ImRaii.PushColor(ImGuiCol.ButtonHovered, _theme.BtnHovered))
														{
															using (ImRaii.PushColor(ImGuiCol.ButtonActive, _theme.BtnActive))
															{
																using (ImRaii.PushColor(ImGuiCol.TextSelectedBg, _theme.BtnActive))
																{
																	string displayName = (IsThemeCustomized() ? "Custom" : _selectedPreset);
																	if (ImGui.BeginCombo("##theme-preset-inline", displayName))
																	{
																		foreach (KeyValuePair<string, ThemePalette> kv in ThemePresets.Presets)
																		{
																			bool sel = kv.Key == _selectedPreset;
																			if (ImGui.Selectable(kv.Key, sel))
																			{
																				_selectedPreset = kv.Key;
																				_lastSelectedPreset = kv.Key;
																				_themeWorking = Clone(kv.Value);
																				_theme = Clone(_themeWorking);
																			}
																			if (sel)
																			{
																				ImGui.SetItemDefaultFocus();
																			}
																		}
																		ImGui.EndCombo();
																	}
																}
															}
														}
													}
												}
											}
										}
									}
								}
							}
						}
					}
				}
			}
		}
		ImGui.Separator();
		DrawColorRow("Panel Background", () => _themeWorking.PanelBg, delegate(Vector4 v)
		{
			_themeWorking.PanelBg = v;
		});
		DrawColorRow("Panel Border", () => _themeWorking.PanelBorder, delegate(Vector4 v)
		{
			_themeWorking.PanelBorder = v;
		});
		DrawColorRow("Header Background", () => _themeWorking.HeaderBg, delegate(Vector4 v)
		{
			_themeWorking.HeaderBg = v;
		});
		DrawColorRow("Accent", () => _themeWorking.Accent, delegate(Vector4 v)
		{
			_themeWorking.Accent = v;
		});
		DrawColorRow("Button", () => _themeWorking.Btn, delegate(Vector4 v)
		{
			_themeWorking.Btn = v;
		});
		DrawColorRow("Button Hovered", () => _themeWorking.BtnHovered, delegate(Vector4 v)
		{
			_themeWorking.BtnHovered = v;
		});
		DrawColorRow("Button Active", () => _themeWorking.BtnActive, delegate(Vector4 v)
		{
			_themeWorking.BtnActive = v;
		});
		DrawColorRow("Button Text", () => _themeWorking.BtnText, delegate(Vector4 v)
		{
			_themeWorking.BtnText = v;
		});
		DrawColorRow("Button Text Hovered", () => _themeWorking.BtnTextHovered, delegate(Vector4 v)
		{
			_themeWorking.BtnTextHovered = v;
		});
		DrawColorRow("Button Text Active", () => _themeWorking.BtnTextActive, delegate(Vector4 v)
		{
			_themeWorking.BtnTextActive = v;
		});
		DrawColorRow("Text Primary", () => _themeWorking.TextPrimary, delegate(Vector4 v)
		{
			_themeWorking.TextPrimary = v;
		});
		DrawColorRow("Text Secondary", () => _themeWorking.TextSecondary, delegate(Vector4 v)
		{
			_themeWorking.TextSecondary = v;
		});
		DrawColorRow("Text Disabled", () => _themeWorking.TextDisabled, delegate(Vector4 v)
		{
			_themeWorking.TextDisabled = v;
		});
		DrawColorRow("Link", () => _themeWorking.Link, delegate(Vector4 v)
		{
			_themeWorking.Link = v;
		});
		DrawColorRow("Link Hover", () => _themeWorking.LinkHover, delegate(Vector4 v)
		{
			_themeWorking.LinkHover = v;
		});
		DrawColorRow("Tooltip Background", () => _themeWorking.TooltipBg, delegate(Vector4 v)
		{
			_themeWorking.TooltipBg = v;
		});
		DrawColorRow("Tooltip Text", () => _themeWorking.TooltipText, delegate(Vector4 v)
		{
			_themeWorking.TooltipText = v;
		});
		ImGui.Separator();
		using (ImRaii.PushColor(ImGuiCol.Text, _theme.BtnText))
		{
			if (ImGui.Button("Reset to Preset"))
			{
				_themeWorking = (ThemePresets.Presets.TryGetValue(_selectedPreset, out ThemePalette p) ? Clone(p) : new ThemePalette());
				_theme = Clone(_themeWorking);
			}
			ImGui.SameLine();
			if (ImGui.Button("Cancel"))
			{
				_showThemeInline = false;
			}
			ImGui.SameLine();
			if (ImGui.Button("Save"))
			{
				_theme = Clone(_themeWorking);
				PersistTheme(_theme);
				_showThemeInline = false;
			}
		}
		ImGui.Spacing();
	}

	private void ThemedToolTip(string text)
	{
		UiSharedService.AttachThemedToolTip(text, _theme);
	}

	private static ThemePalette Clone(ThemePalette p)
	{
		return new ThemePalette
		{
			PanelBg = p.PanelBg,
			PanelBorder = p.PanelBorder,
			HeaderBg = p.HeaderBg,
			Accent = p.Accent,
			TextPrimary = p.TextPrimary,
			TextSecondary = p.TextSecondary,
			TextDisabled = p.TextDisabled,
			Link = p.Link,
			LinkHover = p.LinkHover,
			Btn = p.Btn,
			BtnHovered = p.BtnHovered,
			BtnActive = p.BtnActive,
			BtnText = p.BtnText,
			BtnTextHovered = p.BtnTextHovered,
			BtnTextActive = p.BtnTextActive,
			TooltipBg = p.TooltipBg,
			TooltipText = p.TooltipText
		};
	}

	private void PersistTheme(ThemePalette theme)
	{
		try
		{
			_configService.Current.Theme = theme;
			_configService.Save();
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to save theme");
		}
	}

	private void DrawColorRow(string label, Func<Vector4> get, Action<Vector4> set)
	{
		float offsetFromStartX = 200f * ImGuiHelpers.GlobalScale;
		float swatchSize = 22f * ImGuiHelpers.GlobalScale;
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted(label);
		ImGui.SameLine(offsetFromStartX);
		ImGui.PushID(label);
		Vector4 color = get();
		ImGui.ColorButton("##swatch", in color, ImGuiColorEditFlags.AlphaPreviewHalf, new Vector2(swatchSize, swatchSize));
		if (ImGui.IsItemClicked())
		{
			ImGui.OpenPopup("picker");
		}
		if (ImGui.BeginPopup("picker"))
		{
			ImGuiColorEditFlags flags = ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.PickerHueWheel;
			ImGui.ColorPicker4("##picker", ref color, flags);
			set(color);
			_theme = Clone(_themeWorking);
			ImGui.EndPopup();
		}
		ImGui.PopID();
	}

	private bool ThemedLink(string text)
	{
		bool clicked = false;
		ImGui.TextColored(_theme.Link, text);
		bool hovered = ImGui.IsItemHovered();
		Vector2 min = ImGui.GetItemRectMin();
		Vector2 max = ImGui.GetItemRectMax();
		ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, max.Y), new Vector2(max.X, max.Y), ImGui.ColorConvertFloat4ToU32(hovered ? _theme.LinkHover : _theme.Link), 1f);
		if (ImGui.IsItemClicked())
		{
			clicked = true;
		}
		return clicked;
	}

	private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
	{
		float max = MathF.Max(r, MathF.Max(g, b));
		float min = MathF.Min(r, MathF.Min(g, b));
		v = max;
		float d = max - min;
		s = ((max <= 0f) ? 0f : (d / max));
		if (d <= 0f)
		{
			h = 0f;
			return;
		}
		if (max == r)
		{
			h = (g - b) / d % 6f;
		}
		else if (max == g)
		{
			h = (b - r) / d + 2f;
		}
		else
		{
			h = (r - g) / d + 4f;
		}
		h /= 6f;
		if (h < 0f)
		{
			h += 1f;
		}
	}

	private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
	{
		h = (h % 1f + 1f) % 1f;
		float c = v * s;
		float x = c * (1f - MathF.Abs(h * 6f % 2f - 1f));
		float m = v - c;
		float r2 = 0f;
		float g2 = 0f;
		float b2 = 0f;
		float seg = h * 6f;
		if (seg < 1f)
		{
			r2 = c;
			g2 = x;
			b2 = 0f;
		}
		else if (seg < 2f)
		{
			r2 = x;
			g2 = c;
			b2 = 0f;
		}
		else if (seg < 3f)
		{
			r2 = 0f;
			g2 = c;
			b2 = x;
		}
		else if (seg < 4f)
		{
			r2 = 0f;
			g2 = x;
			b2 = c;
		}
		else if (seg < 5f)
		{
			r2 = x;
			g2 = 0f;
			b2 = c;
		}
		else
		{
			r2 = c;
			g2 = 0f;
			b2 = x;
		}
		r = r2 + m;
		g = g2 + m;
		b = b2 + m;
	}

	private void DrawProximityVoice()
	{
		ThemePalette t = _theme;
		using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 10f))
		{
			using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8f, 8f)))
			{
				using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 1f))
				{
					using (ImRaii.PushColor(ImGuiCol.ChildBg, t.PanelBg))
					{
						using (ImRaii.PushColor(ImGuiCol.Border, t.PanelBorder))
						{
							using (ImRaii.PushColor(ImGuiCol.Button, t.Btn))
							{
								using (ImRaii.PushColor(ImGuiCol.ButtonHovered, t.BtnHovered))
								{
									using (ImRaii.PushColor(ImGuiCol.ButtonActive, t.BtnActive))
									{
										using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6f))
										{
											ImGui.BeginChild("voice-surface", new Vector2(_windowContentWidth, 0f), border: true);
											using (ImRaii.PushColor(ImGuiCol.Text, t.TextPrimary))
											{
												using (ImRaii.PushColor(ImGuiCol.TextDisabled, t.TextDisabled))
												{
													ImGui.AlignTextToFramePadding();
													ImGui.TextColored(t.Accent, "Proximity Voice");
													ImGui.SameLine();
													ImGui.Checkbox(" Enable", ref _voiceEnabled);
													ThemedToolTip("Toggle proximity voice on/off");
												}
											}
											float spacing = ImGui.GetStyle().ItemSpacing.X;
											Vector2 iconSz = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Microphone);
											ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - iconSz.X * 3f - spacing * 2f);
											using (ImRaii.Disabled(!_voiceEnabled))
											{
												FontAwesomeIcon micIcon = (_voiceMuted ? FontAwesomeIcon.MicrophoneSlash : FontAwesomeIcon.Microphone);
												using (ImRaii.PushColor(ImGuiCol.Text, t.BtnText))
												{
													if (_voiceMuted)
													{
														using (ImRaii.PushColor(ImGuiCol.Button, t.BtnActive))
														{
															if (_uiSharedService.IconButton(micIcon))
															{
																_voiceMuted = !_voiceMuted;
															}
														}
													}
													else if (_uiSharedService.IconButton(micIcon))
													{
														_voiceMuted = !_voiceMuted;
													}
												}
												ThemedToolTip(_voiceMuted ? "Unmute" : "Mute");
												ImGui.SameLine(0f, spacing);
												FontAwesomeIcon deafIcon = (_voiceDeafened ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute);
												using (ImRaii.PushColor(ImGuiCol.Text, t.BtnText))
												{
													if (_voiceDeafened)
													{
														using (ImRaii.PushColor(ImGuiCol.Button, t.BtnActive))
														{
															if (_uiSharedService.IconButton(deafIcon))
															{
																_voiceDeafened = !_voiceDeafened;
															}
														}
													}
													else if (_uiSharedService.IconButton(deafIcon))
													{
														_voiceDeafened = !_voiceDeafened;
													}
												}
												ThemedToolTip(_voiceDeafened ? "Undeafen" : "Deafen");
												ImGui.SameLine(0f, spacing);
												using (ImRaii.PushColor(ImGuiCol.Text, t.BtnText))
												{
													_uiSharedService.IconButton(FontAwesomeIcon.Cog);
												}
												ThemedToolTip("Configure proximity voice");
											}
											ImGui.EndChild();
										}
									}
								}
							}
						}
					}
				}
			}
		}
	}

	private void DrawUserCountOverlay()
	{
		if (_apiController.ServerState == ServerState.Connected && !_showThemeInline)
		{
			string userCount = _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture);
			Vector2 textSize = ImGui.CalcTextSize(userCount + " Users Online");
			Vector2 childPos = ImGui.GetWindowPos();
			Vector2 childSize = ImGui.GetWindowSize();
			Vector2 textPos = new Vector2(childPos.X + (childSize.X - textSize.X) / 2f, childPos.Y + childSize.Y - textSize.Y - 15f);
			ImDrawListPtr drawList = ImGui.GetWindowDrawList();
			uint accentColor = ImGui.ColorConvertFloat4ToU32(_theme.Accent);
			Vector2 userCountSize = ImGui.CalcTextSize(userCount);
			drawList.AddText(textPos, accentColor, userCount);
			uint textColor = ImGui.ColorConvertFloat4ToU32(_theme.TextPrimary);
			Vector2 remainingTextPos = new Vector2(textPos.X + userCountSize.X, textPos.Y);
			drawList.AddText(remainingTextPos, textColor, " Users Online");
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
