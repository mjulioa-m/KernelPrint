using System.Net.Http.Json;
using System.Text.Json;
using KernelPrint.Contracts;

namespace KernelPrint.Client;

public sealed class KernelPrintClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public KernelPrintClient(HttpClient http)
    {
        _http = http;
    }

    public void ConfigureBaseAddress(Uri baseAddress) => _http.BaseAddress = baseAddress;

    public void ConfigureApiKey(string apiKey)
    {
        _http.DefaultRequestHeaders.Remove("X-Api-Key");
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    public void ConfigureCorrelationId(string correlationId)
    {
        _http.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _http.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
    }

    public async Task<PrintPdfResult> PrintPdfAsync(PrintRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/print")
        {
            Content = JsonContent.Create(request, options: SerializerOptions)
        };

        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new PrintPdfResult
        {
            Bytes = bytes,
            ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf",
            FileName = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'),
            Timings = ParseTimings(response),
            Metadata = ParseMetadata(response)
        };
    }

    public async Task<PrintPdfJsonResponse> PrintPdfJsonAsync(PrintRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/print")
        {
            Content = JsonContent.Create(request, options: SerializerOptions)
        };

        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var dto = await response.Content.ReadFromJsonAsync<PrintPdfJsonResponse>(SerializerOptions, cancellationToken);
        if (dto is null)
        {
            throw new KernelPrintApiException(
                System.Net.HttpStatusCode.BadGateway,
                TryGetCorrelationId(response),
                "Invalid Response",
                "Expected JSON body compatible with PrintPdfJsonResponse.",
                await response.Content.ReadAsStringAsync(cancellationToken));
        }

        return dto;
    }

    public async Task<byte[]> PrintPreviewPngAsync(PrintPreviewRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/print/preview")
        {
            Content = JsonContent.Create(request, options: SerializerOptions)
        };

        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static PrintTimings ParseTimings(HttpResponseMessage response)
    {
        long totalMs = 0;
        long renderMs = 0;

        if (response.Headers.TryGetValues("X-KernelPrint-TotalMs", out var totalValues) &&
            long.TryParse(totalValues.FirstOrDefault(), out var parsedTotal))
        {
            totalMs = parsedTotal;
        }

        if (response.Headers.TryGetValues("X-KernelPrint-RenderMs", out var renderValues) &&
            long.TryParse(renderValues.FirstOrDefault(), out var parsedRender))
        {
            renderMs = parsedRender;
        }

        return new PrintTimings { TotalMs = totalMs, RenderMs = renderMs };
    }

    private static IReadOnlyDictionary<string, string> ParseMetadata(HttpResponseMessage response)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            if (!header.Key.StartsWith("X-KernelPrint-Meta-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = header.Key["X-KernelPrint-Meta-".Length..].Replace('-', ' ');
            dict[name] = header.Value.FirstOrDefault() ?? string.Empty;
        }

        return dict;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var correlationId = TryGetCorrelationId(response);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        string? title = null;
        string? detail = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = doc.RootElement;
            if (root.TryGetProperty("title", out var titleEl))
            {
                title = titleEl.GetString();
            }

            if (root.TryGetProperty("detail", out var detailEl))
            {
                detail = detailEl.GetString();
            }
        }
        catch
        {
            // ignore parse failures
        }

        throw new KernelPrintApiException(response.StatusCode, correlationId, title, detail, body);
    }

    private static string? TryGetCorrelationId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-Correlation-Id", out var values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }
}
