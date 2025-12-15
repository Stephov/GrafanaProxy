using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using GrafanaProxy.Services;

[ApiController]
[Route("grafana/{**rest}")]
public class ProxyController : ControllerBase
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _config;
    private readonly SessionCookieStore _store;
    private readonly string _grafanaBase;

    public ProxyController(IHttpClientFactory factory, IConfiguration config, SessionCookieStore store)
    {
        _factory = factory;
        _config = config;
        _store = store;
        _grafanaBase = _config.GetValue<string>("GrafanaBaseUrl")?.TrimEnd('/') ?? throw new InvalidOperationException("GrafanaBaseUrl not set");
    }

    [HttpGet, HttpPost, HttpPut, HttpDelete, HttpPatch]
    public async Task Proxy()
    {
        var rest = (string?)RouteData.Values["rest"] ?? string.Empty;
        var target = string.IsNullOrEmpty(rest) ? _grafanaBase : $"{_grafanaBase}/{rest}";

        var requestMessage = new HttpRequestMessage(new HttpMethod(Request.Method), target);

        // Copy content
        if (Request.ContentLength > 0)
        {
            requestMessage.Content = new StreamContent(Request.Body);
            if (Request.ContentType != null)
                requestMessage.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(Request.ContentType);
        }

        // Copy headers except Host
        foreach (var header in Request.Headers)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
            if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        var client = _factory.CreateClient();
        var resp = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);

        // Capture Set-Cookie
        if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            // store full cookie header(s) concatenated
            var combined = string.Join("; ", setCookies);
            _store.SetCookieHeader(combined);
        }

        // copy status
        Response.StatusCode = (int)resp.StatusCode;

        // copy response headers
        foreach (var header in resp.Headers)
            Response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in resp.Content.Headers)
            Response.Headers[header.Key] = header.Value.ToArray();

        // Prevent automatic transfer-encoding header duplication
        Response.Headers.Remove("transfer-encoding");

        // copy content
        await resp.Content.CopyToAsync(Response.Body);
    }
}