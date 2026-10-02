namespace Deskverse.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Deskverse.App.Messaging;
using Deskverse.App.Services;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using Deskverse.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Studio: metadata curation and visual analysis for a selected wallpaper,
/// including per-display apply and re-analysis with Windows imaging.
/// </summary>
public partial class StudioViewModel : ViewModelBase, IRecipient<WallpaperSelectedMessage>, IRecipient<WallpaperUpdatedMessage>
{
    public const string AllDisplaysLabel = "All displays";

    private readonly WallpaperActions _actions;
    private readonly IWallpaperRepository _repository;
    private readonly IVisualAnalyzer _analyzer;
    private readonly IThumbnailService _thumbnails;
    private readonly IDisplayService _displays;
    private readonly CacheManager _cacheManager;
    private readonly NotificationService _notifications;
    private readonly ILogger<StudioViewModel> _logger;

    private IReadOnlyList<(string Label, string? DeviceName)> _displayChoices = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private WallpaperItemViewModel? _wallpaper;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _categoriesText = string.Empty;

    [ObservableProperty]
    private string _analysisSummary = "No wallpaper selected. Pick one from My Wallpapers to curate it here.";

    [ObservableProperty]
    private string _brightnessText = "\u2014";

    [ObservableProperty]
    private double _brightnessPercent;

    [ObservableProperty]
    private string _densityText = "\u2014";

    [ObservableProperty]
    private double _densityPercent;

    [ObservableProperty]
    private string _dimensionsText = "\u2014";

    [ObservableProperty]
    private string _dominantColorHex = "#22262f";

    [ObservableProperty]
    private bool _hasDominantColor;

    [ObservableProperty]
    private string _selectedDisplayLabel = AllDisplaysLabel;

    public StudioViewModel(
        WallpaperActions actions,
        IWallpaperRepository repository,
        IVisualAnalyzer analyzer,
        IThumbnailService thumbnails,
        IDisplayService displays,
        CacheManager cacheManager,
        NotificationService notifications,
        ILogger<StudioViewModel> logger)
    {
        _actions = actions;
        _repository = repository;
        _analyzer = analyzer;
        _thumbnails = thumbnails;
        _displays = displays;
        _cacheManager = cacheManager;
        _notifications = notifications;
        _logger = logger;
        WeakReferenceMessenger.Default.Register(this);
        DisplayLabels = new ObservableCollection<string>(BuildDisplayLabels());
    }

    public ObservableCollection<string> DisplayLabels { get; }

    public bool HasSelection => Wallpaper is not null;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var labels = BuildDisplayLabels();
        await RunOnUiAsync(() =>
        {
            DisplayLabels.Clear();
            foreach (var label in labels)
            {
                DisplayLabels.Add(label);
            }

            if (!DisplayLabels.Contains(SelectedDisplayLabel))
            {
                SelectedDisplayLabel = AllDisplaysLabel;
            }

            return true;
        }).ConfigureAwait(true);
    }

    private string[] BuildDisplayLabels()
    {
        _displayChoices = new[] { (AllDisplaysLabel, (string?)null) }
            .Concat(_displays.GetDisplays().Select(d => (Label: DescribeDisplay(d), DeviceName: d.DeviceName)))
            .ToArray();
        return _displayChoices.Select(c => c.Label).ToArray();
    }

    private static string DescribeDisplay(DisplayInfo display) => display.IsPrimary
        ? $"{display.Width} \u00D7 {display.Height} (primary)"
        : $"{display.Width} \u00D7 {display.Height}";

    public void Receive(WallpaperSelectedMessage message)
    {
        RunOnUi(() => Bind(message.Item));
    }

    public void Receive(WallpaperUpdatedMessage message)
    {
        if (Wallpaper?.Id == message.WallpaperId)
        {
            _ = RefreshFromStoreAsync();
        }
    }

    private async Task RefreshFromStoreAsync()
    {
        var fresh = await _repository.GetByIdAsync(Wallpaper!.Id).ConfigureAwait(true);
        if (fresh is not null)
        {
            RunOnUi(() => Bind(new WallpaperItemViewModel(fresh, _actions, Dispatcher)));
        }
    }

    private void Bind(WallpaperItemViewModel item)
    {
        Wallpaper = item;
        Title = item.Model.Title;
        Description = item.Model.Description ?? string.Empty;
        CategoriesText = string.Join(", ", item.Model.Categories);
        OnPropertyChanged(nameof(HasSelection));
        UpdateAnalysis(item.Model);
        _ = item.LoadThumbnailAsync(_thumbnails);
    }

    private void UpdateAnalysis(Wallpaper model)
    {
        AnalysisSummary = model.DominantColor is null && model.Brightness is null
            ? "This wallpaper has not been analyzed yet. Run analysis to unlock smart recommendations."
            : "Visual features feed the recommendation engine.";

        BrightnessText = model.Brightness is null ? "\u2014" : $"{model.Brightness.Value * 100:N0}%";
        BrightnessPercent = (model.Brightness ?? 0) * 100;

        DensityText = model.VisualDensity is null ? "\u2014" : $"{model.VisualDensity.Value * 100:N0}%";
        DensityPercent = (model.VisualDensity ?? 0) * 100;

        DimensionsText = model.Width > 0 ? $"{model.Width} \u00D7 {model.Height}" : "\u2014";
        HasDominantColor = model.DominantColor is not null;
        if (model.DominantColor is not null)
        {
            DominantColorHex = $"#{model.DominantColor}";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Wallpaper is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var trimmedTitle = Title.Trim();
            if (string.IsNullOrWhiteSpace(trimmedTitle))
            {
                _notifications.Warning("The title cannot be empty.");
                return;
            }

            var model = Wallpaper.Model;
            model.Title = trimmedTitle;
            model.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
            model.Categories = CategoriesText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(c => c.Length <= 30)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray();

            await _repository.UpdateAsync(model).ConfigureAwait(true);
            _notifications.Success("Metadata saved.");
            WeakReferenceMessenger.Default.Send(new WallpaperUpdatedMessage(model.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Studio save failed for {Id}", Wallpaper.Id);
            _notifications.Error("The metadata could not be saved.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReanalyzeAsync()
    {
        if (Wallpaper is null)
        {
            return;
        }

        if (!Wallpaper.Model.IsCached)
        {
            _notifications.Warning("Re-analysis needs the wallpaper to be present in the cache.");
            return;
        }

        IsBusy = true;
        try
        {
            var absolute = _cacheManager.ResolveCachePath(Wallpaper.Model.CacheRelativePath);
            if (absolute is null || !File.Exists(absolute))
            {
                _notifications.Warning("The cached file could not be located.");
                return;
            }

            var features = await _analyzer.AnalyzeAsync(absolute).ConfigureAwait(true);
            if (features is null)
            {
                _notifications.Warning("Analysis could not read this file.");
                return;
            }

            var model = Wallpaper.Model;
            model.DominantColor = features.DominantColorHex;
            model.Brightness = features.Brightness;
            model.VisualDensity = features.VisualDensity;
            model.Width = features.Width;
            model.Height = features.Height;
            await _repository.UpdateAsync(model).ConfigureAwait(true);

            RunOnUi(() => UpdateAnalysis(model));
            _notifications.Success("Visual analysis updated.");
            WeakReferenceMessenger.Default.Send(new WallpaperUpdatedMessage(model.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Re-analysis failed for {Id}", Wallpaper.Id);
            _notifications.Error("Re-analysis failed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshThumbnailAsync()
    {
        if (Wallpaper is null)
        {
            return;
        }

        await _actions.RefreshThumbnailAsync(Wallpaper.Model).ConfigureAwait(true);
        _notifications.Info("Thumbnail regenerated.");
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (Wallpaper is null)
        {
            return;
        }

        var deviceName = _displayChoices
            .FirstOrDefault(c => c.Label == SelectedDisplayLabel)
            .DeviceName;
        await _actions.ApplyAsync(Wallpaper.Id, deviceName).ConfigureAwait(true);
    }
}
