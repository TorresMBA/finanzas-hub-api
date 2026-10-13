# PersonalHub

Plataforma de finanzas personales y colaborativas.

| Carpeta | Contenido |
|---|---|
| `finanzas-hub-api/` | Backend en .NET 10: monolito modular sobre PostgreSQL, con una API y un Worker |
| `finanzas-hub-web/` | SPA en Angular 22 |
| `docs/` | Arquitectura, decisiones, modelo de datos, roadmap. Empieza por [docs/README.md](docs/README.md) |
| `Mocks_finanzas-hub-angular/` | Mock interactivo de la interfaz objetivo |

## Arranque rápido

```powershell
cd finanzas-hub-api
Copy-Item .env.example .env          # poner una contraseña local
docker compose up -d
dotnet user-secrets set "ConnectionStrings:PersonalFinance" "Host=localhost;Port=5432;Database=personal_hub;Username=personal_hub;Password=<la de .env>" --project src/PersonalFinance.Api
dotnet run --project src/PersonalFinance.Api      # http://localhost:5166
dotnet run --project src/PersonalFinance.Worker   # jobs; otra terminal, solo si trabajas en uno

cd ../finanzas-hub-web
npm ci
npm start                                          # http://localhost:4200
```

Detalle y alternativa sin Docker en [docs/development/local-development.md](docs/development/local-development.md).

## Estado

Fase 0 (fundación) terminada: estructura de la solución, plataforma común, reglas de arquitectura vigiladas por tests y shell del frontend. Todavía no hay inicio de sesión ni funcionalidad de negocio; llegan a partir de la fase 1. Ver [docs/roadmap/phases.md](docs/roadmap/phases.md).
