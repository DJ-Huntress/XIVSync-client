using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using Microsoft.Extensions.Logging;
using XIVSync.Services.Mediator;

namespace XIVSync.Interop;

public class VfxSpawnManager : DisposableMediatorSubscriberBase
{
	[StructLayout(LayoutKind.Explicit)]
	internal struct VfxStruct
	{
		[FieldOffset(56)]
		public byte Flags;

		[FieldOffset(80)]
		public Vector3 Position;

		[FieldOffset(96)]
		public Quaternion Rotation;

		[FieldOffset(112)]
		public Vector3 Scale;

		[FieldOffset(296)]
		public int ActorCaster;

		[FieldOffset(304)]
		public int ActorTarget;

		[FieldOffset(440)]
		public int StaticCaster;

		[FieldOffset(448)]
		public int StaticTarget;

		[FieldOffset(584)]
		public byte SomeFlags;

		[FieldOffset(608)]
		public float Red;

		[FieldOffset(612)]
		public float Green;

		[FieldOffset(616)]
		public float Blue;

		[FieldOffset(620)]
		public float Alpha;
	}

	private static readonly byte[] _pool = ((ReadOnlySpan<byte>)new byte[43]
	{
		67, 108, 105, 101, 110, 116, 46, 83, 121, 115,
		116, 101, 109, 46, 83, 99, 104, 101, 100, 117,
		108, 101, 114, 46, 73, 110, 115, 116, 97, 110,
		99, 101, 46, 86, 102, 120, 79, 98, 106, 101,
		99, 116, 0
	}).ToArray();

	[Signature("E8 ?? ?? ?? ?? F3 0F 10 35 ?? ?? ?? ?? 48 89 43 08")]
	private unsafe readonly delegate* unmanaged<byte*, byte*, VfxStruct*> _staticVfxCreate;

	[Signature("E8 ?? ?? ?? ?? ?? ?? ?? 8B 4A ?? 85 C9")]
	private unsafe readonly delegate* unmanaged<VfxStruct*, float, int, ulong> _staticVfxRun;

	[Signature("40 53 48 83 EC 20 48 8B D9 48 8B 89 ?? ?? ?? ?? 48 85 C9 74 28 33 D2 E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9")]
	private unsafe readonly delegate* unmanaged<VfxStruct*, nint> _staticVfxRemove;

	private readonly Dictionary<Guid, (nint Address, float Visibility)> _spawnedObjects = new Dictionary<Guid, (nint, float)>();

	public VfxSpawnManager(ILogger<VfxSpawnManager> logger, IGameInteropProvider gameInteropProvider, MareMediator mareMediator)
		: base(logger, mareMediator)
	{
		gameInteropProvider.InitializeFromAttributes(this);
		mareMediator.Subscribe<GposeStartMessage>(this, delegate
		{
			ChangeSpawnVisibility(0f);
		});
		mareMediator.Subscribe<GposeEndMessage>(this, delegate
		{
			RestoreSpawnVisiblity();
		});
		mareMediator.Subscribe<CutsceneStartMessage>(this, delegate
		{
			ChangeSpawnVisibility(0f);
		});
		mareMediator.Subscribe<CutsceneEndMessage>(this, delegate
		{
			RestoreSpawnVisiblity();
		});
	}

	private unsafe void RestoreSpawnVisiblity()
	{
		foreach (KeyValuePair<Guid, (nint, float)> vfx in _spawnedObjects)
		{
			((VfxStruct*)vfx.Value.Item1)->Alpha = vfx.Value.Item2;
		}
	}

	private unsafe void ChangeSpawnVisibility(float visibility)
	{
		foreach (KeyValuePair<Guid, (nint, float)> spawnedObject in _spawnedObjects)
		{
			((VfxStruct*)spawnedObject.Value.Item1)->Alpha = visibility;
		}
	}

	private unsafe VfxStruct* SpawnStatic(string path, Vector3 pos, Quaternion rotation, float r, float g, float b, float a, Vector3 scale)
	{
		VfxStruct* vfx;
		fixed (byte* terminatedPath = Encoding.UTF8.GetBytes(path).NullTerminate())
		{
			fixed (byte* pool = _pool)
			{
				vfx = _staticVfxCreate(terminatedPath, pool);
			}
		}
		if (vfx == null)
		{
			return null;
		}
		vfx->Position = new Vector3(pos.X, pos.Y + 1f, pos.Z);
		vfx->Rotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
		byte* someFlags = &vfx->SomeFlags;
		*someFlags = (byte)(*someFlags & 0xF7u);
		byte* flags = &vfx->Flags;
		*flags = (byte)(*flags | 2u);
		vfx->Red = r;
		vfx->Green = g;
		vfx->Blue = b;
		vfx->Scale = scale;
		vfx->Alpha = a;
		_staticVfxRun(vfx, 0f, -1);
		return vfx;
	}

	public unsafe Guid? SpawnObject(Vector3 position, Quaternion rotation, Vector3 scale, float r = 1f, float g = 1f, float b = 1f, float a = 0.5f)
	{
		base.Logger.LogDebug("Trying to Spawn orb VFX at {pos}, {rot}", position, rotation);
		VfxStruct* vfx = SpawnStatic("bgcommon/world/common/vfx_for_event/eff/b0150_eext_y.avfx", position, rotation, r, g, b, a, scale);
		if (vfx == null || vfx == (VfxStruct*)IntPtr.Zero)
		{
			base.Logger.LogDebug("Failed to Spawn VFX at {pos}, {rot}", position, rotation);
			return null;
		}
		Guid guid = Guid.NewGuid();
		base.Logger.LogDebug("Spawned VFX at {pos}, {rot}: 0x{ptr:X}", position, rotation, (nint)vfx);
		_spawnedObjects[guid] = ((nint)vfx, a);
		return guid;
	}

	public unsafe void MoveObject(Guid id, Vector3 newPosition)
	{
		if (_spawnedObjects.TryGetValue(id, out var vfxValue) && vfxValue.Address != IntPtr.Zero)
		{
			VfxStruct* vfx = (VfxStruct*)vfxValue.Address;
			Vector3 position = newPosition;
			position.Y = newPosition.Y + 1f;
			vfx->Position = position;
			byte* flags = &vfx->Flags;
			*flags = (byte)(*flags | 2u);
		}
	}

	public unsafe void DespawnObject(Guid? id)
	{
		if (id.HasValue && _spawnedObjects.Remove(id.Value, out var value))
		{
			base.Logger.LogDebug("Despawning {obj:X}", value.Address);
			_staticVfxRemove((VfxStruct*)value.Address);
		}
	}

	private unsafe void RemoveAllVfx()
	{
		foreach (var obj in _spawnedObjects.Values)
		{
			base.Logger.LogDebug("Despawning {obj:X}", obj);
			_staticVfxRemove((VfxStruct*)obj.Address);
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (disposing)
		{
			RemoveAllVfx();
		}
	}
}
