# Word-to-PDF Service — Architecture & Deployment

## Architecture at a glance

```
 Creatio (C# script task / business process)
        │  POST /api/v1/convert
        │  X-API-Key: <key>
        │  multipart/form-data: file=<xxx.docx>
        ▼
 ┌───────────────────────────────┐
 │  ASP.NET Core 8 Minimal API   │
 │  (WordToPdfService)           │
 │                               │
 │  Auth: API key or JWT Bearer  │
 │  Health: GET /health          │
 │                               │
 │  soffice --headless --convert │
 │          -to pdf ...          │
 └───────────────────────────────┘
        ▲
        │  isolated LibreOffice user profile per request
        │  (safe for parallel conversions)
```

## Local test

```bash
docker compose up --build
# then:
curl -H "X-API-Key: dev-key-please-change" \
     -F "file=@sample.docx" \
     http://localhost:8080/api/v1/convert \
     --output sample.pdf
```

Swagger UI is available at http://localhost:8080/swagger when `EnableSwagger=true`.

## Azure App Service for Containers

1. Build and push:
   ```bash
   az acr login --name <your-acr>
   docker build -t <your-acr>.azurecr.io/word-to-pdf:1.0 .
   docker push <your-acr>.azurecr.io/word-to-pdf:1.0
   ```
2. Create a **Linux** App Service plan (Standard S1 or higher — LibreOffice
   needs more memory than the free tier provides; B2/S1 at minimum, P1v3+
   recommended for throughput).
3. Web App settings → **Configuration** → app settings (these override
   `appsettings.json` via the `__` separator):
   - `AUTH__APIKEY__KEYS` = `<one or more strong keys, comma-separated>`
   - `CONVERTER__TIMEOUTSECONDS` = `90`
   - `CONVERTER__MAXUPLOADMB` = `50`
   - `ASPNETCORE_URLS` = `http://+:8080`
   - `WEBSITES_PORT` = `8080`
4. Enable **Always On**. Enable **HTTPS Only**.
5. Restrict access with **Access Restrictions** (only allow your Creatio
   tenant’s outbound IP) or put the service behind an APIM gateway.

## AWS App Runner / ECS Fargate

- App Runner: point at the ECR image, set the same env vars, health check on
  `/health`.
- ECS Fargate: task definition with 1 vCPU / 2 GB RAM (minimum); enable
  CloudWatch logs; ALB target group health check `/health`; restrict the ALB
  to Creatio egress IPs.

## Sizing notes

LibreOffice cold-starts take a few seconds; the Dockerfile pre-warms the
profile. A single 1-vCPU container comfortably handles 3–5 concurrent small
conversions. Scale out horizontally (App Service slots, App Runner
auto-scaling, ECS service desired count) — conversions are stateless.

## Security checklist

- Rotate `AUTH__APIKEY__KEYS` via env var; never commit it.
- Prefer JWT if you want per-caller identity/revocation — set
  `AUTH__JWT__SIGNINGKEY`, `AUTH__JWT__ISSUER`, `AUTH__JWT__AUDIENCE` and
  issue tokens from Creatio.
- Terminate TLS at the load balancer / App Service (HTTPS Only).
- Limit request size (`CONVERTER__MAXUPLOADMB`) — defense in depth against
  resource exhaustion.
- Run behind a WAF or IP allowlist in production.

## Creatio side

1. Deploy `creatio/WordToPdfClient.cs` into a **Configuration** package
   (e.g. `UsrIntegrations`).
2. Create two system settings:
   - `WordToPdfServiceUrl` (text) — e.g. `https://word-to-pdf.yourcompany.com`
   - `WordToPdfApiKey` (secure text) — the API key.
3. Use `ScriptTask_Example.cs` inside a business process, or call
   `WordToPdfClient.ConvertAsync(bytes, name)` from any custom C# code.
