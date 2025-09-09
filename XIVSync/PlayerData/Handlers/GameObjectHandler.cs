using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Enum;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Utils;

namespace XIVSync.PlayerData.Handlers;

public sealed class GameObjectHandler : DisposableMediatorSubscriberBase, IHighPriorityMediatorSubscriber, IMediatorSubscriber
{
	public enum DrawCondition
	{
		None,
		ObjectZero,
		DrawObjectZero,
		RenderFlags,
		ModelInSlotLoaded,
		ModelFilesInSlotLoaded
	}

	private readonly DalamudUtilService _dalamudUtil;

	private readonly Func<nint> _getAddress;

	private readonly bool _isOwnedObject;

	private readonly PerformanceCollectorService _performanceCollector;

	private byte _classJob;

	private Task? _delayedZoningTask;

	private bool _haltProcessing;

	private CancellationTokenSource _zoningCts = new CancellationTokenSource();

	public nint Address { get; private set; }

	public DrawCondition CurrentDrawCondition { get; set; }

	public byte Gender { get; private set; }

	public string Name { get; private set; }

	public XIVSync.API.Data.Enum.ObjectKind ObjectKind { get; }

	public byte RaceId { get; private set; }

	public byte TribeId { get; private set; }

	private byte[] CustomizeData { get; set; } = new byte[26];

	private nint DrawObjectAddress { get; set; }

	private byte[] EquipSlotData { get; set; } = new byte[40];

	private ushort[] MainHandData { get; set; } = new ushort[3];

	private ushort[] OffHandData { get; set; } = new ushort[3];

	public GameObjectHandler(ILogger<GameObjectHandler> logger, PerformanceCollectorService performanceCollector, MareMediator mediator, DalamudUtilService dalamudUtil, XIVSync.API.Data.Enum.ObjectKind objectKind, Func<nint> getAddress, bool ownedObject = true)
		: base(logger, mediator)
	{
		GameObjectHandler gameObjectHandler = this;
		_performanceCollector = performanceCollector;
		ObjectKind = objectKind;
		_dalamudUtil = dalamudUtil;
		_getAddress = delegate
		{
			gameObjectHandler._dalamudUtil.EnsureIsOnFramework();
			return getAddress();
		};
		_isOwnedObject = ownedObject;
		Name = string.Empty;
		if (ownedObject)
		{
			base.Mediator.Subscribe(this, delegate(TransientResourceChangedMessage msg)
			{
				Task? delayedZoningTask = gameObjectHandler._delayedZoningTask;
				if ((delayedZoningTask == null || delayedZoningTask.IsCompleted) && msg.Address == gameObjectHandler.Address)
				{
					gameObjectHandler.Mediator.Publish(new CreateCacheForObjectMessage(gameObjectHandler));
				}
			});
		}
		base.Mediator.Subscribe<FrameworkUpdateMessage>(this, delegate
		{
			gameObjectHandler.FrameworkUpdate();
		});
		base.Mediator.Subscribe<ZoneSwitchEndMessage>(this, delegate
		{
			gameObjectHandler.ZoneSwitchEnd();
		});
		base.Mediator.Subscribe<ZoneSwitchStartMessage>(this, delegate
		{
			gameObjectHandler.ZoneSwitchStart();
		});
		base.Mediator.Subscribe<CutsceneStartMessage>(this, delegate
		{
			gameObjectHandler._haltProcessing = true;
		});
		base.Mediator.Subscribe<CutsceneEndMessage>(this, delegate
		{
			gameObjectHandler._haltProcessing = false;
			gameObjectHandler.ZoneSwitchEnd();
		});
		base.Mediator.Subscribe(this, delegate(PenumbraStartRedrawMessage msg)
		{
			if (msg.Address == gameObjectHandler.Address)
			{
				gameObjectHandler._haltProcessing = true;
			}
		});
		base.Mediator.Subscribe(this, delegate(PenumbraEndRedrawMessage msg)
		{
			if (msg.Address == gameObjectHandler.Address)
			{
				gameObjectHandler._haltProcessing = false;
			}
		});
		base.Mediator.Publish(new GameObjectHandlerCreatedMessage(this, _isOwnedObject));
		_dalamudUtil.RunOnFrameworkThread(CheckAndUpdateObject, ".ctor", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\PlayerData\\Handlers\\GameObjectHandler.cs", 80).GetAwaiter().GetResult();
	}

	public async Task ActOnFrameworkAfterEnsureNoDrawAsync(Action<ICharacter> act, CancellationToken token)
	{
		while (await _dalamudUtil.RunOnFrameworkThread(delegate
		{
			if (_haltProcessing)
			{
				CheckAndUpdateObject();
			}
			if (CurrentDrawCondition != DrawCondition.None)
			{
				return true;
			}
			if (_dalamudUtil.CreateGameObject(Address) is ICharacter obj)
			{
				act(obj);
			}
			return false;
		}, "ActOnFrameworkAfterEnsureNoDrawAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\PlayerData\\Handlers\\GameObjectHandler.cs", 108).ConfigureAwait(continueOnCapturedContext: false))
		{
			await Task.Delay(250, token).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public void CompareNameAndThrow(string name)
	{
		if (!string.Equals(Name, name, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Player name not equal to requested name, pointer invalid");
		}
		if (Address == IntPtr.Zero)
		{
			throw new InvalidOperationException("Player pointer is zero, pointer invalid");
		}
	}

	public IGameObject? GetGameObject()
	{
		return _dalamudUtil.CreateGameObject(Address);
	}

	public void Invalidate()
	{
		Address = IntPtr.Zero;
		DrawObjectAddress = IntPtr.Zero;
		_haltProcessing = false;
	}

	public async Task<bool> IsBeingDrawnRunOnFrameworkAsync()
	{
		return await _dalamudUtil.RunOnFrameworkThread((Func<bool>)IsBeingDrawn, "IsBeingDrawnRunOnFrameworkAsync", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\PlayerData\\Handlers\\GameObjectHandler.cs", 150).ConfigureAwait(continueOnCapturedContext: false);
	}

	public override string ToString()
	{
		string owned = (_isOwnedObject ? "Self" : "Other");
		return $"{owned}/{ObjectKind}:{Name} ({Address:X},{DrawObjectAddress:X})";
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		base.Mediator.Publish(new GameObjectHandlerDestroyedMessage(this, _isOwnedObject));
	}

	private unsafe void CheckAndUpdateObject()
	{
		nint prevAddr = Address;
		nint prevDrawObj = DrawObjectAddress;
		Address = _getAddress();
		if (Address != IntPtr.Zero)
		{
			nint drawObjAddr = (nint)((GameObject*)Address)->DrawObject;
			DrawObjectAddress = drawObjAddr;
			CurrentDrawCondition = DrawCondition.None;
		}
		else
		{
			DrawObjectAddress = IntPtr.Zero;
			CurrentDrawCondition = DrawCondition.DrawObjectZero;
		}
		CurrentDrawCondition = IsBeingDrawnUnsafe();
		if (_haltProcessing)
		{
			return;
		}
		bool drawObjDiff = DrawObjectAddress != prevDrawObj;
		bool addrDiff = Address != prevAddr;
		if (Address != IntPtr.Zero && DrawObjectAddress != IntPtr.Zero)
		{
			Character* chara = (Character*)Address;
			string name = chara->GameObject.NameString;
			bool nameChange = !string.Equals(name, Name, StringComparison.Ordinal);
			if (nameChange)
			{
				Name = name;
			}
			bool equipDiff = false;
			if (((DrawObject*)DrawObjectAddress)->Object.GetObjectType() == ObjectType.CharacterBase && ((CharacterBase*)DrawObjectAddress)->GetModelType() == CharacterBase.ModelType.Human)
			{
				byte classJob = chara->CharacterData.ClassJob;
				if (classJob != _classJob)
				{
					base.Logger.LogTrace("[{this}] classjob changed from {old} to {new}", this, _classJob, classJob);
					_classJob = classJob;
					base.Mediator.Publish(new ClassJobChangedMessage(this));
				}
				equipDiff = CompareAndUpdateEquipByteData((byte*)(&((Human*)DrawObjectAddress)->Head));
				ref DrawObjectData mh = ref chara->DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand);
				ref DrawObjectData oh = ref chara->DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand);
				equipDiff |= CompareAndUpdateMainHand((Weapon*)mh.DrawObject);
				equipDiff |= CompareAndUpdateOffHand((Weapon*)oh.DrawObject);
				if (equipDiff)
				{
					base.Logger.LogTrace("Checking [{this}] equip data as human from draw obj, result: {diff}", this, equipDiff);
				}
			}
			else
			{
				equipDiff = CompareAndUpdateEquipByteData((byte*)Unsafe.AsPointer(ref chara->DrawData.EquipmentModelIds[0]));
				if (equipDiff)
				{
					base.Logger.LogTrace("Checking [{this}] equip data from game obj, result: {diff}", this, equipDiff);
				}
			}
			if (equipDiff && !_isOwnedObject)
			{
				base.Logger.LogTrace("[{this}] Changed", this);
				return;
			}
			bool customizeDiff = false;
			if (((DrawObject*)DrawObjectAddress)->Object.GetObjectType() == ObjectType.CharacterBase && ((CharacterBase*)DrawObjectAddress)->GetModelType() == CharacterBase.ModelType.Human)
			{
				byte gender = ((Human*)DrawObjectAddress)->Customize.Sex;
				byte raceId = ((Human*)DrawObjectAddress)->Customize.Race;
				byte tribeId = ((Human*)DrawObjectAddress)->Customize.Tribe;
				if (_isOwnedObject && ObjectKind == XIVSync.API.Data.Enum.ObjectKind.Player && (gender != Gender || raceId != RaceId || tribeId != TribeId))
				{
					base.Mediator.Publish(new CensusUpdateMessage(gender, raceId, tribeId));
					Gender = gender;
					RaceId = raceId;
					TribeId = tribeId;
				}
				customizeDiff = CompareAndUpdateCustomizeData(((Human*)DrawObjectAddress)->Customize.Data);
				if (customizeDiff)
				{
					base.Logger.LogTrace("Checking [{this}] customize data as human from draw obj, result: {diff}", this, customizeDiff);
				}
			}
			else
			{
				customizeDiff = CompareAndUpdateCustomizeData(chara->DrawData.CustomizeData.Data);
				if (customizeDiff)
				{
					base.Logger.LogTrace("Checking [{this}] customize data from game obj, result: {diff}", this, equipDiff);
				}
			}
			if ((addrDiff || drawObjDiff || equipDiff || customizeDiff || nameChange) && _isOwnedObject)
			{
				base.Logger.LogDebug("[{this}] Changed, Sending CreateCacheObjectMessage", this);
				base.Mediator.Publish(new CreateCacheForObjectMessage(this));
			}
		}
		else if (addrDiff || drawObjDiff)
		{
			CurrentDrawCondition = DrawCondition.DrawObjectZero;
			base.Logger.LogTrace("[{this}] Changed", this);
			if (_isOwnedObject && ObjectKind != XIVSync.API.Data.Enum.ObjectKind.Player)
			{
				base.Mediator.Publish(new ClearCacheForObjectMessage(this));
			}
		}
	}

	private bool CompareAndUpdateCustomizeData(Span<byte> customizeData)
	{
		bool hasChanges = false;
		for (int i = 0; i < customizeData.Length; i++)
		{
			byte data = customizeData[i];
			if (CustomizeData[i] != data)
			{
				CustomizeData[i] = data;
				hasChanges = true;
			}
		}
		return hasChanges;
	}

	private unsafe bool CompareAndUpdateEquipByteData(byte* equipSlotData)
	{
		bool hasChanges = false;
		for (int i = 0; i < EquipSlotData.Length; i++)
		{
			byte data = equipSlotData[i];
			if (EquipSlotData[i] != data)
			{
				EquipSlotData[i] = data;
				hasChanges = true;
			}
		}
		return hasChanges;
	}

	private unsafe bool CompareAndUpdateMainHand(Weapon* weapon)
	{
		if (weapon == (Weapon*)IntPtr.Zero)
		{
			return false;
		}
		int num = 0 | ((weapon->ModelSetId != MainHandData[0]) ? 1 : 0);
		MainHandData[0] = weapon->ModelSetId;
		int num2 = num | ((weapon->Variant != MainHandData[1]) ? 1 : 0);
		MainHandData[1] = weapon->Variant;
		int result = num2 | ((weapon->SecondaryId != MainHandData[2]) ? 1 : 0);
		MainHandData[2] = weapon->SecondaryId;
		return (byte)result != 0;
	}

	private unsafe bool CompareAndUpdateOffHand(Weapon* weapon)
	{
		if (weapon == (Weapon*)IntPtr.Zero)
		{
			return false;
		}
		int num = 0 | ((weapon->ModelSetId != OffHandData[0]) ? 1 : 0);
		OffHandData[0] = weapon->ModelSetId;
		int num2 = num | ((weapon->Variant != OffHandData[1]) ? 1 : 0);
		OffHandData[1] = weapon->Variant;
		int result = num2 | ((weapon->SecondaryId != OffHandData[2]) ? 1 : 0);
		OffHandData[2] = weapon->SecondaryId;
		return (byte)result != 0;
	}

	private void FrameworkUpdate()
	{
		Task? delayedZoningTask = _delayedZoningTask;
		if (delayedZoningTask != null && !delayedZoningTask.IsCompleted)
		{
			return;
		}
		try
		{
			PerformanceCollectorService performanceCollector = _performanceCollector;
			MareInterpolatedStringHandler counterName = new MareInterpolatedStringHandler(24, 4);
			counterName.AppendLiteral("CheckAndUpdateObject>");
			counterName.AppendFormatted(_isOwnedObject ? "Self" : "Other");
			counterName.AppendLiteral("+");
			counterName.AppendFormatted(ObjectKind);
			counterName.AppendLiteral("/");
			counterName.AppendFormatted(string.IsNullOrEmpty(Name) ? "Unk" : Name);
			counterName.AppendLiteral("+");
			counterName.AppendFormatted(((IntPtr)Address).ToString("X"));
			performanceCollector.LogPerformance(this, counterName, CheckAndUpdateObject);
		}
		catch (Exception exception)
		{
			base.Logger.LogWarning(exception, "Error during FrameworkUpdate of {this}", this);
		}
	}

	private bool IsBeingDrawn()
	{
		if (_haltProcessing)
		{
			CheckAndUpdateObject();
		}
		if (_dalamudUtil.IsAnythingDrawing)
		{
			base.Logger.LogTrace("[{this}] IsBeingDrawn, Global draw block", this);
			return true;
		}
		base.Logger.LogTrace("[{this}] IsBeingDrawn, Condition: {cond}", this, CurrentDrawCondition);
		return CurrentDrawCondition != DrawCondition.None;
	}

	private unsafe DrawCondition IsBeingDrawnUnsafe()
	{
		if (Address == IntPtr.Zero)
		{
			return DrawCondition.ObjectZero;
		}
		if (DrawObjectAddress == IntPtr.Zero)
		{
			return DrawCondition.DrawObjectZero;
		}
		if (((GameObject*)Address)->RenderFlags != 0)
		{
			return DrawCondition.RenderFlags;
		}
		if (ObjectKind == XIVSync.API.Data.Enum.ObjectKind.Player)
		{
			if (((CharacterBase*)DrawObjectAddress)->HasModelInSlotLoaded != 0)
			{
				return DrawCondition.ModelInSlotLoaded;
			}
			if (((CharacterBase*)DrawObjectAddress)->HasModelFilesInSlotLoaded != 0)
			{
				return DrawCondition.ModelFilesInSlotLoaded;
			}
		}
		return DrawCondition.None;
	}

	private void ZoneSwitchEnd()
	{
		if (!_isOwnedObject)
		{
			return;
		}
		try
		{
			_zoningCts?.CancelAfter(2500);
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception exception)
		{
			base.Logger.LogWarning(exception, "Zoning CTS cancel issue");
		}
	}

	private void ZoneSwitchStart()
	{
		if (!_isOwnedObject)
		{
			return;
		}
		_zoningCts = new CancellationTokenSource();
		base.Logger.LogDebug("[{obj}] Starting Delay After Zoning", this);
		_delayedZoningTask = Task.Run(async delegate
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(120L), _zoningCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
			}
			finally
			{
				base.Logger.LogDebug("[{this}] Delay after zoning complete", this);
				_zoningCts.Dispose();
			}
		});
	}
}
