using KernelPrint.Contracts;
using KernelPrint.Engine;
using KernelPrint.Engine.Abstractions;
using System.Diagnostics;
using System.Diagnostics.Metrics;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

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
        request.TemplateUrl);

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
