# Cambios

Lo que cambia en cada versión de PersonalHub (API, Worker y web). El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y las versiones, [SemVer](https://semver.org/lang/es/). Cómo se publica una versión: [docs/devops/go-live.md](docs/devops/go-live.md#versiones).

## [Sin publicar]

## [1.0.2] - 2026-11-08

API, Worker y web. Sin cambios de comportamiento: el quality gate de SonarQube no dejaba desplegar (builds #25 de los dos jobs).

### Corregido

- API, 9 problemas nuevos:
  - Ternarios anidados (S3358), pasados a `if` con retorno temprano: `WhatsAppController`, `Goal.CanDelete`, `SharedGroup.CanDelete`, `Account.CanDelete` y `TransactionReader`.
  - Complejidad cognitiva (S3776): `Operation.PostBookTransfer` saca sus comprobaciones a `CheckBookTransfer`, y `DefaultCategoriesSeeder.SeedAsync` sus etiquetas a `SuggestedTags`.
  - Operador `!` sobrante (S8969) en `Session.ValidateAsync`.
  - `RowLock` (S2077) se justifica con `SuppressMessage`: los nombres de esquema, tabla y columna salen del modelo de EF y el id va como parámetro.
- Web, 2 problemas nuevos:
  - `persist()` del formulario de movimientos (S3776) pasa a una función por tipo: movimiento, transferencia y transferencia entre libros.
  - `sectionsFor(items, bool)` (S2301) pasa a `withoutCollaboration(items)`.
- El test de `/health/live` ya no fija la versión.

## [1.0.1] - 2026-11-08

Solo la web (`finanzas-hub-web` 1.0.1). La API y el Worker siguen en 1.0.0.

### Corregido

- Al volver a iniciar sesión tras caducar la sesión (por ejemplo, la primera vez tras desplegar la 1.0, que invalida las cookies anteriores) aparecía «Algo salió mal. Si vuelve a pasar, recarga la página.». Las vistas que leían una petición rechazada con 401 lanzaban el `ResourceValueError` de Angular, que lleva el `ApiError` dentro (`cause`), y el manejador global solo miraba el error de fuera. Ahora recorre la cadena de causas, y el aviso empieza limpio en cada sesión.
- El mismo aviso salía por cosas que no son fallos de la aplicación y que la ventana también reporta: el aviso inofensivo del navegador «ResizeObserver loop completed with undelivered notifications» y los errores de las extensiones. Ahora solo se registran en la consola.

## [1.0.0] - 2026-11-08

Primera versión para producción: las fases 0 a 13 del [plan](docs/roadmap/phases.md), endurecidas.

### Funcionalidad

- Cuentas de usuario con confirmación de correo y recuperación de contraseña; espacios de tipo Personal, Negocio u Otros, con miembros, roles e invitaciones en los Personal.
- Categorías y etiquetas privadas de cada persona.
- Cuentas con ingresos, gastos y transferencias (también entre libros), tarjetas de crédito, patrimonio y tipo de cambio. El saldo se deriva siempre de los movimientos.
- Presupuestos mensuales por categoría, movimientos programados con su calendario, metas personales y compartidas, y gastos compartidos entre miembros.
- Avisos en la bandeja, por correo y en tiempo real; WhatsApp con número verificado (ver *Configuración*).
- Importación de archivos de banco y de Bluecoins con reglas de clasificación.
- Reportes por mes, trimestre y año, exportación a Excel y CSV, y búsqueda global.
- Inversiones con su valor, depósitos a plazo y patrimonio sin doble conteo.

### Seguridad

- Cambiar o restablecer la contraseña cierra las demás sesiones; una sesión sin usar caduca a los 7 días.
- Las cuentas se crean solo por invitación, salvo la primera de la instalación.
- Un correo de recuperación por cuenta cada 2 minutos.
- La web envía CSP, HSTS, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` y `Permissions-Policy`, y sirve sus propias fuentes (sin Google Fonts).
- El frontend lleva a iniciar sesión cuando la sesión termina con la página abierta.

### Accesibilidad y usabilidad

- Contraste AA en el tema claro, foco devuelto al cerrar un diálogo y llevado al contenido al cambiar de página, errores de formulario enlazados a su campo, y una auditoría con axe en los E2E.
- Confirmación antes de eliminar una regla, anular un pago o revocar una invitación; página 404; aviso sin conexión y de errores inesperados.

### Operación

- `tools/backup.sh` para las copias (también antes de cada migración en prod) y `tools/db-roles.sql` para que la aplicación use un rol sin permisos sobre el esquema ni sobre la auditoría.
- Los tests de integración corren en el CI.
- El Worker tiene un health check en su imagen; los logs salen en JSON fuera de Development; `/health/live` dice qué versión corre.

### Configuración (para quien despliega)

- Nuevas: `Auth__OpenRegistration` (`false`) y `WhatsApp__Enabled` (`false`). WhatsApp queda apagado hasta que Meta apruebe las plantillas.
- Production ya no arranca sin `Email__SmtpHost` ni `Frontend__BaseUrl`, con `WhatsApp__ShowCodesWithoutAccount=true`, con WhatsApp encendido sin sus cuatro claves, o con un origen CORS que no sea https.
- Prod necesita `/srv/mercury/apps/prod/finanzas-hub-backup.env` ([operations.md](docs/devops/operations.md#copias-de-seguridad)).
- Las cookies de sesión anteriores a esta versión dejan de valer: cada persona vuelve a iniciar sesión una vez.

[Sin publicar]: https://github.com/TorresMBA/finanzas-hub-api/compare/v1.0.2...HEAD
[1.0.2]: https://github.com/TorresMBA/finanzas-hub-api/releases/tag/v1.0.2
[1.0.1]: https://github.com/TorresMBA/finanzas-hub-web/releases/tag/v1.0.1
[1.0.0]: https://github.com/TorresMBA/finanzas-hub-api/releases/tag/v1.0.0
