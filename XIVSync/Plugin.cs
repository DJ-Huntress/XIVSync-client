using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using Dalamud;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NReco.Logging.File;
using XIVSync.FileCache;
using XIVSync.Interop;
using XIVSync.Interop.Ipc;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Configurations;
using XIVSync.PlayerData.Factories;
using XIVSync.PlayerData.Pairs;
using XIVSync.PlayerData.Services;
using XIVSync.Services;
using XIVSync.Services.CharaData;
using XIVSync.Services.Events;
using XIVSync.Services.Mediator;
using XIVSync.Services.ServerConfiguration;
using XIVSync.UI;
using XIVSync.UI.Components;
using XIVSync.UI.Components.Popup;
using XIVSync.UI.Handlers;
using XIVSync.UI.Theming;
using XIVSync.WebAPI;
using XIVSync.WebAPI.Files;
using XIVSync.WebAPI.SignalR;

namespace XIVSync;

public sealed class Plugin : IDalamudPlugin, IDisposable
{
	private readonly IHost _host;

	public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IDataManager gameData, IFramework framework, IObjectTable objectTable, IClientState clientState, ICondition condition, IChatGui chatGui, IGameGui gameGui, IDtrBar dtrBar, IPluginLog pluginLog, ITargetManager targetManager, INotificationManager notificationManager, ITextureProvider textureProvider, IContextMenu contextMenu, IGameInteropProvider gameInteropProvider, IGameConfig gameConfig, ISigScanner sigScanner)
	{
		if (!Directory.Exists(pluginInterface.ConfigDirectory.FullName))
		{
			Directory.CreateDirectory(pluginInterface.ConfigDirectory.FullName);
		}
		string traceDir = Path.Join(pluginInterface.ConfigDirectory.FullName, "tracelog");
		if (!Directory.Exists(traceDir))
		{
			Directory.CreateDirectory(traceDir);
		}
		foreach (FileInfo file in (from f in Directory.EnumerateFiles(traceDir)
			select new FileInfo(f) into f
			orderby f.LastWriteTimeUtc descending
			select f).Skip(9))
		{
			int attempts = 0;
			bool deleted = false;
			while (!deleted && attempts < 5)
			{
				try
				{
					file.Delete();
					deleted = true;
				}
				catch
				{
					attempts++;
					Thread.Sleep(500);
				}
			}
		}
		UILoggingProvider uiLoggingProvider = new UILoggingProvider();
		_host = new HostBuilder().UseContentRoot(pluginInterface.ConfigDirectory.FullName).ConfigureLogging(delegate(ILoggingBuilder lb)
		{
			lb.ClearProviders();
			lb.AddDalamudLogging(pluginLog, gameData.HasModifiedGameDataFiles);
			lb.AddUILogging(uiLoggingProvider);
			lb.AddFile(Path.Combine(traceDir, $"xivsync-trace-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log"), delegate(FileLoggerOptions opt)
			{
				opt.Append = true;
				opt.RollingFilesConvention = FileLoggerOptions.FileRollingConvention.Ascending;
				opt.MinLevel = LogLevel.Trace;
				opt.FileSizeLimitBytes = 52428800L;
			});
			lb.SetMinimumLevel(LogLevel.Trace);
		}).ConfigureServices(delegate(IServiceCollection collection)
		{
			collection.AddSingleton(new WindowSystem("MareSynchronos"));
			collection.AddSingleton<FileDialogManager>();
			collection.AddSingleton(new Dalamud.Localization("MareSynchronos.Localization.", "", useEmbedded: true));
			collection.AddSingleton<MareMediator>();
			collection.AddSingleton<FileCacheManager>();
			collection.AddSingleton<ServerConfigurationManager>();
			collection.AddSingleton<ApiController>();
			collection.AddSingleton<PerformanceCollectorService>();
			collection.AddSingleton<HubFactory>();
			collection.AddSingleton<FileUploadManager>();
			collection.AddSingleton<FileTransferOrchestrator>();
			collection.AddSingleton<MarePlugin>();
			collection.AddSingleton<MareProfileManager>();
			collection.AddSingleton<GameObjectHandlerFactory>();
			collection.AddSingleton<FileDownloadManagerFactory>();
			collection.AddSingleton<PairHandlerFactory>();
			collection.AddSingleton<PairFactory>();
			collection.AddSingleton<XivDataAnalyzer>();
			collection.AddSingleton<CharacterAnalyzer>();
			collection.AddSingleton<TokenProvider>();
			collection.AddSingleton<PluginWarningNotificationService>();
			collection.AddSingleton<FileCompactor>();
			collection.AddSingleton<TagHandler>();
			collection.AddSingleton<IdDisplayHandler>();
			collection.AddSingleton<ModernThemeService>();
			collection.AddSingleton<PlayerPerformanceService>();
			collection.AddSingleton<TransientResourceManager>();
			collection.AddSingleton<CharaDataManager>();
			collection.AddSingleton<CharaDataFileHandler>();
			collection.AddSingleton<CharaDataCharacterHandler>();
			collection.AddSingleton<CharaDataNearbyManager>();
			collection.AddSingleton<CharaDataGposeTogetherManager>();
			collection.AddSingleton((IServiceProvider s) => new VfxSpawnManager(s.GetRequiredService<ILogger<VfxSpawnManager>>(), gameInteropProvider, s.GetRequiredService<MareMediator>()));
			collection.AddSingleton((IServiceProvider s) => new BlockedCharacterHandler(s.GetRequiredService<ILogger<BlockedCharacterHandler>>(), gameInteropProvider));
			collection.AddSingleton((IServiceProvider s) => new IpcProvider(s.GetRequiredService<ILogger<IpcProvider>>(), pluginInterface, s.GetRequiredService<CharaDataManager>(), s.GetRequiredService<MareMediator>()));
			collection.AddSingleton<SelectPairForTagUi>();
			collection.AddSingleton(uiLoggingProvider);
			collection.AddSingleton((IServiceProvider s) => new EventAggregator(pluginInterface.ConfigDirectory.FullName, s.GetRequiredService<ILogger<EventAggregator>>(), s.GetRequiredService<MareMediator>()));
			collection.AddSingleton((IServiceProvider s) => new DalamudUtilService(s.GetRequiredService<ILogger<DalamudUtilService>>(), clientState, objectTable, framework, gameGui, condition, gameData, targetManager, gameConfig, s.GetRequiredService<BlockedCharacterHandler>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<PerformanceCollectorService>(), s.GetRequiredService<MareConfigService>()));
			collection.AddSingleton((IServiceProvider s) => new DtrEntry(s.GetRequiredService<ILogger<DtrEntry>>(), dtrBar, s.GetRequiredService<MareConfigService>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<PairManager>(), s.GetRequiredService<ApiController>()));
			collection.AddSingleton((IServiceProvider s) => new PairManager(s.GetRequiredService<ILogger<PairManager>>(), s.GetRequiredService<PairFactory>(), s.GetRequiredService<MareConfigService>(), s.GetRequiredService<MareMediator>(), contextMenu));
			collection.AddSingleton<RedrawManager>();
			collection.AddSingleton((IServiceProvider s) => new IpcCallerPenumbra(s.GetRequiredService<ILogger<IpcCallerPenumbra>>(), pluginInterface, s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<RedrawManager>()));
			collection.AddSingleton((IServiceProvider s) => new IpcCallerGlamourer(s.GetRequiredService<ILogger<IpcCallerGlamourer>>(), pluginInterface, s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<RedrawManager>()));
			collection.AddSingleton((IServiceProvider s) => new IpcCallerCustomize(s.GetRequiredService<ILogger<IpcCallerCustomize>>(), pluginInterface, s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));
			collection.AddSingleton((IServiceProvider s) => new IpcCallerHeels(s.GetRequiredService<ILogger<IpcCallerHeels>>(), pluginInterface, s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));
			collection.AddSingleton((IServiceProvider s) => new IpcCallerHonorific(s.GetRequiredService<ILogger<IpcCallerHonorific>>(), pluginInterface, s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));
			collection.AddSingleton((IServiceProvider s) => new IpcCallerMoodles(s.GetRequiredService<ILogger<IpcCallerMoodles>>(), pluginInterface, s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));
			collection.AddSingleton((IServiceProvider s) => new IpcCallerPetNames(s.GetRequiredService<ILogger<IpcCallerPetNames>>(), pluginInterface, s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));
			collection.AddSingleton((IServiceProvider s) => new IpcCallerBrio(s.GetRequiredService<ILogger<IpcCallerBrio>>(), pluginInterface, s.GetRequiredService<DalamudUtilService>()));
			collection.AddSingleton((IServiceProvider s) => new IpcManager(s.GetRequiredService<ILogger<IpcManager>>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<IpcCallerPenumbra>(), s.GetRequiredService<IpcCallerGlamourer>(), s.GetRequiredService<IpcCallerCustomize>(), s.GetRequiredService<IpcCallerHeels>(), s.GetRequiredService<IpcCallerHonorific>(), s.GetRequiredService<IpcCallerMoodles>(), s.GetRequiredService<IpcCallerPetNames>(), s.GetRequiredService<IpcCallerBrio>()));
			collection.AddSingleton((IServiceProvider s) => new NotificationService(s.GetRequiredService<ILogger<NotificationService>>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<DalamudUtilService>(), notificationManager, chatGui, s.GetRequiredService<MareConfigService>()));
			collection.AddSingleton(delegate
			{
				HttpClient httpClient = new HttpClient();
				Version version = Assembly.GetExecutingAssembly().GetName().Version;
				httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", version.Major + "." + version.Minor + "." + version.Build));
				httpClient.Timeout = TimeSpan.FromMinutes(10L);
				return httpClient;
			});
			collection.AddSingleton((IServiceProvider s) => new MareConfigService(pluginInterface.ConfigDirectory.FullName));
			collection.AddSingleton((IServiceProvider s) => new ServerConfigService(pluginInterface.ConfigDirectory.FullName));
			collection.AddSingleton((IServiceProvider s) => new NotesConfigService(pluginInterface.ConfigDirectory.FullName));
			collection.AddSingleton((IServiceProvider s) => new ServerTagConfigService(pluginInterface.ConfigDirectory.FullName));
			collection.AddSingleton((IServiceProvider s) => new TransientConfigService(pluginInterface.ConfigDirectory.FullName));
			collection.AddSingleton((IServiceProvider s) => new XivDataStorageService(pluginInterface.ConfigDirectory.FullName));
			collection.AddSingleton((IServiceProvider s) => new PlayerPerformanceConfigService(pluginInterface.ConfigDirectory.FullName));
			collection.AddSingleton((IServiceProvider s) => new CharaDataConfigService(pluginInterface.ConfigDirectory.FullName));
			collection.AddSingleton((Func<IServiceProvider, IConfigService<IMareConfiguration>>)((IServiceProvider s) => s.GetRequiredService<MareConfigService>()));
			collection.AddSingleton((Func<IServiceProvider, IConfigService<IMareConfiguration>>)((IServiceProvider s) => s.GetRequiredService<ServerConfigService>()));
			collection.AddSingleton((Func<IServiceProvider, IConfigService<IMareConfiguration>>)((IServiceProvider s) => s.GetRequiredService<NotesConfigService>()));
			collection.AddSingleton((Func<IServiceProvider, IConfigService<IMareConfiguration>>)((IServiceProvider s) => s.GetRequiredService<ServerTagConfigService>()));
			collection.AddSingleton((Func<IServiceProvider, IConfigService<IMareConfiguration>>)((IServiceProvider s) => s.GetRequiredService<TransientConfigService>()));
			collection.AddSingleton((Func<IServiceProvider, IConfigService<IMareConfiguration>>)((IServiceProvider s) => s.GetRequiredService<XivDataStorageService>()));
			collection.AddSingleton((Func<IServiceProvider, IConfigService<IMareConfiguration>>)((IServiceProvider s) => s.GetRequiredService<PlayerPerformanceConfigService>()));
			collection.AddSingleton((Func<IServiceProvider, IConfigService<IMareConfiguration>>)((IServiceProvider s) => s.GetRequiredService<CharaDataConfigService>()));
			collection.AddSingleton<ConfigurationMigrator>();
			collection.AddSingleton<ChangelogService>();
			collection.AddSingleton<ConfigurationSaveService>();
			collection.AddSingleton<HubFactory>();
			collection.AddScoped<DrawEntityFactory>();
			collection.AddScoped<CacheMonitor>();
			collection.AddScoped<UiFactory>();
			collection.AddScoped<SelectTagForPairUi>();
			collection.AddScoped<WindowMediatorSubscriberBase, ModernSettingsUi>();
			collection.AddScoped<WindowMediatorSubscriberBase, CompactUi>();
			collection.AddScoped<WindowMediatorSubscriberBase, IntroUi>();
			collection.AddScoped<WindowMediatorSubscriberBase, DownloadUi>();
			collection.AddScoped<WindowMediatorSubscriberBase, PopoutProfileUi>();
			collection.AddScoped<WindowMediatorSubscriberBase, DataAnalysisUi>();
			collection.AddScoped<WindowMediatorSubscriberBase, JoinSyncshellUI>();
			collection.AddScoped<WindowMediatorSubscriberBase, CreateSyncshellUI>();
			collection.AddScoped<WindowMediatorSubscriberBase, EventViewerUI>();
			collection.AddScoped<WindowMediatorSubscriberBase, CharaDataHubUi>();
			collection.AddScoped<WindowMediatorSubscriberBase, EditProfileUi>((IServiceProvider s) => new EditProfileUi(s.GetRequiredService<ILogger<EditProfileUi>>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<ApiController>(), s.GetRequiredService<UiSharedService>(), s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareProfileManager>(), s.GetRequiredService<PerformanceCollectorService>()));
			collection.AddScoped<WindowMediatorSubscriberBase, PopupHandler>();
			collection.AddScoped<IPopupHandler, BanUserPopupHandler>();
			collection.AddScoped<IPopupHandler, CensusPopupHandler>();
			collection.AddScoped<IPopupHandler, ChangelogPopupHandler>();
			collection.AddScoped<CacheCreationService>();
			collection.AddScoped<PlayerDataFactory>();
			collection.AddScoped<VisibleUserDataDistributor>();
			collection.AddScoped((IServiceProvider s) => new UiService(s.GetRequiredService<ILogger<UiService>>(), pluginInterface.UiBuilder, s.GetRequiredService<MareConfigService>(), s.GetRequiredService<WindowSystem>(), s.GetServices<WindowMediatorSubscriberBase>(), s.GetRequiredService<UiFactory>(), s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareMediator>()));
			collection.AddScoped((IServiceProvider s) => new CommandManagerService(commandManager, s.GetRequiredService<PerformanceCollectorService>(), s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<CacheMonitor>(), s.GetRequiredService<ApiController>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<MareConfigService>()));
			collection.AddScoped((IServiceProvider s) => new UiSharedService(s.GetRequiredService<ILogger<UiSharedService>>(), s.GetRequiredService<IpcManager>(), s.GetRequiredService<ApiController>(), s.GetRequiredService<CacheMonitor>(), s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareConfigService>(), s.GetRequiredService<DalamudUtilService>(), pluginInterface, textureProvider, s.GetRequiredService<Dalamud.Localization>(), s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<TokenProvider>(), s.GetRequiredService<MareMediator>()));
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<ConfigurationSaveService>());
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<MareMediator>());
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<NotificationService>());
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<FileCacheManager>());
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<ConfigurationMigrator>());
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<ChangelogService>());
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<DalamudUtilService>());
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<PerformanceCollectorService>());
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<DtrEntry>());
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<EventAggregator>());
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<IpcProvider>());
			collection.AddHostedService((IServiceProvider p) => p.GetRequiredService<MarePlugin>());
		})
			.Build();
		_host.StartAsync();
	}

	public void Dispose()
	{
		_host.StopAsync().GetAwaiter().GetResult();
		_host.Dispose();
	}
}
