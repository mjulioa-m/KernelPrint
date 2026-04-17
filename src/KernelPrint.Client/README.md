# KernelPrint.Client

Small HTTP client for calling a KernelPrint server (`POST /v1/print`, `POST /v1/print/preview`).

## Usage

```csharp
using KernelPrint.Client;
using KernelPrint.Contracts;

var services = new ServiceCollection();
services.AddHttpClient<KernelPrintClient>(client =>
{
    client.BaseAddress = new Uri("http://localhost:5294/");
    client.DefaultRequestHeaders.Add("X-Api-Key", "change-me");
});

var provider = services.BuildServiceProvider();
var kernelPrint = provider.GetRequiredService<KernelPrintClient>();

var pdf = await kernelPrint.PrintPdfAsync(new PrintRequest
{
    TemplateId = "invoice",
    Data = new Dictionary<string, object?> { ["invoiceNumber"] = "INV-1" }
});

File.WriteAllBytes("out.pdf", pdf.Bytes);
```

For `ResponseMode = PrintResponseMode.JsonWithPdf`, use `PrintPdfJsonAsync`.

## Notes

- Always set `HttpClient.BaseAddress` to the API root (with trailing slash recommended).
- Send `X-Api-Key` when the server is configured with `KernelPrint:Security:ApiKey` or `KernelPrint:Security:Clients`.
