using KernelPrint.Engine.Abstractions;
using KernelPrint.Engine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KernelPrint.Engine;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKernelPrintEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPrintService, PrintService>();
        return services;
    }
}
