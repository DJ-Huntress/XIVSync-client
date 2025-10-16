using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using K4os.Compression.LZ4.Legacy;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data;
using XIVSync.API.Dto.CharaData;
using XIVSync.Interop.Ipc;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Models;
using XIVSync.PlayerData.Factories;
using XIVSync.PlayerData.Handlers;
using XIVSync.PlayerData.Pairs;
using XIVSync.Services.CharaData.Models;
using XIVSync.Services.Mediator;
using XIVSync.Utils;
using XIVSync.WebAPI;

namespace XIVSync.Services;

public sealed class CharaDataManager : DisposableMediatorSubscriberBase
{
	private readonly ApiController _apiController;

	private readonly CharaDataConfigService _configService;

	private readonly DalamudUtilService _dalamudUtilService;

	private readonly CharaDataFileHandler _fileHandler;

	private readonly IpcManager _ipcManager;

	private readonly ConcurrentDictionary<string, CharaDataMetaInfoExtendedDto?> _metaInfoCache = new ConcurrentDictionary<string, CharaDataMetaInfoExtendedDto>();

	private readonly List<CharaDataMetaInfoExtendedDto> _nearbyData = new List<CharaDataMetaInfoExtendedDto>();

	private readonly CharaDataNearbyManager _nearbyManager;

	private readonly CharaDataCharacterHandler _characterHandler;

	private readonly PairManager _pairManager;

	private readonly Dictionary<string, CharaDataFullExtendedDto> _ownCharaData = new Dictionary<string, CharaDataFullExtendedDto>();

	private readonly Dictionary<string, Task> _sharedMetaInfoTimeoutTasks = new Dictionary<string, Task>();

	private readonly Dictionary<UserData, List<CharaDataMetaInfoExtendedDto>> _sharedWithYouData = new Dictionary<UserData, List<CharaDataMetaInfoExtendedDto>>();

	private readonly Dictionary<string, CharaDataExtendedUpdateDto> _updateDtos = new Dictionary<string, CharaDataExtendedUpdateDto>();

	private CancellationTokenSource _applicationCts = new CancellationTokenSource();

	private CancellationTokenSource _charaDataCreateCts = new CancellationTokenSource();

	private CancellationTokenSource _connectCts = new CancellationTokenSource();

	private CancellationTokenSource _getAllDataCts = new CancellationTokenSource();

	private CancellationTokenSource _getSharedDataCts = new CancellationTokenSource();

	private CancellationTokenSource _uploadCts = new CancellationTokenSource();

	private readonly SemaphoreSlim _distributionSemaphore = new SemaphoreSlim(1, 1);

	public Task? AttachingPoseTask { get; private set; }

	public Task? CharaUpdateTask { get; set; }

	public string DataApplicationProgress { get; private set; } = string.Empty;


	public Task? DataApplicationTask { get; private set; }

	public Task<(string Output, bool Success)>? DataCreationTask { get; private set; }

	public Task? DataGetTimeoutTask { get; private set; }

	public Task<(string Result, bool Success)>? DownloadMetaInfoTask { get; private set; }

	public Task<List<CharaDataFullExtendedDto>>? GetAllDataTask { get; private set; }

	public Task<List<CharaDataMetaInfoDto>>? GetSharedWithYouTask { get; private set; }

	public Task? GetSharedWithYouTimeoutTask { get; private set; }

	public IEnumerable<HandledCharaDataEntry> HandledCharaData => _characterHandler.HandledCharaData;

	public bool Initialized { get; private set; }

	public CharaDataMetaInfoExtendedDto? LastDownloadedMetaInfo { get; private set; }

	public Task<(MareCharaFileHeader LoadedFile, long ExpectedLength)>? LoadedMcdfHeader { get; private set; }

	public int MaxCreatableCharaData { get; private set; }

	public Task? McdfApplicationTask { get; private set; }

	public List<CharaDataMetaInfoExtendedDto> NearbyData => _nearbyData;

	public IDictionary<string, CharaDataFullExtendedDto> OwnCharaData => _ownCharaData;

	public IDictionary<UserData, List<CharaDataMetaInfoExtendedDto>> SharedWithYouData => _sharedWithYouData;

	public Task? UiBlockingComputation { get; private set; }

	public ValueProgress<string>? UploadProgress { get; private set; }

	public Task<(string Output, bool Success)>? UploadTask { get; set; }

	public bool BrioAvailable => _ipcManager.Brio.APIAvailable;

	public CharaDataManager(ILogger<CharaDataManager> logger, ApiController apiController, CharaDataFileHandler charaDataFileHandler, MareMediator mareMediator, IpcManager ipcManager, DalamudUtilService dalamudUtilService, FileDownloadManagerFactory fileDownloadManagerFactory, CharaDataConfigService charaDataConfigService, CharaDataNearbyManager charaDataNearbyManager, CharaDataCharacterHandler charaDataCharacterHandler, PairManager pairManager)
		: base(logger, mareMediator)
	{
		_apiController = apiController;
		_fileHandler = charaDataFileHandler;
		_ipcManager = ipcManager;
		_dalamudUtilService = dalamudUtilService;
		_configService = charaDataConfigService;
		_nearbyManager = charaDataNearbyManager;
		_characterHandler = charaDataCharacterHandler;
		_pairManager = pairManager;
		mareMediator.Subscribe(this, delegate(ConnectedMessage msg)
		{
			_connectCts?.Cancel();
			_connectCts?.Dispose();
			_connectCts = new CancellationTokenSource();
			_ownCharaData.Clear();
			_metaInfoCache.Clear();
			_sharedWithYouData.Clear();
			_updateDtos.Clear();
			Initialized = false;
			MaxCreatableCharaData = (string.IsNullOrEmpty(msg.Connection.User.Alias) ? msg.Connection.ServerInfo.MaxCharaData : msg.Connection.ServerInfo.MaxCharaDataVanity);
			if (_configService.Current.DownloadMcdDataOnConnection)
			{
				CancellationToken token = _connectCts.Token;
				GetAllData(token);
				GetAllSharedData(token);
			}
		});
		mareMediator.Subscribe<DisconnectedMessage>(this, delegate
		{
			_ownCharaData.Clear();
			_metaInfoCache.Clear();
			_sharedWithYouData.Clear();
			_updateDtos.Clear();
			Initialized = false;
		});
	}

	public Task ApplyCharaData(CharaDataDownloadDto dataDownloadDto, string charaName)
	{
		Task task2 = (DataApplicationTask = Task.Run(async delegate
		{
			if (!string.IsNullOrEmpty(charaName))
			{
				CharaDataMetaInfoDto metaInfo = new CharaDataMetaInfoDto(dataDownloadDto.Id, dataDownloadDto.Uploader)
				{
					CanBeDownloaded = true,
					Description = "Data from " + dataDownloadDto.Uploader.AliasOrUID + " for " + dataDownloadDto.Id,
					UpdatedDate = dataDownloadDto.UpdatedDate
				};
				await DownloadAndAplyDataAsync(charaName, dataDownloadDto, metaInfo, autoRevert: false).ConfigureAwait(continueOnCapturedContext: false);
			}
		}));
		return UiBlockingComputation = task2;
	}

	public Task ApplyCharaData(CharaDataMetaInfoDto dataMetaInfoDto, string charaName)
	{
		Task task2 = (DataApplicationTask = Task.Run(async delegate
		{
			if (!string.IsNullOrEmpty(charaName))
			{
				CharaDataDownloadDto download = await _apiController.CharaDataDownload(dataMetaInfoDto.Uploader.UID + ":" + dataMetaInfoDto.Id).ConfigureAwait(continueOnCapturedContext: false);
				if (download == null)
				{
					DataApplicationTask = null;
				}
				else
				{
					await DownloadAndAplyDataAsync(charaName, download, dataMetaInfoDto, autoRevert: false).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}));
		return UiBlockingComputation = task2;
	}

	public Task ApplyCharaDataToGposeTarget(CharaDataMetaInfoDto dataMetaInfoDto)
	{
		Task task2 = (DataApplicationTask = Task.Run(async delegate
		{
			string charaName = (await _dalamudUtilService.GetGposeTargetGameObjectAsync().ConfigureAwait(continueOnCapturedContext: false))?.Name.TextValue ?? string.Empty;
			if (!string.IsNullOrEmpty(charaName))
			{
				await ApplyCharaData(dataMetaInfoDto, charaName).ConfigureAwait(continueOnCapturedContext: false);
			}
		}));
		return UiBlockingComputation = task2;
	}

	public async Task ApplyOwnDataToGposeTarget(CharaDataFullExtendedDto dataDto)
	{
		string charaName = (await _dalamudUtilService.GetGposeTargetGameObjectAsync().ConfigureAwait(continueOnCapturedContext: false))?.Name.TextValue ?? string.Empty;
		CharaDataDownloadDto downloadDto = new CharaDataDownloadDto(dataDto.Id, dataDto.Uploader)
		{
			CustomizeData = dataDto.CustomizeData,
			Description = dataDto.Description,
			FileGamePaths = dataDto.FileGamePaths,
			GlamourerData = dataDto.GlamourerData,
			FileSwaps = dataDto.FileSwaps,
			ManipulationData = dataDto.ManipulationData,
			UpdatedDate = dataDto.UpdatedDate
		};
		CharaDataMetaInfoDto metaInfoDto = new CharaDataMetaInfoDto(dataDto.Id, dataDto.Uploader)
		{
			CanBeDownloaded = true,
			Description = dataDto.Description,
			PoseData = dataDto.PoseData,
			UpdatedDate = dataDto.UpdatedDate
		};
		CharaDataManager charaDataManager = this;
		Task uiBlockingComputation = (DataApplicationTask = DownloadAndAplyDataAsync(charaName, downloadDto, metaInfoDto, autoRevert: false));
		charaDataManager.UiBlockingComputation = uiBlockingComputation;
	}

	public Task ApplyPoseData(PoseEntry pose, string targetName)
	{
		return UiBlockingComputation = Task.Run(async delegate
		{
			bool flag = string.IsNullOrEmpty(pose.PoseData);
			if (!flag)
			{
				flag = !(await CanApplyInGpose().ConfigureAwait(continueOnCapturedContext: false)).Item1;
			}
			if (!flag)
			{
				ICharacter gposeChara = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(targetName, onlyGposeCharacters: true).ConfigureAwait(continueOnCapturedContext: false);
				if (gposeChara != null)
				{
					string poseJson = Encoding.UTF8.GetString(LZ4Wrapper.Unwrap(Convert.FromBase64String(pose.PoseData)));
					if (!string.IsNullOrEmpty(poseJson))
					{
						await _ipcManager.Brio.SetPoseAsync(gposeChara.Address, poseJson).ConfigureAwait(continueOnCapturedContext: false);
					}
				}
			}
		});
	}

	public Task ApplyPoseDataToGPoseTarget(PoseEntry pose)
	{
		return UiBlockingComputation = Task.Run(async delegate
		{
			(bool CanApply, string TargetName) apply = await CanApplyInGpose().ConfigureAwait(continueOnCapturedContext: false);
			if (apply.CanApply)
			{
				await ApplyPoseData(pose, apply.TargetName).ConfigureAwait(continueOnCapturedContext: false);
			}
		});
	}

	public Task ApplyWorldDataToTarget(PoseEntry pose, string targetName)
	{
		return UiBlockingComputation = Task.Run(async delegate
		{
			await CanApplyInGpose().ConfigureAwait(continueOnCapturedContext: false);
			bool flag = !pose.WorldData.HasValue;
			if (!flag)
			{
				flag = !(await CanApplyInGpose().ConfigureAwait(continueOnCapturedContext: false)).Item1;
			}
			if (!flag)
			{
				ICharacter gposeChara = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(targetName, onlyGposeCharacters: true).ConfigureAwait(continueOnCapturedContext: false);
				if (gposeChara != null && pose.WorldData.HasValue && pose.WorldData.HasValue)
				{
					base.Logger.LogDebug("Applying World data {data}", pose.WorldData);
					await _ipcManager.Brio.ApplyTransformAsync(gposeChara.Address, pose.WorldData.Value).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		});
	}

	public Task ApplyWorldDataToGPoseTarget(PoseEntry pose)
	{
		return UiBlockingComputation = Task.Run(async delegate
		{
			(bool CanApply, string TargetName) apply = await CanApplyInGpose().ConfigureAwait(continueOnCapturedContext: false);
			if (apply.CanApply)
			{
				await ApplyPoseData(pose, apply.TargetName).ConfigureAwait(continueOnCapturedContext: false);
			}
		});
	}

	public void AttachWorldData(PoseEntry pose, CharaDataExtendedUpdateDto updateDto)
	{
		AttachingPoseTask = Task.Run(async delegate
		{
			ICharacter playerChar = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (playerChar != null)
			{
				if (_dalamudUtilService.IsInGpose)
				{
					playerChar = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(playerChar.Name.TextValue, onlyGposeCharacters: true).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (playerChar != null)
				{
					WorldData worldData = await _ipcManager.Brio.GetTransformAsync(playerChar.Address).ConfigureAwait(continueOnCapturedContext: false);
					if (!(worldData == default(WorldData)))
					{
						base.Logger.LogTrace("Attaching World data {data}", worldData);
						worldData.LocationInfo = await _dalamudUtilService.GetMapDataAsync().ConfigureAwait(continueOnCapturedContext: false);
						base.Logger.LogTrace("World data serialized: {data}", worldData);
						pose.WorldData = worldData;
						updateDto.UpdatePoseList();
					}
				}
			}
		});
	}

	public async Task<(bool CanApply, string TargetName)> CanApplyInGpose()
	{
		IGameObject obj = await _dalamudUtilService.GetGposeTargetGameObjectAsync().ConfigureAwait(continueOnCapturedContext: false);
		_ = string.Empty;
		bool num = _dalamudUtilService.IsInGpose && obj != null && obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player;
		string targetName = ((!num) ? "Invalid Target" : obj.Name.TextValue);
		return (CanApply: num, TargetName: targetName);
	}

	public void CancelDataApplication()
	{
		_applicationCts.Cancel();
	}

	public void CancelUpload()
	{
		_uploadCts.Cancel();
	}

	public void CreateCharaDataEntry(CancellationToken cancelToken)
	{
		Task<(string, bool)> uiBlockingComputation = (DataCreationTask = Task.Run(async delegate
		{
			CharaDataFullDto result = await _apiController.CharaDataCreate().ConfigureAwait(continueOnCapturedContext: false);
			Task.Run(async delegate
			{
				_charaDataCreateCts = _charaDataCreateCts.CancelRecreate();
				using CancellationTokenSource ct = CancellationTokenSource.CreateLinkedTokenSource(_charaDataCreateCts.Token, cancelToken);
				await Task.Delay(TimeSpan.FromSeconds(10L), ct.Token).ConfigureAwait(continueOnCapturedContext: false);
				DataCreationTask = null;
			});
			if (result == null)
			{
				return ("Failed to create character data, see log for more information", false);
			}
			await AddOrUpdateDto(result).ConfigureAwait(continueOnCapturedContext: false);
			return ("Created Character Data", true);
		}));
		UiBlockingComputation = uiBlockingComputation;
	}

	public async Task DeleteCharaData(CharaDataFullExtendedDto dto)
	{
		if (await _apiController.CharaDataDelete(dto.Id).ConfigureAwait(continueOnCapturedContext: false))
		{
			_ownCharaData.Remove(dto.Id);
			_metaInfoCache.Remove<string, CharaDataMetaInfoExtendedDto>(dto.FullId, out var _);
		}
		DistributeMetaInfo();
	}

	public void DownloadMetaInfo(string importCode, bool store = true)
	{
		DownloadMetaInfoTask = Task.Run(async delegate
		{
			_ = 2;
			try
			{
				if (store)
				{
					LastDownloadedMetaInfo = null;
				}
				CharaDataMetaInfoDto metaInfo = await _apiController.CharaDataGetMetainfo(importCode).ConfigureAwait(continueOnCapturedContext: false);
				_sharedMetaInfoTimeoutTasks[importCode] = Task.Delay(TimeSpan.FromSeconds(10L));
				if (metaInfo == null)
				{
					_metaInfoCache[importCode] = null;
					return ("Failed to download meta info for this code. Check if the code is valid and you have rights to access it.", false);
				}
				await CacheData(metaInfo).ConfigureAwait(continueOnCapturedContext: false);
				if (store)
				{
					CharaDataMetaInfoExtendedDto lastDownloadedMetaInfo = await CharaDataMetaInfoExtendedDto.Create(metaInfo, _dalamudUtilService).ConfigureAwait(continueOnCapturedContext: false);
					LastDownloadedMetaInfo = lastDownloadedMetaInfo;
				}
				return ("Ok", true);
			}
			finally
			{
				if (!store)
				{
					DownloadMetaInfoTask = null;
				}
			}
		});
	}

	public async Task GetAllData(CancellationToken cancelToken)
	{
		foreach (KeyValuePair<string, CharaDataFullExtendedDto> data in _ownCharaData)
		{
			_metaInfoCache.Remove<string, CharaDataMetaInfoExtendedDto>(data.Key, out var _);
		}
		_ownCharaData.Clear();
		CharaDataManager charaDataManager = this;
		Task<List<CharaDataFullExtendedDto>> uiBlockingComputation = (GetAllDataTask = Task.Run(async delegate
		{
			_getAllDataCts = _getAllDataCts.CancelRecreate();
			List<CharaDataFullDto> source = await _apiController.CharaDataGetOwn().ConfigureAwait(continueOnCapturedContext: false);
			Initialized = true;
			if (source.Any())
			{
				DataGetTimeoutTask = Task.Run(async delegate
				{
					using CancellationTokenSource ct = CancellationTokenSource.CreateLinkedTokenSource(_getAllDataCts.Token, cancelToken);
					await Task.Delay(TimeSpan.FromMinutes(1L), ct.Token).ConfigureAwait(continueOnCapturedContext: false);
				});
			}
			return (from u in source
				orderby u.CreatedDate
				select u into k
				select new CharaDataFullExtendedDto(k)).ToList();
		}));
		charaDataManager.UiBlockingComputation = uiBlockingComputation;
		List<CharaDataFullExtendedDto> result = await GetAllDataTask.ConfigureAwait(continueOnCapturedContext: false);
		foreach (CharaDataFullExtendedDto item in result)
		{
			await AddOrUpdateDto(item).ConfigureAwait(continueOnCapturedContext: false);
		}
		foreach (string id in _updateDtos.Keys.Where((string r) => !result.Exists((CharaDataFullExtendedDto res) => string.Equals(res.Id, r, StringComparison.Ordinal))).ToList())
		{
			_updateDtos.Remove(id);
		}
		GetAllDataTask = null;
	}

	public async Task GetAllSharedData(CancellationToken token)
	{
		base.Logger.LogDebug("Getting Shared with You Data");
		CharaDataManager charaDataManager = this;
		Task<List<CharaDataMetaInfoDto>> uiBlockingComputation = (GetSharedWithYouTask = _apiController.CharaDataGetShared());
		charaDataManager.UiBlockingComputation = uiBlockingComputation;
		_sharedWithYouData.Clear();
		GetSharedWithYouTimeoutTask = Task.Run(async delegate
		{
			_getSharedDataCts = _getSharedDataCts.CancelRecreate();
			using CancellationTokenSource ct = CancellationTokenSource.CreateLinkedTokenSource(_getSharedDataCts.Token, token);
			await Task.Delay(TimeSpan.FromMinutes(1L), ct.Token).ConfigureAwait(continueOnCapturedContext: false);
			GetSharedWithYouTimeoutTask = null;
			base.Logger.LogDebug("Finished Shared with You Data Timeout");
		});
		foreach (IGrouping<UserData, CharaDataMetaInfoDto> grouping in from r in await GetSharedWithYouTask.ConfigureAwait(continueOnCapturedContext: false)
			group r by r.Uploader)
		{
			if (_pairManager.GetPairByUID(grouping.Key.UID)?.IsPaused ?? false)
			{
				continue;
			}
			List<CharaDataMetaInfoExtendedDto> newList = new List<CharaDataMetaInfoExtendedDto>();
			foreach (CharaDataMetaInfoDto item in grouping)
			{
				CharaDataMetaInfoExtendedDto extended = await CharaDataMetaInfoExtendedDto.Create(item, _dalamudUtilService).ConfigureAwait(continueOnCapturedContext: false);
				newList.Add(extended);
				CacheData(extended);
			}
			_sharedWithYouData[grouping.Key] = newList;
		}
		DistributeMetaInfo();
		base.Logger.LogDebug("Finished getting Shared with You Data");
		GetSharedWithYouTask = null;
	}

	public CharaDataExtendedUpdateDto? GetUpdateDto(string id)
	{
		if (_updateDtos.TryGetValue(id, out CharaDataExtendedUpdateDto dto))
		{
			return dto;
		}
		return null;
	}

	public bool IsInTimeout(string key)
	{
		if (!_sharedMetaInfoTimeoutTasks.TryGetValue(key, out Task task))
		{
			return false;
		}
		if (task == null)
		{
			return false;
		}
		return !task.IsCompleted;
	}

	public void LoadMcdf(string filePath)
	{
		LoadedMcdfHeader = _fileHandler.LoadCharaFileHeader(filePath);
	}

	public void McdfApplyToTarget(string charaName)
	{
		if (LoadedMcdfHeader == null || !LoadedMcdfHeader.IsCompletedSuccessfully)
		{
			return;
		}
		List<string> actuallyExtractedFiles = new List<string>();
		Task uiBlockingComputation = (McdfApplicationTask = Task.Run(async delegate
		{
			Guid applicationId = Guid.NewGuid();
			try
			{
				using GameObjectHandler tempHandler = await _characterHandler.TryCreateGameObjectHandler(charaName, gPoseOnly: true).ConfigureAwait(continueOnCapturedContext: false);
				if (tempHandler == null)
				{
					return;
				}
				IPlayerCharacter playerChar = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(continueOnCapturedContext: false);
				bool isSelf = playerChar != null && string.Equals(playerChar.Name.TextValue, tempHandler.Name, StringComparison.Ordinal);
				long expectedExtractedSize = LoadedMcdfHeader.Result.ExpectedLength;
				MareCharaFileHeader charaFile = LoadedMcdfHeader.Result.LoadedFile;
				DataApplicationProgress = "Extracting MCDF data";
				Dictionary<string, string> extractedFiles = _fileHandler.McdfExtractFiles(charaFile, expectedExtractedSize, actuallyExtractedFiles);
				foreach (KeyValuePair<string, string> entry in from k in charaFile.CharaFileData.FileSwaps
					from p in k.GamePaths
					select new KeyValuePair<string, string>(p, k.FileSwapPath))
				{
					extractedFiles[entry.Key] = entry.Value;
				}
				DataApplicationProgress = "Applying MCDF data";
				CharaDataMetaInfoExtendedDto extended = await CharaDataMetaInfoExtendedDto.Create(new CharaDataMetaInfoDto(charaFile.FilePath, new UserData(string.Empty)), _dalamudUtilService).ConfigureAwait(continueOnCapturedContext: false);
				await ApplyDataAsync(applicationId, tempHandler, isSelf, autoRevert: false, extended, extractedFiles, charaFile.CharaFileData.ManipulationData, charaFile.CharaFileData.GlamourerData, charaFile.CharaFileData.CustomizePlusData, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				base.Logger.LogWarning(ex, "Failed to extract MCDF");
				throw;
			}
			finally
			{
				foreach (string item in actuallyExtractedFiles)
				{
					File.Delete(item);
				}
			}
		}));
		UiBlockingComputation = uiBlockingComputation;
	}

	public async Task McdfApplyToGposeTarget()
	{
		(bool CanApply, string TargetName) apply = await CanApplyInGpose().ConfigureAwait(continueOnCapturedContext: false);
		if (apply.CanApply)
		{
			McdfApplyToTarget(apply.TargetName);
		}
	}

	public void SaveMareCharaFile(string description, string filePath)
	{
		UiBlockingComputation = Task.Run(async delegate
		{
			await _fileHandler.SaveCharaFileAsync(description, filePath).ConfigureAwait(continueOnCapturedContext: false);
		});
	}

	public void SetAppearanceData(string dtoId)
	{
		if (_ownCharaData.TryGetValue(dtoId, out CharaDataFullExtendedDto dto) && !(dto == null) && _updateDtos.TryGetValue(dtoId, out CharaDataExtendedUpdateDto updateDto) && !(updateDto == null))
		{
			UiBlockingComputation = Task.Run(async delegate
			{
				await _fileHandler.UpdateCharaDataAsync(updateDto).ConfigureAwait(continueOnCapturedContext: false);
			});
		}
	}

	public Task<HandledCharaDataEntry?> SpawnAndApplyData(CharaDataDownloadDto charaDataDownloadDto)
	{
		return (Task<HandledCharaDataEntry?>)(UiBlockingComputation = Task.Run(async delegate
		{
			IGameObject newActor = await _ipcManager.Brio.SpawnActorAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (newActor == null)
			{
				return (HandledCharaDataEntry)null;
			}
			await Task.Delay(TimeSpan.FromSeconds(1L)).ConfigureAwait(continueOnCapturedContext: false);
			await ApplyCharaData(charaDataDownloadDto, newActor.Name.TextValue).ConfigureAwait(continueOnCapturedContext: false);
			return _characterHandler.HandledCharaData.FirstOrDefault((HandledCharaDataEntry f) => string.Equals(f.Name, newActor.Name.TextValue, StringComparison.Ordinal));
		}));
	}

	public Task<HandledCharaDataEntry?> SpawnAndApplyData(CharaDataMetaInfoDto charaDataMetaInfoDto)
	{
		return (Task<HandledCharaDataEntry?>)(UiBlockingComputation = Task.Run(async delegate
		{
			IGameObject newActor = await _ipcManager.Brio.SpawnActorAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (newActor == null)
			{
				return (HandledCharaDataEntry)null;
			}
			await Task.Delay(TimeSpan.FromSeconds(1L)).ConfigureAwait(continueOnCapturedContext: false);
			await ApplyCharaData(charaDataMetaInfoDto, newActor.Name.TextValue).ConfigureAwait(continueOnCapturedContext: false);
			return _characterHandler.HandledCharaData.FirstOrDefault((HandledCharaDataEntry f) => string.Equals(f.Name, newActor.Name.TextValue, StringComparison.Ordinal));
		}));
	}

	private async Task<CharaDataMetaInfoExtendedDto> CacheData(CharaDataFullExtendedDto ownCharaData)
	{
		CharaDataMetaInfoExtendedDto extended = await CharaDataMetaInfoExtendedDto.Create(new CharaDataMetaInfoDto(ownCharaData.Id, ownCharaData.Uploader)
		{
			Description = ownCharaData.Description,
			UpdatedDate = ownCharaData.UpdatedDate,
			CanBeDownloaded = (!string.IsNullOrEmpty(ownCharaData.GlamourerData) && ownCharaData.OriginalFiles.Count == ownCharaData.FileGamePaths.Count),
			PoseData = ownCharaData.PoseData
		}, _dalamudUtilService, isOwnData: true).ConfigureAwait(continueOnCapturedContext: false);
		_metaInfoCache[extended.FullId] = extended;
		DistributeMetaInfo();
		return extended;
	}

	private async Task<CharaDataMetaInfoExtendedDto> CacheData(CharaDataMetaInfoDto metaInfo, bool isOwnData = false)
	{
		CharaDataMetaInfoExtendedDto extended = await CharaDataMetaInfoExtendedDto.Create(metaInfo, _dalamudUtilService, isOwnData).ConfigureAwait(continueOnCapturedContext: false);
		_metaInfoCache[extended.FullId] = extended;
		DistributeMetaInfo();
		return extended;
	}

	private void DistributeMetaInfo()
	{
		_distributionSemaphore.Wait();
		_nearbyManager.UpdateSharedData(_metaInfoCache.ToDictionary());
		_characterHandler.UpdateHandledData(_metaInfoCache.ToDictionary());
		_distributionSemaphore.Release();
	}

	private void CacheData(CharaDataMetaInfoExtendedDto charaData)
	{
		_metaInfoCache[charaData.FullId] = charaData;
	}

	public bool TryGetMetaInfo(string key, out CharaDataMetaInfoExtendedDto? metaInfo)
	{
		return _metaInfoCache.TryGetValue(key, out metaInfo);
	}

	public void UploadAllCharaData()
	{
		UiBlockingComputation = Task.Run(async delegate
		{
			foreach (CharaDataExtendedUpdateDto updateDto in _updateDtos.Values.Where((CharaDataExtendedUpdateDto u) => u.HasChanges))
			{
				CharaUpdateTask = CharaUpdateAsync(updateDto);
				await CharaUpdateTask.ConfigureAwait(continueOnCapturedContext: false);
			}
		});
	}

	public void UploadCharaData(string id)
	{
		if (_updateDtos.TryGetValue(id, out CharaDataExtendedUpdateDto updateDto) && !(updateDto == null))
		{
			Task uiBlockingComputation = (CharaUpdateTask = CharaUpdateAsync(updateDto));
			UiBlockingComputation = uiBlockingComputation;
		}
	}

	public void UploadMissingFiles(string id)
	{
		if (_ownCharaData.TryGetValue(id, out CharaDataFullExtendedDto dto) && !(dto == null))
		{
			Task<(string, bool)> uiBlockingComputation = (UploadTask = RestoreThenUpload(dto));
			UiBlockingComputation = uiBlockingComputation;
		}
	}

	private async Task<(string Output, bool Success)> RestoreThenUpload(CharaDataFullExtendedDto dto)
	{
		CharaDataFullDto newDto = await _apiController.CharaDataAttemptRestore(dto.Id).ConfigureAwait(continueOnCapturedContext: false);
		if (newDto == null)
		{
			_ownCharaData.Remove(dto.Id);
			_metaInfoCache.Remove<string, CharaDataMetaInfoExtendedDto>(dto.FullId, out var _);
			UiBlockingComputation = null;
			return (Output: "No such DTO found", Success: false);
		}
		await AddOrUpdateDto(newDto).ConfigureAwait(continueOnCapturedContext: false);
		_ownCharaData.TryGetValue(dto.Id, out CharaDataFullExtendedDto extendedDto);
		if (!extendedDto.HasMissingFiles)
		{
			UiBlockingComputation = null;
			return (Output: "Restored successfully", Success: true);
		}
		List<GamePathEntry> missingFileList = extendedDto.MissingFiles.ToList();
		(string, bool) result = await UploadFiles(missingFileList, async delegate
		{
			List<GamePathEntry> newFilePaths = dto.FileGamePaths;
			foreach (GamePathEntry missing in missingFileList)
			{
				newFilePaths.Add(missing);
			}
			CharaDataUpdateDto updateDto = new CharaDataUpdateDto(dto.Id)
			{
				FileGamePaths = newFilePaths
			};
			CharaDataFullDto res = await _apiController.CharaDataUpdate(updateDto).ConfigureAwait(continueOnCapturedContext: false);
			await AddOrUpdateDto(res).ConfigureAwait(continueOnCapturedContext: false);
		}).ConfigureAwait(continueOnCapturedContext: false);
		UiBlockingComputation = null;
		return result;
	}

	internal void ApplyDataToSelf(CharaDataFullExtendedDto dataDto)
	{
		string chara = _dalamudUtilService.GetPlayerName();
		CharaDataDownloadDto downloadDto = new CharaDataDownloadDto(dataDto.Id, dataDto.Uploader)
		{
			CustomizeData = dataDto.CustomizeData,
			Description = dataDto.Description,
			FileGamePaths = dataDto.FileGamePaths,
			GlamourerData = dataDto.GlamourerData,
			FileSwaps = dataDto.FileSwaps,
			ManipulationData = dataDto.ManipulationData,
			UpdatedDate = dataDto.UpdatedDate
		};
		CharaDataMetaInfoDto metaInfoDto = new CharaDataMetaInfoDto(dataDto.Id, dataDto.Uploader)
		{
			CanBeDownloaded = true,
			Description = dataDto.Description,
			PoseData = dataDto.PoseData,
			UpdatedDate = dataDto.UpdatedDate
		};
		Task uiBlockingComputation = (DataApplicationTask = DownloadAndAplyDataAsync(chara, downloadDto, metaInfoDto));
		UiBlockingComputation = uiBlockingComputation;
	}

	internal void AttachPoseData(PoseEntry pose, CharaDataExtendedUpdateDto updateDto)
	{
		AttachingPoseTask = Task.Run(async delegate
		{
			ICharacter playerChar = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (playerChar != null)
			{
				if (_dalamudUtilService.IsInGpose)
				{
					playerChar = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(playerChar.Name.TextValue, onlyGposeCharacters: true).ConfigureAwait(continueOnCapturedContext: false);
				}
				if (playerChar != null)
				{
					string poseData = await _ipcManager.Brio.GetPoseAsync(playerChar.Address).ConfigureAwait(continueOnCapturedContext: false);
					if (poseData != null)
					{
						byte[] compressedByteData = LZ4Wrapper.WrapHC(Encoding.UTF8.GetBytes(poseData));
						pose.PoseData = Convert.ToBase64String(compressedByteData);
						updateDto.UpdatePoseList();
					}
				}
			}
		});
	}

	internal unsafe void McdfSpawnApplyToGposeTarget()
	{
		UiBlockingComputation = Task.Run(async delegate
		{
			IGameObject newActor = await _ipcManager.Brio.SpawnActorAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (newActor != null)
			{
				await Task.Delay(TimeSpan.FromSeconds(1L)).ConfigureAwait(continueOnCapturedContext: false);
				_dalamudUtilService.GposeTarget = (GameObject*)newActor.Address;
				await McdfApplyToGposeTarget().ConfigureAwait(continueOnCapturedContext: false);
			}
		});
	}

	internal void ApplyFullPoseDataToTarget(PoseEntry value, string targetName)
	{
		UiBlockingComputation = Task.Run(async delegate
		{
			await ApplyPoseData(value, targetName).ConfigureAwait(continueOnCapturedContext: false);
			await ApplyWorldDataToTarget(value, targetName).ConfigureAwait(continueOnCapturedContext: false);
		});
	}

	internal void ApplyFullPoseDataToGposeTarget(PoseEntry value)
	{
		UiBlockingComputation = Task.Run(async delegate
		{
			(bool CanApply, string TargetName) apply = await CanApplyInGpose().ConfigureAwait(continueOnCapturedContext: false);
			if (apply.CanApply)
			{
				await ApplyPoseData(value, apply.TargetName).ConfigureAwait(continueOnCapturedContext: false);
				await ApplyWorldDataToTarget(value, apply.TargetName).ConfigureAwait(continueOnCapturedContext: false);
			}
		});
	}

	internal void SpawnAndApplyWorldTransform(CharaDataMetaInfoDto metaInfo, PoseEntry value)
	{
		UiBlockingComputation = Task.Run(async delegate
		{
			HandledCharaDataEntry actor = await SpawnAndApplyData(metaInfo).ConfigureAwait(continueOnCapturedContext: false);
			if (!(actor == null))
			{
				await ApplyPoseData(value, actor.Name).ConfigureAwait(continueOnCapturedContext: false);
				await ApplyWorldDataToTarget(value, actor.Name).ConfigureAwait(continueOnCapturedContext: false);
			}
		});
	}

	internal unsafe void TargetGposeActor(HandledCharaDataEntry actor)
	{
		ICharacter gposeActor = _dalamudUtilService.GetGposeCharacterFromObjectTableByName(actor.Name, onlyGposeCharacters: true);
		if (gposeActor != null)
		{
			_dalamudUtilService.GposeTarget = (GameObject*)gposeActor.Address;
		}
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (disposing)
		{
			_getAllDataCts?.Cancel();
			_getAllDataCts?.Dispose();
			_getSharedDataCts?.Cancel();
			_getSharedDataCts?.Dispose();
			_charaDataCreateCts?.Cancel();
			_charaDataCreateCts?.Dispose();
			_uploadCts?.Cancel();
			_uploadCts?.Dispose();
			_applicationCts.Cancel();
			_applicationCts.Dispose();
			_connectCts?.Cancel();
			_connectCts?.Dispose();
		}
	}

	private async Task AddOrUpdateDto(CharaDataFullDto? dto)
	{
		if (!(dto == null))
		{
			_ownCharaData[dto.Id] = new CharaDataFullExtendedDto(dto);
			_updateDtos[dto.Id] = new CharaDataExtendedUpdateDto(new CharaDataUpdateDto(dto.Id), _ownCharaData[dto.Id]);
			await CacheData(_ownCharaData[dto.Id]).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private async Task ApplyDataAsync(Guid applicationId, GameObjectHandler tempHandler, bool isSelf, bool autoRevert, CharaDataMetaInfoExtendedDto metaInfo, Dictionary<string, string> modPaths, string? manipData, string? glamourerData, string? customizeData, CancellationToken token)
	{
		Guid? cPlusId = null;
		try
		{
			DataApplicationProgress = "Reverting previous Application";
			base.Logger.LogTrace("[{appId}] Reverting chara {chara}", applicationId, tempHandler.Name);
			if (await _characterHandler.RevertHandledChara(tempHandler.Name).ConfigureAwait(continueOnCapturedContext: false))
			{
				await Task.Delay(TimeSpan.FromSeconds(3L)).ConfigureAwait(continueOnCapturedContext: false);
			}
			base.Logger.LogTrace("[{appId}] Applying data in Penumbra", applicationId);
			DataApplicationProgress = "Applying Penumbra information";
			Guid penumbraCollection = await _ipcManager.Penumbra.CreateTemporaryCollectionAsync(base.Logger, metaInfo.Uploader.UID + metaInfo.Id).ConfigureAwait(continueOnCapturedContext: false);
			ushort idx = (await _dalamudUtilService.RunOnFrameworkThread(() => tempHandler.GetGameObject()?.ObjectIndex, "ApplyDataAsync", "C:\\Users\\Owner\\sync_client2\\XIVSync\\Services\\CharaData\\CharaDataManager.cs", 838).ConfigureAwait(continueOnCapturedContext: false)).GetValueOrDefault();
			await _ipcManager.Penumbra.AssignTemporaryCollectionAsync(base.Logger, penumbraCollection, idx).ConfigureAwait(continueOnCapturedContext: false);
			await _ipcManager.Penumbra.SetTemporaryModsAsync(base.Logger, applicationId, penumbraCollection, modPaths).ConfigureAwait(continueOnCapturedContext: false);
			await _ipcManager.Penumbra.SetManipulationDataAsync(base.Logger, applicationId, penumbraCollection, manipData ?? string.Empty).ConfigureAwait(continueOnCapturedContext: false);
			base.Logger.LogTrace("[{appId}] Applying Glamourer data and Redrawing", applicationId);
			DataApplicationProgress = "Applying Glamourer and redrawing Character";
			await _ipcManager.Glamourer.ApplyAllAsync(base.Logger, tempHandler, glamourerData, applicationId, token).ConfigureAwait(continueOnCapturedContext: false);
			await _ipcManager.Penumbra.RedrawAsync(base.Logger, tempHandler, applicationId, token).ConfigureAwait(continueOnCapturedContext: false);
			await _dalamudUtilService.WaitWhileCharacterIsDrawing(base.Logger, tempHandler, applicationId, 5000, token).ConfigureAwait(continueOnCapturedContext: false);
			base.Logger.LogTrace("[{appId}] Removing collection", applicationId);
			await _ipcManager.Penumbra.RemoveTemporaryCollectionAsync(base.Logger, applicationId, penumbraCollection).ConfigureAwait(continueOnCapturedContext: false);
			DataApplicationProgress = "Applying Customize+ data";
			base.Logger.LogTrace("[{appId}] Appplying C+ data", applicationId);
			cPlusId = (string.IsNullOrEmpty(customizeData) ? (await _ipcManager.CustomizePlus.SetBodyScaleAsync(tempHandler.Address, Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))).ConfigureAwait(continueOnCapturedContext: false)) : (await _ipcManager.CustomizePlus.SetBodyScaleAsync(tempHandler.Address, customizeData).ConfigureAwait(continueOnCapturedContext: false)));
			if (autoRevert)
			{
				base.Logger.LogTrace("[{appId}] Starting wait for auto revert", applicationId);
				for (int i = 15; i > 0; i--)
				{
					DataApplicationProgress = $"All data applied. Reverting automatically in {i} seconds.";
					await Task.Delay(TimeSpan.FromSeconds(1L), token).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			else
			{
				base.Logger.LogTrace("[{appId}] Adding {name} to handled objects", applicationId, tempHandler.Name);
				_characterHandler.AddHandledChara(new HandledCharaDataEntry(tempHandler.Name, isSelf, cPlusId, metaInfo));
			}
		}
		finally
		{
			if (token.IsCancellationRequested)
			{
				DataApplicationProgress = "Application aborted. Reverting Character...";
			}
			else if (autoRevert)
			{
				DataApplicationProgress = "Application finished. Reverting Character...";
			}
			if (autoRevert)
			{
				await _characterHandler.RevertChara(tempHandler.Name, cPlusId).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (!_dalamudUtilService.IsInGpose)
			{
				base.Mediator.Publish(new HaltCharaDataCreation(Resume: true));
			}
			if (metaInfo != null && _configService.Current.FavoriteCodes.TryGetValue(metaInfo.Uploader.UID + ":" + metaInfo.Id, out CharaDataFavorite favorite) && favorite != null)
			{
				favorite.LastDownloaded = DateTime.UtcNow;
				_configService.Save();
			}
			DataApplicationTask = null;
			DataApplicationProgress = string.Empty;
		}
	}

	private async Task CharaUpdateAsync(CharaDataExtendedUpdateDto updateDto)
	{
		base.Logger.LogDebug("Uploading Chara Data to Server");
		CharaDataUpdateDto baseUpdateDto = updateDto.BaseDto;
		if (baseUpdateDto.FileGamePaths != null)
		{
			base.Logger.LogDebug("Detected file path changes, starting file upload");
			UploadTask = UploadFiles(baseUpdateDto.FileGamePaths);
			if (!(await UploadTask.ConfigureAwait(continueOnCapturedContext: false)).Item2)
			{
				return;
			}
		}
		base.Logger.LogDebug("Pushing update dto to server: {data}", baseUpdateDto);
		await AddOrUpdateDto(await _apiController.CharaDataUpdate(baseUpdateDto).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task DownloadAndAplyDataAsync(string charaName, CharaDataDownloadDto charaDataDownloadDto, CharaDataMetaInfoDto metaInfo, bool autoRevert = true)
	{
		_applicationCts = _applicationCts.CancelRecreate();
		CancellationToken token = _applicationCts.Token;
		ICharacter chara = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(charaName, _dalamudUtilService.IsInGpose).ConfigureAwait(continueOnCapturedContext: false);
		if (chara == null)
		{
			return;
		}
		Guid applicationId = Guid.NewGuid();
		IPlayerCharacter playerChar = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(continueOnCapturedContext: false);
		bool isSelf = playerChar != null && string.Equals(playerChar.Name.TextValue, chara.Name.TextValue, StringComparison.Ordinal);
		DataApplicationProgress = "Checking local files";
		base.Logger.LogTrace("[{appId}] Computing local missing files", applicationId);
		_fileHandler.ComputeMissingFiles(charaDataDownloadDto, out Dictionary<string, string> modPaths, out List<FileReplacementData> missingFiles);
		base.Logger.LogTrace("[{appId}] Computing local missing files", applicationId);
		using GameObjectHandler tempHandler = await _characterHandler.TryCreateGameObjectHandler(chara.ObjectIndex).ConfigureAwait(continueOnCapturedContext: false);
		if (tempHandler == null)
		{
			return;
		}
		if (missingFiles.Any())
		{
			try
			{
				DataApplicationProgress = "Downloading Missing Files. Please be patient.";
				await _fileHandler.DownloadFilesAsync(tempHandler, missingFiles, modPaths, token).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (FileNotFoundException)
			{
				DataApplicationProgress = "Failed to download one or more files. Aborting.";
				DataApplicationTask = null;
				return;
			}
			catch (OperationCanceledException)
			{
				DataApplicationProgress = "Application aborted.";
				DataApplicationTask = null;
				return;
			}
		}
		if (!_dalamudUtilService.IsInGpose)
		{
			base.Mediator.Publish(new HaltCharaDataCreation());
		}
		await ApplyDataAsync(applicationId, tempHandler, isSelf, autoRevert, await CacheData(metaInfo).ConfigureAwait(continueOnCapturedContext: false), modPaths, charaDataDownloadDto.ManipulationData, charaDataDownloadDto.GlamourerData, charaDataDownloadDto.CustomizeData, token).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<(string Result, bool Success)> UploadFiles(List<GamePathEntry> missingFileList, Func<Task>? postUpload = null)
	{
		UploadProgress = new ValueProgress<string>();
		try
		{
			_uploadCts = _uploadCts.CancelRecreate();
			List<string> missingFiles = await _fileHandler.UploadFiles(missingFileList.Select((GamePathEntry k) => k.HashOrFileSwap).ToList(), UploadProgress, _uploadCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (missingFiles.Any())
			{
				base.Logger.LogInformation("Failed to upload {files}", string.Join(", ", missingFiles));
				return (Result: $"Upload failed: {missingFiles.Count} missing or forbidden to upload local files.", Success: false);
			}
			if (postUpload != null)
			{
				await postUpload().ConfigureAwait(continueOnCapturedContext: false);
			}
			return (Result: "Upload sucessful", Success: true);
		}
		catch (Exception ex)
		{
			base.Logger.LogWarning(ex, "Error during upload");
			if (ex is OperationCanceledException)
			{
				return (Result: "Upload Cancelled", Success: false);
			}
			return (Result: "Error in upload, see log for more details", Success: false);
		}
		finally
		{
			UiBlockingComputation = null;
		}
	}

	public void RevertChara(HandledCharaDataEntry? handled)
	{
		UiBlockingComputation = _characterHandler.RevertHandledChara(handled);
	}

	internal void RemoveChara(string handledActor)
	{
		if (string.IsNullOrEmpty(handledActor))
		{
			return;
		}
		UiBlockingComputation = Task.Run(async delegate
		{
			await _characterHandler.RevertHandledChara(handledActor).ConfigureAwait(continueOnCapturedContext: false);
			ICharacter gposeChara = await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(handledActor, onlyGposeCharacters: true).ConfigureAwait(continueOnCapturedContext: false);
			if (gposeChara != null)
			{
				await _ipcManager.Brio.DespawnActorAsync(gposeChara.Address).ConfigureAwait(continueOnCapturedContext: false);
			}
		});
	}
}
