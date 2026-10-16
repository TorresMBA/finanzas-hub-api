# Bugs frecuentes en el frontend

Síntoma, causa y cómo evitarlo. Repásalo antes de abrir un pull request.

| # | Síntoma | Causa | Cómo evitarlo |
|---|---|---|---|
| 1 | La pantalla no se actualiza tras cambiar un valor | Sin zonas, una propiedad normal no dispara la detección de cambios | Guarda en un `signal` todo lo que se pinta |
| 2 | Petición a `/api/v1/workspaces/undefined/...` | La URL se construyó antes de tener workspace | Devuelve `undefined` desde la función del `httpResource` mientras falte el dato |
| 3 | `NG0203: inject() must be called from an injection context` en un guard | `inject()` después de un `await` | Todos los `inject()` antes del primer `await` |
| 4 | Un guard decide con datos a medio cargar | Se confió en que otro guard de la misma ruta había corrido antes (corren en paralelo) | Un guard por ruta y que cargue todo lo que necesita |
| 5 | Gasto duplicado al hacer doble clic | Clave de idempotencia creada al enviar, o botón sin `[disabled]="busy()"` | Clave al construir el formulario; desactiva el botón mientras se envía |
| 6 | Un movimiento aparece el día anterior | Convertir `2026-10-15` a `Date` en una zona horaria negativa | Trata las fechas contables como texto ISO; usa los helpers de `shared/ui/dates.ts` |
| 7 | Sumas con decimales raros (`0.30000000000000004`) o concatenadas (`"10" + "5"`) | Importes que llegan como `string` o sumas en coma flotante | `Number()` solo para mostrar; suma con `sumAmounts`; los totales vienen de la API |
| 8 | Mensaje de error en inglés o genérico | `code` nuevo sin entrada en `error-messages.ts` | Añade la traducción con el `code` exacto |
| 9 | `npm run lint` falla con `boundaries/dependencies` | Una feature importa algo de otra | Sube lo compartido a `shared` (UI) o `core` (estado/servicios) |
| 10 | El frontend no compila tras un cambio de API, o un campo es `undefined` | Tipos desactualizados | `npm run api:generate` después de `tools/update-openapi.ps1` |
| 11 | La pantalla parpadea a esqueleto en cada recarga | Mostrar el esqueleto con `isLoading()` aunque ya hay datos | `!resource.hasValue() && resource.isLoading()` |
| 12 | Datos de otro workspace se ven un instante tras cambiar | Un signal local con datos copiados del recurso | Deriva con `computed` del recurso; no copies |
| 13 | Una lista se repinta entera o pierde el foco | `@for` con `track $index` en una lista que cambia | `track item.id` |
| 14 | Un `effect` entra en bucle | El efecto escribe un signal que él mismo lee | Usa `computed`; si de verdad hay que escribir, `untracked` |
| 15 | Fuga de memoria o peticiones tras salir de la página | `setInterval`, `subscribe` o listeners sin limpiar | Limpia en `DestroyRef.onDestroy`; prefiere recursos y signals |
| 16 | Se rompe en móvil o en tema claro | Solo se probó en escritorio oscuro | Revisa los tres breakpoints y los dos temas; usa tokens |
| 17 | La sesión «se pierde» al recargar en local | Llamada a la API sin pasar por el proxy (URL absoluta a `:5166`) | Rutas relativas `/api/...` siempre |
| 18 | Test que pasa solo a veces | Depende de la fecha real o de peticiones sin responder | Fija `TODAY` y termina con `http.verify()` |
| 19 | El aviso en tiempo real llega pero la página no cambia | La página no reacciona a `resourceChanged` | Lee `NotificationCenter.resourceChanged()` en un `effect` y recarga el recurso |

## Lista de comprobación antes de un pull request

- [ ] Carga, vacío, error con reintento y datos.
- [ ] Tres breakpoints y dos temas; solo tokens.
- [ ] Tipos generados al día y sin tipos de API escritos a mano.
- [ ] Clave de idempotencia en los formularios que mueven dinero.
- [ ] Errores nuevos traducidos en `error-messages.ts`.
- [ ] `npm run lint`, `npm test -- --watch=false` y `npm run build` en verde.
