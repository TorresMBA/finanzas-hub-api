# Modelo de seguridad

## J. Modelo de seguridad

### J.1 Tres niveles

| Nivel | Pregunta | Dónde se resuelve | Si falla |
|---|---|---|---|
| Usuario | ¿Está autenticado? | Middleware de ASP.NET con política por defecto "autenticado" | 401 |
| Workspace | ¿Es miembro activo de `{workspaceId}`? ¿Tiene el rol que pide el caso de uso? | Filtro global de la API sobre toda ruta `W`, que llena `IWorkspaceContext`; el handler exige rol con `Require(role)` | 404 si no es miembro; 403 si lo es pero le falta rol |
| Recurso | ¿Es suyo, o participa en él? | Filtro global de EF en `AppDbContext` y políticas de recurso en la capa de aplicación | 404 si no puede verlo; 403 si puede verlo pero no hacer esa acción |

`[Authorize]` solo cubre el primer nivel. El segundo lo aplica un filtro a todos los controllers a la vez, no cada controller por su cuenta; un test comprueba que toda ruta de workspace nombra el parámetro `workspaceId`, que es lo que el filtro lee. El tercero vive en la capa de aplicación y en el contexto de datos, y se ejercita igual desde HTTP, desde un job o desde un comando de chat.

### J.2 Estrategia de aislamiento de datos

| Clase de dato | Columnas | Predicado de acceso | Módulos |
|---|---|---|---|
| Privado | `workspace_id`, `owner_user_id` | `workspace_id = @ws AND owner_user_id = @yo` | Ledger, Categories, Budgets, Scheduling, Integrations, Investments |
| Colaborativo | `workspace_id`, `created_by_user_id` y tabla de participantes | `workspace_id = @ws AND EXISTS (participante activo con user_id = @yo)` | Goals, SharedExpenses |
| Del workspace | `workspace_id` | Membresía activa y rol | Workspaces |
| Personal sin workspace | `user_id` | `user_id = @yo` | Identity (perfil, preferencias), Notifications |
| Referencia compartida | — | Cualquier autenticado, solo lectura | Monedas, tipos de cambio, catálogo de categorías por defecto |

Reglas:

1. Toda entidad privada implementa `IOwned` y `AppDbContext` le aplica un filtro global ligado al `ICurrentUser` e `IWorkspaceContext` de la petición. Olvidar un `WHERE` no filtra datos ajenos, porque el filtro ya está.
2. Las filas hijas de un agregado heredan el acceso de su raíz. `ledger.entries` repite `workspace_id` y `owner_user_id` porque las sumas de saldo consultan esa tabla directamente.
3. Un test recorre el modelo EF y falla si una entidad `IOwned` o colaborativa no tiene filtro.
4. `IgnoreQueryFilters()` solo se permite en código de sistema enumerado en una lista blanca que el test de arquitectura comprueba.
5. Los contratos entre módulos no aceptan un `ownerUserId` arbitrario: operan sobre el actor del contexto. Un módulo no puede pedir a Ledger los movimientos de otro usuario.
6. **Ningún rol concede acceso a datos privados ajenos.** Tampoco Owner. No hay puerta de soporte dentro de la aplicación.

### J.3 Contexto de ejecución

| Abstracción | Contenido | Origen en HTTP | Origen en un job |
|---|---|---|---|
| `ICurrentUser` | `UserId`, tipo de actor | Claims de la cookie | `IActorContextInitializer.ActAs(userId, ...)` con el dueño del recurso que procesa |
| `IWorkspaceContext` | `WorkspaceId`, rol, moneda base, zona horaria | Ruta `{workspaceId}` y consulta de membresía, una vez por petición | `IActorContextInitializer.EnterWorkspace(...)` con la membresía del dueño, tras comprobar que sigue activa |

Un job nunca corre sin actor. El scheduler registra movimientos "como" el dueño de la regla; si ese usuario ya no es miembro, la regla se pausa y se le avisa (misma decisión que el ADR-0014 de V1).

### J.4 Roles del workspace

| Rol | Puede | No puede |
|---|---|---|
| Owner (uno) | Lo de Admin; cambiar moneda base; transferir propiedad; archivar el workspace | Ver datos privados de otros |
| Admin | Invitar y expulsar; cambiar roles salvo el de Owner y sin conceder Admin; renombrar; cambiar zona horaria | Ver datos privados de otros |
| Member | Usar sus finanzas privadas; crear recursos compartidos y participar en ellos | Administrar miembros |

Correspondencia con el mock: Administrador → Admin, Editor → Member, Solo lectura se elimina (OQ-02).

### J.5 Políticas de recurso

| Política | Regla | Uso |
|---|---|---|
| `WorkspaceMember` | Membresía activa | Todo lo que cuelga de `W` |
| `WorkspaceAdmin`, `WorkspaceOwner` | Rol mínimo | Miembros, invitaciones, ajustes |
| `ResourceOwner` | `owner_user_id = yo` | Todo lo privado; se cumple por el filtro global |
| `GoalParticipant` | Participante activo de la meta | Ver, aportar, retirar lo propio, salir |
| `GoalOwner` | Participante con rol Owner | Editar, invitar, archivar, cancelar |
| `SharedGroupMember` | Miembro activo del grupo | Ver grupo, gastos y balances; crear gastos |
| `SharedGroupAdmin` | Miembro con rol Admin | Gestionar miembros; archivar |
| `SharedExpenseEditor` | Creador o pagador del gasto | Editar, publicar, cancelar |
| `SettlementParty` | Deudor o acreedor de la liquidación | Registrar, vincular cuenta |

Las dos primeras filas las aplica el filtro de rutas y `Require(role)`. `ResourceOwner`, `GoalParticipant` y `SharedGroupMember` son estructurales (filtro de consulta). Las demás son métodos del agregado que devuelven `Result` con error `Forbidden`.

**Ejemplo del encargo.** Workspace con Brian, Juan, Pedro y Ana; meta "Viaje Japón" con participantes Brian y Juan. `GET W/goals` devuelve la meta a Brian y a Juan y no la incluye para Pedro ni Ana. `GET W/goals/{id}` responde 404 a Pedro y Ana. Brian ve que Juan aportó S/ 500 el día X; no ve desde qué cuenta.

### J.6 Autenticación

ASP.NET Core Identity dentro del módulo `Identity`, con cookie de sesión HttpOnly, Secure y SameSite=Lax (OQ-14). La cookie lleva solo el id del usuario: workspace y rol se resuelven en cada petición. La SPA y la API se sirven bajo el mismo sitio; en local, el proxy de Angular. SignalR usa la misma cookie. Los endpoints de registro son propios (no `MapIdentityApi`) porque el registro crea el workspace y puede aceptar una invitación. Los webhooks son anónimos y se autentican por firma.

Las claves de Data Protection se guardan en la base (`platform.data_protection_keys`), para que un redespliegue no cierre las sesiones ni invalide los enlaces ya enviados por correo.

**CSRF.** `SameSite=Lax` ya deja la cookie fuera de un POST hecho desde otro sitio. Además, la API rechaza con 403 (`request.cross_site`) toda petición que cambia estado cuyo `Origin` no sea su propia dirección o un frontend de `Cors:AllowedOrigins`. No se usan tokens antifalsificación.

**Contraseñas y bloqueo.** Las de ASP.NET Core Identity: mínimo 12 caracteres con minúscula, dígito y símbolo; cinco intentos fallidos bloquean la cuenta 15 minutos. El login responde lo mismo para un correo desconocido y para una contraseña errónea, y pedir la recuperación responde lo mismo exista o no la cuenta.

**Enlaces por correo.** Confirmación y recuperación usan los tokens de Identity. La invitación usa un token aleatorio de 256 bits del que solo se guarda el hash.

### J.7 Otras medidas

- CORS con lista explícita de orígenes y fallo cerrado en producción (se reutiliza la validación de `Program.cs` de V1).
- Límite de peticiones en `/auth` (login, registro, recuperación): 20 por minuto y dirección, configurable con `RateLimiting:AuthPermitsPerMinute`. Los webhooks tendrán el suyo en la fase 11.
- El comando por WhatsApp solo actúa para un teléfono verificado con código, vinculado a un usuario. En V1 el teléfono se vinculaba al workspace (`WorkspaceWhatsAppRecipient`), no a un usuario.
- No se registran en logs contraseñas, tokens, correos, teléfonos ni cuerpos de archivos.
- Secretos fuera del repositorio: `dotnet user-secrets` en local, Key Vault en Azure.
