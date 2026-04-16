using KernelPrint.Contracts;
using KernelPrint.Engine.Abstractions;
using KernelPrint.Engine.Configuration;
using KernelPrint.Engine.Runtime;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using System.Diagnostics;
using System.Text.Json;

namespace KernelPrint.Engine.Services;

internal sealed class PrintService : IPrintService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IBrowserPool _browserPool;
    private readonly KernelPrintEngineOptions _engineOptions;

    public PrintService(IBrowserPool browserPool, IOptions<KernelPrintEngineOptions> options)
    {
        _browserPool = browserPool;
        _engineOptions = options.Value;
    }

    public Task<PrintResult> GenerateAsync(PrintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        return _browserPool.WithPageAsync(async page =>
        {
            var totalStopwatch = Stopwatch.StartNew();
            var templateUrl = ResolveTemplateUrl(request);

            await page.GotoAsync(templateUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = request.Options.WaitFor.MaxRenderTimeoutMs
            });

            if (request.Options.WaitFor.NetworkIdleMs > 0)
            {
                await page.WaitForTimeoutAsync(request.Options.WaitFor.NetworkIdleMs);
            }

            // Inyecta payload para templates React/Next y dispara evento para consumo en cliente.
            var jsonPayload = JsonSerializer.Serialize(request.Data, JsonSerializerOptions);
            await page.EvaluateAsync(
                """
                (payload) => {
                    const data = typeof payload === "string" ? JSON.parse(payload) : payload;
                    window.__KERNELPRINT_DATA__ = data;
                    window.dispatchEvent(new CustomEvent("kernelprint:data-ready", { detail: data }));
                }
                """,
                jsonPayload);

            // Allow client-side template rendering to flush DOM updates before printing.
            await page.WaitForTimeoutAsync(400);

            var renderStopwatch = Stopwatch.StartNew();

            if (request.Options.WaitFor.FontsReadyTimeoutMs > 0)
            {
                await page.EvaluateAsync(
                    """
                    async ({ timeoutMs }) => {
                        await Promise.race([
                            document.fonts.ready,
                            new Promise(resolve => setTimeout(resolve, timeoutMs))
                        ]);
                    }
                    """,
                    new { timeoutMs = request.Options.WaitFor.FontsReadyTimeoutMs });
            }

            var renderedRowCount = await page.EvaluateAsync<int>(
                "() => document.querySelectorAll('tbody tr').length");

            var pdfBytes = await page.PdfAsync(new PagePdfOptions
            {
                Format = MapFormat(request.Options.Format),
                Landscape = request.Options.Landscape,
                DisplayHeaderFooter = !string.IsNullOrWhiteSpace(request.Options.HeaderTemplateHtml) ||
                                      !string.IsNullOrWhiteSpace(request.Options.FooterTemplateHtml),
                HeaderTemplate = request.Options.HeaderTemplateHtml,
                FooterTemplate = request.Options.FooterTemplateHtml,
                Margin = new Margin
                {
                    Top = request.Options.Margins.Top,
                    Right = request.Options.Margins.Right,
                    Bottom = request.Options.Margins.Bottom,
                    Left = request.Options.Margins.Left
                },
                PrintBackground = true
            });

            renderStopwatch.Stop();
            totalStopwatch.Stop();

            return new PrintResult
            {
                Bytes = pdfBytes,
                ContentType = "application/pdf",
                FileName = "kernelprint-document.pdf",
                Metadata = new Dictionary<string, string>
                {
                    ["status"] = "ok",
                    ["template"] = request.TemplateId ?? request.TemplateUrl ?? "unknown",
                    ["renderedRows"] = renderedRowCount.ToString()
                },
                Timings = new PrintTimings
                {
                    TotalMs = totalStopwatch.ElapsedMilliseconds,
                    RenderMs = renderStopwatch.ElapsedMilliseconds
                }
            };
        }, cancellationToken);
    }

    private string ResolveTemplateUrl(PrintRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TemplateUrl))
        {
            return request.TemplateUrl;
        }

        var baseUrl = _engineOptions.TemplateBaseUrl.TrimEnd('/');
        return $"{baseUrl}/{request.TemplateId}";
    }

    private static string MapFormat(PdfPageFormat format) => format switch
    {
        PdfPageFormat.Letter => "Letter",
        PdfPageFormat.Legal => "Legal",
        _ => "A4"
    };

    private static void ValidateRequest(PrintRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TemplateId) && string.IsNullOrWhiteSpace(request.TemplateUrl))
        {
            throw new ArgumentException("Either TemplateId or TemplateUrl is required.", nameof(request));
        }
    }
}
