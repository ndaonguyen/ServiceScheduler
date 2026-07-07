# ClientApp

Vite + React 19 single-page app for the ServiceScheduler service.

## Develop

```bash
# 1. Run the API (separate terminal) — serves https://localhost:7443
dotnet run --project ..      # from source/ServiceScheduler.Api

# 2. Run the Vite dev server with hot reload — http://localhost:5173
npm install
npm run dev
```

The dev server proxies `/api`, `/openapi`, and `/health` to the API
(see `vite.config.ts`). Override the target with `VITE_API_PROXY`.

## Build

```bash
npm run build      # outputs to ./build
```

On `dotnet publish` of `ServiceScheduler.Api`, this build runs automatically and its
output is copied into `wwwroot` (see `ServiceScheduler.Api.csproj`).

## Typed API client (optional)

With the API running, generate a typed TypeScript client from the OpenAPI
document:

```bash
npm run generate-api   # writes src/web-api-client.ts via nswag.json
```

The servicescheduler pages use `fetch` directly so this step is optional.
