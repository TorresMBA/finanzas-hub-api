# ADR-010: Estado en el frontend

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

Estado mayormente de servidor por pantalla.

## Decisión

Signals con stores por feature; RxJS solo para flujos; sin NgRx.

## Alternativas consideradas

NgRx Store; NgRx SignalStore.

## Consecuencias

Menos ceremonia. SignalStore queda como evolución.
