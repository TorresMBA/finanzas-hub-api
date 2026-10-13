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
