using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Microsoft.Extensions.Logging;
using XIVSync.FileCache;
using XIVSync.Localization;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;

namespace XIVSync.UI;

public class IntroUi : WindowMediatorSubscriberBase
{
	private readonly MareConfigService _configService;

	private readonly CacheMonitor _cacheMonitor;

	private readonly Dictionary<string, string> _languages = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		{ "English", "en" },
		{ "Deutsch", "de" },
		{ "Français", "fr" }
	};

	private readonly ServerConfigurationManager _serverConfigurationManager;

	private readonly DalamudUtilService _dalamudUtilService;

	private readonly UiSharedService _uiShared;

	private int _currentLanguage;

	private bool _readFirstPage;

	private string _secretKey = string.Empty;

	private string _timeoutLabel = string.Empty;

	private Task? _timeoutTask;

	private string[]? _tosParagraphs;

	private bool _useLegacyLogin;

	private int _prevIdx = -1;

	public IntroUi(ILogger<IntroUi> logger, UiSharedService uiShared, MareConfigService configService, CacheMonitor fileCacheManager, ServerConfigurationManager serverConfigurationManager, MareMediator mareMediator, PerformanceCollectorService performanceCollectorService, DalamudUtilService dalamudUtilService)
		: base(logger, mareMediator, "XIVSync Setup", performanceCollectorService)
	{
		IntroUi introUi = this;
		_uiShared = uiShared;
		_configService = configService;
		_cacheMonitor = fileCacheManager;
		_serverConfigurationManager = serverConfigurationManager;
		_dalamudUtilService = dalamudUtilService;
		base.IsOpen = false;
		base.ShowCloseButton = false;
		base.RespectCloseHotkey = false;
		base.SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(600f, 400f),
			MaximumSize = new Vector2(600f, 2000f)
		};
		GetToSLocalization();
		base.Mediator.Subscribe<SwitchToMainUiMessage>(this, delegate
		{
			introUi.IsOpen = false;
		});
		base.Mediator.Subscribe<SwitchToIntroUiMessage>(this, delegate
		{
			introUi._configService.Current.UseCompactor = !dalamudUtilService.IsWine;
			introUi.IsOpen = true;
		});
	}

	protected override void DrawInternal()
	{
		if (_uiShared.IsInGpose)
		{
			return;
		}
		if (!_configService.Current.AcceptedAgreement && !_readFirstPage)
		{
			_uiShared.BigText("Welcome to XIVSync");
			ImGui.Separator();
			UiSharedService.TextWrapped("XIVSync is a plugin that will replicate your full current character state including all Penumbra mods to other paired XIVSync users. Note that you will have to have Penumbra as well as Glamourer installed to use this plugin.");
			UiSharedService.TextWrapped("We will have to setup a few things first before you can start using this plugin. Click on next to continue.");
			UiSharedService.ColorTextWrapped("Note: Any modifications you have applied through anything but Penumbra cannot be shared and your character state on other clients might look broken because of this or others players mods might not apply on your end altogether. If you want to use this plugin you will have to move your mods to Penumbra.", ImGuiColors.DalamudYellow);
			if (!_uiShared.DrawOtherPluginState())
			{
				return;
			}
			ImGui.Separator();
			if (!ImGui.Button("Next##toSetup"))
			{
				return;
			}
			_readFirstPage = true;
			_configService.Current.AcceptedAgreement = true;
			_configService.Save();
			_timeoutTask = Task.Run(async delegate
			{
				for (int i = 60; i > 0; i--)
				{
					_timeoutLabel = $"{Strings.ToS.ButtonWillBeAvailableIn} {i}s";
					await Task.Delay(TimeSpan.FromSeconds(1L)).ConfigureAwait(continueOnCapturedContext: false);
				}
			});
		}
		else if (!_configService.Current.AcceptedAgreement && _readFirstPage)
		{
			Vector2 textSize;
			using (_uiShared.UidFont.Push())
			{
				textSize = ImGui.CalcTextSize(Strings.ToS.LanguageLabel);
				ImGui.TextUnformatted(Strings.ToS.AgreementLabel);
			}
			ImGui.SameLine();
			Vector2 languageSize = ImGui.CalcTextSize(Strings.ToS.LanguageLabel);
			ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X - languageSize.X - 80f);
			ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textSize.Y / 2f - languageSize.Y / 2f);
			ImGui.TextUnformatted(Strings.ToS.LanguageLabel);
			ImGui.SameLine();
			ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textSize.Y / 2f - (languageSize.Y + ImGui.GetStyle().FramePadding.Y) / 2f);
			ImGui.SetNextItemWidth(80f);
			if (ImGui.Combo((ImU8String)"", ref _currentLanguage, (ReadOnlySpan<string>)_languages.Keys.ToArray(), _languages.Count))
			{
				GetToSLocalization(_currentLanguage);
			}
			ImGui.Separator();
			ImGui.SetWindowFontScale(1.5f);
			string readLabel = Strings.ToS.ReadLabel;
			textSize = ImGui.CalcTextSize(readLabel);
			ImGui.SetCursorPosX(ImGui.GetWindowSize().X / 2f - textSize.X / 2f);
			UiSharedService.ColorText(readLabel, ImGuiColors.DalamudRed);
			ImGui.SetWindowFontScale(1f);
			ImGui.Separator();
			UiSharedService.TextWrapped(_tosParagraphs[0]);
			UiSharedService.TextWrapped(_tosParagraphs[1]);
			UiSharedService.TextWrapped(_tosParagraphs[2]);
			UiSharedService.TextWrapped(_tosParagraphs[3]);
			UiSharedService.TextWrapped(_tosParagraphs[4]);
			UiSharedService.TextWrapped(_tosParagraphs[5]);
			ImGui.Separator();
			Task? timeoutTask = _timeoutTask;
			if (timeoutTask == null || timeoutTask.IsCompleted)
			{
				if (ImGui.Button(Strings.ToS.AgreeLabel + "##toSetup"))
				{
					_configService.Current.AcceptedAgreement = true;
					_configService.Save();
				}
			}
			else
			{
				UiSharedService.TextWrapped(_timeoutLabel);
			}
		}
		else if (_configService.Current.AcceptedAgreement && (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !_configService.Current.InitialScanComplete || !Directory.Exists(_configService.Current.CacheFolder)))
		{
			using (_uiShared.UidFont.Push())
			{
				ImGui.TextUnformatted("File Storage Setup");
			}
			ImGui.Separator();
			if (!_uiShared.HasValidPenumbraModPath)
			{
				UiSharedService.ColorTextWrapped("You do not have a valid Penumbra path set. Open Penumbra and set up a valid path for the mod directory.", ImGuiColors.DalamudRed);
			}
			else
			{
				UiSharedService.TextWrapped("To not unnecessary download files already present on your computer, XIVSync will have to scan your Penumbra mod directory. Additionally, a local storage folder must be set where XIVSync will download other character files to. Once the storage folder is set and the scan complete, this page will automatically forward to registration at a service.");
				UiSharedService.TextWrapped("Note: The initial scan, depending on the amount of mods you have, might take a while. Please wait until it is completed.");
				UiSharedService.ColorTextWrapped("Warning: once past this step you should not delete the FileCache.csv of XIVSync in the Plugin Configurations folder of Dalamud. Otherwise on the next launch a full re-scan of the file cache database will be initiated.", ImGuiColors.DalamudYellow);
				UiSharedService.ColorTextWrapped("Warning: if the scan is hanging and does nothing for a long time, chances are high your Penumbra folder is not set up properly.", ImGuiColors.DalamudYellow);
				_uiShared.DrawCacheDirectorySetting();
			}
			if (!_cacheMonitor.IsScanRunning && !string.IsNullOrEmpty(_configService.Current.CacheFolder) && _uiShared.HasValidPenumbraModPath && Directory.Exists(_configService.Current.CacheFolder))
			{
				if (ImGui.Button("Start Scan##startScan"))
				{
					_cacheMonitor.InvokeScan();
				}
			}
			else
			{
				_uiShared.DrawFileScanState();
			}
			if (!_dalamudUtilService.IsWine)
			{
				bool useFileCompactor = _configService.Current.UseCompactor;
				if (ImGui.Checkbox("Use File Compactor", ref useFileCompactor))
				{
					_configService.Current.UseCompactor = useFileCompactor;
					_configService.Save();
				}
				UiSharedService.ColorTextWrapped("The File Compactor can save a tremendeous amount of space on the hard disk for downloads through Mare. It will incur a minor CPU penalty on download but can speed up loading of other characters. It is recommended to keep it enabled. You can change this setting later anytime in the Mare settings.", ImGuiColors.DalamudYellow);
			}
		}
		else if (!_uiShared.ApiController.ServerAlive)
		{
			using (_uiShared.UidFont.Push())
			{
				ImGui.TextUnformatted("Service Registration");
			}
			ImGui.Separator();
			UiSharedService.TextWrapped("To be able to use XIVSync you will have to register an account.");
			UiSharedService.TextWrapped("For the official XIVSync Servers the account creation will be handled on the official XIVSync Discord. Due to security risks for the server, there is no way to handle this sensibly otherwise.");
			UiSharedService.TextWrapped("If you want to register at the main server \"XIVSync Central Server\" join the Discord and follow the instructions as described in #mare-service.");
			if (ImGui.Button("Join the XIVSync Discord"))
			{
				Util.OpenLink("https://discord.gg/DUNmcCqcH3");
			}
			UiSharedService.TextWrapped("For all other non official services you will have to contact the appropriate service provider how to obtain a secret key.");
			UiSharedService.DistanceSeparator();
			UiSharedService.TextWrapped("Once you have registered you can connect to the service using the tools provided below.");
			int serverIdx = 0;
			ServerStorage selectedServer = _serverConfigurationManager.GetServerByIndex(serverIdx);
			using (ImRaii.IEndObject node = ImRaii.TreeNode("Advanced Options"))
			{
				if (node)
				{
					serverIdx = _uiShared.DrawServiceSelection(selectOnChange: true, showConnect: false);
					if (serverIdx != _prevIdx)
					{
						_uiShared.ResetOAuthTasksState();
						_prevIdx = serverIdx;
					}
					selectedServer = _serverConfigurationManager.GetServerByIndex(serverIdx);
					_useLegacyLogin = !selectedServer.UseOAuth2;
					if (ImGui.Checkbox("Use Legacy Login with Secret Key", ref _useLegacyLogin))
					{
						_serverConfigurationManager.GetServerByIndex(serverIdx).UseOAuth2 = !_useLegacyLogin;
						_serverConfigurationManager.Save();
					}
				}
			}
			if (_useLegacyLogin)
			{
				string buttonText = "Save";
				float buttonWidth = ((_secretKey.Length != 64) ? 0f : (ImGuiHelpers.GetButtonSize(buttonText).X + ImGui.GetStyle().ItemSpacing.X));
				Vector2 textSize2 = ImGui.CalcTextSize("Enter Secret Key");
				ImGuiHelpers.ScaledDummy(5f);
				UiSharedService.DrawGroupedCenteredColorText("Strongly consider to use OAuth2 to authenticate, if the server supports it (the current main server does). The authentication flow is simpler and you do not require to store or maintain Secret Keys. You already implicitly register using Discord, so the OAuth2 method will be cleaner and more straight-forward to use.", ImGuiColors.DalamudYellow, 500f);
				ImGuiHelpers.ScaledDummy(5f);
				ImGui.AlignTextToFramePadding();
				ImGui.TextUnformatted("Enter Secret Key");
				ImGui.SameLine();
				ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonWidth - textSize2.X);
				ImGui.InputText("", ref _secretKey, 64);
				if (_secretKey.Length > 0 && _secretKey.Length != 64)
				{
					UiSharedService.ColorTextWrapped("Your secret key must be exactly 64 characters long. Don't enter your Lodestone auth here.", ImGuiColors.DalamudRed);
				}
				else if (_secretKey.Length == 64 && !HexRegex().IsMatch(_secretKey))
				{
					UiSharedService.ColorTextWrapped("Your secret key can only contain ABCDEF and the numbers 0-9.", ImGuiColors.DalamudRed);
				}
				else
				{
					if (_secretKey.Length != 64)
					{
						return;
					}
					ImGui.SameLine();
					if (!ImGui.Button(buttonText))
					{
						return;
					}
					if (_serverConfigurationManager.CurrentServer == null)
					{
						_serverConfigurationManager.SelectServer(0);
					}
					if (!_serverConfigurationManager.CurrentServer.SecretKeys.Any())
					{
						_serverConfigurationManager.CurrentServer.SecretKeys.Add(_serverConfigurationManager.CurrentServer.SecretKeys.Select<KeyValuePair<int, SecretKey>, int>((KeyValuePair<int, SecretKey> k) => k.Key).LastOrDefault() + 1, new SecretKey
						{
							FriendlyName = $"Secret Key added on Setup ({DateTime.Now:yyyy-MM-dd})",
							Key = _secretKey
						});
						_serverConfigurationManager.AddCurrentCharacterToServer();
					}
					else
					{
						_serverConfigurationManager.CurrentServer.SecretKeys[0] = new SecretKey
						{
							FriendlyName = $"Secret Key added on Setup ({DateTime.Now:yyyy-MM-dd})",
							Key = _secretKey
						};
					}
					_secretKey = string.Empty;
					Task.Run(() => _uiShared.ApiController.CreateConnectionsAsync());
				}
				return;
			}
			if (string.IsNullOrEmpty(selectedServer.OAuthToken))
			{
				UiSharedService.TextWrapped("Press the button below to verify the server has OAuth2 capabilities. Afterwards, authenticate using Discord in the Browser window.");
				_uiShared.DrawOAuth(selectedServer);
				return;
			}
			UiSharedService.ColorTextWrapped("OAuth2 is connected. Linked to: Discord User " + _serverConfigurationManager.GetDiscordUserFromToken(selectedServer), ImGuiColors.HealerGreen);
			UiSharedService.TextWrapped("Now press the update UIDs button to get a list of all of your UIDs on the server.");
			_uiShared.DrawUpdateOAuthUIDsButton(selectedServer);
			string playerName = _dalamudUtilService.GetPlayerName();
			uint playerWorld = _dalamudUtilService.GetHomeWorldId();
			UiSharedService.TextWrapped("Once pressed, select the UID you want to use for your current character " + _dalamudUtilService.GetPlayerName() + ". If no UIDs are visible, make sure you are connected to the correct Discord account. If that is not the case, use the unlink button below (hold CTRL to unlink).");
			_uiShared.DrawUnlinkOAuthButton(selectedServer);
			Authentication auth = selectedServer.Authentications.Find((Authentication a) => string.Equals(a.CharacterName, playerName, StringComparison.Ordinal) && a.WorldId == playerWorld);
			if (auth == null)
			{
				auth = new Authentication
				{
					CharacterName = playerName,
					WorldId = playerWorld
				};
				selectedServer.Authentications.Add(auth);
				_serverConfigurationManager.Save();
			}
			_uiShared.DrawUIDComboForAuthentication(0, auth, selectedServer.ServerUri);
			using (ImRaii.Disabled(string.IsNullOrEmpty(auth.UID)))
			{
				if (_uiShared.IconTextButton(FontAwesomeIcon.Link, "Connect to Service"))
				{
					Task.Run(() => _uiShared.ApiController.CreateConnectionsAsync());
				}
			}
			if (string.IsNullOrEmpty(auth.UID))
			{
				UiSharedService.AttachToolTip("Select a UID to be able to connect to the service");
			}
		}
		else
		{
			base.Mediator.Publish(new SwitchToMainUiMessage());
			base.IsOpen = false;
		}
	}

	private void GetToSLocalization(int changeLanguageTo = -1)
	{
		if (changeLanguageTo != -1)
		{
			_uiShared.LoadLocalization(_languages.ElementAt(changeLanguageTo).Value);
		}
		_tosParagraphs = new string[6]
		{
			Strings.ToS.Paragraph1,
			Strings.ToS.Paragraph2,
			Strings.ToS.Paragraph3,
			Strings.ToS.Paragraph4,
			Strings.ToS.Paragraph5,
			Strings.ToS.Paragraph6
		};
	}

	[GeneratedRegex("^([A-F0-9]{2})+")]
	[GeneratedCode("System.Text.RegularExpressions.Generator", "9.0.12.6610")]
	private static Regex HexRegex()
	{
		return _003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__HexRegex_1.Instance;
	}
}
