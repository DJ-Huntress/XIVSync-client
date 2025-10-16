using System;
using Microsoft.Extensions.Logging;
using XIVSync.Services.Mediator;

namespace XIVSync.Interop.Ipc;

public sealed class IpcManager : DisposableMediatorSubscriberBase
{
	public bool Initialized
	{
		get
		{
			if (Penumbra.APIAvailable)
			{
				return Glamourer.APIAvailable;
			}
			return false;
		}
	}

	public IpcCallerCustomize CustomizePlus { get; init; }

	public IpcCallerHonorific Honorific { get; init; }

	public IpcCallerHeels Heels { get; init; }

	public IpcCallerGlamourer Glamourer { get; }

	public IpcCallerPenumbra Penumbra { get; }

	public IpcCallerMoodles Moodles { get; }

	public IpcCallerPetNames PetNames { get; }

	public IpcCallerBrio Brio { get; }

	public IpcManager(ILogger<IpcManager> logger, MareMediator mediator, IpcCallerPenumbra penumbraIpc, IpcCallerGlamourer glamourerIpc, IpcCallerCustomize customizeIpc, IpcCallerHeels heelsIpc, IpcCallerHonorific honorificIpc, IpcCallerMoodles moodlesIpc, IpcCallerPetNames ipcCallerPetNames, IpcCallerBrio ipcCallerBrio)
		: base(logger, mediator)
	{
		CustomizePlus = customizeIpc;
		Heels = heelsIpc;
		Glamourer = glamourerIpc;
		Penumbra = penumbraIpc;
		Honorific = honorificIpc;
		Moodles = moodlesIpc;
		PetNames = ipcCallerPetNames;
		Brio = ipcCallerBrio;
		if (Initialized)
		{
			base.Mediator.Publish(new PenumbraInitializedMessage());
		}
		base.Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, delegate
		{
			PeriodicApiStateCheck();
		});
		try
		{
			PeriodicApiStateCheck();
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to check for some IPC, plugin not installed?");
		}
	}

	private void PeriodicApiStateCheck()
	{
		Penumbra.CheckAPI();
		Penumbra.CheckModDirectory();
		Glamourer.CheckAPI();
		Heels.CheckAPI();
		CustomizePlus.CheckAPI();
		Honorific.CheckAPI();
		Moodles.CheckAPI();
		PetNames.CheckAPI();
		Brio.CheckAPI();
	}
}
