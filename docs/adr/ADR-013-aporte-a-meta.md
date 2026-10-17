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

## Actualización del 16-oct-2026

El dueño revirtió la parte de OQ-26 que tocaba a las metas: el fondo de una meta **cuenta en el patrimonio por defecto**, porque el dinero apartado sigue siendo de la persona. Vuelve la consecuencia original: aportar no cambia el patrimonio; lo baja gastar ese dinero. La casilla "Incluir en mi patrimonio" sigue existiendo para quien quiera sacar un fondo. La migración `GoalFundsInNetWorth` marcó como incluidos los fondos que ya existían.

Gastar lo ahorrado tiene su propia acción, «Usar el dinero» (`POST W/goals/{id}/spend`). En un solo paso retira del fondo a la cuenta con la que se paga (una transferencia, como cualquier retiro) y registra el gasto en esa cuenta. El gasto cuenta en el mes en que se hace, y lleva el origen `goals/spend` para que Reportes diga cuánto de lo gastado salió de una meta.
