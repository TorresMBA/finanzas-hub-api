# Glosario

Los términos que aparecen en el código, en la documentación y en la aplicación. Entre paréntesis, el nombre en el código cuando difiere.

## Dominio

| Término | Significado |
|---|---|
| **Workspace** (espacio) | Un directorio de personas y el contenedor de lo que comparten. Pertenecer a uno **no** da acceso a las finanzas de los demás miembros |
| **Espacio personal** (`IsPersonal`) | El workspace propio de cada persona, creado con su cuenta. Lo privado vive siempre aquí, tenga abierto el workspace que tenga ([ADR-021](../adr/ADR-021-finanzas-privadas-de-la-persona.md)) |
| **Miembro** (`Membership`) | Una persona en un workspace. Al expulsarla, la fila se conserva para que la historia siga atribuida |
| **Rol** (`WorkspaceRole`) | `Member`, `Admin` u `Owner`. Gobierna qué se puede hacer con el workspace (invitar, cambiar roles), nunca da acceso a datos privados ajenos |
| **Invitación** | Oferta para unirse a un workspace enviada por correo. Solo se guarda el hash de su token |
| **Onboarding** | Configurar moneda base y zona horaria de un workspace recién creado |
| **Moneda base** | La moneda en la que se expresan reportes, presupuestos y patrimonio del workspace |
| **Cuenta** (`Account`) | Donde está el dinero: banco, efectivo, billetera, tarjeta de crédito, inversión… En una moneda. No tiene columna de saldo |
| **Operación** (`Operation`) | El hecho que la persona ve como «una transacción»: ingreso, gasto, reembolso, transferencia, ajuste o saldo inicial (`OperationKind`). Estados `Pending`, `Confirmed`, `Voided` |
| **Asiento** (`Entry`) | El movimiento de una operación sobre una cuenta: importe positivo y dirección. El saldo de una cuenta es la suma de sus asientos |
| **Transferencia** | Dos asientos que se compensan entre cuentas propias. No es ingreso ni gasto |
| **Anular** (`Void`) | Dejar sin efecto una operación conservándola, con motivo y fecha. Nada financiero se borra |
| **Tipo de cambio** (`ExchangeRate`) | Cuánto vale una unidad de otra moneda en la moneda base desde un día. Lo mantiene la persona; valora saldos y reportes, nunca mueve dinero |
| **Patrimonio** (net worth) | Suma de las cuentas incluidas, convertidas a la moneda base. Las inversiones cuentan por su valor, sin doble conteo |
| **Tarjeta de crédito** (`CreditCardProfile`) | Una cuenta con línea de crédito, día de cierre y día de pago. Lo que se debe es su saldo negativo |
| **Categoría / subcategoría** | Clasificación de ingresos o gastos, privada de cada persona, en dos niveles |
| **Tag** | Etiqueta libre y privada que una operación puede llevar |
| **Comercio** (`MerchantName`) | El nombre normalizado del comercio de un gasto («Wong», «WONG » y «Wóng» son el mismo) |
| **Presupuesto** (`Budget`) | Límite mensual de una categoría raíz desde un mes en adelante. Lo gastado no se guarda: se lee del ledger |
| **Umbral de aviso** | 75, 90 o 100 % del límite: cuándo avisar a la persona |
| **Presupuesto fijo** | Un pago igual cada mes (alquiler): se juzga como pagado o por pagar, no por su ritmo |
| **Regla recurrente** (`RecurringRule`) | Un ingreso o gasto esperado en una fecha, una vez o cada mes o año |
| **Ocurrencia** (`Occurrence`) | Lo que pasó con una regla en una de sus fechas: registrada, confirmada, omitida |
| **Meta** (`Goal`) | Un monto a ahorrar para una fecha, de una persona o de varias. Se comparte por participación, no por pertenecer al workspace |
| **Participante** | Quien ahorra para una meta. Solo un participante activo la ve |
| **Aporte / retiro** (`GoalContribution`) | Dinero que un participante pone en la meta o saca. Cada uno es una transferencia del ledger |
| **Grupo de gastos** (`SharedGroup`) | Personas de un workspace que reparten gastos entre sí. Solo sus miembros activos lo ven |
| **Gasto compartido** (`SharedExpense`) | Algo que un miembro pagó por varios; se reparte en partes |
| **Parte** (`ExpenseShare`) | Lo que una persona debe de un gasto y cuánto ya devolvió |
| **Liquidación** (`Settlement`) | Un miembro devolviendo a otro lo que le debe; se reparte entre gastos (`SettlementAllocation`) |
| **Importación** (`ImportSession`) | Un archivo de movimientos (banco o Bluecoins) desde que se lee hasta que entra al ledger, con su revisión |
| **Regla de clasificación** (`CategorizationRule`) | «Si la descripción contiene esto, es de esta categoría» |
| **Inversión** (`Holding`) | Fondo, depósito a plazo, acciones, ETF, cripto u otra, en una institución |
| **Valorización** (`HoldingValuation`) | Cuánto valía una inversión un día |
| **Depósito a plazo** | Inversión con TEA y plazo; su valor crece solo (año de 360 días, como los bancos peruanos) |
| **TEA** | Tasa efectiva anual |
| **Aviso** (`Notification`) | Un mensaje en la bandeja de una persona; también puede salir por correo o WhatsApp según sus preferencias |
| **Canal** | Bandeja, correo o WhatsApp |
| **Periodo** | El mes elegido en la barra superior (o su trimestre o año en Reportes) |

## Técnica

| Término | Significado |
|---|---|
| **Módulo** | Un proyecto `PersonalFinance.Modules.X` con `Domain`, `Application` y `Contracts`. Hay 12 |
| **Contracts** | Lo único de un módulo que otro puede usar: interfaces de consulta, DTO y eventos |
| **`IXDb`** | La interfaz con los `DbSet` que un módulo puede ver (`ILedgerDb`, `IBudgetsDb`…) |
| **Agregado** (`AggregateRoot`) | La unidad que se carga, se valida y se guarda entera; la única puerta para cambiar lo que contiene |
| **Caso de uso** | Un comando (`ICommand<T>`, cambia estado) o una consulta (`IQuery<T>`, solo lee) con su handler |
| **Handler** | La clase que ejecuta un caso de uso y devuelve `Result<T>` |
| **Decorador** | Envoltorio de los handlers que añade logging, transacción e idempotencia |
| **`Result` / `Error`** | Resultado de un caso de uso; un fallo esperado lleva un `Error` con tipo y `code` estable |
| **Code** | Identificador estable de un error (`budgets.already_exists`); el frontend lo traduce |
| **Evento de dominio** | Algo que pasó en un agregado (`LedgerOperationChanged`); otros módulos reaccionan dentro de la misma transacción |
| **Reacción** | Un `IDomainEventHandler<T>` |
| **`IOwned`** | Marca de una entidad privada (`workspace_id` + `owner_user_id`); el filtro global la aísla |
| **Filtro global** | Condición que EF añade a toda consulta para que solo se vean las filas de la persona actual |
| **Actor** | En nombre de quién se ejecuta algo: la persona de la cookie o la que fija un job (`ActorContext`) |
| **Clave de idempotencia** | `Idempotency-Key`: hace que repetir un comando devuelva la primera respuesta en vez de aplicarlo dos veces |
| **`xmin`** | Columna de sistema de PostgreSQL usada como token de concurrencia optimista |
| **Cola en tabla** | Filas pendientes (`outgoing_emails`, `deliveries`) que un job procesa |
| **Worker** | El proceso sin HTTP que ejecuta los jobs |
| **Job** | Tarea periódica del Worker (`PeriodicJob`) |
| **Auditoría** | Registro de negocio de quién hizo qué y cuándo (`platform.audit_log`) |
| **ProblemDetails** | Formato estándar (RFC 9457) de las respuestas de error de la API |
| **Contrato OpenAPI** | `docs/api/openapi.v1.json`: la descripción de la API de la que se generan los tipos del frontend |
| **Signal** | Valor reactivo de Angular; lo que cambia y se pinta |
| **`httpResource`** | Lectura reactiva de Angular: se vuelve a pedir cuando cambia su URL |
| **Cuatro estados** | Carga, vacío, error y datos: los que implementa cada vista |
| **Porción vertical** | Una funcionalidad completa por todas las capas, de dominio a pantalla |
| **Fase** | Una entrega del roadmap ([roadmap/status.md](../roadmap/status.md)) |
| **ADR** | Architecture Decision Record: una decisión de arquitectura con su contexto y consecuencias ([adr/](../adr/README.md)) |
| **DoD** | Definition of Done: cuándo una feature está terminada ([definition-of-done.md](definition-of-done.md)) |
| **Mercury** | El servidor propio donde corren Jenkins y los ambientes dev y prod |
