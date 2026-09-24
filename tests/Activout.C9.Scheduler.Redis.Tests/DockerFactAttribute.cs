using System.Net.Sockets;

namespace Activout.C9.Scheduler.Redis.Tests;

/// <summary>Skips locally when no Docker daemon is reachable. Never skips in CI.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly bool DockerAvailable = Environment.GetEnvironmentVariable("CI") == "true" || CanConnect();

    public DockerFactAttribute()
    {
        if (!DockerAvailable) Skip = "Docker is not available";
    }

    private static bool CanConnect()
    {
        var host = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrEmpty(host)) return true; // trust explicit configuration
        foreach (var path in new[] { "/var/run/docker.sock", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker/run/docker.sock") })
        {
            try
            {
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(path));
                return true;
            }
            catch (SocketException)
            {
            }
        }

        return false;
    }
}
