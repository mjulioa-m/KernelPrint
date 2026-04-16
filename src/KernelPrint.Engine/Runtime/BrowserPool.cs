using KernelPrint.Engine.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using System.Threading;

namespace KernelPrint.Engine.Runtime;

public sealed class BrowserPool : IBrowserPool, IAsyncDisposable
{
    private readonly KernelPrintEngineOptions _options;
    private readonly SemaphoreSlim _semaphore;
    private readonly SemaphoreSlim _startupLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private DateTimeOffset _browserCreatedUtc;
    private int _successfulJobsSinceRecycle;

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
            await MaybeRecycleBrowserAsync(cancellationToken);

            var browser = await EnsureBrowserAsync(cancellationToken);
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();

            try
            {
                var result = await action(page);
                Interlocked.Increment(ref _successfulJobsSinceRecycle);
                return result;
            }
            catch
            {
                // A template crash can poison Chromium; recycle defensively.
                await ResetAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var browser = await EnsureBrowserAsync(cancellationToken);
                await using var context = await browser.NewContextAsync();
                var page = await context.NewPageAsync();
                await page.GotoAsync("about:blank", new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = Math.Min(10_000, _options.BrowserLaunchTimeoutMs)
                });
                return true;
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _startupLock.WaitAsync(cancellationToken);
        try
        {
            await DisposeBrowserCoreAsync();
            Interlocked.Exchange(ref _successfulJobsSinceRecycle, 0);
        }
        finally
        {
            _startupLock.Release();
        }
    }

    private async Task MaybeRecycleBrowserAsync(CancellationToken cancellationToken)
    {
        var shouldRecycle = false;

        if (_options.BrowserRecycleAfterJobs > 0 &&
            Volatile.Read(ref _successfulJobsSinceRecycle) >= _options.BrowserRecycleAfterJobs)
        {
            shouldRecycle = true;
        }

        if (_options.BrowserRecycleAfterMinutes > 0 &&
            _browser is not null &&
            DateTimeOffset.UtcNow - _browserCreatedUtc > TimeSpan.FromMinutes(_options.BrowserRecycleAfterMinutes))
        {
            shouldRecycle = true;
        }

        if (!shouldRecycle)
        {
            return;
        }

        await ResetAsync(cancellationToken);
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

            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Timeout = _options.BrowserLaunchTimeoutMs
            });
            _browserCreatedUtc = DateTimeOffset.UtcNow;

            return _browser;
        }
        finally
        {
            _startupLock.Release();
        }
    }

    private async Task DisposeBrowserCoreAsync()
    {
        if (_browser is not null)
        {
            try
            {
                await _browser.CloseAsync();
            }
            catch
            {
                // ignore
            }
        }

        _browser = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _startupLock.WaitAsync();
        try
        {
            await DisposeBrowserCoreAsync();
        }
        finally
        {
            _startupLock.Release();
        }

        _playwright?.Dispose();
        _startupLock.Dispose();
        _semaphore.Dispose();
    }
}
