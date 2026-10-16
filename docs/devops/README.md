# DevOps

Construir, configurar, desplegar y operar PersonalHub: en local, en Mercury (el servidor propio) y en cualquier otro servidor o plataforma.

## Mapa en 30 segundos

```text
push ─► Jenkins (por repo) ─► build + tests + SonarQube/Semgrep/Trivy ─► imágenes (api, worker, web)
                                   │
                                   └─► dev: migraciones ─► api ─► worker ─► web   ─► [aprobación] ─► prod (mismas imágenes)

Navegador ─HTTPS─► proxy TLS ─► web (nginx :8080) ─► /api /hubs /webhooks ─► api (:8080) ─► PostgreSQL
                                                                       worker ─────────────┘   └─► SMTP, WhatsApp
Configuración: variables de entorno (.env del servidor)  ·  Secretos: fuera del repo  ·  Telemetría: OTLP
```

## Orden de lectura

| # | Documento | Para qué |
|---|---|---|
| 1 | [configuration.md](configuration.md) | De dónde lee cada parte su configuración; todas las claves |
| 2 | [secrets.md](secrets.md) | Qué es secreto, dónde vive en cada entorno, cómo se rota |
| 3 | [deployment.md](deployment.md) | Qué se despliega, requisitos, Docker Compose, systemd, plataformas gestionadas y Mercury |
| 4 | [ci-cd.md](ci-cd.md) | Los pipelines de Jenkins y cómo cambiarlos |
| 5 | [observability.md](observability.md) | Logs, trazas, métricas, auditoría y qué mirar |
| 6 | [operations.md](operations.md) | Copias, migraciones a mano, purgas, incidentes |
| 7 | [best-practices.md](best-practices.md) | Reglas para construir, desplegar y operar |
| 8 | [common-issues.md](common-issues.md) | Problemas frecuentes en local, en el CI y en el servidor |
| — | [study-guide.md](study-guide.md) | Conceptos que conviene estudiar |
| — | [azure.md](azure.md) | Plan de evolución en Azure si un día hace falta escalar |

## Si vas a…

| Tarea | Lee |
|---|---|
| Levantar el entorno local | [getting-started/local-development.md](../getting-started/local-development.md) |
| Añadir una variable de configuración | [configuration.md § Añadir una clave](configuration.md#añadir-una-clave) |
| Guardar o rotar un secreto | [secrets.md](secrets.md#rotar-un-secreto) |
| Desplegar en un servidor nuevo | [deployment.md](deployment.md) |
| Crear un ambiente en Mercury | [ci-cd.md § Cambiar un pipeline](ci-cd.md#cambiar-un-pipeline), [deployment.md §11](deployment.md#11-cómo-se-despliega-hoy-mercury) |
| Investigar un error en producción | [observability.md § Operación](observability.md#operación-qué-mirar), [operations.md § Ante un incidente](operations.md#ante-un-incidente) |
| Hacer o restaurar una copia | [operations.md](operations.md#copias-de-seguridad) |

## Archivos de infraestructura en el repositorio

| Archivo | Qué es |
|---|---|
| `finanzas-hub-api/docker-compose.yml` | Infraestructura **local**: PostgreSQL, Mailpit y Aspire Dashboard |
| `finanzas-hub-api/.env.example` | Plantilla del `.env` local de `docker compose` |
| `finanzas-hub-api/Dockerfile` | Imagen de API y Worker sobre una carpeta ya publicada |
| `finanzas-hub-api/Jenkinsfile` | Pipeline del backend |
| `finanzas-hub-api/tools/*.ps1`, `update-db-docs.mjs` | Migraciones, contrato OpenAPI y documentación de la base |
| `finanzas-hub-web/Dockerfile` | Imagen nginx del SPA con el proxy a la API |
| `finanzas-hub-web/Jenkinsfile` | Pipeline del frontend |
| `finanzas-hub-web/proxy.conf.json` | Proxy del servidor de desarrollo |
