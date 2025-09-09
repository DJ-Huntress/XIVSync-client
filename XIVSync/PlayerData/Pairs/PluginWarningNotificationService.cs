using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using XIVSync.API.Data;
using XIVSync.API.Data.Comparer;
using XIVSync.Interop.Ipc;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.Services.Mediator;

namespace XIVSync.PlayerData.Pairs;

public class PluginWarningNotificationService
{
	private readonly ConcurrentDictionary<UserData, OptionalPluginWarning> _cachedOptionalPluginWarnings = new ConcurrentDictionary<UserData, OptionalPluginWarning>(UserDataComparer.Instance);

	private readonly IpcManager _ipcManager;

	private readonly MareConfigService _mareConfigService;

	private readonly MareMediator _mediator;

	public PluginWarningNotificationService(MareConfigService mareConfigService, IpcManager ipcManager, MareMediator mediator)
	{
		_mareConfigService = mareConfigService;
		_ipcManager = ipcManager;
		_mediator = mediator;
	}

	public void NotifyForMissingPlugins(UserData user, string playerName, HashSet<PlayerChanges> changes)
	{
		if (!_cachedOptionalPluginWarnings.TryGetValue(user, out OptionalPluginWarning warning))
		{
			ConcurrentDictionary<UserData, OptionalPluginWarning> cachedOptionalPluginWarnings = _cachedOptionalPluginWarnings;
			OptionalPluginWarning obj = new OptionalPluginWarning
			{
				ShownCustomizePlusWarning = _mareConfigService.Current.DisableOptionalPluginWarnings,
				ShownHeelsWarning = _mareConfigService.Current.DisableOptionalPluginWarnings,
				ShownHonorificWarning = _mareConfigService.Current.DisableOptionalPluginWarnings,
				ShownMoodlesWarning = _mareConfigService.Current.DisableOptionalPluginWarnings,
				ShowPetNicknamesWarning = _mareConfigService.Current.DisableOptionalPluginWarnings
			};
			warning = obj;
			cachedOptionalPluginWarnings[user] = obj;
		}
		List<string> missingPluginsForData = new List<string>();
		if (changes.Contains(PlayerChanges.Heels) && !warning.ShownHeelsWarning && !_ipcManager.Heels.APIAvailable)
		{
			missingPluginsForData.Add("SimpleHeels");
			warning.ShownHeelsWarning = true;
		}
		if (changes.Contains(PlayerChanges.Customize) && !warning.ShownCustomizePlusWarning && !_ipcManager.CustomizePlus.APIAvailable)
		{
			missingPluginsForData.Add("Customize+");
			warning.ShownCustomizePlusWarning = true;
		}
		if (changes.Contains(PlayerChanges.Honorific) && !warning.ShownHonorificWarning && !_ipcManager.Honorific.APIAvailable)
		{
			missingPluginsForData.Add("Honorific");
			warning.ShownHonorificWarning = true;
		}
		if (changes.Contains(PlayerChanges.Moodles) && !warning.ShownMoodlesWarning && !_ipcManager.Moodles.APIAvailable)
		{
			missingPluginsForData.Add("Moodles");
			warning.ShownMoodlesWarning = true;
		}
		if (changes.Contains(PlayerChanges.PetNames) && !warning.ShowPetNicknamesWarning && !_ipcManager.PetNames.APIAvailable)
		{
			missingPluginsForData.Add("PetNicknames");
			warning.ShowPetNicknamesWarning = true;
		}
		if (missingPluginsForData.Any())
		{
			_mediator.Publish(new NotificationMessage("Missing plugins for " + playerName, $"Received data for {playerName} that contained information for plugins you have not installed. Install {string.Join(", ", missingPluginsForData)} to experience their character fully.", NotificationType.Warning, TimeSpan.FromSeconds(10L)));
		}
	}
}
