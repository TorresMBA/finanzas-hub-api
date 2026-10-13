# Módulo Identity

| | |
|---|---|
| Responsabilidad | Ciclo de vida del usuario, credenciales, sesión, perfil y preferencias personales |
| Aggregate roots | `User` (sobre ASP.NET Core Identity), `UserPreferences` |
| Value objects | `Email`, `Locale`, `DateFormat`, `WeekStart` |
| Posee | Esquema `identity`: usuarios, tokens de Identity, preferencias |
| Consulta de otros | Ninguno |
| Expone | `IUserDirectory` (nombre y avatar por id; búsqueda por correo) e `IPostRegistrationStep`, interfaz que Workspaces implementa para crear el workspace personal y aceptar la invitación dentro de la transacción del registro |
| Eventos | `UserRegistered` (I), `UserProfileChanged` (I), `PasswordResetRequested` (D) |

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| RegisterUser | Anónimo | correo, contraseña, nombre, token de invitación opcional | Correo único; contraseña ≥ 12 con dígito y símbolo | Crea usuario; crea workspace personal en la misma transacción; acepta la invitación si viene; correo de confirmación; `UserRegistered` |
| Login / Logout | Anónimo / autenticado | credenciales | Bloqueo tras intentos fallidos | Emite o borra cookie de sesión |
| ConfirmEmail, RequestPasswordReset, ResetPassword, ChangePassword | Anónimo con token / autenticado | token, nueva contraseña | Token de un solo uso con caducidad | Auditoría de seguridad |
| UpdateProfile | Propio | nombre, celular, avatar | — | `UserProfileChanged` |
| UpdatePreferences | Propio | locale, formato de fecha, inicio de semana, tema, ocultar saldos, workspace por defecto, meta de ahorro | — | — |
| GetMe (query) | Propio | — | — | Perfil, preferencias y lista de workspaces con rol |

Se entra con correo y contraseña. El inicio de sesión con Google o Microsoft está diferido (OQ-25); la tabla `user_logins` de ASP.NET Core Identity ya lo permite sin cambiar el modelo.

No se escribe autenticación propia: el hash de contraseñas, el bloqueo por intentos y los tokens de confirmación y de recuperación son los de ASP.NET Core Identity, con su almacenamiento estándar sobre `AppDbContext`.

## Estado tras la fase 1

Implementado: todos los casos de uso de la tabla salvo la foto de perfil. Endpoints: `POST /auth/register`, `/auth/login`, `/auth/logout`, `/auth/confirm-email`, `/auth/forgot-password`, `/auth/reset-password`, `/auth/change-password`; `GET /me`; `PATCH /me/profile`; `PUT /me/preferences`.

- `User` hereda de `IdentityUser<Guid>` y vive en el dominio del módulo; los handlers usan `UserManager` directamente. Es la única excepción a la regla de que un módulo no usa ASP.NET.
- El controller crea la cookie de sesión; el módulo solo dice de quién es.
- Un login fallido es un comando que termina bien y devuelve el motivo: si devolviera un error, la transacción se revertiría y con ella el contador de intentos que lleva al bloqueo.
- Los eventos `UserRegistered` y `UserProfileChanged` no se levantan todavía: nadie reacciona a ellos hasta Notifications (fase 6).
- `GET /me` no incluye los workspaces; se piden con `GET /me/workspaces`, que es de Workspaces.
