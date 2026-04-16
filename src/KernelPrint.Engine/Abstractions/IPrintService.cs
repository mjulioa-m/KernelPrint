using KernelPrint.Contracts;

namespace KernelPrint.Engine.Abstractions;

public interface IPrintService
{
    Task<PrintResult> GenerateAsync(PrintRequest request, CancellationToken cancellationToken = default);
}
