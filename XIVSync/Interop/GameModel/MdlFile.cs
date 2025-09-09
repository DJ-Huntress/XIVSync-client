using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lumina.Data;
using Lumina.Data.Parsing;
using Lumina.Extensions;

namespace XIVSync.Interop.GameModel;

public class MdlFile
{
	public struct ModelHeader
	{
		public float Radius;

		public ushort MeshCount;

		public ushort AttributeCount;

		public ushort SubmeshCount;

		public ushort MaterialCount;

		public ushort BoneCount;

		public ushort BoneTableCount;

		public ushort ShapeCount;

		public ushort ShapeMeshCount;

		public ushort ShapeValueCount;

		public byte LodCount;

		public ModelFlags1 Flags1;

		public ushort ElementIdCount;

		public byte TerrainShadowMeshCount;

		public ModelFlags2 Flags2;

		public float ModelClipOutDistance;

		public float ShadowClipOutDistance;

		public ushort CullingGridCount;

		public ushort TerrainShadowSubmeshCount;

		public byte Flags3;

		public byte BGChangeMaterialIndex;

		public byte BGCrestChangeMaterialIndex;

		public byte Unknown6;

		public ushort BoneTableArrayCountTotal;

		public ushort Unknown8;

		public ushort Unknown9;

		private unsafe fixed byte _padding[6];
	}

	public struct ShapeStruct
	{
		public uint StringOffset;

		public ushort[] ShapeMeshStartIndex;

		public ushort[] ShapeMeshCount;

		public static ShapeStruct Read(LuminaBinaryReader br)
		{
			return new ShapeStruct
			{
				StringOffset = br.ReadUInt32(),
				ShapeMeshStartIndex = br.ReadUInt16Array(3),
				ShapeMeshCount = br.ReadUInt16Array(3)
			};
		}
	}

	[Flags]
	public enum ModelFlags1 : byte
	{
		DustOcclusionEnabled = 0x80,
		SnowOcclusionEnabled = 0x40,
		RainOcclusionEnabled = 0x20,
		Unknown1 = 0x10,
		LightingReflectionEnabled = 8,
		WavingAnimationDisabled = 4,
		LightShadowDisabled = 2,
		ShadowDisabled = 1
	}

	[Flags]
	public enum ModelFlags2 : byte
	{
		Unknown2 = 0x80,
		BgUvScrollEnabled = 0x40,
		EnableForceNonResident = 0x20,
		ExtraLodEnabled = 0x10,
		ShadowMaskEnabled = 8,
		ForceLodRangeEnabled = 4,
		EdgeGeometryEnabled = 2,
		Unknown3 = 1
	}

	public struct VertexDeclarationStruct
	{
		public MdlStructs.VertexElement[] VertexElements;

		public static VertexDeclarationStruct Read(LuminaBinaryReader br)
		{
			VertexDeclarationStruct ret = default(VertexDeclarationStruct);
			List<MdlStructs.VertexElement> elems = new List<MdlStructs.VertexElement>();
			MdlStructs.VertexElement thisElem = br.ReadStructure<MdlStructs.VertexElement>();
			do
			{
				elems.Add(thisElem);
				thisElem = br.ReadStructure<MdlStructs.VertexElement>();
			}
			while (thisElem.Stream != byte.MaxValue);
			int toSeek = 136 - (elems.Count + 1) * 8;
			br.Seek(br.BaseStream.Position + toSeek);
			ret.VertexElements = elems.ToArray();
			return ret;
		}
	}

	public const int V5 = 16777221;

	public const int V6 = 16777222;

	public const uint NumVertices = 17u;

	public const uint FileHeaderSize = 68u;

	public uint Version = 16777221u;

	public float Radius;

	public float ModelClipOutDistance;

	public float ShadowClipOutDistance;

	public byte BgChangeMaterialIndex;

	public byte BgCrestChangeMaterialIndex;

	public ushort CullingGridCount;

	public byte Flags3;

	public byte Unknown6;

	public ushort Unknown8;

	public ushort Unknown9;

	public uint[] VertexOffset = new uint[3];

	public uint[] IndexOffset = new uint[3];

	public uint[] VertexBufferSize = new uint[3];

	public uint[] IndexBufferSize = new uint[3];

	public byte LodCount;

	public bool EnableIndexBufferStreaming;

	public bool EnableEdgeGeometry;

	public ModelFlags1 Flags1;

	public ModelFlags2 Flags2;

	public VertexDeclarationStruct[] VertexDeclarations = Array.Empty<VertexDeclarationStruct>();

	public MdlStructs.ElementIdStruct[] ElementIds = Array.Empty<MdlStructs.ElementIdStruct>();

	public MdlStructs.MeshStruct[] Meshes = Array.Empty<MdlStructs.MeshStruct>();

	public MdlStructs.BoundingBoxStruct[] BoneBoundingBoxes = Array.Empty<MdlStructs.BoundingBoxStruct>();

	public MdlStructs.LodStruct[] Lods = Array.Empty<MdlStructs.LodStruct>();

	public MdlStructs.ExtraLodStruct[] ExtraLods = Array.Empty<MdlStructs.ExtraLodStruct>();

	public MdlFile(string filePath)
	{
		using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
		using LuminaBinaryReader r = new LuminaBinaryReader(stream);
		MdlStructs.ModelFileHeader header = LoadModelFileHeader(r);
		LodCount = header.LodCount;
		VertexBufferSize = header.VertexBufferSize;
		IndexBufferSize = header.IndexBufferSize;
		VertexOffset = header.VertexOffset;
		IndexOffset = header.IndexOffset;
		uint dataOffset = 68 + header.RuntimeSize + header.StackSize;
		for (int i = 0; i < LodCount; i++)
		{
			VertexOffset[i] -= dataOffset;
			IndexOffset[i] -= dataOffset;
		}
		VertexDeclarations = new VertexDeclarationStruct[header.VertexDeclarationCount];
		for (int j = 0; j < header.VertexDeclarationCount; j++)
		{
			VertexDeclarations[j] = VertexDeclarationStruct.Read(r);
		}
		LoadStrings(r);
		ModelHeader modelHeader = LoadModelHeader(r);
		ElementIds = new MdlStructs.ElementIdStruct[modelHeader.ElementIdCount];
		for (int k = 0; k < modelHeader.ElementIdCount; k++)
		{
			ElementIds[k] = MdlStructs.ElementIdStruct.Read(r);
		}
		Lods = new MdlStructs.LodStruct[3];
		for (int l = 0; l < 3; l++)
		{
			MdlStructs.LodStruct lod = r.ReadStructure<MdlStructs.LodStruct>();
			if (l < LodCount)
			{
				lod.VertexDataOffset -= dataOffset;
				lod.IndexDataOffset -= dataOffset;
			}
			Lods[l] = lod;
		}
		ExtraLods = (((modelHeader.Flags2 & ModelFlags2.ExtraLodEnabled) != 0) ? r.ReadStructuresAsArray<MdlStructs.ExtraLodStruct>(3) : Array.Empty<MdlStructs.ExtraLodStruct>());
		Meshes = new MdlStructs.MeshStruct[modelHeader.MeshCount];
		for (int m = 0; m < modelHeader.MeshCount; m++)
		{
			Meshes[m] = MdlStructs.MeshStruct.Read(r);
		}
	}

	private MdlStructs.ModelFileHeader LoadModelFileHeader(LuminaBinaryReader r)
	{
		MdlStructs.ModelFileHeader header = MdlStructs.ModelFileHeader.Read(r);
		Version = header.Version;
		EnableIndexBufferStreaming = header.EnableIndexBufferStreaming;
		EnableEdgeGeometry = header.EnableEdgeGeometry;
		return header;
	}

	private ModelHeader LoadModelHeader(BinaryReader r)
	{
		ModelHeader modelHeader = r.ReadStructure<ModelHeader>();
		Radius = modelHeader.Radius;
		Flags1 = modelHeader.Flags1;
		Flags2 = modelHeader.Flags2;
		ModelClipOutDistance = modelHeader.ModelClipOutDistance;
		ShadowClipOutDistance = modelHeader.ShadowClipOutDistance;
		CullingGridCount = modelHeader.CullingGridCount;
		Flags3 = modelHeader.Flags3;
		Unknown6 = modelHeader.Unknown6;
		Unknown8 = modelHeader.Unknown8;
		Unknown9 = modelHeader.Unknown9;
		BgChangeMaterialIndex = modelHeader.BGChangeMaterialIndex;
		BgCrestChangeMaterialIndex = modelHeader.BGCrestChangeMaterialIndex;
		return modelHeader;
	}

	private static (uint[], string[]) LoadStrings(BinaryReader r)
	{
		ushort stringCount = r.ReadUInt16();
		r.ReadUInt16();
		int stringSize = (int)r.ReadUInt32();
		byte[] stringData = r.ReadBytes(stringSize);
		int start = 0;
		string[] strings = new string[stringCount];
		uint[] offsets = new uint[stringCount];
		for (int i = 0; i < stringCount; i++)
		{
			Span<byte> span = stringData.AsSpan(start);
			int idx = span.IndexOf<byte>(0);
			strings[i] = Encoding.UTF8.GetString(span.Slice(0, idx));
			offsets[i] = (uint)start;
			start = start + idx + 1;
		}
		return (offsets, strings);
	}
}
