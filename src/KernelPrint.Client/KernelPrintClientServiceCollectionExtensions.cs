using Microsoft.Extensions.DependencyInjection;

namespace KernelPrint.Client;

public static class KernelPrintClientServiceCollectionExtensions
{
    public static IHttpClientBuilder AddKernelPrintClient(
        this IServiceCollection services,
        Action<HttpClient>? configureClient = null)
    {
        return services.AddHttpClient<KernelPrintClient>(client =>
        {
            configureClient?.Invoke(client);
        });
    }
}
