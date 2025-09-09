using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public abstract class ConfigurationServiceBase<T> : IConfigService<T>, IDisposable where T : IMareConfiguration
{
	private readonly CancellationTokenSource _periodicCheckCts = new CancellationTokenSource();

	private DateTime _configLastWriteTime;

	private Lazy<T> _currentConfigInternal;

	private bool _disposed;

	public string ConfigurationDirectory { get; init; }

	public T Current => _currentConfigInternal.Value;

	public abstract string ConfigurationName { get; }

	public string ConfigurationPath => Path.Combine(ConfigurationDirectory, ConfigurationName);

	public event EventHandler? ConfigSave;

	protected ConfigurationServiceBase(string configDirectory)
	{
		ConfigurationDirectory = configDirectory;
		Task.Run((Func<Task?>)CheckForConfigUpdatesInternal, _periodicCheckCts.Token);
		_currentConfigInternal = LazyConfig();
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public void Save()
	{
		this.ConfigSave?.Invoke(this, EventArgs.Empty);
	}

	public void UpdateLastWriteTime()
	{
		_configLastWriteTime = GetConfigLastWriteTime();
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing && !_disposed)
		{
			_disposed = true;
			_periodicCheckCts.Cancel();
			_periodicCheckCts.Dispose();
		}
	}

	protected T LoadConfig()
	{
		T config;
		if (!File.Exists(ConfigurationPath))
		{
			config = AttemptToLoadBackup();
			if (object.Equals(config, default(T)))
			{
				config = (T)Activator.CreateInstance(typeof(T));
				Save();
			}
		}
		else
		{
			try
			{
				config = JsonSerializer.Deserialize<T>(File.ReadAllText(ConfigurationPath));
			}
			catch
			{
				config = AttemptToLoadBackup();
			}
			if (config == null || object.Equals(config, default(T)))
			{
				config = (T)Activator.CreateInstance(typeof(T));
				Save();
			}
		}
		_configLastWriteTime = GetConfigLastWriteTime();
		return config;
	}

	private T? AttemptToLoadBackup()
	{
		string configBackupFolder = Path.Join(ConfigurationDirectory, "config_backup");
		string[] configNameSplit = ConfigurationName.Split(".");
		if (!Directory.Exists(configBackupFolder))
		{
			return default(T);
		}
		foreach (string file in from f in Directory.EnumerateFiles(configBackupFolder, configNameSplit[0] + "*")
			orderby new FileInfo(f).LastWriteTimeUtc descending
			select f)
		{
			try
			{
				T? val = JsonSerializer.Deserialize<T>(File.ReadAllText(file));
				if (object.Equals(val, default(T)))
				{
					File.Delete(file);
				}
				File.Copy(file, ConfigurationPath, overwrite: true);
				return val;
			}
			catch
			{
				File.Delete(file);
			}
		}
		return default(T);
	}

	private async Task CheckForConfigUpdatesInternal()
	{
		while (!_periodicCheckCts.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(5L), _periodicCheckCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (GetConfigLastWriteTime() != _configLastWriteTime)
			{
				_currentConfigInternal = LazyConfig();
			}
		}
	}

	private DateTime GetConfigLastWriteTime()
	{
		try
		{
			return new FileInfo(ConfigurationPath).LastWriteTimeUtc;
		}
		catch
		{
			return DateTime.MinValue;
		}
	}

	private Lazy<T> LazyConfig()
	{
		_configLastWriteTime = GetConfigLastWriteTime();
		return new Lazy<T>(LoadConfig);
	}
}
