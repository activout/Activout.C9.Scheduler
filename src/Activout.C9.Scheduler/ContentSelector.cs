namespace Activout.C9.Scheduler;

/// <summary>
/// Selects entries. All populated properties must match (logical AND). At least one property must
/// be populated; an empty selector never means "all entries". For OR, define multiple schedules.
/// </summary>
public sealed class ContentSelector
{
    /// <summary>Contentful tag ID (not display name) the entry must have.</summary>
    public string? Tag { get; set; }

    /// <summary>Content type ID the entry must have.</summary>
    public string? ContentType { get; set; }

    /// <summary>Specific entry ID.</summary>
    public string? EntryId { get; set; }

    internal bool IsEmpty =>
        string.IsNullOrWhiteSpace(Tag) && string.IsNullOrWhiteSpace(ContentType) && string.IsNullOrWhiteSpace(EntryId);

    /// <inheritdoc />
    public override string ToString() => $"Tag={Tag}, ContentType={ContentType}, EntryId={EntryId}";
}
