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

Detalle y alternativa sin Docker en [docs/getting-started/local-development.md](docs/getting-started/local-development.md).

## Estado

Versión 1.0 ([CHANGELOG.md](CHANGELOG.md)): fases 0 a 13 terminadas y endurecidas para producción. Cubren cuentas y sesión, espacios con miembros e invitaciones, categorías, cuentas con ingresos, gastos y transferencias, tarjetas, patrimonio y tipo de cambio, presupuestos, avisos con correo y tiempo real, movimientos programados, metas y gastos compartidos, importación de archivos de banco, WhatsApp (apagado hasta que Meta apruebe las plantillas), reportes e inversiones. Lo siguiente es la 1.1, con la fase 14 (tarjetas avanzadas y FX). El estado de cada fase está en [docs/roadmap/status.md](docs/roadmap/status.md), su detalle en [docs/roadmap/phases.md](docs/roadmap/phases.md), y cómo salir a producción en [docs/devops/go-live.md](docs/devops/go-live.md).
