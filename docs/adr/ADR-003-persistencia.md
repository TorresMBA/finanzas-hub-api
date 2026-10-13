# ADR-003: Persistencia

- Estado: sustituido por [ADR-019](ADR-019-simplificacion-de-la-plataforma.md)
- Fecha: 2026-10-12

## Contexto

Aislar datos por módulo sin perder transacciones.

## Decisión

`DbContext` y esquema por módulo; conexión y transacción compartidas por petición; sin FKs entre esquemas; migraciones por módulo.

## Alternativas consideradas

`DbContext` único con configuraciones por módulo; base por módulo.

## Consecuencias

Fontanería de transacción en BuildingBlocks. Integridad entre módulos por construcción y por reconciliación.
