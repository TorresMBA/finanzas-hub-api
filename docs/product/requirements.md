# Requisitos

## C. Requisitos funcionales descubiertos

Funcionalidad que aparece en los mocks y no estaba en la lista del encargo.

| Área | Descubierto | Destino |
|---|---|---|
| Shell | Selector global de mes; búsqueda global; badges de pendientes; navegación móvil con FAB | MVP |
| Onboarding | Elegir moneda base y zona horaria; sembrar categorías por defecto | MVP (no hay mock: diseñar) |
| Transacciones | Estado Pendiente/Confirmada; "Dividir gasto" desde una transacción; transferencia multimoneda; nota; fila única por transferencia | MVP |
| Cuentas | Número enmascarado; cuentas destacadas en Inicio; ajuste de saldo; tipo de cambio referencial visible | MVP |
| Cuentas | Conexión bancaria y estado de sincronización | Diferido |
| Categorías | Tags asociados a categorías; nube de tags con conteo; icono y color | MVP |
| Presupuestos | Motor de ritmo (ritmo ideal, margen diario, estados); umbral de aviso; acumulación del sobrante; presupuestos fijos | MVP, acumulación en segunda iteración |
| Metas | Motor de estado y proyección; aporte sugerido; aporte rápido; aporte automático mensual; cuenta de ahorro vinculada | MVP |
| Calendario | Saldo proyectado; recordatorio con antelación; frecuencia única, mensual o anual | MVP |
| Compartidos | Grupos; balances por persona; sugerencia de liquidación mínima; recordatorio por WhatsApp; actividad del grupo | MVP |
| Compartidos | Comentarios en gastos; participantes sin cuenta de usuario | Diferido |
| Inversiones | Depósitos a plazo (TEA, plazo, vencimiento); fondos mutuos; distribución; sparkline de 12 meses | Fase 13 |
| Inversiones | Perfil de riesgo; comparación con inflación | Diferido |
| Reportes | Comparación con periodo anterior; comercios frecuentes; tasa de ahorro; exportación | Fase 12 |
| Reportes | Reportes programados; resumen semanal | Diferido |
| Integraciones | Importar CSV/XLSX/OFX; reglas de autoclasificación | MVP (D4) |
| Integraciones | Lectura de correo; Google Sheets; tokens de API y webhooks salientes | Diferido |
| Notificaciones | Preferencias por categoría y canal; horario silencioso; umbral de movimiento grande; enlaces profundos | MVP |
| Notificaciones | Alertas personalizadas; cargos duplicados o fraude; push | Diferido |
| WhatsApp | Recordatorios y comandos de consulta, con teléfono verificado por usuario | MVP (D4) |
| Configuración | Perfil con foto y celular; preferencias de formato; ocultar saldos | MVP |
| Configuración | 2FA; sesiones activas; biometría; plan, facturación y límites de uso; DNI | Diferido (D4) |
| Plataforma | Cliente móvil nativo (el mock menciona "App FinanzasHub" y biometría) | Fuera de alcance; la API queda preparada |

## D. Requisitos no funcionales

| Atributo | Requisito | Cómo se verifica |
|---|---|---|
| Privacidad | Ningún endpoint devuelve datos privados de otro usuario; un recurso ajeno responde 404 | Matriz de tests de autorización por endpoint (sección V) |
| Corrección financiera | `decimal` siempre; moneda explícita; ninguna cifra tiene dos orígenes | Tests de dominio y consulta de reconciliación en CI |
| Idempotencia | Todo POST con efecto financiero acepta `Idempotency-Key`; webhooks y jobs deduplicados por clave natural | Tests de reintento |
| Auditoría | Toda mutación financiera deja quién, qué, cuándo, antes, después y `correlationId` | Test por caso de uso |
| Modularidad | Un módulo solo usa el `Contracts` de otros; Domain no referencia EF ni ASP.NET | Tests de arquitectura en CI |
| Rendimiento | p95 < 300 ms en listados con 100 000 asientos; dashboard < 500 ms | Test de carga puntual con datos sembrados |
| Disponibilidad | Instancia única aceptable; respaldo con recuperación a un punto en el tiempo de 7 días | Configuración de PostgreSQL gestionado |
| Operación | Un contenedor de API, una base de datos; arranque local con `docker compose up` | Fase 0 |
| Observabilidad | Trazas, métricas y logs por OpenTelemetry; `traceId` en cada error | Sección U |
| Localización | es-PE inicial; formatos `S/` y `US$`; zona horaria por workspace (por defecto `America/Lima`) | Tests de formato |
| Accesibilidad | Contraste AA, foco visible, navegación por teclado en diálogos | Revisión por pantalla en la DoD |
| Responsive | Los 3 breakpoints del mock | E2E en 3 viewports |
| Evolución | Sustituir correo, almacenamiento de archivos, bus de eventos y canales sin tocar dominio | Fronteras de la sección F.6 |
| Coste | Orden de decenas de USD al mes en Azure | Sección W |
