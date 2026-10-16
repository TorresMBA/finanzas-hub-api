# Buenas prácticas del frontend

Cómo se escribe código en `finanzas-hub-web`. Lo que comprueba una herramienta se marca con **[lint]**, **[build]** o **[test]**.

## Componentes

- **Standalone y `OnPush`** en todos los componentes.
- **Selector con prefijo `app-`** en kebab-case **[lint]**.
- **Sin sufijos de rol** en archivos y clases: `budget-form.ts` con `class BudgetForm` ([architecture.md](architecture.md)).
- **`input()`, `input.required()` y `output()`**, no los decoradores `@Input`/`@Output`.
- **`inject()`** en vez de inyectar por constructor.
- **Miembros `protected`** para lo que usa la plantilla y `private` para lo interno: la plantilla es parte del componente, pero nadie de fuera debe usarlo.
- **Plantillas con el control de flujo nuevo** (`@if`, `@for`, `@switch`). En `@for`, `track` por un id estable, nunca por el índice si la lista cambia.
- **Páginas que orquestan, formularios que emiten.** El formulario recibe su objetivo por `input`, llama al servicio de la feature y emite `saved`/`closed`; la página decide qué recargar.
- **Copia la forma de una feature existente** (`features/budgets/` es buena referencia) antes de inventar otra.

## Estado y reactividad

- **Todo lo que se pinta es un signal** o se deriva de uno con `computed`. Una propiedad normal que cambia no vuelve a pintar (no hay zonas).
- **No guardes derivados**: calcula con `computed`, no copies valores entre signals con `effect`.
- **`effect` solo para efectos de verdad**: recargar un recurso por una señal de tiempo real, sincronizar con `localStorage`, enfocar un campo.
- **`httpResource` para leer** y servicio `*-api.ts` con `Promise` para escribir.
- **Recarga en vez de parchear**: tras un comando, `resource.reload()`. El servidor calcula los totales.
- **Sin suscripciones manuales** a observables en componentes. Si hace falta RxJS, conviértelo con `firstValueFrom` o `toSignal`.

## Datos y API

- **Tipos generados, nunca escritos a mano**: `npm run api:generate` y un alias en `core/api/api-types.ts`.
- **Rutas relativas** (`/api/v1/...`) construidas en el servicio de la feature a partir de `WorkspaceStore.current()`.
- **URL `undefined` mientras falte un dato**: el `httpResource` espera en vez de pedir con `undefined` en la ruta.
- **Idempotencia**: los formularios que mueven dinero crean su clave al construirse ([patterns.md](patterns.md#6-clave-de-idempotencia-por-formulario)).
- **Errores por `code`**: `errorMessage(error)`; un `code` nuevo lleva su texto en `error-messages.ts`.
- **No recalcules dinero en el cliente** salvo para mostrar (una barra, un porcentaje). Saldos, totales y repartos llegan de la API.

## Dinero y fechas

- **Los importes pueden llegar como `number` o `string`** (`Amount` en `shared/ui/money.ts`): conviértelos con `Number()` solo para mostrar o comparar, y suma con `sumAmounts`.
- **Muestra con `MoneyPipe`/`formatMoney`**, nunca con `toFixed` y un símbolo a mano.
- **Lee montos con `parseAmount`**: acepta coma o punto y rechaza más de dos decimales.
- **Fechas contables como texto ISO** (`2026-10-15`) y meses como `2026-10`. No las conviertas a `Date` para compararlas: la zona horaria del navegador puede correr el día.
- **«Hoy» sale de `PeriodStore`/`TODAY`**, que los tests sustituyen.

## Estilos

- **Tokens de `_tokens.scss`**, nunca colores ni sombras sueltos ([ui-design-system.md](ui-design-system.md)).
- **Mixins de breakpoint** en vez de media queries escritas a mano.
- **Clases globales de `_forms.scss`** (`.button`, `.input`, `.card`…) antes de crear estilos nuevos.
- **Estilos del componente en su `.scss`**: encapsulados, sin `::ng-deep`.
- **Tres tamaños y dos temas** antes de dar una pantalla por terminada.

## Arquitectura

- **Una feature no importa a otra** **[lint]**.
- **`shared` no conoce el negocio**; **`core` no tiene UI**.
- **Carga diferida** de cada feature con su `*.routes.ts`.
- **Un guard por ruta**, con todos los `inject()` antes del primer `await`.

## Seguridad y privacidad

- **Nada de secretos en el frontend**: todo lo que va en el bundle es público.
- **La sesión es una cookie `HttpOnly`**: no guardes tokens en `localStorage`. En `localStorage` solo preferencias de pantalla (el workspace elegido, el tema).
- **No uses `innerHTML`** con datos de la persona; si fuera inevitable, sanitiza.
- **La autorización la decide la API**: ocultar un botón es comodidad, no seguridad.

## Tests

- **Un spec por página** que cubra datos, vacío y error, y las acciones principales, con `provideSession` y `HttpTestingController` ([testing.md](testing.md)).
- **Un E2E por recorrido crítico**, no por pantalla.
- **`npm run lint` y `npm test -- --watch=false` en verde** antes de confirmar **[lint] [test]**.
