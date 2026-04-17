using KernelPrint.Contracts;

namespace KernelPrint.Engine.Abstractions;

public interface IPrintService
{
    Task<PrintResult> GenerateAsync(PrintRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="GenerateAsync"/> but fails fast when the browser pool is saturated.
    /// </summary>
    /// <exception cref="KernelPrint.Engine.Exceptions.ServiceSaturatedException">No slot was available immediately.</exception>
    Task<PrintResult> TryGenerateAsync(PrintRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders the template to a PNG screenshot after readiness (no PDF).
    /// </summary>
    /// <exception cref="KernelPrint.Engine.Exceptions.ServiceSaturatedException">No slot was available immediately.</exception>
    Task<byte[]> TryGeneratePreviewPngAsync(PrintPreviewRequest request, CancellationToken cancellationToken = default);
}
