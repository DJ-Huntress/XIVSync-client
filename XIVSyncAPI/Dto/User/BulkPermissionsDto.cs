using System.Collections.Generic;
using MessagePack;
using XIVSync.API.Data.Enum;

namespace XIVSync.API.Dto.User;

[MessagePackObject(true)]
public record BulkPermissionsDto(Dictionary<string, UserPermissions> AffectedUsers, Dictionary<string, GroupUserPreferredPermissions> AffectedGroups);
