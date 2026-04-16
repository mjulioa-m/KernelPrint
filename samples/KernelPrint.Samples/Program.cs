using KernelPrint.Contracts;
using KernelPrint.Engine;
using KernelPrint.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

var services = new ServiceCollection()
    .AddKernelPrintEngine()
    .BuildServiceProvider();

var printService = services.GetRequiredService<IPrintService>();

var templateHtml =
    """
    <!doctype html>
    <html>
    <head>
      <meta charset="utf-8" />
      <style>
        body { font-family: Arial, sans-serif; margin: 32px; }
      </style>
    </head>
    <body>
      <h1 id="title">Invoice</h1>
      <p id="customer"></p>
      <script>
        window.addEventListener("kernelprint:data-ready", (event) => {
          const data = event.detail || {};
          document.getElementById("title").textContent = `Invoice ${data.invoiceNumber ?? ""}`;
          document.getElementById("customer").textContent = `Customer: ${data.customer ?? ""}`;
        });
      </script>
    </body>
    </html>
    """;

var templateUrl = $"data:text/html;charset=utf-8,{WebUtility.UrlEncode(templateHtml)}";

var request = new PrintRequest
{
    TemplateUrl = templateUrl,
    Data = new Dictionary<string, object?>
    {
        ["invoiceNumber"] = "INV-2026-0001",
        ["customer"] = "ACME Corp"
    }
};

var result = await printService.GenerateAsync(request);
Console.WriteLine($"Generado ContentType: {result.ContentType}, Bytes: {result.Bytes.Length}, TotalMs: {result.Timings.TotalMs}");
