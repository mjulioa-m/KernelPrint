using KernelPrint.Contracts;
using KernelPrint.Engine;
using KernelPrint.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection()
    .AddKernelPrintEngine()
    .BuildServiceProvider();

var printService = services.GetRequiredService<IPrintService>();

var request = new PrintRequest
{
    TemplateId = "invoice",
    Data = new Dictionary<string, object?>
    {
        ["invoiceNumber"] = "INV-2026-0001",
        ["customer"] = "ACME Corp"
    }
};

var result = await printService.GenerateAsync(request);
Console.WriteLine($"Generado ContentType: {result.ContentType}, Bytes: {result.Bytes.Length}");
