using KernelPrint.Contracts;
using KernelPrint.Engine;
using KernelPrint.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

var services = new ServiceCollection()
    .AddKernelPrintEngine()
    .BuildServiceProvider();

var printService = services.GetRequiredService<IPrintService>();

var rowsHtml = string.Join(
    Environment.NewLine,
    Enumerable.Range(1, 800).Select(i =>
        $"<tr><td>ITM-{i:0000}</td><td>Premium line item detail #{i} with decorative report styling and print pagination stress test content.</td><td class=\"text-right\">{(i * 17.35m):F2}</td><td class=\"text-right\">{(i * 17.35m * 0.13m):F2}</td></tr>"));

var templateHtml =
    $$"""
    <!doctype html>
    <html>
    <head>
      <meta charset="utf-8" />
      <style>
        @page {
          size: A4;
          margin: 16mm 12mm 16mm 12mm;
        }

        :root {
          --ink: #0f172a;
          --muted: #475569;
          --line: #e2e8f0;
          --card: #ffffff;
          --brand-a: #4f46e5;
          --brand-b: #06b6d4;
          --brand-c: #22c55e;
        }

        * {
          box-sizing: border-box;
        }

        body {
          margin: 0;
          font-family: "Segoe UI", Arial, sans-serif;
          color: var(--ink);
          font-size: 12px;
          background: linear-gradient(140deg, #eef2ff 0%, #ecfeff 45%, #f0fdf4 100%);
        }

        .hero {
          margin-bottom: 14px;
          border-radius: 14px;
          color: white;
          padding: 18px 20px;
          background: linear-gradient(120deg, var(--brand-a), var(--brand-b));
          box-shadow: 0 10px 30px rgba(79, 70, 229, 0.25);
        }

        h1 {
          margin: 0;
          font-size: 22px;
          letter-spacing: 0.2px;
        }

        .subtitle {
          margin: 6px 0 0;
          opacity: 0.9;
        }

        .kpi-grid {
          display: grid;
          grid-template-columns: 1fr 1fr 1fr;
          gap: 10px;
          margin-bottom: 14px;
        }

        .kpi-card {
          background: var(--card);
          border: 1px solid var(--line);
          border-radius: 10px;
          padding: 10px 12px;
          break-inside: avoid;
          page-break-inside: avoid;
        }

        .kpi-label {
          margin: 0 0 3px;
          font-size: 10px;
          text-transform: uppercase;
          color: var(--muted);
          letter-spacing: 0.5px;
        }

        .kpi-value {
          margin: 0;
          font-size: 16px;
          font-weight: 700;
        }

        table {
          width: 100%;
          border-collapse: collapse;
          table-layout: fixed;
          background: white;
          border-radius: 10px;
          overflow: hidden;
          border: 1px solid var(--line);
        }

        thead {
          display: table-header-group;
        }

        tfoot {
          display: table-footer-group;
        }

        tr {
          break-inside: avoid;
          page-break-inside: avoid;
        }

        th, td {
          border-bottom: 1px solid var(--line);
          text-align: left;
          padding: 7px 8px;
          vertical-align: top;
          word-wrap: break-word;
        }

        th {
          font-size: 11px;
          color: #e2e8f0;
          background: linear-gradient(120deg, #334155, #1e293b);
        }

        tbody tr:nth-child(odd) {
          background: #f8fafc;
        }

        .text-right {
          text-align: right;
        }
      </style>
    </head>
    <body>
      <div class="hero">
        <h1>Financial Report REP-2026-04</h1>
        <p class="subtitle">Customer: ACME Corp</p>
      </div>

      <div class="kpi-grid">
        <div class="kpi-card">
          <p class="kpi-label">Total Revenue</p>
          <p class="kpi-value">$ 1,984,455.22</p>
        </div>
        <div class="kpi-card">
          <p class="kpi-label">Net Margin</p>
          <p class="kpi-value">18.9%</p>
        </div>
        <div class="kpi-card">
          <p class="kpi-label">Active Lines</p>
          <p class="kpi-value">800</p>
        </div>
      </div>

      <table>
        <thead>
          <tr>
            <th style="width: 14%">Code</th>
            <th style="width: 46%">Description</th>
            <th style="width: 20%" class="text-right">Amount</th>
            <th style="width: 20%" class="text-right">Tax</th>
          </tr>
        </thead>
        <tbody id="lines">
          {{rowsHtml}}
        </tbody>
      </table>
    </body>
    </html>
    """;

var templateUrl = $"data:text/html;charset=utf-8,{WebUtility.UrlEncode(templateHtml)}";

var request = new PrintRequest
{
    TemplateUrl = templateUrl,
    Data = new Dictionary<string, object?>()
};

var result = await printService.GenerateAsync(request);
var outputPath = Path.Combine(AppContext.BaseDirectory, "output.pdf");
await File.WriteAllBytesAsync(outputPath, result.Bytes);
Console.WriteLine($"Generado ContentType: {result.ContentType}, Bytes: {result.Bytes.Length}, TotalMs: {result.Timings.TotalMs}");
Console.WriteLine($"Filas renderizadas: {result.Metadata.GetValueOrDefault("renderedRows", "n/a")}");
Console.WriteLine($"Archivo PDF: {outputPath}");
