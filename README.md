# KernelPrint MVP

KernelPrint is a small **print rendering platform** for teams that want to stop generating PDFs client-side with `jsPDF` + `html2canvas` and instead render **real HTML/CSS** using **headless Chromium** (Playwright) from a .NET service.

## What you get today

- **.NET library + service**
  - `KernelPrint.Engine`: Playwright rendering pipeline + browser pooling/recycling
  - `KernelPrint.Server`: `POST /v1/print`, `POST /v1/print/preview`, health, rate limits, ProblemDetails errors, optional admin pool reset
  - `KernelPrint.Client`: typed HTTP client for backends calling the API
  - `KernelPrint.Contracts`: request/response DTOs
- **Template app (React/Vite)**
  - `templates/kernelprint-templates/invoice-app`: a polished invoice UI with a browser print button (for visual parity checks)
- **Docker Compose**
  - `docker-compose.yml`: runs templates + API together

## Why this replaces `jsPDF` well

- Pagination is handled by the browser print engine (Chromium), not manual canvas slicing.
- Layout fidelity is closer to “Print to PDF” from Chrome/Edge.
- You keep your SPA UX, but move PDF generation to a dedicated renderer.

## Architecture (high level)

```mermaid
flowchart LR
  Client[Your app / backend] -->|POST /v1/print| Api[KernelPrint.Server]
  Api --> Engine[KernelPrint.Engine]
  Engine --> PW[Playwright Chromium]
  PW --> Templates[TemplateHost React]
  Templates -->|HTML/CSS| PW
  PW -->|PDF bytes| Engine
  Engine --> Api
```

## Quick Start with Docker

### 1) Build and run

```bash
docker compose up --build
```

### 2) Open services

- Template app (browser preview + print button): `http://localhost:4173`
- Print API: `http://localhost:5294`

### 3) Generate a PDF from the API

```bash
curl -X POST "http://localhost:5294/v1/print" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: change-me" \
  -d '{
    "templateUrl": "http://templates:4173",
    "data": {
      "invoiceNumber": "INV-2026-0415",
      "customer": "ACME Corp"
    },
    "options": {
      "format": "A4",
      "landscape": false
    }
  }' --output invoice.pdf
```

> If you call the API from your host machine (not from inside Docker), use:
> `templateUrl: "http://host.docker.internal:4173"`

### Security defaults

- Set `KernelPrint__Security__ApiKey` (recommended) and send it as `X-Api-Key`.
- Template URLs must be allowlisted via `KernelPrint__Engine__Templates__AllowedHosts`.
- By default, `data:` template URLs are blocked (`KernelPrint__Engine__Templates__BlockDataUrls`).
- Subresource loading can be restricted (`KernelPrint__Engine__Templates__EnforceResourceHostAllowlist`).

### Docker environment variables (common)

- `KERNELPRINT_API_KEY`: overrides the default API key used by compose (`change-me` is only a bootstrap default; change it).
- Optional staging-style overrides (examples):
  - `KernelPrint__Server__ReadinessHttpProbeUrl`: e.g. `http://templates:4173/` for an HTTP readiness check
  - `KernelPrint__Server__RequestTimeoutMs`: server-side request timeout for print/preview
  - `KernelPrint__Admin__ApiKey`: enables `POST /admin/pool/reset` with header `X-Admin-Key`
  - `KernelPrint__Security__RateLimit__PermitLimit` / `KernelPrint__Security__RateLimit__WindowSeconds`: per-client in-memory limits (per server instance)

## Local Development (without Docker)

- `dotnet build KernelPrint.slnx`
- `dotnet run --project src/KernelPrint.Server/KernelPrint.Server.csproj`
- `cd templates/kernelprint-templates/invoice-app && npm install && npm run dev`

### Playwright browser install (one-time per machine)

After restoring packages, install Chromium for Playwright (Windows example):

```powershell
powershell -ExecutionPolicy Bypass -File "samples/KernelPrint.Samples/bin/Debug/net10.0/playwright.ps1" install chromium
```

## API

### `POST /v1/print`

Body: `PrintRequest` (`KernelPrint.Contracts`)

- `templateUrl` **or** `templateId`
  - `templateId` is resolved against `KernelPrint:Engine:TemplateBaseUrl`
- `profile` (optional): merges defaults from `KernelPrint:Profiles:{name}` with the request `options` (non-default JSON fields win)
- `data`: JSON injected into the template page as `window.__KERNELPRINT_DATA__` and a `kernelprint:data-ready` event
- `options`: PDF + wait options (`PrintOptions`)
- `responseMode` (optional): `pdfOnly` (default) or `jsonWithPdf` (returns JSON with Base64 PDF when under `KernelPrint:Server:MaxJsonResponsePdfBytes`)

Headers:

- `X-Api-Key`: required when `KernelPrint:Security:ApiKey` is set or when `KernelPrint:Security:Clients` is non-empty (Docker compose enables this by default)
- `X-Correlation-Id`: optional; if omitted the server generates one and returns it on the response

Response:

- `application/pdf` bytes (default), or `application/json` (`PrintPdfJsonResponse`) when `responseMode` is `jsonWithPdf`
- helpful headers like `X-KernelPrint-TotalMs` / `X-KernelPrint-RenderMs` and `X-KernelPrint-Meta-*`

Errors:

- JSON `ProblemDetails` for most failures (including `401`, `429`, `503` when saturated, `504` on timeouts)

### `POST /v1/print/preview`

Same template + `data` + readiness pipeline as printing, but returns a **PNG** of the current viewport (useful to validate layout before generating a PDF).

### `POST /admin/pool/reset`

Operator escape hatch: resets the shared Chromium browser. Requires `KernelPrint:Admin:ApiKey` and header `X-Admin-Key`.

### Health

- `GET /health/live`
- `GET /health/ready` (browser pool + TCP reachability to `TemplateBaseUrl`, optional HTTP probe via `KernelPrint:Server:ReadinessHttpProbeUrl`)

### .NET client (`KernelPrint.Client`)

See [`src/KernelPrint.Client/README.md`](src/KernelPrint.Client/README.md).

## Migrating from `jsPDF` / `html2canvas`

Recommended pattern:

1. **Create a dedicated print route** in your SPA (example: `/print/invoice`) that renders a simplified, print-friendly layout (often the same components with different CSS via `@media print`).
2. **Listen for injected data** using the KernelPrint template contract (`kernelprint:data-ready` and/or `window.__KERNELPRINT_DATA__`).
3. **Signal readiness** by setting `window.__KERNELPRINT_READY__ = true` once fonts/layout are stable.
4. Replace the old client-side PDF button with:
   - `POST /v1/print` using `templateUrl` pointing at that route (or `templateId` if you host templates centrally)
   - download the returned PDF bytes (or use `KernelPrint.Client`)

Optional: add `@kernelprint/template-sdk` from `packages/kernelprint-template-sdk` for small helpers in your template app.

## Helm (Kubernetes)

A minimal chart skeleton lives under [`deploy/helm/kernelprint`](deploy/helm/kernelprint). It is intended as a starting point for staging/production wiring (probes, env, ports).

## Template contract (important for reliability)

Templates should:

- Listen for `kernelprint:data-ready` (or read `window.__KERNELPRINT_DATA__` on boot)
- When rendering is complete, set:

```js
window.__KERNELPRINT_READY__ = true
```

Optionally, callers can also set:

- `options.waitFor.readySelector`
- `options.waitFor.readyExpression`

## Configuration reference

See `src/KernelPrint.Server/appsettings.json` for defaults and tuning knobs under:

- `KernelPrint:Engine`
- `KernelPrint:Security`
- `KernelPrint:Server`
- `KernelPrint:Admin`
- `KernelPrint:Profiles`

## Troubleshooting

- **401 Unauthorized**: missing/wrong `X-Api-Key`.
- **429 Too Many Requests**: rate limit (`KernelPrint:Security:RateLimit`) exceeded for your client/IP partition.
- **503 Service Unavailable** with `Retry-After`: browser pool saturated; retry after a short backoff.
- **Template blocked**: host not allowlisted; update `KernelPrint:Engine:Templates:AllowedHosts`.
- **Fonts blocked**: add hosts to `KernelPrint:Engine:Templates:ExtraResourceAllowedHosts` or disable allowlisting (not recommended publicly).
- **Colors missing in browser print preview**: enable “Background graphics” in the print dialog; templates also set `print-color-adjust: exact` where applicable.
