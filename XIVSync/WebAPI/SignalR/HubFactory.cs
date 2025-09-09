using System;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.WebAPI.SignalR.Utils;

namespace XIVSync.WebAPI.SignalR;

public class HubFactory : MediatorSubscriberBase
{
	private readonly ILoggerProvider _loggingProvider;

	private readonly ServerConfigurationManager _serverConfigurationManager;

	private readonly TokenProvider _tokenProvider;

	private HubConnection? _instance;

	private bool _isDisposed;

	private readonly bool _isWine;

	public HubFactory(ILogger<HubFactory> logger, MareMediator mediator, ServerConfigurationManager serverConfigurationManager, TokenProvider tokenProvider, ILoggerProvider pluginLog, DalamudUtilService dalamudUtilService)
		: base(logger, mediator)
	{
		_serverConfigurationManager = serverConfigurationManager;
		_tokenProvider = tokenProvider;
		_loggingProvider = pluginLog;
		_isWine = dalamudUtilService.IsWine;
	}

	public async Task DisposeHubAsync()
	{
		if (_instance != null && !_isDisposed)
		{
			base.Logger.LogDebug("Disposing current HubConnection");
			_isDisposed = true;
			_instance.Closed -= HubOnClosed;
			_instance.Reconnecting -= HubOnReconnecting;
			_instance.Reconnected -= HubOnReconnected;
			await _instance.StopAsync().ConfigureAwait(continueOnCapturedContext: false);
			await _instance.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
			_instance = null;
			base.Logger.LogDebug("Current HubConnection disposed");
		}
	}

	public HubConnection GetOrCreate(CancellationToken ct)
	{
		if (!_isDisposed && _instance != null)
		{
			return _instance;
		}
		return BuildHubConnection(ct);
	}

	private HubConnection BuildHubConnection(CancellationToken ct)
	{
		HttpTransportType transportType = _serverConfigurationManager.GetTransport() switch
		{
			HttpTransportType.None => HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling, 
			HttpTransportType.WebSockets => HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling, 
			HttpTransportType.ServerSentEvents => HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling, 
			HttpTransportType.LongPolling => HttpTransportType.LongPolling, 
			_ => HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling, 
		};
		if (_isWine && !_serverConfigurationManager.CurrentServer.ForceWebSockets && transportType.HasFlag(HttpTransportType.WebSockets))
		{
			base.Logger.LogDebug("Wine detected, falling back to ServerSentEvents / LongPolling");
			transportType = HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling;
		}
		base.Logger.LogDebug("Building new HubConnection using transport {transport}", transportType);
		_instance = new HubConnectionBuilder().WithUrl(_serverConfigurationManager.CurrentApiUrl + "/mare", delegate(HttpConnectionOptions options)
		{
			options.AccessTokenProvider = () => _tokenProvider.GetOrUpdateToken(ct);
			options.Transports = transportType;
		}).AddMessagePackProtocol(delegate(MessagePackHubProtocolOptions opt)
		{
			IFormatterResolver resolver = CompositeResolver.Create(StandardResolverAllowPrivate.Instance, BuiltinResolver.Instance, AttributeFormatterResolver.Instance, DynamicEnumAsStringResolver.Instance, DynamicGenericResolver.Instance, DynamicUnionResolver.Instance, DynamicObjectResolver.Instance, PrimitiveObjectResolver.Instance, StandardResolver.Instance);
			opt.SerializerOptions = MessagePackSerializerOptions.Standard.WithResolver(CompositeResolver.Create(ContractlessStandardResolver.Instance, StandardResolver.Instance));
			opt.SerializerOptions = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4Block).WithResolver(resolver);
		}).WithAutomaticReconnect(new ForeverRetryPolicy(base.Mediator))
			.ConfigureLogging(delegate(ILoggingBuilder a)
			{
				a.ClearProviders().AddProvider(_loggingProvider);
				a.SetMinimumLevel(LogLevel.Information);
			})
			.Build();
		_instance.Closed += HubOnClosed;
		_instance.Reconnecting += HubOnReconnecting;
		_instance.Reconnected += HubOnReconnected;
		_isDisposed = false;
		return _instance;
	}

	private Task HubOnClosed(Exception? arg)
	{
		base.Mediator.Publish(new HubClosedMessage(arg));
		return Task.CompletedTask;
	}

	private Task HubOnReconnected(string? arg)
	{
		base.Mediator.Publish(new HubReconnectedMessage(arg));
		return Task.CompletedTask;
	}

	private Task HubOnReconnecting(Exception? arg)
	{
		base.Mediator.Publish(new HubReconnectingMessage(arg));
		return Task.CompletedTask;
	}
}
