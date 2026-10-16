# Documentación de PersonalHub

PersonalHub es una plataforma de finanzas personales y colaborativas: API en .NET 10 (monolito modular sobre PostgreSQL) y SPA en Angular 22. Pensada para unos 10 usuarios y una persona que la mantiene: se prefiere lo simple.

## Organización

```text
docs/
  getting-started/   lo común: guía técnica, entorno local, flujo de trabajo, glosario, Definition of Done
  backend/           API, Worker, módulos, base de datos: patrones, concurrencia, buenas prácticas, bugs, recetas
  frontend/          Angular: arquitectura, patrones, sistema de diseño, tests, bugs, recetas
  devops/            configuración, secretos, CI/CD, despliegue, observabilidad, operación
  modules/           un documento por módulo de negocio
  adr/               decisiones de arquitectura
  diagrams/          contexto, contenedores, módulos, ERD y flujos
  product/           requisitos, análisis de los mocks, decisiones abiertas
  roadmap/           fases, orden de implementación y estado
  api/               openapi.v1.json, el contrato generado de la API
```

Cada carpeta de área tiene un `README.md` con el orden de lectura, y además: **patrones**, **buenas prácticas**, **bugs frecuentes**, **recetas** paso a paso y una **guía de estudio** con los conceptos que conviene dominar.

## Por dónde empezar

| Si eres o vas a… | Empieza por |
|---|---|
| Recién llegado al equipo | [getting-started/README.md](getting-started/README.md): plan de la primera semana |
| Trabajar en el backend | [backend/README.md](backend/README.md) |
| Trabajar en el frontend | [frontend/README.md](frontend/README.md) |
| Desplegar u operar | [devops/README.md](devops/README.md) |
| Levantar el entorno | [getting-started/local-development.md](getting-started/local-development.md) |
| Llevar un cambio a producción | [getting-started/workflow.md](getting-started/workflow.md) |
| Saber qué fases están hechas y qué falta | [roadmap/status.md](roadmap/status.md) |
| Implementar una fase | [roadmap/phases.md](roadmap/phases.md) y el documento del módulo en [modules/](modules/) |
| Saber por qué la plataforma es así de simple | [adr/ADR-019](adr/ADR-019-simplificacion-de-la-plataforma.md) |

## Por tema

| Tema | Documento |
|---|---|
| Visión general de la arquitectura | [getting-started/architecture-overview.md](getting-started/architecture-overview.md) |
| Tecnologías, paquetes, cómo se comunican los proyectos | [getting-started/onboarding.md](getting-started/onboarding.md) |
| Añadir un módulo o un caso de uso | [backend/recipes.md](backend/recipes.md), [backend/structure.md](backend/structure.md) |
| Patrones de diseño | [backend/patterns.md](backend/patterns.md), [frontend/patterns.md](frontend/patterns.md) |
| Concurrencia, idempotencia y transacciones | [backend/concurrency.md](backend/concurrency.md) |
| Tocar dinero | [backend/ledger.md](backend/ledger.md) |
| Tocar permisos o consultas | [backend/security.md](backend/security.md) |
| Cambiar la API | [backend/api-conventions.md](backend/api-conventions.md) |
| Tablas, índices, relaciones y ER | [backend/database.md](backend/database.md) |
| Cambiar la base de datos | [backend/data-model.md](backend/data-model.md), [backend/migrations.md](backend/migrations.md) |
| Sistema de diseño | [frontend/ui-design-system.md](frontend/ui-design-system.md) |
| Configuración y variables de entorno | [devops/configuration.md](devops/configuration.md) |
| Secretos (cadena de conexión, SMTP, WhatsApp, CI) | [devops/secrets.md](devops/secrets.md) |
| Desplegar en Mercury o en otro servidor | [devops/deployment.md](devops/deployment.md) |
| Pipelines | [devops/ci-cd.md](devops/ci-cd.md) |
| Dar algo por terminado | [getting-started/definition-of-done.md](getting-started/definition-of-done.md) |

## Mapa de secciones

Los documentos nacieron de un único análisis y conservan sus letras de sección, porque se citan entre sí ("sección K.9", "J.2", "OQ-05").

| Sección | Tema | Documento |
|---|---|---|
| A | Resumen ejecutivo | [getting-started/architecture-overview.md](getting-started/architecture-overview.md) |
| B | Análisis de los mocks | [product/mock-analysis.md](product/mock-analysis.md) |
| C, D | Requisitos funcionales y no funcionales | [product/requirements.md](product/requirements.md) |
| E | Decisiones pendientes (OQ-01 a OQ-25) | [product/open-questions.md](product/open-questions.md) |
| F, G | Arquitectura y diagramas generales | [getting-started/architecture-overview.md](getting-started/architecture-overview.md) |
| H | Límites de módulos | [backend/modules.md](backend/modules.md) y [modules/](modules/) |
| I | Reglas de dependencia | [backend/module-communication.md](backend/module-communication.md) |
| J | Seguridad | [backend/security.md](backend/security.md) |
| K | Modelo financiero | [backend/ledger.md](backend/ledger.md) |
| L | Modelo de datos | [backend/data-model.md](backend/data-model.md) |
| M | ERD | [diagrams/erd.md](diagrams/erd.md) |
| N | Estructura del backend | [backend/structure.md](backend/structure.md) |
| O | Frontend | [frontend/architecture.md](frontend/architecture.md) |
| P | Convenciones de API | [backend/api-conventions.md](backend/api-conventions.md); P.4 en [product/screen-api-matrix.md](product/screen-api-matrix.md) |
| Q | Eventos | [backend/module-communication.md](backend/module-communication.md) |
| R, S | Segundo plano y webhooks | [backend/background-processing.md](backend/background-processing.md) |
| T | Tiempo real | [backend/realtime.md](backend/realtime.md) |
| U | Observabilidad y auditoría | [devops/observability.md](devops/observability.md) |
| V | Pruebas | [backend/testing.md](backend/testing.md) |
| W | Azure | [devops/azure.md](devops/azure.md) |
| X | ADRs | [adr/](adr/README.md) |
| Y | Roadmap | [roadmap/phases.md](roadmap/phases.md); Y.3 en [getting-started/definition-of-done.md](getting-started/definition-of-done.md) |
| Z | Orden de implementación | [roadmap/implementation-order.md](roadmap/implementation-order.md) |
| Anexo 1 | Revisión de la versión anterior | [roadmap/v1-review.md](roadmap/v1-review.md) |

## Diagramas

[Contexto](diagrams/system-context.md) · [Contenedores](diagrams/containers.md) · [Módulos](diagrams/modules.md) · [Dependencias](diagrams/dependencies.md) · [ERD](diagrams/erd.md) · [Transacción](diagrams/flow-transaction.md) · [Meta compartida](diagrams/flow-shared-goal.md) · [Gasto compartido](diagrams/flow-shared-expense.md) · [Webhook](diagrams/flow-webhook.md) · [Tiempo real](diagrams/flow-realtime.md)

## Contrato de la API

[api/openapi.v1.json](api/openapi.v1.json) se genera desde la API y se versiona. Un test falla si la API cambia sin actualizarlo, y los tipos del frontend se generan a partir de él.

## Estado

Fases 0 a 3 terminadas; el tablero con el estado de cada fase y lo que quedó pendiente está en [roadmap/status.md](roadmap/status.md). La fase 0 se rediseñó tras su revisión ([ADR-019](adr/ADR-019-simplificacion-de-la-plataforma.md)). El detalle de lo verificado y de lo que no está en [roadmap/phases.md](roadmap/phases.md).
