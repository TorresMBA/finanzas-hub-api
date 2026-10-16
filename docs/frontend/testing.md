# Tests del frontend

Dos niveles: tests de componente con Vitest y jsdom (rápidos, sin navegador ni API) y recorridos E2E con Playwright contra el stack local. La estrategia de todo el sistema está en [backend/testing.md](../backend/testing.md).

| Nivel | Dónde | Herramienta | Necesita |
|---|---|---|---|
| Componente, store, utilidades | `src/**/*.spec.ts` junto al código | Vitest + jsdom (`npm test`) | Nada |
| Recorridos de punta a punta | `e2e/*.spec.ts` | Playwright (`npm run e2e`) | API y Worker en marcha |
| Fronteras entre carpetas | `eslint.config.js` | `npm run lint` | Nada |

## Tests de componente

Patrón, tomado de `src/app/features/budgets/budgets.spec.ts`:

```ts
beforeEach(async () => {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(withInterceptors([idempotencyKeyInterceptor, apiErrorInterceptor])),
      provideHttpClientTesting(),
      provideSession(),                                       // persona con sesión y un workspace, sin HTTP
      { provide: TODAY, useValue: () => new Date(2026, 9, 13) },   // «hoy» fijo
    ],
  });
  await TestBed.inject(WorkspaceStore).ensureLoaded();
  http = TestBed.inject(HttpTestingController);
  fixture = TestBed.createComponent(Budgets);
  element = fixture.nativeElement;
  TestBed.tick();
});

afterEach(() => http.verify());   // ninguna petición sin responder ni inesperada
```

Claves:

- **`provideSession()`** (`src/app/core/testing/signed-in.ts`) pone el componente detrás de los guards con `TEST_USER` y `TEST_WORKSPACE`. `provideSession(null)` simula a alguien sin sesión. También desactiva el tiempo real (`LIVE_NOTIFICATIONS = false`).
- **`TODAY`** fija la fecha para que los meses y «Día 13 de 31» sean deterministas.
- **`HttpTestingController`**: responde cada URL esperada con `http.expectOne(url).flush(datos)` o con un error (`flush(problem, { status: 422, statusText: '…' })`) para probar los mensajes.
- **Sin zonas**, después de responder hay que dejar correr las promesas y pintar: `await new Promise((r) => setTimeout(r)); TestBed.tick();` y `await fixture.whenStable()`.
- **Interactúa como una persona**: busca botones por su texto, escribe en los `input` y lanza `input`/`submit`; no llames a métodos `protected` del componente.
- **Comprueba lo que se ve** (texto, filas, estado vacío) y **las peticiones que salen** (método, URL, cuerpo y cabecera `Idempotency-Key`).

Qué cubrir en el spec de una página: datos, vacío, error con reintento, la acción principal (crear o ajustar) con su recarga, y un error de la API traducido.

## E2E con Playwright

`playwright.config.ts` arranca `npm start` y usa `http://localhost:4200`; la API debe estar en `http://localhost:5166` y el Worker en marcha (envía los correos que algunos recorridos leen).

```powershell
# terminales aparte: API y Worker (ver getting-started/local-development.md)
cd finanzas-hub-web
$env:PW_CHANNEL = 'chrome'    # o instala el Chromium de Playwright: npx playwright install chromium
npm run e2e
```

Ayudas de `e2e/support.ts`:

| Función | Qué hace |
|---|---|
| `signUp(page, name)` | Crea una cuenta y su workspace por la API y deja la página con sesión |
| `newEmail(name)` | Correo único para que cada test cree sus propios usuarios |
| `linkEmailedTo(email, pattern)` | Busca un enlace en los `.eml` que escribe el Worker (`E2E_MAIL_DIR` o `%TEMP%\personal-hub-mail`) |
| `PASSWORD` | Contraseña fija de los usuarios desechables de los tests |

Los recorridos están en [backend/testing.md §V.4](../backend/testing.md#v4-recorridos-e2e). Un E2E por recorrido crítico, no uno por pantalla: lo detallado se prueba en los specs de componente.

En el CI de Mercury solo corren lint, tests de componente y build; los E2E se ejecutan en local antes de cerrar una fase.

## Errores típicos en los tests

| Síntoma | Causa |
|---|---|
| `Expected one matching request … found none` | Falta `settle()` antes de `expectOne`, o la URL del recurso aún es `undefined` (workspace sin cargar) |
| `Expected no open requests, found 1` | Una petición que el test no respondió; responde también las secundarias (categorías, detalle) |
| La plantilla no cambia tras un clic | Falta `TestBed.tick()` o `fixture.whenStable()` |
| El mes esperado no coincide | No se fijó `TODAY` |
| El E2E no encuentra el correo | El Worker no está en marcha |
