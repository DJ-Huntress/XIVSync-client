using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XIVSync.UI;

public sealed class Vector4JsonConverter : JsonConverter<Vector4>
{
	public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.StartArray)
		{
			reader.Read();
			float single = reader.GetSingle();
			reader.Read();
			float y = reader.GetSingle();
			reader.Read();
			float z = reader.GetSingle();
			reader.Read();
			float w = reader.GetSingle();
			reader.Read();
			return new Vector4(single, y, z, w);
		}
		if (reader.TokenType == JsonTokenType.StartObject)
		{
			float x = 0f;
			float y2 = 0f;
			float z2 = 0f;
			float w2 = 0f;
			while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
			{
				string? text = reader.GetString();
				reader.Read();
				float v = reader.GetSingle();
				string text2 = text.ToLowerInvariant();
				if (text2 == null)
				{
					continue;
				}
				switch (text2.Length)
				{
				case 1:
					switch (text2[0])
					{
					case 'r':
					case 'x':
						break;
					case 'g':
					case 'y':
						goto IL_0187;
					case 'b':
					case 'z':
						goto IL_018d;
					case 'a':
					case 'w':
						goto end_IL_00ac;
					default:
						continue;
					}
					goto IL_0182;
				case 5:
				{
					char c = text2[0];
					if (c != 'a')
					{
						if (c != 'g' || !(text2 == "green"))
						{
							continue;
						}
						goto IL_0187;
					}
					if (!(text2 == "alpha"))
					{
						continue;
					}
					break;
				}
				case 3:
					if (!(text2 == "red"))
					{
						continue;
					}
					goto IL_0182;
				case 4:
					if (!(text2 == "blue"))
					{
						continue;
					}
					goto IL_018d;
				default:
					continue;
					IL_018d:
					z2 = v;
					continue;
					IL_0182:
					x = v;
					continue;
					IL_0187:
					y2 = v;
					continue;
					end_IL_00ac:
					break;
				}
				w2 = v;
			}
			return new Vector4(x, y2, z2, w2);
		}
		throw new JsonException("Invalid Vector4 JSON.");
	}

	public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		writer.WriteNumberValue(value.X);
		writer.WriteNumberValue(value.Y);
		writer.WriteNumberValue(value.Z);
		writer.WriteNumberValue(value.W);
		writer.WriteEndArray();
	}
}
