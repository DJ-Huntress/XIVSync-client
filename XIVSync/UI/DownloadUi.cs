using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;
using XIVSync.MareConfiguration;
using XIVSync.PlayerData.Handlers;
using XIVSync.Services;
using XIVSync.Services.Mediator;
using XIVSync.WebAPI.Files;
using XIVSync.WebAPI.Files.Models;

namespace XIVSync.UI;

public class DownloadUi : WindowMediatorSubscriberBase
{
	private readonly MareConfigService _configService;

	private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>>();

	private readonly DalamudUtilService _dalamudUtilService;

	private readonly FileUploadManager _fileTransferManager;

	private readonly UiSharedService _uiShared;

	private readonly ConcurrentDictionary<GameObjectHandler, bool> _uploadingPlayers = new ConcurrentDictionary<GameObjectHandler, bool>();

	public DownloadUi(ILogger<DownloadUi> logger, DalamudUtilService dalamudUtilService, MareConfigService configService, FileUploadManager fileTransferManager, MareMediator mediator, UiSharedService uiShared, PerformanceCollectorService performanceCollectorService)
		: base(logger, mediator, "XIVSync Downloads", performanceCollectorService)
	{
		_dalamudUtilService = dalamudUtilService;
		_configService = configService;
		_fileTransferManager = fileTransferManager;
		_uiShared = uiShared;
		base.SizeConstraints = new WindowSizeConstraints
		{
			MaximumSize = new Vector2(500f, 90f),
			MinimumSize = new Vector2(500f, 90f)
		};
		base.Flags |= ImGuiWindowFlags.NoMove;
		base.Flags |= ImGuiWindowFlags.NoBackground;
		base.Flags |= ImGuiWindowFlags.NoInputs;
		base.Flags |= ImGuiWindowFlags.NoNavFocus;
		base.Flags |= ImGuiWindowFlags.NoResize;
		base.Flags |= ImGuiWindowFlags.NoScrollbar;
		base.Flags |= ImGuiWindowFlags.NoTitleBar;
		base.Flags |= ImGuiWindowFlags.NoDecoration;
		base.Flags |= ImGuiWindowFlags.NoFocusOnAppearing;
		base.DisableWindowSounds = true;
		base.ForceMainWindow = true;
		base.IsOpen = true;
		base.Mediator.Subscribe(this, delegate(DownloadStartedMessage msg)
		{
			_currentDownloads[msg.DownloadId] = msg.DownloadStatus;
		});
		base.Mediator.Subscribe(this, delegate(DownloadFinishedMessage msg)
		{
			_currentDownloads.TryRemove(msg.DownloadId, out Dictionary<string, FileDownloadStatus> _);
		});
		base.Mediator.Subscribe<GposeStartMessage>(this, delegate
		{
			base.IsOpen = false;
		});
		base.Mediator.Subscribe<GposeEndMessage>(this, delegate
		{
			base.IsOpen = true;
		});
		base.Mediator.Subscribe(this, delegate(PlayerUploadingMessage msg)
		{
			if (msg.IsUploading)
			{
				_uploadingPlayers[msg.Handler] = true;
			}
			else
			{
				_uploadingPlayers.TryRemove(msg.Handler, out var _);
			}
		});
	}

	protected override void DrawInternal()
	{
		if (_configService.Current.ShowTransferWindow)
		{
			try
			{
				if (_fileTransferManager.CurrentUploads.Any())
				{
					List<FileTransfer> list = _fileTransferManager.CurrentUploads.ToList();
					int totalUploads = list.Count;
					int doneUploads = list.Count((FileTransfer c) => c.IsTransferred);
					long totalUploaded = list.Sum((FileTransfer c) => c.Transferred);
					long totalToUpload = list.Sum((FileTransfer c) => c.Total);
					UiSharedService.DrawOutlinedFont("▲", ImGuiColors.DalamudWhite, new Vector4(0f, 0f, 0f, 255f), 1);
					ImGui.SameLine();
					float cursorPosX = ImGui.GetCursorPosX();
					UiSharedService.DrawOutlinedFont($"Compressing+Uploading {doneUploads}/{totalUploads}", ImGuiColors.DalamudWhite, new Vector4(0f, 0f, 0f, 255f), 1);
					ImGui.NewLine();
					ImGui.SameLine(cursorPosX);
					UiSharedService.DrawOutlinedFont(UiSharedService.ByteToString(totalUploaded, addSuffix: false) + "/" + UiSharedService.ByteToString(totalToUpload), ImGuiColors.DalamudWhite, new Vector4(0f, 0f, 0f, 255f), 1);
					if (_currentDownloads.Any())
					{
						ImGui.Separator();
					}
				}
			}
			catch
			{
			}
			try
			{
				foreach (KeyValuePair<GameObjectHandler, Dictionary<string, FileDownloadStatus>> item in _currentDownloads.ToList())
				{
					int dlSlot = item.Value.Count((KeyValuePair<string, FileDownloadStatus> c) => c.Value.DownloadStatus == DownloadStatus.WaitingForSlot);
					int dlQueue = item.Value.Count((KeyValuePair<string, FileDownloadStatus> c) => c.Value.DownloadStatus == DownloadStatus.WaitingForQueue);
					int dlProg = item.Value.Count((KeyValuePair<string, FileDownloadStatus> c) => c.Value.DownloadStatus == DownloadStatus.Downloading);
					int dlDecomp = item.Value.Count((KeyValuePair<string, FileDownloadStatus> c) => c.Value.DownloadStatus == DownloadStatus.Decompressing);
					int totalFiles = item.Value.Sum((KeyValuePair<string, FileDownloadStatus> c) => c.Value.TotalFiles);
					int transferredFiles = item.Value.Sum((KeyValuePair<string, FileDownloadStatus> c) => c.Value.TransferredFiles);
					long totalBytes = item.Value.Sum((KeyValuePair<string, FileDownloadStatus> c) => c.Value.TotalBytes);
					long transferredBytes = item.Value.Sum((KeyValuePair<string, FileDownloadStatus> c) => c.Value.TransferredBytes);
					UiSharedService.DrawOutlinedFont("▼", ImGuiColors.DalamudWhite, new Vector4(0f, 0f, 0f, 255f), 1);
					ImGui.SameLine();
					float cursorPosX2 = ImGui.GetCursorPosX();
					UiSharedService.DrawOutlinedFont($"{item.Key.Name} [W:{dlSlot}/Q:{dlQueue}/P:{dlProg}/D:{dlDecomp}]", ImGuiColors.DalamudWhite, new Vector4(0f, 0f, 0f, 255f), 1);
					ImGui.NewLine();
					ImGui.SameLine(cursorPosX2);
					UiSharedService.DrawOutlinedFont($"{transferredFiles}/{totalFiles} ({UiSharedService.ByteToString(transferredBytes, addSuffix: false)}/{UiSharedService.ByteToString(totalBytes)})", ImGuiColors.DalamudWhite, new Vector4(0f, 0f, 0f, 255f), 1);
				}
			}
			catch
			{
			}
		}
		if (!_configService.Current.ShowTransferBars)
		{
			return;
		}
		foreach (KeyValuePair<GameObjectHandler, Dictionary<string, FileDownloadStatus>> transfer in _currentDownloads.ToList())
		{
			Vector2 screenPos = _dalamudUtilService.WorldToScreen(transfer.Key.GetGameObject());
			if (!(screenPos == Vector2.Zero))
			{
				long totalBytes2 = transfer.Value.Sum((KeyValuePair<string, FileDownloadStatus> c) => c.Value.TotalBytes);
				long transferredBytes2 = transfer.Value.Sum((KeyValuePair<string, FileDownloadStatus> c) => c.Value.TransferredBytes);
				string maxDlText = UiSharedService.ByteToString(totalBytes2, addSuffix: false) + "/" + UiSharedService.ByteToString(totalBytes2);
				Vector2 textSize = (_configService.Current.TransferBarsShowText ? ImGui.CalcTextSize(maxDlText) : new Vector2(10f, 10f));
				int dlBarHeight = ((_configService.Current.TransferBarsHeight > (int)textSize.Y + 5) ? _configService.Current.TransferBarsHeight : ((int)textSize.Y + 5));
				int dlBarWidth = ((_configService.Current.TransferBarsWidth > (int)textSize.X + 10) ? _configService.Current.TransferBarsWidth : ((int)textSize.X + 10));
				Vector2 dlBarStart = new Vector2(screenPos.X - (float)dlBarWidth / 2f, screenPos.Y - (float)dlBarHeight / 2f);
				Vector2 dlBarEnd = new Vector2(screenPos.X + (float)dlBarWidth / 2f, screenPos.Y + (float)dlBarHeight / 2f);
				ImDrawListPtr drawList = ImGui.GetBackgroundDrawList();
				Vector2 vector = dlBarStart;
				vector.X = dlBarStart.X - 3f - 1f;
				vector.Y = dlBarStart.Y - 3f - 1f;
				Vector2 pMin = vector;
				vector = dlBarEnd;
				vector.X = dlBarEnd.X + 3f + 1f;
				vector.Y = dlBarEnd.Y + 3f + 1f;
				drawList.AddRectFilled(pMin, vector, UiSharedService.Color(0, 0, 0, 100), 1f);
				vector = dlBarStart;
				vector.X = dlBarStart.X - 3f;
				vector.Y = dlBarStart.Y - 3f;
				Vector2 pMin2 = vector;
				vector = dlBarEnd;
				vector.X = dlBarEnd.X + 3f;
				vector.Y = dlBarEnd.Y + 3f;
				drawList.AddRectFilled(pMin2, vector, UiSharedService.Color(220, 220, 220, 100), 1f);
				drawList.AddRectFilled(dlBarStart, dlBarEnd, UiSharedService.Color(0, 0, 0, 100), 1f);
				double dlProgressPercent = (double)transferredBytes2 / (double)totalBytes2;
				Vector2 pMin3 = dlBarStart;
				vector = dlBarEnd;
				vector.X = dlBarStart.X + (float)(dlProgressPercent * (double)dlBarWidth);
				drawList.AddRectFilled(pMin3, vector, UiSharedService.Color(50, 205, 50, 100), 1f);
				if (_configService.Current.TransferBarsShowText)
				{
					string downloadText = UiSharedService.ByteToString(transferredBytes2, addSuffix: false) + "/" + UiSharedService.ByteToString(totalBytes2);
					ImDrawListPtr drawList2 = drawList;
					vector = screenPos;
					vector.X = screenPos.X - textSize.X / 2f - 1f;
					vector.Y = screenPos.Y - textSize.Y / 2f - 1f;
					UiSharedService.DrawOutlinedFont(drawList2, downloadText, vector, UiSharedService.Color(byte.MaxValue, byte.MaxValue, byte.MaxValue, 100), UiSharedService.Color(0, 0, 0, 100), 1);
				}
			}
		}
		if (!_configService.Current.ShowUploading)
		{
			return;
		}
		foreach (GameObjectHandler player in _uploadingPlayers.Select<KeyValuePair<GameObjectHandler, bool>, GameObjectHandler>((KeyValuePair<GameObjectHandler, bool> p) => p.Key).ToList())
		{
			Vector2 screenPos2 = _dalamudUtilService.WorldToScreen(player.GetGameObject());
			if (screenPos2 == Vector2.Zero)
			{
				continue;
			}
			try
			{
				using (_uiShared.UidFont.Push())
				{
					string uploadText = "Uploading";
					Vector2 textSize2 = ImGui.CalcTextSize(uploadText);
					ImDrawListPtr backgroundDrawList = ImGui.GetBackgroundDrawList();
					Vector2 vector = screenPos2;
					vector.X = screenPos2.X - textSize2.X / 2f - 1f;
					vector.Y = screenPos2.Y - textSize2.Y / 2f - 1f;
					UiSharedService.DrawOutlinedFont(backgroundDrawList, uploadText, vector, UiSharedService.Color(byte.MaxValue, byte.MaxValue, 0, 100), UiSharedService.Color(0, 0, 0, 100), 2);
				}
			}
			catch
			{
			}
		}
	}

	public override bool DrawConditions()
	{
		if (_uiShared.EditTrackerPosition)
		{
			return true;
		}
		if (!_configService.Current.ShowTransferWindow && !_configService.Current.ShowTransferBars)
		{
			return false;
		}
		if (!_currentDownloads.Any() && !_fileTransferManager.CurrentUploads.Any() && !_uploadingPlayers.Any())
		{
			return false;
		}
		if (!base.IsOpen)
		{
			return false;
		}
		return true;
	}

	public override void PreDraw()
	{
		base.PreDraw();
		if (_uiShared.EditTrackerPosition)
		{
			base.Flags &= ~ImGuiWindowFlags.NoMove;
			base.Flags &= ~ImGuiWindowFlags.NoBackground;
			base.Flags &= ~ImGuiWindowFlags.NoInputs;
			base.Flags &= ~ImGuiWindowFlags.NoResize;
		}
		else
		{
			base.Flags |= ImGuiWindowFlags.NoMove;
			base.Flags |= ImGuiWindowFlags.NoBackground;
			base.Flags |= ImGuiWindowFlags.NoInputs;
			base.Flags |= ImGuiWindowFlags.NoResize;
		}
		float maxHeight = ImGui.GetTextLineHeight() * (float)(_configService.Current.ParallelDownloads + 3);
		base.SizeConstraints = new WindowSizeConstraints
		{
			MinimumSize = new Vector2(300f, maxHeight),
			MaximumSize = new Vector2(300f, maxHeight)
		};
	}
}
