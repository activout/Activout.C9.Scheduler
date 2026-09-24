using System.Net;

namespace Activout.C9.Scheduler;

internal sealed class ScheduledActionsApiException(HttpStatusCode statusCode, string message)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
