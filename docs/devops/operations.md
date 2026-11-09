# Operación

Tareas del día a día con la aplicación en marcha: copias de seguridad, migraciones a mano, purgas, rotación de secretos y qué hacer ante un incidente. Los ejemplos usan los nombres de Mercury (`finanzas-hub-api-<env>`); en otro servidor cambian los nombres, no los pasos.

## Copias de seguridad

**Qué contiene la base:** datos financieros, hashes de contraseñas, claves de Data Protection y la auditoría. Una copia es tan sensible como la base ([secrets.md](secrets.md)).

Las hace el servidor, no la aplicación: en Mercury, `mercury-backup.timer` copia a diario las bases, la de prod incluida. Ni el repositorio ni Jenkins tienen otro mecanismo (hasta la 1.0.2, `tools/backup.sh` hacía una copia antes de migrar prod; se retiró por redundante). En otro servidor, programa una copia diaria con `pg_dump --format=custom`, o usa la copia a un punto en el tiempo del proveedor.

```bash
# Restaurar en una base vacía
createdb personal_hub_restaurada
pg_restore --no-owner --dbname=personal_hub_restaurada <copia>.dump
```

**Prueba de restauración.** Una vez al mes, y antes de abrir la 1.0: restaurar la última copia en una base aparte, arrancar la API contra ella (`ConnectionStrings__PersonalFinance` apuntando a la restaurada, `Database__MigrateOnStartup=false`) y comprobar `/health/ready` y un inicio de sesión. Anota aquí la fecha:

| Fecha | Copia | Resultado |
|---|---|---|
| — | — | Pendiente de la primera |

- **A diario** (el timer del servidor). Antes de una migración delicada, una a mano.
- Guárdalas cifradas, fuera del servidor y con acceso restringido. Conserva al menos 7 diarias y 4 semanales.
- **Prueba a restaurar** de vez en cuando en una base aparte: una copia que nunca se restauró no es una copia.
- Restaurar una copia con otras claves de Data Protection cierra todas las sesiones e invalida los enlaces de correo pendientes; es lo esperado.

## Migraciones a mano

Normalmente las aplica el pipeline. Si hace falta a mano:

```bash
# Con la imagen de la versión que se va a desplegar y la configuración del ambiente
docker run --rm --env-file /srv/mercury/apps/<env>/finanzas-hub-api.env --network <red-de-la-base> \
  <registro>/finanzas-hub-api:<tag> migrate
```

En local: `tools/migrate.ps1`. Nunca dos procesos a la vez, y siempre después de una copia. Para saber si faltan: `GET /health/ready` responde con error si hay migraciones pendientes.

## Purgas y mantenimiento automático

| Qué | Quién | Cuándo |
|---|---|---|
| Claves de idempotencia de más de 7 días | `PurgeExpiredIdempotencyKeysJob` (Worker) | A diario |
| Avisos de más de 90 días, mensajes de WhatsApp enviados de más de 90 días e ids de eventos del webhook de más de 30 | `PurgeNotificationHistoryJob` (Worker) | A diario |
| Cuerpo de los correos enviados | `EmailDispatcher` al enviar | En cada envío |
| Claves de Data Protection | ASP.NET Core | Rotan cada 90 días |
| Auditoría (`platform.audit_log`) | Nadie | No caduca: es dato de negocio |

Si el Worker estuvo parado, al volver procesa lo acumulado; no hace falta intervenir.

## Rotar secretos

Cómo rotar cada secreto (base de datos, SMTP, WhatsApp, Data Protection, credenciales de Jenkins): [secrets.md § Rotar un secreto](secrets.md#rotar-un-secreto). Después de cambiar un `.env`, redespliega el contenedor afectado con la misma etiqueta para que lea los valores nuevos.

## La primera cuenta y las siguientes

Las cuentas se crean por invitación (`Auth__OpenRegistration=false`). La única excepción es la primera de una instalación: con la base vacía, `/register` deja crearla sin invitación. Quien la crea invita a los demás desde Configuración › Espacio, y cada persona se registra desde el enlace de su correo (o el que se le pasa a mano: la pantalla lo muestra al invitar). Crea esa primera cuenta en cuanto despliegues prod, antes de publicar la dirección.

## Cerrar sesiones

Cambiar o restablecer la contraseña cierra las demás sesiones de esa persona: la cookie lleva el *security stamp* de la cuenta y deja de valer cuando cambia. Una sesión sin usar caduca a los 7 días.

Para cerrar las de todos, por ejemplo tras una sospecha de robo de la base o de una cookie:

```sql
DELETE FROM platform.data_protection_keys;
```

Y reinicia la API. Todas las cookies y los enlaces de correo pendientes dejan de valer; cada persona vuelve a iniciar sesión.

## Ante un incidente

1. **¿Qué está roto y para quién?** Comprueba `/health/ready`, la web y los logs de API y Worker (`docker logs --since 30m finanzas-hub-api-prod`).
2. **¿Empezó con un despliegue?** Vuelve a la etiqueta anterior de API, Worker o web ([deployment.md §5](deployment.md#5-orden-de-un-despliegue)). Si la migración nueva no es compatible con el código anterior, restaura la copia previa al despliegue.
3. **¿Es la base?** Conexiones, espacio en disco, `SELECT 1`. Sin base, la API responde 500 y `/health/ready` falla.
4. **¿Son los envíos?** Correos o WhatsApp que no llegan: logs del Worker, credenciales del proveedor y filas pendientes (`SELECT count(*) FROM platform.outgoing_emails WHERE sent_at IS NULL`).
5. **¿Fuga de datos o acceso indebido?** Rota los secretos afectados, cierra las sesiones (arriba), revisa `platform.audit_log` y documenta qué datos y de quién.
6. **Después**: una nota con qué pasó, por qué, cómo se arregló y qué cambia para que no se repita (un test, una alerta, un paso del pipeline).

## Consultas útiles

```sql
-- Correos atascados
SELECT id, attempts, last_error, next_attempt_at FROM platform.outgoing_emails WHERE sent_at IS NULL ORDER BY created_at;

-- WhatsApp pendientes o fallidos
SELECT status, count(*) FROM notifications.deliveries GROUP BY status;

-- Historial de un recurso
SELECT occurred_at, action, actor_user_id, before, after
FROM platform.audit_log WHERE resource_type = 'budget' AND resource_id = '<id>' ORDER BY occurred_at;

-- Migraciones aplicadas
SELECT * FROM platform.__ef_migrations_history ORDER BY 1;
```

Recuerda que una consulta directa **no** tiene los filtros de privacidad de la aplicación: no extraigas datos de una persona sin motivo y no los compartas.
