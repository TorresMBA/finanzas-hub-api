# Secretos y configuración sensible

Qué valores son secretos, quién los usa (API, Worker, frontend, CI), dónde se guardan en cada entorno y cómo se cambian. La mecánica general de la configuración (orden de las fuentes, todas las claves) está en [onboarding.md §8](../getting-started/onboarding.md#8-configuración-y-variables-de-entorno).

## Reglas

1. **Ningún secreto en el repositorio.** Ni en `appsettings*.json`, ni en `launchSettings.json`, ni en `docker-compose.yml`, ni en el código del frontend, ni en un commit «temporal».
2. **En local, `dotnet user-secrets`**; en los servidores, **variables de entorno** en los ficheros `.env` de cada app, fuera del repositorio.
3. **El frontend no tiene secretos.** Todo lo que viaja en el bundle de Angular lo puede leer cualquiera.
4. **No se registran en logs.** No imprimas cadenas de conexión, tokens ni cabeceras de autenticación, tampoco en un `LogDebug`.
5. **Un secreto que llegó a git está comprometido**: se rota. Borrarlo del fichero o del historial no basta, porque ya puede estar en clones, forks o cachés.

## Inventario

### Backend: API y Worker

Los dos hosts llaman a `AddInfrastructure`, así que leen las mismas claves; la tabla dice cuál las necesita de verdad.

| Clave (variable de entorno) | Secreto | API | Worker | Para qué |
|---|---|---|---|---|
| `ConnectionStrings:PersonalFinance` (`ConnectionStrings__PersonalFinance`) | **Sí**, lleva la contraseña de PostgreSQL | Obligatoria | Obligatoria | Acceso a la base. Sin ella la API no arranca en Production |
| `Email:SmtpUser`, `Email:SmtpPassword` (`Email__SmtpPassword`…) | **Sí** | — | Sí | Autenticarse en el servidor SMTP. Solo el Worker envía (`SendEmailsJob`); la API encola |
| `Email:SmtpHost`, `SmtpPort`, `SmtpEnableSsl`, `From` | No | — | Sí | Servidor y remitente |
| `WhatsApp:AccessToken` | **Sí**, token de la app de Meta | — | Sí | Enviar mensajes por la Graph API (`SendWhatsAppJob`) |
| `WhatsApp:AppSecret` | **Sí** | Sí | — | Verificar la firma HMAC-SHA256 de cada evento del webhook. Sin él se rechazan todos |
| `WhatsApp:VerifyToken` | **Sí** (valor que eliges tú y das a Meta) | Sí | — | Responder a la verificación del webhook |
| `WhatsApp:PhoneNumberId`, `NoticeTemplate`, `CodeTemplate`, `TemplateLanguage`, `GraphApiVersion` | No | — | Sí | Número emisor y plantillas aprobadas |
| `WhatsApp:ShowCodesWithoutAccount` | No, pero **nunca `true` fuera de Development** | Sí | — | Muestra en pantalla el código de verificación cuando no hay cuenta de WhatsApp |
| `Cors:AllowedOrigins`, `Frontend:BaseUrl`, `OTEL_EXPORTER_OTLP_ENDPOINT`, `RateLimiting:*`, `Database:MigrateOnStartup` | No | Sí | Según la clave | Configuración, no secretos |

### Secretos que viven en la base

| Qué | Dónde | Notas |
|---|---|---|
| Contraseñas de usuarios | `identity.users.password_hash` | Hash de ASP.NET Core Identity (PBKDF2 con sal). Nunca se guardan ni se registran en claro |
| Claves de Data Protection | `platform.data_protection_keys` | Firman la cookie `ph.session` y los tokens de los correos (confirmación, recuperación, invitación). Están **en claro** en la tabla: quien lee la base puede falsificar una sesión. Se rotan solas cada 90 días |
| Tokens de invitación | `workspaces.invitations` | Solo el hash; el token existe únicamente en el enlace enviado |
| Respuestas de comandos idempotentes | `platform.idempotency_keys` | Pueden contener datos financieros. Se purgan a los 7 días |
| Cuerpo de los correos | `platform.outgoing_emails` | Se borra al enviarlo, porque puede llevar un enlace de un solo uso |

Por eso **una copia de la base es un secreto**: cífrala y guárdala con acceso restringido. Ver [database.md](../backend/database.md).

### Frontend

| Valor | Secreto | Dónde |
|---|---|---|
| URL de la API | No. El código usa rutas relativas | `proxy.conf.json` en local; `API_UPSTREAM` en `/srv/mercury/apps/<env>/finanzas-hub-web.env` en el servidor |
| Sesión | Cookie `ph.session`: `HttpOnly`, `SameSite=Lax` y `Secure` fuera de Development | La pone la API. El JavaScript del frontend no la ve ni la guarda |
| `PASSWORD` de `e2e/support.ts` | No. Contraseña fija de los usuarios desechables que crean los E2E | Solo para tests contra una base local; nunca contra un entorno real |
| `E2E_MAIL_DIR`, `PW_CHANNEL`, `CI` | No | Variables de los E2E |

No guardes tokens en `localStorage` ni en `sessionStorage`, y no pongas claves de servicios de terceros en el frontend: si algo necesita una clave, lo llama la API.

### CI/CD (Jenkins, servidor Mercury)

| Secreto | Dónde vive | Lo usa |
|---|---|---|
| Credenciales del registro de imágenes | Credencial de Jenkins `registry` (`REGISTRY_USR` / `REGISTRY_PSW`) | Los dos Jenkinsfile, para publicar y descargar imágenes |
| Token de SonarQube | Servidor `sonarqube` configurado en Jenkins (`withSonarQubeEnv`, `SONAR_AUTH_TOKEN`) | Jenkinsfile del backend |
| Configuración de cada app | `/srv/mercury/apps/<env>/finanzas-hub-api.env`, `finanzas-hub-worker.env`, `finanzas-hub-web.env` | El despliegue los pasa al contenedor como variables de entorno. Deben ser legibles solo por el agente de despliegue |

Los Jenkinsfile no contienen secretos: solo nombran las credenciales.

## Dónde se guarda cada cosa, por entorno

| Entorno | Backend | Frontend | Infraestructura local |
|---|---|---|---|
| Local | `dotnet user-secrets` con id `personal-finance-api`, compartido por API y Worker. Fichero en `%APPDATA%\Microsoft\UserSecrets\personal-finance-api\secrets.json` (Windows) o `~/.microsoft/usersecrets/personal-finance-api/secrets.json` | Ninguno | `finanzas-hub-api/.env` (ignorado por git) con la contraseña de PostgreSQL de `docker compose` |
| Tests de integración | Testcontainers crea una base desechable con credenciales aleatorias; o `PERSONALFINANCE_TEST_POSTGRES` hacia un clúster local de usar y tirar | — | — |
| CI | No necesita secretos de la aplicación: los tests que corre no tocan la base | — | — |
| dev / prod | Variables de entorno de `/srv/mercury/apps/<env>/*.env` | `API_UPSTREAM` | — |

`user-secrets` **no cifra**: es un JSON en tu perfil, fuera del repositorio. Sirve para no subir secretos a git, no para protegerlos en una máquina compartida.

## Tareas habituales

### Configurar tu máquina

```powershell
cd finanzas-hub-api
dotnet user-secrets set "ConnectionStrings:PersonalFinance" "Host=localhost;Port=5432;Database=personal_hub;Username=personal_hub;Password=<la de .env>" --project src/PersonalFinance.Api
# Solo si vas a probar con un SMTP o un WhatsApp reales:
dotnet user-secrets set "Email:SmtpHost" "smtp.ejemplo.com" --project src/PersonalFinance.Api
dotnet user-secrets set "Email:SmtpPassword" "<contraseña>" --project src/PersonalFinance.Api
dotnet user-secrets list --project src/PersonalFinance.Api      # comprobar (cuidado: muestra los valores)
```

Sin SMTP los correos se escriben como `.eml` en `%TEMP%\personal-hub-mail`, y sin token de WhatsApp los mensajes van a `%TEMP%\personal-hub-whatsapp`: para desarrollar no hacen falta secretos de terceros.

### Añadir un secreto nuevo

1. Propiedad en la clase `XOptions` correspondiente (`EmailOptions`, `WhatsAppOptions`…), **sin** valor por defecto.
2. Nada en `appsettings.json`. Si quieres documentar la clave, hazlo en la tabla de este documento.
3. Que la aplicación funcione sin él en local (modo de archivo, como el correo) o que falle al arrancar con un mensaje claro si es imprescindible en Production.
4. Añádelo a los `.env` del servidor de cada entorno y a esta tabla.

### Rotar un secreto

| Secreto | Cómo |
|---|---|
| Contraseña de PostgreSQL | `ALTER ROLE <usuario> PASSWORD '...'`; actualizar la cadena en los `.env` de API **y** Worker y redesplegar los dos (o `user-secrets` en local) |
| SMTP | Generar la nueva en el proveedor, cambiar `Email__SmtpPassword` en el `.env` del Worker y redesplegarlo |
| WhatsApp `AccessToken` | Nuevo token en Meta, `.env` del Worker |
| WhatsApp `AppSecret` | Restablecerlo en Meta, `.env` de la API. Hasta que coincidan se rechazan los eventos del webhook |
| Data Protection | Automática. Para invalidar todas las sesiones y enlaces de golpe, borrar las filas de `platform.data_protection_keys` y reiniciar la API |
| Credenciales de Jenkins | En la configuración de Jenkins; los Jenkinsfile no cambian |

## Recomendaciones pendientes

- **Usuario de base de datos propio.** La aplicación debería conectarse con un rol que solo tenga permisos sobre sus esquemas, no con `postgres`. Hoy no hay `GRANT` en las migraciones, así que la tabla de auditoría es de solo inserción por convención, no porque la base lo impida.
- **Cifrar las claves de Data Protection** (`ProtectKeysWith...`) si la base se aloja donde otras personas tienen acceso de lectura.
- **Detectar secretos antes del commit** (por ejemplo `gitleaks` en un hook o en el CI) para que la regla 5 no dependa de la memoria de nadie.
