namespace Activout.C9.Scheduler;

/// <summary>Minimal view of an entry for selector evaluation.</summary>
internal sealed record EntryInfo(string Id, string? ContentType, IReadOnlyCollection<string> TagIds, bool Archived);

internal static class ContentSelectorQuery
{
    /// <summary>
    /// CMA entries query pushing all selector predicates to the server. Results are still verified
    /// locally with <see cref="Matches"/>, so AND semantics never depend on the server honouring the query.
    /// </summary>
    public static string Build(ContentSelector selector, int skip, int limit)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(selector.ContentType)) query.Add("content_type=" + Uri.EscapeDataString(selector.ContentType));
        if (!string.IsNullOrWhiteSpace(selector.Tag)) query.Add("metadata.tags.sys.id[all]=" + Uri.EscapeDataString(selector.Tag));
        if (!string.IsNullOrWhiteSpace(selector.EntryId)) query.Add("sys.id=" + Uri.EscapeDataString(selector.EntryId));
        query.Add("sys.archivedAt[exists]=false");
        query.Add("order=sys.id");
        query.Add($"skip={skip}");
        query.Add($"limit={limit}");
        return "?" + string.Join('&', query);
    }

    public static bool Matches(ContentSelector selector, EntryInfo entry)
    {
        if (selector.IsEmpty || entry.Archived) return false;
        if (!string.IsNullOrWhiteSpace(selector.EntryId) && entry.Id != selector.EntryId) return false;
        if (!string.IsNullOrWhiteSpace(selector.ContentType) && entry.ContentType != selector.ContentType) return false;
        if (!string.IsNullOrWhiteSpace(selector.Tag) && !entry.TagIds.Contains(selector.Tag)) return false;
        return true;
    }
}
