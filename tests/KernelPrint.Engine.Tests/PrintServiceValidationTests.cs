using KernelPrint.Contracts;
using KernelPrint.Engine.Configuration;
using KernelPrint.Engine.Runtime;
using KernelPrint.Engine.Services;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Xunit;

namespace KernelPrint.Engine.Tests;

public sealed class PrintServiceValidationTests
{
    [Fact]
    public async Task GenerateAsync_Throws_WhenTemplateIsMissing()
    {
        var service = CreateService();
        var request = new PrintRequest
        {
            TemplateId = null,
            TemplateUrl = null
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateAsync(request));

        Assert.Contains("Either TemplateId or TemplateUrl is required.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_Throws_WhenMaxRenderTimeoutIsInvalid()
    {
        var service = CreateService();
        var request = new PrintRequest
        {
            TemplateId = "invoice",
            Options = new PrintOptions
            {
                WaitFor = new WaitForOptions
                {
                    MaxRenderTimeoutMs = 0
                }
            }
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateAsync(request));

        Assert.Contains("MaxRenderTimeoutMs", ex.Message, StringComparison.Ordinal);
    }

    private static PrintService CreateService()
    {
        var options = new KernelPrintEngineOptions
        {
            TemplateBaseUrl = "http://templates:4173",
            Templates = new KernelPrintTemplateSecurityOptions()
        };

        return new PrintService(new StubBrowserPool(), Options.Create(options));
    }

    private sealed class StubBrowserPool : IBrowserPool
    {
        public Task<T> WithPageAsync<T>(Func<IPage, Task<T>> action, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Browser pool should not be used for validation failures.");

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
