namespace Deskverse.Core.Entities;

/// <summary>Per-provider enablement and configuration state.</summary>
public class ProviderConfiguration
{
    public string ProviderId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Provider-specific settings as JSON. Secrets must never be stored here.</summary>
    public string? ConfigurationJson { get; set; }

    public DateTimeOffset? LastSuccessfulSync { get; set; }

    public string? LastError { get; set; }
}
