using KernelPrint.Contracts;
using KernelPrint.Engine.Abstractions;

namespace KernelPrint.Engine.Services;

internal sealed class PrintService : IPrintService
{
    public Task<PrintResult> GenerateAsync(PrintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TemplateId) && string.IsNullOrWhiteSpace(request.TemplateUrl))
        {
            throw new ArgumentException("Either TemplateId or TemplateUrl is required.", nameof(request));
        }

        // Fase 2 reemplazará este stub por pipeline Playwright/Chromium.
        var result = new PrintResult
        {
            Bytes = [],
            ContentType = "application/pdf",
            FileName = "kernelprint-document.pdf",
            Metadata = new Dictionary<string, string>
            {
                ["status"] = "stub",
                ["template"] = request.TemplateId ?? request.TemplateUrl ?? "unknown"
            },
            Timings = new PrintTimings
            {
                TotalMs = 0,
                RenderMs = 0
            }
        };

        return Task.FromResult(result);
    }
}
