using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using XIVSync.API.Dto.User;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;

namespace XIVSync.PlayerData.Factories;

public class PairFactory
{
	private readonly PairHandlerFactory _cachedPlayerFactory;

	private readonly ILoggerFactory _loggerFactory;

	private readonly MareMediator _mareMediator;

	private readonly ServerConfigurationManager _serverConfigurationManager;

	public PairFactory(ILoggerFactory loggerFactory, PairHandlerFactory cachedPlayerFactory, MareMediator mareMediator, ServerConfigurationManager serverConfigurationManager)
	{
		_loggerFactory = loggerFactory;
		_cachedPlayerFactory = cachedPlayerFactory;
		_mareMediator = mareMediator;
		_serverConfigurationManager = serverConfigurationManager;
	}

	public Pair Create(UserFullPairDto userPairDto)
	{
		return new Pair(_loggerFactory.CreateLogger<Pair>(), userPairDto, _cachedPlayerFactory, _mareMediator, _serverConfigurationManager);
	}

	public Pair Create(UserPairDto userPairDto)
	{
		return new Pair(_loggerFactory.CreateLogger<Pair>(), new UserFullPairDto(userPairDto.User, userPairDto.IndividualPairStatus, new List<string>(), userPairDto.OwnPermissions, userPairDto.OtherPermissions), _cachedPlayerFactory, _mareMediator, _serverConfigurationManager);
	}
}
