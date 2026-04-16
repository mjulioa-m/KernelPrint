using KernelPrint.Engine.Abstractions;
using KernelPrint.Engine.Configuration;
using KernelPrint.Engine.Runtime;
using KernelPrint.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KernelPrint.Engine;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKernelPrintEngine(
        this IServiceCollection services,
        Action<KernelPrintEngineOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<KernelPrintEngineOptions>(_ => { });
        }

        services.TryAddSingleton<BrowserPool>();
        services.TryAddSingleton<IBrowserPool>(sp => sp.GetRequiredService<BrowserPool>());
        services.AddSingleton<IPrintService, PrintService>();
        return services;
    }
}
