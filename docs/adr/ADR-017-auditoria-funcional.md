# ADR-017: Auditoría funcional

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

Trazabilidad de operaciones financieras.

## Decisión

Tabla `audit_log` de solo inserción, escrita en la transacción, con antes y después; distinta del log técnico.

## Alternativas consideradas

Solo logs; captura automática de cambios de EF; event sourcing.

## Consecuencias

Escritura explícita por caso de uso. Alimenta la actividad de grupos.
