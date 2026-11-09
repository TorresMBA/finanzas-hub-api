# Salir a producción

Lista para abrir PersonalHub a sus usuarios, y para comprobar cada despliegue después. Lo que cada punto configura se explica en [deployment.md](deployment.md), [configuration.md](configuration.md), [secrets.md](secrets.md) y [operations.md](operations.md); aquí solo va qué tiene que estar hecho y cómo saberlo.

## Antes del primer despliegue a prod

### Configuración

Production no arranca si falta algo de lo marcado como obligatorio (`ProductionSettings`): el contenedor se para con la lista de lo que falta. Revisa el log del primer arranque.

| Qué | Dónde | Comprobación |
|---|---|---|
| `ConnectionStrings__PersonalFinance` | `finanzas-hub-api.env` y `finanzas-hub-worker.env` | La API arranca y `/health/ready` responde 200 |
| `Cors__AllowedOrigins__0=https://<dominio>` | API | Sin barra final; tiene que ser https |
| `Frontend__BaseUrl=https://<dominio>` | API y Worker | Los enlaces de los correos abren la aplicación |
| `Email__SmtpHost`, `SmtpPort`, `SmtpUser`, `SmtpPassword`, `From` | API (solo `SmtpHost`) y Worker | Llega el correo de confirmación de una cuenta de prueba |
| `WhatsApp__Enabled=false` | API y Worker | Hasta que Meta apruebe las plantillas. La pantalla de Notificaciones no muestra WhatsApp |
| `WhatsApp__ShowCodesWithoutAccount` sin poner (o `false`) | API | Con `true` no arranca |
| `Auth__OpenRegistration` sin poner (o `false`) | API | Registrarse sin invitación responde `identity.invitation_required` (después de la primera cuenta) |
| `API_UPSTREAM` | `finanzas-hub-web.env` | La web carga y `/api/v1/me` responde 401, no 502 |

### Correo

Un proveedor SMTP con dominio propio, no una cuenta personal de Gmail: los correos de recuperación tienen que llegar a la bandeja de entrada.

| Qué | Cómo |
|---|---|
| Remitente del dominio | `Email__From="PersonalHub <no-reply@<dominio>>"` |
| SPF | Registro TXT del dominio que incluya al proveedor (`v=spf1 include:<proveedor> ~all`) |
| DKIM | La clave que da el proveedor, publicada en el DNS |
| DMARC | `_dmarc.<dominio>` TXT, al menos `v=DMARC1; p=none; rua=mailto:...` para empezar |
| Prueba | Pedir una recuperación de contraseña hacia Gmail y Outlook y ver que no cae en spam |

### Red y TLS

| Qué | Comprobación |
|---|---|
| DNS del dominio hacia el proxy TLS | `https://<dominio>` carga con certificado válido |
| El proxy envía `X-Forwarded-For` y `X-Forwarded-Proto` | Iniciar sesión funciona (la cookie es `Secure`) |
| ¿Hay un CDN delante (Cloudflare)? | Si lo hay, el proxy reemplaza `X-Forwarded-For` por la IP del CDN (`CF-Connecting-IP`); si no, el límite de `/auth` se comparte entre todos ([deployment.md §10](deployment.md#10-limitaciones-conocidas)) |
| WebSockets y `proxy_read_timeout 1h` en `/hubs/` | Un aviso nuevo aparece sin recargar |
| Cabeceras de la web | `curl -sI https://<dominio>/` muestra `Content-Security-Policy`, `Strict-Transport-Security`, `X-Content-Type-Options` y `X-Frame-Options` |

### Base de datos

| Qué | Cómo |
|---|---|
| Copias diarias | El `mercury-backup.timer` del servidor incluye la base de prod y guarda una copia fuera del servidor ([operations.md](operations.md#copias-de-seguridad)) |
| Restauración probada | Restaurar una copia en una base aparte y arrancar la API contra ella; anotarlo en la tabla de operations.md |
| Rol propio de la aplicación (recomendado) | [`tools/db-roles.sql`](../../finanzas-hub-api/tools/db-roles.sql) y `finanzas-hub-migrate.env` ([secrets.md](secrets.md)) |

### Cuentas

1. Desplegar prod y, en cuanto responda, crear la primera cuenta en `/register`: con la base vacía no hace falta invitación.
2. Desde Configuración › Espacio, invitar a los demás. Cada uno se registra desde su enlace.
3. Confirmar que un registro sin invitación ya no entra.

## En cada despliegue

| Qué | Comprobación |
|---|---|
| Copia previa | El log de Jenkins muestra `Copia lista: ... pre-deploy-<build>.dump` |
| Versión | `curl https://<dominio>/health/live` devuelve `{"status":"Healthy","version":"1.0.0"}` con la versión esperada |
| Base y migraciones | `/health/ready` responde 200 |
| Worker | `docker ps` muestra `finanzas-hub-worker-prod` como `healthy`: el Worker toca un archivo tras cada job correcto, y la imagen lo vigila |
| Logs | `docker logs finanzas-hub-api-prod --since 10m` sin errores; en prod salen en JSON, una línea por entrada |
| Recorrido | Iniciar sesión, registrar un movimiento, verlo en Inicio |

Y las comprobaciones de [deployment.md §8](deployment.md#8-comprobar-un-despliegue).

## Versiones

- La versión vive en `finanzas-hub-api/Directory.Build.props` (`<Version>`) y en `finanzas-hub-web/package.json`, y se anota en [CHANGELOG.md](../../CHANGELOG.md).
- Al publicar una versión: subir los dos números, añadir su entrada al CHANGELOG y etiquetar los dos repos (`git tag v1.0.0`; el frontend es su propio repositorio).
- Las imágenes siguen etiquetadas con el número de build de Jenkins; `/health/live` dice qué versión hay dentro.

## Para volver atrás

Volver a desplegar las etiquetas anteriores de API, Worker y web ([deployment.md §5](deployment.md#5-orden-de-un-despliegue)). Si las migraciones de la versión nueva no son compatibles con el código anterior, hay que restaurar la copia `pre-deploy`.
