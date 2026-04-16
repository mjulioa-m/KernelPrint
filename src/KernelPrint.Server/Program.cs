using KernelPrint.Contracts;
using KernelPrint.Engine;
using KernelPrint.Engine.Abstractions;
using KernelPrint.Engine.Configuration;
using KernelPrint.Engine.Runtime;
using KernelPrint.Server.Configuration;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.Configure<KernelPrintSecurityOptions>(builder.Configuration.GetSection("KernelPrint:Security"));
builder.Services.AddKernelPrintEngine(options =>
{
    builder.Configuration.GetSection("KernelPrint:Engine").Bind(options);
});

var app = builder.Build();
var meter = new Meter("KernelPrint.Server");
var printLatencyMs = meter.CreateHistogram<double>("kernelprint.print.latency.ms");
var printRequestCount = meter.CreateCounter<long>("kernelprint.print.request.count");

app.Use(async (context, next) =>
{
    const string correlationHeader = "X-Correlation-Id";
    var correlationId = context.Request.Headers[correlationHeader].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.CreateVersion7().ToString();
    }

    context.TraceIdentifier = correlationId;
    context.Response.Headers[correlationHeader] = correlationId;
    await next();
});

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/v1/print"))
    {
        await next();
        return;
    }

    var security = context.RequestServices.GetRequiredService<IOptions<KernelPrintSecurityOptions>>().Value;
    var env = context.RequestServices.GetRequiredService<IHostEnvironment>();

    var requireKey = !string.IsNullOrWhiteSpace(security.ApiKey) &&
                     (!env.IsDevelopment() || security.RequireApiKeyInDevelopment);

    if (!requireKey)
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
        !string.Equals(provided.ToString(), security.ApiKey, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Unauthorized");
        return;
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

app.MapGet("/health/ready", async (BrowserPool browserPool, IOptions<KernelPrintEngineOptions> engineOptions) =>
{
    var engine = engineOptions.Value;
    var checks = new Dictionary<string, object>();

    var poolOk = await browserPool.IsReadyAsync();
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
            var connectTask = client.ConnectAsync(baseUri.Host, baseUri.Port == -1 ? 80 : baseUri.Port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(2)));
            checks["templates"] = new { ok = completed == connectTask && client.Connected };
        }
    }
    catch (Exception ex)
    {
        checks["templates"] = new { ok = false, error = ex.GetType().Name };
    }

    var ok = checks.Values.All(v =>
    {
        if (v is not null && v.GetType().GetProperty("ok")?.GetValue(v) is bool b)
        {
            return b;
        }

        return false;
    });

    return ok ? Results.Ok(new { status = "ready", checks }) : Results.Json(new { status = "not_ready", checks }, statusCode: 503);
});

app.MapPost("/v1/print", async (
    PrintRequest request,
    HttpContext httpContext,
    IPrintService printService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var validationErrors = ValidatePrintRequest(request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    var stopwatch = Stopwatch.StartNew();
    printRequestCount.Add(1);
    logger.LogInformation(
        "Print request started. CorrelationId={CorrelationId} TemplateId={TemplateId} TemplateUrl={TemplateUrl}",
        httpContext.TraceIdentifier,
        request.TemplateId,
        RedactUrlForLogs(request.TemplateUrl));

    var result = await printService.GenerateAsync(request, cancellationToken);
    stopwatch.Stop();
    printLatencyMs.Record(stopwatch.Elapsed.TotalMilliseconds);

    httpContext.Response.Headers["X-KernelPrint-TotalMs"] = result.Timings.TotalMs.ToString();
    httpContext.Response.Headers["X-KernelPrint-RenderMs"] = result.Timings.RenderMs.ToString();

    foreach (var item in result.Metadata)
    {
        var normalizedKey = item.Key.Replace(' ', '-');
        httpContext.Response.Headers[$"X-KernelPrint-Meta-{normalizedKey}"] = item.Value;
    }

    logger.LogInformation(
        "Print request completed. CorrelationId={CorrelationId} Bytes={Bytes} TotalMs={TotalMs} RenderMs={RenderMs}",
        httpContext.TraceIdentifier,
        result.Bytes.Length,
        result.Timings.TotalMs,
        result.Timings.RenderMs);

    return Results.File(
        result.Bytes,
        contentType: result.ContentType,
        fileDownloadName: result.FileName,
        enableRangeProcessing: false);
});

app.Run();

static Dictionary<string, string[]> ValidatePrintRequest(PrintRequest request)
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

static string? RedactUrlForLogs(string? url)
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
