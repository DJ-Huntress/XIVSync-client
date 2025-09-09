using System;
using XIVSync.MareConfiguration.Configurations;

namespace XIVSync.MareConfiguration;

public interface IConfigService<out T> : IDisposable where T : IMareConfiguration
{
	T Current { get; }

	string ConfigurationName { get; }

	string ConfigurationPath { get; }

	event EventHandler? ConfigSave;

	void UpdateLastWriteTime();
}
