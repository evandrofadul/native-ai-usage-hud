using AiUsageHud.Core.Config;
using AiUsageHud.Core.Models;
using AiUsageHud.Presentation.Theming;
using AiUsageHud.Presentation.ViewModels;

namespace AiUsageHud.Core.Tests;

public class DummyThemeService : IThemeService
{
    public ThemeId Current { get; set; } = ThemeId.OneDark;
    public event Action? Changed;

    public void Apply(ThemeId theme)
    {
        Current = theme;
        Changed?.Invoke();
    }
}

public class SettingsViewModelTests
{
    [Fact]
    public void LoadsVendorsAndSummaryFromConfig()
    {
        var cfg = new AppConfig();
        cfg.Openai.Enabled = false;
        var theme = new DummyThemeService();

        var vm = new SettingsViewModel(cfg, theme);

        Assert.Equal(5, vm.VendorOptions.Count);
        Assert.Equal(4, vm.AvailablePrimaryVendors.Count);
        Assert.Contains(VendorId.Anthropic, vm.AvailablePrimaryVendors);
        Assert.DoesNotContain(VendorId.Openai, vm.AvailablePrimaryVendors);
        Assert.Equal("4 of 5 active", vm.VendorSummary);
    }

    [Fact]
    public void TogglingVendorUpdatesAvailablePrimaryVendorsAndSummary()
    {
        var cfg = new AppConfig();
        var theme = new DummyThemeService();
        var vm = new SettingsViewModel(cfg, theme);

        Assert.Equal("All vendors (5/5)", vm.VendorSummary);

        var copilotOption = vm.VendorOptions.First(v => v.Vendor == VendorId.Copilot);
        copilotOption.IsEnabled = false;

        Assert.Equal("4 of 5 active", vm.VendorSummary);
        Assert.DoesNotContain(VendorId.Copilot, vm.AvailablePrimaryVendors);

        copilotOption.IsEnabled = true;
        Assert.Equal("All vendors (5/5)", vm.VendorSummary);
        Assert.Contains(VendorId.Copilot, vm.AvailablePrimaryVendors);
    }

    [Fact]
    public void PrimaryVendorShiftsWhenCurrentPrimaryIsDisabled()
    {
        var cfg = new AppConfig();
        cfg.Ui.Primary = VendorId.Anthropic;
        var theme = new DummyThemeService();
        var vm = new SettingsViewModel(cfg, theme);

        Assert.Equal(VendorId.Anthropic, vm.Primary);

        var anthropicOption = vm.VendorOptions.First(v => v.Vendor == VendorId.Anthropic);
        anthropicOption.IsEnabled = false;

        Assert.NotEqual(VendorId.Anthropic, vm.Primary);
        Assert.Equal(VendorId.Openai, vm.Primary);
    }

    [Fact]
    public void PreventsDisablingAllVendors()
    {
        var cfg = new AppConfig();
        var theme = new DummyThemeService();
        var vm = new SettingsViewModel(cfg, theme);

        foreach (var opt in vm.VendorOptions)
        {
            opt.IsEnabled = false;
        }

        // At least one vendor must remain active.
        Assert.Contains(vm.VendorOptions, v => v.IsEnabled);
        Assert.NotEmpty(vm.AvailablePrimaryVendors);
    }

    [Fact]
    public void ToggleVendorDropdownChangesState()
    {
        var cfg = new AppConfig();
        var theme = new DummyThemeService();
        var vm = new SettingsViewModel(cfg, theme);

        Assert.False(vm.IsVendorDropdownOpen);
        vm.ToggleVendorDropdownCommand.Execute(null);
        Assert.True(vm.IsVendorDropdownOpen);
        vm.ToggleVendorDropdownCommand.Execute(null);
        Assert.False(vm.IsVendorDropdownOpen);
    }
}
