namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;

public sealed partial class StorageView
{
    public StorageViewModel ViewModel { get; } = App.Services.GetRequiredService<StorageViewModel>();

    public StorageView()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }

    private async void OnChangeDirectoryClick(object sender, RoutedEventArgs e)
    {
        var path = await ViewModel.PickCacheDirectoryAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            await ViewModel.ApplyCacheDirectoryAsync(path).ConfigureAwait(true);
        }
    }
}
