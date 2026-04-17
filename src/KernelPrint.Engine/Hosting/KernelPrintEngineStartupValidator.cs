using System.Linq;
using KernelPrint.Engine.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KernelPrint.Engine.Hosting;

/// <summary>
/// Validates engine security options at startup (e.g. wildcard hosts in Production).
/// </summary>
public sealed class KernelPrintEngineStartupValidator : IHostedService
{
    private readonly IHostEnvironment _environment;
    private readonly KernelPrintEngineOptions _options;

    public KernelPrintEngineStartupValidator(
        IHostEnvironment environment,
        IOptions<KernelPrintEngineOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var hosts = _options.Templates.AllowedHosts;
        var hasWildcard = hosts.Any(static h => string.Equals(h, "*", StringComparison.Ordinal));
        if (_environment.IsProduction() && hasWildcard && !_options.Templates.AllowWildcardHosts)
        {
            throw new InvalidOperationException(
                "KernelPrint:Engine:Templates:AllowedHosts contains '*' which is unsafe in Production. " +
                "Remove the wildcard, tighten the allowlist, or set KernelPrint:Engine:Templates:AllowWildcardHosts to true explicitly.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
