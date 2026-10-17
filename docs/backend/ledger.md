# Modelo financiero

## K. Modelo financiero / ledger

### K.1 Fuente de verdad

| Cifra | Única fuente | Cómo se obtiene |
|---|---|---|
| Saldo de una cuenta | `ledger.entries` | Suma de `signed_amount` de los asientos efectivos de la cuenta |
| Deuda y crédito disponible de tarjeta | `ledger.entries` y `credit_card_profiles` | Saldo; disponible = línea + saldo |
| Ingresos y gastos de un periodo | `ledger.operations` y `ledger.entries` | Suma por `kind` |
| Consumo de un presupuesto | Ledger | Consulta por categoría y periodo; nunca almacenado |
| Patrimonio | Ledger e Investments | Saldos de las cuentas que cuentan (K.13), convertidos a moneda base, más valor de mercado de posiciones |
| Progreso de una meta | `goals.goal_contributions` | Suma de aportes menos retiros no anulados |
| Deuda entre dos personas | `shared_expenses` | Partes menos asignaciones de liquidaciones |

**Sobre las dos últimas filas.** El aporte guarda su monto en Goals y además genera una operación en Ledger. No son dos fuentes para una misma cifra: el saldo sale solo del ledger y el progreso sale solo de los aportes. El monto se repite porque el aporte es un dato colaborativo que ven todos los participantes, mientras que la operación es privada de quien aportó; si Goals tuviera que leer el monto del ledger, tendría que saltarse el filtro de dueño. La coherencia se garantiza por construcción: ambos se crean en la misma transacción, ninguno es editable, y se anulan juntos. Una consulta de reconciliación en CI verifica que cada aporte vigente tiene una operación vigente del mismo monto. Lo mismo vale para las liquidaciones de gastos compartidos.

### K.2 Estrategia de saldo

| Opción | Descripción | Veredicto |
|---|---|---|
| **A. Derivado del ledger** | Sin columna de saldo; se suma al consultar | **Elegida** |
| B. Saldo almacenado + ledger + reconciliación | Columna `balance` actualizada en cada asiento | Descartada: puede divergir y exige maquinaria de reconciliación |
| C. Snapshots o proyecciones | Punto de control periódico más asientos posteriores | Reservada para cuando una medición lo pida |

Con ~10 usuarios y del orden de 10⁵ asientos, la suma sobre el índice `(account_id, occurred_on) INCLUDE (signed_amount) WHERE is_effective` es de milisegundos. El saldo inicial es un asiento `OpeningBalance` y corregir un saldo es un asiento `Adjustment` por la diferencia.

### K.3 Estructura

Una **operación** es el hecho de negocio que ve el usuario ("una transacción"). Tiene uno o más **asientos**, cada uno sobre una cuenta.

| Operación | Asientos |
|---|---|
| Ingreso | 1 entrada en la cuenta |
| Gasto | 1 salida de la cuenta |
| Reembolso | 1 entrada en la cuenta, con la categoría del gasto que compensa |
| Transferencia | 1 salida del origen y 1 entrada en el destino |
| Ajuste, saldo inicial | 1 entrada o salida |

El monto se guarda positivo con dirección explícita (`In` u `Out`), como decidió el ADR-0008 de V1, y una columna generada `signed_amount` sirve a las sumas. Se renombra `Debit/Credit` a `Out/In` porque V1 usaba esos términos con el sentido del extracto bancario, opuesto al contable. La regla es uniforme para toda cuenta: saldo = entradas − salidas. Una tarjeta tiene saldo negativo cuando hay deuda.

### K.4 Taxonomía de transacciones

En lugar de un enum con diez o más valores que crece con cada módulo, tres dimensiones independientes:

| Dimensión | Naturaleza | Valores |
|---|---|---|
| `kind` | Cerrada; mecánica contable | `Income`, `Expense`, `Refund`, `Transfer`, `Adjustment`, `OpeningBalance` |
| `source` | Abierta; la declara el módulo que origina | `(module, type, id)`, por ejemplo `goals/contribution/{id}`, `shared-expenses/settlement/{id}`, `investments/buy/{id}`, `cards/payment`, `scheduling/occurrence/{id}` |
| Tipo de cuenta | Del agregado `Account` | `Bank`, `Cash`, `Wallet`, `CreditCard` y de sistema `GoalFund`, `Investment` |

Los tipos del encargo se obtienen como vistas:

| Tipo pedido | Se expresa como |
|---|---|
| Income, Expense, Adjustment | `kind` |
| Transfer | `kind = Transfer` sin `source` |
| CreditCardPurchase | `Expense` sobre cuenta `CreditCard` |
| CreditCardPayment | `Transfer` con destino `CreditCard` |
| GoalContribution | `Transfer` con `source.module = goals` |
| SharedExpensePayment | `Expense` (deudor) o `Refund` (acreedor) con `source.module = shared-expenses` |
| InvestmentContribution, InvestmentWithdrawal | `Transfer` con `source.module = investments` |

Un módulo nuevo añade un `source` sin tocar Ledger. La API expone un campo calculado `displayType` para pestañas y filtros.

**Efecto de cada `kind`.**

| `kind` | Saldo | Reportes de ingreso y gasto | Presupuesto | Patrimonio |
|---|---|---|---|---|
| Income | + | Ingreso | — | + |
| Expense | − | Gasto | Consume | − |
| Refund | + | Resta al gasto de su categoría | Devuelve | + |
| Transfer | − y + | Neutro | — | 0, salvo diferencia de cambio |
| Adjustment | ± | Línea aparte "ajustes" | — | ± |
| OpeningBalance | ± | Excluido | — | ± |

### K.5 Estados y anulación

`Pending` → `Confirmed`; `Voided` desde cualquiera. Son efectivos `Pending` y `Confirmed` (OQ-05). No hay borrado físico: anular excluye la operación de toda suma y la deja visible en la auditoría con motivo y autor. Los estados `Cleared` y `Reconciled` de V1 se reintroducen con la conciliación de extractos (Fase 14).

Edición (OQ-22): campos descriptivos libres; monto, cuenta y fecha editables en sitio con antes y después en auditoría, salvo si la operación viene de otro módulo o está conciliada.

### K.6 Transferencias

Una operación `Transfer` con dos asientos. Patrimonio sin cambio. Se lista como una sola fila. Anularla anula ambos asientos a la vez. La comisión es una operación `Expense` vinculada. En multimoneda el usuario introduce los dos montos y el tipo de cambio implícito se guarda como dato; no hay conversión automática.

### K.7 Tarjetas de crédito

**Decisión: dentro de `Ledger`, como una porción propia, no como módulo.** La tarjeta es una cuenta del ledger con un `CreditCardProfile` 1:1.

| A favor de dejarla en Ledger | A favor de un módulo aparte |
|---|---|
| Compras, pagos, estado de cuenta e intereses leen y escriben asientos de la cuenta de forma continua | Tiene vocabulario propio (ciclo, corte, cuotas) |
| Patrimonio, transferencias y saldos funcionan igual para toda cuenta | — |
| Un módulo aparte necesitaría un contrato muy hablador con Ledger | — |

No se trata como cuenta bancaria normal: tiene línea, día de pago y de cierre, crédito disponible, estado de cuenta derivado por ciclo y planes de cuotas. El vocabulario propio se resuelve con la carpeta `Ledger/CreditCards` y sus tablas.

| Concepto | Modelo |
|---|---|
| Límite, día de corte, día de pago | `credit_card_profiles` |
| Saldo utilizado | Saldo de la cuenta |
| Crédito disponible | Línea + saldo |
| Compra | `Expense` sobre la tarjeta; cuenta para presupuesto en la fecha de compra |
| Pago | `Transfer` hacia la tarjeta |
| Estado de cuenta | Proyección por ciclo, no almacenada (Fase 14, se porta `CreditCardStatementBuilder`) |
| Cuotas | `InstallmentPlan` sobre la compra; la compra se reconoce entera; el interés se registra por ciclo y una sola vez (Fase 14, ADR-0013 de V1) |

En la fase 4 entraron el perfil básico, el disponible y el pago. El pago no lleva `source`: una operación con `source` solo la anula el módulo que la creó, y un pago de tarjeta debe poder anularse como cualquier movimiento; se reconoce porque su destino es una tarjeta. El aviso de vencimiento llega con Notifications (fase 6).

### K.8 Moneda

| Elemento | Decisión |
|---|---|
| `Currency` | Value object con código ISO 4217 y decimales; lista permitida configurable (inicial PEN y USD) |
| `Money` | Monto `decimal` y `Currency`; solo opera con la misma moneda |
| Moneda base | Por workspace; es la moneda de reportes y presupuestos |
| Cuenta | Moneda fija desde el primer asiento |
| Asiento | En la moneda de su cuenta. Columnas opcionales `base_amount`, `fx_rate`, `fx_rate_date` rellenadas al registrar cuando la moneda difiere de la base |
| `ExchangeRate` | `(base, moneda, rate, as_of)`, privada de cada persona (OQ-27): una fila por moneda y día, que vale desde ese día. Se mantiene a mano; una fuente automática se añade tras `IExchangeRateSource` (fase 14) |
| Reportes | Saldos y patrimonio: tipo de cambio más reciente. Ingresos y gastos: `base_amount` capturado al registrar o editar; un asiento sin tipo de cambio para su fecha se valora al más reciente. Una moneda sin ningún tipo de cambio queda fuera del total y la respuesta la nombra |
| Transferencia multimoneda | Dos montos explícitos |
| Metas | Totales por moneda, sin convertir (como el mock) |

Las columnas de conversión existen desde la primera migración para incorporar FX automático sin migrar datos.

### K.9 Consistencia, concurrencia, idempotencia, auditoría

| Tema | Decisión |
|---|---|
| Transacciones | Una transacción PostgreSQL por comando, nivel READ COMMITTED, que abarca todo lo que el comando y sus reacciones escriben, sea del módulo que sea |
| Concurrencia optimista | `xmin` como token en: `Workspace`, `Account`, `Operation`, `Budget`, `RecurringRule`, `Goal`, `SharedGroup`, `CategorizationRule`, `ImportSession`, `Holding`. Un choque entre dos escrituras simultáneas responde 409 con código `concurrency_conflict`. La API aún no expone la versión como `ETag` ni exige `If-Match` ([concurrency.md](concurrency.md)) |
| Dónde no hace falta | Aportes y asientos son inserciones; como el saldo y el progreso se derivan, dos aportes simultáneos no se pisan |
| Meta alcanzada | La transición `Active → Achieved` actualiza la fila de la meta bajo `xmin`; si dos aportes cruzan el objetivo a la vez, uno gana y el otro reintenta y no hace nada |
| Idempotencia de API | Cabecera `Idempotency-Key` obligatoria en POST con efecto financiero. Tabla `platform.idempotency_keys` con clave `(user_id, key)`, hash del request y respuesta guardada. Misma clave y mismo cuerpo devuelven la respuesta original; misma clave y otro cuerpo, 422 |
| Idempotencia entre módulos | Índice único sobre `(source_module, source_type, source_id)` en `ledger.operations` |
| Idempotencia de procesos | Claves naturales únicas: `(rule_id, scheduled_date)`, `(plan_id, number)`, `(provider, external_id)`, `(user_id, dedupe_key)`, `(message_id, consumer)` |
| Auditoría | `platform.audit_log` escrita en la misma transacción (sección U.4) |

### K.10 Flujo de una transacción

Diagrama: [Flujo de una transacción](../diagrams/flow-transaction.md).

### K.11 Flujo de aporte a una meta compartida

Caso del encargo: Brian aporta S/ 500 a "Viaje Japón" desde Cuenta BCP.

Diagrama: [Flujo de aporte a una meta compartida](../diagrams/flow-shared-goal.md).

Resultado frente a los seis puntos del encargo:

| Requisito | Cómo se cumple |
|---|---|
| Registrar la contribución | Fila en `goals.goal_contributions` |
| Registrar una salida financiera | Asiento `Out` de S/ 500 sobre BCP |
| Afectar el saldo de BCP | El saldo se deriva; baja S/ 500 |
| Generar la transacción | Operación `Transfer` visible en las transacciones de Brian |
| Reflejar el progreso | Suma de aportes; lo ven Brian y Juan |
| Trazabilidad | Aporte ↔ operación por id; `audit_log` con actor y `correlationId` |

El dinero sigue siendo de Brian (D3) y no consume presupuesto. Que su patrimonio mostrado cambie o no depende de si la cuenta que recibe cuenta en él (K.13): la cuenta de una meta nace fuera, así que el aporte lo baja hasta que Brian la incluya. La entrada va a su cuenta de ahorro vinculada si el dinero se mueve a una cuenta suya, o a un fondo de sistema a su nombre si sale hacia un lugar que la aplicación no registra.

### K.12 Flujo de pago de un gasto compartido

Caso del encargo: cena de S/ 300 entre Brian, Juan y Pedro, a partes iguales, pagada por Brian. Juan paga su parte.

Diagrama: [Flujo de pago de un gasto compartido](../diagrams/flow-shared-expense.md).

Cuando también pague Pedro, el gasto pasa a `Settled`. En los libros de Brian quedan un gasto de 300 y dos reembolsos de 100 en la misma categoría: gasto neto de 100, que es su parte. Cada asiento vive en el ledger de quien lo registra; nadie escribe en los libros de otro.

### K.13 Qué cuentas entran en el patrimonio

Decisión del dueño del 13-oct-2026 (OQ-26).

| Cuenta | ¿Cuenta en el patrimonio? |
|---|---|
| En uso (banco, efectivo, billetera, tarjeta) | Sí, salvo que la persona desmarque "Incluir en mi patrimonio" |
| Archivada | Nunca, tenga o no saldo, diga lo que diga su casilla |
| De una meta (`GoalFund`, fase 8) | Sí, salvo que la persona desmarque la casilla (desde el 16-oct-2026; antes no contaba, ver ADR-013) |

Nada financiero se borra, así que no existe la cuenta eliminada. La casilla es la columna `include_in_net_worth` de `accounts`: no cambia el saldo ni lo que se puede hacer con la cuenta, solo si suma en el total. Una tarjeta cuenta como deuda. Mover dinero a una cuenta que no cuenta baja el patrimonio mostrado; el dinero sigue en la cuenta.
