using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LabSupport.Shared.Models;

public class LabRoom : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private int _capacity = 30;
    private string _description = string.Empty;
    private int _onlineCount;
    private int _totalRegistered;
    private bool _isSelected;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public int Capacity
    {
        get => _capacity;
        set
        {
            if (SetField(ref _capacity, value))
            {
                OnPropertyChanged(nameof(QuotaDisplay));
                OnPropertyChanged(nameof(SummaryDisplay));
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public int OnlineCount
    {
        get => _onlineCount;
        set
        {
            if (SetField(ref _onlineCount, value))
            {
                OnPropertyChanged(nameof(QuotaDisplay));
                OnPropertyChanged(nameof(SummaryDisplay));
            }
        }
    }

    public int TotalRegistered
    {
        get => _totalRegistered;
        set
        {
            if (SetField(ref _totalRegistered, value))
            {
                OnPropertyChanged(nameof(SummaryDisplay));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string QuotaDisplay => $"{OnlineCount}/{Capacity} Online";
    public string SummaryDisplay => $"{Name} ({OnlineCount}/{Capacity})";

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
