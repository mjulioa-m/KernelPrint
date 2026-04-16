# KernelPrint MVP

PDF rendering toolkit for .NET using Playwright/Chromium plus a React template app.

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

## Local Development (without Docker)

- `dotnet build KernelPrint.slnx`
- `dotnet run --project src/KernelPrint.Server/KernelPrint.Server.csproj`
- `cd templates/kernelprint-templates/invoice-app && npm install && npm run dev`
