namespace Deskverse.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>
/// Stores an instant as a UTC <see cref="DateTime"/> so SQLite can sort it, and
/// reads it back as an offset-aware value. Every timestamp DeskVerse writes is
/// already UTC, so the conversion loses no information.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTimeOffset, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            instant => instant.UtcDateTime,
            stored => new DateTimeOffset(stored, TimeSpan.Zero))
    {
    }
}
