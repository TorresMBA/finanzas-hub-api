# Configuración y variables de entorno

De dónde lee cada parte del sistema su configuración, todas las claves que existen y cómo se añade una. Qué valores son secretos y cómo se protegen está en [secrets.md](secrets.md).

## Backend: orden de las fuentes

ASP.NET Core combina estas fuentes; **la última gana**:

1. `appsettings.json`: valores por defecto, versionado, **sin secretos**.
2. `appsettings.{Entorno}.json`: hoy solo existe `appsettings.Development.json` (migraciones al arrancar, límite de login alto, códigos de WhatsApp en pantalla).
3. **User secrets**: solo en Development, fuera del repositorio. Aquí va tu cadena de conexión local.
4. **Variables de entorno**: en servidores y contenedores. El `:` de la clave se escribe `__` (doble guion bajo): `ConnectionStrings__PersonalFinance`, `Email__SmtpHost`, `Cors__AllowedOrigins__0` (el índice de un arreglo también va con `__`).
5. Argumentos de línea de comandos (`dotnet PersonalFinance.Api.dll migrate`).

El entorno lo fija `ASPNETCORE_ENVIRONMENT` (API) o `DOTNET_ENVIRONMENT` (Worker). En local viene de `Properties/launchSettings.json` como `Development`; en un contenedor, si no se fija, es `Production`.

La API y el Worker comparten el `UserSecretsId` (`personal-finance-api`) y llaman los dos a `AddInfrastructure`, así que leen las mismas claves; cada uno usa las que necesita.

```powershell
# una vez, desde finanzas-hub-api
dotnet user-secrets set "ConnectionStrings:PersonalFinance" `
  "Host=localhost;Port=5432;Database=personal_hub;Username=personal_hub;Password=<la de .env>" `
  --project src/PersonalFinance.Api
dotnet user-secrets list --project src/PersonalFinance.Api     # muestra los valores: cuidado al compartir pantalla
```

## Claves del backend

| Clave | Por defecto | La usa | Efecto |
|---|---|---|---|
| `ConnectionStrings:PersonalFinance` | — | API, Worker | **Obligatoria.** En Production la API no arranca sin ella |
| `Database:MigrateOnStartup` | `false`; `true` en Development | API | Aplica las migraciones al arrancar |
| `Cors:AllowedOrigins` | `[]`; `http://localhost:4200` en Development | API | Orígenes del navegador permitidos (CORS y protección de origen). En Production exige al menos uno HTTPS o no arranca |
| `RateLimiting:AuthPermitsPerMinute` | 20; 300 en Development | API | Peticiones a `/auth` por minuto y dirección IP |
| `RateLimiting:WebhookPermitsPerMinute` | 300 | API | Límite del webhook de WhatsApp |
| `Frontend:BaseUrl` | `http://localhost:4200` | API, Worker | Con qué empiezan los enlaces de los correos |
| `Email:From` | `PersonalHub <no-reply@personalhub.local>` | Worker | Remitente |
| `Email:SmtpHost`, `SmtpPort` (587), `SmtpUser`, `SmtpPassword`, `SmtpEnableSsl` (true) | Sin servidor | Worker | Sin `SmtpHost` los correos se escriben como `.eml` |
| `Email:PickupDirectory` | `personal-hub-mail` en la carpeta temporal | Worker | Dónde se escriben esos `.eml` |
| `WhatsApp:VerifyToken`, `WhatsApp:AppSecret` | vacíos | API | Verificación y firma del webhook |
| `WhatsApp:AccessToken`, `WhatsApp:PhoneNumberId` | vacíos | Worker | Envío por la Cloud API. Sin ellos WhatsApp queda desconectado |
| `WhatsApp:NoticeTemplate`, `CodeTemplate`, `TemplateLanguage` (`es`) | vacíos | Worker | Plantillas aprobadas en Meta |
| `WhatsApp:GraphApiVersion` | `v21.0` | Worker | Versión de la Graph API |
| `WhatsApp:OutboxDirectory` | `personal-hub-whatsapp` en la carpeta temporal | Worker | Dónde se escriben los mensajes cuando no hay token |
| `WhatsApp:ShowCodesWithoutAccount` | `true` en Development | API | Muestra el código de verificación en pantalla. **Nunca `true` fuera de Development** |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4317` en `launchSettings` | API, Worker | Destino de la telemetría; sin ella no se exporta nada |
| `Logging:LogLevel:*` | `Information`; EF Core, Npgsql y ASP.NET Core en `Warning` | API, Worker | Verbosidad de los logs |
| `ASPNETCORE_HTTP_PORTS` | `8080` en la imagen Docker | API | Puerto HTTP del contenedor |
| `APP_DLL` | Se deduce del `*.runtimeconfig.json` | Imagen Docker | Qué ensamblado arranca |

## Ficheros `.env`

| Fichero | Versionado | Lo lee | Contenido |
|---|---|---|---|
| `finanzas-hub-api/.env.example` | Sí | Nadie; es la plantilla | `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_PORT` |
| `finanzas-hub-api/.env` | **No** (`.gitignore`) | `docker compose` | Lo mismo, con tu contraseña local |
| `/srv/mercury/apps/<env>/finanzas-hub-api.env` | No; vive en el servidor | Contenedor de la API | `ConnectionStrings__PersonalFinance`, `Cors__AllowedOrigins__0`, `Frontend__BaseUrl`, `WhatsApp__VerifyToken`, `WhatsApp__AppSecret`… |
| `/srv/mercury/apps/<env>/finanzas-hub-worker.env` | No | Contenedor del Worker | La cadena de conexión, `Frontend__BaseUrl`, `Email__*`, `WhatsApp__*` |
| `/srv/mercury/apps/<env>/finanzas-hub-web.env` | No | nginx del frontend | `API_UPSTREAM` (host:puerto de la API) |

La aplicación **no** lee ficheros `.env`: el local es solo para `docker compose`, y los del servidor los pasa el despliegue al contenedor como variables de entorno.

## Frontend

No hay ficheros `environment.*.ts`. El frontend siempre llama con rutas relativas (`/api/...`, `/hubs/...`), y quien sabe dónde está la API es:

| Entorno | Quién | Configuración |
|---|---|---|
| Local | Servidor de desarrollo de Angular | `finanzas-hub-web/proxy.conf.json` → `http://localhost:5166` |
| Contenedor | nginx de la imagen web | `API_UPSTREAM` |

No guardes URLs, claves ni banderas de entorno en el código del frontend.

## Tests y herramientas

| Variable | Efecto |
|---|---|
| `PERSONALFINANCE_TEST_POSTGRES` | Cadena de conexión a un PostgreSQL existente para los tests de integración; sin ella se usa Testcontainers (necesita Docker). Sin Docker, levanta un clúster desechable (`initdb` y `postgres -p 55432`) y apunta la variable a él; no uses tu base de desarrollo |
| `PW_CHANNEL` | `chrome` o `msedge`: Playwright usa el navegador instalado |
| `E2E_MAIL_DIR` | Carpeta de los `.eml` que leen los E2E; por defecto `%TEMP%\personal-hub-mail` |
| `CI` | En Playwright: prohíbe `test.only`, reintenta una vez y no reutiliza el servidor |
| `Database__MigrateOnStartup=false` | Útil para arrancar la API en Development sin tocar tu base |

## Añadir una clave

1. Propiedad en una clase `XOptions` con `public const string SectionName = "X";`, en `PersonalFinance.Infrastructure`.
2. `services.Configure<XOptions>(configuration.GetSection(XOptions.SectionName));` en `DependencyInjection.cs`; inyéctala como `IOptions<XOptions>`.
3. El valor no secreto por defecto en `appsettings.json` y, si cambia en local, en `appsettings.Development.json`. Los secretos, nunca en `appsettings`.
4. Si es imprescindible en Production, que la aplicación falle al arrancar con un mensaje claro (como la cadena de conexión o `Cors:AllowedOrigins`).
5. Una fila en la tabla de este documento, en [secrets.md](secrets.md) si es secreta, y en los `.env` del servidor de cada ambiente.
