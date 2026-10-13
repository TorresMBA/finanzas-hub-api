# ADR-014: Contabilidad de gastos compartidos

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

Evitar inflar ingresos y gastos.

## Decisión

Base de caja: el pagador registra el gasto completo y recibe reembolsos como `Refund` de la misma categoría; el deudor registra un gasto al pagar; cada lado en su propio ledger.

## Alternativas consideradas

Ingreso "Reembolsos"; cuenta por cobrar; devengo con cuenta por pagar.

## Consecuencias

Gasto neto correcto tras cobrar. Mientras no cobre, el presupuesto del pagador muestra el total.
