# Patrones del frontend

Cada patrón con su porqué y un ejemplo del código. Las rutas son relativas a `finanzas-hub-web/src/app/`.

## 1. Componentes standalone, `OnPush` y sin zonas

La aplicación arranca sin `zone.js`: Angular no vuelve a pintar porque haya terminado un `setTimeout` o una petición, sino porque **cambió un signal** que lee una plantilla. Por eso todos los componentes son standalone, declaran `changeDetection: ChangeDetectionStrategy.OnPush` y guardan en signals todo lo que se muestra.

```ts
@Component({
  selector: 'app-budgets',
  imports: [BudgetForm, EmptyState, ErrorState, Skeleton, ...],
  templateUrl: './budgets.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Budgets { ... }
```

## 2. Leer con `httpResource`

Las lecturas son recursos reactivos: la URL es una función que lee signals, y el recurso se vuelve a pedir cuando cambia (`features/budgets/budgets.ts`):

```ts
protected readonly budgets = httpResource<BudgetMonth>(() => this.api.listUrl(this.period.isoMonth()));
protected readonly detail = httpResource<BudgetDetail>(() => this.api.detailUrl(this.selected()?.id ?? null, this.period.isoMonth()));
```

- Si la función devuelve **`undefined`, el recurso espera** sin pedir nada. Así se espera al workspace: `BudgetsApi.listUrl` devuelve `undefined` mientras `WorkspaceStore.current()` es nulo.
- Cambiar el mes de la barra superior (`PeriodStore`) o el workspace cambia la URL y recarga solo.
- `value()`, `isLoading()`, `hasValue()`, `error()` y `reload()` alimentan los cuatro estados (§5).

## 3. Escribir con un servicio `<feature>-api.ts`

Los comandos van en un servicio raíz por feature que devuelve `Promise` (`features/budgets/budgets-api.ts`):

```ts
@Injectable({ providedIn: 'root' })
export class BudgetsApi {
  private readonly http = inject(HttpClient);
  private readonly workspaces = inject(WorkspaceStore);
  private readonly base = computed(() => `/api/v1/workspaces/${this.workspaces.current()?.id}`);

  listUrl(month: string): string | undefined {
    return this.workspaces.current() ? `${this.base()}/budgets?month=${month}` : undefined;
  }

  create(request: CreateBudgetRequest): Promise<Budget> {
    return firstValueFrom(this.http.post<Budget>(`${this.base()}/budgets`, request));
  }
}
```

Tras un comando con éxito, la página llama a `reload()` de los recursos afectados. No se mantiene una copia local que haya que sincronizar a mano.

## 4. Estado de pantalla con `signal`, `computed` y `linkedSignal`

- `signal` para lo que decide la persona: filtro, elemento elegido, formulario abierto, `busy`, `failure`.
- `computed` para todo lo derivado (filas filtradas, contadores, resumen). Nunca se guarda un derivado en otro signal a mano.
- `linkedSignal` para un valor que se reinicia cuando cambia su origen pero que la persona puede editar: el modelo de un formulario que se rellena con el presupuesto elegido (`budget-form.ts`).

```ts
protected readonly rows = computed(() => {
  const filter = this.filter();
  return this.all().filter((budget) => filter === 'all' || filterOf(budget) === filter);
});
```

## 5. Los cuatro estados de cada vista

Cada vista distingue carga, error, vacío y datos con los componentes de `shared/ui` (`features/budgets/budgets.html`):

```html
@if (!budgets.hasValue() && budgets.isLoading()) {
  <app-skeleton height="150px" />
} @else if (budgets.error()) {
  <app-error-state title="No se pudieron cargar tus presupuestos" (retry)="budgets.reload()" />
} @else if (all().length === 0) {
  <app-empty-state ...>…</app-empty-state>
} @else {
  …datos…
}
```

`!hasValue() && isLoading()` evita que la pantalla parpadee a esqueleto cuando solo se está recargando.

## 6. Clave de idempotencia por formulario

Un comando que mueve dinero debe poder reintentarse sin duplicarse. La clave se crea **al construir el formulario**, no al enviar, y viaja en el contexto de la petición (`core/http/api-interceptors.ts`):

```ts
// en el formulario (por ejemplo features/accounts/account-form.ts)
private readonly idempotencyKey = newIdempotencyKey();

// en el servicio de la feature (features/accounts/accounts-api.ts)
const withKey = (key: string) => new HttpContext().set(IDEMPOTENCY_KEY, key);
this.http.post<X>(url, body, { context: withKey(key) });
```

`idempotencyKeyInterceptor` la convierte en la cabecera `Idempotency-Key`. Un doble clic o un reintento tras un error de red repiten la misma clave, y la API aplica el efecto una sola vez.

## 7. Errores de la API

`apiErrorInterceptor` convierte toda respuesta fallida de `/api/` en un `ApiError` (`core/errors/api-error.ts`) con `status`, `code` estable, `traceId` y `fieldErrors` por campo. La interfaz nunca muestra el mensaje en inglés de la API: `errorMessage(error)` (`core/errors/error-messages.ts`) traduce el `code` y, si no lo conoce, muestra un texto genérico.

```ts
try {
  await this.api.create(request);
} catch (error) {
  this.failure.set(errorMessage(error));
}
```

## 8. Formularios con Signal Forms

`@angular/forms/signals`: el modelo es un signal, `form()` describe validaciones y `submit()` solo ejecuta si son válidas (`features/budgets/budget-form.ts`):

```ts
protected readonly budgetForm = form(this.model, (path) => {
  validate(path.amount, ({ value }) =>
    (parseAmount(value()) ?? 0) > 0 ? undefined : { kind: 'amount', message: 'Un monto mayor que cero, con hasta dos decimales.' });
});

protected save(): void {
  void submit(this.budgetForm, async () => { … });
}
```

En la plantilla, `[formField]="budgetForm.amount"` y `<app-field [state]="budgetForm.amount()">` pintan el error junto al campo. Los formularios se abren en `<app-dialog>` y emiten `saved` / `closed` a la página.

## 9. Tiempo real

`NotificationCenter` (`core/notifications/notification-center.ts`) abre la conexión SignalR con `/hubs/realtime` cuando aparece el marco de la sesión. Por el hub solo viaja una señal:

- `unread` y `toast`: avisos nuevos.
- `resourceChanged`: «algo compartido cambió» (`{ resource, id }`), un objeto nuevo cada vez.

La página que lo necesita (por ejemplo, metas) lee ese signal en un `effect` y recarga su recurso por HTTP, donde se aplica la autorización. Además, cada minuto y al volver el foco a la ventana se vuelve a pedir el contador, por si se perdió la conexión. En los tests, `LIVE_NOTIFICATIONS = false` evita conexiones reales.

## 10. Stores globales en `core`

Servicios `providedIn: 'root'` con signals privados y lectura pública de solo lectura:

| Store | Qué guarda |
|---|---|
| `SessionStore` | La persona con sesión y sus preferencias; `ensureLoaded()` |
| `WorkspaceStore` | Los workspaces y el actual (`current`); recuerda la elección en `localStorage` |
| `PeriodStore` | El mes de la barra superior (`month`, `isoMonth`, `label`); «hoy» viene del token `TODAY` |
| `ThemeStore` | Tema oscuro o claro |
| `NotificationCenter` | No leídas, toast y cambios en tiempo real |

```ts
private readonly selected = signal<Month>(this.currentMonth());
readonly month = this.selected.asReadonly();
```

## 11. Guards completos

El router ejecuta los guards de una ruta en paralelo, así que cada ruta tiene **un** guard que carga todo lo que necesita (`core/auth/guards.ts`): `appGuard`, `onboardingGuard`, `guestGuard`. Todos los `inject()` van **antes del primer `await`**: después ya no hay contexto de inyección.

## 12. Interceptores funcionales

Registrados en `app.config.ts`, en este orden:

| Interceptor | Qué hace |
|---|---|
| `credentialsInterceptor` | `withCredentials` en `/api/`: la sesión es una cookie `HttpOnly` |
| `correlationIdInterceptor` | `X-Correlation-Id` aleatorio para encontrar la petición en los logs |
| `idempotencyKeyInterceptor` | Cabecera `Idempotency-Key` desde el `HttpContext` |
| `apiErrorInterceptor` | `HttpErrorResponse` → `ApiError` |

## 13. Carga diferida y secciones

`app.routes.ts` genera una ruta por cada `NAV_ITEMS` de `layout/navigation.ts`; las construidas cargan su `*.routes.ts` con `loadChildren` y el resto muestra `FeaturePlaceholder`. El bundle inicial solo trae el marco.

## 14. Fronteras entre carpetas

`core` → `core`; `shared` → `shared`; `layout` → `layout`, `shared`, `core`; `features/x` → su propia carpeta, `shared` y `core`. Lo comprueba ESLint. Si dos features necesitan lo mismo, súbelo a `shared` (UI sin negocio) o a `core` (estado o servicios sin UI).
