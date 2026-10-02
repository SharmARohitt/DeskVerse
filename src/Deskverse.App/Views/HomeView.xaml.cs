namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;

public sealed partial class HomeView
{
    public HomeViewModel ViewModel { get; } = App.Services.GetRequiredService<HomeViewModel>();

    public HomeView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }
}
