# Análisis de los mocks

## B. Análisis de los mocks

Fuente: `Mocks_finanzas-hub-angular\FinanzasHub Mocks.dc.html`. Los datos de ejemplo usan el "Hogar Quispe" (3 miembros), soles y dólares, octubre 2026.

**Divergencia de fondo con el requisito.** El mock habla de "las finanzas de tu hogar", "Tags del hogar", "Meta del hogar: 25%" y define un rol "Solo lectura ve reportes y saldos". Por D2 eso no aplica: todas las pantallas salvo Gastos compartidos, metas compartidas y Hogar y miembros muestran datos del usuario actual. Hay que reescribir ese copy.

**Diferencias de navegación respecto a V1.** En el mock no existen páginas propias de Transferencias (es una pestaña y un tipo en Transacciones), Workspaces (está en Configuración › Hogar y miembros) ni Imports (está dentro de Integraciones).

### B.0 Shell (sidebar, topbar, navegación móvil)

| Aspecto | Detalle |
|---|---|
| Objetivo | Navegar, fijar contexto (workspace y mes), buscar, ver avisos |
| Información | 13 ítems en 4 grupos; badge en Gastos compartidos (2) y Notificaciones (no leídas); workspace actual; mes; usuario con plan y nº de miembros |
| Acciones | Cambiar de página, de workspace, mes anterior/siguiente, tema, abrir notificaciones, búsqueda global `⌘K`, CTA contextual por página |
| Estados | Desktop ≥1100 px (sidebar 252 px y riel derecho), tablet 700–1099 (riel de iconos 84 px), móvil <700 (barra inferior, FAB, hoja "Todas las secciones"); oscuro/claro; toast |
| Endpoints | `GET /api/v1/me`, `GET /api/v1/me/workspaces`, `GET /api/v1/me/notifications/unread-count`, `GET W/search?q=` |
| Autorización | Autenticado; el workspace seleccionado exige membresía activa |
| Tiempo real | Badge de notificaciones |
| EXPLÍCITO | Estructura de navegación, selector de mes y de workspace, breakpoints |
| INFERIDO | El mes del topbar filtra Inicio, Transacciones, Presupuestos y Calendario; el badge de compartidos cuenta deudas pendientes |
| REQUIERE DECISIÓN | Alcance de la búsqueda global (propuesta: transacciones, cuentas y metas propias). "Plan Hogar" queda diferido por D4 |

### B.1 Inicio

| Aspecto | Detalle |
|---|---|
| Objetivo | Resumen del mes |
| Información | 4 KPI (patrimonio neto con delta vs mes anterior, ingresos, gastos con % del presupuesto, tasa de ahorro con meta); flujo de efectivo de 6 meses; top 5 gastos por categoría; 5 movimientos recientes; 4 cuentas principales; 3 metas activas; 3 próximos pagos |
| Acciones | Nueva transacción; enlaces a Presupuestos, Transacciones, Cuentas, Metas, Calendario |
| Filtros | Mes global |
| Estados | Las capturas de V1 muestran "No hay información" por tarjeta; falta estado de carga y de error |
| Navegación | A las 5 secciones enlazadas |
| Dependencias | Ledger, Budgets, Goals, Scheduling, Categories |
| Endpoint | `GET W/dashboard?month=2026-10` (compuesto en Reporting) |
| Autorización | Solo datos propios; metas donde participa |
| Tiempo real | No |
| EXPLÍCITO | Patrimonio = activos − deudas, con USD convertido a TC 3.75; ahorro = (ingresos − gastos) / ingresos |
| INFERIDO | "Falta cobrar freelance (15 oct)" sale de ingresos programados; el mes en curso se pinta atenuado |
| REQUIERE DECISIÓN | "Cuentas principales": marca de favorita o top por saldo (OQ-24). Meta de tasa de ahorro (OQ-19) |

### B.2 Transacciones

| Aspecto | Detalle |
|---|---|
| Objetivo | Listar, buscar, clasificar y registrar movimientos |
| Información | Lista agrupada por día con neto diario; columnas descripción, categoría, cuenta, monto, estado; resumen ingresos/gastos/neto del filtro; panel de detalle con tags |
| Acciones | Nueva transacción, Editar, **Dividir gasto** (lleva la transacción a Gastos compartidos), limpiar filtros |
| Filtros | Pestañas Todas/Ingresos/Gastos/Transferencias con conteo; texto libre; cuenta |
| Estados | Vacío "Sin resultados" con "Limpiar filtros"; estado por fila Confirmada/Pendiente |
| Formulario | Tipo (Gasto/Ingreso/Transferencia), monto, moneda, descripción, categoría filtrada por tipo, cuenta (o Desde/Hacia), fecha, etiquetas, "Repetir cada mes", nota |
| Endpoints | `GET W/transactions?type&accountId&categoryId&tagId&q&from&to&status&cursor`, `POST W/transactions`, `POST W/transfers`, `GET/PATCH W/transactions/{id}`, `POST W/transactions/{id}/void`, `POST W/transactions/{id}/confirm` |
| Autorización | Dueño |
| Tiempo real | No |
| EXPLÍCITO | La transferencia es **una sola fila** con monto neutro y "Entre tus cuentas" (V1 mostraba dos filas). Existe transferencia multimoneda ("Compra de dólares" US$ 400). Monto > 0 y descripción obligatorios |
| INFERIDO | "Repetir cada mes" crea una regla recurrente; el detalle muestra los tags de la categoría |
| REQUIERE DECISIÓN | Moneda elegible aparte de la cuenta (OQ-06); semántica de Pendiente (OQ-05); modelo de tags (OQ-04); edición de montos confirmados (OQ-22) |

### B.3 Cuentas

| Aspecto | Detalle |
|---|---|
| Objetivo | Ver saldos y gestionar cuentas, billeteras y tarjetas |
| Información | KPI patrimonio, activos y deudas; grupos Cuentas bancarias, Efectivo y billeteras, Tarjetas de crédito con total; tarjeta por cuenta con tipo, moneda, saldo o deuda, número enmascarado y estado de sincronización; detalle con movimientos del mes |
| Acciones | Conectar o crear cuenta, Transferir, Editar |
| Filtros | Todas/Bancos/Billeteras/Tarjetas |
| Formulario | Modo Conectar banco (lista de bancos, redirección) o Cuenta manual (nombre, tipo, moneda, saldo inicial; en tarjeta: deuda actual, línea de crédito, día de pago 1–28) |
| Endpoints | `GET W/accounts`, `GET W/accounts/summary`, `POST W/accounts`, `GET/PATCH W/accounts/{id}`, `POST W/accounts/{id}/archive`, `POST W/accounts/{id}/adjust-balance` |
| Autorización | Dueño |
| EXPLÍCITO | Tarjeta: "Deuda actual", barra de utilización, "Disponible X de Y", "Pago 18 oct". Total de grupo con "(equiv.)" y "Tipo de cambio referencial: S/ 3.75 por US$" |
| INFERIDO | Saldo inicial o deuda actual se registran como asiento de apertura; disponible = línea + saldo |
| REQUIERE DECISIÓN | "Conectar banco" y estado de sincronización quedan diferidos por D4, solo cuenta manual en MVP. El mock no pide día de cierre de tarjeta y V1 sí (propuesta: opcional). Tarjetas bimoneda (OQ-18) |

### B.4 Categorías y etiquetas

| Aspecto | Detalle |
|---|---|
| Objetivo | Mantener la taxonomía |
| Información | KPI categorías, subcategorías y tags únicos; filas expandibles con subcategorías, tags, monto y nº de movimientos del mes; nube de tags con conteo |
| Acciones | Nueva categoría o subcategoría, añadir tag a una categoría, crear tag rápido, filtrar por tag |
| Filtros | Texto (nombre o tag); Todas/Gastos/Ingresos; tag activo |
| Formulario | Tipo Gasto/Ingreso, nombre, ubicación (principal o subcategoría de X), icono y color (solo principal), tags separados por coma |
| Endpoints | `GET/POST W/categories`, `PATCH W/categories/{id}`, `POST W/categories/{id}/archive`, `PUT W/categories/{id}/tags`, `GET/POST W/tags` |
| Autorización | Dueño (ver OQ-03) |
| EXPLÍCITO | Jerarquía de 2 niveles; la categoría tiene tipo ingreso o gasto; tags normalizados a minúsculas con guiones; no se repite un tag en la misma categoría |
| INFERIDO | La subcategoría hereda tipo, icono y color del padre |
| REQUIERE DECISIÓN | Taxonomía por usuario o por workspace (OQ-03); relación entre tags de categoría y de transacción (OQ-04) |

### B.5 Presupuestos

| Aspecto | Detalle |
|---|---|
| Objetivo | Fijar límites por categoría y vigilar el ritmo de gasto |
| Información | Disponible del mes sobre total presupuestado, día X de N, barra con marca de "ritmo ideal"; gasto permitido por día; categorías en alerta; fila por categoría con gastado/límite, % usado, margen diario y estado; detalle con movimientos; gráfico de gasto diario |
| Acciones | Nuevo presupuesto, Ajustar límite, Ver movimientos (va a Transacciones filtrado) |
| Filtros | Todos/En riesgo/Excedidos/En ritmo/Fijos |
| Formulario | Categoría de gasto, límite, periodo Mensual/Semanal, aviso al 75/90/100 %, acumular lo no gastado |
| Endpoints | `GET W/budgets?month=`, `POST W/budgets`, `PATCH W/budgets/{id}`, `DELETE W/budgets/{id}`, `GET W/budgets/{id}?month=` |
| Autorización | Dueño |
| EXPLÍCITO | Estados: Excedido si gastado > límite; Fijo o Pagado si es fijo; En riesgo si % usado > ritmo + 10 pp; si no, En ritmo. Ritmo = día / días del mes. El movimiento pendiente (Rappi S/ 54) suma al consumo |
| INFERIDO | Presupuesto solo sobre categoría principal; el formulario sugiere con el promedio de 3 meses |
| REQUIERE DECISIÓN | Origen de la marca "Fijo" (OQ-16); semanal en MVP (OQ-17); moneda del presupuesto (propuesta: la base del workspace) |

### B.6 Metas

| Aspecto | Detalle |
|---|---|
| Objetivo | Ahorrar con objetivo y fecha |
| Información | KPI ahorrado (PEN y USD por separado), aporte mensual sugerido, metas en camino; tarjetas con anillo de %, ahorrado/objetivo, fecha, cuota mensual; detalle con cuenta, aportes de 6 meses y proyección |
| Acciones | Nueva meta, aporte rápido +100/+250/+500 |
| Filtros | Activas/Completadas/Todas |
| Formulario | Nombre, monto objetivo, moneda, fecha objetivo (mes), **cuenta de ahorro**, aporte automático mensual y su monto |
| Endpoints | `GET/POST W/goals`, `GET/PATCH W/goals/{id}`, `POST W/goals/{id}/contributions`, `POST W/goals/{id}/withdrawals`, `PUT W/goals/{id}/auto-contribution`, `POST/DELETE W/goals/{id}/participants` |
| Autorización | Participante de la meta |
| Tiempo real | Señal de cambio para metas compartidas |
| EXPLÍCITO | Estados: Completada; Atrasada si lo necesario > 1.5 × promedio; Ajustar ritmo si > 1.15 ×; En camino. Necesario = restante / meses restantes. Varias metas comparten una misma cuenta, así que progreso ≠ saldo de la cuenta |
| INFERIDO | El aporte es una transferencia hacia la cuenta de ahorro (el calendario lo muestra como `BCP Sueldo → BCP Ahorros`). Coincide con D3 |
| REQUIERE DECISIÓN | El mock no muestra participantes ni metas compartidas: hay que diseñar esa UI. Sobreaporte, retiros y moneda distinta (OQ-12); custodia del dinero común (OQ-13) |

### B.7 Calendario

| Aspecto | Detalle |
|---|---|
| Objetivo | Ver ingresos, pagos recurrentes y vencimientos del mes |
| Información | KPI ingresos del mes (ya cobrados), pagos pendientes, saldo proyectado a fin de mes; rejilla mensual con hasta 2 píldoras y "+N más"; detalle del día con neto; próximos pagos |
| Acciones | Programar pago, seleccionar día, Hoy, mes anterior/siguiente |
| Filtros | Todos/Ingresos/Gastos/Recurrentes |
| Formulario | Descripción, monto, moneda, fecha, cuenta, frecuencia Una vez/Mensual/Anual, categoría, recordatorio (mismo día, 1 día, 3 días, 1 semana antes) |
| Endpoints | `GET W/calendar?from&to&filter`, `POST W/recurring-rules`, `PATCH W/recurring-rules/{id}`, `POST W/recurring-rules/{id}/pause\|resume\|end`, `POST W/recurring-rules/{id}/occurrences/{date}/confirm\|skip` |
| Autorización | Dueño |
| EXPLÍCITO | Tres clases de evento: ingreso, gasto, transferencia o pago de tarjeta. Estado Confirmado (pasado) o Programado (futuro). Proyección = saldo hoy + ingresos pendientes − pagos pendientes |
| INFERIDO | Lo pasado se proyecta desde el ledger; lo futuro desde reglas, vencimientos de tarjeta y aportes automáticos. No se duplica información para pintarla |
| REQUIERE DECISIÓN | Qué cuentas entran en el "saldo hoy" (propuesta: activos líquidos, sin tarjetas ni fondos) |

### B.8 Gastos compartidos

| Aspecto | Detalle |
|---|---|
| Objetivo | Dividir gastos y liquidar deudas |
| Información | KPI te deben, debes, balance neto; grupos con su balance; balances por miembro; gastos del grupo (quién pagó, fecha, tu parte); resumen; actividad reciente; sugerencia de liquidación |
| Acciones | Nuevo gasto, Recordar (WhatsApp), Registrar cobro, Pagar con Yape, cambiar de grupo |
| Formulario | Grupo, fecha, descripción, monto total, quién pagó, cómo dividir (Partes iguales/Montos/Porcentajes), participantes, vista previa del reparto |
| Endpoints | `GET/POST W/shared-groups`, `GET W/shared-groups/{id}`, `GET/POST W/shared-groups/{id}/expenses`, `POST .../expenses/{eid}/publish\|cancel`, `POST W/shared-groups/{id}/settlements`, `POST .../settlements/{sid}/link-account\|void`, `POST .../members/{mid}/remind`, `GET W/shared-expenses/summary` |
| Autorización | Miembro del grupo |
| Tiempo real | Señal de cambio del grupo |
| EXPLÍCITO | Modelo tipo Splitwise con grupos y pagador; el pagador puede no participar; la actividad incluye comentarios ("Lucía comentó") |
| INFERIDO | El balance entre personas es derivado; "Pagar con Yape" solo registra el pago |
| REQUIERE DECISIÓN | Unidad de autorización grupo o gasto (OQ-07); participantes que no son usuarios, como "Rosa (mamá)" (OQ-08); contabilidad del reembolso (OQ-09, OQ-10); confirmación del acreedor (OQ-11). Comentarios: diferido |

### B.9 Inversiones

| Aspecto | Detalle |
|---|---|
| Objetivo | Seguir el portafolio y su rendimiento |
| Información | KPI valor del portafolio en moneda base, ganancia no realizada, rendimiento anual vs inflación; distribución por tipo; posiciones con sparkline de 12 meses; detalle con monto invertido, tipo, antigüedad, vencimiento o peso |
| Acciones | Registrar inversión, Aportar, Rescatar |
| Filtros | Todas/Fondos mutuos/Depósitos a plazo/Acciones y ETFs/Cripto |
| Formulario | Tipo, nombre o ticker, institución, fecha de compra, monto, moneda, TEA y plazo (depósito), cantidad (acción o cripto), cuenta de origen |
| Endpoints | `GET W/investments/summary`, `GET/POST W/holdings`, `GET W/holdings/{id}`, `POST W/holdings/{id}/contributions\|withdrawals\|valuations` |
| Autorización | Dueño |
| EXPLÍCITO | Depósitos a plazo con TEA, plazo y valor proyectado al vencimiento; fondos mutuos sin ticker; "Cuenta de origen" obligatoria |
| INFERIDO | Comprar es transformar activos, no gasto (ADR-0010 de V1) |
| REQUIERE DECISIÓN | Fuente de precios (propuesta: valuación manual en MVP); "Perfil: moderado" e inflación son dato externo: diferido |

### B.10 Reportes

| Aspecto | Detalle |
|---|---|
| Objetivo | Entender en qué se va el dinero y cómo evoluciona el ahorro |
| Información | KPI ingresos, gastos, ahorro y gasto promedio diario con delta vs periodo anterior; dona de gasto por categoría (top 5 y Otros); variación por categoría; comercios frecuentes; tasa de ahorro mensual |
| Acciones | Exportar PDF y Excel; Programar reporte |
| Filtros | Este mes/Trimestre/Año |
| Endpoints | `GET W/reports/summary?period=&anchor=`, `GET W/reports/export?format=` |
| Autorización | Dueño |
| EXPLÍCITO | Comparación con el periodo anterior equivalente; ranking de comercios con nº de compras |
| INFERIDO | El ranking exige normalizar el campo `payee` |
| REQUIERE DECISIÓN | Reportes programados y resumen semanal: diferidos. PDF después de XLSX/CSV |

### B.11 Integraciones

| Aspecto | Detalle |
|---|---|
| Objetivo | Traer movimientos y automatizar su clasificación |
| Información | Conexiones con estado (sincronizado, token vencido, sincronizando); catálogo disponible (bancos, broker, correo, Google Sheets, API y webhooks); zona de importación; reglas automáticas |
| Acciones | Sincronizar, Reconectar, Conectar, importar archivo, activar o desactivar regla |
| Endpoints (MVP) | `POST W/imports`, `GET W/imports/{id}`, `GET W/imports/{id}/records`, `PATCH .../records/{rid}`, `POST W/imports/{id}/commit\|discard`, CRUD `W/categorization-rules` |
| Autorización | Dueño |
| Tiempo real | No; el estado de la importación se consulta por sondeo |
| EXPLÍCITO | Formatos CSV, XLSX, OFX, máximo 10 MB; reglas "si contiene X → categoría" |
| INFERIDO | La importación necesita staging, detección de duplicados y revisión antes de confirmar (ya resuelto en V1) |
| REQUIERE DECISIÓN | Conexiones bancarias, lectura de correo, Google Sheets y tokens de API: diferidos por D4 |

### B.12 Notificaciones

| Aspecto | Detalle |
|---|---|
| Objetivo | Bandeja de avisos y preferencias |
| Información | Avisos agrupados Hoy/Ayer/Esta semana con icono, título, texto, hora, acción con enlace profundo y marca de no leído; preferencias por categoría y canal; horario silencioso |
| Acciones | Marcar leído, marcar todo, navegar a la acción, alternar preferencias, Crear alerta |
| Filtros | Todas/No leídas/Alertas/Hogar con conteo |
| Estados | Vacío "Estás al día" |
| Endpoints | `GET /api/v1/me/notifications?filter&cursor`, `POST /api/v1/me/notifications/{id}/read`, `POST /api/v1/me/notifications/read-all`, `GET/PUT /api/v1/me/notification-preferences`, `GET/PUT /api/v1/me/notification-settings` |
| Autorización | Solo el destinatario |
| Tiempo real | Sí |
| EXPLÍCITO | 8 tipos: presupuesto excedido, movimiento pendiente, miembro registró gasto con tu parte, integración caída, meta al 93 %, pago de tarjeta en 6 días, te pagaron, resumen semanal. 5 categorías de preferencia con canales App, Correo y WhatsApp. Silencio 22:00–07:00 salvo alertas críticas |
| INFERIDO | "Movimientos mayores a S/ 500" es un umbral configurable |
| REQUIERE DECISIÓN | "Crear alerta" personalizada y detección de cargos duplicados: diferido. Push: diferido |

### B.13 Configuración

| Aspecto | Detalle |
|---|---|
| Objetivo | Perfil, miembros, preferencias, seguridad y plan |
| Información | Perfil (foto, nombre, correo, celular, DNI enmascarado); miembros con rol; preferencias (moneda principal, inicio de semana, formato de fecha, tema); seguridad (2FA, biometría, ocultar saldos, sesiones); plan y uso |
| Acciones | Guardar perfil, cambiar foto, invitar, cambiar rol, cambiar preferencias, cerrar sesión remota, gestionar pago |
| Endpoints (MVP) | `PATCH /api/v1/me/profile`, `PUT /api/v1/me/preferences`, `GET W/members`, `PATCH W/members/{userId}`, `DELETE W/members/{userId}`, `GET/POST W/invitations`, `DELETE W/invitations/{id}`, `POST /api/v1/invitations/{token}/accept` |
| Autorización | Perfil y preferencias: el propio usuario. Miembros: Admin u Owner |
| EXPLÍCITO | Roles Administrador, Editor, Solo lectura; no puedes cambiar tu propio rol |
| INFERIDO | Invitar es por correo |
| REQUIERE DECISIÓN | Semántica de roles bajo D2 (OQ-02). "Moneda principal" aparece como preferencia de usuario mientras el requisito la fija por workspace (propuesta: `Workspace.BaseCurrency` manda y solo Owner la cambia). DNI: no se pide en MVP. 2FA, sesiones, biometría y plan: diferidos por D4 |

### B.14 Capturas de la app V1

Documentan el estado actual, no el objetivo: selector "Demo household", pestaña "Inversiones" en Transacciones, transferencias como dos filas llamadas "Transfer", botón "Crear Tag con API" y estados vacíos "No hay información". El mock HTML sustituye a ese diseño.
