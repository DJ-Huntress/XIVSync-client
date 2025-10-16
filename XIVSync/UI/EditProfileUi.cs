using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using XIVSync.API.Data;
using XIVSync.API.Dto.User;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.WebAPI;

namespace XIVSync.UI;

public class EditProfileUi : WindowMediatorSubscriberBase
{
	private readonly ApiController _apiController;

	private readonly FileDialogManager _fileDialogManager;

	private readonly MareProfileManager _mareProfileManager;

	private readonly UiSharedService _uiSharedService;

	private bool _adjustedForScollBarsLocalProfile;

	private bool _adjustedForScollBarsOnlineProfile;

	private string _descriptionText = string.Empty;

	private IDalamudTextureWrap? _pfpTextureWrap;

	private string _profileDescription = string.Empty;

	private byte[] _profileImage = Array.Empty<byte>();

	private bool _showFileDialogError;

	private bool _wasOpen;

	public EditProfileUi(ILogger<EditProfileUi> logger, MareMediator mediator, ApiController apiController, UiSharedService uiSharedService, FileDialogManager fileDialogManager, MareProfileManager mareProfileManager, PerformanceCollectorService performanceCollectorService)
		: base(logger, mediator, "XIVSync Edit Profile###XIVSyncEditProfileUI", performanceCollectorService)
	{
		base.IsOpen = false;
		base.SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(768f, 512f),
			MaximumSize = new Vector2(768f, 2000f)
		};
		_apiController = apiController;
		_uiSharedService = uiSharedService;
		_fileDialogManager = fileDialogManager;
		_mareProfileManager = mareProfileManager;
		base.Mediator.Subscribe<GposeStartMessage>(this, delegate
		{
			_wasOpen = base.IsOpen;
			base.IsOpen = false;
		});
		base.Mediator.Subscribe<GposeEndMessage>(this, delegate
		{
			base.IsOpen = _wasOpen;
		});
		base.Mediator.Subscribe<DisconnectedMessage>(this, delegate
		{
			base.IsOpen = false;
		});
		base.Mediator.Subscribe(this, delegate(ClearProfileDataMessage msg)
		{
			if (msg.UserData == null || string.Equals(msg.UserData.UID, _apiController.UID, StringComparison.Ordinal))
			{
				_pfpTextureWrap?.Dispose();
				_pfpTextureWrap = null;
			}
		});
	}

	protected override void DrawInternal()
	{
		_uiSharedService.BigText("Current Profile (as saved on server)");
		MareProfileData profile = _mareProfileManager.GetMareProfile(new UserData(_apiController.UID));
		if (profile.IsFlagged)
		{
			UiSharedService.ColorTextWrapped(profile.Description, ImGuiColors.DalamudRed);
			return;
		}
		if (!_profileImage.SequenceEqual(profile.ImageData.Value))
		{
			_profileImage = profile.ImageData.Value;
			_pfpTextureWrap?.Dispose();
			_pfpTextureWrap = _uiSharedService.LoadImage(_profileImage);
		}
		if (!string.Equals(_profileDescription, profile.Description, StringComparison.OrdinalIgnoreCase))
		{
			_profileDescription = profile.Description;
			_descriptionText = _mareProfileManager.GetProfileDescriptionWithoutStatus(new UserData(_apiController.UID));
		}
		if (_pfpTextureWrap != null)
		{
			ImGui.Image(_pfpTextureWrap.Handle, ImGuiHelpers.ScaledVector2(_pfpTextureWrap.Width, _pfpTextureWrap.Height));
		}
		float spacing = ImGui.GetStyle().ItemSpacing.X;
		ImGuiHelpers.ScaledRelativeSameLine(256f, spacing);
		using (_uiSharedService.GameFont.Push())
		{
			string filteredDescription = _mareProfileManager.GetProfileDescriptionWithoutStatus(new UserData(_apiController.UID));
			Vector2 vector = ImGui.CalcTextSize(filteredDescription, hideTextAfterDoubleHash: false, 256f);
			Vector2 childFrame = ImGuiHelpers.ScaledVector2(256f + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 256f);
			if (vector.Y > childFrame.Y)
			{
				_adjustedForScollBarsOnlineProfile = true;
			}
			else
			{
				_adjustedForScollBarsOnlineProfile = false;
			}
			Vector2 vector2 = childFrame;
			vector2.X = childFrame.X + (_adjustedForScollBarsOnlineProfile ? ImGui.GetStyle().ScrollbarSize : 0f);
			childFrame = vector2;
			if (ImGui.BeginChildFrame(101u, childFrame))
			{
				UiSharedService.TextWrapped(filteredDescription);
			}
			ImGui.EndChildFrame();
		}
		bool nsfw = profile.IsNSFW;
		ImGui.BeginDisabled();
		ImGui.Checkbox("Is NSFW", ref nsfw);
		ImGui.EndDisabled();
		ImGui.Separator();
		_uiSharedService.BigText("Notes and Rules for Profiles");
		ImU8String text = new ImU8String(851, 6);
		text.AppendLiteral("- All users that are paired and unpaused with you will be able to see your profile picture and description.");
		text.AppendFormatted(Environment.NewLine);
		text.AppendLiteral("- Other users have the possibility to report your profile for breaking the rules.");
		text.AppendFormatted(Environment.NewLine);
		text.AppendLiteral("- !!! AVOID: anything as profile image that can be considered highly illegal or obscene (bestiality, anything that could be considered a sexual act with a minor (that includes Lalafells), etc.)");
		text.AppendFormatted(Environment.NewLine);
		text.AppendLiteral("- !!! AVOID: slurs of any kind in the description that can be considered highly offensive");
		text.AppendFormatted(Environment.NewLine);
		text.AppendLiteral("- In case of valid reports from other users this can lead to disabling your profile forever or terminating your Mare account indefinitely.");
		text.AppendFormatted(Environment.NewLine);
		text.AppendLiteral("- Judgement of your profile validity from reports through staff is not up to debate and the decisions to disable your profile/account permanent.");
		text.AppendFormatted(Environment.NewLine);
		text.AppendLiteral("- If your profile picture or profile description could be considered NSFW, enable the toggle below.");
		ImGui.TextWrapped(text);
		ImGui.Separator();
		_uiSharedService.BigText("Profile Settings");
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.FileUpload, "Upload new profile picture"))
		{
			_fileDialogManager.OpenFileDialog("Select new Profile picture", ".png", delegate(bool success, string file)
			{
				if (success)
				{
					Task.Run(async delegate
					{
						byte[] fileContent = File.ReadAllBytes(file);
						using MemoryStream ms = new MemoryStream(fileContent);
						if (!(await Image.DetectFormatAsync(ms).ConfigureAwait(continueOnCapturedContext: false)).FileExtensions.Contains<string>("png", StringComparer.OrdinalIgnoreCase))
						{
							_showFileDialogError = true;
							return;
						}
						using Image<Rgba32> image = Image.Load<Rgba32>(fileContent);
						if (image.Width > 256 || image.Height > 256 || fileContent.Length > 256000)
						{
							_showFileDialogError = true;
						}
						else
						{
							_showFileDialogError = false;
							await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, null, Convert.ToBase64String(fileContent), null)).ConfigureAwait(continueOnCapturedContext: false);
						}
					});
				}
			});
		}
		UiSharedService.AttachToolTip("Select and upload a new profile picture");
		ImGui.SameLine();
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear uploaded profile picture"))
		{
			_apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, null, "", null));
		}
		UiSharedService.AttachToolTip("Clear your currently uploaded profile picture");
		if (_showFileDialogError)
		{
			UiSharedService.ColorTextWrapped("The profile picture must be a PNG file with a maximum height and width of 256px and 250KiB size", ImGuiColors.DalamudRed);
		}
		bool isNsfw = profile.IsNSFW;
		if (ImGui.Checkbox("Profile is NSFW", ref isNsfw))
		{
			_apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, isNsfw, null, null));
		}
		_uiSharedService.DrawHelpText("If your profile description or image can be considered NSFW, toggle this to ON");
		int widthTextBox = 400;
		float cursorPosX = ImGui.GetCursorPosX();
		text = new ImU8String(17, 1);
		text.AppendLiteral("Description ");
		text.AppendFormatted(_descriptionText.Length);
		text.AppendLiteral("/1500");
		ImGui.TextUnformatted(text);
		ImGui.SetCursorPosX(cursorPosX);
		ImGuiHelpers.ScaledRelativeSameLine(widthTextBox, ImGui.GetStyle().ItemSpacing.X);
		ImGui.TextUnformatted("Preview (approximate)");
		using (_uiSharedService.GameFont.Push())
		{
			ImGui.InputTextMultiline("##description", ref _descriptionText, 1500, ImGuiHelpers.ScaledVector2(widthTextBox, 200f));
		}
		ImGui.SameLine();
		using (_uiSharedService.GameFont.Push())
		{
			Vector2 vector3 = ImGui.CalcTextSize(_descriptionText, hideTextAfterDoubleHash: false, 256f);
			Vector2 childFrameLocal = ImGuiHelpers.ScaledVector2(256f + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 200f);
			if (vector3.Y > childFrameLocal.Y)
			{
				_adjustedForScollBarsLocalProfile = true;
			}
			else
			{
				_adjustedForScollBarsLocalProfile = false;
			}
			Vector2 vector2 = childFrameLocal;
			vector2.X = childFrameLocal.X + (_adjustedForScollBarsLocalProfile ? ImGui.GetStyle().ScrollbarSize : 0f);
			childFrameLocal = vector2;
			if (ImGui.BeginChildFrame(102u, childFrameLocal))
			{
				UiSharedService.TextWrapped(_descriptionText);
			}
			ImGui.EndChildFrame();
		}
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save Description"))
		{
			string fullDescription = BuildDescriptionWithStatus(_descriptionText);
			_apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, null, null, fullDescription));
		}
		UiSharedService.AttachToolTip("Sets your profile description text");
		ImGui.SameLine();
		if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear Description"))
		{
			string fullDescription = BuildDescriptionWithStatus("");
			_apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, null, null, fullDescription));
		}
		UiSharedService.AttachToolTip("Clears your profile description text");
	}

	private string BuildDescriptionWithStatus(string profileContent)
	{
		string[] lines = _mareProfileManager.GetMareProfile(new UserData(_apiController.UID)).Description.Split(new char[2] { '\r', '\n' }, StringSplitOptions.None);
		List<string> statusSection = new List<string>();
		if (lines.Length != 0 && lines[0].Trim().StartsWith("STATUS:"))
		{
			statusSection.Add(lines[0]);
			if (lines.Length > 1 && lines[1].Trim() == "---")
			{
				statusSection.Add(lines[1]);
			}
		}
		List<string> newDescriptionParts = new List<string>();
		newDescriptionParts.AddRange(statusSection);
		if (!string.IsNullOrWhiteSpace(profileContent))
		{
			newDescriptionParts.Add(profileContent);
		}
		string result = string.Join("\n", newDescriptionParts).Trim();
		if (!string.IsNullOrEmpty(result))
		{
			return result;
		}
		return "-- User has no description set --";
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		_pfpTextureWrap?.Dispose();
	}
}
