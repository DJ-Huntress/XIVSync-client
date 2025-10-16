using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Enum;
using XIVSync.MareConfiguration;
using XIVSync.PlayerData.Data;
using XIVSync.PlayerData.Factories;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services;
using XIVSync.Services.Mediator;

namespace XIVSync.PlayerData.Services;

public sealed class CacheCreationService : DisposableMediatorSubscriberBase
{
	private readonly SemaphoreSlim _cacheCreateLock = new SemaphoreSlim(1);

	private readonly HashSet<ObjectKind> _cachesToCreate = new HashSet<ObjectKind>();

	private readonly PlayerDataFactory _characterDataFactory;

	private readonly MareConfigService _mareConfigService;

	private readonly HashSet<ObjectKind> _currentlyCreating = new HashSet<ObjectKind>();

	private readonly HashSet<ObjectKind> _debouncedObjectCache = new HashSet<ObjectKind>();

	private readonly CharacterData _playerData = new CharacterData();

	private readonly Dictionary<ObjectKind, GameObjectHandler> _playerRelatedObjects = new Dictionary<ObjectKind, GameObjectHandler>();

	private readonly CancellationTokenSource _runtimeCts = new CancellationTokenSource();

	private CancellationTokenSource _creationCts = new CancellationTokenSource();

	private CancellationTokenSource _debounceCts = new CancellationTokenSource();

	private bool _haltCharaDataCreation;

	private bool _isZoning;

	public CacheCreationService(ILogger<CacheCreationService> logger, MareMediator mediator, GameObjectHandlerFactory gameObjectHandlerFactory, PlayerDataFactory characterDataFactory, DalamudUtilService dalamudUtil, MareConfigService mareConfigService) : base(logger, mediator)
	{
		CacheCreationService cacheCreationService = this;
		_characterDataFactory = characterDataFactory;
		_mareConfigService = mareConfigService;
		base.Mediator.Subscribe<ZoneSwitchStartMessage>(this, delegate
		{
			cacheCreationService._isZoning = true;
		});
		base.Mediator.Subscribe<ZoneSwitchEndMessage>(this, delegate
		{
			cacheCreationService._isZoning = false;
		});
		base.Mediator.Subscribe(this, delegate(HaltCharaDataCreation msg)
		{
			cacheCreationService._haltCharaDataCreation = !msg.Resume;
		});
		base.Mediator.Subscribe(this, delegate(CreateCacheForObjectMessage msg)
		{
			cacheCreationService.Logger.LogDebug("Received CreateCacheForObject for {handler}, updating", msg.ObjectToCreateFor);
			cacheCreationService.AddCacheToCreate(msg.ObjectToCreateFor.ObjectKind);
		});
		_playerRelatedObjects[ObjectKind.Player] = gameObjectHandlerFactory.Create(ObjectKind.Player, dalamudUtil.GetPlayerPtr, isWatched: true).GetAwaiter().GetResult();
		_playerRelatedObjects[ObjectKind.MinionOrMount] = gameObjectHandlerFactory.Create(ObjectKind.MinionOrMount, () => dalamudUtil.GetMinionOrMountPtr(), isWatched: true).GetAwaiter().GetResult();
		_playerRelatedObjects[ObjectKind.Pet] = gameObjectHandlerFactory.Create(ObjectKind.Pet, () => dalamudUtil.GetPetPtr(), isWatched: true).GetAwaiter().GetResult();
		_playerRelatedObjects[ObjectKind.Companion] = gameObjectHandlerFactory.Create(ObjectKind.Companion, () => dalamudUtil.GetCompanionPtr(), isWatched: true).GetAwaiter().GetResult();
		base.Mediator.Subscribe(this, delegate(ClassJobChangedMessage msg)
		{
			if (msg.GameObjectHandler == cacheCreationService._playerRelatedObjects[ObjectKind.Player])
			{
				cacheCreationService.AddCacheToCreate();
				cacheCreationService.AddCacheToCreate(ObjectKind.Pet);
			}
		});
		base.Mediator.Subscribe(this, delegate(ClearCacheForObjectMessage msg)
		{
			if (msg.ObjectToCreateFor.ObjectKind == ObjectKind.Pet)
			{
				cacheCreationService.Logger.LogTrace("Received clear cache for {obj}, ignoring", msg.ObjectToCreateFor);
			}
			else
			{
				cacheCreationService.Logger.LogDebug("Clearing cache for {obj}", msg.ObjectToCreateFor);
				cacheCreationService.AddCacheToCreate(msg.ObjectToCreateFor.ObjectKind);
			}
		});
		base.Mediator.Subscribe(this, delegate(CustomizePlusMessage msg)
		{
			if (cacheCreationService._isZoning)
			{
				return;
			}
			foreach (ObjectKind current in from item in cacheCreationService._playerRelatedObjects
				where !msg.Address.HasValue || item.Value.Address == msg.Address
				select item into k
				select k.Key)
			{
				cacheCreationService.Logger.LogDebug("Received CustomizePlus change, updating {obj}", current);
				cacheCreationService.AddCacheToCreate(current);
			}
		});
		base.Mediator.Subscribe<HeelsOffsetMessage>(this, delegate
		{
			if (!cacheCreationService._isZoning)
			{
				cacheCreationService.Logger.LogDebug("Received Heels Offset change, updating player");
				cacheCreationService.AddCacheToCreate();
			}
		});
		base.Mediator.Subscribe(this, delegate(GlamourerChangedMessage msg)
		{
			if (!cacheCreationService._isZoning)
			{
				KeyValuePair<ObjectKind, GameObjectHandler> keyValuePair2 = cacheCreationService._playerRelatedObjects.FirstOrDefault<KeyValuePair<ObjectKind, GameObjectHandler>>((KeyValuePair<ObjectKind, GameObjectHandler> f) => f.Value.Address == msg.Address);
				if (!default(KeyValuePair<ObjectKind, GameObjectHandler>).Equals(keyValuePair2))
				{
					cacheCreationService.Logger.LogDebug("Received GlamourerChangedMessage for {kind}", keyValuePair2);
					cacheCreationService.AddCacheToCreate(keyValuePair2.Key);
				}
			}
		});
		base.Mediator.Subscribe(this, delegate(HonorificMessage msg)
		{
			if (!cacheCreationService._isZoning && !string.Equals(msg.NewHonorificTitle, cacheCreationService._playerData.HonorificData, StringComparison.Ordinal))
			{
				cacheCreationService.Logger.LogDebug("Received Honorific change, updating player");
				cacheCreationService.AddCacheToCreate();
			}
		});
		base.Mediator.Subscribe(this, delegate(MoodlesMessage msg)
		{
			if (!cacheCreationService._isZoning)
			{
				KeyValuePair<ObjectKind, GameObjectHandler> keyValuePair = cacheCreationService._playerRelatedObjects.FirstOrDefault<KeyValuePair<ObjectKind, GameObjectHandler>>((KeyValuePair<ObjectKind, GameObjectHandler> f) => f.Value.Address == msg.Address);
				if (!default(KeyValuePair<ObjectKind, GameObjectHandler>).Equals(keyValuePair) && keyValuePair.Key == ObjectKind.Player)
				{
					cacheCreationService.Logger.LogDebug("Received Moodles change, updating player");
					cacheCreationService.AddCacheToCreate();
				}
			}
		});
		base.Mediator.Subscribe(this, delegate(PetNamesMessage msg)
		{
			if (!cacheCreationService._isZoning && !string.Equals(msg.PetNicknamesData, cacheCreationService._playerData.PetNamesData, StringComparison.Ordinal))
			{
				cacheCreationService.Logger.LogDebug("Received Pet Nicknames change, updating player");
				cacheCreationService.AddCacheToCreate();
			}
		});
		base.Mediator.Subscribe<PenumbraModSettingChangedMessage>(this, delegate
		{
			cacheCreationService.Logger.LogDebug("Received Penumbra Mod settings change, updating everything");
			cacheCreationService.AddCacheToCreate();
			cacheCreationService.AddCacheToCreate(ObjectKind.Pet);
			cacheCreationService.AddCacheToCreate(ObjectKind.MinionOrMount);
			cacheCreationService.AddCacheToCreate(ObjectKind.Companion);
		});
		base.Mediator.Subscribe<SelfMuteSettingChangedMessage>(this, delegate
		{
			cacheCreationService.Logger.LogInformation("[Self-Mute] Cache service received message, triggering player data recreation");
			cacheCreationService.AddCacheToCreate();
		});
		base.Mediator.Subscribe<FrameworkUpdateMessage>(this, delegate
		{
			cacheCreationService.ProcessCacheCreation();
		});
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		_playerRelatedObjects.Values.ToList().ForEach(delegate(GameObjectHandler p)
		{
			p.Dispose();
		});
		_runtimeCts.Cancel();
		_runtimeCts.Dispose();
		_creationCts.Cancel();
		_creationCts.Dispose();
	}

	private void AddCacheToCreate(ObjectKind kind = ObjectKind.Player)
	{
		_debounceCts.Cancel();
		_debounceCts.Dispose();
		_debounceCts = new CancellationTokenSource();
		CancellationToken token = _debounceCts.Token;
		_cacheCreateLock.Wait();
		_debouncedObjectCache.Add(kind);
		_cacheCreateLock.Release();
		Task.Run(async delegate
		{
			await Task.Delay(TimeSpan.FromSeconds(1L), token).ConfigureAwait(continueOnCapturedContext: false);
			base.Logger.LogTrace("Debounce complete, inserting objects to create for: {obj}", string.Join(", ", _debouncedObjectCache));
			await _cacheCreateLock.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			foreach (ObjectKind item in _debouncedObjectCache)
			{
				_cachesToCreate.Add(item);
			}
			_debouncedObjectCache.Clear();
			_cacheCreateLock.Release();
		});
	}

	private void ProcessCacheCreation()
	{
		if (_isZoning || _haltCharaDataCreation || _cachesToCreate.Count == 0)
		{
			return;
		}
		if (_playerRelatedObjects.Any<KeyValuePair<ObjectKind, GameObjectHandler>>(delegate(KeyValuePair<ObjectKind, GameObjectHandler> p)
		{
			GameObjectHandler.DrawCondition currentDrawCondition = p.Value.CurrentDrawCondition;
			bool flag = (uint)currentDrawCondition <= 2u;
			return !flag;
		}))
		{
			base.Logger.LogDebug("Waiting for draw to finish before executing cache creation");
			return;
		}
		_creationCts.Cancel();
		_creationCts.Dispose();
		_creationCts = new CancellationTokenSource();
		_cacheCreateLock.Wait(_creationCts.Token);
		List<ObjectKind> objectKindsToCreate = _cachesToCreate.ToList();
		foreach (ObjectKind creationObj in objectKindsToCreate)
		{
			_currentlyCreating.Add(creationObj);
		}
		_cachesToCreate.Clear();
		_cacheCreateLock.Release();
		Task.Run(async delegate
		{
			using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_creationCts.Token, _runtimeCts.Token);
			await Task.Delay(TimeSpan.FromSeconds(1L), linkedCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			base.Logger.LogDebug("Creating Caches for {objectKinds}", string.Join(", ", objectKindsToCreate));
			try
			{
				Dictionary<ObjectKind, CharacterDataFragment?> createdData = new Dictionary<ObjectKind, CharacterDataFragment>();
				foreach (ObjectKind objectKind in _currentlyCreating)
				{
					Dictionary<ObjectKind, CharacterDataFragment?> dictionary = createdData;
					ObjectKind key = objectKind;
					dictionary[key] = await _characterDataFactory.BuildCharacterData(_playerRelatedObjects[objectKind], linkedCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
				foreach (KeyValuePair<ObjectKind, CharacterDataFragment> kvp in createdData)
				{
					_playerData.SetFragment(kvp.Key, kvp.Value);
				}
				base.Mediator.Publish(new CharacterDataCreatedMessage(_playerData.ToAPI(_mareConfigService.Current.MuteOwnSounds)));
				_currentlyCreating.Clear();
			}
			catch (OperationCanceledException)
			{
				base.Logger.LogDebug("Cache Creation cancelled");
			}
			catch (Exception ex)
			{
				base.Logger.LogCritical(ex, "Error during Cache Creation Processing");
			}
			finally
			{
				base.Logger.LogDebug("Cache Creation complete");
			}
		});
	}
}
