using Microsoft.Playwright;

namespace KernelPrint.Engine.Runtime;

internal interface IBrowserPool
{
    Task<T> WithPageAsync<T>(Func<IPage, Task<T>> action, CancellationToken cancellationToken);
}
