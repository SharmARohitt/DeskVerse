namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

public sealed partial class SettingsView : Page
{
    public SettingsView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        RotationVm = App.Services.GetRequiredService<RotationViewModel>();
    }

    public SettingsViewModel ViewModel { get; }

    public RotationViewModel RotationVm { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
        _ = RotationVm.LoadAsync();
    }
}
