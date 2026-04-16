using Microsoft.Playwright;

namespace KernelPrint.Engine.Runtime;

public interface IBrowserPool
{
    Task<T> WithPageAsync<T>(Func<IPage, Task<T>> action, CancellationToken cancellationToken);

    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);
}
