namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

public sealed partial class StorageView : Page
{
    public StorageView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<StorageViewModel>();
    }

    public StorageViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }

    private async void OnMoveCacheClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var path = await ViewModel.PickCacheDirectoryAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            await ViewModel.ApplyCacheDirectoryAsync(path).ConfigureAwait(true);
        }
    }
}
