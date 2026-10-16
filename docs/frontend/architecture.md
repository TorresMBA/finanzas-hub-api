# Arquitectura del frontend

## O. Angular architecture

Raíz: `finanzas-hub-web/` (repositorio git propio). Angular 22 sin zonas, componentes standalone, `OnPush`, signals y Signal Forms. Este documento describe lo que existe tras la fase 13.

**Nombres.** Convención de Angular 20 en adelante: archivos y clases sin sufijo de rol (`shell.ts` con `class Shell`, `budgets-api.ts` con `class BudgetsApi`), un archivo por clase y `.spec.ts` para los tests. La carpeta indica el rol.

### O.1 Estructura

```text
finanzas-hub-web/
  proxy.conf.json               /api, /health, /openapi y /hubs (WebSocket) hacia http://localhost:5166
  eslint.config.js              angular-eslint y reglas de límites entre carpetas
  playwright.config.ts          E2E contra el stack local
  Dockerfile                    nginx: sirve el SPA y reenvía /api, /hubs y /webhooks a la API
  Jenkinsfile                   CI/CD en Mercury
  e2e/                          un spec por área (auth, ledger, budgets, goals, shared…) y support.ts
  src/
    styles/                     _tokens (colores y medidas del mock, tema oscuro y claro), _mixins (breakpoints),
                                _reset, _typography, _forms
    app/
      app.ts, app.config.ts     arranque: router, HttpClient con los interceptores
      app.routes.ts             rutas de acceso, onboarding y el Shell con las secciones (carga diferida)
      core/                     singletons sin UI
        api/                    schema.ts (generado), api-types.ts (alias cortos), managed-accounts.ts
        auth/                   SessionStore; appGuard, onboardingGuard, guestGuard
        workspace/              WorkspaceStore (workspace actual), zonas horarias
        period/                 PeriodStore: mes de la barra superior; TODAY inyectable
        http/                   interceptores: credenciales, X-Correlation-Id, Idempotency-Key, errores
        errors/                 ApiError (ProblemDetails) y error-messages.ts (code → texto en español)
        notifications/          NotificationCenter: no leídas, toast y SignalR (/hubs/realtime)
        theme/                  ThemeStore
        testing/                provideSession: sesión y workspace simulados para tests de componentes
      layout/                   el marco
        navigation.ts           las secciones y grupos del mock (NAV_GROUPS, NAV_ITEMS)
        shell/, sidebar/, topbar/, mobile-tab-bar/, more-sheet/, search/ (Ctrl+K)
      shared/ui/                UI reutilizable sin negocio: dialog, field, stat-card, money, dates, tags,
                                icon, icon-tile, page-header, empty-state, error-state, skeleton, brand-mark
      features/                 una carpeta por sección
        auth/ onboarding/ dashboard/ transactions/ accounts/ categories/ budgets/ calendar/
        goals/ shared/ (gastos compartidos) integrations/ investments/ reports/ notifications/
        settings/ placeholder/
```

### O.2 Forma de una feature

Tomando `features/budgets/` como referencia:

| Archivo | Rol |
|---|---|
| `budgets.routes.ts` | `BUDGETS_ROUTES`, que `app.routes.ts` carga de forma diferida |
| `budgets.ts`, `.html`, `.scss` | La página: lee con `httpResource`, deriva con `computed`, muestra los cuatro estados |
| `budgets-api.ts` | `BudgetsApi` (`providedIn: 'root'`): URLs para los `httpResource` y métodos de comando (`create`, `adjust`, `end`) que devuelven `Promise` |
| `budget-form.ts` | Formulario en un `Dialog`, con Signal Forms (`form`, `validate`, `submit`) |
| `budgets.spec.ts` | Test del componente con `HttpTestingController` y `provideSession` |

No hay stores por feature: el estado de servidor lo tienen los `httpResource` de la página, y el estado de pantalla (filtros, selección, formulario abierto) son `signal` locales. Cuando algo cambia, la página recarga sus recursos.

| Feature | Rutas | Notas |
|---|---|---|
| `auth` | `/login`, `/register`, `/forgot-password`, `/reset-password`, `/confirm-email`, `/invite/:token` | `guestGuard` en login y registro; la sesión vive en `SessionStore` |
| `onboarding` | `/onboarding` | Moneda base y zona horaria del espacio |
| `dashboard` | `/` | Todo viene de `GET …/dashboard`; no suma nada en el cliente |
| `transactions` | `/transactions` | Filtros como signals, lista por cursor, detalle y formulario (gasto, ingreso o transferencia) |
| `accounts` | `/accounts` | Cuentas, patrimonio, tarjetas, pago de tarjeta y tipos de cambio |
| `categories` | `/categories` | Categorías, subcategorías y tags |
| `budgets` | `/budgets` | Presupuestos del mes de la barra superior y su detalle |
| `calendar` | `/calendar` | Movimientos programados y su calendario |
| `goals` | `/goals` | Metas; se recarga con `NotificationCenter.resourceChanged` |
| `shared` | `/shared` | Gastos compartidos: grupos, gastos y liquidaciones |
| `integrations` | `/integrations` | Importación de archivos y reglas de clasificación |
| `investments` | `/investments` | Portafolio, depósitos a plazo, movimientos y valorizaciones |
| `reports` | `/reports` | Mes, trimestre o año; exportación a Excel y CSV |
| `notifications` | `/notifications` | Bandeja, preferencias y canal de WhatsApp |
| `settings` | `/settings/profile`, `/workspace`, `/preferences`, `/security` | Perfil, espacio y miembros (con invitaciones), preferencias y seguridad |

`app.routes.ts` enruta todas las secciones de `NAV_ITEMS`; la que todavía no tiene feature muestra `FeaturePlaceholder`. Construir una sección es añadir su entrada al mapa `BUILT`.

### O.3 Reglas de dependencia

`features` → `shared`, `core`; `layout` → `layout`, `shared`, `core`; `shared` → `shared`; `core` → `core`. **Una feature no importa a otra**: se encuentran solo en el router. Lo comprueba `eslint-plugin-boundaries` (`boundaries/dependencies` en `eslint.config.js`): un import que cruza un límite hace fallar `npm run lint`.

### O.4 Gestión de estado

| Opción | Uso |
|---|---|
| **Signals** (`signal`, `computed`, `linkedSignal`) | Todo el estado que se pinta. Sin zonas, es lo que dispara la detección de cambios |
| **`httpResource`** | Lecturas del servidor. Su URL es un `computed` que vale `undefined` mientras falta un dato (por ejemplo el workspace), y así espera |
| **Stores en `core`** | Lo global: `SessionStore`, `WorkspaceStore`, `PeriodStore`, `ThemeStore`, `NotificationCenter` |
| **RxJS** | Solo donde Angular lo exige (`HttpClient`, interceptores); se convierte a `Promise` con `firstValueFrom` |
| NgRx | No se usa ([ADR-010](../adr/ADR-010-estado-en-el-frontend.md)) |

Cambiar de workspace cambia las URLs de los recursos, que se recargan solos.

### O.5 Contrato con el backend

- Los tipos se generan desde `docs/api/openapi.v1.json` con `npm run api:generate` en `core/api/schema.ts`, que se versiona. `core/api/api-types.ts` solo les da nombres cortos (`export type Budget = Schemas['BudgetResponse']`). No se escriben tipos de la API a mano.
- Rutas relativas (`/api/v1/workspaces/{id}/...`): el proxy (local) o nginx (despliegue) las lleva a la API, en el mismo origen, para que la cookie de sesión funcione.
- Todo comando con efecto financiero lleva una `Idempotency-Key` creada al abrir el formulario.
- Cada vista implementa los cuatro estados: carga (skeleton), vacío, error con reintento, y datos.
- `ProblemDetails` llega como `ApiError`; el texto sale de `error-messages.ts` por `code` y los errores de validación se pintan en su campo.

### O.6 Sistema de diseño

Los tokens del mock (`--bg`, `--sf`, `--sf2`, `--sf3`, `--bd`, `--tx`, `--tx2`, `--mu`, `--ac`, `--bl`, `--rd`, `--am`, `--vi` y sus variantes suaves) están en `src/styles/_tokens.scss` para el tema oscuro y el claro. Tipografía Manrope e iconos Material Symbols Rounded. Gráficos con CSS y SVG propios, sin librería. Detalle en [ui-design-system.md](ui-design-system.md).

### Guards

El router ejecuta a la vez todos los guards de una ruta, no uno tras otro. Por eso cada ruta lleva un solo guard y cada guard es completo: carga la sesión y los workspaces antes de decidir. `appGuard` protege la aplicación (sin sesión lleva a `/login` con la dirección de vuelta; con un workspace sin configurar lleva a su dueño a `/onboarding`), `onboardingGuard` protege esa página y `guestGuard` saca de las páginas de acceso a quien ya tiene sesión.

Dentro de un guard, `inject()` solo funciona antes del primer `await`.

### Errores de la API

La API responde con un `code` estable y un mensaje en inglés pensado para quien desarrolla. El texto que ve la persona sale de `core/errors/error-messages.ts`, que traduce cada `code`. Un código nuevo sin entrada muestra un mensaje genérico.

### Scripts

| Script | Qué hace |
|---|---|
| `npm start` | Servidor de desarrollo en `http://localhost:4200` con proxy a la API |
| `npm test -- --watch=false` | Tests unitarios (Vitest + jsdom), una pasada |
| `npm run lint` | ESLint, incluidas las reglas de límites |
| `npm run api:generate` | Regenera `core/api/schema.ts` desde el contrato OpenAPI |
| `npm run e2e` | Playwright; requiere la API y el Worker en marcha. `PW_CHANNEL=chrome` usa el navegador instalado |
| `npm run build` | Build de producción en `dist/finanzas-hub-web/browser` |
