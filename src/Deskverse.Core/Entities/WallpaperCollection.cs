namespace Deskverse.Core.Entities;

/// <summary>A named user collection (playlist) of wallpapers.</summary>
public class WallpaperCollection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>True for system-generated collections such as "Favorites".</summary>
    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<CollectionItem> Items { get; set; } = new List<CollectionItem>();
}

public class CollectionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CollectionId { get; set; }

    public WallpaperCollection? Collection { get; set; }

    public Guid WallpaperId { get; set; }

    public Wallpaper? Wallpaper { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
