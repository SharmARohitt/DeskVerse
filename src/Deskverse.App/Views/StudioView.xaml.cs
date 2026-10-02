namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

public sealed partial class StudioView : Page
{
    public StudioView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<StudioViewModel>();
    }

    public StudioViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => base.OnNavigatedTo(e);
}
