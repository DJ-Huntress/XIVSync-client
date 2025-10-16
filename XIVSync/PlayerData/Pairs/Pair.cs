using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Data.Enum;
using XIVSync.API.Data.Extensions;
using XIVSync.API.Dto.User;
using XIVSync.PlayerData.Factories;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.Utils;

namespace XIVSync.PlayerData.Pairs;

public class Pair : IDisposable
{
	private readonly PairHandlerFactory _cachedPlayerFactory;

	private readonly SemaphoreSlim _creationSemaphore = new SemaphoreSlim(1);

	private readonly ILogger<Pair> _logger;

	private readonly MareMediator _mediator;

	private readonly ServerConfigurationManager _serverConfigurationManager;

	private CancellationTokenSource _applicationCts = new CancellationTokenSource();

	private OnlineUserIdentDto? _onlineUserIdentDto;

	public bool HasCachedPlayer
	{
		get
		{
			if (CachedPlayer != null && !string.IsNullOrEmpty(CachedPlayer.PlayerName))
			{
				return _onlineUserIdentDto != null;
			}
			return false;
		}
	}

	public IndividualPairStatus IndividualPairStatus => UserPair.IndividualPairStatus;

	public bool IsDirectlyPaired => IndividualPairStatus != IndividualPairStatus.None;

	public bool IsOneSidedPair => IndividualPairStatus == IndividualPairStatus.OneSided;

	public bool IsOnline => CachedPlayer != null;

	public bool IsPaired
	{
		get
		{
			if (IndividualPairStatus != IndividualPairStatus.Bidirectional)
			{
				return UserPair.Groups.Any();
			}
			return true;
		}
	}

	public bool IsPaused => UserPair.OwnPermissions.IsPaused();

	public bool IsVisible => CachedPlayer?.IsVisible ?? false;

	public CharacterData? LastReceivedCharacterData { get; set; }

	public string? PlayerName => CachedPlayer?.PlayerName ?? string.Empty;

	public long LastAppliedDataBytes => CachedPlayer?.LastAppliedDataBytes ?? (-1);

	public long LastAppliedDataTris { get; set; } = -1L;


	public long LastAppliedApproximateVRAMBytes { get; set; } = -1L;


	public string Ident => _onlineUserIdentDto?.Ident ?? string.Empty;

	public UserData UserData => UserPair.User;

	public UserFullPairDto UserPair { get; set; }

	private PairHandler? CachedPlayer { get; set; }

	public Pair(ILogger<Pair> logger, UserFullPairDto userPair, PairHandlerFactory cachedPlayerFactory, MareMediator mediator, ServerConfigurationManager serverConfigurationManager)
	{
		_logger = logger;
		UserPair = userPair;
		_cachedPlayerFactory = cachedPlayerFactory;
		_mediator = mediator;
		_serverConfigurationManager = serverConfigurationManager;
	}

	public void AddContextMenu(IMenuOpenedArgs args)
	{
		if (CachedPlayer != null && args.Target is MenuTargetDefault target && target.TargetObjectId == CachedPlayer.PlayerCharacterId && !IsPaused)
		{
			SeStringBuilder seStringBuilder5 = new SeStringBuilder();
			SeStringBuilder seStringBuilder2 = new SeStringBuilder();
			SeStringBuilder seStringBuilder3 = new SeStringBuilder();
			SeStringBuilder seStringBuilder4 = new SeStringBuilder();
			SeString openProfileSeString = seStringBuilder5.AddText("Open Profile").Build();
			SeString reapplyDataSeString = seStringBuilder2.AddText("Reapply last data").Build();
			SeString cyclePauseState = seStringBuilder3.AddText("Cycle pause state").Build();
			SeString changePermissions = seStringBuilder4.AddText("Change Permissions").Build();
			args.AddMenuItem(new MenuItem
			{
				Name = openProfileSeString,
				OnClicked = delegate
				{
					_mediator.Publish(new ProfileOpenStandaloneMessage(this));
				},
				UseDefaultPrefix = false,
				PrefixChar = 'M',
				PrefixColor = 526
			});
			args.AddMenuItem(new MenuItem
			{
				Name = reapplyDataSeString,
				OnClicked = delegate
				{
					ApplyLastReceivedData(forced: true);
				},
				UseDefaultPrefix = false,
				PrefixChar = 'M',
				PrefixColor = 526
			});
			args.AddMenuItem(new MenuItem
			{
				Name = changePermissions,
				OnClicked = delegate
				{
					_mediator.Publish(new OpenPermissionWindow(this));
				},
				UseDefaultPrefix = false,
				PrefixChar = 'M',
				PrefixColor = 526
			});
			args.AddMenuItem(new MenuItem
			{
				Name = cyclePauseState,
				OnClicked = delegate
				{
					_mediator.Publish(new CyclePauseMessage(UserData));
				},
				UseDefaultPrefix = false,
				PrefixChar = 'M',
				PrefixColor = 526
			});
		}
	}

	public void ApplyData(OnlineUserCharaDataDto data)
	{
		_applicationCts = _applicationCts.CancelRecreate();
		LastReceivedCharacterData = data.CharaData;
		if (CachedPlayer == null)
		{
			_logger.LogDebug("Received Data for {uid} but CachedPlayer does not exist, waiting", data.User.UID);
			Task.Run(async delegate
			{
				using CancellationTokenSource timeoutCts = new CancellationTokenSource();
				timeoutCts.CancelAfter(TimeSpan.FromSeconds(120L));
				CancellationToken appToken = _applicationCts.Token;
				using CancellationTokenSource combined = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, appToken);
				while (CachedPlayer == null && !combined.Token.IsCancellationRequested)
				{
					await Task.Delay(250, combined.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (!combined.IsCancellationRequested)
				{
					_logger.LogDebug("Applying delayed data for {uid}", data.User.UID);
					ApplyLastReceivedData();
				}
			});
		}
		else
		{
			ApplyLastReceivedData();
		}
	}

	public void ApplyLastReceivedData(bool forced = false)
	{
		if (CachedPlayer != null && LastReceivedCharacterData != null)
		{
			CachedPlayer.ApplyCharacterData(Guid.NewGuid(), RemoveNotSyncedFiles(LastReceivedCharacterData.DeepClone()), forced);
		}
	}

	public void CreateCachedPlayer(OnlineUserIdentDto? dto = null)
	{
		try
		{
			_creationSemaphore.Wait();
			if (CachedPlayer != null)
			{
				return;
			}
			if (dto == null && _onlineUserIdentDto == null)
			{
				CachedPlayer?.Dispose();
				CachedPlayer = null;
				return;
			}
			if (dto != null)
			{
				_onlineUserIdentDto = dto;
			}
			CachedPlayer?.Dispose();
			CachedPlayer = _cachedPlayerFactory.Create(this);
		}
		finally
		{
			_creationSemaphore.Release();
		}
	}

	public string? GetNote()
	{
		return _serverConfigurationManager.GetNoteForUid(UserData.UID);
	}

	public string GetPlayerNameHash()
	{
		return CachedPlayer?.PlayerNameHash ?? string.Empty;
	}

	public bool HasAnyConnection()
	{
		if (!UserPair.Groups.Any())
		{
			return UserPair.IndividualPairStatus != IndividualPairStatus.None;
		}
		return true;
	}

	public void MarkOffline(bool wait = true)
	{
		try
		{
			if (wait)
			{
				_creationSemaphore.Wait();
			}
			LastReceivedCharacterData = null;
            PairHandler? cachedPlayer = CachedPlayer;
            CachedPlayer = null;
			cachedPlayer?.Dispose();
			_onlineUserIdentDto = null;
		}
		finally
		{
			if (wait)
			{
				_creationSemaphore.Release();
			}
		}
	}

	public void SetNote(string note)
	{
		_serverConfigurationManager.SetNoteForUid(UserData.UID, note);
	}

	internal void SetIsUploading()
	{
		CachedPlayer?.SetUploading();
	}

	private CharacterData? RemoveNotSyncedFiles(CharacterData? data)
	{
		_logger.LogTrace("Removing not synced files");
		if (data == null)
		{
			_logger.LogTrace("Nothing to remove");
			return data;
		}
		bool disableIndividualAnimations = UserPair.OtherPermissions.IsDisableAnimations() || UserPair.OwnPermissions.IsDisableAnimations();
		bool disableIndividualVFX = UserPair.OtherPermissions.IsDisableVFX() || UserPair.OwnPermissions.IsDisableVFX();
		bool disableIndividualSounds = UserPair.OtherPermissions.IsDisableSounds() || UserPair.OwnPermissions.IsDisableSounds();
		_logger.LogTrace("Disable: Sounds: {disableIndividualSounds}, Anims: {disableIndividualAnims}; VFX: {disableGroupSounds}", disableIndividualSounds, disableIndividualAnimations, disableIndividualVFX);
		if (disableIndividualAnimations || disableIndividualSounds || disableIndividualVFX)
		{
			_logger.LogTrace("Data cleaned up: Animations disabled: {disableAnimations}, Sounds disabled: {disableSounds}, VFX disabled: {disableVFX}", disableIndividualAnimations, disableIndividualSounds, disableIndividualVFX);
			foreach (ObjectKind objectKind in data.FileReplacements.Select<KeyValuePair<ObjectKind, List<FileReplacementData>>, ObjectKind>((KeyValuePair<ObjectKind, List<FileReplacementData>> k) => k.Key))
			{
				if (disableIndividualSounds)
				{
					data.FileReplacements[objectKind] = data.FileReplacements[objectKind].Where((FileReplacementData f) => !f.GamePaths.Any((string p) => p.EndsWith("scd", StringComparison.OrdinalIgnoreCase))).ToList();
				}
				if (disableIndividualAnimations)
				{
					data.FileReplacements[objectKind] = data.FileReplacements[objectKind].Where((FileReplacementData f) => !f.GamePaths.Any((string p) => p.EndsWith("tmb", StringComparison.OrdinalIgnoreCase) || p.EndsWith("pap", StringComparison.OrdinalIgnoreCase))).ToList();
				}
				if (!disableIndividualVFX)
				{
					continue;
				}
				data.FileReplacements[objectKind] = data.FileReplacements[objectKind].Where((FileReplacementData f) => !f.GamePaths.Any((string p) => p.EndsWith("atex", StringComparison.OrdinalIgnoreCase) || p.EndsWith("avfx", StringComparison.OrdinalIgnoreCase))).ToList();
			}
		}
		return data;
	}

	public void Dispose()
	{
		_applicationCts?.CancelDispose();
		_creationSemaphore?.Dispose();
		CachedPlayer?.Dispose();
	}
}
