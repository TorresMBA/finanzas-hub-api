# Buenas prácticas de DevOps

Reglas para construir, configurar, desplegar y operar PersonalHub. Valen para Mercury y para cualquier otro servidor.

## Construcción

- **Una imagen, muchos ambientes.** Se construye una vez y se promueve (dev → prod) con la misma etiqueta. La diferencia entre ambientes está en la configuración, nunca en el código ni en la imagen.
- **Etiquetas inmutables** (número de build o hash del commit); nunca `latest` en un despliegue.
- **Versiones fijadas**: paquetes NuGet en `Directory.Packages.props`, npm con `package-lock.json` y `npm ci`, herramientas en `dotnet-tools.json`, imágenes base con versión (`aspnet:10.0`, `postgres:17-alpine`, `nginx-unprivileged:1.30-alpine`).
- **Análisis en cada build**: SonarQube, Semgrep y Trivy (código e imagen). Un hallazgo se corrige o se documenta; no se desactiva la etapa.
- **Contenedores sin root** (`USER $APP_UID`, `nginx-unprivileged`) y sin herramientas que no hacen falta.

## Configuración y secretos

- **Doce factores**: configuración por variables de entorno, sin ficheros por ambiente dentro de la imagen ([configuration.md](configuration.md)).
- **Secretos fuera del repositorio** y con el mínimo acceso: `.env` del servidor con permisos de solo lectura para el despliegue, o el gestor de secretos de la plataforma ([secrets.md](secrets.md)).
- **Fallar al arrancar** si falta algo imprescindible (cadena de conexión, orígenes CORS), en vez de arrancar a medias.
- **Mínimo privilegio en la base**: un rol propio para la aplicación, no `postgres`.

## Despliegue

- **Copia de la base antes de migrar** en prod.
- **Migraciones antes que el código**, en un proceso aparte y de una en una. Si fallan, no se despliega.
- **Migraciones compatibles hacia atrás** siempre que se pueda (añadir antes de exigir, renombrar en dos pasos): permiten volver a la imagen anterior sin restaurar la base.
- **Readiness con `/health/ready`**, que también falla si faltan migraciones; liveness con `/health/live`.
- **Una instancia de API y una de Worker** hasta que se resuelva el escalado ([deployment.md §10](deployment.md#10-limitaciones-conocidas)).
- **Aprobación manual para prod** y un plan de vuelta atrás conocido antes de desplegar.
- **HTTPS siempre** fuera de la máquina local: la cookie de sesión lo exige.

## Operación

- **Observar antes de adivinar**: trazas, métricas y logs con `traceId` ([observability.md](observability.md)).
- **Logs sin datos personales ni secretos.**
- **Copias diarias, cifradas, fuera del servidor, y restauradas de prueba** de vez en cuando ([operations.md](operations.md)).
- **Actualizaciones de seguridad** del sistema operativo, Docker, PostgreSQL y las imágenes base de forma periódica; Trivy avisa de las de la aplicación.
- **Cada incidente deja algo**: un test, una alerta o un paso del pipeline que lo habría evitado.

## Lo que no hace falta (todavía)

Kubernetes, varias réplicas, colas externas, Redis, CDN o infraestructura como código completa no aportan nada con unos 10 usuarios y una persona que mantiene. La complejidad que se añade es complejidad que se opera ([ADR-019](../adr/ADR-019-simplificacion-de-la-plataforma.md)). Cuando haga falta crecer, el camino está en [azure.md](azure.md).
