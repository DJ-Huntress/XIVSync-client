using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services;
using XIVSync.Services.Mediator;

namespace XIVSync.Interop.Ipc;

public class IpcProvider : IHostedService, IMediatorSubscriber
{
	private readonly ILogger<IpcProvider> _logger;

	private readonly IDalamudPluginInterface _pi;

	private readonly CharaDataManager _charaDataManager;

	private ICallGateProvider<string, IGameObject, bool>? _loadFileProvider;

	private ICallGateProvider<string, IGameObject, Task<bool>>? _loadFileAsyncProvider;

	private ICallGateProvider<List<nint>>? _handledGameAddresses;

	private readonly List<GameObjectHandler> _activeGameObjectHandlers = new List<GameObjectHandler>();

	public MareMediator Mediator { get; init; }

	public IpcProvider(ILogger<IpcProvider> logger, IDalamudPluginInterface pi, CharaDataManager charaDataManager, MareMediator mareMediator)
	{
		_logger = logger;
		_pi = pi;
		_charaDataManager = charaDataManager;
		Mediator = mareMediator;
		Mediator.Subscribe(this, delegate(GameObjectHandlerCreatedMessage msg)
		{
			if (!msg.OwnedObject)
			{
				_activeGameObjectHandlers.Add(msg.GameObjectHandler);
			}
		});
		Mediator.Subscribe(this, delegate(GameObjectHandlerDestroyedMessage msg)
		{
			if (!msg.OwnedObject)
			{
				_activeGameObjectHandlers.Remove(msg.GameObjectHandler);
			}
		});
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting IpcProviderService");
		_loadFileProvider = _pi.GetIpcProvider<string, IGameObject, bool>("MareSynchronos.LoadMcdf");
		_loadFileProvider.RegisterFunc(LoadMcdf);
		_loadFileAsyncProvider = _pi.GetIpcProvider<string, IGameObject, Task<bool>>("MareSynchronos.LoadMcdfAsync");
		_loadFileAsyncProvider.RegisterFunc(LoadMcdfAsync);
		_handledGameAddresses = _pi.GetIpcProvider<List<nint>>("MareSynchronos.GetHandledAddresses");
		_handledGameAddresses.RegisterFunc(GetHandledAddresses);
		_logger.LogInformation("Started IpcProviderService");
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_logger.LogDebug("Stopping IpcProvider Service");
		_loadFileProvider?.UnregisterFunc();
		_loadFileAsyncProvider?.UnregisterFunc();
		_handledGameAddresses?.UnregisterFunc();
		Mediator.UnsubscribeAll(this);
		return Task.CompletedTask;
	}

	private async Task<bool> LoadMcdfAsync(string path, IGameObject target)
	{
		await ApplyFileAsync(path, target).ConfigureAwait(continueOnCapturedContext: false);
		return true;
	}

	private bool LoadMcdf(string path, IGameObject target)
	{
		Task.Run(async delegate
		{
			await ApplyFileAsync(path, target).ConfigureAwait(continueOnCapturedContext: false);
		}).ConfigureAwait(continueOnCapturedContext: false);
		return true;
	}

	private async Task ApplyFileAsync(string path, IGameObject target)
	{
		_charaDataManager.LoadMcdf(path);
		await (_charaDataManager.LoadedMcdfHeader ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);
		_charaDataManager.McdfApplyToTarget(target.Name.TextValue);
	}

	private List<nint> GetHandledAddresses()
	{
		return (from g in _activeGameObjectHandlers
			where g.Address != IntPtr.Zero
			select g.Address).Distinct().ToList();
	}
}
