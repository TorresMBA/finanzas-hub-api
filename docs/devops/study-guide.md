# Guía de estudio de DevOps

Conceptos para construir, desplegar y operar PersonalHub con soltura, por prioridad.

## Imprescindible

| Concepto | Qué saber | Dónde se usa aquí | Para estudiar |
|---|---|---|---|
| Configuración en .NET | Proveedores, orden, `__` en variables de entorno, user-secrets, Options | [configuration.md](configuration.md) | [Configuración en .NET](https://learn.microsoft.com/dotnet/core/extensions/configuration), [User secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) |
| Docker | Imagen, capa, contenedor, volumen, red, `ENTRYPOINT` frente a `CMD`, usuarios sin root | `Dockerfile` de API/Worker y web | [Docker docs](https://docs.docker.com/get-started/) |
| Docker Compose | Servicios, redes, volúmenes, `.env`, `depends_on` con `healthcheck` | `finanzas-hub-api/docker-compose.yml`, [deployment.md §6.1](deployment.md#61-un-servidor-con-docker-compose-vps-máquina-propia) | [Compose](https://docs.docker.com/compose/) |
| Migraciones de EF Core en despliegue | Por qué fuera del arranque, compatibilidad hacia atrás | [deployment.md §5](deployment.md#5-orden-de-un-despliegue) | [Aplicar migraciones](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying) |
| HTTP detrás de un proxy | TLS, proxy inverso, cabeceras `X-Forwarded-*`, WebSockets | nginx de la imagen web | [ASP.NET Core detrás de un proxy](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer) |

## Importante

| Concepto | Qué saber | Dónde se usa aquí | Para estudiar |
|---|---|---|---|
| nginx | `location`, `proxy_pass`, `try_files` para un SPA, caché, `resolver` | `finanzas-hub-web/Dockerfile` | [nginx docs](https://nginx.org/en/docs/) |
| TLS y certificados | Let's Encrypt, renovación, HSTS | Proxy delante de la web | [Caddy](https://caddyserver.com/docs/automatic-https), [Certbot](https://certbot.eff.org/) |
| Jenkins declarativo | `pipeline`, `stages`, `agent`, `credentials()`, `input`, `parallel` | `Jenkinsfile` de cada repo, [ci-cd.md](ci-cd.md) | [Pipeline syntax](https://www.jenkins.io/doc/book/pipeline/syntax/) |
| PostgreSQL en operación | Roles y permisos, `pg_dump`/`pg_restore`, conexiones, espacio | [operations.md](operations.md) | [Backup and Restore](https://www.postgresql.org/docs/current/backup.html), [Roles](https://www.postgresql.org/docs/current/user-manag.html) |
| Health checks | Liveness frente a readiness | `/health/live`, `/health/ready` | [Health checks en ASP.NET Core](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks) |
| OpenTelemetry | Trazas, métricas, logs, OTLP, colector | [observability.md](observability.md) | [OpenTelemetry](https://opentelemetry.io/docs/), [Aspire Dashboard](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone) |

## Para profundizar

| Concepto | Qué saber | Dónde se usa aquí | Para estudiar |
|---|---|---|---|
| Seguridad de la cadena de suministro | SAST, análisis de dependencias e imágenes | Semgrep, Trivy, SonarQube | [Trivy](https://trivy.dev/), [Semgrep](https://semgrep.dev/docs/), [SonarQube](https://docs.sonarsource.com/sonarqube/) |
| OWASP de configuración | Cabeceras de seguridad, CORS, cookies, secretos | `HttpSetup.cs`, [secrets.md](secrets.md) | [OWASP Cheat Sheets](https://cheatsheetseries.owasp.org/) |
| The Twelve-Factor App | Configuración, procesos sin estado, paridad entre ambientes | Todo el despliegue | [12factor.net](https://12factor.net/es/) |
| Plataformas gestionadas | Contenedores con réplicas mínimas y máximas, jobs, secretos | [deployment.md §6.3](deployment.md#63-una-plataforma-de-contenedores-gestionada), [azure.md](azure.md) | La documentación de la plataforma elegida |

## Ejercicio recomendado

Levanta el escenario de [deployment.md §6.1](deployment.md#61-un-servidor-con-docker-compose-vps-máquina-propia) en tu máquina (con `localhost` y sin Caddy, publicando el puerto 8080 de `web`): construye las tres imágenes, aplica las migraciones con `docker compose run --rm api migrate`, arranca todo y recorre la lista de comprobaciones de [deployment.md §8](deployment.md#8-comprobar-un-despliegue). Después rompe algo a propósito (quita `API_UPSTREAM`, o `Cors__AllowedOrigins__0`) y busca el síntoma en [common-issues.md](common-issues.md).
