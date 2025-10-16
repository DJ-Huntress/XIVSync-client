using System;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging;
using XIVSync.API.Dto.CharaData;
using XIVSync.Services;

namespace XIVSync.Interop.Ipc;

public sealed class IpcCallerBrio : IIpcCaller, IDisposable
{
	private readonly ILogger<IpcCallerBrio> _logger;

	private readonly DalamudUtilService _dalamudUtilService;

	private readonly ICallGateSubscriber<(int, int)> _brioApiVersion;

	private readonly ICallGateSubscriber<bool, bool, bool, Task<IGameObject?>> _brioSpawnExAsync;

	private readonly ICallGateSubscriber<Task<IGameObject?>> _brioSpawnAsync;

	private readonly ICallGateSubscriber<IGameObject?> _brioSpawn;

	private readonly ICallGateSubscriber<IGameObject, bool> _brioDespawnActor;

	private readonly ICallGateSubscriber<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool> _brioSetModelTransform;

	private readonly ICallGateSubscriber<IGameObject, (Vector3?, Quaternion?, Vector3?)> _brioGetModelTransform;

	private readonly ICallGateSubscriber<IGameObject, string?> _brioGetPoseAsJson;

	private readonly ICallGateSubscriber<IGameObject, string, bool, bool> _brioSetPoseFromJson;

	private readonly ICallGateSubscriber<IGameObject, bool> _brioFreezeActor;

	private readonly ICallGateSubscriber<bool> _brioFreezePhysics;

	public bool APIAvailable { get; private set; }

	public IpcCallerBrio(ILogger<IpcCallerBrio> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtilService)
	{
		_logger = logger;
		_dalamudUtilService = dalamudUtilService;
		_brioApiVersion = pi.GetIpcSubscriber<(int, int)>("Brio.ApiVersion");
		_brioSpawnExAsync = pi.GetIpcSubscriber<bool, bool, bool, Task<IGameObject>>("Brio.Actor.SpawnExAsync");
		_brioSpawnAsync = pi.GetIpcSubscriber<Task<IGameObject>>("Brio.Actor.SpawnAsync");
		_brioSpawn = pi.GetIpcSubscriber<IGameObject>("Brio.Actor.Spawn");
		_brioDespawnActor = pi.GetIpcSubscriber<IGameObject, bool>("Brio.Actor.Despawn");
		_brioSetModelTransform = pi.GetIpcSubscriber<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool>("Brio.Actor.SetModelTransform");
		_brioGetModelTransform = pi.GetIpcSubscriber<IGameObject, (Vector3?, Quaternion?, Vector3?)>("Brio.Actor.GetModelTransform");
		_brioGetPoseAsJson = pi.GetIpcSubscriber<IGameObject, string>("Brio.Actor.Pose.GetPoseAsJson");
		_brioSetPoseFromJson = pi.GetIpcSubscriber<IGameObject, string, bool, bool>("Brio.Actor.Pose.LoadFromJson");
		_brioFreezeActor = pi.GetIpcSubscriber<IGameObject, bool>("Brio.Actor.Freeze");
		_brioFreezePhysics = pi.GetIpcSubscriber<bool>("Brio.FreezePhysics");
		CheckAPI();
	}

	public void CheckAPI()
	{
		try
		{
			(int, int) tuple = _brioApiVersion.InvokeFunc();
			int major = tuple.Item1;
			int minor = tuple.Item2;
			APIAvailable = major >= 2;
			_logger.LogDebug("Brio.ApiVersion = {Major}.{Minor}, APIAvailable={Avail}", major, minor, APIAvailable);
		}
		catch
		{
			APIAvailable = false;
		}
	}

	public async Task<IGameObject?> SpawnActorAsync()
	{
		if (!APIAvailable)
		{
			return null;
		}
		try
		{
			IGameObject ex = await _brioSpawnExAsync.InvokeFunc(arg1: false, arg2: false, arg3: true).ConfigureAwait(continueOnCapturedContext: false);
			if (ex != null)
			{
				return ex;
			}
		}
		catch (Exception e)
		{
			_logger.LogDebug(e, "Brio.Actor.SpawnExAsync failed");
		}
		try
		{
			IGameObject a = await _brioSpawnAsync.InvokeFunc().ConfigureAwait(continueOnCapturedContext: false);
			if (a != null)
			{
				return a;
			}
		}
		catch (Exception e)
		{
			_logger.LogDebug(e, "Brio.Actor.SpawnAsync failed");
		}
		try
		{
			return _brioSpawn.InvokeFunc();
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Brio.Actor.Spawn failed");
			return null;
		}
	}

	public async Task<bool> DespawnActorAsync(nint address)
	{
		if (!APIAvailable)
		{
			return false;
		}
		IGameObject gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(continueOnCapturedContext: false);
		if (gameObject == null)
		{
			return false;
		}
		try
		{
			return await _dalamudUtilService.RunOnFrameworkThread(() => _brioDespawnActor.InvokeFunc(gameObject), "DespawnActorAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerBrio.cs", 110).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Brio.Actor.Despawn failed");
			return false;
		}
	}

	public async Task<bool> ApplyTransformAsync(nint address, WorldData data)
	{
		if (!APIAvailable)
		{
			return false;
		}
		IGameObject gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(continueOnCapturedContext: false);
		if (gameObject == null)
		{
			return false;
		}
		try
		{
			return await _dalamudUtilService.RunOnFrameworkThread(() => _brioSetModelTransform.InvokeFunc(gameObject, new Vector3(data.PositionX, data.PositionY, data.PositionZ), new Quaternion(data.RotationX, data.RotationY, data.RotationZ, data.RotationW), new Vector3(data.ScaleX, data.ScaleY, data.ScaleZ), arg5: false), "ApplyTransformAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerBrio.cs", 126).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Brio.Actor.SetModelTransform failed");
			return false;
		}
	}

	public async Task<WorldData> GetTransformAsync(nint address)
	{
		if (!APIAvailable)
		{
			return default(WorldData);
		}
		IGameObject gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(continueOnCapturedContext: false);
		if (gameObject == null)
		{
			return default(WorldData);
		}
		try
		{
			(Vector3?, Quaternion?, Vector3?) data = await _dalamudUtilService.RunOnFrameworkThread(() => _brioGetModelTransform.InvokeFunc(gameObject), "GetTransformAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerBrio.cs", 148).ConfigureAwait(continueOnCapturedContext: false);
			if (!data.Item1.HasValue || !data.Item2.HasValue || !data.Item3.HasValue)
			{
				return default(WorldData);
			}
			WorldData result = default(WorldData);
			result.PositionX = data.Item1.Value.X;
			result.PositionY = data.Item1.Value.Y;
			result.PositionZ = data.Item1.Value.Z;
			result.RotationX = data.Item2.Value.X;
			result.RotationY = data.Item2.Value.Y;
			result.RotationZ = data.Item2.Value.Z;
			result.RotationW = data.Item2.Value.W;
			result.ScaleX = data.Item3.Value.X;
			result.ScaleY = data.Item3.Value.Y;
			result.ScaleZ = data.Item3.Value.Z;
			return result;
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Brio.Actor.GetModelTransform failed");
			return default(WorldData);
		}
	}

	public async Task<string?> GetPoseAsync(nint address)
	{
		if (!APIAvailable)
		{
			return null;
		}
		IGameObject gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(continueOnCapturedContext: false);
		if (gameObject == null)
		{
			return null;
		}
		try
		{
			return await _dalamudUtilService.RunOnFrameworkThread(() => _brioGetPoseAsJson.InvokeFunc(gameObject), "GetPoseAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerBrio.cs", 178).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Brio.Actor.Pose.GetPoseAsJson failed");
			return null;
		}
	}

	public async Task<bool> SetPoseAsync(nint address, string pose)
	{
		if (!APIAvailable)
		{
			return false;
		}
		IGameObject gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(continueOnCapturedContext: false);
		if (gameObject == null)
		{
			return false;
		}
		try
		{
			JsonNode applicablePose = JsonNode.Parse(pose);
			string currentPose = await _dalamudUtilService.RunOnFrameworkThread(() => _brioGetPoseAsJson.InvokeFunc(gameObject), "SetPoseAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerBrio.cs", 196).ConfigureAwait(continueOnCapturedContext: false);
			if (currentPose == null)
			{
				return false;
			}
			JsonNode current = JsonNode.Parse(currentPose);
			if (current["ModelDifference"] != null)
			{
				applicablePose["ModelDifference"] = JsonNode.Parse(current["ModelDifference"].ToJsonString());
			}
			await _dalamudUtilService.RunOnFrameworkThread(delegate
			{
				_brioFreezeActor.InvokeFunc(gameObject);
				_brioFreezePhysics.InvokeFunc();
			}, "SetPoseAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerBrio.cs", 203).ConfigureAwait(continueOnCapturedContext: false);
			return await _dalamudUtilService.RunOnFrameworkThread(() => _brioSetPoseFromJson.InvokeFunc(gameObject, applicablePose.ToJsonString(), arg3: false), "SetPoseAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Interop\\Ipc\\IpcCallerBrio.cs", 209).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Brio.Actor.Pose.LoadFromJson failed");
			return false;
		}
	}

	public void Dispose()
	{
	}
}
