using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using Microsoft.Extensions.Logging;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Utils;

namespace XIVSync.Interop.Ipc;

public class RedrawManager : IDisposable
{
	private readonly MareMediator _mareMediator;

	private readonly DalamudUtilService _dalamudUtil;

	private readonly ConcurrentDictionary<nint, bool> _penumbraRedrawRequests = new ConcurrentDictionary<nint, bool>();

	private CancellationTokenSource _disposalCts = new CancellationTokenSource();

	public SemaphoreSlim RedrawSemaphore { get; init; } = new SemaphoreSlim(2, 2);

	public RedrawManager(MareMediator mareMediator, DalamudUtilService dalamudUtil)
	{
		_mareMediator = mareMediator;
		_dalamudUtil = dalamudUtil;
	}

	public async Task PenumbraRedrawInternalAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, Action<ICharacter> action, CancellationToken token)
	{
		_mareMediator.Publish(new PenumbraStartRedrawMessage(handler.Address));
		_penumbraRedrawRequests[handler.Address] = true;
		try
		{
			using CancellationTokenSource cancelToken = new CancellationTokenSource();
			[cancelToken.Token, token, _disposalCts.Token]
			using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource([cancelToken.Token, token, _disposalCts.Token]);
			CancellationToken combinedToken = combinedCts.Token;
			cancelToken.CancelAfter(TimeSpan.FromSeconds(15L));
			await handler.ActOnFrameworkAfterEnsureNoDrawAsync(action, combinedToken).ConfigureAwait(continueOnCapturedContext: false);
			if (!_disposalCts.Token.IsCancellationRequested)
			{
				await _dalamudUtil.WaitWhileCharacterIsDrawing(logger, handler, applicationId, 30000, combinedToken).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		finally
		{
			_penumbraRedrawRequests[handler.Address] = false;
			_mareMediator.Publish(new PenumbraEndRedrawMessage(handler.Address));
		}
	}

	internal void Cancel()
	{
		_disposalCts = _disposalCts.CancelRecreate();
	}

	public void Dispose()
	{
		_disposalCts?.CancelDispose();
		RedrawSemaphore?.Dispose();
	}
}
