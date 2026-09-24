using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Activout.C9.Scheduler;

/// <summary>
/// Minimal <see cref="HttpClient"/> adapter for the Contentful Scheduled Actions API, which the
/// Contentful .NET SDK does not cover. Base address and auth are configured on the named client.
/// </summary>
internal sealed class HttpScheduledActionsClient(
    IHttpClientFactory httpClientFactory,
    IOptions<ContentSchedulerOptions> options,
    TimeProvider timeProvider) : IScheduledActionsClient
{
    public const string HttpClientName = "Activout.C9.Scheduler.ScheduledActions";
    public const string MediaType = "application/vnd.contentful.management.v1+json";
    private const int MaxRateLimitRetries = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string SpaceId => Uri.EscapeDataString(options.Value.SpaceId);
    private string EnvironmentQuery => "environment.sys.id=" + Uri.EscapeDataString(options.Value.Environment);

    public async Task<IReadOnlyList<ScheduledAction>> GetPending(CancellationToken cancellationToken)
    {
        var result = new List<ScheduledAction>();
        var basePath = $"spaces/{SpaceId}/scheduled_actions?{EnvironmentQuery}&sys.status=scheduled&limit=100";
        string? path = basePath;
        while (path is not null)
        {
            var page = Deserialize<CollectionDto>(await Send(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken));
            result.AddRange(page.Items.Select(ToModel));
            path = NextPagePath(basePath, page.Pages?.Next);
        }

        return result;
    }

    /// <summary>
    /// <c>pages.next</c> is usually a path (<c>/spaces/.../scheduled_actions?...&amp;next=...</c>), but
    /// accept a bare cursor too.
    /// </summary>
    internal static string? NextPagePath(string basePath, string? next)
    {
        if (string.IsNullOrEmpty(next)) return null;
        if (next.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return next;
        if (next.StartsWith('/')) return next.TrimStart('/');
        if (next.Contains('?')) return next;
        return $"{basePath}&next={Uri.EscapeDataString(next)}";
    }

    public Task<ScheduledAction> Create(ScheduledActionRequest request, CancellationToken cancellationToken) =>
        SendAction(() => new HttpRequestMessage(HttpMethod.Post, $"spaces/{SpaceId}/scheduled_actions")
        {
            Content = ToContent(request),
        }, cancellationToken);

    public Task<ScheduledAction> Update(string id, int version, ScheduledActionRequest request, CancellationToken cancellationToken) =>
        SendAction(() =>
        {
            var message = new HttpRequestMessage(HttpMethod.Put,
                $"spaces/{SpaceId}/scheduled_actions/{Uri.EscapeDataString(id)}?{EnvironmentQuery}")
            {
                Content = ToContent(request),
            };
            message.Headers.Add("X-Contentful-Version", version.ToString(CultureInfo.InvariantCulture));
            return message;
        }, cancellationToken);

    public Task Cancel(string id, CancellationToken cancellationToken) =>
        Send(() => new HttpRequestMessage(HttpMethod.Delete,
            $"spaces/{SpaceId}/scheduled_actions/{Uri.EscapeDataString(id)}?{EnvironmentQuery}"), cancellationToken);

    private async Task<ScheduledAction> SendAction(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken) =>
        ToModel(Deserialize<ScheduledActionDto>(await Send(createRequest, cancellationToken)));

    private static T Deserialize<T>(string body) =>
        JsonSerializer.Deserialize<T>(body, Json)
        ?? throw new InvalidOperationException("Empty response from Contentful Scheduled Actions API.");

    private async Task<string> Send(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        for (var attempt = 0; ; attempt++)
        {
            using var request = createRequest();
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRateLimitRetries)
            {
                await Task.Delay(RetryDelay(response), timeProvider, cancellationToken);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ScheduledActionsApiException(response.StatusCode,
                    $"Contentful Scheduled Actions API {request.Method} {request.RequestUri?.AbsolutePath} failed with {(int)response.StatusCode} {response.StatusCode}: {body}");
            }

            return body;
        }
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-Contentful-RateLimit-Reset", out var values)
            && int.TryParse(values.FirstOrDefault(), out var seconds) && seconds > 0)
            return TimeSpan.FromSeconds(Math.Min(seconds, 60));
        return response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
    }

    private JsonContent ToContent(ScheduledActionRequest request)
    {
        var dto = new ScheduledActionDto
        {
            Action = request.Action,
            Entity = LinkDto.To("Entry", request.EntryId),
            Environment = LinkDto.To("Environment", options.Value.Environment),
            ScheduledFor = new ScheduledForDto
            {
                Datetime = request.ScheduledFor.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                Timezone = request.TimeZone,
            },
        };
        return JsonContent.Create(dto, new MediaTypeHeaderValue(MediaType), Json);
    }

    private static ScheduledAction ToModel(ScheduledActionDto dto) => new(
        dto.Sys?.Id ?? "",
        dto.Sys?.Version ?? 0,
        dto.Action ?? "",
        dto.Entity?.Sys?.LinkType ?? "",
        dto.Entity?.Sys?.Id ?? "",
        DateTimeOffset.Parse(dto.ScheduledFor?.Datetime ?? "", CultureInfo.InvariantCulture).ToUniversalTime(),
        dto.ScheduledFor?.Timezone,
        dto.Sys?.CreatedBy?.Sys?.Id);

    // Transport DTOs: only the fields the scheduler needs.
    private sealed class CollectionDto
    {
        public List<ScheduledActionDto> Items { get; set; } = [];
        public PagesDto? Pages { get; set; }
    }

    private sealed class PagesDto
    {
        public string? Next { get; set; }
    }

    private sealed class ScheduledActionDto
    {
        public SysDto? Sys { get; set; }
        public string? Action { get; set; }
        public LinkDto? Entity { get; set; }
        public LinkDto? Environment { get; set; }
        public ScheduledForDto? ScheduledFor { get; set; }
    }

    private sealed class SysDto
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? LinkType { get; set; }
        public int? Version { get; set; }
        public LinkDto? CreatedBy { get; set; }
    }

    private sealed class LinkDto
    {
        public SysDto? Sys { get; set; }

        public static LinkDto To(string linkType, string id) =>
            new() { Sys = new SysDto { Type = "Link", LinkType = linkType, Id = id } };
    }

    private sealed class ScheduledForDto
    {
        public string? Datetime { get; set; }
        public string? Timezone { get; set; }
    }
}
