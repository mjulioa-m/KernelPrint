using Microsoft.Playwright;

namespace KernelPrint.Engine.Runtime;

public interface IBrowserPool
{
    Task<T> WithPageAsync<T>(Func<IPage, Task<T>> action, CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="WithPageAsync{T}"/>, but returns (false, default) if no worker slot is available immediately.
    /// </summary>
    Task<(bool Acquired, T? Result)> TryWithPageAsync<T>(
        Func<IPage, Task<T>> action,
        CancellationToken cancellationToken) where T : class;

    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);
}
