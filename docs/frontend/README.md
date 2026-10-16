# Frontend

Todo lo necesario para cambiar `finanzas-hub-web/`: un SPA Angular 22 sin zonas, con componentes standalone, signals y Signal Forms, que habla con la API por rutas relativas y recibe avisos en tiempo real por SignalR. Es un **repositorio git propio**, anidado dentro del principal. Si es tu primer día, lee antes [getting-started/](../getting-started/README.md).

## Mapa mental en 30 segundos

```text
Router (carga diferida, un guard por ruta)
  └─ Shell (layout: sidebar, topbar, tab bar móvil, buscador)
       └─ Página de la feature
            ├─ httpResource(() => url | undefined)   ← lee; se recarga cuando cambia la URL
            ├─ signal / computed                     ← estado de pantalla y derivados
            ├─ <feature>-api.ts                      ← comandos: Promise, con Idempotency-Key si mueve dinero
            └─ shared/ui                             ← dialog, field, skeleton, empty-state, error-state…
core/: SessionStore, WorkspaceStore, PeriodStore, ThemeStore, NotificationCenter, interceptores, errores
Tipos de la API: generados desde docs/api/openapi.v1.json (npm run api:generate)
```

Tres ideas que lo explican casi todo:

1. **Todo lo que se pinta es un signal.** Sin zonas, Angular solo se entera de los cambios que pasan por signals.
2. **El servidor manda.** Las páginas leen con `httpResource` y no recalculan cifras: los totales llegan hechos de la API.
3. **Una feature no conoce a otra.** Se encuentran en el router; lo comparte `core` (estado) o `shared` (UI).

## Orden de lectura

| # | Documento | Para qué |
|---|---|---|
| 1 | [architecture.md](architecture.md) | Estructura, forma de una feature, reglas de dependencia, estado |
| 2 | [patterns.md](patterns.md) | Los patrones del código con ejemplos reales |
| 3 | [best-practices.md](best-practices.md) | Cómo escribir componentes, estado, formularios y estilos |
| 4 | [ui-design-system.md](ui-design-system.md) | Tokens, componentes de `shared/ui`, responsive y temas |
| 5 | [testing.md](testing.md) | Vitest, `provideSession`, `HttpTestingController`, Playwright |
| 6 | [common-bugs.md](common-bugs.md) | Errores típicos y cómo evitarlos |
| 7 | [recipes.md](recipes.md) | Paso a paso: pantalla, llamada, formulario, componente |
| — | [study-guide.md](study-guide.md) | Conceptos que conviene estudiar |

## Si vas a…

| Tarea | Lee | Y no olvides |
|---|---|---|
| Crear una sección | [recipes.md](recipes.md#1-una-sección-nueva) | Cuatro estados, tres breakpoints, dos temas |
| Consumir un endpoint nuevo | [recipes.md](recipes.md#2-un-endpoint-nuevo-de-la-api) | `npm run api:generate` y el alias en `api-types.ts` |
| Hacer un formulario que mueve dinero | [patterns.md](patterns.md#6-clave-de-idempotencia-por-formulario) | Clave creada al abrir el formulario |
| Mostrar un error nuevo | [patterns.md](patterns.md#7-errores-de-la-api) | Entrada en `error-messages.ts` |
| Reaccionar en tiempo real | [patterns.md](patterns.md#9-tiempo-real) | Solo la señal llega por SignalR; los datos, por HTTP |
| Añadir un componente reutilizable | [ui-design-system.md](ui-design-system.md), [recipes.md](recipes.md#4-un-componente-en-sharedui) | Sin negocio y sin llamadas a la API |

## Relacionado

- Contrato de la API: [backend/api-conventions.md](../backend/api-conventions.md) y `docs/api/openapi.v1.json`.
- Por qué signals y no NgRx: [ADR-010](../adr/ADR-010-estado-en-el-frontend.md).
- Despliegue del frontend (imagen nginx): [devops/deployment.md](../devops/deployment.md).
- Mocks visuales de referencia: `Mocks_finanzas-hub-angular/` en la raíz del repositorio.

## Comandos

```powershell
# desde finanzas-hub-web
npm ci
npm start                     # http://localhost:4200, con proxy a la API en :5166
npm test -- --watch=false
npm run lint
npm run build
npm run api:generate          # tras actualizar docs/api/openapi.v1.json
npm run e2e                   # API y Worker en marcha; $env:PW_CHANNEL='chrome' para usar el Chrome instalado
```

No hay `ng` global: usa `npx ng ...`.
