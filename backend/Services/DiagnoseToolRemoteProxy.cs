using System.Net.Http.Headers;
using System.Text.Json;

namespace HardwareCheck.Api.Services;

/// <summary>
/// Leitet REST-Aufrufe an die DiagnoseTool-Desktop-App (Port 8085) eines Clients weiter,
/// damit Hardware-Check den vollständigen Funktionsumfang des DiagnoseTools fernsteuern kann
/// (Tools, Reparatur, HWiNFO, Stresstests, Akkutest, Aufzeichnung, PDF-Berichte, Oberfläche).
/// </summary>
public static class DiagnoseToolRemoteProxy
{
    public static (string Host, int Port)? ResolveTarget(string id, LhmDiscoveryAndMonitorService monitorService)
    {
        if (string.Equals(id, "local-8085", StringComparison.OrdinalIgnoreCase))
        {
            return ("127.0.0.1", 8085);
        }

        var ep = monitorService.GetEndpointById(id);
        if (ep == null || string.IsNullOrWhiteSpace(ep.Host))
        {
            return null;
        }
        return (ep.Host, ep.Port > 0 ? ep.Port : 8085);
    }

    public static async Task<IResult> ForwardAsync(
        string id,
        string path,
        HttpContext httpContext,
        LhmDiscoveryAndMonitorService monitorService,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var target = ResolveTarget(id, monitorService);
        if (target == null)
        {
            return Results.NotFound(new { error = $"Gerät '{id}' wurde nicht gefunden oder ist ein Demo-Gerät." });
        }

        string cleanPath = (path ?? string.Empty).Trim().TrimStart('/');
        if (cleanPath.Contains("..", StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "Ungültiger Pfad." });
        }

        // "remote" ohne Pfad liefert den Endpunkt-Katalog des DiagnoseTools
        string remotePath = cleanPath.Length == 0 ? "api" : $"api/{cleanPath}";
        string targetUrl = $"http://{target.Value.Host}:{target.Value.Port}/{remotePath}{httpContext.Request.QueryString}";

        var method = HttpMethods.IsPost(httpContext.Request.Method) ? HttpMethod.Post : HttpMethod.Get;
        using var outbound = new HttpRequestMessage(method, targetUrl);

        if (method == HttpMethod.Post)
        {
            string body;
            using (var reader = new StreamReader(httpContext.Request.Body, System.Text.Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync(ct);
            }
            outbound.Content = new StringContent(string.IsNullOrWhiteSpace(body) ? "{}" : body, System.Text.Encoding.UTF8, "application/json");
        }

        try
        {
            var client = httpClientFactory.CreateClient("DiagnoseToolRemote");
            using var resp = await client.SendAsync(outbound, HttpCompletionOption.ResponseContentRead, ct);
            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            string contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";

            ContentDispositionHeaderValue? disposition = resp.Content.Headers.ContentDisposition;
            if (disposition == null && resp.Headers.TryGetValues("Content-Disposition", out var cdValues))
            {
                ContentDispositionHeaderValue.TryParse(cdValues.FirstOrDefault(), out disposition);
            }

            if (disposition != null && !string.IsNullOrWhiteSpace(disposition.FileName ?? disposition.FileNameStar))
            {
                string fileName = (disposition.FileNameStar ?? disposition.FileName ?? "download").Trim('"');
                if (resp.IsSuccessStatusCode)
                {
                    return Results.File(bytes, contentType, fileName);
                }
            }

            return new RawProxyResult(bytes, contentType, (int)resp.StatusCode);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Json(new { error = $"Zeitüberschreitung beim DiagnoseTool ({targetUrl})." }, statusCode: 504);
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = $"Verbindung zum DiagnoseTool fehlgeschlagen ({targetUrl}): {ex.Message}" }, statusCode: 502);
        }
    }

    public static async Task<JsonElement?> TryGetJsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using var resp = await client.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private sealed class RawProxyResult : IResult
    {
        private readonly byte[] _content;
        private readonly string _contentType;
        private readonly int _statusCode;

        public RawProxyResult(byte[] content, string contentType, int statusCode)
        {
            _content = content;
            _contentType = contentType;
            _statusCode = statusCode;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = _statusCode;
            httpContext.Response.ContentType = _contentType;
            httpContext.Response.ContentLength = _content.Length;
            await httpContext.Response.Body.WriteAsync(_content, httpContext.RequestAborted);
        }
    }
}
