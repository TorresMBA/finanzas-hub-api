# Modelo de datos

El diagrama entidad-relación está en [diagrams/erd.md](../diagrams/erd.md).

## L. Modelo de datos

El modelo sale del dominio de las secciones H y K, no de los DTO.

### L.1 Convenciones

| Tema | Convención |
|---|---|
| Motor | PostgreSQL 17, un esquema por módulo más `platform`, mapeados por un único `AppDbContext` |
| Nombres | `snake_case`, tablas en plural |
| Claves | `uuid` versión 7 generado en la aplicación (`Guid.CreateVersion7()`) |
| Dinero | `numeric(18,2)`; cantidades de inversión `numeric(28,10)`; precios y tipos de cambio `numeric(18,8)` |
| Moneda | `char(3)` |
| Fechas | Fecha de negocio `date` (`occurred_on`), en la zona del workspace; instantes `timestamptz` |
| Enumerados | `text` con `CHECK` |
| Concurrencia | `xmin` en los agregados listados en K.9 |
| Auditoría mínima de fila | `created_at` siempre; `updated_at` en lo mutable; `created_by_user_id` donde el autor importa |
| FKs | Declaradas dentro del esquema. Entre esquemas se guarda el id sin propiedad de navegación; la restricción en base de datos se declara cuando protege un enlace de dinero |
| Borrado | En cascada solo de hijo a raíz dentro del mismo agregado |

En V1 la fecha era `DateTimeOffset OccurredAt` y hubo que añadir `WorkspaceCalendar` para calcular límites de mes. Con `date` una compra del 12 de octubre no cambia de día por la zona horaria.

### L.2 Tablas por esquema

Columnas de acceso: **P** privado `(workspace_id, owner_user_id)`, **C** colaborativo (participantes), **W** del workspace, **U** personal del usuario, **R** referencia, **S** sistema.

**`identity`**

| Tabla | Clave y relaciones | Únicos, índices y checks | Acceso |
|---|---|---|---|
| `users` | PK `id` | UNIQUE `normalized_user_name` (el correo es el nombre de usuario); índice `normalized_email`. Columnas de ASP.NET Core Identity más `display_name` y `created_at` | U |
| `user_preferences` | PK y FK `user_id` → `users` | `week_start IN (Monday, Sunday)`; `savings_rate_target` entre 0 y 100 | U |
| Tablas de ASP.NET Identity (`user_tokens`, `user_logins`, `user_claims`) | FK → `users` | Las del framework. Sin tablas de roles | U |

**`workspaces`**

| Tabla | Clave y relaciones | Únicos, índices y checks | Acceso |
|---|---|---|---|
| `workspaces` | PK `id`; ref `owner_user_id` | `status IN (PendingSetup, Active, Archived)`; `base_currency` no nulo si `status = Active` | W |
| `memberships` | PK `id`; FK `workspace_id`; ref `user_id` | UNIQUE `(workspace_id, user_id)`; índice `(user_id) WHERE status = Active`. Que haya un solo Owner lo garantiza el agregado | W |
| `invitations` | PK `id`; FK `workspace_id` | UNIQUE `token_hash`; índice `(workspace_id, status)`; `role IN (Member, Admin)`. Que un correo no tenga dos invitaciones abiertas lo comprueba el caso de uso | W |

**`notifications`**

| Tabla | Clave y relaciones | Únicos, índices y checks | Acceso |
|---|---|---|---|
| `notifications` | PK `id`; ref `user_id`, `workspace_id` | UNIQUE `(user_id, dedupe_key)`; índice `(user_id, created_at DESC)`; parcial `(user_id) WHERE read_at IS NULL` | U |
| `notification_preferences` | PK `(user_id, category, channel)` | `channel IN (InApp, Email, WhatsApp, Push)` | U |
| `notification_settings` | PK `user_id` | Horas de silencio; `large_movement_threshold ≥ 0` | U |
| `channel_endpoints` | PK `id`; ref `user_id` | Único parcial `(channel, address) WHERE verified_at IS NOT NULL` | U |
| `deliveries` | PK `id`; FK `notification_id` | UNIQUE `(notification_id, channel)`; índice `(status, next_attempt_at)` | S |

**`categories`**

| Tabla | Clave y relaciones | Únicos, índices y checks | Acceso |
|---|---|---|---|
| `categories` | PK `id`; FK `parent_id` → `categories` | Único parcial `(workspace_id, owner_user_id, parent_id, name_key) NULLS NOT DISTINCT WHERE archived_at IS NULL`, donde `name_key` es el nombre en minúsculas; único parcial `(workspace_id, owner_user_id, system_key) WHERE system_key IS NOT NULL`; `kind IN (Income, Expense)`; `icon` y `color` solo en las de primer nivel | P |
| `tags` | PK `id` | UNIQUE `(workspace_id, owner_user_id, name)` | P |
| `category_tags` | PK `(category_id, tag_id)`; FK a ambas | Solo categorías de primer nivel (regla del dominio) | P por raíz |

**`ledger`**

| Tabla | Clave y relaciones | Únicos, índices y checks | Acceso |
|---|---|---|---|
| `accounts` | PK `id` | Único parcial `(workspace_id, owner_user_id, lower(name)) WHERE status = Active`; único parcial `(owner_user_id, linked_resource_type, linked_resource_id) WHERE is_system`; `type IN (...)`; `status IN (Active, Archived)` | P |
| `credit_card_profiles` | PK y FK `account_id` → `accounts` | `credit_limit > 0`; días entre 1 y 31 | P por raíz |
| `operations` | PK `id`; ref `category_id` | Índice `(workspace_id, owner_user_id, occurred_on DESC, id DESC)`; índice `(owner_user_id, category_id, occurred_on)`; único parcial `(source_module, source_type, source_id) WHERE source_id IS NOT NULL`; `kind` y `status` con CHECK; categoría obligatoria si `kind IN (Income, Expense, Refund)` | P |
| `entries` | PK `id`; FK `operation_id` → `operations` (cascada); FK `account_id` → `accounts` | `amount > 0`; `direction IN (In, Out)`; `signed_amount` generada; índice `(account_id, occurred_on) INCLUDE (signed_amount) WHERE is_effective` | P |
| `operation_tags` | PK `(operation_id, tag_id)`; FK `operation_id`; ref `tag_id` | Índice `(tag_id)` | P por raíz |
| `installment_plans` (Fase 14) | PK `id`; FK `purchase_operation_id`, `account_id` | `months` entre 2 y 48; índice `(account_id, status)` | P |
| `installment_charges` (Fase 14) | PK `(plan_id, number)`; FK `plan_id`, `operation_id` | UNIQUE `operation_id` | P por raíz |
| `exchange_rates` | PK `id` | UNIQUE `(base_currency, quote_currency, as_of, source)`; `rate > 0` | R |

**`scheduling`**

| Tabla | Clave y relaciones | Únicos, índices y checks | Acceso |
|---|---|---|---|
| `recurring_rules` | PK `id`; ref cuentas y categoría | Índice `(workspace_id, owner_user_id, status)`; `amount > 0`; `frequency`, `posting_mode`, `status` con CHECK | P |
| `occurrences` | PK `id`; FK `rule_id` | UNIQUE `(rule_id, scheduled_date)`; único parcial `ledger_operation_id` | P por raíz |

**`budgets`**

| Tabla | Clave y relaciones | Únicos, índices y checks | Acceso |
|---|---|---|---|
| `budgets` | PK `id`; ref `category_id` | UNIQUE `(workspace_id, owner_user_id, category_id, effective_from)`; `amount > 0`; `alert_threshold_percent IN (75, 90, 100)`; `ends_on ≥ effective_from` | P |
| `budget_alerts` | PK `(budget_id, period_start, threshold)`; FK `budget_id` | — | S |

**`goals`**

| Tabla | Clave y relaciones | Únicos, índices y checks | Acceso |
|---|---|---|---|
| `goals` | PK `id` | Índice `(workspace_id, status)`; `target_amount > 0`; `status IN (Active, Achieved, Cancelled, Archived)` | C |
| `goal_participants` | PK `id`; FK `goal_id`; ref `user_id`, `linked_account_id`, `fund_account_id` | UNIQUE `(goal_id, user_id)`; índice `(user_id, status)`; único parcial `(goal_id) WHERE role = Owner AND status = Active` | C |
| `goal_contributions` | PK `id`; FK `goal_id`, `participant_id`; ref `ledger_operation_id` | UNIQUE `ledger_operation_id`; índice `(goal_id, occurred_on)`; `amount > 0`; `type IN (Contribution, Withdrawal)` | C |

**`shared_expenses`**

| Tabla | Clave y relaciones | Únicos, índices y checks | Acceso |
|---|---|---|---|
| `shared_groups` | PK `id` | Índice `(workspace_id, status)` | C |
| `group_members` | PK `id`; FK `group_id`; ref `user_id` | Único parcial `(group_id, user_id) WHERE user_id IS NOT NULL`; índice `(user_id, status)` | C |
| `shared_expenses` | PK `id`; FK `group_id`, `paid_by_member_id`; ref `payer_operation_id` | Índice `(group_id, occurred_on DESC)`; `total_amount > 0`; `status` y `split_type` con CHECK | C |
| `expense_shares` | PK `id`; FK `expense_id` (cascada), `member_id` | UNIQUE `(expense_id, member_id)`; `amount > 0`; `0 ≤ paid_amount ≤ amount` | C por raíz |
| `settlements` | PK `id`; FK `group_id`, `from_member_id`, `to_member_id`; ref operaciones | Únicos parciales en `payer_operation_id` y `payee_operation_id`; `amount > 0`; `from_member_id <> to_member_id` | C |
| `settlement_allocations` | PK `(settlement_id, share_id)`; FK a ambas | `amount > 0` | C por raíz |

**`integrations`**

| Tabla | Clave y relaciones | Únicos, índices y checks | Acceso |
|---|---|---|---|
| `import_sessions` | PK `id`; ref `target_account_id` | Único parcial `(owner_user_id, file_hash) WHERE status <> Discarded`; `status` con CHECK | P |
| `import_records` | PK `id`; FK `session_id` (cascada); ref `ledger_operation_id` | UNIQUE `(session_id, line_number)`; índice `(fingerprint)` | P por raíz |
| `categorization_rules` | PK `id`; ref `category_id` | Índice `(workspace_id, owner_user_id, enabled, priority)` | P |

**`investments`**

| Tabla | Clave y relaciones | Únicos, índices y checks | Acceso |
|---|---|---|---|
| `assets` | PK `id` | Único parcial `(symbol) WHERE owner_user_id IS NULL`; `type` con CHECK | R si es global, P si es propio |
| `holdings` | PK `id`; FK `asset_id`; ref `ledger_account_id` | Índice `(workspace_id, owner_user_id, status)` | P |
| `investment_transactions` | PK `id`; FK `holding_id`; ref `ledger_operation_id` | Único parcial `ledger_operation_id`; `type` con CHECK | P por raíz |
| `price_snapshots` | PK `(asset_id, as_of, source)`; FK `asset_id` | `price > 0` | R |
| `holding_valuations` | PK `(holding_id, as_of)`; FK `holding_id` | `value ≥ 0` | P por raíz |

**`platform`**

| Tabla | Clave | Únicos e índices | Uso |
|---|---|---|---|
| `idempotency_keys` | PK `(user_id, key)` | Índice `(expires_at)` | Reintentos de API |
| `audit_log` | PK `id` | Índices `(resource_type, resource_id, occurred_at)` y `(scope_type, scope_id, occurred_at)` | Auditoría funcional; el rol de la aplicación solo tiene INSERT y SELECT |
| `outgoing_emails` | PK `id` | Índice `(next_attempt_at) WHERE sent_at IS NULL` | Cola de correos; el cuerpo se borra al enviar |
| `data_protection_keys` | PK `id` | — | Claves que protegen la cookie de sesión y los tokens de los correos |

### L.3 Semántica de eliminación

No hay un `is_deleted` universal. Cada entidad usa el verbo de su dominio.

| Entidad | Operación | Borrado físico |
|---|---|---|
| Usuario | Desactivar | Nunca; anonimizar a petición |
| Workspace | Archivar | Diferido |
| Membresía | `Removed` | Nunca |
| Invitación | Revocar o caducar | Purga de antiguas |
| Cuenta | Archivar | Solo si no tiene asientos |
| Operación, transferencia | Anular (`Voided`) | Nunca |
| Categoría | Archivar | Nunca si está referenciada |
| Tag | Archivar | Solo si no se usa |
| Presupuesto | Terminar (`ends_on`) | Permitido si no cubre ningún mes transcurrido |
| Meta | Archivar o cancelar | Nunca si tiene aportes |
| Aporte, liquidación | Anular | Nunca |
| Gasto compartido | Cancelar | Solo en `Draft` |
| Grupo | Archivar | Nunca si tuvo gastos |
| Regla recurrente | Pausar o terminar | Solo si no tiene ocurrencias |
| Ocurrencia | Omitir (`Skipped`) | Nunca |
| Posición de inversión | Cerrar | Nunca si tuvo operaciones |
| Sesión de importación | Descartar | Sí, el staging; se conserva el resumen de las confirmadas |
| Notificación | Marcar leída | Purga a los 90 días |
| Claves de idempotencia | — | Purga diaria de las caducadas (job del Worker) |
| Auditoría | — | Nunca |
