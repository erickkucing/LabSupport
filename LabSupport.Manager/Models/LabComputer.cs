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
    private BitmapSource? _thumbnailSource;
    private DateTime _lastDbSaved = DateTime.MinValue;
    private bool _isDuplicateHostname;

    public DateTime LastDbSaved
    {
        get => _lastDbSaved;
        set => SetField(ref _lastDbSaved, value);
    }

    public bool IsDuplicateHostname
    {
        get => _isDuplicateHostname;
        set => SetField(ref _isDuplicateHostname, value);
    }

    public new bool IsSelected
    {
        get => base.IsSelected;
        set
        {
            if (base.IsSelected != value)
            {
                base.IsSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public new string GroupName
    {
        get => base.GroupName;
        set
        {
            if (base.GroupName != value)
            {
                base.GroupName = value;
                OnPropertyChanged();
            }
        }
    }

    public new string RoomName
    {
        get => base.RoomName;
        set
        {
            if (base.RoomName != value)
            {
                base.RoomName = value;
                OnPropertyChanged();
            }
        }
    }

    public new string? DisplayName
    {
        get => base.DisplayName;
        set
        {
            if (base.DisplayName != value)
            {
                base.DisplayName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveName));
            }
        }
    }

    public new double PositionX
    {
        get => base.PositionX;
        set
        {
            if (Math.Abs(base.PositionX - value) > 0.001)
            {
                base.PositionX = value;
                OnPropertyChanged();
            }
        }
    }

    public new double PositionY
    {
        get => base.PositionY;
        set
        {
            if (Math.Abs(base.PositionY - value) > 0.001)
            {
                base.PositionY = value;
                OnPropertyChanged();
            }
        }
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

    public void SetThumbnailSource(BitmapSource? source)
    {
        ThumbnailSource = source;
    }

    public static BitmapSource? CreateFrozenBitmap(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var image = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
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
        OnPropertyChanged(nameof(NetworkDownloadKbps));
        OnPropertyChanged(nameof(NetworkUploadKbps));
        OnPropertyChanged(nameof(NetworkFormatted));
        OnPropertyChanged(nameof(IsBandwidthHog));
        OnPropertyChanged(nameof(CpuModel));
        OnPropertyChanged(nameof(GpuModel));
        OnPropertyChanged(nameof(DiskTotalGb));
        OnPropertyChanged(nameof(MotherboardSerial));
        OnPropertyChanged(nameof(IsLowDiskSpace));
        OnPropertyChanged(nameof(IsHighRamUsage));
        OnPropertyChanged(nameof(DiskStorageFormatted));
        OnPropertyChanged(nameof(IdleSeconds));
        OnPropertyChanged(nameof(IdleFormatted));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(LastSeen));
        OnPropertyChanged(nameof(LastSeenFormatted));
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(RoomName));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(EffectiveName));
        OnPropertyChanged(nameof(PositionX));
        OnPropertyChanged(nameof(PositionY));
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
