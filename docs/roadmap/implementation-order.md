# Orden de implementación

## Z. Orden recomendado de implementación

### Z.1 Secuencia

```text
F0 → F1 → F2 → F3 → F4 → F5 ─┬→ F6 → F7 → F8 → F9 ─┬→ F10 → F11 ─┬→ F12
                              │                      │             ├→ F13
                              └── H1                 └── H2        └→ F14
```

Las fases 12, 13 y 14 son independientes entre sí. La 10 solo depende de la 4 y puede adelantarse si se quiere cargar histórico pronto.

### Z.2 Dentro de cada fase

Porción vertical por caso de uso, en este orden:

1. Dominio y sus tests unitarios.
2. Handler y su test de integración.
3. Configuración EF en `Infrastructure/Persistence/Configurations/<Módulo>` y migración.
4. Controller en `Api/Controllers/<Módulo>`; regenerar `openapi.v1.json`.
5. Casos en la matriz de seguridad.
6. Regenerar el cliente y construir la pantalla con sus cuatro estados.
7. E2E del recorrido si es uno de los críticos.
8. Documentación del módulo.

### Z.3 Pasos de la Fase 0

Hecha. Se conserva como registro de qué hay montado.

1. Inicializar git en `PersonalHubV2` y crear `.gitignore` y `.editorconfig` (se copian de V1).
2. Crear `finanzas-hub-api/PersonalFinance.slnx`, `Directory.Build.props`, `Directory.Packages.props` y los dos BuildingBlocks.
3. Crear `PersonalFinance.Infrastructure` con `AppDbContext`, el pipeline de comandos (logging, transacción con eventos de dominio, idempotencia), auditoría y OpenTelemetry.
4. Crear `PersonalFinance.Api` (controllers, filtro de workspace, errores como `ProblemDetails`) y `PersonalFinance.Worker` (jobs).
5. Módulo `Sample` de punta a punta: comando idempotente, evento de dominio con su reacción y consulta filtrada por dueño. (Se eliminó en la fase 3; los tests de la plataforma usan desde entonces módulos reales.)
6. Crear los tres proyectos de tests con las reglas de V.2 y el fixture de Testcontainers.
7. Escribir `docker-compose.yml` con postgres, mailpit y aspire-dashboard.
8. Crear `finanzas-hub-web` con Angular 22, tokens del mock, shell responsive, `shared/ui` base e interceptores.
9. Volcar el análisis a `docs/`.
10. Verificar (ver el estado de la fase 0 en [phases.md](phases.md)).
