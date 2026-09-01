using AiUsageHud.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUsageHud.Presentation.ViewModels;

/// <summary>
/// A selectable vendor row for the Settings visible-vendors multi-select dropdown.
/// </summary>
public sealed partial class VendorSettingItemVm : ObservableObject
{
    public VendorId Vendor { get; }
    public string Label => Vendor.Label();

    [ObservableProperty] private bool _isEnabled;

    public event EventHandler? Toggled;

    public VendorSettingItemVm(VendorId vendor, bool isEnabled)
    {
        Vendor = vendor;
        _isEnabled = isEnabled;
    }

    [RelayCommand]
    public void Toggle()
    {
        IsEnabled = !IsEnabled;
    }

    partial void OnIsEnabledChanged(bool value) => Toggled?.Invoke(this, EventArgs.Empty);
}
