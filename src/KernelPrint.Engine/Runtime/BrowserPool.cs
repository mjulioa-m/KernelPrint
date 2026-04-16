using KernelPrint.Engine.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using System.Threading;

namespace KernelPrint.Engine.Runtime;

internal sealed class BrowserPool : IBrowserPool, IAsyncDisposable
{
    private readonly KernelPrintEngineOptions _options;
    private readonly SemaphoreSlim _semaphore;
    private readonly SemaphoreSlim _startupLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public BrowserPool(IOptions<KernelPrintEngineOptions> options)
    {
        _options = options.Value;
        _semaphore = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency), Math.Max(1, _options.MaxConcurrency));
    }

    public async Task<T> WithPageAsync<T>(Func<IPage, Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var browser = await EnsureBrowserAsync(cancellationToken);
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            return await action(page);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<IBrowser> EnsureBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is not null)
        {
            return _browser;
        }

        await _startupLock.WaitAsync(cancellationToken);
        try
        {
            if (_browser is not null)
            {
                return _browser;
            }

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Timeout = _options.BrowserLaunchTimeoutMs
            });

            return _browser;
        }
        finally
        {
            _startupLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
        _startupLock.Dispose();
        _semaphore.Dispose();
    }
}
