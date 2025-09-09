using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Enum;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Configurations;
using XIVSync.PlayerData.Data;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Utils;

namespace XIVSync.FileCache;

public sealed class TransientResourceManager : DisposableMediatorSubscriberBase
{
	public record TransientRecord(GameObjectHandler Owner, string GamePath, string FilePath, bool AlreadyTransient)
	{
		public bool AddTransient { get; set; }
	}

	private readonly object _cacheAdditionLock = new object();

	private readonly HashSet<string> _cachedHandledPaths = new HashSet<string>(StringComparer.Ordinal);

	private readonly TransientConfigService _configurationService;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly string[] _handledFileTypes = new string[10] { "tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk" };

	private readonly string[] _handledRecordingFileTypes = new string[3] { "tex", "mdl", "mtrl" };

	private readonly HashSet<GameObjectHandler> _playerRelatedPointers = new HashSet<GameObjectHandler>();

	private ConcurrentDictionary<nint, ObjectKind> _cachedFrameAddresses = new ConcurrentDictionary<nint, ObjectKind>();

	private ConcurrentDictionary<ObjectKind, HashSet<string>>? _semiTransientResources;

	private uint _lastClassJobId = uint.MaxValue;

	private readonly HashSet<TransientRecord> _recordedTransients = new HashSet<TransientRecord>();

	private CancellationTokenSource _sendTransientCts = new CancellationTokenSource();

	public bool IsTransientRecording { get; private set; }

	private TransientConfig.TransientPlayerConfig PlayerConfig
	{
		get
		{
			if (!_configurationService.Current.TransientConfigs.TryGetValue(PlayerPersistentDataKey, out TransientConfig.TransientPlayerConfig transientConfig))
			{
				transientConfig = (_configurationService.Current.TransientConfigs[PlayerPersistentDataKey] = new TransientConfig.TransientPlayerConfig());
			}
			return transientConfig;
		}
	}

	private string PlayerPersistentDataKey => _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult() + "_" + _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult();

	private ConcurrentDictionary<ObjectKind, HashSet<string>> SemiTransientResources
	{
		get
		{
			if (_semiTransientResources == null)
			{
				_semiTransientResources = new ConcurrentDictionary<ObjectKind, HashSet<string>>();
				PlayerConfig.JobSpecificCache.TryGetValue(_dalamudUtil.ClassJobId, out List<string> jobSpecificData);
				_semiTransientResources[ObjectKind.Player] = PlayerConfig.GlobalPersistentCache.Concat(jobSpecificData ?? new List<string>()).ToHashSet<string>(StringComparer.Ordinal);
				PlayerConfig.JobSpecificPetCache.TryGetValue(_dalamudUtil.ClassJobId, out List<string> petSpecificData);
				ConcurrentDictionary<ObjectKind, HashSet<string>> semiTransientResources = _semiTransientResources;
				HashSet<string> hashSet = new HashSet<string>();
				foreach (string item in petSpecificData ?? new List<string>())
				{
					hashSet.Add(item);
				}
				semiTransientResources[ObjectKind.Pet] = hashSet;
			}
			return _semiTransientResources;
		}
	}

	private ConcurrentDictionary<ObjectKind, HashSet<string>> TransientResources { get; } = new ConcurrentDictionary<ObjectKind, HashSet<string>>();

	public IReadOnlySet<TransientRecord> RecordedTransients => _recordedTransients;

	public ValueProgress<TimeSpan> RecordTimeRemaining { get; } = new ValueProgress<TimeSpan>();

	public TransientResourceManager(ILogger<TransientResourceManager> logger, TransientConfigService configurationService, DalamudUtilService dalamudUtil, MareMediator mediator)
		: base(logger, mediator)
	{
		_configurationService = configurationService;
		_dalamudUtil = dalamudUtil;
		base.Mediator.Subscribe<PenumbraResourceLoadMessage>(this, Manager_PenumbraResourceLoadEvent);
		base.Mediator.Subscribe<PenumbraModSettingChangedMessage>(this, delegate
		{
			Manager_PenumbraModSettingChanged();
		});
		base.Mediator.Subscribe<PriorityFrameworkUpdateMessage>(this, delegate
		{
			DalamudUtil_FrameworkUpdate();
		});
		base.Mediator.Subscribe(this, delegate(GameObjectHandlerCreatedMessage msg)
		{
			if (msg.OwnedObject)
			{
				_playerRelatedPointers.Add(msg.GameObjectHandler);
			}
		});
		base.Mediator.Subscribe(this, delegate(GameObjectHandlerDestroyedMessage msg)
		{
			if (msg.OwnedObject)
			{
				_playerRelatedPointers.Remove(msg.GameObjectHandler);
			}
		});
	}

	public void CleanUpSemiTransientResources(ObjectKind objectKind, List<FileReplacement>? fileReplacement = null)
	{
		if (!SemiTransientResources.TryGetValue(objectKind, out HashSet<string> value))
		{
			return;
		}
		if (fileReplacement == null)
		{
			value.Clear();
			return;
		}
		int removedPaths = 0;
		foreach (string replacement in fileReplacement.Where((FileReplacement p) => !p.HasFileReplacement).SelectMany((FileReplacement p) => p.GamePaths).ToList())
		{
			removedPaths += PlayerConfig.RemovePath(replacement, objectKind);
			value.Remove(replacement);
		}
		if (removedPaths > 0)
		{
			base.Logger.LogTrace("Removed {amount} of SemiTransient paths during CleanUp, Saving from {name}", removedPaths, "CleanUpSemiTransientResources");
			_configurationService.Save();
		}
	}

	public HashSet<string> GetSemiTransientResources(ObjectKind objectKind)
	{
		SemiTransientResources.TryGetValue(objectKind, out HashSet<string> result);
		return result ?? new HashSet<string>(StringComparer.Ordinal);
	}

	public void PersistTransientResources(ObjectKind objectKind)
	{
		if (!SemiTransientResources.TryGetValue(objectKind, out HashSet<string> semiTransientResources))
		{
			semiTransientResources = (SemiTransientResources[objectKind] = new HashSet<string>(StringComparer.Ordinal));
		}
		if (!TransientResources.TryGetValue(objectKind, out HashSet<string> resources))
		{
			return;
		}
		List<string> transientResources = resources.ToList();
		base.Logger.LogDebug("Persisting {count} transient resources", transientResources.Count);
		List<string> newlyAddedGamePaths = resources.Except<string>(semiTransientResources, StringComparer.Ordinal).ToList();
		foreach (string gamePath in transientResources)
		{
			semiTransientResources.Add(gamePath);
		}
		bool saveConfig = false;
		if (objectKind == ObjectKind.Player && newlyAddedGamePaths.Count != 0)
		{
			saveConfig = true;
			foreach (string item in newlyAddedGamePaths.Where((string f) => !string.IsNullOrEmpty(f)))
			{
				PlayerConfig.AddOrElevate(_dalamudUtil.ClassJobId, item);
			}
		}
		else if (objectKind == ObjectKind.Pet && newlyAddedGamePaths.Count != 0)
		{
			saveConfig = true;
			if (!PlayerConfig.JobSpecificPetCache.TryGetValue(_dalamudUtil.ClassJobId, out List<string> petPerma))
			{
				petPerma = (PlayerConfig.JobSpecificPetCache[_dalamudUtil.ClassJobId] = new List<string>());
			}
			foreach (string item2 in newlyAddedGamePaths.Where((string f) => !string.IsNullOrEmpty(f)))
			{
				petPerma.Add(item2);
			}
		}
		if (saveConfig)
		{
			base.Logger.LogTrace("Saving transient.json from {method}", "PersistTransientResources");
			_configurationService.Save();
		}
		TransientResources[objectKind].Clear();
	}

	public void RemoveTransientResource(ObjectKind objectKind, string path)
	{
		if (SemiTransientResources.TryGetValue(objectKind, out HashSet<string> resources))
		{
			resources.RemoveWhere((string f) => string.Equals(path, f, StringComparison.Ordinal));
			if (objectKind == ObjectKind.Player)
			{
				PlayerConfig.RemovePath(path, objectKind);
				base.Logger.LogTrace("Saving transient.json from {method}", "RemoveTransientResource");
				_configurationService.Save();
			}
		}
	}

	internal bool AddTransientResource(ObjectKind objectKind, string item)
	{
		if (SemiTransientResources.TryGetValue(objectKind, out HashSet<string> semiTransient) && semiTransient != null && semiTransient.Contains(item))
		{
			return false;
		}
		if (!TransientResources.TryGetValue(objectKind, out HashSet<string> transientResource))
		{
			transientResource = new HashSet<string>(StringComparer.Ordinal);
			TransientResources[objectKind] = transientResource;
		}
		return transientResource.Add(item.ToLowerInvariant());
	}

	internal void ClearTransientPaths(ObjectKind objectKind, List<string> list)
	{
		int recordingOnlyRemoved = list.RemoveAll((string entry) => _handledRecordingFileTypes.Any((string ext) => entry.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));
		if (recordingOnlyRemoved > 0)
		{
			base.Logger.LogTrace("Ignored {0} game paths when clearing transients", recordingOnlyRemoved);
		}
		if (TransientResources.TryGetValue(objectKind, out HashSet<string> set))
		{
			foreach (string file in set.Where((string p) => list.Contains<string>(p, StringComparer.OrdinalIgnoreCase)))
			{
				base.Logger.LogTrace("Removing From Transient: {file}", file);
			}
			int removed = set.RemoveWhere((string p) => list.Contains<string>(p, StringComparer.OrdinalIgnoreCase));
			base.Logger.LogDebug("Removed {removed} previously existing transient paths", removed);
		}
		bool reloadSemiTransient = false;
		if (objectKind == ObjectKind.Player && SemiTransientResources.TryGetValue(objectKind, out HashSet<string> semiset))
		{
			foreach (string file2 in semiset.Where((string p) => list.Contains<string>(p, StringComparer.OrdinalIgnoreCase)))
			{
				base.Logger.LogTrace("Removing From SemiTransient: {file}", file2);
				PlayerConfig.RemovePath(file2, objectKind);
			}
			int removed2 = semiset.RemoveWhere((string p) => list.Contains<string>(p, StringComparer.OrdinalIgnoreCase));
			base.Logger.LogDebug("Removed {removed} previously existing semi transient paths", removed2);
			if (removed2 > 0)
			{
				reloadSemiTransient = true;
				base.Logger.LogTrace("Saving transient.json from {method}", "ClearTransientPaths");
				_configurationService.Save();
			}
		}
		if (reloadSemiTransient)
		{
			_semiTransientResources = null;
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		TransientResources.Clear();
		SemiTransientResources.Clear();
	}

	private void DalamudUtil_FrameworkUpdate()
	{
		_cachedFrameAddresses = new ConcurrentDictionary<nint, ObjectKind>(_playerRelatedPointers.Where((GameObjectHandler k) => k.Address != IntPtr.Zero).ToDictionary((GameObjectHandler c) => c.Address, (GameObjectHandler c) => c.ObjectKind));
		lock (_cacheAdditionLock)
		{
			_cachedHandledPaths.Clear();
		}
		HashSet<string> value2;
		if (_lastClassJobId != _dalamudUtil.ClassJobId)
		{
			_lastClassJobId = _dalamudUtil.ClassJobId;
			if (SemiTransientResources.TryGetValue(ObjectKind.Pet, out HashSet<string> value))
			{
				value?.Clear();
			}
			PlayerConfig.JobSpecificCache.TryGetValue(_dalamudUtil.ClassJobId, out List<string> jobSpecificData);
			SemiTransientResources[ObjectKind.Player] = PlayerConfig.GlobalPersistentCache.Concat(jobSpecificData ?? new List<string>()).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
			PlayerConfig.JobSpecificPetCache.TryGetValue(_dalamudUtil.ClassJobId, out List<string> petSpecificData);
			ConcurrentDictionary<ObjectKind, HashSet<string>> semiTransientResources = SemiTransientResources;
			value2 = new HashSet<string>();
			foreach (string item in petSpecificData ?? new List<string>())
			{
				value2.Add(item);
			}
			semiTransientResources[ObjectKind.Pet] = value2;
		}
		foreach (object kind in Enum.GetValues(typeof(ObjectKind)))
		{
			if (!_cachedFrameAddresses.Any((KeyValuePair<nint, ObjectKind> k) => k.Value == (ObjectKind)kind) && TransientResources.Remove<ObjectKind, HashSet<string>>((ObjectKind)kind, out value2))
			{
				base.Logger.LogDebug("Object not present anymore: {kind}", kind.ToString());
			}
		}
	}

	private void Manager_PenumbraModSettingChanged()
	{
		Task.Run(delegate
		{
			base.Logger.LogDebug("Penumbra Mod Settings changed, verifying SemiTransientResources");
			foreach (GameObjectHandler current in _playerRelatedPointers)
			{
				base.Mediator.Publish(new TransientResourceChangedMessage(current.Address));
			}
		});
	}

	public void RebuildSemiTransientResources()
	{
		_semiTransientResources = null;
	}

	private void Manager_PenumbraResourceLoadEvent(PenumbraResourceLoadMessage msg)
	{
		string gamePath = msg.GamePath.ToLowerInvariant();
		nint gameObjectAddress = msg.GameObject;
		string filePath = msg.FilePath;
		if (_cachedHandledPaths.Contains(gamePath))
		{
			return;
		}
		lock (_cacheAdditionLock)
		{
			_cachedHandledPaths.Add(gamePath);
		}
		if (filePath.StartsWith("|", StringComparison.OrdinalIgnoreCase))
		{
			filePath = filePath.Split("|")[2];
		}
		filePath = filePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);
		string replacedGamePath = gamePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);
		if (string.Equals(filePath, replacedGamePath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		IEnumerable<string> source;
		if (!IsTransientRecording)
		{
			IEnumerable<string> handledFileTypes = _handledFileTypes;
			source = handledFileTypes;
		}
		else
		{
			source = _handledRecordingFileTypes.Concat(_handledFileTypes);
		}
		if (!source.Any((string type) => gamePath.EndsWith(type, StringComparison.OrdinalIgnoreCase)))
		{
			lock (_cacheAdditionLock)
			{
				_cachedHandledPaths.Add(gamePath);
				return;
			}
		}
		if (!_cachedFrameAddresses.TryGetValue(gameObjectAddress, out var objectKind))
		{
			lock (_cacheAdditionLock)
			{
				_cachedHandledPaths.Add(gamePath);
				return;
			}
		}
		if (!TransientResources.TryGetValue(objectKind, out HashSet<string> transientResources))
		{
			transientResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			TransientResources[objectKind] = transientResources;
		}
		GameObjectHandler owner = _playerRelatedPointers.FirstOrDefault((GameObjectHandler f) => f.Address == gameObjectAddress);
		bool alreadyTransient = false;
		bool transientContains = transientResources.Contains(replacedGamePath);
		bool semiTransientContains = SemiTransientResources.SelectMany<KeyValuePair<ObjectKind, HashSet<string>>, string>((KeyValuePair<ObjectKind, HashSet<string>> k) => k.Value).Any((string f) => string.Equals(f, gamePath, StringComparison.OrdinalIgnoreCase));
		if (transientContains || semiTransientContains)
		{
			if (!IsTransientRecording)
			{
				base.Logger.LogTrace("Not adding {replacedPath} => {filePath}, Reason: Transient: {contains}, SemiTransient: {contains2}", replacedGamePath, filePath, transientContains, semiTransientContains);
			}
			alreadyTransient = true;
		}
		else if (!IsTransientRecording && transientResources.Add(replacedGamePath))
		{
			base.Logger.LogDebug("Adding {replacedGamePath} for {gameObject} ({filePath})", replacedGamePath, owner?.ToString() ?? ((IntPtr)gameObjectAddress).ToString("X"), filePath);
			SendTransients(gameObjectAddress, objectKind);
		}
		if (owner != null && IsTransientRecording)
		{
			_recordedTransients.Add(new TransientRecord(owner, replacedGamePath, filePath, alreadyTransient)
			{
				AddTransient = !alreadyTransient
			});
		}
	}

	private void SendTransients(nint gameObject, ObjectKind objectKind)
	{
		Task.Run(async delegate
		{
			_sendTransientCts?.Cancel();
			_sendTransientCts?.Dispose();
			_sendTransientCts = new CancellationTokenSource();
			CancellationToken token = _sendTransientCts.Token;
			await Task.Delay(TimeSpan.FromSeconds(5L), token).ConfigureAwait(continueOnCapturedContext: false);
			foreach (KeyValuePair<ObjectKind, HashSet<string>> transientResource in TransientResources)
			{
				_ = transientResource;
				if (TransientResources.TryGetValue(objectKind, out HashSet<string> values) && values.Any())
				{
					base.Logger.LogTrace("Sending Transients for {kind}", objectKind);
					base.Mediator.Publish(new TransientResourceChangedMessage(gameObject));
				}
			}
		});
	}

	public void StartRecording(CancellationToken token)
	{
		if (IsTransientRecording)
		{
			return;
		}
		_recordedTransients.Clear();
		IsTransientRecording = true;
		RecordTimeRemaining.Value = TimeSpan.FromSeconds(150L);
		Task.Run(async delegate
		{
			try
			{
				while (RecordTimeRemaining.Value > TimeSpan.Zero && !token.IsCancellationRequested)
				{
					await Task.Delay(TimeSpan.FromSeconds(1L), token).ConfigureAwait(continueOnCapturedContext: false);
					RecordTimeRemaining.Value = RecordTimeRemaining.Value.Subtract(TimeSpan.FromSeconds(1L));
				}
			}
			finally
			{
				IsTransientRecording = false;
			}
		});
	}

	public async Task WaitForRecording(CancellationToken token)
	{
		while (IsTransientRecording)
		{
			await Task.Delay(TimeSpan.FromSeconds(1L), token).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	internal void SaveRecording()
	{
		HashSet<nint> addedTransients = new HashSet<nint>();
		foreach (TransientRecord item in _recordedTransients)
		{
			if (item.AddTransient && !item.AlreadyTransient)
			{
				if (!TransientResources.TryGetValue(item.Owner.ObjectKind, out HashSet<string> transient))
				{
					transient = (TransientResources[item.Owner.ObjectKind] = new HashSet<string>());
				}
				base.Logger.LogTrace("Adding recorded: {gamePath} => {filePath}", item.GamePath, item.FilePath);
				transient.Add(item.GamePath);
				addedTransients.Add(item.Owner.Address);
			}
		}
		_recordedTransients.Clear();
		foreach (nint item2 in addedTransients)
		{
			base.Mediator.Publish(new TransientResourceChangedMessage(item2));
		}
	}
}
