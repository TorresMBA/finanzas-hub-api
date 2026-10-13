# Arquitectura del frontend

## O. Angular architecture

Raíz: `finanzas-hub-web/`. Angular 22, componentes standalone, sin zonas, `OnPush`.

**Nombres.** El proyecto usa la convención de Angular 20 en adelante: archivos y clases sin sufijo de rol (`shell.ts` con `class Shell`, `theme-store.ts` con `class ThemeStore`), un archivo por clase, y `.spec.ts` para los tests. La carpeta indica el rol.

### O.0 Estado tras la fase 0

```text
finanzas-hub-web/
  proxy.conf.json               /api, /health, /openapi y /hubs hacia http://localhost:5166
  eslint.config.js              angular-eslint y reglas de límites entre carpetas
  playwright.config.ts          E2E contra el stack local
  e2e/shell.spec.ts             shell en 3 tamaños, tema, y API a través del proxy
  src/
    styles/                     _tokens, _mixins, _reset, _typography
    app/
      app.ts, app.config.ts, app.routes.ts
      core/
        api/schema.ts           tipos generados desde docs/api/openapi.v1.json
        http/api-interceptors.ts   credenciales, correlation-id, idempotency-key, errores
        errors/api-error.ts     ProblemDetails a ApiError
        period/period-store.ts  mes seleccionado en el topbar
        theme/theme-store.ts    tema oscuro o claro
      layout/
        navigation.ts           las 13 secciones del mock
        shell/, sidebar/, topbar/, mobile-tab-bar/, more-sheet/, brand-mark.ts
      shared/ui/                icon, page-header, empty-state, error-state, skeleton
      features/placeholder/     página provisional de toda sección aún no construida
```

Todas las secciones están enrutadas y se cargan de forma diferida; hasta que su fase las construya muestran la página provisional. Construir una sección es sustituir su entrada en `app.routes.ts` por las rutas de la feature.

Del topbar del mock faltan a propósito el selector de workspace, el buscador, el usuario y el contador de no leídas: se añaden con los módulos que les dan datos (fases 1, 6 y 12). El botón central "+" de la barra móvil llega con el formulario de transacción (fase 3).

| Script | Qué hace |
|---|---|
| `npm start` | Servidor de desarrollo en `http://localhost:4200` con proxy a la API |
| `npm test` | Tests unitarios (Vitest) |
| `npm run lint` | ESLint, incluidas las reglas de límites |
| `npm run api:generate` | Regenera `core/api/schema.ts` desde el contrato OpenAPI |
| `npm run e2e` | Playwright; requiere la API en marcha. `PW_CHANNEL=chrome` usa el navegador instalado |
| `npm run build` | Build de producción |

### O.1 Estructura objetivo

```text
src/app/
  core/                       singletons, sin UI
    api/                      cliente generado desde openapi.v1.json y ApiClient base
    auth/                     SessionStore; appGuard, onboardingGuard, guestGuard
    workspace/                WorkspaceStore, zonas horarias
    testing/                  provideSession: sesión y workspace simulados para tests de componentes
    period/                   PeriodStore (mes global del topbar)
    realtime/                 RealtimeService (SignalR)
    http/                     interceptores: credenciales y XSRF, correlation-id, idempotency-key, errores
    errors/                   ProblemDetails a mensajes, ErrorHandler global
    config/                   tokens y entorno
  layout/
    shell/                    AppShell con los 3 modos responsive
    sidebar/, topbar/, mobile-tab-bar/, more-sheet/, toast-host/
  shared/
    ui/                       stat-card, segmented, list-row, detail-rail, modal-form, progress-bar,
                              ring, bar-chart, chip, toggle, avatar, money, badge, empty-state,
                              error-state, skeleton, confirm-dialog
    forms/                    money-input, date-input, account-select, category-select, tag-chips,
                              icon-picker, color-picker, member-picker
    pipes/                    money, date-label, relative-time
    util/
  features/
    <feature>/
      <feature>.routes.ts
      pages/                  componentes enrutados; orquestan
      components/             presentacionales; inputs y outputs
      data/                   <feature>.api.ts y <feature>.store.ts
      models/                 solo view models; los tipos de API se generan
styles/
  _tokens.scss                variables del mock para tema oscuro y claro
  _typography.scss, _reset.scss, _mixins.scss
```

Regla de dependencias: `features` → `shared`, `core`; `layout` → `shared`, `core`; `shared` → nada de la aplicación; una feature no importa a otra salvo por rutas; `core` no importa `shared`. Lo comprueba `eslint-plugin-boundaries` (regla `boundaries/dependencies` en `eslint.config.js`): un import que cruza un límite hace fallar `npm run lint`.

### O.2 Features

| Feature | Rutas y páginas | Componentes principales | Estado |
|---|---|---|---|
| `auth` | `/login`, `/register`, `/forgot-password`, `/reset-password`, `/invite/:token` | formularios | `SessionStore` (core) |
| `onboarding` | `/onboarding` | selector de moneda y zona horaria | local |
| `dashboard` | `/` | kpi-row, cashflow-chart, top-categories, recent-list, accounts-card, goals-card, upcoming-card | `DashboardStore` con recurso por mes |
| `transactions` | `/transactions`, `?id=` para el detalle | tx-filters, tx-summary, tx-list, tx-detail, tx-form | `TransactionsStore`: filtros como signals, lista con cursor |
| `accounts` | `/accounts` | account-stats, account-group, account-card, account-detail, account-form, card-payment-form | `AccountsStore` |
| `categories` | `/categories` | category-tree, tag-cloud, quick-tag, category-form | `CategoriesStore`, reutilizado por los selectores |
| `budgets` | `/budgets` | budget-summary, budget-list, budget-detail, daily-chart, budget-form | `BudgetsStore` con recurso por mes |
| `goals` | `/goals` | goal-stats, goal-card, goal-detail, contribution-form, participants-panel, goal-form | `GoalsStore`; se invalida con la señal de tiempo real |
| `calendar` | `/calendar` | month-grid, day-detail, upcoming-list, schedule-form | `CalendarStore` con recurso por mes |
| `shared-expenses` | `/shared`, `/shared/:groupId` | group-tabs, balances-list, expense-list, group-summary, activity-feed, expense-form, split-editor, settlement-form | `SharedStore`; se invalida con la señal de tiempo real |
| `investments` | `/investments` | portfolio-stats, allocation-bar, holdings-list, holding-detail, holding-form | `InvestmentsStore` |
| `reports` | `/reports` | period-tabs, kpi-row, category-donut, delta-list, merchants-list, savings-rate-chart | recurso por periodo |
| `integrations` | `/integrations`, `/integrations/imports/:id` | import-dropzone, import-review-table, rules-list | `ImportStore` con sondeo de estado |
| `notifications` | `/notifications` | notification-list, preferences-panel, quiet-hours | `NotificationsStore` (core, por el badge) |
| `settings` | `/settings/profile`, `/members`, `/preferences`, `/security` | profile-form, members-list, invite-dialog, preferences-form | local más stores de core |

Todas las rutas se cargan de forma diferida. Transferencias no es una feature: es un modo del formulario de transacciones.

### O.3 Gestión de estado

| Opción | Evaluación |
|---|---|
| **Signals y servicios store por feature** | **Elegida.** El estado es casi todo estado de servidor por pantalla; no hay flujos complejos de cliente |
| RxJS | Solo donde hay un flujo real: debounce de búsqueda, eventos de SignalR, sondeo de importación |
| NgRx Store | Innecesario: no hay estado compartido complejo ni necesidad de time-travel |
| NgRx SignalStore | Útil más adelante si un store supera lo manejable a mano |

Patrón de un store: signals privados para filtros y selección, `resource` o `httpResource` para lo que viene del servidor (comprobar su estabilidad en la versión instalada), `computed` para derivados, y métodos de comando que llaman a la API y recargan. Formularios con Signal Forms, estables desde Angular 22.

Estado global en `core`: sesión, workspace actual, mes seleccionado, tema, contador de no leídas. Cambiar de workspace reinicia los stores de feature.

### O.4 Contrato con el backend

- Los tipos TypeScript se generan desde `docs/api/openapi.v1.json` con `npm run api:generate` y el resultado (`core/api/schema.ts`) se versiona. No se escriben a mano modelos de API, como ocurría por duplicado en `core/models` y `features/*/models` de V1.
- Dinero como `{ amount: number, currency: string }` con 2 decimales; cantidades de inversión como `string`.
- Todo POST con efecto financiero lleva `Idempotency-Key` generado al abrir el formulario.
- Cada vista implementa los cuatro estados: carga (skeleton), vacío, error con reintento, y datos.
- `ProblemDetails` se traduce por `code`; los errores de validación se pintan en su campo.

### O.5 Sistema de diseño

Los tokens del mock (`--bg`, `--sf`, `--sf2`, `--sf3`, `--bd`, `--tx`, `--tx2`, `--mu`, `--ac`, `--bl`, `--rd`, `--am`, `--vi` y sus variantes suaves) pasan tal cual a `_tokens.scss`. Tipografía Manrope e iconos Material Symbols Rounded. Los gráficos del mock son CSS puro (barras, `conic-gradient`); no se añade librería de gráficos hasta que Reportes lo pida.

### Guards

El router ejecuta a la vez todos los guards de una ruta, no uno tras otro. Por eso cada ruta lleva un solo guard y cada guard es completo: carga la sesión y los workspaces antes de decidir. `appGuard` protege la aplicación (sin sesión lleva a `/login` con la dirección de vuelta; con un workspace sin configurar lleva a su dueño a `/onboarding`), `onboardingGuard` protege esa página y `guestGuard` saca de las páginas de acceso a quien ya tiene sesión.

Dentro de un guard, `inject()` solo funciona antes del primer `await`.

### Errores de la API

La API responde con un `code` estable y un mensaje en inglés pensado para quien desarrolla. El texto que ve la persona sale de `core/errors/error-messages.ts`, que traduce cada `code`. Un código nuevo sin entrada muestra un mensaje genérico.
