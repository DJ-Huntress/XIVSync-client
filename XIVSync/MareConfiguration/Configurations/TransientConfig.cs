using System;
using System.Collections.Generic;
using System.Linq;
using XIVSync.API.Data.Enum;

namespace XIVSync.MareConfiguration.Configurations;

public class TransientConfig : IMareConfiguration
{
	public class TransientPlayerConfig
	{
		public List<string> GlobalPersistentCache { get; set; } = new List<string>();


		public Dictionary<uint, List<string>> JobSpecificCache { get; set; } = new Dictionary<uint, List<string>>();


		public Dictionary<uint, List<string>> JobSpecificPetCache { get; set; } = new Dictionary<uint, List<string>>();


		private bool ElevateIfNeeded(uint jobId, string gamePath)
		{
			foreach (KeyValuePair<uint, List<string>> kvp in JobSpecificCache)
			{
				if (kvp.Key != jobId && kvp.Value.Contains<string>(gamePath, StringComparer.Ordinal))
				{
					JobSpecificCache[kvp.Key].Remove(gamePath);
					GlobalPersistentCache.Add(gamePath);
					return true;
				}
			}
			return false;
		}

		public int RemovePath(string gamePath, ObjectKind objectKind)
		{
			int removedEntries = 0;
			if (objectKind == ObjectKind.Player)
			{
				if (GlobalPersistentCache.Remove(gamePath))
				{
					removedEntries++;
				}
				foreach (KeyValuePair<uint, List<string>> item in JobSpecificCache)
				{
					if (item.Value.Remove(gamePath))
					{
						removedEntries++;
					}
				}
			}
			if (objectKind == ObjectKind.Pet)
			{
				foreach (KeyValuePair<uint, List<string>> item2 in JobSpecificPetCache)
				{
					if (item2.Value.Remove(gamePath))
					{
						removedEntries++;
					}
				}
			}
			return removedEntries;
		}

		public void AddOrElevate(uint jobId, string gamePath)
		{
			if (!GlobalPersistentCache.Contains<string>(gamePath, StringComparer.Ordinal) && !ElevateIfNeeded(jobId, gamePath))
			{
				if (!JobSpecificCache.TryGetValue(jobId, out List<string> jobCache))
				{
					jobCache = (JobSpecificCache[jobId] = new List<string>());
				}
				if (!jobCache.Contains<string>(gamePath, StringComparer.Ordinal))
				{
					jobCache.Add(gamePath);
				}
			}
		}
	}

	public Dictionary<string, TransientPlayerConfig> TransientConfigs { get; set; } = new Dictionary<string, TransientPlayerConfig>();


	public int Version { get; set; } = 1;

}
