# Módulo Integrations

| | |
|---|---|
| Responsabilidad | Importación de archivos, reglas de autoclasificación y comandos por chat. Más adelante: conexiones bancarias y tokens de API |
| Aggregate roots | `ImportSession` (con `ImportRecord`), `CategorizationRule` |
| Value objects | `SourceFormat`, `Fingerprint`, `ImportRecordStatus`, `MatchPattern` |
| Servicios | `IImportSourceAdapter` (CSV genérico, XLSX, OFX, Bluecoins), `DuplicateDetector`, `RuleEngine`, `ChatCommandParser` |
| Posee | Esquema `integrations` |
| Consulta de otros | `Ledger.Contracts`, `Categories.Contracts`, `Notifications.Contracts` |
| Eventos | `ImportCommitted` (D, publica notificación) |

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| UploadImport | Dueño | archivo, cuenta destino, formato | Máximo 10 MB; el hash evita reprocesar el mismo archivo | Sesión `Uploaded`; análisis en segundo plano |
| ParseImport (Sistema) | Como el dueño | sesión | Normaliza, marca inválidos, detecta duplicados por huella contra el ledger, aplica reglas | Sesión `Parsed` |
| ReviewRecord | Dueño | categoría, excluir | Solo antes de confirmar | — |
| CommitImport | Dueño | sesión | Idempotente; cada registro se confirma una vez | Operaciones en Ledger con `origin = Import`; `ImportCommitted` |
| DiscardImport | Dueño | sesión | — | Borra el staging |
| CreateRule, UpdateRule, ToggleRule, DeleteRule | Dueño | patrón, categoría, tags, prioridad | Categoría propia | — |
| HandleInboundMessage (Sistema, consume `InboundMessageReceived`) | Usuario del teléfono verificado | texto | Solo comandos de lectura en MVP; teléfono no verificado se ignora | Responde por `IDirectMessenger` |

## Lo construido en la fase 10

| | |
|---|---|
| Proyecto | `Modules/Integrations`. Depende de `Ledger.Contracts` (`ILedgerImport`, `ILedgerQueries`) y `Categories.Contracts` (`ICategoryDirectory`, `ICategoryLookup`) |
| Lectura | `ImportFileReader`: OFX, Bluecoins (cabecera de exportar o de la plantilla numerada) y tabla de banco. Las líneas ilegibles quedan con su motivo |
| Sesión | `ImportSession` con sus cuentas (`ImportAccount`) y categorías (`ImportCategory`) del archivo; `ImportRecord` es staging en tabla aparte, borrado en cascada al descartar |
| Estado de una línea | Se calcula al leer, no se guarda: inválida, ya registrada, excluida, sin cuenta, otra moneda, sin categoría, lista o importada |
| Duplicados | Contra `ILedgerImport.GetEntryKeysAsync`; se recalculan al cambiar una cuenta y al confirmar |
| Límites | 10 MB, 10 000 líneas; el contenido del archivo no se registra en logs ni se guarda, solo su SHA-256 |
| API | `W/imports` (subida con el archivo como cuerpo, detalle paginado, `accounts/{id}`, `categories/{id}`, `records/{id}`, `commit`, descartar) y `W/categorization-rules` |
| Pantalla | `/integrations`: subida, historial, reglas y la revisión |
