using KernelPrint.Contracts;
using KernelPrint.Engine.Abstractions;
using KernelPrint.Engine.Configuration;
using KernelPrint.Engine.Exceptions;
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
        ValidatePrintRequest(request);

        return _browserPool.WithPageAsync(
            page => RunPrintJobAsync(page, request, cancellationToken),
            cancellationToken);
    }

    public async Task<PrintResult> TryGenerateAsync(PrintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePrintRequest(request);

        var (acquired, result) = await _browserPool.TryWithPageAsync(
            page => RunPrintJobAsync(page, request, cancellationToken),
            cancellationToken);

        if (!acquired || result is null)
        {
            throw new ServiceSaturatedException();
        }

        return result;
    }

    public async Task<byte[]> TryGeneratePreviewPngAsync(
        PrintPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePreviewRequest(request);

        var (acquired, result) = await _browserPool.TryWithPageAsync(
            page => RunPreviewPngJobAsync(page, request, cancellationToken),
            cancellationToken);

        if (!acquired || result is null)
        {
            throw new ServiceSaturatedException();
        }

        return result;
    }

    private async Task<PrintResult> RunPrintJobAsync(IPage page, PrintRequest request, CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var security = _engineOptions.Templates;

        var jsonPayload = JsonSerializer.Serialize(request.Data, JsonSerializerOptions);
        var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(jsonPayload);
        if (payloadBytes > security.MaxDataJsonBytes)
        {
            throw new ArgumentException($"Data JSON exceeds MaxDataJsonBytes ({security.MaxDataJsonBytes}).", nameof(request));
        }

        var templateUrl = ResolveTemplateUrl(request.TemplateId, request.TemplateUrl);
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

        await WaitForTemplateReadyAsync(page, request.Options, readyTimeoutMs, jobCts.Token);

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
    }

    private async Task<byte[]> RunPreviewPngJobAsync(
        IPage page,
        PrintPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var security = _engineOptions.Templates;

        var jsonPayload = JsonSerializer.Serialize(request.Data, JsonSerializerOptions);
        var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(jsonPayload);
        if (payloadBytes > security.MaxDataJsonBytes)
        {
            throw new ArgumentException($"Data JSON exceeds MaxDataJsonBytes ({security.MaxDataJsonBytes}).", nameof(request));
        }

        var templateUrl = ResolveTemplateUrl(request.TemplateId, request.TemplateUrl);
        var navigationUri = new Uri(templateUrl, UriKind.Absolute);
        ValidateTemplateUri(navigationUri, security);

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        jobCts.CancelAfter(TimeSpan.FromMilliseconds(request.Options.WaitFor.MaxRenderTimeoutMs));

        var navigationTimeoutMs = Math.Min(security.MaxNavigationTimeoutMs, request.Options.WaitFor.MaxRenderTimeoutMs);
        var readyTimeoutMs = Math.Min(security.MaxReadySignalTimeoutMs, request.Options.WaitFor.MaxRenderTimeoutMs);

        await InstallResourceAllowlistAsync(page, navigationUri, security, jobCts.Token);

        var vw = Math.Clamp(request.MaxWidth, 320, 4096);
        var vh = Math.Clamp(request.MaxHeight, 240, 10_000);
        await page.SetViewportSizeAsync(vw, vh);

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

        await page.WaitForTimeoutAsync(150);

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

        await WaitForTemplateReadyAsync(page, request.Options, readyTimeoutMs, jobCts.Token);

        return await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = false
        });
    }

    private string ResolveTemplateUrl(string? templateId, string? templateUrl)
    {
        if (!string.IsNullOrWhiteSpace(templateUrl))
        {
            return templateUrl;
        }

        var baseUrl = _engineOptions.TemplateBaseUrl.TrimEnd('/');
        var id = templateId!.Trim();
        if (id.StartsWith('/') || id.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("TemplateId must be a simple identifier without path traversal.", nameof(templateId));
        }

        return $"{baseUrl}/{id}";
    }

    private static string MapFormat(PdfPageFormat format) => format switch
    {
        PdfPageFormat.Letter => "Letter",
        PdfPageFormat.Legal => "Legal",
        _ => "A4"
    };

    private static void ValidatePrintRequest(PrintRequest request)
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

    private static void ValidatePreviewRequest(PrintPreviewRequest request)
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

    private static async Task WaitForTemplateReadyAsync(
        IPage page,
        PrintOptions options,
        int readyTimeoutMs,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.WaitFor.ReadySelector))
        {
            await page.WaitForSelectorAsync(options.WaitFor.ReadySelector!, new PageWaitForSelectorOptions
            {
                Timeout = readyTimeoutMs,
                State = WaitForSelectorState.Visible
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.WaitFor.ReadyExpression))
        {
            await page.WaitForFunctionAsync(options.WaitFor.ReadyExpression!, new PageWaitForFunctionOptions
            {
                Timeout = readyTimeoutMs
            });
            return;
        }

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
