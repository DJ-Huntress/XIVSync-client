using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;
using System.Threading;
using System.Threading.Tasks;
using Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Microsoft.Extensions.Logging;
using XIVSync.FileCache;
using XIVSync.Interop.Ipc;
using XIVSync.Localization;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.Utils;
using XIVSync.WebAPI;
using XIVSync.WebAPI.SignalR;

namespace XIVSync.UI;

public class UiSharedService : DisposableMediatorSubscriberBase
{
	public sealed record IconScaleData(Vector2 IconSize, Vector2 NormalizedIconScale, float OffsetX, float IconScaling);

	private record UIDAliasPair(string? UID, string? Alias);

	public const string TooltipSeparator = "--SEP--";

	public static readonly ImGuiWindowFlags PopupWindowFlags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

	public readonly FileDialogManager FileDialogManager;

	private const string _notesEnd = "##XIVSYNC_USER_NOTES_END##";

	private const string _notesStart = "##XIVSYNC_USER_NOTES_START##";

	private readonly ApiController _apiController;

	private readonly CacheMonitor _cacheMonitor;

	private readonly MareConfigService _configService;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly IpcManager _ipcManager;

	private readonly Dalamud.Localization _localization;

	private readonly IDalamudPluginInterface _pluginInterface;

	private readonly Dictionary<string, object?> _selectedComboItems = new Dictionary<string, object>(StringComparer.Ordinal);

	private readonly ServerConfigurationManager _serverConfigurationManager;

	private readonly ITextureProvider _textureProvider;

	private readonly TokenProvider _tokenProvider;

	private bool _brioExists;

	private bool _cacheDirectoryHasOtherFilesThanCache;

	private bool _cacheDirectoryIsValidPath = true;

	private bool _customizePlusExists;

	private string _customServerName = "";

	private string _customServerUri = "";

	private Task<Uri?>? _discordOAuthCheck;

	private Task<string?>? _discordOAuthGetCode;

	private CancellationTokenSource _discordOAuthGetCts = new CancellationTokenSource();

	private Task<Dictionary<string, string>>? _discordOAuthUIDs;

	private bool _glamourerExists;

	private bool _heelsExists;

	private bool _honorificExists;

	private bool _isDirectoryWritable;

	private bool _isOneDrive;

	private bool _isPenumbraDirectory;

	private bool _moodlesExists;

	private Dictionary<string, DateTime> _oauthTokenExpiry = new Dictionary<string, DateTime>();

	private bool _penumbraExists;

	private bool _petNamesExists;

	private int _serverSelectionIndex = -1;

	public static string DoubleNewLine => Environment.NewLine + Environment.NewLine;

	public ApiController ApiController => _apiController;

	public bool EditTrackerPosition { get; set; }

	public IFontHandle GameFont { get; init; }

	public bool HasValidPenumbraModPath
	{
		get
		{
			if (!(_ipcManager.Penumbra.ModDirectory ?? string.Empty).IsNullOrEmpty())
			{
				return Directory.Exists(_ipcManager.Penumbra.ModDirectory);
			}
			return false;
		}
	}

	public IFontHandle IconFont { get; init; }

	public bool IsInGpose => _dalamudUtil.IsInGpose;

	public Dictionary<uint, string> JobData => _dalamudUtil.JobData.Value;

	public string PlayerName => _dalamudUtil.GetPlayerName();

	public IFontHandle UidFont { get; init; }

	public Dictionary<ushort, string> WorldData => _dalamudUtil.WorldData.Value;

	public uint WorldId => _dalamudUtil.GetHomeWorldId();

	public UiSharedService(ILogger<UiSharedService> logger, IpcManager ipcManager, ApiController apiController, CacheMonitor cacheMonitor, FileDialogManager fileDialogManager, MareConfigService configService, DalamudUtilService dalamudUtil, IDalamudPluginInterface pluginInterface, ITextureProvider textureProvider, Dalamud.Localization localization, ServerConfigurationManager serverManager, TokenProvider tokenProvider, MareMediator mediator)
		: base(logger, mediator)
	{
		_ipcManager = ipcManager;
		_apiController = apiController;
		_cacheMonitor = cacheMonitor;
		FileDialogManager = fileDialogManager;
		_configService = configService;
		_dalamudUtil = dalamudUtil;
		_pluginInterface = pluginInterface;
		_textureProvider = textureProvider;
		_localization = localization;
		_serverConfigurationManager = serverManager;
		_tokenProvider = tokenProvider;
		_localization.SetupWithLangCode("en");
		_isDirectoryWritable = IsDirectoryWritable(_configService.Current.CacheFolder);
		base.Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, delegate
		{
			_penumbraExists = _ipcManager.Penumbra.APIAvailable;
			_glamourerExists = _ipcManager.Glamourer.APIAvailable;
			_customizePlusExists = _ipcManager.CustomizePlus.APIAvailable;
			_heelsExists = _ipcManager.Heels.APIAvailable;
			_honorificExists = _ipcManager.Honorific.APIAvailable;
			_moodlesExists = _ipcManager.Moodles.APIAvailable;
			_petNamesExists = _ipcManager.PetNames.APIAvailable;
			_brioExists = _ipcManager.Brio.APIAvailable;
		});
		UidFont = _pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(delegate(IFontAtlasBuildToolkit e)
		{
			e.OnPreBuild(delegate(IFontAtlasBuildToolkitPreBuild tk)
			{
				SafeFontConfig fontConfig = new SafeFontConfig
				{
					SizePx = 35f
				};
				tk.AddDalamudAssetFont(DalamudAsset.NotoSansJpMedium, in fontConfig);
			});
		});
		GameFont = _pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12));
		IconFont = _pluginInterface.UiBuilder.IconFontFixedWidthHandle;
	}

	public static void AttachToolTip(string text)
	{
		if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
		{
			return;
		}
		ImGui.BeginTooltip();
		ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
		if (text.Contains("--SEP--", StringComparison.Ordinal))
		{
			string[] splitText = text.Split("--SEP--", StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < splitText.Length; i++)
			{
				ImGui.TextUnformatted(splitText[i]);
				if (i != splitText.Length - 1)
				{
					ImGui.Separator();
				}
			}
		}
		else
		{
			ImGui.TextUnformatted(text);
		}
		ImGui.PopTextWrapPos();
		ImGui.EndTooltip();
	}

	public static void AttachThemedToolTip(string text, ThemePalette theme)
	{
		if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
		{
			return;
		}
		using (ImRaii.PushColor(ImGuiCol.PopupBg, theme.TooltipBg))
		{
			using (ImRaii.PushColor(ImGuiCol.Text, theme.TooltipText))
			{
				ImGui.BeginTooltip();
				ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
				if (text.Contains("--SEP--", StringComparison.Ordinal))
				{
					string[] splitText = text.Split("--SEP--", StringSplitOptions.RemoveEmptyEntries);
					for (int i = 0; i < splitText.Length; i++)
					{
						ImGui.TextUnformatted(splitText[i]);
						if (i != splitText.Length - 1)
						{
							ImGui.Separator();
						}
					}
				}
				else
				{
					ImGui.TextUnformatted(text);
				}
				ImGui.PopTextWrapPos();
				ImGui.EndTooltip();
			}
		}
	}

	public static string ByteToString(long bytes, bool addSuffix = true)
	{
		string[] suffix = new string[5] { "B", "KiB", "MiB", "GiB", "TiB" };
		double dblSByte = bytes;
		int i = 0;
		while (i < suffix.Length && bytes >= 1024)
		{
			dblSByte = (double)bytes / 1024.0;
			i++;
			bytes /= 1024;
		}
		if (addSuffix)
		{
			return $"{dblSByte:0.00} {suffix[i]}";
		}
		return $"{dblSByte:0.00}";
	}

	public static void CenterNextWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
	{
		Vector2 center = ImGui.GetMainViewport().GetCenter();
		ImGui.SetNextWindowPos(new Vector2(center.X - width / 2f, center.Y - height / 2f), cond);
	}

	public static uint Color(byte r, byte g, byte b, byte a)
	{
		return (uint)((((a << 8) + b << 8) + g << 8) + r);
	}

	public static uint Color(Vector4 color)
	{
		return (uint)(((((byte)(color.W * 255f) << 8) + (byte)(color.Z * 255f) << 8) + (byte)(color.Y * 255f) << 8) + (byte)(color.X * 255f));
	}

	public static void ColorText(string text, Vector4 color)
	{
		using (ImRaii.PushColor(ImGuiCol.Text, color))
		{
			ImGui.TextUnformatted(text);
		}
	}

	public static void ColorTextWrapped(string text, Vector4 color, float wrapPos = 0f)
	{
		using (ImRaii.PushColor(ImGuiCol.Text, color))
		{
			TextWrapped(text, wrapPos);
		}
	}

	public static bool CtrlPressed()
	{
		if ((GetKeyState(162) & 0x8000) == 0)
		{
			return (GetKeyState(163) & 0x8000) != 0;
		}
		return true;
	}

	public static void DrawGrouped(Action imguiDrawAction, float rounding = 5f, float? expectedWidth = null)
	{
		Vector2 cursorPos = ImGui.GetCursorPos();
		using (ImRaii.Group())
		{
			if (expectedWidth.HasValue)
			{
				ImGui.Dummy(new Vector2(expectedWidth.Value, 0f));
				ImGui.SetCursorPos(cursorPos);
			}
			imguiDrawAction();
		}
		ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin() - ImGui.GetStyle().ItemInnerSpacing, ImGui.GetItemRectMax() + ImGui.GetStyle().ItemInnerSpacing, Color(ImGuiColors.DalamudGrey2), rounding);
	}

	public static void DrawGroupedCenteredColorText(string text, Vector4 color, float? maxWidth = null)
	{
		float availWidth = ImGui.GetContentRegionAvail().X;
		float textWidth = ImGui.CalcTextSize(text, hideTextAfterDoubleHash: false, availWidth).X;
		if (maxWidth.HasValue && textWidth > maxWidth * ImGuiHelpers.GlobalScale)
		{
			textWidth = maxWidth.Value * ImGuiHelpers.GlobalScale;
		}
		ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availWidth / 2f - textWidth / 2f);
		DrawGrouped(delegate
		{
			ColorTextWrapped(text, color, ImGui.GetCursorPosX() + textWidth);
		}, 5f, (!maxWidth.HasValue) ? ((float?)null) : (maxWidth * ImGuiHelpers.GlobalScale));
	}

	public static void DrawOutlinedFont(string text, Vector4 fontColor, Vector4 outlineColor, int thickness)
	{
		Vector2 original = ImGui.GetCursorPos();
		using (ImRaii.PushColor(ImGuiCol.Text, outlineColor))
		{
			Vector2 cursorPos = original;
			cursorPos.Y = original.Y - (float)thickness;
			ImGui.SetCursorPos(cursorPos);
			ImGui.TextUnformatted(text);
			cursorPos = original;
			cursorPos.X = original.X - (float)thickness;
			ImGui.SetCursorPos(cursorPos);
			ImGui.TextUnformatted(text);
			cursorPos = original;
			cursorPos.Y = original.Y + (float)thickness;
			ImGui.SetCursorPos(cursorPos);
			ImGui.TextUnformatted(text);
			cursorPos = original;
			cursorPos.X = original.X + (float)thickness;
			ImGui.SetCursorPos(cursorPos);
			ImGui.TextUnformatted(text);
			cursorPos = original;
			cursorPos.X = original.X - (float)thickness;
			cursorPos.Y = original.Y - (float)thickness;
			ImGui.SetCursorPos(cursorPos);
			ImGui.TextUnformatted(text);
			cursorPos = original;
			cursorPos.X = original.X + (float)thickness;
			cursorPos.Y = original.Y + (float)thickness;
			ImGui.SetCursorPos(cursorPos);
			ImGui.TextUnformatted(text);
			cursorPos = original;
			cursorPos.X = original.X - (float)thickness;
			cursorPos.Y = original.Y + (float)thickness;
			ImGui.SetCursorPos(cursorPos);
			ImGui.TextUnformatted(text);
			cursorPos = original;
			cursorPos.X = original.X + (float)thickness;
			cursorPos.Y = original.Y - (float)thickness;
			ImGui.SetCursorPos(cursorPos);
			ImGui.TextUnformatted(text);
		}
		using (ImRaii.PushColor(ImGuiCol.Text, fontColor))
		{
			ImGui.SetCursorPos(original);
			ImGui.TextUnformatted(text);
			ImGui.SetCursorPos(original);
			ImGui.TextUnformatted(text);
		}
	}

	public static void DrawOutlinedFont(ImDrawListPtr drawList, string text, Vector2 textPos, uint fontColor, uint outlineColor, int thickness)
	{
		Vector2 pos = textPos;
		pos.Y = textPos.Y - (float)thickness;
		drawList.AddText(pos, outlineColor, text);
		pos = textPos;
		pos.X = textPos.X - (float)thickness;
		drawList.AddText(pos, outlineColor, text);
		pos = textPos;
		pos.Y = textPos.Y + (float)thickness;
		drawList.AddText(pos, outlineColor, text);
		pos = textPos;
		pos.X = textPos.X + (float)thickness;
		drawList.AddText(pos, outlineColor, text);
		drawList.AddText(new Vector2(textPos.X - (float)thickness, textPos.Y - (float)thickness), outlineColor, text);
		drawList.AddText(new Vector2(textPos.X + (float)thickness, textPos.Y + (float)thickness), outlineColor, text);
		drawList.AddText(new Vector2(textPos.X - (float)thickness, textPos.Y + (float)thickness), outlineColor, text);
		drawList.AddText(new Vector2(textPos.X + (float)thickness, textPos.Y - (float)thickness), outlineColor, text);
		drawList.AddText(textPos, fontColor, text);
		drawList.AddText(textPos, fontColor, text);
	}

	public static void DrawTree(string leafName, Action drawOnOpened, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
	{
		using ImRaii.IEndObject tree = ImRaii.TreeNode(leafName, flags);
		if (tree)
		{
			drawOnOpened();
		}
	}

	public static Vector4 GetBoolColor(bool input)
	{
		if (!input)
		{
			return ImGuiColors.DalamudRed;
		}
		return ImGuiColors.ParsedGreen;
	}

	public static string GetNotes(List<Pair> pairs)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("##XIVSYNC_USER_NOTES_START##");
		foreach (Pair entry in pairs)
		{
			if (!entry.GetNote().IsNullOrEmpty())
			{
				sb.Append(entry.UserData.UID).Append(":\"").Append(entry.GetNote())
					.AppendLine("\"");
			}
		}
		sb.AppendLine("##XIVSYNC_USER_NOTES_END##");
		return sb.ToString();
	}

	public static float GetWindowContentRegionWidth()
	{
		return ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
	}

	public static bool IsDirectoryWritable(string dirPath, bool throwIfFails = false)
	{
		try
		{
			using (File.Create(Path.Combine(dirPath, Path.GetRandomFileName()), 1, FileOptions.DeleteOnClose))
			{
				return true;
			}
		}
		catch
		{
			if (throwIfFails)
			{
				throw;
			}
			return false;
		}
	}

	public static void ScaledNextItemWidth(float width)
	{
		ImGui.SetNextItemWidth(width * ImGuiHelpers.GlobalScale);
	}

	public static void ScaledSameLine(float offset)
	{
		ImGui.SameLine(offset * ImGuiHelpers.GlobalScale);
	}

	public static void SetScaledWindowSize(float width, bool centerWindow = true)
	{
		float newLineHeight = ImGui.GetCursorPosY();
		ImGui.NewLine();
		newLineHeight = ImGui.GetCursorPosY() - newLineHeight;
		float y = ImGui.GetCursorPos().Y + ImGui.GetWindowContentRegionMin().Y - newLineHeight * 2f - ImGui.GetStyle().ItemSpacing.Y;
		SetScaledWindowSize(width, y, centerWindow, scaledHeight: true);
	}

	public static void SetScaledWindowSize(float width, float height, bool centerWindow = true, bool scaledHeight = false)
	{
		ImGui.SameLine();
		float x = width * ImGuiHelpers.GlobalScale;
		float y = (scaledHeight ? height : (height * ImGuiHelpers.GlobalScale));
		if (centerWindow)
		{
			CenterWindow(x, y);
		}
		ImGui.SetWindowSize(new Vector2(x, y));
	}

	public static bool ShiftPressed()
	{
		if ((GetKeyState(161) & 0x8000) == 0)
		{
			return (GetKeyState(160) & 0x8000) != 0;
		}
		return true;
	}

	public static void TextWrapped(string text, float wrapPos = 0f)
	{
		ImGui.PushTextWrapPos(wrapPos);
		ImGui.TextUnformatted(text);
		ImGui.PopTextWrapPos();
	}

	public static Vector4 UploadColor((long, long) data)
	{
		if (data.Item1 != 0L)
		{
			if (data.Item1 != data.Item2)
			{
				return ImGuiColors.DalamudYellow;
			}
			return ImGuiColors.ParsedGreen;
		}
		return ImGuiColors.DalamudGrey;
	}

	public bool ApplyNotesFromClipboard(string notes, bool overwrite)
	{
		List<string> splitNotes = notes.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
		string splitNotesStart = splitNotes.FirstOrDefault();
		string splitNotesEnd = splitNotes.LastOrDefault();
		if (!string.Equals(splitNotesStart, "##XIVSYNC_USER_NOTES_START##", StringComparison.Ordinal) || !string.Equals(splitNotesEnd, "##XIVSYNC_USER_NOTES_END##", StringComparison.Ordinal))
		{
			return false;
		}
		splitNotes.RemoveAll((string n) => string.Equals(n, "##XIVSYNC_USER_NOTES_START##", StringComparison.Ordinal) || string.Equals(n, "##XIVSYNC_USER_NOTES_END##", StringComparison.Ordinal));
		foreach (string note in splitNotes)
		{
			try
			{
				string[] array = note.Split(":", 2, StringSplitOptions.RemoveEmptyEntries);
				string uid = array[0];
				string comment = array[1].Trim('"');
				if (_serverConfigurationManager.GetNoteForUid(uid) == null || overwrite)
				{
					_serverConfigurationManager.SetNoteForUid(uid, comment);
				}
			}
			catch
			{
				base.Logger.LogWarning("Could not parse {note}", note);
			}
		}
		_serverConfigurationManager.SaveNotes();
		return true;
	}

	public void BigText(string text, Vector4? color = null)
	{
		FontText(text, UidFont, color);
	}

	public void BooleanToColoredIcon(bool value, bool inline = true)
	{
		using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, value))
		{
			using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed, !value))
			{
				if (inline)
				{
					ImGui.SameLine();
				}
				if (value)
				{
					IconText(FontAwesomeIcon.Check);
				}
				else
				{
					IconText(FontAwesomeIcon.Times);
				}
			}
		}
	}

	public void DrawCacheDirectorySetting()
	{
		ColorTextWrapped("Note: The storage folder should be somewhere close to root (i.e. C:\\XIVSync) in a new empty folder. DO NOT point this to your game folder. DO NOT point this to your Penumbra folder.", ImGuiColors.DalamudYellow);
		string cacheDirectory = _configService.Current.CacheFolder;
		ImGui.InputText("Storage Folder##cache", ref cacheDirectory, 255, ImGuiInputTextFlags.ReadOnly);
		ImGui.SameLine();
		using (ImRaii.Disabled(_cacheMonitor.MareWatcher != null))
		{
			if (IconButton(FontAwesomeIcon.Folder))
			{
				FileDialogManager.OpenFolderDialog("Pick XIVSync Storage Folder", delegate(bool success, string path)
				{
					if (success)
					{
						_isOneDrive = path.Contains("onedrive", StringComparison.OrdinalIgnoreCase);
						_isPenumbraDirectory = string.Equals(path.ToLowerInvariant(), _ipcManager.Penumbra.ModDirectory?.ToLowerInvariant(), StringComparison.Ordinal);
						string[] files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
						_cacheDirectoryHasOtherFilesThanCache = false;
						string[] array = files;
						foreach (string text in array)
						{
							string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
							if (fileNameWithoutExtension.Length != 40 && !string.Equals(fileNameWithoutExtension, "desktop", StringComparison.OrdinalIgnoreCase))
							{
								_cacheDirectoryHasOtherFilesThanCache = true;
								base.Logger.LogWarning("Found illegal file in {path}: {file}", path, text);
								break;
							}
						}
						string[] directories = Directory.GetDirectories(path);
						if (directories.Any())
						{
							_cacheDirectoryHasOtherFilesThanCache = true;
							base.Logger.LogWarning("Found folders in {path} not belonging to Mare: {dirs}", path, string.Join(", ", directories));
						}
						_isDirectoryWritable = IsDirectoryWritable(path);
						_cacheDirectoryIsValidPath = PathRegex().IsMatch(path);
						if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && _isDirectoryWritable && !_isPenumbraDirectory && !_isOneDrive && !_cacheDirectoryHasOtherFilesThanCache && _cacheDirectoryIsValidPath)
						{
							_configService.Current.CacheFolder = path;
							_configService.Save();
							_cacheMonitor.StartMareWatcher(path);
							_cacheMonitor.InvokeScan();
						}
					}
				}, _dalamudUtil.IsWine ? "Z:\\" : "C:\\");
			}
		}
		if (_cacheMonitor.MareWatcher != null)
		{
			AttachToolTip("Stop the Monitoring before changing the Storage folder. As long as monitoring is active, you cannot change the Storage folder location.");
		}
		if (_isPenumbraDirectory)
		{
			ColorTextWrapped("Do not point the storage path directly to the Penumbra directory. If necessary, make a subfolder in it.", ImGuiColors.DalamudRed);
		}
		else if (_isOneDrive)
		{
			ColorTextWrapped("Do not point the storage path to a folder in OneDrive. Do not use OneDrive folders for any Mod related functionality.", ImGuiColors.DalamudRed);
		}
		else if (!_isDirectoryWritable)
		{
			ColorTextWrapped("The folder you selected does not exist or cannot be written to. Please provide a valid path.", ImGuiColors.DalamudRed);
		}
		else if (_cacheDirectoryHasOtherFilesThanCache)
		{
			ColorTextWrapped("Your selected directory has files or directories inside that are not Mare related. Use an empty directory or a previous Mare storage directory only.", ImGuiColors.DalamudRed);
		}
		else if (!_cacheDirectoryIsValidPath)
		{
			ColorTextWrapped("Your selected directory contains illegal characters unreadable by FFXIV. Restrict yourself to latin letters (A-Z), underscores (_), dashes (-) and arabic numbers (0-9).", ImGuiColors.DalamudRed);
		}
		float maxCacheSize = (float)_configService.Current.MaxLocalCacheInGiB;
		if (ImGui.SliderFloat("Maximum Storage Size in GiB", ref maxCacheSize, 1f, 200f, "%.2f GiB"))
		{
			_configService.Current.MaxLocalCacheInGiB = maxCacheSize;
			_configService.Save();
		}
		DrawHelpText("The storage is automatically governed by Mare. It will clear itself automatically once it reaches the set capacity by removing the oldest unused files. You typically do not need to clear it yourself.");
	}

	public T? DrawCombo<T>(string comboName, IEnumerable<T> comboItems, Func<T?, string> toName, Action<T?>? onSelected = null, T? initialSelectedItem = default(T?))
	{
		if (!comboItems.Any())
		{
			return default(T);
		}
		if (!_selectedComboItems.TryGetValue(comboName, out object selectedItem) && selectedItem == null)
		{
			selectedItem = initialSelectedItem;
			_selectedComboItems[comboName] = selectedItem;
		}
		if (ImGui.BeginCombo(comboName, (selectedItem == null) ? "Unset Value" : toName((T)selectedItem)))
		{
			foreach (T item in comboItems)
			{
				bool isSelected = EqualityComparer<T>.Default.Equals(item, (T)selectedItem);
				if (ImGui.Selectable(toName(item), isSelected))
				{
					_selectedComboItems[comboName] = item;
					onSelected?.Invoke(item);
				}
			}
			ImGui.EndCombo();
		}
		return (T)_selectedComboItems[comboName];
	}

	public void DrawFileScanState()
	{
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("File Scanner Status");
		ImGui.SameLine();
		if (_cacheMonitor.IsScanRunning)
		{
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted("Scan is running");
			ImGui.TextUnformatted("Current Progress:");
			ImGui.SameLine();
			ImGui.TextUnformatted((_cacheMonitor.TotalFiles == 1) ? "Collecting files" : $"Processing {_cacheMonitor.CurrentFileProgress}/{_cacheMonitor.TotalFilesStorage} from storage ({_cacheMonitor.TotalFiles} scanned in)");
			AttachToolTip("Note: it is possible to have more files in storage than scanned in, this is due to the scanner normally ignoring those files but the game loading them in and using them on your character, so they get added to the local storage.");
			return;
		}
		if (_cacheMonitor.HaltScanLocks.Any<KeyValuePair<string, int>>((KeyValuePair<string, int> f) => f.Value > 0))
		{
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted("Halted (" + string.Join(", ", from locker in _cacheMonitor.HaltScanLocks
				where locker.Value > 0
				select locker.Key + ": " + locker.Value + " halt requests") + ")");
			ImGui.SameLine();
			if (ImGui.Button("Reset halt requests##clearlocks"))
			{
				_cacheMonitor.ResetLocks();
			}
			return;
		}
		ImGui.TextUnformatted("Idle");
		if (_configService.Current.InitialScanComplete)
		{
			ImGui.SameLine();
			if (IconTextButton(FontAwesomeIcon.Play, "Force rescan"))
			{
				_cacheMonitor.InvokeScan();
			}
		}
	}

	public void DrawHelpText(string helpText)
	{
		ImGui.SameLine();
		IconText(FontAwesomeIcon.QuestionCircle, ImGui.GetColorU32(ImGuiCol.TextDisabled));
		AttachToolTip(helpText);
	}

	public void DrawOAuth(ServerStorage selectedServer)
	{
		string oauthToken = selectedServer.OAuthToken;
		ImRaii.PushIndent(10f);
		if (oauthToken == null)
		{
			if (_discordOAuthCheck == null)
			{
				if (IconTextButton(FontAwesomeIcon.QuestionCircle, "Check if Server supports Discord OAuth2"))
				{
					_discordOAuthCheck = _serverConfigurationManager.CheckDiscordOAuth(selectedServer.ServerUri);
				}
			}
			else if (!_discordOAuthCheck.IsCompleted)
			{
				ColorTextWrapped("Checking OAuth2 compatibility with " + selectedServer.ServerUri, ImGuiColors.DalamudYellow);
			}
			else if (_discordOAuthCheck.Result != null)
			{
				ColorTextWrapped("Server is compatible with Discord OAuth2", ImGuiColors.HealerGreen);
			}
			else
			{
				ColorTextWrapped("Server is not compatible with Discord OAuth2", ImGuiColors.DalamudRed);
			}
			if (_discordOAuthCheck != null && _discordOAuthCheck.IsCompleted)
			{
				if (IconTextButton(FontAwesomeIcon.ArrowRight, "Authenticate with Server"))
				{
					_discordOAuthGetCode = _serverConfigurationManager.GetDiscordOAuthToken(_discordOAuthCheck.Result, selectedServer.ServerUri, _discordOAuthGetCts.Token);
				}
				else if (_discordOAuthGetCode != null && !_discordOAuthGetCode.IsCompleted)
				{
					TextWrapped("A browser window has been opened, follow it to authenticate. Click the button below if you accidentally closed the window and need to restart the authentication.");
					if (IconTextButton(FontAwesomeIcon.Ban, "Cancel Authentication"))
					{
						_discordOAuthGetCts = _discordOAuthGetCts.CancelRecreate();
						_discordOAuthGetCode = null;
					}
				}
				else if (_discordOAuthGetCode != null && _discordOAuthGetCode.IsCompleted)
				{
					TextWrapped("Discord OAuth is completed, status: ");
					ImGui.SameLine();
					if (_discordOAuthGetCode.Result != null)
					{
						selectedServer.OAuthToken = _discordOAuthGetCode.Result;
						_discordOAuthGetCode = null;
						_serverConfigurationManager.Save();
						ColorTextWrapped("Success", ImGuiColors.HealerGreen);
					}
					else
					{
						ColorTextWrapped("Failed, please check /xllog for more information", ImGuiColors.DalamudRed);
					}
				}
			}
		}
		if (oauthToken == null)
		{
			return;
		}
		if (!_oauthTokenExpiry.TryGetValue(oauthToken, out var tokenExpiry))
		{
			try
			{
				JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(oauthToken);
				DateTime dateTime = (_oauthTokenExpiry[oauthToken] = jwt.ValidTo);
				tokenExpiry = dateTime;
			}
			catch (Exception exception)
			{
				base.Logger.LogWarning(exception, "Could not parse OAuth token, deleting");
				selectedServer.OAuthToken = null;
				_serverConfigurationManager.Save();
			}
		}
		if (tokenExpiry > DateTime.UtcNow)
		{
			ColorTextWrapped("OAuth2 is enabled, linked to: Discord User " + _serverConfigurationManager.GetDiscordUserFromToken(selectedServer), ImGuiColors.HealerGreen);
			TextWrapped($"The OAuth2 token will expire on {tokenExpiry:yyyy-MM-dd} and automatically renew itself during login on or after {tokenExpiry - TimeSpan.FromDays(7):yyyy-MM-dd}.");
			using (ImRaii.Disabled(!CtrlPressed()))
			{
				if (IconTextButton(FontAwesomeIcon.Exclamation, "Renew OAuth2 token manually") && CtrlPressed())
				{
					_tokenProvider.TryUpdateOAuth2LoginTokenAsync(selectedServer, forced: true).ContinueWith((Task<bool> _) => _apiController.CreateConnectionsAsync());
				}
			}
			DrawHelpText("Hold CTRL to manually refresh your OAuth2 token. Normally you do not need to do this.");
			ImGuiHelpers.ScaledDummy(10f);
			if ((_discordOAuthUIDs == null || _discordOAuthUIDs.IsCompleted) && IconTextButton(FontAwesomeIcon.Question, "Check Discord Connection"))
			{
				_discordOAuthUIDs = _serverConfigurationManager.GetUIDsWithDiscordToken(selectedServer.ServerUri, oauthToken);
			}
			else if (_discordOAuthUIDs != null)
			{
				if (!_discordOAuthUIDs.IsCompleted)
				{
					ColorTextWrapped("Checking UIDs on Server", ImGuiColors.DalamudYellow);
				}
				else
				{
					int foundUids = _discordOAuthUIDs.Result?.Count ?? 0;
					KeyValuePair<string, string> primaryUid = _discordOAuthUIDs.Result?.FirstOrDefault() ?? new KeyValuePair<string, string>(string.Empty, string.Empty);
					string vanity = (string.IsNullOrEmpty(primaryUid.Value) ? "-" : primaryUid.Value);
					if (foundUids > 0)
					{
						ColorTextWrapped($"Found {foundUids} associated UIDs on the server, Primary UID: {primaryUid.Key} (Vanity UID: {vanity})", ImGuiColors.HealerGreen);
					}
					else
					{
						ColorTextWrapped("Found no UIDs associated to this linked OAuth2 account", ImGuiColors.DalamudRed);
					}
				}
			}
		}
		else
		{
			ColorTextWrapped("The OAuth2 token is stale and expired. Please renew the OAuth2 connection.", ImGuiColors.DalamudRed);
			if (IconTextButton(FontAwesomeIcon.Exclamation, "Renew OAuth2 connection"))
			{
				selectedServer.OAuthToken = null;
				_serverConfigurationManager.Save();
				_serverConfigurationManager.CheckDiscordOAuth(selectedServer.ServerUri).ContinueWith((Func<Task<Uri>, Task>)async delegate(Task<Uri?> urlTask)
				{
					Uri url = await urlTask.ConfigureAwait(continueOnCapturedContext: false);
					string token = await _serverConfigurationManager.GetDiscordOAuthToken(url, selectedServer.ServerUri, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
					selectedServer.OAuthToken = token;
					_serverConfigurationManager.Save();
					await _apiController.CreateConnectionsAsync().ConfigureAwait(continueOnCapturedContext: false);
				});
			}
		}
		DrawUnlinkOAuthButton(selectedServer);
	}

	public bool DrawOtherPluginState()
	{
		ImGui.TextUnformatted("Mandatory Plugins:");
		ImGui.SameLine(150f);
		ColorText("Penumbra", GetBoolColor(_penumbraExists));
		AttachToolTip("Penumbra is " + (_penumbraExists ? "available and up to date." : "unavailable or not up to date."));
		ImGui.SameLine();
		ColorText("Glamourer", GetBoolColor(_glamourerExists));
		AttachToolTip("Glamourer is " + (_glamourerExists ? "available and up to date." : "unavailable or not up to date."));
		ImGui.TextUnformatted("Optional Plugins:");
		ImGui.SameLine(150f);
		ColorText("SimpleHeels", GetBoolColor(_heelsExists));
		AttachToolTip("SimpleHeels is " + (_heelsExists ? "available and up to date." : "unavailable or not up to date."));
		ImGui.SameLine();
		ColorText("Customize+", GetBoolColor(_customizePlusExists));
		AttachToolTip("Customize+ is " + (_customizePlusExists ? "available and up to date." : "unavailable or not up to date."));
		ImGui.SameLine();
		ColorText("Honorific", GetBoolColor(_honorificExists));
		AttachToolTip("Honorific is " + (_honorificExists ? "available and up to date." : "unavailable or not up to date."));
		ImGui.SameLine();
		ColorText("Moodles", GetBoolColor(_moodlesExists));
		AttachToolTip("Moodles is " + (_moodlesExists ? "available and up to date." : "unavailable or not up to date."));
		ImGui.SameLine();
		ColorText("PetNicknames", GetBoolColor(_petNamesExists));
		AttachToolTip("PetNicknames is " + (_petNamesExists ? "available and up to date." : "unavailable or not up to date."));
		ImGui.SameLine();
		ColorText("Brio", GetBoolColor(_brioExists));
		AttachToolTip("Brio is " + (_brioExists ? "available and up to date." : "unavailable or not up to date."));
		if (!_penumbraExists || !_glamourerExists)
		{
			ImGui.TextColored(ImGuiColors.DalamudRed, "You need to install both Penumbra and Glamourer and keep them up to date to use XIVSync.");
			return false;
		}
		return true;
	}

	public int DrawServiceSelection(bool selectOnChange = false, bool showConnect = true)
	{
		string[] comboEntries = _serverConfigurationManager.GetServerNames();
		if (_serverSelectionIndex == -1)
		{
			_serverSelectionIndex = Array.IndexOf(_serverConfigurationManager.GetServerApiUrls(), _serverConfigurationManager.CurrentApiUrl);
		}
		if (_serverSelectionIndex == -1 || _serverSelectionIndex >= comboEntries.Length)
		{
			_serverSelectionIndex = 0;
		}
		for (int i = 0; i < comboEntries.Length; i++)
		{
			if (string.Equals(_serverConfigurationManager.CurrentServer?.ServerName, comboEntries[i], StringComparison.OrdinalIgnoreCase))
			{
				comboEntries[i] += " [Current]";
			}
		}
		if (ImGui.BeginCombo("Select Service", comboEntries[_serverSelectionIndex]))
		{
			for (int j = 0; j < comboEntries.Length; j++)
			{
				bool isSelected = _serverSelectionIndex == j;
				if (ImGui.Selectable(comboEntries[j], isSelected))
				{
					_serverSelectionIndex = j;
					if (selectOnChange)
					{
						_serverConfigurationManager.SelectServer(j);
					}
				}
				if (isSelected)
				{
					ImGui.SetItemDefaultFocus();
				}
			}
			ImGui.EndCombo();
		}
		if (showConnect)
		{
			ImGui.SameLine();
			string text = "Connect";
			if (_serverSelectionIndex == _serverConfigurationManager.CurrentServerIndex)
			{
				text = "Reconnect";
			}
			if (IconTextButton(FontAwesomeIcon.Link, text))
			{
				_serverConfigurationManager.SelectServer(_serverSelectionIndex);
				_apiController.CreateConnectionsAsync();
			}
		}
		if (ImGui.TreeNode("Add Custom Service"))
		{
			ImGui.SetNextItemWidth(250f);
			ImGui.InputText("Custom Service URI", ref _customServerUri, 255);
			ImGui.SetNextItemWidth(250f);
			ImGui.InputText("Custom Service Name", ref _customServerName, 255);
			if (IconTextButton(FontAwesomeIcon.Plus, "Add Custom Service") && !string.IsNullOrEmpty(_customServerUri) && !string.IsNullOrEmpty(_customServerName))
			{
				_serverConfigurationManager.AddServer(new ServerStorage
				{
					ServerName = _customServerName,
					ServerUri = _customServerUri,
					UseOAuth2 = true
				});
				_customServerName = string.Empty;
				_customServerUri = string.Empty;
				_configService.Save();
			}
			ImGui.TreePop();
		}
		return _serverSelectionIndex;
	}

	public void DrawUIDComboForAuthentication(int indexOffset, Authentication item, string serverUri, ILogger? logger = null)
	{
		using (ImRaii.Disabled(_discordOAuthUIDs == null))
		{
			object obj = _discordOAuthUIDs?.Result?.Select((KeyValuePair<string, string> t) => new UIDAliasPair(t.Key, t.Value)).ToList();
			if (obj == null)
			{
				int num = 1;
				obj = new List<UIDAliasPair>(num);
				CollectionsMarshal.SetCount((List<UIDAliasPair>)obj, num);
				Span<UIDAliasPair> span = CollectionsMarshal.AsSpan((List<UIDAliasPair>?)obj);
				int index = 0;
				span[index] = new UIDAliasPair(item.UID ?? null, null);
			}
			List<UIDAliasPair> aliasPairs = (List<UIDAliasPair>)obj;
			string uidComboName = "UID###" + item.CharacterName + item.WorldId + serverUri + indexOffset + aliasPairs.Count;
			DrawCombo(uidComboName, aliasPairs, delegate(UIDAliasPair? v)
			{
				if ((object)v == null)
				{
					return "No UID set";
				}
				if (!string.IsNullOrEmpty(v.Alias))
				{
					return v.UID + " (" + v.Alias + ")";
				}
				return string.IsNullOrEmpty(v.UID) ? "No UID set" : (v.UID ?? "");
			}, delegate(UIDAliasPair? v)
			{
				if (!string.Equals(v?.UID ?? null, item.UID, StringComparison.Ordinal))
				{
					item.UID = v?.UID ?? null;
					_serverConfigurationManager.Save();
				}
			}, aliasPairs.Find((UIDAliasPair f) => string.Equals(f.UID, item.UID, StringComparison.Ordinal)) ?? null);
		}
		if (_discordOAuthUIDs == null)
		{
			AttachToolTip("Use the button above to update your UIDs from the service before you can assign UIDs to characters.");
		}
	}

	public void DrawUnlinkOAuthButton(ServerStorage selectedServer)
	{
		using (ImRaii.Disabled(!CtrlPressed()))
		{
			if (IconTextButton(FontAwesomeIcon.Trash, "Unlink OAuth2 Connection") && CtrlPressed())
			{
				selectedServer.OAuthToken = null;
				_serverConfigurationManager.Save();
				ResetOAuthTasksState();
			}
		}
		DrawHelpText("Hold CTRL to unlink the current OAuth2 connection.");
	}

	public void DrawUpdateOAuthUIDsButton(ServerStorage selectedServer)
	{
		if (!selectedServer.UseOAuth2)
		{
			return;
		}
		using (ImRaii.Disabled(string.IsNullOrEmpty(selectedServer.OAuthToken)))
		{
			if ((_discordOAuthUIDs == null || _discordOAuthUIDs.IsCompleted) && IconTextButton(FontAwesomeIcon.ArrowsSpin, "Update UIDs from Service") && !string.IsNullOrEmpty(selectedServer.OAuthToken))
			{
				_discordOAuthUIDs = _serverConfigurationManager.GetUIDsWithDiscordToken(selectedServer.ServerUri, selectedServer.OAuthToken);
			}
		}
		DateTime tokenExpiry = DateTime.MinValue;
		if (!string.IsNullOrEmpty(selectedServer.OAuthToken) && !_oauthTokenExpiry.TryGetValue(selectedServer.OAuthToken, out tokenExpiry))
		{
			try
			{
				JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(selectedServer.OAuthToken);
				DateTime dateTime = (_oauthTokenExpiry[selectedServer.OAuthToken] = jwt.ValidTo);
				tokenExpiry = dateTime;
			}
			catch (Exception exception)
			{
				base.Logger.LogWarning(exception, "Could not parse OAuth token, deleting");
				selectedServer.OAuthToken = null;
				_serverConfigurationManager.Save();
				tokenExpiry = DateTime.MinValue;
			}
		}
		if (string.IsNullOrEmpty(selectedServer.OAuthToken) || tokenExpiry < DateTime.UtcNow)
		{
			ColorTextWrapped("You have no OAuth token or the OAuth token is expired. Please use the Service Configuration to link your OAuth2 account or refresh the token.", ImGuiColors.DalamudRed);
		}
	}

	public Vector2 GetIconButtonSize(FontAwesomeIcon icon)
	{
		using (IconFont.Push())
		{
			return ImGuiHelpers.GetButtonSize(icon.ToIconString());
		}
	}

	public Vector2 GetIconSize(FontAwesomeIcon icon)
	{
		using (IconFont.Push())
		{
			return ImGui.CalcTextSize(icon.ToIconString());
		}
	}

	public float GetIconTextButtonSize(FontAwesomeIcon icon, string text)
	{
		Vector2 vector;
		using (IconFont.Push())
		{
			vector = ImGui.CalcTextSize(icon.ToIconString());
		}
		Vector2 vector2 = ImGui.CalcTextSize(text);
		float num = 3f * ImGuiHelpers.GlobalScale;
		return vector.X + vector2.X + ImGui.GetStyle().FramePadding.X * 2f + num;
	}

	public bool IconButton(FontAwesomeIcon icon, float? height = null)
	{
		string text = icon.ToIconString();
		ThemePalette theme = GetCurrentTheme();
		ImGui.PushID(text);
		int colorCount = 0;
		if (theme != null)
		{
			ImGui.PushStyleColor(ImGuiCol.Button, theme.Btn);
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.BtnHovered);
			ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.BtnActive);
			colorCount = 3;
		}
		Vector2 vector;
		using (IconFont.Push())
		{
			vector = ImGui.CalcTextSize(text);
		}
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float x = vector.X + ImGui.GetStyle().FramePadding.X * 2f;
		float frameHeight = height ?? ImGui.GetFrameHeight();
		bool result = ImGui.Button(string.Empty, new Vector2(x, frameHeight));
		uint textColor = ImGui.GetColorU32(ImGuiCol.Text);
		if (theme != null)
		{
			textColor = (ImGui.IsItemActive() ? ImGui.GetColorU32(theme.BtnTextActive) : ((!ImGui.IsItemHovered()) ? ImGui.GetColorU32(theme.BtnText) : ImGui.GetColorU32(theme.BtnTextHovered)));
		}
		Vector2 pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X, cursorScreenPos.Y + (height ?? ImGui.GetFrameHeight()) / 2f - vector.Y / 2f);
		using (IconFont.Push())
		{
			windowDrawList.AddText(pos, textColor, text);
		}
		ImGui.PopID();
		if (colorCount > 0)
		{
			ImGui.PopStyleColor(colorCount);
		}
		return result;
	}

	public void IconText(FontAwesomeIcon icon, uint color)
	{
		FontText(icon.ToIconString(), IconFont, color);
	}

	public void IconText(FontAwesomeIcon icon, Vector4? color = null)
	{
		IconText(icon, (!color.HasValue) ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(color.Value));
	}

	public bool IconTextButton(FontAwesomeIcon icon, string text, float? width = null, bool isInPopup = false)
	{
		ThemePalette theme = GetCurrentTheme();
		return IconTextButtonInternal(icon, text, theme, isInPopup ? new Vector4?(ColorHelpers.RgbaUintToVector4(ImGui.GetColorU32(ImGuiCol.PopupBg))) : ((Vector4?)null), (width <= 0f) ? ((float?)null) : width);
	}

	public IDalamudTextureWrap LoadImage(byte[] imageData)
	{
		return _textureProvider.CreateFromImageAsync(imageData).Result;
	}

	public void LoadLocalization(string languageCode)
	{
		_localization.SetupWithLangCode(languageCode);
		Strings.ToS = new Strings.ToSStrings();
	}

	internal static void DistanceSeparator()
	{
		ImGuiHelpers.ScaledDummy(5f);
		ImGui.Separator();
		ImGuiHelpers.ScaledDummy(5f);
	}

	[DllImport("user32", ExactSpelling = true)]
	[LibraryImport("user32")]
	internal static extern short GetKeyState(int nVirtKey);

	internal void ResetOAuthTasksState()
	{
		_discordOAuthCheck = null;
		_discordOAuthGetCts = _discordOAuthGetCts.CancelRecreate();
		_discordOAuthGetCode = null;
		_discordOAuthUIDs = null;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			base.Dispose(disposing);
			_discordOAuthGetCts?.CancelDispose();
			UidFont.Dispose();
			GameFont.Dispose();
		}
	}

	private static void CenterWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
	{
		Vector2 center = ImGui.GetMainViewport().GetCenter();
		ImGui.SetWindowPos(new Vector2(center.X - width / 2f, center.Y - height / 2f), cond);
	}

	[GeneratedRegex("^(?:[a-zA-Z]:\\\\[\\w\\s\\-\\\\]+?|\\/(?:[\\w\\s\\-\\/])+?)$", RegexOptions.ECMAScript, 5000)]
	[GeneratedCode("System.Text.RegularExpressions.Generator", "9.0.12.6610")]
	private static Regex PathRegex()
	{
		return _003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__PathRegex_2.Instance;
	}

	private static void FontText(string text, IFontHandle font, Vector4? color = null)
	{
		FontText(text, font, (!color.HasValue) ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(color.Value));
	}

	private static void FontText(string text, IFontHandle font, uint color)
	{
		using (font.Push())
		{
			using (ImRaii.PushColor(ImGuiCol.Text, color))
			{
				ImGui.TextUnformatted(text);
			}
		}
	}

	private bool IconTextButtonInternal(FontAwesomeIcon icon, string text, ThemePalette? theme = null, Vector4? defaultColor = null, float? width = null)
	{
		int num = 0;
		if (defaultColor.HasValue)
		{
			ImGui.PushStyleColor(ImGuiCol.Button, defaultColor.Value);
			num++;
		}
		else if (theme != null)
		{
			ImGui.PushStyleColor(ImGuiCol.Button, theme.Btn);
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, theme.BtnHovered);
			ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.BtnActive);
			num += 3;
		}
		ImGui.PushID(text);
		Vector2 vector;
		using (IconFont.Push())
		{
			vector = ImGui.CalcTextSize(icon.ToIconString());
		}
		Vector2 vector2 = ImGui.CalcTextSize(text);
		ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
		Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
		float num2 = 3f * ImGuiHelpers.GlobalScale;
		float x = width ?? (vector.X + vector2.X + ImGui.GetStyle().FramePadding.X * 2f + num2);
		float frameHeight = ImGui.GetFrameHeight();
		bool result = ImGui.Button(string.Empty, new Vector2(x, frameHeight));
		uint textColor = ImGui.GetColorU32(ImGuiCol.Text);
		if (theme != null)
		{
			textColor = (ImGui.IsItemActive() ? ImGui.GetColorU32(theme.BtnTextActive) : ((!ImGui.IsItemHovered()) ? ImGui.GetColorU32(theme.BtnText) : ImGui.GetColorU32(theme.BtnTextHovered)));
		}
		Vector2 pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X, cursorScreenPos.Y + ImGui.GetStyle().FramePadding.Y);
		using (IconFont.Push())
		{
			windowDrawList.AddText(pos, textColor, icon.ToIconString());
		}
		Vector2 pos2 = new Vector2(pos.X + vector.X + num2, cursorScreenPos.Y + ImGui.GetStyle().FramePadding.Y);
		windowDrawList.AddText(pos2, textColor, text);
		ImGui.PopID();
		if (num > 0)
		{
			ImGui.PopStyleColor(num);
		}
		return result;
	}

	public ThemePalette? GetCurrentTheme()
	{
		return _configService.Current?.Theme;
	}
}
