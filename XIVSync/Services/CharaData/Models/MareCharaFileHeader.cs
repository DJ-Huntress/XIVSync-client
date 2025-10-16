using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace XIVSync.Services.CharaData.Models;

public record MareCharaFileHeader(byte Version, MareCharaFileData CharaFileData)
{
	public string FilePath { get; private set; } = string.Empty;


	public static readonly byte CurrentVersion = 1;

	public void WriteToStream(BinaryWriter writer)
	{
		writer.Write('M');
		writer.Write('C');
		writer.Write('D');
		writer.Write('F');
		writer.Write(Version);
		byte[] charaFileDataArray = CharaFileData.ToByteArray();
		writer.Write(charaFileDataArray.Length);
		writer.Write(charaFileDataArray);
	}

	public static MareCharaFileHeader? FromBinaryReader(string path, BinaryReader reader)
	{
		if (!string.Equals(new string(reader.ReadChars(4)), "MCDF", StringComparison.Ordinal))
		{
			throw new InvalidDataException("Not a Mare Chara File");
		}
		MareCharaFileHeader decoded = null;
		byte version = reader.ReadByte();
		if (version == 1)
		{
			int dataLength = reader.ReadInt32();
			decoded = new MareCharaFileHeader(version, MareCharaFileData.FromByteArray(reader.ReadBytes(dataLength)))
			{
				FilePath = path
			};
		}
		return decoded;
	}

	public static void AdvanceReaderToData(BinaryReader reader)
	{
		reader.ReadChars(4);
		if (reader.ReadByte() == 1)
		{
			int length = reader.ReadInt32();
			reader.ReadBytes(length);
		}
	}

	[CompilerGenerated]
	protected MareCharaFileHeader(MareCharaFileHeader original)
	{
		Version = original.Version;
		CharaFileData = original.CharaFileData;
		FilePath = original.FilePath;
	}
}
