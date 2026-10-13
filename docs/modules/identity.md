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
