using Microsoft.AspNetCore.Mvc;
using GrafanaProxy.Services;
using System.Text.Json;
using System.Text;

[ApiController]
[Route("api")]
public class LokiController : ControllerBase
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _config;
    private readonly SessionCookieStore _store;
    private readonly string _grafanaBase;

    public LokiController(IHttpClientFactory factory, IConfiguration config, SessionCookieStore store)
    {
        _factory = factory;
        _config = config;
        _store = store;
        _grafanaBase = _config.GetValue<string>("GrafanaBaseUrl")?.TrimEnd('/') ?? throw new InvalidOperationException("GrafanaBaseUrl not set");
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string relative)
    {
        var msg = new HttpRequestMessage(method, $"{_grafanaBase}{relative}");
        var cookie = _store.GetCookieHeader();
        if (!string.IsNullOrEmpty(cookie))
            msg.Headers.Add("Cookie", cookie);
        return msg;
    }

    [HttpGet("labels")]
    public async Task<IActionResult> Labels()
    {
        if (!_store.Has) return Unauthorized("No grafana session. Please login via proxy.");
        var client = _factory.CreateClient();
        var req = BuildRequest(HttpMethod.Get, "/loki/api/v1/labels");
        var resp = await client.SendAsync(req);
        var content = await resp.Content.ReadAsStringAsync();
        return Content(content, resp.Content.Headers.ContentType?.ToString() ?? "application/json");
    }

    [HttpGet("label/{name}/values")]
    public async Task<IActionResult> LabelValues(string name)
    {
        if (!_store.Has) return Unauthorized("No grafana session. Please login via proxy.");
        var client = _factory.CreateClient();
        var req = BuildRequest(HttpMethod.Get, $"/loki/api/v1/label/{Uri.EscapeDataString(name)}/values");
        var resp = await client.SendAsync(req);
        var content = await resp.Content.ReadAsStringAsync();
        return Content(content, resp.Content.Headers.ContentType?.ToString() ?? "application/json");
    }

    [HttpGet("query_range")]
    public async Task<IActionResult> QueryRange([FromQuery] string query, [FromQuery] long start, [FromQuery] long end, [FromQuery] int limit = 1000)
    {
        if (!_store.Has) return Unauthorized("No grafana session. Please login via proxy.");
        var client = _factory.CreateClient();
        var q = $"?query={Uri.EscapeDataString(query)}&start={start}&end={end}&limit={limit}";
        var req = BuildRequest(HttpMethod.Get, $"/loki/api/v1/query_range{q}");
        var resp = await client.SendAsync(req);
        var content = await resp.Content.ReadAsStringAsync();
        return Content(content, resp.Content.Headers.ContentType?.ToString() ?? "application/json");
    }

    // New: Unified search endpoint (alias of query_range)
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] long start, [FromQuery] long end, [FromQuery] int limit = 1000)
    {
        return await QueryRange(query, start, end, limit);
    }

    public class ExportRequest
    {
        public string Query { get; set; } = string.Empty;
        public long Start { get; set; }
        public long End { get; set; }
        public int Limit { get; set; } = 1000;
        public string Format { get; set; } = "json"; // "json" or "csv"
    }

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] ExportRequest reqModel)
    {
        if (!_store.Has) return Unauthorized("No grafana session. Please login via proxy.");
        if (string.IsNullOrWhiteSpace(reqModel.Query)) return BadRequest("Query is required");
        var client = _factory.CreateClient();
        var q = $"?query={Uri.EscapeDataString(reqModel.Query)}&start={reqModel.Start}&end={reqModel.End}&limit={reqModel.Limit}";
        var req = BuildRequest(HttpMethod.Get, $"/loki/api/v1/query_range{q}");
        var resp = await client.SendAsync(req);
        var content = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            return StatusCode((int)resp.StatusCode, content);
        }

        var format = (reqModel.Format ?? "json").ToLowerInvariant();
        if (format == "csv")
        {
            var csv = ConvertLokiJsonToCsv(content);
            var fileName = $"loki-export-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
        }
        else
        {
            var fileName = $"loki-export-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            return File(Encoding.UTF8.GetBytes(content), "application/json", fileName);
        }
    }

    private string ConvertLokiJsonToCsv(string lokiJson)
    {
        using var doc = JsonDocument.Parse(lokiJson);
        var sb = new StringBuilder();
        sb.AppendLine("timestamp,line,stream");
        if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("result", out var result))
        {
            foreach (var item in result.EnumerateArray())
            {
                string streamLabels = string.Empty;
                if (item.TryGetProperty("stream", out var stream))
                {
                    // serialize labels dictionary to single string like key=value pairs
                    var parts = new List<string>();
                    foreach (var prop in stream.EnumerateObject())
                    {
                        parts.Add($"{prop.Name}={prop.Value.GetString()}");
                    }
                    streamLabels = string.Join(";", parts);
                }
                if (item.TryGetProperty("values", out var values))
                {
                    foreach (var v in values.EnumerateArray())
                    {
                        // value is [timestamp, line]
                        var ts = v[0].GetString() ?? "";
                        var line = v[1].GetString() ?? "";
                        // escape quotes and commas in line
                        var safeLine = line.Replace("\"", "\"\"");
                        if (safeLine.Contains(',') || safeLine.Contains('\n'))
                            safeLine = $"\"{safeLine}\"";
                        var safeStream = streamLabels.Replace("\"", "\"\"");
                        if (safeStream.Contains(',') || safeStream.Contains('\n'))
                            safeStream = $"\"{safeStream}\"";
                        sb.AppendLine($"{ts},{safeLine},{safeStream}");
                    }
                }
            }
        }
        return sb.ToString();
    }
}