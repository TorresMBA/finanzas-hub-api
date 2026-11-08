# Entorno local

## Requisitos

| Herramienta | Versión | Para qué |
|---|---|---|
| .NET SDK | 10 | API y tests |
| Node.js | 24 | Frontend |
| PostgreSQL | 17 o superior | Con Docker (`docker compose`) o instalado en la máquina |
| Docker | opcional | PostgreSQL, Mailpit, Aspire Dashboard y Testcontainers |

## Primera vez

```powershell
# 1. Infraestructura local (omitir si se usa un PostgreSQL ya instalado)
cd finanzas-hub-api
Copy-Item .env.example .env        # y poner una contraseña local en POSTGRES_PASSWORD
docker compose up -d

# 2. Cadena de conexión, fuera del repositorio
dotnet user-secrets set "ConnectionStrings:PersonalFinance" `
  "Host=localhost;Port=5432;Database=personal_hub;Username=personal_hub;Password=<la de .env>" `
  --project src/PersonalFinance.Api

# 3. API en http://localhost:5166 (en Development aplica las migraciones al arrancar)
dotnet run --project src/PersonalFinance.Api

# 4. Frontend en http://localhost:4200, con proxy hacia la API
cd ../finanzas-hub-web
npm ci
npm start

# 5. Worker, en otra terminal. Solo hace falta cuando trabajas en un job
cd ../finanzas-hub-api
dotnet run --project src/PersonalFinance.Worker
```

Si el puerto 5432 ya lo ocupa un PostgreSQL instalado, cambia `POSTGRES_PORT` en `.env` y el puerto de la cadena de conexión.

La base de datos se crea sola en el primer arranque. En ese arranque aparecen líneas `fail:` que son esperadas y no se repiten: dos de conexión, mientras EF Core comprueba que la base aún no existe, y una `Failed executing DbCommand`, cuando consulta un historial de migraciones que todavía no ha creado.

## Direcciones

| Qué | Dónde |
|---|---|
| Aplicación | http://localhost:4200 |
| API | http://localhost:5166 |
| Estado de la API | http://localhost:5166/health/ready |
| Contrato OpenAPI | http://localhost:5166/openapi/v1.json |
| Correos enviados (Mailpit) | http://localhost:8025 |
| Trazas, métricas y logs (Aspire Dashboard) | http://localhost:18888 |

## Configuración

Los secretos no van en `appsettings`: `dotnet user-secrets` en local, variables de entorno o Key Vault en despliegue.

| Clave | Valor por defecto | Efecto |
|---|---|---|
| `ConnectionStrings:PersonalFinance` | — | Obligatoria. Sin ella la API falla al usar la base de datos, y en Production no arranca |
| `Database:MigrateOnStartup` | `false`; `true` en Development | Aplica las migraciones pendientes al arrancar |
| `Cors:AllowedOrigins` | `http://localhost:4200` en Development | Orígenes del navegador permitidos. En Production es obligatoria |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4317` en el perfil de arranque | Destino de la telemetría. Sin ella no se exporta nada |
| `Frontend:BaseUrl` | `http://localhost:4200` | Con qué empiezan los enlaces de los correos |
| `Email:SmtpHost`, `Email:SmtpPort`, `Email:SmtpEnableSsl`, `Email:SmtpUser`, `Email:SmtpPassword`, `Email:From` | Sin servidor | Servidor de correo. Sin `SmtpHost` los correos se escriben como archivos `.eml`. Ver [Correo real](#correo-real-smtp) |
| `Email:PickupDirectory` | `personal-hub-mail` en la carpeta temporal del sistema | Dónde se escriben esos archivos |
| `RateLimiting:AuthPermitsPerMinute` | 20; 300 en Development | Peticiones a `/auth` por minuto y dirección |

## Qué hace hoy la aplicación

Tras la fase 1 se puede crear una cuenta, iniciar sesión, configurar el espacio personal, invitar a otras personas y gestionar miembros y roles, además de editar el perfil y las preferencias. El resto de secciones muestra todavía una página provisional.

Los correos (confirmación, recuperación de contraseña, invitaciones) los envía el Worker. En local, sin servidor SMTP, aparecen como archivos `.eml` en `%TEMP%\personal-hub-mail`: ábrelos para copiar el enlace.

## Correo real (SMTP)

Para que la recuperación de contraseña, la confirmación de correo y las invitaciones lleguen a la bandeja de cada persona, el Worker necesita un servidor SMTP. Sin él no falla nada: los correos se quedan en archivos `.eml`.

| Clave | Quién la usa | Valor | Notas |
|---|---|---|---|
| `Email:SmtpHost` | Worker | `smtp.gmail.com`, `smtp-relay.brevo.com`, `smtp.resend.com`… | Basta con ponerla para dejar de escribir `.eml` |
| `Email:SmtpPort` | Worker | `587` (por defecto) | Solo STARTTLS. El puerto 465 (TLS implícito) no funciona: `System.Net.Mail` no lo admite |
| `Email:SmtpEnableSsl` | Worker | `true` (por defecto) | Déjalo en `true` con cualquier proveedor real |
| `Email:SmtpUser` | Worker | La cuenta o el usuario SMTP del proveedor | En Gmail, la dirección completa |
| `Email:SmtpPassword` | Worker | Contraseña de aplicación o clave SMTP | Secreto: solo en `user-secrets` o en variables de entorno. En Gmail no sirve la contraseña normal: hace falta la verificación en dos pasos y una [contraseña de aplicación](https://myaccount.google.com/apppasswords) |
| `Email:From` | Worker | `FinanzasHub <cuenta@dominio>` | El remitente: con nombre, la dirección va entre `< >`; sin nombre, basta la dirección. Sin el `>` final, o con el nombre y la dirección sin `< >`, el Worker no envía nada. En PowerShell escríbelo entre comillas: sin ellas, o en `cmd`, `<` y `>` se toman como redirecciones y se pierden. Gmail lo reemplaza por la cuenta si no es ella o un alias suyo; Brevo y Resend exigen un remitente o un dominio verificado |
| `Frontend:BaseUrl` | API | `http://localhost:4200` en local; la URL pública en despliegue | La API arma con ella los enlaces de los correos. Si está mal, el enlace del correo no abre la aplicación |

La API y el Worker comparten los secretos (`UserSecretsId` `personal-finance-api`), así que se configuran una sola vez. Desde `finanzas-hub-api`, con Gmail:

```powershell
dotnet user-secrets set "Email:SmtpHost" "smtp.gmail.com" --project src/PersonalFinance.Worker
dotnet user-secrets set "Email:SmtpPort" "587" --project src/PersonalFinance.Worker
dotnet user-secrets set "Email:SmtpUser" "tu-cuenta@gmail.com" --project src/PersonalFinance.Worker
dotnet user-secrets set "Email:SmtpPassword" "xxxx xxxx xxxx xxxx" --project src/PersonalFinance.Worker
dotnet user-secrets set "Email:From" "FinanzasHub <tu-cuenta@gmail.com>" --project src/PersonalFinance.Worker
dotnet user-secrets list --project src/PersonalFinance.Worker   # comprobar; muestra la contraseña, no lo compartas
```

Para volver a los archivos `.eml`: `dotnet user-secrets remove "Email:SmtpHost" --project src/PersonalFinance.Worker`.

En despliegue van como variables de entorno, con `__` en lugar de `:` (`Email__SmtpHost`, `Email__SmtpPassword`, `Frontend__BaseUrl`…).

Para comprobarlo, con la API y el Worker en marcha, pide en `/forgot-password` la recuperación de una cuenta tuya. El correo sale en el siguiente ciclo del Worker. Si no llega, el Worker registra `Email … could not be sent` con el motivo y reintenta hasta 8 veces. Una contraseña de aplicación mal copiada responde `5.7.8 Username and Password not accepted`.

## Tareas habituales

| Tarea | Comando |
|---|---|
| Tests del backend | `dotnet test finanzas-hub-api/PersonalFinance.slnx` (ver [testing.md](../backend/testing.md)) |
| Crear una migración | `finanzas-hub-api/tools/add-migration.ps1 -Name AddLedgerAccounts` |
| Aplicar migraciones | `finanzas-hub-api/tools/migrate.ps1` |
| Actualizar el contrato tras cambiar la API | `finanzas-hub-api/tools/update-openapi.ps1` y luego `npm run api:generate` en `finanzas-hub-web` |
| Tests, lint y E2E del frontend | `npm test`, `npm run lint`, `npm run e2e` |

## Worker

`PersonalFinance.Worker` ejecuta los jobs: lo que corre sin una petición (limpieza, y más adelante envíos, pagos programados e importaciones). Es la misma aplicación que la API, sin HTTP, y usa la misma cadena de conexión de `user-secrets`.

- La API no ejecuta ningún job. Si el Worker no está en marcha, lo encolado espera.
- El Worker no aplica migraciones. Si arranca antes que la API en una base vacía, sus jobs fallan, lo registran y reintentan al minuto.
- Sus jobs de hoy son el envío de correos y la purga diaria de claves de idempotencia. Hace falta tenerlo en marcha para recibir los correos de confirmación, recuperación e invitación, y para los tests E2E.
