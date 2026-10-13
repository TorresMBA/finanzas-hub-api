# Decisiones pendientes

## E. Decisiones pendientes

Cada pregunta tiene opción por defecto. Se ejecuta la recomendada salvo indicación contraria.

| OQ | Pregunta | Alternativa A | Alternativa B | Recomendación | Impacto |
|---|---|---|---|---|---|
| 01 | Alcance de los datos privados | Por `(workspace, usuario)`: cada workspace es un juego de libros separado | Global por usuario: las cuentas del usuario se ven igual desde cualquier workspace | **A**. Coincide con el encargo (§16) y con las URLs. Relajar a B después es quitar un filtro; lo contrario exige migrar datos. Riesgo de A: quien esté en dos workspaces duplica cuentas | Alto |
| 02 | Roles del workspace | Owner, Admin, Member | Añadir "Solo lectura" como en el mock | **A**. Bajo D2 "Solo lectura" no tiene nada que leer | Bajo |
| 03 | Alcance de categorías y tags | Privadas por usuario, sembradas desde un catálogo con `system_key` | Compartidas por workspace y gestionadas por Admin | **A**, aplicada en la fase 2 (13-oct-2026). Los nombres de categoría revelan hábitos; `system_key` permite mapear entre usuarios | Medio |
| 04 | Modelo de tags | Entidad `Tag` única; la categoría sugiere tags por defecto y la transacción guarda los suyos | Tags solo en categorías | **A**, aplicada en la fase 2 (13-oct-2026). El formulario de transacción ya tiene etiquetas propias | Medio |
| 05 | Movimiento Pendiente | Cuenta en saldos y presupuestos; es una marca de "por revisar" | Excluido hasta confirmarse (regla de V1) | **A**, aplicada en la fase 3 (13-oct-2026). Es lo que muestran los datos del mock | Medio |
| 06 | Moneda de la transacción | Se deriva de la cuenta | Libre, con conversión | **A**, aplicada en la fase 3 (13-oct-2026). Sin FX automático en MVP | Bajo |
| 07 | Unidad de autorización en compartidos | El grupo | Cada gasto | **A**. Coincide con el mock y simplifica balances | Medio |
| 08 | Participantes de compartidos | Solo miembros del workspace | También invitados por nombre | **A** ahora; la tabla admite `user_id` nulo para B | Medio |
| 09 | Reembolso recibido por quien pagó | `Refund` contra la categoría original: gasto neto = su parte | Ingreso en categoría "Reembolsos" (mock) | **A**. B infla ingresos y gastos y falsea el presupuesto | Alto |
| 10 | Cuándo reconoce el gasto el deudor | Al pagar (caja) | Al crearse el gasto (devengo, con cuenta "por pagar") | **A**. No escribe en libros ajenos | Medio |
| 11 | Confirmación de liquidaciones | Sin confirmación; cualquiera de las dos partes registra y puede anular | El acreedor debe confirmar | **A** para 10 usuarios de confianza | Bajo |
| 12 | Aportes a metas | Se permite superar el objetivo; hay retiros; misma moneda que la meta | Tope en el objetivo; sin retiros | **A** | Bajo |
| 13 | Custodia del dinero de una meta compartida | Cada participante conserva lo suyo | Un custodio recibe los aportes | **A**. B exige modelar pasivos de custodia | Medio |
| 14 | Transporte de autenticación | Cookie HttpOnly con app y API bajo el mismo sitio | Bearer con refresh token | **A**. Evita exponer tokens a XSS; el proxy de Angular ya da mismo origen en local | Medio |
| 15 | Datos privados de un miembro expulsado | Se conservan inaccesibles; exportación a petición | Purga a los 30 días | **A** | Bajo |
| 16 | Presupuesto "Fijo" | Marca explícita en el presupuesto | Derivado de reglas recurrentes | **A** | Bajo |
| 17 | Periodos de presupuesto en MVP | Solo mensual | Mensual y semanal | **A**; semanal después, el motor de prorrateo de V1 ya lo soporta | Bajo |
| 18 | Tarjeta bimoneda | Dos cuentas, una por moneda | Una tarjeta con dos saldos | **A** | Bajo |
| 19 | Meta de tasa de ahorro | Preferencia del usuario | Omitir | **A** | Bajo |
| 20 | Nombre | Namespace `PersonalFinance`, marca configurable | Renombrar a `PersonalHub` | **A**. Facilita portar y coincide con los ejemplos del encargo | Bajo |
| 21 | Registro mediante invitación | También crea workspace personal; el predeterminado es el que invita | No crea workspace personal | **A**, por el requisito §4 | Bajo |
| 22 | Editar monto, cuenta o fecha de un movimiento confirmado | En sitio, con antes y después en auditoría | Anular y recrear | **A**, aplicada en la fase 3 (13-oct-2026). B es fricción de nivel bancario | Medio |
| 23 | Alcance de WhatsApp | Recordatorios salientes y comandos de solo lectura (`saldo`, `ayuda`) | También registrar gastos por chat | **A**. Los mensajes iniciados por el negocio exigen plantillas aprobadas por Meta | Medio |
| 24 | "Cuentas principales" en Inicio | Marca de favorita | Top por saldo | **A** | Bajo |
| 25 | Inicio de sesión con Google o Microsoft | Solo correo y contraseña en la fase 1; los proveedores externos quedan en el backlog | Añadirlos ya, solos o junto a la contraseña | **A**, decidida por el dueño el 13-oct-2026. ASP.NET Core Identity ya trae la tabla `user_logins`, así que añadirlos después no cambia el modelo | Bajo |
