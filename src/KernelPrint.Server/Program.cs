using KernelPrint.Engine;
using KernelPrint.Server.Auth;
using KernelPrint.Server.Configuration;
using KernelPrint.Server.Endpoints;
using KernelPrint.Server.Infrastructure;
using KernelPrint.Server.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.Configure<KernelPrintServerOptions>(builder.Configuration.GetSection("KernelPrint:Server"));
builder.Services.Configure<KernelPrintAdminOptions>(builder.Configuration.GetSection("KernelPrint:Admin"));
builder.Services.Configure<KernelPrintSecurityOptions>(builder.Configuration.GetSection("KernelPrint:Security"));

builder.Services.AddSingleton<KernelPrintMeters>();
builder.Services.AddSingleton<PrintProfilesStore>();

builder.Services.AddHttpClient("kernelprint-readiness")
    .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler());

var serverBootstrap = builder.Configuration.GetSection("KernelPrint:Server").Get<KernelPrintServerOptions>() ?? new KernelPrintServerOptions();
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = serverBootstrap.MaxRequestBodyBytes;
});

builder.Services.AddKernelPrintEngine(options =>
{
    builder.Configuration.GetSection("KernelPrint:Engine").Bind(options);
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        }

        await Results.Problem(
                title: "Too Many Requests",
                detail: "Rate limit exceeded.",
                statusCode: StatusCodes.Status429TooManyRequests,
                type: "https://kernelprint.dev/problems/too-many-requests",
                extensions: new Dictionary<string, object?> { ["traceId"] = context.HttpContext.TraceIdentifier })
            .ExecuteAsync(context.HttpContext);
    };

    options.AddPolicy("kernelprint", httpContext =>
    {
        var security = httpContext.RequestServices.GetRequiredService<IOptions<KernelPrintSecurityOptions>>().Value;
        if (!security.RateLimit.Enabled)
        {
            return RateLimitPartition.GetNoLimiter("global");
        }

        var partitionKey = httpContext.Items[ApiKeyResolution.ClientIdItemKey] as string
                             ?? httpContext.Connection.RemoteIpAddress?.ToString()
                             ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = Math.Max(1, security.RateLimit.PermitLimit),
                Window = TimeSpan.FromSeconds(Math.Max(1, security.RateLimit.WindowSeconds)),
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

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

app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/v1"))
    {
        await next();
        return;
    }

    var security = context.RequestServices.GetRequiredService<IOptions<KernelPrintSecurityOptions>>().Value;
    var env = context.RequestServices.GetRequiredService<IHostEnvironment>();
    if (!ApiKeyResolution.IsApiKeyRequired(env, security))
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
        !ApiKeyResolution.TryValidateApiKey(provided.ToString(), security, out var clientId))
    {
        await Results.Problem(
                title: "Unauthorized",
                detail: "Missing or invalid X-Api-Key.",
                statusCode: StatusCodes.Status401Unauthorized,
                type: "https://kernelprint.dev/problems/unauthorized",
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier })
            .ExecuteAsync(context);
        return;
    }

    context.Items[ApiKeyResolution.ClientIdItemKey] = clientId;
    await next();
});

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapKernelPrint();

app.Run();
