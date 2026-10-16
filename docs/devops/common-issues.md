# Problemas frecuentes de entorno y despliegue

Síntoma, causa y solución. Los problemas de código están en [backend/common-bugs.md](../backend/common-bugs.md) y [frontend/common-bugs.md](../frontend/common-bugs.md).

## En local

| Síntoma | Causa | Solución |
|---|---|---|
| `ConnectionStrings:PersonalFinance is not configured` | Falta el user-secret | `dotnet user-secrets set "ConnectionStrings:PersonalFinance" "…" --project src/PersonalFinance.Api` ([configuration.md](configuration.md)) |
| `docker compose up` falla con el puerto 5432 ocupado | Ya hay un PostgreSQL instalado | Cambia `POSTGRES_PORT` en `.env` y el puerto de la cadena de conexión, o usa el PostgreSQL instalado |
| Líneas `fail:` en el primer arranque de la API | La base aún no existe y EF lo comprueba antes de crearla | Esperado: solo pasa la primera vez ([local-development.md](../getting-started/local-development.md)) |
| No llegan los correos de confirmación | El Worker no está en marcha | `dotnet run --project src/PersonalFinance.Worker`; sin SMTP, búscalos en `%TEMP%\personal-hub-mail` |
| Los tests de integración fallan al arrancar | No hay Docker ni `PERSONALFINANCE_TEST_POSTGRES` | Arranca Docker, o un clúster PostgreSQL desechable y la variable apuntándolo ([backend/testing.md](../backend/testing.md)) |
| `npm start` funciona pero `/api` devuelve 504 o error de proxy | La API no está en `:5166` | Arranca la API; el puerto está en `proxy.conf.json` y `launchSettings.json` |
| Playwright pide instalar navegadores | No hay Chromium de Playwright | `$env:PW_CHANNEL = 'chrome'` o `npx playwright install chromium` |
| Un comando largo de PowerShell muere a la mitad | `\| Select-Object -First N` cerró la tubería | Captura toda la salida y filtra después |
| `ng` no se reconoce | No hay Angular CLI global | `npx ng …` o los scripts de `npm` |
| Aviso de datos en `appsettings.json` al hacer commit | Se pegó una cadena con contraseña | Muévela a user-secrets y **rota la contraseña** si llegó a subirse ([secrets.md](secrets.md)) |

## En el CI

| Síntoma | Causa | Solución |
|---|---|---|
| El build falla por un aviso | `TreatWarningsAsErrors` | Corrige el aviso; no lo silencies |
| Quality gate rojo | Un problema nuevo, duplicación o un hotspot sin revisar en SonarQube | Revisa el informe del proyecto (filtro *New code*). Corrige el problema; si es un falso positivo, sigue [ci-cd.md § Quality gate y excepciones](ci-cd.md#quality-gate-y-excepciones-de-sonarqube) |
| Semgrep o Trivy paran el pipeline | Patrón inseguro o dependencia vulnerable | Corrige o actualiza el paquete; si es un falso positivo, documenta la excepción |
| `Falta /srv/mercury/apps/<env>/<app>.env` | Ambiente sin configurar | Crea el `.env` con las variables de [deployment.md §4](deployment.md#4-configuración-por-contenedor) |
| Pasa en el CI pero falla en local un test de integración (o al revés) | El CI no corre los de integración | Córrelos en local antes de subir |

## En el servidor

| Síntoma | Causa | Solución |
|---|---|---|
| La API no arranca: `Cors:AllowedOrigins must contain at least one HTTPS frontend origin` | Falta el origen o no es HTTPS | `Cors__AllowedOrigins__0=https://…` |
| 403 «Requests from this site are not allowed» al guardar | El origen del navegador no está permitido | La URL pública exacta del frontend en `Cors__AllowedOrigins__0` |
| La sesión no se mantiene | Sin HTTPS, el navegador descarta la cookie `Secure` | HTTPS en el proxy |
| 502 en `/api` desde la web | `API_UPSTREAM` incorrecto, API caída o, fuera de Docker, el `resolver 127.0.0.11` | Revisa `API_UPSTREAM` y [deployment.md §6.3](deployment.md#63-una-plataforma-de-contenedores-gestionada) |
| 413 al importar un archivo | Límite de tamaño del proxy | `client_max_body_size 11m` |
| El tiempo real no llega | El proxy no deja pasar el WebSocket o lo corta a los 60 s | Cabeceras `Upgrade`/`Connection` y `proxy_read_timeout 1h` |
| 429 en el login para todo el mundo | La API ve la IP del proxy, no la de cada persona | Limitación conocida ([deployment.md §10](deployment.md#10-limitaciones-conocidas)) |
| `/health/ready` falla tras desplegar | Migraciones sin aplicar o base inalcanzable | Revisa el paso de migraciones y la red hacia la base |
| El Worker registra errores cada minuto tras desplegar | Arrancó antes que la migración | Se resuelve solo cuando la migración termina |
| El contenedor se reinicia en bucle | Falta memoria (`APP_MEM_LIMIT`) o una variable obligatoria | `docker logs` y `docker inspect` (`OOMKilled`) |
| Todas las sesiones se cerraron tras restaurar | Las claves de Data Protection cambiaron con la copia | Esperado; cada persona vuelve a iniciar sesión |
