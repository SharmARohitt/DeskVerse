namespace Deskverse.App;

using Deskverse.App.Services;
using Deskverse.App.ViewModels;
using Deskverse.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

/// <summary>Chosen cache directory and storage limit from first-run setup.</summary>
public sealed record FirstRunSetupResult(string CacheDirectory, long StorageLimitBytes);

public sealed partial class MainWindow : Window
{
    private static readonly IReadOnlyList<string> StorageLimitLabels =
        ["1 GB", "2 GB", "4 GB", "8 GB", "16 GB", "32 GB", "64 GB"];

    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<MainViewModel>();

        Title = "DeskVerse";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        NavView.SelectedItem = NavHome;
    }

    private MainViewModel Vm => _vm;

    private static long ParseStorageLimit(string label) => label switch
    {
        "1 GB" => 1L * 1024 * 1024 * 1024,
        "4 GB" => 4L * 1024 * 1024 * 1024,
        "8 GB" => 8L * 1024 * 1024 * 1024,
        "16 GB" => 16L * 1024 * 1024 * 1024,
        "32 GB" => 32L * 1024 * 1024 * 1024,
        "64 GB" => 64L * 1024 * 1024 * 1024,
        _ => 2L * 1024 * 1024 * 1024,
    };

    private static string FormatStorageLimit(long bytes) => bytes switch
    {
        >= 64L * 1024 * 1024 * 1024 => "64 GB",
        >= 32L * 1024 * 1024 * 1024 => "32 GB",
        >= 16L * 1024 * 1024 * 1024 => "16 GB",
        >= 8L * 1024 * 1024 * 1024 => "8 GB",
        >= 4L * 1024 * 1024 * 1024 => "4 GB",
        >= 1L * 1024 * 1024 * 1024 => "1 GB",
        _ => "2 GB",
    };

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        var page = tag switch
        {
            "home" => typeof(HomeView),
            "discover" => typeof(DiscoverView),
            "library" => typeof(LibraryView),
            "collections" => typeof(CollectionsView),
            "studio" => typeof(StudioView),
            "storage" => typeof(StorageView),
            "settings" => typeof(SettingsView),
            _ => (Type?)null,
        };

        if (page is not null && ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }

    public void OnStartupComplete()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
        _vm.MarkReady();
        ContentFrame.Navigate(typeof(HomeView));
    }

    public void ShowFatalError(string message)
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
        _vm.ShowFatal(message);
    }

    /// <summary>
    /// Shows the first-run setup dialog. Returns null only if the dialog is
    /// torn down without a choice (the caller then applies safe defaults).
    /// </summary>
    public async Task<FirstRunSetupResult?> ShowFirstRunSetupAsync(
        string defaultCacheDirectory,
        long defaultLimitBytes)
    {
        var chosenDirectory = defaultCacheDirectory;
        var chosenLimitLabel = FormatStorageLimit(defaultLimitBytes);

        var directoryText = new TextBlock
        {
            Text = defaultCacheDirectory,
            Style = (Style)Resources["BodyText"],
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
        };

        var limitCombo = new ComboBox
        {
            Header = "Storage limit for cached wallpapers",
            SelectedItem = chosenLimitLabel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 220,
        };
        foreach (var label in StorageLimitLabels)
        {
            limitCombo.Items.Add(label);
        }

        limitCombo.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string label)
            {
                chosenLimitLabel = label;
            }
        };

        var browseButton = new Button
        {
            Content = "Browse…",
            Style = (Style)Resources["SecondaryButtonStyle"],
        };
        browseButton.Click += async (_, _) =>
        {
            var pickers = App.Services.GetRequiredService<PickerService>();
            var picked = await pickers.PickFolderAsync(chosenDirectory);
            if (picked is not null)
            {
                chosenDirectory = picked;
                directoryText.Text = picked;
            }
        };

        var directoryRow = new Grid { ColumnSpacing = 10 };
        directoryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        directoryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(directoryText, 0);
        Grid.SetColumn(browseButton, 1);
        directoryRow.Children.Add(directoryText);
        directoryRow.Children.Add(browseButton);

        var content = new StackPanel { Spacing = 18 };
        content.Children.Add(new TextBlock
        {
            Text = "DeskVerse keeps a managed cache of wallpapers on this PC. Choose where it lives and how much space it may use. Nothing is ever uploaded, and cleanup always spares the active and pinned wallpapers.",
            Style = (Style)Resources["BodyText"],
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Cache location", Style = (Style)Resources["SubtitleText"] },
                directoryRow,
            },
        });
        content.Children.Add(limitCombo);

        var dialog = new ContentDialog
        {
            Title = "Welcome to DeskVerse",
            Content = content,
            PrimaryButtonText = "Start exploring",
            SecondaryButtonText = "Use defaults",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.Primary)
        {
            return new FirstRunSetupResult(chosenDirectory, ParseStorageLimit(chosenLimitLabel));
        }

        return new FirstRunSetupResult(defaultCacheDirectory, defaultLimitBytes);
    }
}
