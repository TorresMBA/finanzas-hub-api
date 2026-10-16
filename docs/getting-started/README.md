# Primeros pasos

Lo común a todo el equipo, sea cual sea el área en la que vayas a trabajar. Después sigue con la carpeta de tu área: [backend/](../backend/README.md), [frontend/](../frontend/README.md) o [devops/](../devops/README.md).

## Documentos

| Documento | Para qué |
|---|---|
| [onboarding.md](onboarding.md) | La guía técnica: tecnologías, paquetes, arquitectura, comunicación entre proyectos, patrones, base de datos y configuración, en resumen |
| [architecture-overview.md](architecture-overview.md) | La visión de arquitectura del sistema (resumen ejecutivo y diagramas generales) |
| [local-development.md](local-development.md) | Levantar el entorno en tu máquina, con o sin Docker |
| [workflow.md](workflow.md) | Cómo se lleva un cambio de la idea a producción: porción vertical, comprobaciones, git, pull request |
| [definition-of-done.md](definition-of-done.md) | Cuándo una feature está terminada |
| [glossary.md](glossary.md) | Términos del dominio y técnicos |
| [AGENTS.md](../../AGENTS.md) | Las reglas que no se negocian (privacidad, dinero, módulos, casos de uso) |

## Primera semana

| Día | Todos | Backend | Frontend | DevOps |
|---|---|---|---|---|
| 1 | [AGENTS.md](../../AGENTS.md), [onboarding.md](onboarding.md), [glossary.md](glossary.md); levantar el entorno con [local-development.md](local-development.md) y usar la aplicación: crear cuenta, cuentas, gastos, un presupuesto | | | |
| 2 | [workflow.md](workflow.md) | [backend/structure.md](../backend/structure.md), [backend/patterns.md](../backend/patterns.md) | [frontend/architecture.md](../frontend/architecture.md), [frontend/patterns.md](../frontend/patterns.md) | [devops/configuration.md](../devops/configuration.md), [devops/secrets.md](../devops/secrets.md) |
| 3 | | [backend/security.md](../backend/security.md), [backend/ledger.md](../backend/ledger.md) | [frontend/ui-design-system.md](../frontend/ui-design-system.md), [frontend/best-practices.md](../frontend/best-practices.md) | [devops/deployment.md](../devops/deployment.md) |
| 4 | | [backend/concurrency.md](../backend/concurrency.md), [backend/best-practices.md](../backend/best-practices.md); ejercicio de [backend/study-guide.md](../backend/study-guide.md#ejercicio-recomendado) | [frontend/testing.md](../frontend/testing.md); ejercicio de [frontend/study-guide.md](../frontend/study-guide.md#ejercicio-recomendado) | [devops/ci-cd.md](../devops/ci-cd.md), [devops/operations.md](../devops/operations.md); ejercicio de [devops/study-guide.md](../devops/study-guide.md#ejercicio-recomendado) |
| 5 | Un primer cambio pequeño siguiendo [workflow.md](workflow.md) | con [backend/recipes.md](../backend/recipes.md) | con [frontend/recipes.md](../frontend/recipes.md) | un ambiente local con Docker Compose |

## Lo que no puede salir mal

Por orden de prioridad, el mismo de [AGENTS.md](../../AGENTS.md):

1. **Corrección financiera**: `decimal`, moneda explícita, saldo derivado de los asientos, nada se borra.
2. **Privacidad**: lo privado de una persona no lo ve nadie más, ni siquiera el dueño del workspace; lo ajeno responde 404.
3. **Fronteras entre módulos**: solo `Contracts`, según el grafo.
4. **Integridad y auditoría**: una transacción por comando; cada acción de negocio auditada.
5. **Idempotencia**: lo que mueve dinero se puede reintentar sin duplicarse.
6. **Testabilidad y mantenibilidad**: lo simple primero, pensado para unos 10 usuarios y una persona que mantiene.
