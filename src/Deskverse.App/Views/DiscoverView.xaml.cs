namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;

public sealed partial class DiscoverView
{
    public DiscoverViewModel ViewModel { get; } = App.Services.GetRequiredService<DiscoverViewModel>();

    public DiscoverView()
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
