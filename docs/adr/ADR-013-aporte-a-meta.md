# ADR-013: Aporte a meta

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

D3.

## Decisión

Transferencia desde la cuenta origen a una cuenta vinculada o a un fondo de sistema del participante; el aporte guarda monto propio enlazado 1:1 a la operación.

## Alternativas consideradas

Salida sin contrapartida; apartado sin movimiento en el ledger.

## Consecuencias

Patrimonio intacto; no consume presupuesto. El saldo en la aplicación de la cuenta origen baja.

## Actualización del 13-oct-2026

El dueño decidió (OQ-26) que la cuenta de una meta no cuenta en el patrimonio salvo que la persona marque "Incluir en mi patrimonio" en ella. La decisión de este ADR no cambia: el aporte sigue siendo una transferencia y no un gasto. Cambia su primera consecuencia: el patrimonio mostrado baja con el aporte mientras la cuenta de la meta esté fuera, y queda intacto si la persona la incluye.
