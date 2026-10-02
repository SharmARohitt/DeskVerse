namespace Deskverse.App.Views;

using Deskverse.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

/// <summary>Collections page code-behind: loads data on navigation and handles library-picker.</summary>
public sealed partial class CollectionsView : Page
{
    public CollectionsView()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<CollectionsViewModel>();
    }

    public CollectionsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }

    private void OnCreateEnter(Microsoft.UI.Xaml.UIElement sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.CreateCommand.Execute(null);
    }

    private async void OnAddFromLibraryClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Add wallpapers from your library",
            PrimaryButtonText = "Add selected",
            SecondaryButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        var picker = new LibraryPickerPanel(ViewModel.Manager);
        dialog.Content = picker;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var selected = picker.GetSelectedWallpapers();
            if (selected.Count > 0)
            {
                await ViewModel.AddWallpapersAsync(selected);
            }
        }
    }
}
