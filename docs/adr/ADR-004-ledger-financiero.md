# ADR-004: Ledger financiero

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

Evitar estados financieros duplicados.

## Decisión

Saldo siempre derivado; operación con asientos; saldo inicial y ajustes como asientos; taxonomía `kind` + `source` + tipo de cuenta.

## Alternativas consideradas

Saldo almacenado con reconciliación; snapshots; enum único de tipos.

## Consecuencias

Cada cifra tiene una fuente. Las lecturas suman; índice dedicado. Snapshots quedan como evolución.
