# Definition of Done

Una feature está completa solo cuando:

- El dominio está implementado y sus invariantes tienen tests unitarios.
- Los casos de uso devuelven `Result` y tienen tests de integración contra PostgreSQL.
- La autorización de los tres niveles está implementada y tiene sus casos en la matriz de seguridad.
- Los comandos con efecto financiero implementan `IIdempotentCommand` y los agregados que editan varias personas llevan `xmin` como token de concurrencia ([concurrency.md](../backend/concurrency.md)).
- La auditoría funcional de sus acciones está escrita y probada.
- La migración está creada y aplicada en limpio y sobre la base anterior.
- El endpoint está en `openapi.v1.json` y el cliente TypeScript regenerado.
- Los errores salen como `ProblemDetails` con `code` estable.
- Hay span por caso de uso, logs estructurados y las métricas que apliquen.
- El frontend está integrado con estados de carga, vacío y error, en los tres breakpoints y en ambos temas.
- Los tests de arquitectura pasan.
- La documentación del módulo y los ADR afectados están actualizados.
- `docker compose up` deja la feature utilizable desde cero.
