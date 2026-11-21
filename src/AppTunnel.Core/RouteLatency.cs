namespace AppTunnel.Core;

public sealed class RouteLatency
{
    private string? session;
    private TimeSpan? firstSlowSample;
    private TimeSpan lastSample;
    private TimeSpan nextSearch;

    public const long SlowMilliseconds = 400;

    public bool ShouldSearch(string currentSession, TimeSpan now, long? milliseconds)
    {
        if (session != currentSession || now - lastSample > TimeSpan.FromSeconds(3))
            firstSlowSample = null;
        session = currentSession;
        lastSample = now;

        if (milliseconds is null or <= SlowMilliseconds)
        {
            firstSlowSample = null;
            return false;
        }

        firstSlowSample ??= now;
        if (now - firstSlowSample.Value < TimeSpan.FromSeconds(5) || now < nextSearch) return false;
        nextSearch = now + TimeSpan.FromSeconds(30);
        return true;
    }
}
