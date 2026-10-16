# Recetas del frontend

Pasos concretos para los cambios más habituales. Las rutas son relativas a `finanzas-hub-web/src/app/`; `things` es el nombre de ejemplo.

## 1. Una sección nueva

1. **Navegación.** Si la sección no existe, añádela a `NAV_GROUPS` en `layout/navigation.ts` (`id`, `path`, `label`, `shortLabel`, `icon` de Material Symbols, `description`, `phase`). Aparece en la barra lateral, el riel y la barra móvil, y muestra el placeholder.
2. **Carpeta** `features/things/` con:
   - `things.routes.ts`: `export const THINGS_ROUTES: Routes = [{ path: '', title: 'Cosas · FinanzasHub', loadComponent: () => import('./things').then((m) => m.Things) }];`
   - `things-api.ts`: el servicio de la feature (§2).
   - `things.ts`, `.html`, `.scss`: la página.
   - `things.spec.ts`: el test ([testing.md](testing.md)).
3. **Ruta.** Añade la entrada al mapa `BUILT` de `app.routes.ts`:
   ```ts
   things: { loadChildren: () => import('./features/things/things.routes').then((m) => m.THINGS_ROUTES) },
   ```
4. **Página** con los cuatro estados:
   ```ts
   @Component({
     selector: 'app-things',
     imports: [PageHeader, Skeleton, EmptyState, ErrorState],
     templateUrl: './things.html',
     styleUrl: './things.scss',
     changeDetection: ChangeDetectionStrategy.OnPush,
   })
   export class Things {
     private readonly api = inject(ThingsApi);
     protected readonly things = httpResource<Thing[]>(() => this.api.listUrl());
   }
   ```
   ```html
   <app-page-header title="Cosas" />
   @if (!things.hasValue() && things.isLoading()) {
     <app-skeleton height="76px" />
   } @else if (things.error()) {
     <app-error-state title="No se pudieron cargar tus cosas" (retry)="things.reload()" />
   } @else if (things.value()!.length === 0) {
     <app-empty-state icon="inventory_2" title="Todavía no tienes cosas" />
   } @else {
     @for (thing of things.value(); track thing.id) { … }
   }
   ```
5. Revisa los tres breakpoints y los dos temas.

## 2. Un endpoint nuevo de la API

1. Con la API en marcha y el cambio hecho en el backend: `tools/update-openapi.ps1` (en `finanzas-hub-api`).
2. `npm run api:generate` (en `finanzas-hub-web`).
3. Alias en `core/api/api-types.ts`: `export type Thing = Schemas['ThingResponse'];`
4. En `things-api.ts`:
   ```ts
   @Injectable({ providedIn: 'root' })
   export class ThingsApi {
     private readonly http = inject(HttpClient);
     private readonly workspaces = inject(WorkspaceStore);
     private readonly base = computed(() => `/api/v1/workspaces/${this.workspaces.current()?.id}`);

     /** Undefined until a workspace is selected, which is how a resource knows to wait. */
     listUrl(): string | undefined {
       return this.workspaces.current() ? `${this.base()}/things` : undefined;
     }

     rename(thingId: string, request: RenameThingRequest): Promise<Thing> {
       return firstValueFrom(this.http.patch<Thing>(`${this.base()}/things/${thingId}`, request));
     }
   }
   ```
5. Si la API añade códigos de error nuevos, tradúcelos en `core/errors/error-messages.ts`.

## 3. Un formulario (y si mueve dinero)

```ts
@Component({
  selector: 'app-thing-form',
  imports: [Dialog, Field, FormField],
  template: `
    <app-dialog [title]="'Nueva cosa'" (closed)="closed.emit()">
      <form class="form-stack" (submit)="$event.preventDefault(); save()" novalidate>
        @if (failure(); as text) { <div class="alert" role="alert">{{ text }}</div> }
        <app-field label="Monto" [state]="thingForm.amount()">
          <input class="input" type="text" inputmode="decimal" [formField]="thingForm.amount" />
        </app-field>
        <div class="form-actions">
          <button class="button" type="button" (click)="closed.emit()">Cancelar</button>
          <button class="button button--primary" type="submit" [disabled]="busy()">Guardar</button>
        </div>
      </form>
    </app-dialog>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ThingForm {
  private readonly api = inject(ThingsApi);
  private readonly idempotencyKey = newIdempotencyKey();   // solo si mueve dinero: una por formulario abierto

  readonly saved = output<string>();
  readonly closed = output<void>();

  protected readonly model = signal({ amount: '' });
  protected readonly thingForm = form(this.model, (path) => {
    validate(path.amount, ({ value }) => ((parseAmount(value()) ?? 0) > 0 ? undefined : { kind: 'amount', message: 'Un monto mayor que cero.' }));
  });
  protected readonly failure = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected save(): void {
    void submit(this.thingForm, async () => {
      this.busy.set(true);
      this.failure.set(null);
      try {
        await this.api.pay({ amount: parseAmount(this.model().amount) ?? 0 }, this.idempotencyKey);
        this.saved.emit('Guardado.');
      } catch (error) {
        this.failure.set(errorMessage(error));
      } finally {
        this.busy.set(false);
      }
    });
  }
}
```

En el servicio, la clave viaja en el contexto: `this.http.post<X>(url, body, { context: new HttpContext().set(IDEMPOTENCY_KEY, key) })` (ver `withKey` en `features/accounts/accounts-api.ts`). La página abre el formulario con un signal (`formTarget`), escucha `(saved)` para mostrar el aviso y recargar sus recursos, y `(closed)` para cerrarlo.

## 4. Un componente en `shared/ui`

1. Comprueba en [ui-design-system.md](ui-design-system.md) que no existe ya.
2. `shared/ui/thing-chip.ts`: standalone, `OnPush`, selector `app-thing-chip`, solo `input`/`output`, sin servicios de features ni llamadas a la API.
3. Estilos con tokens y mixins; plantilla y estilos en línea si es pequeño.
4. Una fila en la tabla de [ui-design-system.md](ui-design-system.md).

## 5. Reaccionar a un cambio en tiempo real

Si el backend ya notifica un recurso (`IRealtimeNotifier` con `resource` e `id`), la página lo escucha:

```ts
private readonly center = inject(NotificationCenter);

constructor() {
  effect(() => {
    if (this.center.resourceChanged()?.resource === 'thing') {
      untracked(() => this.things.reload());
    }
  });
}
```

Ejemplos: `features/goals/goals.ts` (`'goal'`) y `features/shared/shared.ts` (`'shared-group'`).

## 6. Un estado global nuevo

Solo si de verdad lo comparten varias features (si no, es un signal de la página). Servicio en `core/<tema>/<tema>-store.ts`, `providedIn: 'root'`, signals privados y lectura con `asReadonly()` o `computed`; nada de UI. Añádelo a `provideSession` si los tests lo necesitan.
