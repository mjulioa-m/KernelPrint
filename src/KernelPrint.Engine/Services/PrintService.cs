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
            var security = _engineOptions.Templates;

            var jsonPayload = JsonSerializer.Serialize(request.Data, JsonSerializerOptions);
            var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(jsonPayload);
            if (payloadBytes > security.MaxDataJsonBytes)
            {
                throw new ArgumentException($"Data JSON exceeds MaxDataJsonBytes ({security.MaxDataJsonBytes}).", nameof(request));
            }

            var templateUrl = ResolveTemplateUrl(request);
            var navigationUri = new Uri(templateUrl, UriKind.Absolute);
            ValidateTemplateUri(navigationUri, security);

            using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            jobCts.CancelAfter(TimeSpan.FromMilliseconds(request.Options.WaitFor.MaxRenderTimeoutMs));

            var navigationTimeoutMs = Math.Min(security.MaxNavigationTimeoutMs, request.Options.WaitFor.MaxRenderTimeoutMs);
            var pdfTimeoutMs = Math.Min(security.MaxPdfTimeoutMs, request.Options.WaitFor.MaxRenderTimeoutMs);
            var readyTimeoutMs = Math.Min(security.MaxReadySignalTimeoutMs, request.Options.WaitFor.MaxRenderTimeoutMs);

            await InstallResourceAllowlistAsync(page, navigationUri, security, jobCts.Token);

            await page.GotoAsync(templateUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = navigationTimeoutMs
            });

            if (request.Options.WaitFor.NetworkIdleMs > 0)
            {
                await page.WaitForTimeoutAsync(request.Options.WaitFor.NetworkIdleMs);
            }

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
            await page.WaitForTimeoutAsync(150);

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

            await WaitForTemplateReadyAsync(page, request, readyTimeoutMs, jobCts.Token);

            var renderedRowCount = await page.EvaluateAsync<int>(
                "() => document.querySelectorAll('tbody tr').length");

            var pdfTask = page.PdfAsync(new PagePdfOptions
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

            var pdfBytes = await RunWithTimeoutAsync(pdfTask, pdfTimeoutMs, jobCts.Token);

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
        var templateId = request.TemplateId!.Trim();
        if (templateId.StartsWith('/') || templateId.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("TemplateId must be a simple identifier without path traversal.", nameof(request));
        }

        return $"{baseUrl}/{templateId}";
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

        if (request.Options.WaitFor.MaxRenderTimeoutMs <= 0)
        {
            throw new ArgumentException("Options.WaitFor.MaxRenderTimeoutMs must be > 0.", nameof(request));
        }
    }

    private static void ValidateTemplateUri(Uri uri, KernelPrintTemplateSecurityOptions security)
    {
        if (security.BlockDataUrls && string.Equals(uri.Scheme, "data", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("data: template URLs are disabled by policy.", nameof(uri));
        }

        if (!security.AllowedSchemes.Any(s => string.Equals(s, uri.Scheme, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"URL scheme '{uri.Scheme}' is not allowed.", nameof(uri));
        }

        if (!HostAllowlistMatcher.IsHostAllowed(uri.Host, security.AllowedHosts))
        {
            throw new ArgumentException($"Template host '{uri.Host}' is not allowlisted.", nameof(uri));
        }
    }

    private static async Task InstallResourceAllowlistAsync(
        IPage page,
        Uri navigationUri,
        KernelPrintTemplateSecurityOptions security,
        CancellationToken cancellationToken)
    {
        if (!security.EnforceResourceHostAllowlist)
        {
            return;
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in security.AllowedHosts)
        {
            allowed.Add(h);
        }

        foreach (var h in security.ExtraResourceAllowedHosts)
        {
            allowed.Add(h);
        }

        // Always allow the navigation host itself.
        allowed.Add(navigationUri.Host);

        await page.RouteAsync("**/*", async route =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await route.AbortAsync();
                return;
            }

            var req = route.Request;
            var type = req.ResourceType;

            // Navigation/frame navigations must stay on allowlisted hosts.
            if (type is "document" or "frame")
            {
                if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var u))
                {
                    await route.AbortAsync();
                    return;
                }

                if (!HostAllowlistMatcher.IsHostAllowed(u.Host, allowed))
                {
                    await route.AbortAsync();
                    return;
                }

                await route.ContinueAsync();
                return;
            }

            if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var url))
            {
                await route.AbortAsync();
                return;
            }

            if (string.Equals(url.Scheme, "data", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url.Scheme, "blob", StringComparison.OrdinalIgnoreCase))
            {
                await route.ContinueAsync();
                return;
            }

            if (!HostAllowlistMatcher.IsHostAllowed(url.Host, allowed))
            {
                await route.AbortAsync();
                return;
            }

            await route.ContinueAsync();
        });
    }

    private static async Task WaitForTemplateReadyAsync(IPage page, PrintRequest request, int readyTimeoutMs, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Options.WaitFor.ReadySelector))
        {
            await page.WaitForSelectorAsync(request.Options.WaitFor.ReadySelector!, new PageWaitForSelectorOptions
            {
                Timeout = readyTimeoutMs,
                State = WaitForSelectorState.Visible
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.Options.WaitFor.ReadyExpression))
        {
            await page.WaitForFunctionAsync(request.Options.WaitFor.ReadyExpression!, new PageWaitForFunctionOptions
            {
                Timeout = readyTimeoutMs
            });
            return;
        }

        // Default readiness contract for KernelPrint templates.
        await page.WaitForFunctionAsync(
            """
            () => window.__KERNELPRINT_READY__ === true
            """,
            new PageWaitForFunctionOptions
            {
                Timeout = readyTimeoutMs
            });
    }

    private static async Task<byte[]> RunWithTimeoutAsync(Task<byte[]> task, int timeoutMs, CancellationToken cancellationToken)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs, cancellationToken));
        if (completed != task)
        {
            throw new TimeoutException($"Operation timed out after {timeoutMs}ms.");
        }

        return await task;
    }
}
