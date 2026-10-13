# ADR-012: Módulo Ledger unificado

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

Cuentas, asientos, transferencias y tarjetas comparten consistencia.

## Decisión

Un módulo `Ledger` con porciones internas.

## Alternativas consideradas

Cuatro módulos como en V1.

## Consecuencias

Módulo más grande; se controla con carpetas por porción.
