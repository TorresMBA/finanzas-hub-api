# Documentación de PersonalHub

PersonalHub es una plataforma de finanzas personales y colaborativas: API en .NET 10 (monolito modular) y SPA en Angular.

## Por dónde empezar

| Si vas a... | Lee |
|---|---|
| Entender el sistema | [architecture/overview.md](architecture/overview.md) |
| Levantar el entorno | [development/local-development.md](development/local-development.md) |
| Implementar una fase | [roadmap/phases.md](roadmap/phases.md) y el documento del módulo en [modules/](modules/) |
| Saber por qué la plataforma es así de simple | [adr/ADR-019](adr/ADR-019-simplificacion-de-la-plataforma.md) |
| Añadir un módulo o un caso de uso | [architecture/backend-structure.md](architecture/backend-structure.md) y [architecture/module-communication.md](architecture/module-communication.md) |
| Tocar dinero | [architecture/ledger.md](architecture/ledger.md) |
| Tocar permisos o consultas | [architecture/security.md](architecture/security.md) |
| Cambiar la API | [api/conventions.md](api/conventions.md) |
| Cambiar la base de datos | [architecture/data-model.md](architecture/data-model.md) y [development/migrations.md](development/migrations.md) |
| Trabajar en el frontend | [architecture/frontend.md](architecture/frontend.md) |
| Dar algo por terminado | [development/definition-of-done.md](development/definition-of-done.md) |

## Mapa de secciones

Los documentos nacieron de un único análisis y conservan sus letras de sección, porque se citan entre sí ("sección K.9", "J.2", "OQ-05").

| Sección | Tema | Documento |
|---|---|---|
| A | Resumen ejecutivo | [architecture/overview.md](architecture/overview.md) |
| B | Análisis de los mocks | [product/mock-analysis.md](product/mock-analysis.md) |
| C, D | Requisitos funcionales y no funcionales | [product/requirements.md](product/requirements.md) |
| E | Decisiones pendientes (OQ-01 a OQ-24) | [product/open-questions.md](product/open-questions.md) |
| F, G | Arquitectura y diagramas generales | [architecture/overview.md](architecture/overview.md) |
| H | Límites de módulos | [architecture/modules.md](architecture/modules.md) y [modules/](modules/) |
| I | Reglas de dependencia | [architecture/module-communication.md](architecture/module-communication.md) |
| J | Seguridad | [architecture/security.md](architecture/security.md) |
| K | Modelo financiero | [architecture/ledger.md](architecture/ledger.md) |
| L | Modelo de datos | [architecture/data-model.md](architecture/data-model.md) |
| M | ERD | [diagrams/erd.md](diagrams/erd.md) |
| N | Estructura del backend | [architecture/backend-structure.md](architecture/backend-structure.md) |
| O | Frontend | [architecture/frontend.md](architecture/frontend.md) |
| P | Convenciones de API | [api/conventions.md](api/conventions.md); P.4 en [product/screen-api-matrix.md](product/screen-api-matrix.md) |
| Q | Eventos | [architecture/module-communication.md](architecture/module-communication.md) |
| R, S | Segundo plano y webhooks | [architecture/background-processing.md](architecture/background-processing.md) |
| T | Tiempo real | [architecture/realtime.md](architecture/realtime.md) |
| U | Observabilidad y auditoría | [architecture/observability.md](architecture/observability.md) |
| V | Pruebas | [development/testing.md](development/testing.md) |
| W | Azure | [architecture/azure.md](architecture/azure.md) |
| X | ADRs | [adr/](adr/README.md) |
| Y | Roadmap | [roadmap/phases.md](roadmap/phases.md); Y.3 en [development/definition-of-done.md](development/definition-of-done.md) |
| Z | Orden de implementación | [roadmap/implementation-order.md](roadmap/implementation-order.md) |
| Anexo 1 | Revisión de la versión anterior | [roadmap/v1-review.md](roadmap/v1-review.md) |

## Diagramas

[Contexto](diagrams/system-context.md) · [Contenedores](diagrams/containers.md) · [Módulos](diagrams/modules.md) · [Dependencias](diagrams/dependencies.md) · [ERD](diagrams/erd.md) · [Transacción](diagrams/flow-transaction.md) · [Meta compartida](diagrams/flow-shared-goal.md) · [Gasto compartido](diagrams/flow-shared-expense.md) · [Webhook](diagrams/flow-webhook.md) · [Tiempo real](diagrams/flow-realtime.md)

## Contrato de la API

[api/openapi.v1.json](api/openapi.v1.json) se genera desde la API y se versiona. Un test falla si la API cambia sin actualizarlo, y los tipos del frontend se generan a partir de él.

## Estado

Fase 0 terminada, y rediseñada tras su revisión ([ADR-019](adr/ADR-019-simplificacion-de-la-plataforma.md)). El detalle de lo verificado y de lo que no está en [roadmap/phases.md](roadmap/phases.md).
