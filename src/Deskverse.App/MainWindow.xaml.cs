namespace Deskverse.App;

using Deskverse.App.Services;
using Deskverse.App.ViewModels;
using Deskverse.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

/// <summary>Chosen cache directory and storage limit from first-run setup.</summary>
public sealed record FirstRunSetupResult(string CacheDirectory, long StorageLimitBytes);

public sealed partial class MainWindow : Window
{
    private static readonly IReadOnlyList<string> StorageLimitLabels =
        ["1 GB", "2 GB", "4 GB", "8 GB", "16 GB", "32 GB", "64 GB"];

    private readonly MainViewModel _vm;

    private bool _startupComplete;

    public MainWindow()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<MainViewModel>();

        Title = "DeskVerse";

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // AppWindow sizing is a no-op until the window has been activated.
        Activated += OnFirstActivation;

        // Selection is set now, but the page itself is only navigated to once the
        // database migration has run; views resolve data services in their constructors.
        NavView.SelectedItem = NavHome;
    }

    private void OnFirstActivation(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        Activated -= OnFirstActivation;

        var screen = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));

        if (screen is not null)
        {
            var work = screen.WorkArea;
            AppWindow.Move(new Windows.Graphics.PointInt32(
                work.X + Math.Max(0, (work.Width - AppWindow.Size.Width) / 2),
                work.Y + Math.Max(0, (work.Height - AppWindow.Size.Height) / 2)));
        }
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
        // Views build the data layer as they are constructed, so the first page is
        // only navigated to after startup has prepared the database.
        if (!_startupComplete)
        {
            return;
        }

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
        _startupComplete = true;
        _vm.MarkReady();

        if (ContentFrame.CurrentSourcePageType != typeof(HomeView))
        {
            ContentFrame.Navigate(typeof(HomeView));
        }
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
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["BodyText"],
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
        };

        var limitCombo = new ComboBox
        {
            Header = "Storage limit for cached wallpapers",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 220,
        };
        foreach (var label in StorageLimitLabels)
        {
            limitCombo.Items.Add(label);
        }

        // Selecting before the items exist silently leaves the box blank.
        limitCombo.SelectedItem = chosenLimitLabel;

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
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SecondaryButtonStyle"],
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
        directoryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        directoryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(directoryText, 0);
        Grid.SetColumn(browseButton, 1);
        directoryRow.Children.Add(directoryText);
        directoryRow.Children.Add(browseButton);

        var content = new StackPanel { Spacing = 18 };
        content.Children.Add(new TextBlock
        {
            Text = "DeskVerse keeps a managed cache of wallpapers on this PC. Choose where it lives and how much space it may use. Nothing is ever uploaded, and cleanup always spares the active and pinned wallpapers.",
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["BodyText"],
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Cache location", Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SubtitleText"] },
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
