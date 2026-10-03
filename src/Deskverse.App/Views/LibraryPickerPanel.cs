namespace Deskverse.App.Views;

using Deskverse.Application;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

/// <summary>
/// Inline library browser for the "Add from library" dialog in Collections.
/// Presented inside a ContentDialog as its Content. Loads lazily and supports
/// multi-select via CheckBoxes.
/// </summary>
public sealed class LibraryPickerPanel : StackPanel
{
    private readonly WallpaperManager _manager;
    private readonly StackPanel _itemsPanel;
    private readonly List<(CheckBox Box, Wallpaper Wallpaper)> _items = [];

    public LibraryPickerPanel(WallpaperManager manager)
    {
        _manager = manager;
        Spacing = 4;
        MaxWidth = 560;

        var scroller = new ScrollViewer { MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _itemsPanel = new StackPanel { Spacing = 0 };
        scroller.Content = _itemsPanel;
        Children.Add(scroller);

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var page = await _manager.QueryAsync(new WallpaperQuery { Take = 120, CachedOnly = true }).ConfigureAwait(true);

        foreach (var wallpaper in page.Items)
        {
            var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            var img = new Image { Width = 56, Height = 36, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
            var title = new TextBlock
            {
                Text = wallpaper.Title,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(4, 5, 4, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(cb, 0);
            Grid.SetColumn(img, 1);
            Grid.SetColumn(title, 2);
            row.Children.Add(cb);
            row.Children.Add(img);
            row.Children.Add(title);
            _itemsPanel.Children.Add(row);
            _items.Add((cb, wallpaper));

            // Lazy thumbnail
            var w = wallpaper;
            _ = Task.Run(async () =>
            {
                var thumbnails = App.Services.GetRequiredService<Core.Abstractions.IThumbnailService>();
                var path = await thumbnails.GetOrCreateThumbnailAsync(w).ConfigureAwait(false);
                if (path is null)
                {
                    return;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        img.Source = new BitmapImage(new Uri(path, UriKind.Absolute));
                    }
                    catch { /* thumbnail unavailable */ }
                });
            });
        }

        if (page.Items.Count == 0)
        {
            _itemsPanel.Children.Add(new TextBlock
            {
                Text = "No cached wallpapers yet. Import or download some first.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextMutedBrush"],
                Padding = new Thickness(8),
            });
        }
    }

    public IReadOnlyList<Wallpaper> GetSelectedWallpapers() =>
        _items.Where(t => t.Box.IsChecked == true).Select(t => t.Wallpaper).ToList();
}
