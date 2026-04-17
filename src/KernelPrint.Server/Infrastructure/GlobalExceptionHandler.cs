using KernelPrint.Engine.Exceptions;
using KernelPrint.Server.Auth;
using KernelPrint.Server.Configuration;
using KernelPrint.Server.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace KernelPrint.Server.Infrastructure;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IHostEnvironment _environment;
    private readonly IOptions<KernelPrintServerOptions> _serverOptions;
    private readonly KernelPrintMeters _meters;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IHostEnvironment environment,
        IOptions<KernelPrintServerOptions> serverOptions,
        KernelPrintMeters meters,
        ILogger<GlobalExceptionHandler> logger)
    {
        _environment = environment;
        _serverOptions = serverOptions;
        _meters = meters;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception. TraceId={TraceId}", httpContext.TraceIdentifier);

        var clientId = httpContext.Items[ApiKeyResolution.ClientIdItemKey] as string;
        switch (exception)
        {
            case ServiceSaturatedException:
                _meters.RecordPrintOutcome("saturated", clientId);
                break;
            case ArgumentException:
                _meters.RecordPrintOutcome("bad_request", clientId);
                break;
            case TimeoutException:
            case OperationCanceledException:
                _meters.RecordPrintOutcome("timeout", clientId);
                break;
            case PlaywrightException:
                _meters.RecordPrintOutcome("playwright_error", clientId);
                break;
            default:
                _meters.RecordPrintOutcome("internal_error", clientId);
                break;
        }

        var problem = MapException(exception, httpContext.TraceIdentifier);

        if (exception is ServiceSaturatedException)
        {
            httpContext.Response.Headers.RetryAfter =
                _serverOptions.Value.ConcurrencyRetryAfterSeconds.ToString();
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private ProblemDetails MapException(Exception exception, string traceId)
    {
        ProblemDetails problem = exception switch
        {
            ServiceSaturatedException ex => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service Unavailable",
                Detail = ex.Message,
                Type = "https://kernelprint.dev/problems/service-unavailable"
            },
            ArgumentException ex => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = ex.Message,
                Type = "https://kernelprint.dev/problems/bad-request"
            },
            TimeoutException ex => new ProblemDetails
            {
                Status = StatusCodes.Status504GatewayTimeout,
                Title = "Gateway Timeout",
                Detail = ex.Message,
                Type = "https://kernelprint.dev/problems/gateway-timeout"
            },
            OperationCanceledException ex => new ProblemDetails
            {
                Status = StatusCodes.Status504GatewayTimeout,
                Title = "Gateway Timeout",
                Detail = string.IsNullOrWhiteSpace(ex.Message)
                    ? "The print operation was canceled or timed out."
                    : ex.Message,
                Type = "https://kernelprint.dev/problems/gateway-timeout"
            },
            PlaywrightException ex => new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Bad Gateway",
                Detail = _environment.IsDevelopment() ? ex.Message : "Headless browser rendering failed.",
                Type = "https://kernelprint.dev/problems/bad-gateway"
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = _environment.IsDevelopment()
                    ? exception.ToString()
                    : "An unexpected error occurred.",
                Type = "https://kernelprint.dev/problems/internal-server-error"
            }
        };

        problem.Extensions["traceId"] = traceId;
        return problem;
    }
}
