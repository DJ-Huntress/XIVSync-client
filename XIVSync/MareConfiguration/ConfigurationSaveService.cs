using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public class ConfigurationSaveService : IHostedService
{
	private readonly HashSet<object> _configsToSave = new HashSet<object>();

	private readonly ILogger<ConfigurationSaveService> _logger;

	private readonly SemaphoreSlim _configSaveSemaphore = new SemaphoreSlim(1, 1);

	private readonly CancellationTokenSource _configSaveCheckCts = new CancellationTokenSource();

	public const string BackupFolder = "config_backup";

	private readonly MethodInfo _saveMethod;

	public ConfigurationSaveService(ILogger<ConfigurationSaveService> logger, IEnumerable<IConfigService<IMareConfiguration>> configs)
	{
		foreach (IConfigService<IMareConfiguration> config in configs)
		{
			config.ConfigSave += OnConfigurationSave;
		}
		_logger = logger;
		_saveMethod = GetType().GetMethod("SaveConfig", BindingFlags.Instance | BindingFlags.NonPublic);
	}

	private void OnConfigurationSave(object? sender, EventArgs e)
	{
		_configSaveSemaphore.Wait();
		_configsToSave.Add(sender);
		_configSaveSemaphore.Release();
	}

	private async Task PeriodicSaveCheck(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				await SaveConfigs().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Error during SaveConfigs");
			}
			await Task.Delay(TimeSpan.FromSeconds(5L), ct).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private async Task SaveConfigs()
	{
		if (_configsToSave.Count == 0)
		{
			return;
		}
		await _configSaveSemaphore.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		List<object> configList = _configsToSave.ToList();
		_configsToSave.Clear();
		_configSaveSemaphore.Release();
		foreach (object config in configList)
		{
			Type expectedType = config.GetType().BaseType.GetGenericArguments()[0];
			await ((Task)_saveMethod.MakeGenericMethod(expectedType).Invoke(this, new object[1] { config })).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private async Task SaveConfig<T>(IConfigService<T> config) where T : IMareConfiguration
	{
		_logger.LogTrace("Saving {configName}", config.ConfigurationName);
		string configDir = config.ConfigurationPath.Replace(config.ConfigurationName, string.Empty);
		try
		{
			string configBackupFolder = Path.Join(configDir, "config_backup");
			if (!Directory.Exists(configBackupFolder))
			{
				Directory.CreateDirectory(configBackupFolder);
			}
			string[] configNameSplit = config.ConfigurationName.Split(".");
			List<FileInfo> existingConfigs = (from c in Directory.EnumerateFiles(configBackupFolder, configNameSplit[0] + "*")
				select new FileInfo(c) into c
				orderby c.LastWriteTime descending
				select c).ToList();
			if (existingConfigs.Skip(10).Any())
			{
				foreach (FileInfo item in existingConfigs.Skip(10).ToList())
				{
					item.Delete();
				}
			}
			string backupPath = Path.Combine(configBackupFolder, configNameSplit[0] + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + "." + configNameSplit[1]);
			_logger.LogTrace("Backing up current config to {backupPath}", backupPath);
			File.Copy(config.ConfigurationPath, backupPath, overwrite: true);
			new FileInfo(backupPath).LastWriteTimeUtc = DateTime.UtcNow;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Could not create backup for {config}", config.ConfigurationPath);
		}
		string temp = config.ConfigurationPath + ".tmp";
		try
		{
			await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(config.Current, typeof(T), new JsonSerializerOptions
			{
				WriteIndented = true
			})).ConfigureAwait(continueOnCapturedContext: false);
			File.Move(temp, config.ConfigurationPath, overwrite: true);
			config.UpdateLastWriteTime();
		}
		catch (Exception exception2)
		{
			_logger.LogWarning(exception2, "Error during config save of {config}", config.ConfigurationName);
		}
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		Task.Run(() => PeriodicSaveCheck(_configSaveCheckCts.Token));
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		await _configSaveCheckCts.CancelAsync().ConfigureAwait(continueOnCapturedContext: false);
		_configSaveCheckCts.Dispose();
		await SaveConfigs().ConfigureAwait(continueOnCapturedContext: false);
	}
}
