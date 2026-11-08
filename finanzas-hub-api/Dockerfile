# Empaqueta una salida ya publicada de `dotnet publish` (no compila): la misma imagen sirve
# para el API (PersonalFinance.Api) y para el Worker (PersonalFinance.Worker).
# Contexto de build: la carpeta publicada (publish/api o publish/worker).
# Lo usa `mercury-ci package` del servidor Mercury, que lo prefiere a su plantilla por estar
# en el directorio desde el que se empaqueta. DOTNET_VERSION la fija el agente (dotnet-10.0 -> 10.0).
ARG DOTNET_VERSION=10.0
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}

WORKDIR /app
COPY . .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

# Solo el Worker tiene algo que comprobar desde dentro: cada job que termina bien toca este archivo, y los que corren
# cada 10 s lo mantienen al día. Más de 5 minutos sin tocarlo es un Worker atascado o sin base de datos.
# El API responde 200 sin más (su salud se ve desde fuera en /health/ready).
HEALTHCHECK --interval=1m --timeout=5s --start-period=2m --retries=3 \
  CMD sh -c 'ls PersonalFinance.Worker.dll >/dev/null 2>&1 || exit 0; [ -n "$(find /tmp/personal-hub-worker.alive -mmin -5 2>/dev/null)" ]'

# El ensamblado se deduce del único *.runtimeconfig.json de la carpeta (APP_DLL lo fija a mano).
# Los argumentos llegan a la aplicación: `docker run <imagen> migrate` aplica las migraciones y termina.
ENTRYPOINT ["sh", "-c", "exec dotnet \"${APP_DLL:-$(ls *.runtimeconfig.json | head -n 1 | sed 's/\\.runtimeconfig\\.json$/.dll/')}\" \"$@\"", "dotnet-app"]
