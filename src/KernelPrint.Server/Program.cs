using KernelPrint.Contracts;
using KernelPrint.Engine;
using KernelPrint.Engine.Abstractions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddKernelPrintEngine();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapPost("/v1/print", async (PrintRequest request, IPrintService printService, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.TemplateId) && string.IsNullOrWhiteSpace(request.TemplateUrl))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(PrintRequest.TemplateId)] = ["TemplateId o TemplateUrl es requerido."]
        });
    }

    var result = await printService.GenerateAsync(request, cancellationToken);
    return Results.File(
        result.Bytes,
        contentType: result.ContentType,
        fileDownloadName: result.FileName,
        enableRangeProcessing: false);
});

app.Run();
