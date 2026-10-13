# ADR-005: Seguridad del workspace

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

Pertenecer al workspace no da acceso a lo financiero de otros (D2).

## Decisión

Tres niveles: usuario, workspace, recurso. Filtros globales por dueño y por participación. 404 para lo ajeno. Roles solo administrativos.

## Alternativas consideradas

Autorización por rol de workspace (V1); comprobaciones manuales en cada handler; RLS de PostgreSQL.

## Consecuencias

Olvidar un filtro no expone datos. Los jobs necesitan `ActorScope`. RLS queda como segunda barrera futura.
