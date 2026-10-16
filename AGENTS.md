# AGENTS.md — PersonalHub

Punto de entrada para personas y agentes que cambian este repositorio. La documentación completa está en [docs/README.md](docs/README.md); lee solo lo que la tarea necesite.

## Qué es

Finanzas personales y colaborativas. Backend .NET 10 (monolito modular, PostgreSQL) en `finanzas-hub-api/`, frontend Angular 22 en `finanzas-hub-web/`. Pensado para unos 10 usuarios y una persona que lo mantiene: se prefiere lo simple.

El backend tiene dos entradas, la API (`Controllers/<Módulo>`) y el Worker (`Jobs/<Módulo>`). Las dos llaman a los casos de uso de `Modules.<Módulo>/Application`, que usan `Modules.<Módulo>/Domain`.

## Prioridades

1. Corrección financiera.
2. Privacidad y autorización.
3. Fronteras entre módulos.
4. Integridad y auditoría.
5. Idempotencia.
6. Testabilidad y mantenibilidad.

## Reglas que no se negocian

**Privacidad.** Pertenecer a un workspace no da acceso a lo financiero de otros miembros. Lo privado es de la persona: vive en su workspace personal y es lo mismo tenga abierto el workspace que tenga ([ADR-021](docs/adr/ADR-021-finanzas-privadas-de-la-persona.md)). Lleva `workspace_id` y `owner_user_id` e implementa `IOwned`; el filtro global de EF lo aísla con el workspace personal del dueño, que los módulos piden a `IPersonalSpace`. Lo colaborativo (metas y gastos compartidos) se autoriza por participación en el recurso. Ningún rol ve datos privados ajenos. Un recurso ajeno responde 404.

**Dinero.** `decimal` siempre, moneda explícita. El saldo se deriva de los asientos; no existe una columna de saldo. Una transferencia no es ingreso ni gasto. Nada financiero se borra: se anula.

**Módulos.**

- Un módulo es un proyecto, `Modules.X`, con carpetas `Domain`, `Application` y `Contracts`. De otro módulo solo se usa su `Contracts`, y solo siguiendo el grafo de [docs/diagrams/modules.md](docs/diagrams/modules.md).
- `Domain` no depende de nada salvo `BuildingBlocks.Domain`. `Application` no depende de `Infrastructure`, de Npgsql ni de ASP.NET.
- Cada entrada va en la carpeta de su módulo y no toca persistencia: los controllers en `PersonalFinance.Api/Controllers/X`, los jobs en `PersonalFinance.Worker/Jobs/X`. Solo llaman a handlers.
- Un solo `AppDbContext`, en `PersonalFinance.Infrastructure`, con un esquema de PostgreSQL por módulo. Un módulo llega a sus tablas por su interfaz `IXDb`. Entre módulos se guarda el id, sin propiedad de navegación.
- `BuildingBlocks` no referencia módulos, y ningún módulo referencia `Infrastructure`.

Los tipos de un módulo son públicos, así que el compilador no impide nada de esto: lo impiden los tests de arquitectura. Si uno falla, se corrige el código, no el test.

**Casos de uso.**

- Un handler por caso de uso (`ICommandHandler` o `IQueryHandler`), que devuelve `Result<T>`. Las reglas de negocio no lanzan excepciones.
- Los handlers no llaman a `SaveChanges`; lo hace el decorador de transacción.
- Un comando con efecto financiero implementa `IIdempotentCommand`.
- Cada acción de negocio relevante se registra con `IAuditLog`.
- Para que otro módulo reaccione: evento de dominio declarado en `Contracts/Events` y un `IDomainEventHandler` en el módulo que reacciona. Se entrega en proceso, dentro de la transacción del comando: si la reacción falla, el comando se revierte.
- Una reacción no llama a nada externo. Para enviar un correo o un WhatsApp escribe en la cola de envíos, que procesa un job.

**No añadir** sin justificarlo en un ADR: MediatR, AutoMapper, repositorios o unidades de trabajo genéricos, Redis, colas externas, Hangfire, Quartz, NgRx. Tampoco lo que se retiró en el [ADR-019](docs/adr/ADR-019-simplificacion-de-la-plataforma.md): outbox, eventos de integración, un `DbContext` por módulo, proyectos `Contracts` separados o Minimal APIs dentro de los módulos.

## Cómo se trabaja

Por porciones verticales, en el orden de [docs/roadmap/implementation-order.md](docs/roadmap/implementation-order.md): dominio y sus tests, handler y su test de integración, configuración EF y migración, controller y contrato OpenAPI, casos de seguridad, pantalla con sus cuatro estados. Cómo se añade un módulo está en [docs/backend/structure.md](docs/backend/structure.md).

Una feature está terminada cuando cumple [docs/getting-started/definition-of-done.md](docs/getting-started/definition-of-done.md).

Al empezar y al cerrar una fase se actualiza [docs/roadmap/status.md](docs/roadmap/status.md): el estado de cada fase y lo que cada una dejó pendiente.

## Comandos

```powershell
# Backend (desde finanzas-hub-api)
dotnet build PersonalFinance.slnx
dotnet test PersonalFinance.slnx                 # integración: Docker, o PERSONALFINANCE_TEST_POSTGRES
tools/add-migration.ps1 -Name Nombre
tools/update-openapi.ps1                         # tras cambiar la API
node tools/update-db-docs.mjs                    # tras una migración: regenera docs/backend/database.md
dotnet run --project src/PersonalFinance.Api     # http://localhost:5166
dotnet run --project src/PersonalFinance.Worker  # jobs

# Frontend (desde finanzas-hub-web)
npm run build
npm test -- --watch=false
npm run lint
npm run api:generate                             # tras actualizar el contrato
npm run e2e                                      # requiere la API en marcha
```

## Frontend

- Angular sin zonas, componentes standalone, signals. Archivos y clases sin sufijo de rol (`shell.ts`, `class Shell`).
- `core` (singletons sin UI), `shared` (UI reutilizable sin negocio), `layout` (marco), `features/<sección>`. Una feature no importa a otra; ESLint lo comprueba.
- Los tipos de la API se generan; no se escriben a mano.
- Cada vista implementa carga, vacío, error y datos.
- Los colores y medidas salen de los tokens de `src/styles/_tokens.scss`, que son los del mock.

## Estado

Fases 0 a 13 terminadas: plataforma, cuentas y sesión (Identity), workspaces con miembros, roles e invitaciones, categorías y tags privados por usuario (Categories), cuentas con ingresos y gastos (Ledger I), y transferencias, tarjetas de crédito, patrimonio, tipo de cambio e Inicio (Ledger II y Reporting), presupuestos mensuales por categoría (Budgets), bandeja de avisos con correo y tiempo real (Notifications), movimientos programados con su calendario (Scheduling), metas personales y compartidas (Goals), gastos compartidos entre miembros (SharedExpenses), importación de archivos de banco y de Bluecoins con reglas de clasificación (Integrations), avisos y consultas por WhatsApp con teléfono verificado, reportes por mes, trimestre y año con exportación a Excel y CSV y búsqueda global (Reporting), e inversiones con su valor, depósitos a plazo y patrimonio sin doble conteo (Investments). Lo siguiente es la fase 14 (tarjetas avanzadas y FX). El tablero está en [docs/roadmap/status.md](docs/roadmap/status.md).
