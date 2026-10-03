namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

public sealed partial class CollectionsView
{
    public CollectionsViewModel ViewModel { get; } = App.Services.GetRequiredService<CollectionsViewModel>();

    public CollectionsView()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }

    private async void OnAddStudioSelectionClick(object sender, RoutedEventArgs e)
    {
        var selected = App.Services.GetRequiredService<MainViewModel>().SelectedForStudio;
        if (selected is null)
        {
            App.Services.GetRequiredService<Services.NotificationService>()
                .Info("Open a wallpaper in Studio first, then add it here.");
            return;
        }

        await ViewModel.AddWallpapersAsync([selected.Model]);
    }

    private async void OnAddFromLibraryClick(object sender, RoutedEventArgs e)
    {
        var picker = new LibraryPickerPanel(ViewModel.Manager);
        var dialog = new ContentDialog
        {
            Title = "Add wallpapers to this collection",
            Content = picker,
            PrimaryButtonText = "Add",
            SecondaryButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var selected = picker.GetSelectedWallpapers();
            if (selected.Count > 0)
            {
                await ViewModel.AddWallpapersAsync(selected);
            }
        }
    }
}
