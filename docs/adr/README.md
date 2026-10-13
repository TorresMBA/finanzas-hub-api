# Decisiones de arquitectura

Un ADR nuevo recibe el siguiente número. Uno aceptado no se reescribe: se sustituye con otro que lo cita, y en el antiguo solo cambia la línea de estado.

Los ADR 0008, 0009, 0010, 0013 y 0014 del repositorio anterior (`finance-hub-api/docs/adr`) son antecedentes de los ADR 004 y 008 y de la fase 14.

| ADR | Título | Estado |
|---|---|---|
| [ADR-001](ADR-001-monolito-modular.md) | Monolito modular | Sustituido en parte por ADR-019 |
| [ADR-002](ADR-002-comunicacion-entre-modulos.md) | Comunicación entre módulos | Sustituido en parte por ADR-019 |
| [ADR-003](ADR-003-persistencia.md) | Persistencia | Sustituido por ADR-019 |
| [ADR-004](ADR-004-ledger-financiero.md) | Ledger financiero | Aceptado |
| [ADR-005](ADR-005-seguridad-del-workspace.md) | Seguridad del workspace | Aceptado |
| [ADR-006](ADR-006-eventos.md) | Eventos | Sustituido por ADR-019 |
| [ADR-007](ADR-007-tiempo-real.md) | Tiempo real | Aceptado |
| [ADR-008](ADR-008-procesos-en-segundo-plano.md) | Procesos en segundo plano | Sustituido por ADR-019 |
| [ADR-009](ADR-009-contratos-de-api.md) | Contratos de API | Sustituido en parte por ADR-019 |
| [ADR-010](ADR-010-estado-en-el-frontend.md) | Estado en el frontend | Aceptado |
| [ADR-011](ADR-011-errores.md) | Errores | Aceptado |
| [ADR-012](ADR-012-modulo-ledger-unificado.md) | Módulo Ledger unificado | Aceptado |
| [ADR-013](ADR-013-aporte-a-meta.md) | Aporte a meta | Aceptado |
| [ADR-014](ADR-014-contabilidad-de-gastos-compartidos.md) | Contabilidad de gastos compartidos | Aceptado |
| [ADR-015](ADR-015-autenticacion.md) | Autenticación | Aceptado |
| [ADR-016](ADR-016-idempotencia-y-concurrencia.md) | Idempotencia y concurrencia | Aceptado |
| [ADR-017](ADR-017-auditoria-funcional.md) | Auditoría funcional | Aceptado |
| [ADR-018](ADR-018-notificaciones.md) | Notificaciones | Aceptado |
| [ADR-019](ADR-019-simplificacion-de-la-plataforma.md) | Simplificación de la plataforma | Aceptado |
| [ADR-020](ADR-020-sesion-y-correos.md) | Sesión, CSRF y correos | Aceptado |

Los documentos de [architecture/](../architecture/) describen el diseño vigente, es decir, ya con el ADR-019 aplicado. Un ADR sustituido se conserva para entender de dónde se viene.
