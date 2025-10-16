using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Dalamud.Plugin.Services;

namespace XIVSync.Security;

internal static class ExportGuard
{
	private static readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

	private static readonly HashSet<string> _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static IToastGui? _toast;

	private static bool _running;

	public static bool Install(IToastGui toast, IEnumerable<string>? watchDirs = null)
	{
		if (_running)
		{
			return true;
		}
		_toast = toast;
		if (Environment.GetEnvironmentVariable("XIVSYNC_DISABLE_EXPORT_GUARD") == "1")
		{
			return false;
		}
		List<string> dirs = (watchDirs ?? DefaultGuessExportDirs()).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		if (dirs.Count == 0)
		{
			return false;
		}
		try
		{
			foreach (string dir in dirs)
			{
				if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
				{
					FileSystemWatcher w = new FileSystemWatcher(dir)
					{
						IncludeSubdirectories = true,
						Filter = "*.zip",
						NotifyFilter = (NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime)
					};
					w.Created += OnZipEvent;
					w.Changed += OnZipEvent;
					w.Renamed += OnZipRenamed;
					w.EnableRaisingEvents = true;
					_watchers.Add(w);
					FileSystemWatcher w2 = new FileSystemWatcher(dir)
					{
						IncludeSubdirectories = true,
						Filter = "*.pmp",
						NotifyFilter = (NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime)
					};
					w2.Created += OnZipEvent;
					w2.Changed += OnZipEvent;
					w2.Renamed += OnZipRenamed;
					w2.EnableRaisingEvents = true;
					_watchers.Add(w2);
				}
			}
			_running = _watchers.Count > 0;
			return _running;
		}
		catch
		{
			Uninstall();
			return false;
		}
	}

	public static void Uninstall()
	{
		foreach (FileSystemWatcher w in _watchers)
		{
			try
			{
				w.EnableRaisingEvents = false;
				w.Created -= OnZipEvent;
				w.Changed -= OnZipEvent;
				w.Renamed -= OnZipRenamed;
				w.Dispose();
			}
			catch
			{
			}
		}
		_watchers.Clear();
		_pending.Clear();
		_running = false;
	}

	private static IEnumerable<string> DefaultGuessExportDirs()
	{
		string docs = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
		string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
		string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dalamud = Path.Combine([appData, "XIVLauncher", "addon", "Hooks", "dev"]);
        string penumbra = Path.Combine(appData, "XIVLauncher", "pluginConfigs", "Penumbra");

        return new string[6]
		{
			Path.Combine(docs, "Penumbra"),
			Path.Combine(docs, "Penumbra", "Exports"),
			desktop,
			downloads,
			penumbra,
			dalamud
		}.Where(Directory.Exists);
	}

	private static void OnZipRenamed(object sender, RenamedEventArgs e)
	{
		if (LooksLikeCharacterPack(e.FullPath))
		{
			TryDeleteWithRetries(e.FullPath);
		}
	}

	private static void OnZipEvent(object sender, FileSystemEventArgs e)
	{
		if (!_running || !LooksLikeCharacterPack(e.FullPath) || !_pending.Add(e.FullPath))
		{
			return;
		}
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				TryDeleteWithRetries(e.FullPath);
			}
			finally
			{
				_pending.Remove(e.FullPath);
			}
		});
	}

	private static bool LooksLikeCharacterPack(string path)
	{
		string name = Path.GetFileName(path);
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		string n = name.ToLowerInvariant();
		if (!n.Contains("character") && !n.Contains("pcp") && !n.Contains("penumbra"))
		{
			return n.Contains("pack");
		}
		return true;
	}

	private static void TryDeleteWithRetries(string fullPath, int tries = 10, int delayMs = 300)
	{
		for (int i = 0; i < tries; i++)
		{
			try
			{
				using (new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
				{
				}
				File.Delete(fullPath);
				_toast?.ShowError("Character Pack export blocked and removed.");
				return;
			}
			catch (FileNotFoundException)
			{
				return;
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
			Thread.Sleep(delayMs);
		}
		try
		{
			File.Delete(fullPath);
		}
		catch
		{
		}
	}
}
