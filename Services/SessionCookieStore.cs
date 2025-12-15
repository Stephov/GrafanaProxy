namespace GrafanaProxy.Services;

public class SessionCookieStore
{
    private string? _cookieHeader;
    private readonly object _lock = new();

    public void SetCookieHeader(string header)
    {
        lock (_lock)
        {
            _cookieHeader = header;
        }
    }

    public string? GetCookieHeader()
    {
        lock (_lock)
        {
            return _cookieHeader;
        }
    }

    public bool Has => GetCookieHeader() != null;
}