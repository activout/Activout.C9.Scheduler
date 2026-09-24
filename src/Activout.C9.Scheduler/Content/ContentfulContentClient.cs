using Contentful.Core;
using Contentful.Core.Configuration;
using Contentful.Core.Errors;
using Contentful.Core.Models;
using Microsoft.Extensions.Options;

namespace Activout.C9.Scheduler;

internal sealed class ContentfulContentClient(
    IHttpClientFactory httpClientFactory,
    IOptions<ContentSchedulerOptions> options) : IContentClient
{
    public const string HttpClientName = "Activout.C9.Scheduler.Contentful";
    private const int PageSize = 100;

    public async Task<string> GetCurrentUserId(CancellationToken cancellationToken)
    {
        var user = await CreateClient().GetCurrentUser(cancellationToken);
        return user.SystemProperties.Id;
    }

    public async Task<IReadOnlyList<string>> ResolveEntryIds(ContentSelector selector, CancellationToken cancellationToken)
    {
        if (selector.IsEmpty) throw new ArgumentException("Empty selectors are not allowed.", nameof(selector));

        var client = CreateClient();
        var entries = new List<Entry<dynamic>>();

        if (!string.IsNullOrWhiteSpace(selector.EntryId))
        {
            try
            {
                entries.Add(await client.GetEntry(selector.EntryId, cancellationToken: cancellationToken));
            }
            catch (ContentfulException ex) when (ex.StatusCode == 404)
            {
                return [];
            }
        }
        else
        {
            for (var skip = 0; ; skip += PageSize)
            {
                var page = await client.GetEntriesCollection<Entry<dynamic>>(
                    ContentSelectorQuery.Build(selector, skip, PageSize), cancellationToken: cancellationToken);
                entries.AddRange(page.Items);
                if (page.Items.Count() < PageSize || skip + PageSize >= page.Total) break;
            }
        }

        return entries
            .Select(ToEntryInfo)
            .Where(e => ContentSelectorQuery.Matches(selector, e))
            .Select(e => e.Id)
            .Distinct()
            .ToList();
    }

    private static EntryInfo ToEntryInfo(Entry<dynamic> entry) => new(
        entry.SystemProperties.Id,
        entry.SystemProperties.ContentType?.SystemProperties?.Id,
        entry.Metadata?.Tags?.Select(t => t.Sys.Id).ToList() ?? [],
        entry.SystemProperties.ArchivedVersion is not null || entry.SystemProperties.ArchivedAt is not null);

    private ContentfulManagementClient CreateClient()
    {
        var o = options.Value;
        return new ContentfulManagementClient(httpClientFactory.CreateClient(HttpClientName), new ContentfulOptions
        {
            SpaceId = o.SpaceId,
            Environment = o.Environment,
            ManagementApiKey = o.ManagementToken,
            MaxNumberOfRateLimitRetries = 3,
        });
    }
}
