using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;
using XIVSync.API.Data.Enum;
using XIVSync.FileCache;
using XIVSync.Interop.Ipc;
using XIVSync.MareConfiguration;
using XIVSync.MareConfiguration.Configurations;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.Utils;

namespace XIVSync.UI;

public class DataAnalysisUi : WindowMediatorSubscriberBase
{
	private readonly CharacterAnalyzer _characterAnalyzer;

	private readonly Progress<(string, int)> _conversionProgress = new Progress<(string, int)>();

	private readonly IpcManager _ipcManager;

	private readonly UiSharedService _uiSharedService;

	private readonly PlayerPerformanceConfigService _playerPerformanceConfig;

	private readonly TransientResourceManager _transientResourceManager;

	private readonly TransientConfigService _transientConfigService;

	private readonly Dictionary<string, string[]> _texturesToConvert = new Dictionary<string, string[]>(StringComparer.Ordinal);

	private Dictionary<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>>? _cachedAnalysis;

	private CancellationTokenSource _conversionCancellationTokenSource = new CancellationTokenSource();

	private string _conversionCurrentFileName = string.Empty;

	private int _conversionCurrentFileProgress;

	private Task? _conversionTask;

	private bool _enableBc7ConversionMode;

	private bool _hasUpdate;

	private bool _modalOpen;

	private string _selectedFileTypeTab = string.Empty;

	private string _selectedHash = string.Empty;

	private ObjectKind _selectedObjectTab;

	private bool _showModal;

	private CancellationTokenSource _transientRecordCts = new CancellationTokenSource();

	private bool _showAlreadyAddedTransients;

	private bool _acknowledgeReview;

	private string _selectedStoredCharacter = string.Empty;

	private string _selectedJobEntry = string.Empty;

	private readonly List<string> _storedPathsToRemove = new List<string>();

	private readonly Dictionary<string, string> _filePathResolve = new Dictionary<string, string>();

	private string _filterGamePath = string.Empty;

	private string _filterFilePath = string.Empty;

	public DataAnalysisUi(ILogger<DataAnalysisUi> logger, MareMediator mediator, CharacterAnalyzer characterAnalyzer, IpcManager ipcManager, PerformanceCollectorService performanceCollectorService, UiSharedService uiSharedService, PlayerPerformanceConfigService playerPerformanceConfig, TransientResourceManager transientResourceManager, TransientConfigService transientConfigService)
		: base(logger, mediator, "Mare Character Data Analysis", performanceCollectorService)
	{
		_characterAnalyzer = characterAnalyzer;
		_ipcManager = ipcManager;
		_uiSharedService = uiSharedService;
		_playerPerformanceConfig = playerPerformanceConfig;
		_transientResourceManager = transientResourceManager;
		_transientConfigService = transientConfigService;
		base.Mediator.Subscribe<CharacterDataAnalyzedMessage>(this, delegate
		{
			_hasUpdate = true;
		});
		base.SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2
			{
				X = 800f,
				Y = 600f
			},
			MaximumSize = new Vector2
			{
				X = 3840f,
				Y = 2160f
			}
		};
		_conversionProgress.ProgressChanged += ConversionProgress_ProgressChanged;
	}

	protected override void DrawInternal()
	{
		if (_conversionTask != null && !_conversionTask.IsCompleted)
		{
			_showModal = true;
			if (ImGui.BeginPopupModal("BC7 Conversion in Progress"))
			{
				ImGui.TextUnformatted("BC7 Conversion in progress: " + _conversionCurrentFileProgress + "/" + _texturesToConvert.Count);
				UiSharedService.TextWrapped("Current file: " + _conversionCurrentFileName);
				if (_uiSharedService.IconTextButton(FontAwesomeIcon.StopCircle, "Cancel conversion"))
				{
					_conversionCancellationTokenSource.Cancel();
				}
				UiSharedService.SetScaledWindowSize(500f);
				ImGui.EndPopup();
			}
			else
			{
				_modalOpen = false;
			}
		}
		else if (_conversionTask != null && _conversionTask.IsCompleted && _texturesToConvert.Count > 0)
		{
			_conversionTask = null;
			_texturesToConvert.Clear();
			_showModal = false;
			_modalOpen = false;
			_enableBc7ConversionMode = false;
		}
		if (_showModal && !_modalOpen)
		{
			ImGui.OpenPopup("BC7 Conversion in Progress");
			_modalOpen = true;
		}
		if (_hasUpdate)
		{
			_cachedAnalysis = _characterAnalyzer.LastAnalysis.DeepClone();
			_hasUpdate = false;
		}
		using (ImRaii.TabBar("analysisRecordingTabBar"))
		{
			using (ImRaii.IEndObject endObject = ImRaii.TabItem("Analysis"))
			{
				if (endObject)
				{
					using (ImRaii.PushId("analysis"))
					{
						DrawAnalysis();
					}
				}
			}
			using ImRaii.IEndObject tabItem = ImRaii.TabItem("Transient Files");
			if (!ImRaii.IEndObject.op_True(tabItem))
			{
				return;
			}
			using (ImRaii.TabBar("transientData"))
			{
				using (ImRaii.IEndObject transientData = ImRaii.TabItem("Stored Transient File Data"))
				{
					using (ImRaii.PushId("data"))
					{
						if (transientData)
						{
							DrawStoredData();
						}
					}
				}
				using ImRaii.IEndObject transientRecord = ImRaii.TabItem("Record Transient Data");
				using (ImRaii.PushId("recording"))
				{
					if (transientRecord)
					{
						DrawRecording();
					}
				}
			}
		}
	}

	private void DrawStoredData()
	{
		UiSharedService.DrawTree("What is this? (Explanation / Help)", delegate
		{
			UiSharedService.TextWrapped("This tab allows you to see which transient files are attached to your character.");
			UiSharedService.TextWrapped("Transient files are files that cannot be resolved to your character permanently. Mare gathers these files in the background while you execute animations, VFX, sound effects, etc.");
			UiSharedService.TextWrapped("When sending your character data to others, Mare will combine the files listed in \"All Jobs\" and the corresponding currently used job.");
			UiSharedService.TextWrapped("The purpose of this tab is primarily informational for you to see which files you are carrying with you. You can remove added game paths, however if you are using the animations etc. again, Mare will automatically attach these after using them. If you disable associated mods in Penumbra, the associated entries here will also be deleted automatically.");
		});
		ImGuiHelpers.ScaledDummy(5f);
		Dictionary<string, TransientConfig.TransientPlayerConfig> config = _transientConfigService.Current.TransientConfigs;
		Vector2 availableContentRegion = Vector2.Zero;
		using (ImRaii.Group())
		{
			ImGui.TextUnformatted("Character");
			ImGui.Separator();
			ImGuiHelpers.ScaledDummy(3f);
			availableContentRegion = ImGui.GetContentRegionAvail();
			using (ImRaii.ListBox("##characters", new Vector2(200f, availableContentRegion.Y)))
			{
				foreach (KeyValuePair<string, TransientConfig.TransientPlayerConfig> entry in config)
				{
					string[] name = entry.Key.Split("_");
					if (_uiSharedService.WorldData.TryGetValue(ushort.Parse(name[1]), out string worldname) && ImGui.Selectable(name[0] + " (" + worldname + ")", string.Equals(_selectedStoredCharacter, entry.Key, StringComparison.Ordinal)))
					{
						_selectedStoredCharacter = entry.Key;
						_selectedJobEntry = string.Empty;
						_storedPathsToRemove.Clear();
						_filePathResolve.Clear();
						_filterFilePath = string.Empty;
						_filterGamePath = string.Empty;
					}
				}
			}
		}
		ImGui.SameLine();
		TransientConfig.TransientPlayerConfig transientStorage;
		bool selectedData = config.TryGetValue(_selectedStoredCharacter, out transientStorage) && transientStorage != null;
		using (ImRaii.Group())
		{
			ImGui.TextUnformatted("Job");
			ImGui.Separator();
			ImGuiHelpers.ScaledDummy(3f);
			using (ImRaii.ListBox("##data", new Vector2(150f, availableContentRegion.Y)))
			{
				if (selectedData)
				{
					if (ImGui.Selectable("All Jobs", string.Equals(_selectedJobEntry, "alljobs", StringComparison.Ordinal)))
					{
						_selectedJobEntry = "alljobs";
					}
					foreach (KeyValuePair<uint, List<string>> job in transientStorage.JobSpecificCache)
					{
						if (_uiSharedService.JobData.TryGetValue(job.Key, out string jobName) && ImGui.Selectable(jobName, string.Equals(_selectedJobEntry, job.Key.ToString(), StringComparison.Ordinal)))
						{
							_selectedJobEntry = job.Key.ToString();
							_storedPathsToRemove.Clear();
							_filePathResolve.Clear();
							_filterFilePath = string.Empty;
							_filterGamePath = string.Empty;
						}
					}
				}
			}
		}
		ImGui.SameLine();
		using (ImRaii.Group())
		{
			List<string> selectedList = (string.Equals(_selectedJobEntry, "alljobs", StringComparison.Ordinal) ? config[_selectedStoredCharacter].GlobalPersistentCache : (string.IsNullOrEmpty(_selectedJobEntry) ? new List<string>() : config[_selectedStoredCharacter].JobSpecificCache[uint.Parse(_selectedJobEntry)]));
			ImU8String text = new ImU8String(30, 1);
			text.AppendLiteral("Attached Files (Total Files: ");
			text.AppendFormatted(selectedList.Count);
			text.AppendLiteral(")");
			ImGui.TextUnformatted(text);
			ImGui.Separator();
			ImGuiHelpers.ScaledDummy(3f);
			using (ImRaii.Disabled(string.IsNullOrEmpty(_selectedJobEntry)))
			{
				float restContent = availableContentRegion.X - ImGui.GetCursorPosX();
				using (ImRaii.Group())
				{
					if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Resolve Game Paths to used File Paths"))
					{
						Task.Run(async delegate
						{
							string[] paths = selectedList.ToArray();
							(string[] forward, string[][] reverse) resolved = await _ipcManager.Penumbra.ResolvePathsAsync(paths, Array.Empty<string>()).ConfigureAwait(continueOnCapturedContext: false);
							_filePathResolve.Clear();
							for (int i = 0; i < resolved.forward.Length; i++)
							{
								_filePathResolve[paths[i]] = resolved.forward[i];
							}
						});
					}
					ImGui.SameLine();
					ImGuiHelpers.ScaledDummy(20f, 1f);
					ImGui.SameLine();
					using (ImRaii.Disabled(!_storedPathsToRemove.Any()))
					{
						if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Remove selected Game Paths"))
						{
							foreach (string item in _storedPathsToRemove)
							{
								selectedList.Remove(item);
							}
							_transientConfigService.Save();
							_transientResourceManager.RebuildSemiTransientResources();
							_filterFilePath = string.Empty;
							_filterGamePath = string.Empty;
						}
					}
					ImGui.SameLine();
					using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
					{
						if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear ALL Game Paths"))
						{
							selectedList.Clear();
							_transientConfigService.Save();
							_transientResourceManager.RebuildSemiTransientResources();
							_filterFilePath = string.Empty;
							_filterGamePath = string.Empty;
						}
					}
					UiSharedService.AttachToolTip("Hold CTRL to delete all game paths from the displayed list--SEP--You usually do not need to do this. All animation and VFX data will be automatically handled through Mare.");
					ImGuiHelpers.ScaledDummy(5f);
					ImGuiHelpers.ScaledDummy(30f);
					ImGui.SameLine();
					ImGui.SetNextItemWidth((restContent - 30f) / 2f);
					ImGui.InputTextWithHint("##filterGamePath", "Filter by Game Path", ref _filterGamePath, 255);
					ImGui.SameLine();
					ImGui.SetNextItemWidth((restContent - 30f) / 2f);
					ImGui.InputTextWithHint("##filterFilePath", "Filter by File Path", ref _filterFilePath, 255);
					using ImRaii.IEndObject dataTable = ImRaii.Table("##table", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY);
					if (!ImRaii.IEndObject.op_True(dataTable))
					{
						return;
					}
					ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 30f);
					ImGui.TableSetupColumn("Game Path", ImGuiTableColumnFlags.WidthFixed, (restContent - 30f) / 2f);
					ImGui.TableSetupColumn("File Path", ImGuiTableColumnFlags.WidthFixed, (restContent - 30f) / 2f);
					ImGui.TableSetupScrollFreeze(0, 1);
					ImGui.TableHeadersRow();
					int id = 0;
					foreach (string entry in selectedList)
					{
						if (!string.IsNullOrWhiteSpace(_filterGamePath) && !entry.Contains(_filterGamePath, StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}
						string filePath;
						bool hasFileResolve = _filePathResolve.TryGetValue(entry, out filePath);
						if (hasFileResolve && !string.IsNullOrEmpty(_filterFilePath) && !filePath.Contains(_filterFilePath, StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}
						using (ImRaii.PushId(id++))
						{
							ImGui.TableNextColumn();
							bool isSelected = _storedPathsToRemove.Contains<string>(entry, StringComparer.Ordinal);
							if (ImGui.Checkbox("##", ref isSelected))
							{
								if (isSelected)
								{
									_storedPathsToRemove.Add(entry);
								}
								else
								{
									_storedPathsToRemove.Remove(entry);
								}
							}
							ImGui.TableNextColumn();
							ImGui.TextUnformatted(entry);
							UiSharedService.AttachToolTip(entry + "--SEP--Click to copy to clipboard");
							if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
							{
								ImGui.SetClipboardText(entry);
							}
							ImGui.TableNextColumn();
							if (hasFileResolve)
							{
								ImGui.TextUnformatted(filePath ?? "Unk");
								UiSharedService.AttachToolTip(filePath ?? "Unk--SEP--Click to copy to clipboard");
								if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
								{
									ImGui.SetClipboardText(filePath);
								}
							}
							else
							{
								ImGui.TextUnformatted("-");
								UiSharedService.AttachToolTip("Resolve Game Paths to used File Paths to display the associated file paths.");
							}
						}
					}
				}
			}
		}
	}

	private void DrawRecording()
	{
		UiSharedService.DrawTree("What is this? (Explanation / Help)", delegate
		{
			UiSharedService.TextWrapped("This tab allows you to attempt to fix mods that do not sync correctly, especially those with modded models and animations." + Environment.NewLine + Environment.NewLine + "To use this, start the recording, execute one or multiple emotes/animations you want to attempt to fix and check if new data appears in the table below." + Environment.NewLine + "If it doesn't, Mare is not able to catch the data or already has recorded the animation files (check 'Show previously added transient files' to see if not all is already present)." + Environment.NewLine + Environment.NewLine + "For most animations, vfx, etc. it is enough to just run them once unless they have random variations. Longer animations do not require to play out in their entirety to be captured.");
			ImGuiHelpers.ScaledDummy(5f);
			UiSharedService.DrawGroupedCenteredColorText("Important Note: If you need to fix an animation that should apply across multiple jobs, you need to repeat this process with at least one additional job, otherwise the animation will only be fixed for the currently active job. This goes primarily for emotes that are used across multiple jobs.", ImGuiColors.DalamudYellow, 800f);
			ImGuiHelpers.ScaledDummy(5f);
			UiSharedService.DrawGroupedCenteredColorText("WARNING: WHILE RECORDING TRANSIENT DATA, DO NOT CHANGE YOUR APPEARANCE, ENABLED MODS OR ANYTHING. JUST DO THE ANIMATION(S) OR WHATEVER YOU NEED DOING AND STOP THE RECORDING.", ImGuiColors.DalamudRed, 800f);
			ImGuiHelpers.ScaledDummy(5f);
		});
		using (ImRaii.Disabled(_transientResourceManager.IsTransientRecording))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Play, "Start Transient Recording"))
			{
				_transientRecordCts.Cancel();
				_transientRecordCts.Dispose();
				_transientRecordCts = new CancellationTokenSource();
				_transientResourceManager.StartRecording(_transientRecordCts.Token);
				_acknowledgeReview = false;
			}
		}
		ImGui.SameLine();
		using (ImRaii.Disabled(!_transientResourceManager.IsTransientRecording))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Stop, "Stop Transient Recording"))
			{
				_transientRecordCts.Cancel();
			}
		}
		if (_transientResourceManager.IsTransientRecording)
		{
			ImGui.SameLine();
			UiSharedService.ColorText($"RECORDING - Time Remaining: {_transientResourceManager.RecordTimeRemaining.Value}", ImGuiColors.DalamudYellow);
			ImGuiHelpers.ScaledDummy(5f);
			UiSharedService.DrawGroupedCenteredColorText("DO NOT CHANGE YOUR APPEARANCE OR MODS WHILE RECORDING, YOU CAN ACCIDENTALLY MAKE SOME OF YOUR APPEARANCE RELATED MODS PERMANENT.", ImGuiColors.DalamudRed, 800f);
		}
		ImGuiHelpers.ScaledDummy(5f);
		ImGui.Checkbox("Show previously added transient files in the recording", ref _showAlreadyAddedTransients);
		_uiSharedService.DrawHelpText("Use this only if you want to see what was previously already caught by Mare");
		ImGuiHelpers.ScaledDummy(5f);
		using (ImRaii.Disabled(_transientResourceManager.IsTransientRecording || _transientResourceManager.RecordedTransients.All((TransientResourceManager.TransientRecord k) => !k.AddTransient) || !_acknowledgeReview))
		{
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save Recorded Transient Data"))
			{
				_transientResourceManager.SaveRecording();
				_acknowledgeReview = false;
			}
		}
		ImGui.SameLine();
		ImGui.Checkbox("I acknowledge I have reviewed the recorded data", ref _acknowledgeReview);
		if (_transientResourceManager.RecordedTransients.Any((TransientResourceManager.TransientRecord k) => !k.AlreadyTransient))
		{
			ImGuiHelpers.ScaledDummy(5f);
			UiSharedService.DrawGroupedCenteredColorText("Please review the recorded mod files before saving and deselect files that got into the recording on accident.", ImGuiColors.DalamudYellow);
			ImGuiHelpers.ScaledDummy(5f);
		}
		ImGuiHelpers.ScaledDummy(5f);
		Vector2 width = ImGui.GetContentRegionAvail();
		using ImRaii.IEndObject table = ImRaii.Table("Recorded Transients", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY);
		if (!ImRaii.IEndObject.op_True(table))
		{
			return;
		}
		int id = 0;
		ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 30f);
		ImGui.TableSetupColumn("Owner", ImGuiTableColumnFlags.WidthFixed, 100f);
		ImGui.TableSetupColumn("Game Path", ImGuiTableColumnFlags.WidthFixed, (width.X - 30f - 100f) / 2f);
		ImGui.TableSetupColumn("File Path", ImGuiTableColumnFlags.WidthFixed, (width.X - 30f - 100f) / 2f);
		ImGui.TableSetupScrollFreeze(0, 1);
		ImGui.TableHeadersRow();
		List<TransientResourceManager.TransientRecord> list = _transientResourceManager.RecordedTransients.ToList();
		list.Reverse();
		foreach (TransientResourceManager.TransientRecord value in list)
		{
			if (value.AlreadyTransient && !_showAlreadyAddedTransients)
			{
				continue;
			}
			using (ImRaii.PushId(id++))
			{
				if (value.AlreadyTransient)
				{
					ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
				}
				ImGui.TableNextColumn();
				bool addTransient = value.AddTransient;
				if (ImGui.Checkbox("##add", ref addTransient))
				{
					value.AddTransient = addTransient;
				}
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(value.Owner.Name);
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(value.GamePath);
				UiSharedService.AttachToolTip(value.GamePath);
				ImGui.TableNextColumn();
				ImGui.TextUnformatted(value.FilePath);
				UiSharedService.AttachToolTip(value.FilePath);
				if (value.AlreadyTransient)
				{
					ImGui.PopStyleColor();
				}
			}
		}
	}

	private void DrawAnalysis()
	{
		UiSharedService.DrawTree("What is this? (Explanation / Help)", delegate
		{
			UiSharedService.TextWrapped("This tab shows you all files and their sizes that are currently in use through your character and associated entities in Mare");
		});
		if (_cachedAnalysis.Count == 0)
		{
			return;
		}
		if (_characterAnalyzer.IsAnalysisRunning)
		{
			UiSharedService.ColorTextWrapped($"Analyzing {_characterAnalyzer.CurrentFile}/{_characterAnalyzer.TotalFiles}", ImGuiColors.DalamudYellow);
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.StopCircle, "Cancel analysis"))
			{
				_characterAnalyzer.CancelAnalyze();
			}
		}
		else if (_cachedAnalysis.Any<KeyValuePair<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>>>((KeyValuePair<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>> c) => c.Value.Any((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> f) => !f.Value.IsComputed)))
		{
			UiSharedService.ColorTextWrapped("Some entries in the analysis have file size not determined yet, press the button below to analyze your current data", ImGuiColors.DalamudYellow);
			if (_uiSharedService.IconTextButton(FontAwesomeIcon.PlayCircle, "Start analysis (missing entries)"))
			{
				_characterAnalyzer.ComputeAnalysis(print: false);
			}
		}
		else if (_uiSharedService.IconTextButton(FontAwesomeIcon.PlayCircle, "Start analysis (recalculate all entries)"))
		{
			_characterAnalyzer.ComputeAnalysis(print: false, recalculate: true);
		}
		ImGui.Separator();
		ImGui.TextUnformatted("Total files:");
		ImGui.SameLine();
		ImGui.TextUnformatted(_cachedAnalysis.Values.Sum((Dictionary<string, CharacterAnalyzer.FileDataEntry> c) => c.Values.Count).ToString());
		ImGui.SameLine();
		using (ImRaii.PushFont(UiBuilder.IconFont))
		{
			ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
		}
		if (ImGui.IsItemHovered())
		{
			IEnumerable<IGrouping<string, CharacterAnalyzer.FileDataEntry>> groupedfiles = _cachedAnalysis.Values.SelectMany((Dictionary<string, CharacterAnalyzer.FileDataEntry> f) => f.Values).GroupBy<CharacterAnalyzer.FileDataEntry, string>((CharacterAnalyzer.FileDataEntry f) => f.FileType, StringComparer.Ordinal);
			ImGui.SetTooltip(string.Join(Environment.NewLine, from f in groupedfiles.OrderBy<IGrouping<string, CharacterAnalyzer.FileDataEntry>, string>((IGrouping<string, CharacterAnalyzer.FileDataEntry> f) => f.Key, StringComparer.Ordinal)
				select f.Key + ": " + f.Count() + " files, size: " + UiSharedService.ByteToString(f.Sum((CharacterAnalyzer.FileDataEntry v) => v.OriginalSize)) + ", compressed: " + UiSharedService.ByteToString(f.Sum((CharacterAnalyzer.FileDataEntry v) => v.CompressedSize))));
		}
		ImGui.TextUnformatted("Total size (actual):");
		ImGui.SameLine();
		ImGui.TextUnformatted(UiSharedService.ByteToString(_cachedAnalysis.Sum<KeyValuePair<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>>>((KeyValuePair<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>> c) => c.Value.Sum((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> c) => c.Value.OriginalSize))));
		ImGui.TextUnformatted("Total size (compressed for up/download only):");
		ImGui.SameLine();
		ImGui.TextUnformatted(UiSharedService.ByteToString(_cachedAnalysis.Sum<KeyValuePair<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>>>((KeyValuePair<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>> c) => c.Value.Sum((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> c) => c.Value.CompressedSize))));
		ImU8String text = new ImU8String(30, 1);
		text.AppendLiteral("Total modded model triangles: ");
		text.AppendFormatted(_cachedAnalysis.Sum<KeyValuePair<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>>>((KeyValuePair<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>> c) => c.Value.Sum((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> f) => f.Value.Triangles)));
		ImGui.TextUnformatted(text);
		ImGui.Separator();
		using (ImRaii.TabBar("objectSelection"))
		{
			foreach (KeyValuePair<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>> kvp in _cachedAnalysis)
			{
				using (ImRaii.PushId(kvp.Key.ToString()))
				{
					string tabText = kvp.Key.ToString();
					if (kvp.Value.Any((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> f) => !f.Value.IsComputed))
					{
						tabText += " (!)";
					}
					using ImRaii.IEndObject tab = ImRaii.TabItem(tabText + "###" + kvp.Key);
					if (!tab.Success)
					{
						continue;
					}
					List<IGrouping<string, CharacterAnalyzer.FileDataEntry>> groupedfiles = kvp.Value.Select((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> v) => v.Value).GroupBy<CharacterAnalyzer.FileDataEntry, string>((CharacterAnalyzer.FileDataEntry f) => f.FileType, StringComparer.Ordinal).OrderBy<IGrouping<string, CharacterAnalyzer.FileDataEntry>, string>((IGrouping<string, CharacterAnalyzer.FileDataEntry> k) => k.Key, StringComparer.Ordinal)
						.ToList();
					ImGui.TextUnformatted("Files for " + kvp.Key);
					ImGui.SameLine();
					ImGui.TextUnformatted(kvp.Value.Count.ToString());
					ImGui.SameLine();
					using (ImRaii.PushFont(UiBuilder.IconFont))
					{
						ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
					}
					if (ImGui.IsItemHovered())
					{
						ImGui.SetTooltip(string.Join(Environment.NewLine, groupedfiles.Select((IGrouping<string, CharacterAnalyzer.FileDataEntry> f) => f.Key + ": " + f.Count() + " files, size: " + UiSharedService.ByteToString(f.Sum((CharacterAnalyzer.FileDataEntry v) => v.OriginalSize)) + ", compressed: " + UiSharedService.ByteToString(f.Sum((CharacterAnalyzer.FileDataEntry v) => v.CompressedSize)))));
					}
					text = new ImU8String(15, 1);
					text.AppendFormatted(kvp.Key);
					text.AppendLiteral(" size (actual):");
					ImGui.TextUnformatted(text);
					ImGui.SameLine();
					ImGui.TextUnformatted(UiSharedService.ByteToString(kvp.Value.Sum((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> c) => c.Value.OriginalSize)));
					text = new ImU8String(40, 1);
					text.AppendFormatted(kvp.Key);
					text.AppendLiteral(" size (compressed for up/download only):");
					ImGui.TextUnformatted(text);
					ImGui.SameLine();
					ImGui.TextUnformatted(UiSharedService.ByteToString(kvp.Value.Sum((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> c) => c.Value.CompressedSize)));
					ImGui.Separator();
					IGrouping<string, CharacterAnalyzer.FileDataEntry> vramUsage = groupedfiles.SingleOrDefault((IGrouping<string, CharacterAnalyzer.FileDataEntry> v) => string.Equals(v.Key, "tex", StringComparison.Ordinal));
					if (vramUsage != null)
					{
						long actualVramUsage = vramUsage.Sum((CharacterAnalyzer.FileDataEntry f) => f.OriginalSize);
						text = new ImU8String(12, 1);
						text.AppendFormatted(kvp.Key);
						text.AppendLiteral(" VRAM usage:");
						ImGui.TextUnformatted(text);
						ImGui.SameLine();
						ImGui.TextUnformatted(UiSharedService.ByteToString(actualVramUsage));
						if (_playerPerformanceConfig.Current.WarnOnExceedingThresholds || _playerPerformanceConfig.Current.ShowPerformanceIndicator)
						{
							using (ImRaii.PushIndent(10f))
							{
								int currentVramWarning = _playerPerformanceConfig.Current.VRAMSizeWarningThresholdMiB;
								text = new ImU8String(40, 1);
								text.AppendLiteral("Configured VRAM warning threshold: ");
								text.AppendFormatted(currentVramWarning);
								text.AppendLiteral(" MiB.");
								ImGui.TextUnformatted(text);
								if (currentVramWarning * 1024 * 1024 < actualVramUsage)
								{
									UiSharedService.ColorText("You exceed your own threshold by " + UiSharedService.ByteToString(actualVramUsage - currentVramWarning * 1024 * 1024) + ".", ImGuiColors.DalamudYellow);
								}
							}
						}
					}
					long actualTriCount = kvp.Value.Sum((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> f) => f.Value.Triangles);
					text = new ImU8String(25, 2);
					text.AppendFormatted(kvp.Key);
					text.AppendLiteral(" modded model triangles: ");
					text.AppendFormatted(actualTriCount);
					ImGui.TextUnformatted(text);
					if (_playerPerformanceConfig.Current.WarnOnExceedingThresholds || _playerPerformanceConfig.Current.ShowPerformanceIndicator)
					{
						using (ImRaii.PushIndent(10f))
						{
							int currentTriWarning = _playerPerformanceConfig.Current.TrisWarningThresholdThousands;
							text = new ImU8String(50, 1);
							text.AppendLiteral("Configured triangle warning threshold: ");
							text.AppendFormatted(currentTriWarning * 1000);
							text.AppendLiteral(" triangles.");
							ImGui.TextUnformatted(text);
							if (currentTriWarning * 1000 < actualTriCount)
							{
								UiSharedService.ColorText($"You exceed your own threshold by {actualTriCount - currentTriWarning * 1000} triangles.", ImGuiColors.DalamudYellow);
							}
						}
					}
					ImGui.Separator();
					if (_selectedObjectTab != kvp.Key)
					{
						_selectedHash = string.Empty;
						_selectedObjectTab = kvp.Key;
						_selectedFileTypeTab = string.Empty;
						_enableBc7ConversionMode = false;
						_texturesToConvert.Clear();
					}
					using (ImRaii.TabBar("fileTabs"))
					{
						foreach (IGrouping<string, CharacterAnalyzer.FileDataEntry> fileGroup in groupedfiles)
						{
							string fileGroupText = fileGroup.Key + " [" + fileGroup.Count() + "]";
							bool requiresCompute = fileGroup.Any((CharacterAnalyzer.FileDataEntry k) => !k.IsComputed);
							using (ImRaii.PushColor(ImGuiCol.Tab, UiSharedService.Color(ImGuiColors.DalamudYellow), requiresCompute))
							{
								if (requiresCompute)
								{
									fileGroupText += " (!)";
								}
								ImRaii.IEndObject fileTab;
								using (ImRaii.PushColor(ImGuiCol.Text, UiSharedService.Color(new Vector4(0f, 0f, 0f, 1f)), requiresCompute && !string.Equals(_selectedFileTypeTab, fileGroup.Key, StringComparison.Ordinal)))
								{
									fileTab = ImRaii.TabItem(fileGroupText + "###" + fileGroup.Key);
								}
								if (!fileTab)
								{
									fileTab.Dispose();
									continue;
								}
								if (!string.Equals(fileGroup.Key, _selectedFileTypeTab, StringComparison.Ordinal))
								{
									_selectedFileTypeTab = fileGroup.Key;
									_selectedHash = string.Empty;
									_enableBc7ConversionMode = false;
									_texturesToConvert.Clear();
								}
								text = new ImU8String(6, 1);
								text.AppendFormatted(fileGroup.Key);
								text.AppendLiteral(" files");
								ImGui.TextUnformatted(text);
								ImGui.SameLine();
								ImGui.TextUnformatted(fileGroup.Count().ToString());
								text = new ImU8String(21, 1);
								text.AppendFormatted(fileGroup.Key);
								text.AppendLiteral(" files size (actual):");
								ImGui.TextUnformatted(text);
								ImGui.SameLine();
								ImGui.TextUnformatted(UiSharedService.ByteToString(fileGroup.Sum((CharacterAnalyzer.FileDataEntry c) => c.OriginalSize)));
								text = new ImU8String(46, 1);
								text.AppendFormatted(fileGroup.Key);
								text.AppendLiteral(" files size (compressed for up/download only):");
								ImGui.TextUnformatted(text);
								ImGui.SameLine();
								ImGui.TextUnformatted(UiSharedService.ByteToString(fileGroup.Sum((CharacterAnalyzer.FileDataEntry c) => c.CompressedSize)));
								if (string.Equals(_selectedFileTypeTab, "tex", StringComparison.Ordinal))
								{
									ImGui.Checkbox("Enable BC7 Conversion Mode", ref _enableBc7ConversionMode);
									if (_enableBc7ConversionMode)
									{
										UiSharedService.ColorText("WARNING BC7 CONVERSION:", ImGuiColors.DalamudYellow);
										ImGui.SameLine();
										UiSharedService.ColorText("Converting textures to BC7 is irreversible!", ImGuiColors.DalamudRed);
										UiSharedService.ColorTextWrapped("- Converting textures to BC7 will reduce their size (compressed and uncompressed) drastically. It is recommended to be used for large (4k+) textures." + Environment.NewLine + "- Some textures, especially ones utilizing colorsets, might not be suited for BC7 conversion and might produce visual artifacts." + Environment.NewLine + "- Before converting textures, make sure to have the original files of the mod you are converting so you can reimport it in case of issues." + Environment.NewLine + "- Conversion will convert all found texture duplicates (entries with more than 1 file path) automatically." + Environment.NewLine + "- Converting textures to BC7 is a very expensive operation and, depending on the amount of textures to convert, will take a while to complete.", ImGuiColors.DalamudYellow);
										if (_texturesToConvert.Count > 0 && _uiSharedService.IconTextButton(FontAwesomeIcon.PlayCircle, "Start conversion of " + _texturesToConvert.Count + " texture(s)"))
										{
											_conversionCancellationTokenSource = _conversionCancellationTokenSource.CancelRecreate();
											_conversionTask = _ipcManager.Penumbra.ConvertTextureFiles(_logger, _texturesToConvert, _conversionProgress, _conversionCancellationTokenSource.Token);
										}
									}
								}
								ImGui.Separator();
								DrawTable(fileGroup);
								fileTab.Dispose();
							}
						}
					}
				}
			}
			ImGui.Separator();
			ImGui.TextUnformatted("Selected file:");
			ImGui.SameLine();
			UiSharedService.ColorText(_selectedHash, ImGuiColors.DalamudYellow);
			if (_cachedAnalysis[_selectedObjectTab].TryGetValue(_selectedHash, out CharacterAnalyzer.FileDataEntry item))
			{
				List<string> filePaths = item.FilePaths;
				ImGui.TextUnformatted("Local file path:");
				ImGui.SameLine();
				UiSharedService.TextWrapped(filePaths[0]);
				if (filePaths.Count > 1)
				{
					ImGui.SameLine();
					text = new ImU8String(11, 1);
					text.AppendLiteral("(and ");
					text.AppendFormatted(filePaths.Count - 1);
					text.AppendLiteral(" more)");
					ImGui.TextUnformatted(text);
					ImGui.SameLine();
					_uiSharedService.IconText(FontAwesomeIcon.InfoCircle);
					UiSharedService.AttachToolTip(string.Join(Environment.NewLine, filePaths.Skip(1)));
				}
				List<string> gamepaths = item.GamePaths;
				ImGui.TextUnformatted("Used by game path:");
				ImGui.SameLine();
				UiSharedService.TextWrapped(gamepaths[0]);
				if (gamepaths.Count > 1)
				{
					ImGui.SameLine();
					text = new ImU8String(11, 1);
					text.AppendLiteral("(and ");
					text.AppendFormatted(gamepaths.Count - 1);
					text.AppendLiteral(" more)");
					ImGui.TextUnformatted(text);
					ImGui.SameLine();
					_uiSharedService.IconText(FontAwesomeIcon.InfoCircle);
					UiSharedService.AttachToolTip(string.Join(Environment.NewLine, gamepaths.Skip(1)));
				}
			}
		}
	}

	public override void OnOpen()
	{
		_hasUpdate = true;
		_selectedHash = string.Empty;
		_enableBc7ConversionMode = false;
		_texturesToConvert.Clear();
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		_conversionCancellationTokenSource?.CancelDispose();
		_conversionProgress.ProgressChanged -= ConversionProgress_ProgressChanged;
	}

	private void ConversionProgress_ProgressChanged(object? sender, (string, int) e)
	{
		(_conversionCurrentFileName, _conversionCurrentFileProgress) = e;
	}

	private void DrawTable(IGrouping<string, CharacterAnalyzer.FileDataEntry> fileGroup)
	{
		int tableColumns = ((!string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal)) ? (string.Equals(fileGroup.Key, "mdl", StringComparison.Ordinal) ? 6 : 5) : (_enableBc7ConversionMode ? 7 : 6));
		using ImRaii.IEndObject table = ImRaii.Table("Analysis", tableColumns, ImGuiTableFlags.Sortable | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY, new Vector2(0f, 300f));
		if (!table.Success)
		{
			return;
		}
		ImGui.TableSetupColumn("Hash");
		ImGui.TableSetupColumn("Filepaths");
		ImGui.TableSetupColumn("Gamepaths");
		ImGui.TableSetupColumn("Original Size");
		ImGui.TableSetupColumn("Compressed Size");
		if (string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal))
		{
			ImGui.TableSetupColumn("Format");
			if (_enableBc7ConversionMode)
			{
				ImGui.TableSetupColumn("Convert to BC7");
			}
		}
		if (string.Equals(fileGroup.Key, "mdl", StringComparison.Ordinal))
		{
			ImGui.TableSetupColumn("Triangles");
		}
		ImGui.TableSetupScrollFreeze(0, 1);
		ImGui.TableHeadersRow();
		ImGuiTableSortSpecsPtr sortSpecs = ImGui.TableGetSortSpecs();
		if (sortSpecs.SpecsDirty)
		{
			short idx = sortSpecs.Specs.ColumnIndex;
			if (idx == 0 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Key, StringComparer.Ordinal).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (idx == 0 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Key, StringComparer.Ordinal).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (idx == 1 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, int>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.FilePaths.Count).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (idx == 1 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, int>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.FilePaths.Count).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (idx == 2 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, int>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.GamePaths.Count).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (idx == 2 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, int>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.GamePaths.Count).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (idx == 3 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, long>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.OriginalSize).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (idx == 3 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, long>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.OriginalSize).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (idx == 4 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, long>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.CompressedSize).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (idx == 4 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, long>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.CompressedSize).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (string.Equals(fileGroup.Key, "mdl", StringComparison.Ordinal) && idx == 5 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, long>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.Triangles).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (string.Equals(fileGroup.Key, "mdl", StringComparison.Ordinal) && idx == 5 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, long>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.Triangles).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal) && idx == 5 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.Format.Value, StringComparer.Ordinal).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			if (string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal) && idx == 5 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
			{
				_cachedAnalysis[_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> k) => k.Value.Format.Value, StringComparer.Ordinal).ToDictionary<KeyValuePair<string, CharacterAnalyzer.FileDataEntry>, string, CharacterAnalyzer.FileDataEntry>((KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Key, (KeyValuePair<string, CharacterAnalyzer.FileDataEntry> d) => d.Value, StringComparer.Ordinal);
			}
			sortSpecs.SpecsDirty = false;
		}
		foreach (CharacterAnalyzer.FileDataEntry item in fileGroup)
		{
			using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0f, 0f, 0f, 1f), string.Equals(item.Hash, _selectedHash, StringComparison.Ordinal)))
			{
				using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f), !item.IsComputed))
				{
					ImGui.TableNextColumn();
					if (!item.IsComputed)
					{
						ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, UiSharedService.Color(ImGuiColors.DalamudRed));
						ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, UiSharedService.Color(ImGuiColors.DalamudRed));
					}
					if (string.Equals(_selectedHash, item.Hash, StringComparison.Ordinal))
					{
						ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, UiSharedService.Color(ImGuiColors.DalamudYellow));
						ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, UiSharedService.Color(ImGuiColors.DalamudYellow));
					}
					ImGui.TextUnformatted(item.Hash);
					if (ImGui.IsItemClicked())
					{
						_selectedHash = item.Hash;
					}
					ImGui.TableNextColumn();
					ImGui.TextUnformatted(item.FilePaths.Count.ToString());
					if (ImGui.IsItemClicked())
					{
						_selectedHash = item.Hash;
					}
					ImGui.TableNextColumn();
					ImGui.TextUnformatted(item.GamePaths.Count.ToString());
					if (ImGui.IsItemClicked())
					{
						_selectedHash = item.Hash;
					}
					ImGui.TableNextColumn();
					ImGui.TextUnformatted(UiSharedService.ByteToString(item.OriginalSize));
					if (ImGui.IsItemClicked())
					{
						_selectedHash = item.Hash;
					}
					ImGui.TableNextColumn();
					ImGui.TextUnformatted(UiSharedService.ByteToString(item.CompressedSize));
					if (ImGui.IsItemClicked())
					{
						_selectedHash = item.Hash;
					}
					if (!string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal))
					{
						goto IL_0df3;
					}
					ImGui.TableNextColumn();
					ImGui.TextUnformatted(item.Format.Value);
					if (ImGui.IsItemClicked())
					{
						_selectedHash = item.Hash;
					}
					if (!_enableBc7ConversionMode)
					{
						goto IL_0df3;
					}
					ImGui.TableNextColumn();
					if (string.Equals(item.Format.Value, "BC7", StringComparison.Ordinal))
					{
						ImGui.TextUnformatted("");
						continue;
					}
					string filePath = item.FilePaths[0];
					bool toConvert = _texturesToConvert.ContainsKey(filePath);
					if (ImGui.Checkbox("###convert" + item.Hash, ref toConvert))
					{
						if (toConvert && !_texturesToConvert.ContainsKey(filePath))
						{
							_texturesToConvert[filePath] = item.FilePaths.Skip(1).ToArray();
						}
						else if (!toConvert && _texturesToConvert.ContainsKey(filePath))
						{
							_texturesToConvert.Remove(filePath);
						}
					}
					goto IL_0df3;
					IL_0df3:
					if (string.Equals(fileGroup.Key, "mdl", StringComparison.Ordinal))
					{
						ImGui.TableNextColumn();
						ImGui.TextUnformatted(item.Triangles.ToString());
						if (ImGui.IsItemClicked())
						{
							_selectedHash = item.Hash;
						}
					}
				}
			}
		}
	}
}
