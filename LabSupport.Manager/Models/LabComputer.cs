using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabSupport.Shared.Models;

namespace LabSupport.Manager.Models;

public class LabComputer : ComputerInfo, INotifyPropertyChanged
{
    private bool _isSelected;
    private BitmapSource? _thumbnailSource;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public BitmapSource? ThumbnailSource
    {
        get => _thumbnailSource;
        private set => SetField(ref _thumbnailSource, value);
    }

    public string StatusText => IsOnline ? "Online" : "Offline";
    public Brush StatusBrush => IsOnline ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(100, 116, 139));

    public void SetThumbnailBytes(byte[]? bytes)
    {
        ThumbnailData = bytes;
        UpdateThumbnailSource();
    }

    public void NotifyMetricsChanged()
    {
        OnPropertyChanged(nameof(Hostname));
        OnPropertyChanged(nameof(IpAddress));
        OnPropertyChanged(nameof(MacAddress));
        OnPropertyChanged(nameof(OsVersion));
        OnPropertyChanged(nameof(CpuUsage));
        OnPropertyChanged(nameof(CpuFormatted));
        OnPropertyChanged(nameof(RamUsageMb));
        OnPropertyChanged(nameof(RamTotalMb));
        OnPropertyChanged(nameof(RamPercent));
        OnPropertyChanged(nameof(RamFormatted));
        OnPropertyChanged(nameof(DiskFreeGb));
        OnPropertyChanged(nameof(DiskFormatted));
        OnPropertyChanged(nameof(LastSeen));
        OnPropertyChanged(nameof(LastSeenFormatted));
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
    }

    public void UpdateThumbnailSource()
    {
        if (ThumbnailData == null || ThumbnailData.Length == 0)
        {
            ThumbnailSource = null;
            return;
        }

        try
        {
            var image = new BitmapImage();
            using (var ms = new MemoryStream(ThumbnailData))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
            }
            image.Freeze();
            ThumbnailSource = image;
        }
        catch
        {
            ThumbnailSource = null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
