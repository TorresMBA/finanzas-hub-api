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

# El ensamblado se deduce del único *.runtimeconfig.json de la carpeta (APP_DLL lo fija a mano).
# Los argumentos llegan a la aplicación: `docker run <imagen> migrate` aplica las migraciones y termina.
ENTRYPOINT ["sh", "-c", "exec dotnet \"${APP_DLL:-$(ls *.runtimeconfig.json | head -n 1 | sed 's/\\.runtimeconfig\\.json$/.dll/')}\" \"$@\"", "dotnet-app"]
