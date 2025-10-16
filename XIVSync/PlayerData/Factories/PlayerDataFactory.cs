using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Enum;
using XIVSync.FileCache;
using XIVSync.Interop.Ipc;
using XIVSync.MareConfiguration.Models;
using XIVSync.PlayerData.Data;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Utils;

namespace XIVSync.PlayerData.Factories;

public class PlayerDataFactory
{
	private readonly DalamudUtilService _dalamudUtil;

	private readonly FileCacheManager _fileCacheManager;

	private readonly IpcManager _ipcManager;

	private readonly ILogger<PlayerDataFactory> _logger;

	private readonly PerformanceCollectorService _performanceCollector;

	private readonly XivDataAnalyzer _modelAnalyzer;

	private readonly MareMediator _mareMediator;

	private readonly TransientResourceManager _transientResourceManager;

	public PlayerDataFactory(ILogger<PlayerDataFactory> logger, DalamudUtilService dalamudUtil, IpcManager ipcManager, TransientResourceManager transientResourceManager, FileCacheManager fileReplacementFactory, PerformanceCollectorService performanceCollector, XivDataAnalyzer modelAnalyzer, MareMediator mareMediator)
	{
		_logger = logger;
		_dalamudUtil = dalamudUtil;
		_ipcManager = ipcManager;
		_transientResourceManager = transientResourceManager;
		_fileCacheManager = fileReplacementFactory;
		_performanceCollector = performanceCollector;
		_modelAnalyzer = modelAnalyzer;
		_mareMediator = mareMediator;
		_logger.LogTrace("Creating {this}", "PlayerDataFactory");
	}

	public async Task<CharacterDataFragment?> BuildCharacterData(GameObjectHandler playerRelatedObject, CancellationToken token)
	{
		if (!_ipcManager.Initialized)
		{
			throw new InvalidOperationException("Penumbra or Glamourer is not connected");
		}
		if (playerRelatedObject == null)
		{
			return null;
		}
		bool pointerIsZero = true;
		try
		{
			pointerIsZero = playerRelatedObject.Address == IntPtr.Zero;
			try
			{
				pointerIsZero = await CheckForNullDrawObject(playerRelatedObject.Address).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
				pointerIsZero = true;
				_logger.LogDebug("NullRef for {object}", playerRelatedObject);
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not create data for {object}", playerRelatedObject);
		}
		if (pointerIsZero)
		{
			_logger.LogTrace("Pointer was zero for {objectKind}", playerRelatedObject.ObjectKind);
			return null;
		}
		try
		{
			PerformanceCollectorService performanceCollector = _performanceCollector;
			PlayerDataFactory sender = this;
			MareInterpolatedStringHandler counterName = new MareInterpolatedStringHandler(20, 1);
			counterName.AppendLiteral("CreateCharacterData>");
			counterName.AppendFormatted(playerRelatedObject.ObjectKind);
			return await performanceCollector.LogPerformance(sender, counterName, async () => await CreateCharacterData(playerRelatedObject, token).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: true);
		}
		catch (OperationCanceledException)
		{
			_logger.LogDebug("Cancelled creating Character data for {object}", playerRelatedObject);
			throw;
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Failed to create {object} data", playerRelatedObject);
		}
		return null;
	}

	private async Task<bool> CheckForNullDrawObject(nint playerPointer)
	{
		return await _dalamudUtil.RunOnFrameworkThread(() => CheckForNullDrawObjectUnsafe(playerPointer), "CheckForNullDrawObject", "C:\\Users\\Owner\\sync_client2\\XIVSync\\PlayerData\\Factories\\PlayerDataFactory.cs", 97).ConfigureAwait(continueOnCapturedContext: false);
	}

	private unsafe bool CheckForNullDrawObjectUnsafe(nint playerPointer)
	{
		return ((Character*)playerPointer)->GameObject.DrawObject == null;
	}

	private async Task<CharacterDataFragment> CreateCharacterData(GameObjectHandler playerRelatedObject, CancellationToken ct)
	{
		ObjectKind objectKind = playerRelatedObject.ObjectKind;
		CharacterDataFragment fragment = ((objectKind == ObjectKind.Player) ? new CharacterDataFragmentPlayer() : new CharacterDataFragmentPlayer());
		_logger.LogDebug("Building character data for {obj}", playerRelatedObject);
		await _dalamudUtil.WaitWhileCharacterIsDrawing(_logger, playerRelatedObject, Guid.NewGuid(), 30000, ct).ConfigureAwait(continueOnCapturedContext: false);
		int totalWaitTime = 10000;
		while (true)
		{
			DalamudUtilService dalamudUtil = _dalamudUtil;
			if (await dalamudUtil.IsObjectPresentAsync(await _dalamudUtil.CreateGameObjectAsync(playerRelatedObject.Address).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false) || totalWaitTime <= 0)
			{
				break;
			}
			_logger.LogTrace("Character is null but it shouldn't be, waiting");
			await Task.Delay(50, ct).ConfigureAwait(continueOnCapturedContext: false);
			totalWaitTime -= 50;
		}
		ct.ThrowIfCancellationRequested();
		Dictionary<string, List<ushort>> dictionary = ((objectKind == ObjectKind.Player) ? (await _dalamudUtil.RunOnFrameworkThread(() => _modelAnalyzer.GetSkeletonBoneIndices(playerRelatedObject), "CreateCharacterData", "C:\\Users\\Owner\\sync_client2\\XIVSync\\PlayerData\\Factories\\PlayerDataFactory.cs", 127).ConfigureAwait(continueOnCapturedContext: false)) : null);
		Dictionary<string, List<ushort>> boneIndices = dictionary;
		DateTime start = DateTime.UtcNow;
		Dictionary<string, HashSet<string>> resolvedPaths = await _ipcManager.Penumbra.GetCharacterData(_logger, playerRelatedObject).ConfigureAwait(continueOnCapturedContext: false);
		if (resolvedPaths == null)
		{
			throw new InvalidOperationException("Penumbra returned null data");
		}
		ct.ThrowIfCancellationRequested();
		fragment.FileReplacements = new HashSet<FileReplacement>(resolvedPaths.Select((KeyValuePair<string, HashSet<string>> c) => new FileReplacement(c.Value.ToArray(), c.Key)), FileReplacementComparer.Instance).Where((FileReplacement p) => p.HasFileReplacement).ToHashSet();
		fragment.FileReplacements.RemoveWhere((FileReplacement c) => c.GamePaths.Any((string g) => !CacheMonitor.AllowedFileExtensions.Any((string e) => g.EndsWith(e, StringComparison.OrdinalIgnoreCase))));
		ct.ThrowIfCancellationRequested();
		_logger.LogDebug("== Static Replacements ==");
		foreach (FileReplacement replacement in fragment.FileReplacements.Where((FileReplacement i) => i.HasFileReplacement).OrderBy<FileReplacement, string>((FileReplacement i) => i.GamePaths.First(), StringComparer.OrdinalIgnoreCase))
		{
			_logger.LogDebug("=> {repl}", replacement);
			ct.ThrowIfCancellationRequested();
		}
		await _transientResourceManager.WaitForRecording(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (objectKind == ObjectKind.Pet)
		{
			foreach (string item in fragment.FileReplacements.Where((FileReplacement i) => i.HasFileReplacement).SelectMany((FileReplacement p) => p.GamePaths))
			{
				if (_transientResourceManager.AddTransientResource(objectKind, item))
				{
					_logger.LogDebug("Marking static {item} for Pet as transient", item);
				}
			}
			_logger.LogTrace("Clearing {count} Static Replacements for Pet", fragment.FileReplacements.Count);
			fragment.FileReplacements.Clear();
		}
		ct.ThrowIfCancellationRequested();
		_logger.LogDebug("Handling transient update for {obj}", playerRelatedObject);
		_transientResourceManager.ClearTransientPaths(objectKind, fragment.FileReplacements.SelectMany((FileReplacement c) => c.GamePaths).ToList());
		HashSet<string> transientPaths = ManageSemiTransientData(objectKind);
		IReadOnlyDictionary<string, string[]> source = await GetFileReplacementsFromPaths(transientPaths, new HashSet<string>(StringComparer.Ordinal)).ConfigureAwait(continueOnCapturedContext: false);
		_logger.LogDebug("== Transient Replacements ==");
		foreach (FileReplacement replacement in source.Select((KeyValuePair<string, string[]> c) => new FileReplacement(c.Value.ToArray(), c.Key)).OrderBy<FileReplacement, string>((FileReplacement f) => f.ResolvedPath, StringComparer.Ordinal))
		{
			_logger.LogDebug("=> {repl}", replacement);
			fragment.FileReplacements.Add(replacement);
		}
		_transientResourceManager.CleanUpSemiTransientResources(objectKind, fragment.FileReplacements.ToList());
		ct.ThrowIfCancellationRequested();
		fragment.FileReplacements = new HashSet<FileReplacement>(fragment.FileReplacements.Where((FileReplacement v) => v.HasFileReplacement).OrderBy<FileReplacement, string>((FileReplacement v) => v.ResolvedPath, StringComparer.Ordinal), FileReplacementComparer.Instance);
		Task<string> getHeelsOffset = _ipcManager.Heels.GetOffsetAsync();
		Task<string> characterCustomizationAsync = _ipcManager.Glamourer.GetCharacterCustomizationAsync(playerRelatedObject.Address);
		Task<string?> getCustomizeData = _ipcManager.CustomizePlus.GetScaleAsync(playerRelatedObject.Address);
		Task<string> getHonorificTitle = _ipcManager.Honorific.GetTitle();
		CharacterDataFragment characterDataFragment = fragment;
		characterDataFragment.GlamourerString = await characterCustomizationAsync.ConfigureAwait(continueOnCapturedContext: false);
		_logger.LogDebug("Glamourer is now: {data}", fragment.GlamourerString);
		fragment.CustomizePlusScale = (await getCustomizeData.ConfigureAwait(continueOnCapturedContext: false)) ?? string.Empty;
		_logger.LogDebug("Customize is now: {data}", fragment.CustomizePlusScale);
		if (objectKind == ObjectKind.Player)
		{
			CharacterDataFragmentPlayer playerFragment = fragment as CharacterDataFragmentPlayer;
			playerFragment.ManipulationString = _ipcManager.Penumbra.GetMetaManipulations();
			CharacterDataFragmentPlayer characterDataFragmentPlayer = playerFragment;
			characterDataFragmentPlayer.HonorificData = await getHonorificTitle.ConfigureAwait(continueOnCapturedContext: false);
			_logger.LogDebug("Honorific is now: {data}", playerFragment.HonorificData);
			characterDataFragmentPlayer = playerFragment;
			characterDataFragmentPlayer.HeelsData = await getHeelsOffset.ConfigureAwait(continueOnCapturedContext: false);
			_logger.LogDebug("Heels is now: {heels}", playerFragment.HeelsData);
			characterDataFragmentPlayer = playerFragment;
			characterDataFragmentPlayer.MoodlesData = (await _ipcManager.Moodles.GetStatusAsync(playerRelatedObject.Address).ConfigureAwait(continueOnCapturedContext: false)) ?? string.Empty;
			_logger.LogDebug("Moodles is now: {moodles}", playerFragment.MoodlesData);
			playerFragment.PetNamesData = _ipcManager.PetNames.GetLocalNames();
			_logger.LogDebug("Pet Nicknames is now: {petnames}", playerFragment.PetNamesData);
		}
		ct.ThrowIfCancellationRequested();
		FileReplacement[] toCompute = fragment.FileReplacements.Where((FileReplacement f) => !f.IsFileSwap).ToArray();
		_logger.LogDebug("Getting Hashes for {amount} Files", toCompute.Length);
		Dictionary<string, FileCacheEntity> computedPaths = _fileCacheManager.GetFileCachesByPaths(toCompute.Select((FileReplacement c) => c.ResolvedPath).ToArray());
		FileReplacement[] array = toCompute;
		foreach (FileReplacement file in array)
		{
			ct.ThrowIfCancellationRequested();
			file.Hash = computedPaths[file.ResolvedPath]?.Hash ?? string.Empty;
		}
		int removed = fragment.FileReplacements.RemoveWhere((FileReplacement f) => !f.IsFileSwap && string.IsNullOrEmpty(f.Hash));
		if (removed > 0)
		{
			_logger.LogDebug("Removed {amount} of invalid files", removed);
		}
		ct.ThrowIfCancellationRequested();
		if (objectKind == ObjectKind.Player)
		{
			try
			{
				await VerifyPlayerAnimationBones(boneIndices, fragment as CharacterDataFragmentPlayer, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException e)
			{
				_logger.LogDebug(e, "Cancelled during player animation verification");
				throw;
			}
			catch (Exception e)
			{
				_logger.LogWarning(e, "Failed to verify player animations, continuing without further verification");
			}
		}
		_logger.LogInformation("Building character data for {obj} took {time}ms", objectKind, TimeSpan.FromTicks(DateTime.UtcNow.Ticks - start.Ticks).TotalMilliseconds);
		return fragment;
	}

	private async Task VerifyPlayerAnimationBones(Dictionary<string, List<ushort>>? boneIndices, CharacterDataFragmentPlayer fragment, CancellationToken ct)
	{
		if (boneIndices == null)
		{
			return;
		}
		foreach (KeyValuePair<string, List<ushort>> kvp in boneIndices)
		{
			_logger.LogDebug("Found {skellyname} ({idx} bone indices) on player: {bones}", kvp.Key, kvp.Value.Any() ? kvp.Value.Max() : 0, string.Join(',', kvp.Value));
		}
		if (boneIndices.All<KeyValuePair<string, List<ushort>>>((KeyValuePair<string, List<ushort>> u) => u.Value.Count == 0))
		{
			return;
		}
		int noValidationFailed = 0;
		foreach (FileReplacement file in fragment.FileReplacements.Where((FileReplacement f) => !f.IsFileSwap && f.GamePaths.First().EndsWith("pap", StringComparison.OrdinalIgnoreCase)).ToList())
		{
			ct.ThrowIfCancellationRequested();
			Dictionary<string, List<ushort>> skeletonIndices = await _dalamudUtil.RunOnFrameworkThread(() => _modelAnalyzer.GetBoneIndicesFromPap(file.Hash), "VerifyPlayerAnimationBones", "C:\\Users\\Owner\\sync_client2\\XIVSync\\PlayerData\\Factories\\PlayerDataFactory.cs", 282).ConfigureAwait(continueOnCapturedContext: false);
			bool validationFailed = false;
			if (skeletonIndices != null)
			{
				if (skeletonIndices.All((KeyValuePair<string, List<ushort>> k) => k.Value.Max() <= 105))
				{
					_logger.LogTrace("All indices of {path} are <= 105, ignoring", file.ResolvedPath);
					continue;
				}
				_logger.LogDebug("Verifying bone indices for {path}, found {x} skeletons", file.ResolvedPath, skeletonIndices.Count);
				foreach (KeyValuePair<string, List<ushort>> boneCount in skeletonIndices.Select((KeyValuePair<string, List<ushort>> k) => k).ToList())
				{
					if (boneCount.Value.Max() > boneIndices.SelectMany<KeyValuePair<string, List<ushort>>, ushort>((KeyValuePair<string, List<ushort>> b) => b.Value).Max())
					{
						_logger.LogWarning("Found more bone indices on the animation {path} skeleton {skl} (max indice {idx}) than on any player related skeleton (max indice {idx2})", file.ResolvedPath, boneCount.Key, boneCount.Value.Max(), boneIndices.SelectMany<KeyValuePair<string, List<ushort>>, ushort>((KeyValuePair<string, List<ushort>> b) => b.Value).Max());
						validationFailed = true;
						break;
					}
				}
			}
			if (!validationFailed)
			{
				continue;
			}
			noValidationFailed++;
			_logger.LogDebug("Removing {file} from sent file replacements and transient data", file.ResolvedPath);
			fragment.FileReplacements.Remove(file);
			foreach (string gamePath in file.GamePaths)
			{
				_transientResourceManager.RemoveTransientResource(ObjectKind.Player, gamePath);
			}
		}
		if (noValidationFailed > 0)
		{
			_mareMediator.Publish(new NotificationMessage("Invalid Skeleton Setup", $"Your client is attempting to send {noValidationFailed} animation files with invalid bone data. Those animation files have been removed from your sent data. Verify that you are using the correct skeleton for those animation files (Check /xllog for more information).", NotificationType.Warning, TimeSpan.FromSeconds(10L)));
		}
	}

	private async Task<IReadOnlyDictionary<string, string[]>> GetFileReplacementsFromPaths(HashSet<string> forwardResolve, HashSet<string> reverseResolve)
	{
		string[] forwardPaths = forwardResolve.ToArray();
		string[] reversePaths = reverseResolve.ToArray();
		Dictionary<string, List<string>> resolvedPaths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		(string[], string[][]) obj = await _ipcManager.Penumbra.ResolvePathsAsync(forwardPaths, reversePaths).ConfigureAwait(continueOnCapturedContext: false);
		string[] forward = obj.Item1;
		string[][] reverse = obj.Item2;
		for (int i = 0; i < forwardPaths.Length; i++)
		{
			string filePath = forward[i].ToLowerInvariant();
			if (resolvedPaths.TryGetValue(filePath, out List<string> list))
			{
				list.Add(forwardPaths[i].ToLowerInvariant());
				continue;
			}
			resolvedPaths[filePath] = new List<string>(1) { forwardPaths[i].ToLowerInvariant() };
		}
		for (int i = 0; i < reversePaths.Length; i++)
		{
			string filePath = reversePaths[i].ToLowerInvariant();
			if (resolvedPaths.TryGetValue(filePath, out List<string> list))
			{
				list.AddRange(reverse[i].Select((string c) => c.ToLowerInvariant()));
			}
			else
			{
				resolvedPaths[filePath] = new List<string>(reverse[i].Select((string c) => c.ToLowerInvariant()).ToList());
			}
		}
		return resolvedPaths.ToDictionary<KeyValuePair<string, List<string>>, string, string[]>((KeyValuePair<string, List<string>> k) => k.Key, (KeyValuePair<string, List<string>> k) => k.Value.ToArray(), StringComparer.OrdinalIgnoreCase).AsReadOnly();
	}

	private HashSet<string> ManageSemiTransientData(ObjectKind objectKind)
	{
		_transientResourceManager.PersistTransientResources(objectKind);
		HashSet<string> pathsToResolve = new HashSet<string>(StringComparer.Ordinal);
		foreach (string path in from path in _transientResourceManager.GetSemiTransientResources(objectKind)
			where !string.IsNullOrEmpty(path)
			select path)
		{
			pathsToResolve.Add(path);
		}
		return pathsToResolve;
	}
}
