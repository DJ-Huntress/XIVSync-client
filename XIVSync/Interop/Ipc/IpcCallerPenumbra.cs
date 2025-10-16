using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Microsoft.Extensions.Logging;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;
using XIVSync.MareConfiguration.Models;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services;
using XIVSync.Services.Mediator;

namespace XIVSync.Interop.Ipc;

public sealed class IpcCallerPenumbra : DisposableMediatorSubscriberBase, IIpcCaller, IDisposable
{
	private readonly IDalamudPluginInterface _pi;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly MareMediator _mareMediator;

	private readonly RedrawManager _redrawManager;

	private bool _shownPenumbraUnavailable;

	private string? _penumbraModDirectory;

	private readonly ConcurrentDictionary<nint, bool> _penumbraRedrawRequests = new ConcurrentDictionary<nint, bool>();

	private readonly EventSubscriber _penumbraDispose;

	private readonly EventSubscriber<nint, string, string> _penumbraGameObjectResourcePathResolved;

	private readonly EventSubscriber _penumbraInit;

	private readonly EventSubscriber<ModSettingChange, Guid, string, bool> _penumbraModSettingChanged;

	private readonly EventSubscriber<nint, int> _penumbraObjectIsRedrawn;

	private readonly AddTemporaryMod _penumbraAddTemporaryMod;

	private readonly AssignTemporaryCollection _penumbraAssignTemporaryCollection;

	private readonly ConvertTextureFile _penumbraConvertTextureFile;

	private readonly CreateTemporaryCollection _penumbraCreateNamedTemporaryCollection;

	private readonly GetEnabledState _penumbraEnabled;

	private readonly GetPlayerMetaManipulations _penumbraGetMetaManipulations;

	private readonly RedrawObject _penumbraRedraw;

	private readonly DeleteTemporaryCollection _penumbraRemoveTemporaryCollection;

	private readonly RemoveTemporaryMod _penumbraRemoveTemporaryMod;

	private readonly GetModDirectory _penumbraResolveModDir;

	private readonly ResolvePlayerPathsAsync _penumbraResolvePaths;

	private readonly GetGameObjectResourcePaths _penumbraResourcePaths;

	public string? ModDirectory
	{
		get
		{
			return _penumbraModDirectory;
		}
		private set
		{
			if (!string.Equals(_penumbraModDirectory, value, StringComparison.Ordinal))
			{
				_penumbraModDirectory = value;
				_mareMediator.Publish(new PenumbraDirectoryChangedMessage(_penumbraModDirectory));
			}
		}
	}

	public bool APIAvailable { get; private set; }

	public IpcCallerPenumbra(ILogger<IpcCallerPenumbra> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil, MareMediator mareMediator, RedrawManager redrawManager)
		: base(logger, mareMediator)
	{
		_pi = pi;
		_dalamudUtil = dalamudUtil;
		_mareMediator = mareMediator;
		_redrawManager = redrawManager;
		_penumbraInit = Initialized.Subscriber(pi, PenumbraInit);
		_penumbraDispose = Disposed.Subscriber(pi, PenumbraDispose);
		_penumbraResolveModDir = new GetModDirectory(pi);
		_penumbraRedraw = new RedrawObject(pi);
		_penumbraObjectIsRedrawn = GameObjectRedrawn.Subscriber(pi, RedrawEvent);
		_penumbraGetMetaManipulations = new GetPlayerMetaManipulations(pi);
		_penumbraRemoveTemporaryMod = new RemoveTemporaryMod(pi);
		_penumbraAddTemporaryMod = new AddTemporaryMod(pi);
		_penumbraCreateNamedTemporaryCollection = new CreateTemporaryCollection(pi);
		_penumbraRemoveTemporaryCollection = new DeleteTemporaryCollection(pi);
		_penumbraAssignTemporaryCollection = new AssignTemporaryCollection(pi);
		_penumbraResolvePaths = new ResolvePlayerPathsAsync(pi);
		_penumbraEnabled = new GetEnabledState(pi);
		_penumbraModSettingChanged = ModSettingChanged.Subscriber(pi, delegate(ModSettingChange change, Guid arg1, string arg, bool b)
		{
			if (change == ModSettingChange.EnableState)
			{
				_mareMediator.Publish(new PenumbraModSettingChangedMessage());
			}
		});
		_penumbraConvertTextureFile = new ConvertTextureFile(pi);
		_penumbraResourcePaths = new GetGameObjectResourcePaths(pi);
		_penumbraGameObjectResourcePathResolved = GameObjectResourcePathResolved.Subscriber(pi, ResourceLoaded);
		CheckAPI();
		CheckModDirectory();
		base.Mediator.Subscribe(this, delegate(PenumbraRedrawCharacterMessage msg)
		{
			_penumbraRedraw.Invoke(msg.Character.ObjectIndex, RedrawType.AfterGPose);
		});
		base.Mediator.Subscribe<DalamudLoginMessage>(this, delegate
		{
			_shownPenumbraUnavailable = false;
		});
	}

	public void CheckAPI()
	{
		bool penumbraAvailable = false;
		try
		{
			penumbraAvailable = (_pi.InstalledPlugins.FirstOrDefault((IExposedPlugin p) => string.Equals(p.InternalName, "Penumbra", StringComparison.OrdinalIgnoreCase))?.Version ?? new Version(0, 0, 0, 0)) >= new Version(1, 2, 0, 22);
			try
			{
				penumbraAvailable &= _penumbraEnabled.Invoke();
			}
			catch
			{
				penumbraAvailable = false;
			}
			_shownPenumbraUnavailable = _shownPenumbraUnavailable && !penumbraAvailable;
			APIAvailable = penumbraAvailable;
		}
		catch
		{
			APIAvailable = penumbraAvailable;
		}
		finally
		{
			if (!penumbraAvailable && !_shownPenumbraUnavailable)
			{
				_shownPenumbraUnavailable = true;
				_mareMediator.Publish(new NotificationMessage("Penumbra inactive", "Your Penumbra installation is not active or out of date. Update Penumbra and/or the Enable Mods setting in Penumbra to continue to use Mare. If you just updated Penumbra, ignore this message.", NotificationType.Error));
			}
		}
	}

	public void CheckModDirectory()
	{
		if (!APIAvailable)
		{
			ModDirectory = string.Empty;
		}
		else
		{
			ModDirectory = _penumbraResolveModDir.Invoke().ToLowerInvariant();
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		_redrawManager.Cancel();
		_penumbraModSettingChanged.Dispose();
		_penumbraGameObjectResourcePathResolved.Dispose();
		_penumbraDispose.Dispose();
		_penumbraInit.Dispose();
		_penumbraObjectIsRedrawn.Dispose();
	}

	public async Task AssignTemporaryCollectionAsync(ILogger logger, Guid collName, int idx)
	{
		if (APIAvailable)
		{
			await _dalamudUtil.RunOnFrameworkThread(delegate
			{
				PenumbraApiEc penumbraApiEc = _penumbraAssignTemporaryCollection.Invoke(collName, idx);
				logger.LogTrace("Assigning Temp Collection {collName} to index {idx}, Success: {ret}", collName, idx, penumbraApiEc);
				return collName;
			}, "AssignTemporaryCollectionAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerPenumbra.cs", 164).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public async Task ConvertTextureFiles(ILogger logger, Dictionary<string, string[]> textures, IProgress<(string, int)> progress, CancellationToken token)
	{
		if (!APIAvailable)
		{
			return;
		}
		_mareMediator.Publish(new HaltScanMessage("ConvertTextureFiles"));
		int currentTexture = 0;
		foreach (KeyValuePair<string, string[]> texture in textures)
		{
			if (token.IsCancellationRequested)
			{
				break;
			}
			string key = texture.Key;
			int num = currentTexture + 1;
			currentTexture = num;
			progress.Report((key, num));
			logger.LogInformation("Converting Texture {path} to {type}", texture.Key, TextureType.Bc7Tex);
			Task convertTask = _penumbraConvertTextureFile.Invoke(texture.Key, texture.Key, TextureType.Bc7Tex);
			await convertTask.ConfigureAwait(continueOnCapturedContext: false);
			if (!convertTask.IsCompletedSuccessfully || !texture.Value.Any())
			{
				continue;
			}
			string[] value = texture.Value;
			foreach (string duplicatedTexture in value)
			{
				logger.LogInformation("Migrating duplicate {dup}", duplicatedTexture);
				try
				{
					File.Copy(texture.Key, duplicatedTexture, overwrite: true);
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Failed to copy duplicate {dup}", duplicatedTexture);
				}
			}
		}
		_mareMediator.Publish(new ResumeScanMessage("ConvertTextureFiles"));
		await _dalamudUtil.RunOnFrameworkThread((Func<Task>)async delegate
		{
			DalamudUtilService dalamudUtil = _dalamudUtil;
			IGameObject gameObject = await dalamudUtil.CreateGameObjectAsync(await _dalamudUtil.GetPlayerPointerAsync().ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
			_penumbraRedraw.Invoke(gameObject.ObjectIndex);
		}, "ConvertTextureFiles", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerPenumbra.cs", 205).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Guid> CreateTemporaryCollectionAsync(ILogger logger, string uid)
	{
		if (!APIAvailable)
		{
			return Guid.Empty;
		}
		return await _dalamudUtil.RunOnFrameworkThread(delegate
		{
			string text = "Mare_" + uid;
			_penumbraCreateNamedTemporaryCollection.Invoke(uid, text, out var collection);
			logger.LogTrace("Creating Temp Collection {collName}, GUID: {collId}", text, collection);
			return collection;
		}, "CreateTemporaryCollectionAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerPenumbra.cs", 216).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Dictionary<string, HashSet<string>>?> GetCharacterData(ILogger logger, GameObjectHandler handler)
	{
		if (!APIAvailable)
		{
			return null;
		}
		return await _dalamudUtil.RunOnFrameworkThread(delegate
		{
			logger.LogTrace("Calling On IPC: Penumbra.GetGameObjectResourcePaths");
			ushort? num = handler.GetGameObject()?.ObjectIndex;
			return (!num.HasValue) ? null : _penumbraResourcePaths.Invoke(num.Value)[0];
		}, "GetCharacterData", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerPenumbra.cs", 231).ConfigureAwait(continueOnCapturedContext: false);
	}

	public string GetMetaManipulations()
	{
		if (!APIAvailable)
		{
			return string.Empty;
		}
		return _penumbraGetMetaManipulations.Invoke();
	}

	public async Task RedrawAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, CancellationToken token)
	{
		if (!APIAvailable || _dalamudUtil.IsZoning)
		{
			return;
		}
		try
		{
			await _redrawManager.RedrawSemaphore.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			await _redrawManager.PenumbraRedrawInternalAsync(logger, handler, applicationId, delegate(ICharacter chara)
			{
				logger.LogDebug("[{appid}] Calling on IPC: PenumbraRedraw", applicationId);
				_penumbraRedraw.Invoke(chara.ObjectIndex);
			}, token).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_redrawManager.RedrawSemaphore.Release();
		}
	}

	public async Task RemoveTemporaryCollectionAsync(ILogger logger, Guid applicationId, Guid collId)
	{
		if (APIAvailable)
		{
			await _dalamudUtil.RunOnFrameworkThread(delegate
			{
				logger.LogTrace("[{applicationId}] Removing temp collection for {collId}", applicationId, collId);
				PenumbraApiEc penumbraApiEc = _penumbraRemoveTemporaryCollection.Invoke(collId);
				logger.LogTrace("[{applicationId}] RemoveTemporaryCollection: {ret2}", applicationId, penumbraApiEc);
			}, "RemoveTemporaryCollectionAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerPenumbra.cs", 268).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public async Task<(string[] forward, string[][] reverse)> ResolvePathsAsync(string[] forward, string[] reverse)
	{
		return await _penumbraResolvePaths.Invoke(forward, reverse).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task SetManipulationDataAsync(ILogger logger, Guid applicationId, Guid collId, string manipulationData)
	{
		if (APIAvailable)
		{
			await _dalamudUtil.RunOnFrameworkThread(delegate
			{
				logger.LogTrace("[{applicationId}] Manip: {data}", applicationId, manipulationData);
				PenumbraApiEc penumbraApiEc = _penumbraAddTemporaryMod.Invoke("MareChara_Meta", collId, new Dictionary<string, string>(), manipulationData, 0);
				logger.LogTrace("[{applicationId}] Setting temp meta mod for {collId}, Success: {ret}", applicationId, collId, penumbraApiEc);
			}, "SetManipulationDataAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerPenumbra.cs", 285).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public async Task SetTemporaryModsAsync(ILogger logger, Guid applicationId, Guid collId, Dictionary<string, string> modPaths)
	{
		if (!APIAvailable)
		{
			return;
		}
		await _dalamudUtil.RunOnFrameworkThread(delegate
		{
			foreach (KeyValuePair<string, string> current in modPaths)
			{
				logger.LogTrace("[{applicationId}] Change: {from} => {to}", applicationId, current.Key, current.Value);
			}
			PenumbraApiEc penumbraApiEc = _penumbraRemoveTemporaryMod.Invoke("MareChara_Files", collId, 0);
			logger.LogTrace("[{applicationId}] Removing temp files mod for {collId}, Success: {ret}", applicationId, collId, penumbraApiEc);
			PenumbraApiEc penumbraApiEc2 = _penumbraAddTemporaryMod.Invoke("MareChara_Files", collId, modPaths, string.Empty, 0);
			logger.LogTrace("[{applicationId}] Setting temp files mod for {collId}, Success: {ret}", applicationId, collId, penumbraApiEc2);
		}, "SetTemporaryModsAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerPenumbra.cs", 297).ConfigureAwait(continueOnCapturedContext: false);
	}

	private void RedrawEvent(nint objectAddress, int objectTableIndex)
	{
		bool wasRequested = false;
		if (_penumbraRedrawRequests.TryGetValue(objectAddress, out var redrawRequest) && redrawRequest)
		{
			_penumbraRedrawRequests[objectAddress] = false;
		}
		else
		{
			_mareMediator.Publish(new PenumbraRedrawMessage(objectAddress, objectTableIndex, wasRequested));
		}
	}

	private void ResourceLoaded(nint ptr, string arg1, string arg2)
	{
		if (ptr != IntPtr.Zero && string.Compare(arg1, arg2, ignoreCase: true, CultureInfo.InvariantCulture) != 0)
		{
			_mareMediator.Publish(new PenumbraResourceLoadMessage(ptr, arg1, arg2));
		}
	}

	private void PenumbraDispose()
	{
		_redrawManager.Cancel();
		_mareMediator.Publish(new PenumbraDisposedMessage());
	}

	private void PenumbraInit()
	{
		APIAvailable = true;
		ModDirectory = _penumbraResolveModDir.Invoke();
		_mareMediator.Publish(new PenumbraInitializedMessage());
		_penumbraRedraw.Invoke(0);
	}
}
