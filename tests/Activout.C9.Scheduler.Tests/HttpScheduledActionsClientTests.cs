using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Activout.C9.Scheduler.Tests;

public class HttpScheduledActionsClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request, body);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.contentful.com/") };
    }

    private static HttpScheduledActionsClient Client(StubHandler handler) =>
        new(new Factory(handler), Options.Create(new ContentSchedulerOptions { SpaceId = "space", Environment = "master" }), TimeProvider.System);

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json) };

    private static string Action(string id, string createdBy) =>
        """{"sys":{"id":"ID","version":3,"status":"scheduled","createdBy":{"sys":{"type":"Link","linkType":"User","id":"BY"}}},"""
            .Replace("ID", id).Replace("BY", createdBy) +
        """
        "action":"publish","entity":{"sys":{"type":"Link","linkType":"Entry","id":"e1"}},
        "environment":{"sys":{"type":"Link","linkType":"Environment","id":"master"}},
        "scheduledFor":{"datetime":"2026-09-24T23:30:00.000+02:00","timezone":"Europe/Stockholm"}}
        """;

    [Fact]
    public async Task GetPending_FollowsPagination()
    {
        var handler = new StubHandler((request, _) => request.RequestUri!.Query.Contains("next=cursor")
            ? Json("""{"items":[""" + Action("b", "other") + """],"pages":{}}""")
            : Json("""{"items":[""" + Action("a", "me") + """],"pages":{"next":"/spaces/space/scheduled_actions?environment.sys.id=master&next=cursor"}}"""));

        var actions = await Client(handler).GetPending(default);

        Assert.Equal(["a", "b"], actions.Select(a => a.Id));
        Assert.Equal(
            "https://api.contentful.com/spaces/space/scheduled_actions?environment.sys.id=master&sys.status=scheduled&limit=100",
            handler.Requests[0].Request.RequestUri!.ToString());
        var a = actions[0];
        Assert.Equal(("me", "Entry", "e1", 3, "Europe/Stockholm"), (a.CreatedById, a.EntityType, a.EntityId, a.Version, a.TimeZone));
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 21, 30, 0, TimeSpan.Zero), a.ScheduledFor);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("/spaces/s/scheduled_actions?next=x", "spaces/s/scheduled_actions?next=x")]
    [InlineData("abc", "base?q=1&next=abc")]
    public void NextPagePath(string? next, string? expected) =>
        Assert.Equal(expected, HttpScheduledActionsClient.NextPagePath("base?q=1", next));

    [Fact]
    public async Task Create_PostsDocumentedPayload()
    {
        var handler = new StubHandler((_, _) => Json(Action("new", "me"), HttpStatusCode.Created));

        await Client(handler).Create(new ScheduledActionRequest("unpublish", "e1",
            new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero), "Europe/Stockholm"), default);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("application/vnd.contentful.management.v1+json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(
            """{"action":"unpublish","entity":{"sys":{"id":"e1","type":"Link","linkType":"Entry"}},"environment":{"sys":{"id":"master","type":"Link","linkType":"Environment"}},"scheduledFor":{"datetime":"2026-09-24T22:00:00.000Z","timezone":"Europe/Stockholm"}}""",
            body);
    }

    [Fact]
    public async Task Update_SendsVersion_AndCancel_Deletes()
    {
        var handler = new StubHandler((_, _) => Json(Action("a", "me")));
        var client = Client(handler);

        await client.Update("a", 3, new ScheduledActionRequest("publish", "e1", DateTimeOffset.UnixEpoch, "UTC"), default);
        await client.Cancel("a", default);

        Assert.Equal("3", handler.Requests[0].Request.Headers.GetValues("X-Contentful-Version").Single());
        Assert.Equal(HttpMethod.Put, handler.Requests[0].Request.Method);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Request.Method);
        Assert.EndsWith("/scheduled_actions/a?environment.sys.id=master", handler.Requests[1].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Errors_SurfaceStatusAndBody()
    {
        var handler = new StubHandler((_, _) => Json("""{"sys":{"id":"ValidationFailed"},"message":"limit reached"}""", HttpStatusCode.UnprocessableEntity));

        var ex = await Assert.ThrowsAsync<ScheduledActionsApiException>(() => Client(handler).Cancel("a", default));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("limit reached", ex.Message);
    }
}
