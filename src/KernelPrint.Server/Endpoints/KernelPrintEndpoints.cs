using KernelPrint.Contracts;
using KernelPrint.Engine.Abstractions;
using KernelPrint.Engine.Configuration;
using KernelPrint.Engine.Runtime;
using KernelPrint.Server.Auth;
using KernelPrint.Server.Configuration;
using KernelPrint.Server.Services;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Sockets;

namespace KernelPrint.Server.Endpoints;

public static class KernelPrintEndpoints
{
    public static void MapKernelPrint(this WebApplication app)
    {
        var meters = app.Services.GetRequiredService<KernelPrintMeters>();

        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .WithTags("Health")
            .WithSummary("Liveness probe");

        app.MapGet(
                "/health/ready",
                async (
                    BrowserPool browserPool,
                    IOptions<KernelPrintEngineOptions> engineOptions,
                    IOptions<KernelPrintServerOptions> serverOptions,
                    IHttpClientFactory httpClientFactory,
                    CancellationToken cancellationToken) =>
                {
                    var engine = engineOptions.Value;
                    var server = serverOptions.Value;
                    var checks = new Dictionary<string, object>();

                    var poolOk = await browserPool.IsReadyAsync(cancellationToken);
                    checks["browserPool"] = new { ok = poolOk };

                    try
                    {
                        if (!Uri.TryCreate(engine.TemplateBaseUrl, UriKind.Absolute, out var baseUri))
                        {
                            checks["templates"] = new { ok = false, error = "InvalidTemplateBaseUrl" };
                        }
                        else
                        {
                            using var client = new TcpClient();
                            var connectTask = client.ConnectAsync(baseUri.Host, baseUri.Port == -1 ? 80 : baseUri.Port, cancellationToken)
                                .AsTask();
                            var completed = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
                            checks["templates"] = new { ok = completed == connectTask && client.Connected };
                        }
                    }
                    catch (Exception ex)
                    {
                        checks["templates"] = new { ok = false, error = ex.GetType().Name };
                    }

                    if (!string.IsNullOrWhiteSpace(server.ReadinessHttpProbeUrl) &&
                        Uri.TryCreate(server.ReadinessHttpProbeUrl, UriKind.Absolute, out var probeUri))
                    {
                        try
                        {
                            using var http = httpClientFactory.CreateClient("kernelprint-readiness");
                            http.Timeout = TimeSpan.FromMilliseconds(Math.Clamp(server.ReadinessHttpProbeTimeoutMs, 500, 60_000));
                            using var response = await http.SendAsync(
                                new HttpRequestMessage(HttpMethod.Head, probeUri),
                                HttpCompletionOption.ResponseHeadersRead,
                                cancellationToken);
                            checks["httpProbe"] = new { ok = response.IsSuccessStatusCode, statusCode = (int)response.StatusCode };
                        }
                        catch (Exception ex)
                        {
                            checks["httpProbe"] = new { ok = false, error = ex.GetType().Name };
                        }
                    }

                    var ok = checks.Values.All(static v =>
                    {
                        if (v is not null && v.GetType().GetProperty("ok")?.GetValue(v) is bool b)
                        {
                            return b;
                        }

                        return false;
                    });

                    return ok
                        ? Results.Ok(new { status = "ready", checks })
                        : Results.Json(new { status = "not_ready", checks }, statusCode: StatusCodes.Status503ServiceUnavailable);
                })
            .WithTags("Health")
            .WithSummary("Readiness probe (browser pool, templates TCP, optional HTTP probe)");

        app.MapPost(
                "/v1/print",
                async (
                    PrintRequest request,
                    HttpContext httpContext,
                    IPrintService printService,
                    PrintProfilesStore profilesStore,
                    IOptions<KernelPrintServerOptions> serverOptions,
                    ILoggerFactory loggerFactory,
                    CancellationToken cancellationToken) =>
                {
                    var logger = loggerFactory.CreateLogger("KernelPrint.Print");
                    var validationErrors = ValidatePrintRequest(request);
                    if (validationErrors.Count > 0)
                    {
                        meters.RecordPrintOutcome("validation_error", httpContext.Items[ApiKeyResolution.ClientIdItemKey] as string);
                        return Results.ValidationProblem(validationErrors);
                    }

                    var merged = PrintProfilesStore.MergeRequestProfile(request, profilesStore);
                    var server = serverOptions.Value;

                    using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    if (server.RequestTimeoutMs > 0)
                    {
                        requestCts.CancelAfter(TimeSpan.FromMilliseconds(server.RequestTimeoutMs));
                    }

                    var stopwatch = Stopwatch.StartNew();
                    meters.PrintRequestCount.Add(1);
                    var clientId = httpContext.Items[ApiKeyResolution.ClientIdItemKey] as string;
                    logger.LogInformation(
                        "Print request started. CorrelationId={CorrelationId} ClientId={ClientId} TemplateId={TemplateId} TemplateUrl={TemplateUrl}",
                        httpContext.TraceIdentifier,
                        clientId,
                        merged.TemplateId,
                        RedactUrlForLogs(merged.TemplateUrl));

                    var result = await printService.TryGenerateAsync(merged, requestCts.Token);

                    stopwatch.Stop();
                    meters.PrintLatencyMs.Record(stopwatch.Elapsed.TotalMilliseconds);
                    meters.RecordPrintOutcome("ok", clientId);

                    httpContext.Response.Headers["X-KernelPrint-TotalMs"] = result.Timings.TotalMs.ToString();
                    httpContext.Response.Headers["X-KernelPrint-RenderMs"] = result.Timings.RenderMs.ToString();

                    foreach (var item in result.Metadata)
                    {
                        var normalizedKey = item.Key.Replace(' ', '-');
                        httpContext.Response.Headers[$"X-KernelPrint-Meta-{normalizedKey}"] = item.Value;
                    }

                    logger.LogInformation(
                        "Print request completed. CorrelationId={CorrelationId} ClientId={ClientId} Bytes={Bytes} TotalMs={TotalMs} RenderMs={RenderMs}",
                        httpContext.TraceIdentifier,
                        clientId,
                        result.Bytes.Length,
                        result.Timings.TotalMs,
                        result.Timings.RenderMs);

                    if (merged.ResponseMode == PrintResponseMode.JsonWithPdf)
                    {
                        if (result.Bytes.Length > server.MaxJsonResponsePdfBytes)
                        {
                            return Results.Problem(
                                statusCode: StatusCodes.Status413PayloadTooLarge,
                                title: "Payload Too Large",
                                detail: $"PDF exceeds MaxJsonResponsePdfBytes ({server.MaxJsonResponsePdfBytes}). Use PdfOnly response mode or increase the limit.",
                                type: "https://kernelprint.dev/problems/payload-too-large");
                        }

                        var json = new PrintPdfJsonResponse
                        {
                            ContentType = result.ContentType,
                            FileName = result.FileName ?? "kernelprint-document.pdf",
                            PdfBase64 = Convert.ToBase64String(result.Bytes),
                            Metadata = result.Metadata,
                            Timings = result.Timings
                        };

                        return Results.Json(json);
                    }

                    return Results.File(
                        result.Bytes,
                        contentType: result.ContentType,
                        fileDownloadName: result.FileName,
                        enableRangeProcessing: false);
                })
            .WithTags("Print")
            .WithSummary("Render a template to PDF")
            .WithDescription(
                "Requires X-Api-Key when the server is configured with credentials. " +
                "Supports X-Correlation-Id. Returns application/pdf by default, or application/json when responseMode is jsonWithPdf (subject to size limits). " +
                "Common errors: 400 (policy/validation), 401 (API key), 413 (JSON+PDF too large), 422 (model validation), 429 (rate limit), 503 (saturated pool), 504 (timeouts).")
            .RequireRateLimiting("kernelprint")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        app.MapPost(
                "/v1/print/preview",
                async (
                    PrintPreviewRequest request,
                    HttpContext httpContext,
                    IPrintService printService,
                    ILoggerFactory loggerFactory,
                    IOptions<KernelPrintServerOptions> serverOptions,
                    CancellationToken cancellationToken) =>
                {
                    var logger = loggerFactory.CreateLogger("KernelPrint.Preview");
                    var validationErrors = ValidatePreviewRequest(request);
                    if (validationErrors.Count > 0)
                    {
                        meters.RecordPrintOutcome("validation_error", httpContext.Items[ApiKeyResolution.ClientIdItemKey] as string);
                        return Results.ValidationProblem(validationErrors);
                    }

                    var server = serverOptions.Value;
                    using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    if (server.RequestTimeoutMs > 0)
                    {
                        requestCts.CancelAfter(TimeSpan.FromMilliseconds(server.RequestTimeoutMs));
                    }

                    meters.PreviewRequestCount.Add(1);
                    var clientId = httpContext.Items[ApiKeyResolution.ClientIdItemKey] as string;
                    logger.LogInformation(
                        "Preview request started. CorrelationId={CorrelationId} ClientId={ClientId} TemplateId={TemplateId}",
                        httpContext.TraceIdentifier,
                        clientId,
                        request.TemplateId);

                    var png = await printService.TryGeneratePreviewPngAsync(request, requestCts.Token);

                    meters.RecordPrintOutcome("preview_ok", clientId);
                    httpContext.Response.Headers["X-KernelPrint-Preview"] = "png";
                    return Results.Bytes(png, "image/png");
                })
            .WithTags("Print")
            .WithSummary("Render a template to a PNG preview (viewport capture)")
            .WithDescription(
                "Runs the same template readiness pipeline as PDF printing, then returns a PNG of the current viewport. " +
                "Useful for validating data and layout before paying the cost of PDF generation.")
            .RequireRateLimiting("kernelprint")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        app.MapPost(
                "/admin/pool/reset",
                async (
                    HttpContext httpContext,
                    IBrowserPool pool,
                    IOptions<KernelPrintAdminOptions> adminOptions,
                    CancellationToken cancellationToken) =>
                {
                    var admin = adminOptions.Value;
                    if (string.IsNullOrWhiteSpace(admin.ApiKey))
                    {
                        return Results.Problem(
                            statusCode: StatusCodes.Status503ServiceUnavailable,
                            title: "Admin Disabled",
                            detail: "KernelPrint:Admin:ApiKey is not configured.",
                            type: "https://kernelprint.dev/problems/admin-disabled");
                    }

                    if (!httpContext.Request.Headers.TryGetValue("X-Admin-Key", out var provided) ||
                        !string.Equals(provided.ToString(), admin.ApiKey, StringComparison.Ordinal))
                    {
                        return Results.Problem(
                            statusCode: StatusCodes.Status401Unauthorized,
                            title: "Unauthorized",
                            detail: "Missing or invalid X-Admin-Key.",
                            type: "https://kernelprint.dev/problems/unauthorized");
                    }

                    await pool.ResetAsync(cancellationToken);
                    return Results.Ok(new { status = "reset", at = DateTimeOffset.UtcNow });
                })
            .WithTags("Admin")
            .WithSummary("Reset the shared Chromium browser pool (dangerous; use for ops recovery)")
            .WithDescription(
                "Requires X-Admin-Key matching KernelPrint:Admin:ApiKey. Intended for operators when Chromium becomes unhealthy.");
    }

    private static Dictionary<string, string[]> ValidatePrintRequest(PrintRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.TemplateId) && string.IsNullOrWhiteSpace(request.TemplateUrl))
        {
            errors[nameof(PrintRequest.TemplateId)] = ["TemplateId o TemplateUrl es requerido."];
        }

        if (!string.IsNullOrWhiteSpace(request.TemplateUrl) &&
            !Uri.TryCreate(request.TemplateUrl, UriKind.Absolute, out _))
        {
            errors[nameof(PrintRequest.TemplateUrl)] = ["TemplateUrl debe ser una URL absoluta válida."];
        }

        if (request.Options.WaitFor.MaxRenderTimeoutMs <= 0)
        {
            errors["Options.WaitFor.MaxRenderTimeoutMs"] = ["MaxRenderTimeoutMs debe ser mayor a 0."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidatePreviewRequest(PrintPreviewRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.TemplateId) && string.IsNullOrWhiteSpace(request.TemplateUrl))
        {
            errors[nameof(PrintPreviewRequest.TemplateId)] = ["TemplateId o TemplateUrl es requerido."];
        }

        if (!string.IsNullOrWhiteSpace(request.TemplateUrl) &&
            !Uri.TryCreate(request.TemplateUrl, UriKind.Absolute, out _))
        {
            errors[nameof(PrintPreviewRequest.TemplateUrl)] = ["TemplateUrl debe ser una URL absoluta válida."];
        }

        if (request.Options.WaitFor.MaxRenderTimeoutMs <= 0)
        {
            errors["Options.WaitFor.MaxRenderTimeoutMs"] = ["MaxRenderTimeoutMs debe ser mayor a 0."];
        }

        return errors;
    }

    private static string? RedactUrlForLogs(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var qs = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(qs))
        {
            return uri.GetLeftPart(UriPartial.Path);
        }

        return $"{uri.GetLeftPart(UriPartial.Path)}?…";
    }
}
