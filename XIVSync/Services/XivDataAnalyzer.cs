using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Havok.Animation;
using FFXIVClientStructs.Havok.Animation.Animation;
using FFXIVClientStructs.Havok.Common.Base.Container.Array;
using FFXIVClientStructs.Havok.Common.Base.Types;
using FFXIVClientStructs.Havok.Common.Serialize.Resource;
using FFXIVClientStructs.Havok.Common.Serialize.Util;
using Lumina.Data.Parsing;
using Microsoft.Extensions.Logging;
using XIVSync.FileCache;
using XIVSync.Interop.GameModel;
using XIVSync.MareConfiguration;
using XIVSync.PlayerData.Handlers;

namespace XIVSync.Services;

public sealed class XivDataAnalyzer
{
	private readonly ILogger<XivDataAnalyzer> _logger;

	private readonly FileCacheManager _fileCacheManager;

	private readonly XivDataStorageService _configService;

	private readonly List<string> _failedCalculatedTris = new List<string>();

	public XivDataAnalyzer(ILogger<XivDataAnalyzer> logger, FileCacheManager fileCacheManager, XivDataStorageService configService)
	{
		_logger = logger;
		_fileCacheManager = fileCacheManager;
		_configService = configService;
	}

	public unsafe Dictionary<string, List<ushort>>? GetSkeletonBoneIndices(GameObjectHandler handler)
	{
		if (handler.Address == IntPtr.Zero)
		{
			return null;
		}
		CharacterBase* chara = (CharacterBase*)((Character*)handler.Address)->GameObject.DrawObject;
		if (chara->GetModelType() != CharacterBase.ModelType.Human)
		{
			return null;
		}
		SkeletonResourceHandle** resHandles = chara->Skeleton->SkeletonResourceHandles;
		Dictionary<string, List<ushort>> outputIndices = new Dictionary<string, List<ushort>>();
		try
		{
			for (int i = 0; i < chara->Skeleton->PartialSkeletonCount; i++)
			{
				SkeletonResourceHandle* handle = resHandles[i];
				ILogger<XivDataAnalyzer> logger = _logger;
				object[] obj = new object[2] { i, null };
				nint num = (nint)handle;
				obj[1] = ((IntPtr)num).ToString("X");
				logger.LogTrace("Iterating over SkeletonResourceHandle #{i}:{x}", obj);
				if (handle == (SkeletonResourceHandle*)IntPtr.Zero)
				{
					continue;
				}
				uint curBones = handle->BoneCount;
				if (handle->FileName.Length > 1024)
				{
					continue;
				}
				string skeletonName = handle->FileName.ToString();
				if (string.IsNullOrEmpty(skeletonName))
				{
					continue;
				}
				outputIndices[skeletonName] = new List<ushort>();
				for (ushort boneIdx = 0; boneIdx < curBones; boneIdx++)
				{
					if (handle->HavokSkeleton->Bones[boneIdx].Name.String != null)
					{
						outputIndices[skeletonName].Add((ushort)(boneIdx + 1));
					}
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not process skeleton data");
		}
		if (outputIndices.Count == 0 || !outputIndices.Values.All((List<ushort> u) => u.Count > 0))
		{
			return null;
		}
		return outputIndices;
	}

	public unsafe Dictionary<string, List<ushort>>? GetBoneIndicesFromPap(string hash)
	{
		if (_configService.Current.BonesDictionary.TryGetValue(hash, out Dictionary<string, List<ushort>> bones))
		{
			return bones;
		}
		FileCacheEntity cacheEntity = _fileCacheManager.GetFileCacheByHash(hash);
		if (cacheEntity == null)
		{
			return null;
		}
		using BinaryReader reader = new BinaryReader(File.Open(cacheEntity.ResolvedFilepath, FileMode.Open, FileAccess.Read, FileShare.Read));
		reader.ReadInt32();
		reader.ReadInt32();
		reader.ReadInt16();
		reader.ReadInt16();
		if (reader.ReadByte() != 0)
		{
			return null;
		}
		reader.ReadByte();
		reader.ReadInt32();
		int havokPosition = reader.ReadInt32();
		int havokDataSize = reader.ReadInt32() - havokPosition;
		reader.BaseStream.Position = havokPosition;
		byte[] havokData = reader.ReadBytes(havokDataSize);
		if (havokData.Length <= 8)
		{
			return null;
		}
		Dictionary<string, List<ushort>> output = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
		string tempHavokDataPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()) + ".hkx";
		nint tempHavokDataPathAnsi = Marshal.StringToHGlobalAnsi(tempHavokDataPath);
		try
		{
			File.WriteAllBytes(tempHavokDataPath, havokData);
			hkSerializeUtil.LoadOptions* loadoptions = stackalloc hkSerializeUtil.LoadOptions[1];
			loadoptions->TypeInfoRegistry = hkBuiltinTypeRegistry.Instance()->GetTypeInfoRegistry();
			loadoptions->ClassNameRegistry = hkBuiltinTypeRegistry.Instance()->GetClassNameRegistry();
			loadoptions->Flags = new hkFlags<hkSerializeUtil.LoadOptionBits, int>
			{
				Storage = 0
			};
			hkResource* resource = hkSerializeUtil.LoadFromFile((byte*)tempHavokDataPathAnsi, null, loadoptions);
			if (resource == null)
			{
				throw new InvalidOperationException("Resource was null after loading");
			}
			ReadOnlySpan<byte> rootLevelName = "hkRootLevelContainer"u8;
			fixed (byte* n1 = rootLevelName)
			{
				hkRootLevelContainer* container = (hkRootLevelContainer*)resource->GetContentsPointer(n1, hkBuiltinTypeRegistry.Instance()->GetTypeInfoRegistry());
				ReadOnlySpan<byte> animationName = "hkaAnimationContainer"u8;
				fixed (byte* n2 = animationName)
				{
					hkaAnimationContainer* animContainer = (hkaAnimationContainer*)container->findObjectByName(n2, null);
					for (int i = 0; i < animContainer->Bindings.Length; i++)
					{
						hkaAnimationBinding* ptr = animContainer->Bindings[i].ptr;
						hkArray<short> boneTransform = ptr->TransformTrackToBoneIndices;
						string name = ptr->OriginalSkeletonName.String + "_" + i;
						output[name] = new List<ushort>();
						for (int boneIdx = 0; boneIdx < boneTransform.Length; boneIdx++)
						{
							output[name].Add((ushort)boneTransform[boneIdx]);
						}
						output[name].Sort();
					}
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not load havok file in {path}", tempHavokDataPath);
		}
		finally
		{
			Marshal.FreeHGlobal(tempHavokDataPathAnsi);
			File.Delete(tempHavokDataPath);
		}
		_configService.Current.BonesDictionary[hash] = output;
		_configService.Save();
		return output;
	}

	public async Task<long> GetTrianglesByHash(string hash)
	{
		if (_configService.Current.TriangleDictionary.TryGetValue(hash, out var cachedTris) && cachedTris > 0)
		{
			return cachedTris;
		}
		if (_failedCalculatedTris.Contains<string>(hash, StringComparer.Ordinal))
		{
			return 0L;
		}
		FileCacheEntity path = _fileCacheManager.GetFileCacheByHash(hash);
		if (path == null || !path.ResolvedFilepath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
		{
			return 0L;
		}
		string filePath = path.ResolvedFilepath;
		try
		{
			_logger.LogDebug("Detected Model File {path}, calculating Tris", filePath);
			MdlFile file = new MdlFile(filePath);
			if (file.LodCount <= 0)
			{
				_failedCalculatedTris.Add(hash);
				_configService.Current.TriangleDictionary[hash] = 0L;
				_configService.Save();
				return 0L;
			}
			long tris = 0L;
			for (int i = 0; i < file.LodCount; i++)
			{
				try
				{
					ushort meshIdx = file.Lods[i].MeshIndex;
					ushort meshCnt = file.Lods[i].MeshCount;
					tris = file.Meshes.Skip(meshIdx).Take(meshCnt).Sum((MdlStructs.MeshStruct p) => p.IndexCount) / 3;
				}
				catch (Exception ex)
				{
					_logger.LogDebug(ex, "Could not load lod mesh {mesh} from path {path}", i, filePath);
					continue;
				}
				if (tris > 0)
				{
					_logger.LogDebug("TriAnalysis: {filePath} => {tris} triangles", filePath, tris);
					_configService.Current.TriangleDictionary[hash] = tris;
					_configService.Save();
					break;
				}
			}
			return tris;
		}
		catch (Exception e)
		{
			_failedCalculatedTris.Add(hash);
			_configService.Current.TriangleDictionary[hash] = 0L;
			_configService.Save();
			_logger.LogWarning(e, "Could not parse file {file}", filePath);
			return 0L;
		}
	}
}
