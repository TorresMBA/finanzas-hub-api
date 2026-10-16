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

Fases 0 a 4 terminadas: la plataforma común y el shell del frontend; las cuentas de usuario (registro, sesión, recuperación de contraseña) y los workspaces con miembros, roles e invitaciones; las categorías y etiquetas, privadas de cada persona; las cuentas de dinero con sus ingresos y gastos, cuyo saldo se deriva siempre de los movimientos; y las transferencias, las tarjetas de crédito, el patrimonio, el tipo de cambio y la página de Inicio. Lo siguiente es la fase 5: presupuestos. El estado de cada fase está en [docs/roadmap/status.md](docs/roadmap/status.md) y su detalle en [docs/roadmap/phases.md](docs/roadmap/phases.md).
