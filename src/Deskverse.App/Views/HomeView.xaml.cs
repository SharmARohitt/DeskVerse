namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;

public sealed partial class HomeView
{
    public HomeViewModel ViewModel { get; } = App.Services.GetRequiredService<HomeViewModel>();

    public HomeView()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }
}
