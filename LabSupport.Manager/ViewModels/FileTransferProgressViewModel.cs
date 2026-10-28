using System;
using System.Windows.Input;
using LabSupport.Shared.Models;

namespace LabSupport.Manager.ViewModels;

public class FileTransferProgressViewModel : FileTransferProgressTracker
{
    public ICommand CancelCommand { get; }
    public ICommand RetryFailedCommand { get; }
    public ICommand CloseCommand { get; }

    public event Action? OnCloseRequested;

    public FileTransferProgressViewModel()
    {
        CancelCommand = new RelayCommand(CancelTransfer, () => CanCancel);
        RetryFailedCommand = new AsyncRelayCommand(ExecuteRetryFailedAsync, () => CanRetryFailed);
        CloseCommand = new RelayCommand(() => OnCloseRequested?.Invoke(), () => !IsInProgress);
    }
}
