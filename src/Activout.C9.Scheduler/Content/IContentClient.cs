namespace Activout.C9.Scheduler;

/// <summary>Ordinary CMA reads needed by the scheduler (backed by the Contentful .NET SDK).</summary>
internal interface IContentClient
{
    /// <summary>ID of the user owning the management token; used to classify owned Scheduled Actions.</summary>
    Task<string> GetCurrentUserId(CancellationToken cancellationToken);

    /// <summary>IDs of all non-archived entries matching every populated selector property.</summary>
    Task<IReadOnlyList<string>> ResolveEntryIds(ContentSelector selector, CancellationToken cancellationToken);
}
