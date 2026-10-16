# Guía de estudio del frontend

Conceptos que conviene dominar para trabajar en `finanzas-hub-web`, por prioridad. Angular cambió mucho desde la versión 16: si vienes de Angular con módulos, `zone.js` y RxJS en todo, empieza por lo imprescindible aunque ya conozcas el framework.

## Imprescindible (primera semana)

| Concepto | Qué saber | Dónde se usa aquí | Para estudiar |
|---|---|---|---|
| TypeScript | Tipos de unión, `readonly`, genéricos, `Record`, narrowing | Todo el código | [TypeScript Handbook](https://www.typescriptlang.org/docs/handbook/intro.html) |
| Componentes standalone | `imports` en el componente, sin `NgModule` | Todos los componentes | [Components](https://angular.dev/guide/components) |
| Signals | `signal`, `computed`, `effect`, `linkedSignal`, `untracked` | Estado de páginas y stores | [Signals](https://angular.dev/guide/signals) |
| Angular sin zonas | Qué dispara la detección de cambios sin `zone.js` | `app.config.ts`, todo el estado | [Zoneless](https://angular.dev/guide/zoneless) |
| Control de flujo | `@if`, `@for` con `track`, `@switch` | Plantillas | [Control flow](https://angular.dev/guide/templates/control-flow) |
| `httpResource` | Lecturas reactivas, `value`, `isLoading`, `error`, `reload` | Páginas | [httpResource](https://angular.dev/guide/http/http-resource) |
| Inputs y outputs | `input()`, `input.required()`, `output()` | Formularios y `shared/ui` | [Inputs](https://angular.dev/guide/components/inputs) |

## Importante (primer mes)

| Concepto | Qué saber | Dónde se usa aquí | Para estudiar |
|---|---|---|---|
| Signal Forms | `form`, `validate`, `submit`, `[formField]` | `*-form.ts` | [Forms](https://angular.dev/guide/forms) |
| Router | Rutas con carga diferida, guards funcionales, `withComponentInputBinding` | `app.routes.ts`, `core/auth/guards.ts` | [Routing](https://angular.dev/guide/routing) |
| `HttpClient` e interceptores | Interceptores funcionales, `HttpContext` | `core/http/api-interceptors.ts` | [HTTP](https://angular.dev/guide/http) |
| Inyección de dependencias | `inject()`, `providedIn: 'root'`, `InjectionToken`, contexto de inyección | Stores, `TODAY`, `LIVE_NOTIFICATIONS` | [DI](https://angular.dev/guide/di) |
| CSS moderno | Custom properties, grid, flex, `conic-gradient`, media queries | `styles/`, `.scss` de cada feature | [MDN CSS](https://developer.mozilla.org/docs/Web/CSS) |
| Contrato OpenAPI | Generación de tipos y por qué no se escriben a mano | `npm run api:generate` | [openapi-typescript](https://openapi-ts.dev/) |
| Testing de componentes | `TestBed`, `HttpTestingController`, Vitest | `*.spec.ts` | [Testing](https://angular.dev/guide/testing), [Vitest](https://vitest.dev/) |

## Para profundizar

| Concepto | Qué saber | Dónde se usa aquí | Para estudiar |
|---|---|---|---|
| RxJS mínimo | `Observable`, `firstValueFrom`, operadores en interceptores | `HttpClient`, interceptores | [RxJS](https://rxjs.dev/guide/overview) |
| SignalR (cliente) | Conexión, reconexión, mensajes | `core/notifications/notification-center.ts` | [Cliente JavaScript de SignalR](https://learn.microsoft.com/aspnet/core/signalr/javascript-client) |
| Accesibilidad | Roles ARIA, foco, teclado, contraste | Diálogos, formularios, estados | [Accesibilidad en Angular](https://angular.dev/best-practices/a11y), [WAI-ARIA APG](https://www.w3.org/WAI/ARIA/apg/) |
| Seguridad web | Cookies `HttpOnly`/`SameSite`, XSS, por qué no guardar tokens en `localStorage` | Sesión, interceptores | [Seguridad en Angular](https://angular.dev/best-practices/security), [OWASP XSS](https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html) |
| E2E | Selectores por rol y texto, esperas automáticas, aislamiento por usuario | `e2e/` | [Playwright](https://playwright.dev/docs/intro) |
| ESLint y fronteras | Reglas de `eslint-plugin-boundaries` | `eslint.config.js` | [eslint-plugin-boundaries](https://github.com/javierbrea/eslint-plugin-boundaries) |
| Rendimiento | Carga diferida, `track`, `computed` frente a métodos en plantilla | Toda la aplicación | [Performance](https://angular.dev/best-practices/runtime-performance) |

## Ejercicio recomendado

Lee `features/budgets/` completo en este orden: `budgets.routes.ts`, `budgets-api.ts`, `budgets.ts`, `budgets.html`, `budget-form.ts` y `budgets.spec.ts`. Después cambia el mes en la barra superior con las herramientas del navegador abiertas: verás cómo `PeriodStore` cambia la URL del `httpResource` y la página se recarga sola. Por último, rompe a propósito una regla (importa algo de `features/goals` desde `features/budgets`) y ejecuta `npm run lint` para ver la frontera en acción.
