using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Enum;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services;
using XIVSync.Services.Mediator;

namespace XIVSync.PlayerData.Factories;

public class GameObjectHandlerFactory
{
	private readonly DalamudUtilService _dalamudUtilService;

	private readonly ILoggerFactory _loggerFactory;

	private readonly MareMediator _mareMediator;

	private readonly PerformanceCollectorService _performanceCollectorService;

	public GameObjectHandlerFactory(ILoggerFactory loggerFactory, PerformanceCollectorService performanceCollectorService, MareMediator mareMediator, DalamudUtilService dalamudUtilService)
	{
		_loggerFactory = loggerFactory;
		_performanceCollectorService = performanceCollectorService;
		_mareMediator = mareMediator;
		_dalamudUtilService = dalamudUtilService;
	}

	public async Task<GameObjectHandler> Create(ObjectKind objectKind, Func<nint> getAddressFunc, bool isWatched = false)
	{
		return await _dalamudUtilService.RunOnFrameworkThread(() => new GameObjectHandler(_loggerFactory.CreateLogger<GameObjectHandler>(), _performanceCollectorService, _mareMediator, _dalamudUtilService, objectKind, getAddressFunc, isWatched), "Create", "\\\\wsl.localhost\\Ubuntu\\home\\ddev\\xivsync\\sync_client2\\XIVSync\\PlayerData\\Factories\\GameObjectHandlerFactory.cs", 27).ConfigureAwait(continueOnCapturedContext: false);
	}
}
