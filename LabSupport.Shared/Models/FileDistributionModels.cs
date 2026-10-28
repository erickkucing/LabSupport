using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LabSupport.Shared.Models;

public enum TargetTransferState
{
    Waiting,
    Transferring,
    Completed,
    Failed,
    Cancelled
}

public class FileDistributionOptions
{
    public string SourcePath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string TargetDirectory { get; set; } = "Desktop";
    public string CustomSubfolder { get; set; } = string.Empty;
    public bool AutoOpen { get; set; }
    public bool ShowNotification { get; set; } = true;
    public bool Overwrite { get; set; } = true;
}

public class FileTransferTargetItem : INotifyPropertyChanged
{
    private TargetTransferState _state = TargetTransferState.Waiting;
    private string _statusMessage = "Menunggu...";
    private double _progress;
    private long _bytesTransferred;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Hostname { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;

    public TargetTransferState State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(StateBrush));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) > 0.001)
            {
                _progress = value;
                OnPropertyChanged(nameof(Progress));
            }
        }
    }

    public long BytesTransferred
    {
        get => _bytesTransferred;
        set
        {
            if (_bytesTransferred != value)
            {
                _bytesTransferred = value;
                OnPropertyChanged(nameof(BytesTransferred));
            }
        }
    }

    public string StateText => State switch
    {
        TargetTransferState.Waiting => "Menunggu",
        TargetTransferState.Transferring => "Mengunduh...",
        TargetTransferState.Completed => "Selesai",
        TargetTransferState.Failed => "Gagal",
        TargetTransferState.Cancelled => "Dibatalkan",
        _ => "Menunggu"
    };

    public string StateBrush => State switch
    {
        TargetTransferState.Waiting => "#94A3B8",
        TargetTransferState.Transferring => "#38BDF8",
        TargetTransferState.Completed => "#22C55E",
        TargetTransferState.Failed => "#EF4444",
        TargetTransferState.Cancelled => "#F59E0B",
        _ => "#94A3B8"
    };

    protected void OnPropertyChanged(string propName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}

public class FileTransferProgressTracker : INotifyPropertyChanged, IDisposable
{
    private double _overallProgress;
    private string _overallProgressText = "0%";
    private double _transferSpeedMBs;
    private string _speedFormatted = "0.00 MB/s";
    private bool _isInProgress;
    private bool _canCancel = true;
    private bool _canRetryFailed;
    private string _itemSummary = string.Empty;
    private CancellationTokenSource _cts = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FileTransferTargetItem> Targets { get; } = new();

    public double OverallProgress
    {
        get => _overallProgress;
        set
        {
            if (Math.Abs(_overallProgress - value) > 0.001)
            {
                _overallProgress = value;
                OnPropertyChanged(nameof(OverallProgress));
            }
        }
    }

    public string OverallProgressText
    {
        get => _overallProgressText;
        set
        {
            if (_overallProgressText != value)
            {
                _overallProgressText = value;
                OnPropertyChanged(nameof(OverallProgressText));
            }
        }
    }

    public double TransferSpeedMBs
    {
        get => _transferSpeedMBs;
        set
        {
            if (Math.Abs(_transferSpeedMBs - value) > 0.001)
            {
                _transferSpeedMBs = value;
                OnPropertyChanged(nameof(TransferSpeedMBs));
                SpeedFormatted = $"{_transferSpeedMBs:F2} MB/s";
            }
        }
    }

    public string SpeedFormatted
    {
        get => _speedFormatted;
        set
        {
            if (_speedFormatted != value)
            {
                _speedFormatted = value;
                OnPropertyChanged(nameof(SpeedFormatted));
            }
        }
    }

    public bool IsInProgress
    {
        get => _isInProgress;
        set
        {
            if (_isInProgress != value)
            {
                _isInProgress = value;
                OnPropertyChanged(nameof(IsInProgress));
            }
        }
    }

    public bool CanCancel
    {
        get => _canCancel;
        set
        {
            if (_canCancel != value)
            {
                _canCancel = value;
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public bool CanRetryFailed
    {
        get => _canRetryFailed;
        set
        {
            if (_canRetryFailed != value)
            {
                _canRetryFailed = value;
                OnPropertyChanged(nameof(CanRetryFailed));
            }
        }
    }

    public string ItemSummary
    {
        get => _itemSummary;
        set
        {
            if (_itemSummary != value)
            {
                _itemSummary = value;
                OnPropertyChanged(nameof(ItemSummary));
            }
        }
    }

    public CancellationToken CancellationToken => _cts.Token;

    public Func<System.Collections.Generic.List<FileTransferTargetItem>, Task>? RetryFailedHandler { get; set; }

    public void CancelTransfer()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        CanCancel = false;
        foreach (var target in Targets.Where(t => t.State == TargetTransferState.Waiting || t.State == TargetTransferState.Transferring))
        {
            target.State = TargetTransferState.Cancelled;
            target.StatusMessage = "Dibatalkan oleh guru.";
        }

        UpdateCompletionState();
    }

    public async Task ExecuteRetryFailedAsync()
    {
        var failedList = Targets.Where(t => t.State == TargetTransferState.Failed || t.State == TargetTransferState.Cancelled).ToList();
        if (failedList.Count == 0 || RetryFailedHandler == null) return;

        _cts.Dispose();
        _cts = new CancellationTokenSource();

        foreach (var f in failedList)
        {
            f.State = TargetTransferState.Waiting;
            f.StatusMessage = "Mencoba ulang...";
            f.Progress = 0;
            f.BytesTransferred = 0;
        }

        IsInProgress = true;
        CanCancel = true;
        CanRetryFailed = false;

        try
        {
            await RetryFailedHandler(failedList);
        }
        finally
        {
            UpdateCompletionState();
        }
    }

    public void MarkTargetTransferring(string ipAddress)
    {
        var target = Targets.FirstOrDefault(t => t.IpAddress == ipAddress);
        if (target != null)
        {
            target.State = TargetTransferState.Transferring;
            target.StatusMessage = "Sedang mengunduh berkas...";
        }
    }

    public void MarkTargetCompleted(string ipAddress, string? message = null)
    {
        var target = Targets.FirstOrDefault(t => t.IpAddress == ipAddress);
        if (target != null)
        {
            target.State = TargetTransferState.Completed;
            target.Progress = 100.0;
            target.StatusMessage = message ?? "Selesai (100%)";
        }
        UpdateCompletionState();
    }

    public void MarkTargetFailed(string ipAddress, string reason)
    {
        var target = Targets.FirstOrDefault(t => t.IpAddress == ipAddress);
        if (target != null)
        {
            target.State = TargetTransferState.Failed;
            target.StatusMessage = $"Gagal: {reason}";
        }
        UpdateCompletionState();
    }

    public void UpdateAggregateProgress(long totalBytesTransferred, long totalExpectedBytes, double speedMBs)
    {
        TransferSpeedMBs = speedMBs;

        if (totalExpectedBytes > 0)
        {
            double pct = Math.Min(100.0, (double)totalBytesTransferred / totalExpectedBytes * 100.0);
            OverallProgress = pct;
            double mbDone = totalBytesTransferred / (1024.0 * 1024.0);
            double mbTotal = totalExpectedBytes / (1024.0 * 1024.0);
            OverallProgressText = $"{pct:F1}% ({mbDone:F1} MB / {mbTotal:F1} MB)";
        }
        else
        {
            OverallProgress = 0;
            OverallProgressText = "0%";
        }
    }

    public void UpdateCompletionState()
    {
        bool anyPending = Targets.Any(t => t.State == TargetTransferState.Waiting || t.State == TargetTransferState.Transferring);
        IsInProgress = anyPending;
        CanCancel = anyPending;

        int failedCount = Targets.Count(t => t.State == TargetTransferState.Failed || t.State == TargetTransferState.Cancelled);
        CanRetryFailed = !anyPending && failedCount > 0;

        int completedCount = Targets.Count(t => t.State == TargetTransferState.Completed);
        if (!anyPending)
        {
            OverallProgress = 100.0;
            if (failedCount == 0)
            {
                OverallProgressText = $"Selesai! Seluruh {completedCount} PC berhasil menerima berkas.";
            }
            else
            {
                OverallProgressText = $"Selesai: {completedCount} Berhasil, {failedCount} Gagal.";
            }
        }
    }

    public void Dispose()
    {
        _cts.Dispose();
    }

    protected void OnPropertyChanged(string propName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}
