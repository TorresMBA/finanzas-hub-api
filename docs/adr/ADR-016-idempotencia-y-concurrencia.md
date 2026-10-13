# ADR-016: Idempotencia y concurrencia

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

Reintentos de red, dos usuarios sobre un recurso compartido.

## Decisión

`Idempotency-Key` en POST financieros; claves naturales únicas en procesos; `xmin` con `ETag` e `If-Match`.

## Alternativas consideradas

Bloqueo pesimista; sin control.

## Consecuencias

El cliente debe enviar la clave y manejar 409.
