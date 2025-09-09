using System;
using MessagePack;

namespace XIVSync.API.Dto;

[MessagePackObject(true)]
public record ServerInfo
{
	public string ShardName { get; set; } = string.Empty;

	public int MaxGroupUserCount { get; set; } = 100;

	public int MaxGroupsCreatedByUser { get; set; } = 5;

	public int MaxGroupsJoinedByUser { get; set; } = 20;

	public Uri FileServerAddress { get; set; } = new Uri("http://nonemptyuri");

	public int MaxCharaData { get; set; }

	public int MaxCharaDataVanity { get; set; }
}
