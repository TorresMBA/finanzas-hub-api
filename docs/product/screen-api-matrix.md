# Matriz pantalla → API

## P.4 Matriz pantalla → API

Permisos: **D** dueño, **P** participante, **M** miembro del workspace, **A** Admin. Estados obligatorios en toda fila: carga, vacío y error.

| Pantalla | Caso de uso | Endpoint | Módulo | Entidades | Eventos | Perm. | Tiempo real |
|---|---|---|---|---|---|---|---|
| Registro | RegisterUser | `POST /auth/register` | Identity, Workspaces | User, Workspace, Membership | UserRegistered | Anón. | — |
| Onboarding | CompleteOnboarding | `POST W/onboarding/complete` | Workspaces, Categories | Workspace, Category | OnboardingCompleted | Owner | — |
| Shell | GetMe | `GET /me` | Identity, Workspaces | User, Membership | — | Propio | — |
| Shell | GetUnreadCount | `GET /me/notifications/unread-count` | Notifications | Notification | — | Propio | Sí |
| Shell | Search | `GET W/search` | Reporting | — | — | D | — |
| Inicio | GetDashboard | `GET W/dashboard` | Reporting | — | — | D | — |
| Transacciones | GetTransactions | `GET W/transactions` | Ledger | Operation, Entry | — | D | — |
| Transacciones | PostTransaction | `POST W/transactions` | Ledger | Operation, Entry | OperationPosted | D | — |
| Transacciones | PostTransfer | `POST W/transfers` | Ledger | Operation, Entry | OperationPosted | D | — |
| Transacciones | EditTransaction (incluye las etiquetas) | `PATCH W/transactions/{id}` | Ledger | Operation | OperationChanged | D | — |
| Transacciones | ConfirmTransaction, VoidTransaction | `POST W/transactions/{id}/confirm`, `POST W/transactions/{id}/void` | Ledger | Operation | OperationVoided | D | — |
| Transacciones | "Repetir cada mes" | `POST W/recurring-rules` | Scheduling | RecurringRule | — | D | — |
| Transacciones | "Dividir gasto" | `POST W/shared-groups/{id}/expenses` con `fromTransactionId` | SharedExpenses | SharedExpense | SharedExpensePublished | P | Señal |
| Cuentas | GetAccounts, GetAccountSummary | `GET W/accounts`, `GET W/accounts/summary` | Ledger | Account | — | D | — |
| Cuentas | OpenAccount | `POST W/accounts` | Ledger | Account, CreditCardProfile | — | D | — |
| Cuentas | UpdateAccount, ArchiveAccount | `PATCH W/accounts/{id}`, `POST .../archive`, `POST .../restore` | Ledger | Account | AccountArchived | D | — |
| Cuentas | AdjustAccountBalance | `POST W/accounts/{id}/adjust-balance` | Ledger | Operation | OperationPosted | D | — |
| Cuentas | PayCreditCard | `POST W/accounts/{id}/payments` | Ledger | Operation | OperationPosted | D | — |
| Cuentas | ListExchangeRates, SetExchangeRate | `GET W/exchange-rates`, `PUT W/exchange-rates/{moneda}` | Ledger | ExchangeRate | — | D | — |
| Categorías | ListCategories, ListTags | `GET W/categories`, `GET W/tags` | Categories | Category, Tag | — | D | — |
| Categorías | GetCategoryTotals | `GET W/transactions/category-totals` | Ledger | Operation, Entry | — | D | — |
| Categorías | CreateCategory, UpdateCategory | `POST W/categories`, `PATCH W/categories/{id}` | Categories | Category | — | D | — |
| Categorías | ArchiveCategory, RestoreCategory | `POST W/categories/{id}/archive`, `POST W/categories/{id}/restore` | Categories | Category | CategoryArchived (desde la fase 7) | D | — |
| Categorías | SetCategoryTags, ArchiveTag | `PUT W/categories/{id}/tags`, `POST W/tags/{id}/archive` | Categories | Tag | — | D | — |
| Categorías | SeedDefaultCategories | `POST W/categories/defaults` | Categories | Category | — | D | — |
| Presupuestos | GetBudgets, GetBudgetDetail | `GET W/budgets`, `GET W/budgets/{id}` | Budgets | Budget | — | D | — |
| Presupuestos | CreateBudget, AdjustBudgetLimit, EndBudget | `POST`, `PATCH`, `DELETE W/budgets[/{id}]` | Budgets | Budget | — | D | — |
| Metas | GetGoals, GetGoalDetail | `GET W/goals`, `GET W/goals/{id}` | Goals | Goal, Participant, Contribution | — | P | Señal |
| Metas | CreateGoal, UpdateGoal | `POST W/goals`, `PATCH W/goals/{id}` | Goals | Goal | GoalParticipantAdded | M / Owner de meta | — |
| Metas | ContributeToGoal | `POST W/goals/{id}/contributions` | Goals, Ledger | Contribution, Operation | GoalContributionAdded, GoalTargetReached | P | Señal |
| Metas | SetAutoContribution | `PUT W/goals/{id}/auto-contribution` | Goals, Scheduling | RecurringRule | — | P | — |
| Calendario | GetCalendar | `GET W/calendar` | Scheduling | RecurringRule, Occurrence | — | D | — |
| Calendario | CreateRecurringRule | `POST W/recurring-rules` | Scheduling | RecurringRule | — | D | — |
| Calendario | ConfirmOccurrence, SkipOccurrence | `POST W/recurring-rules/{id}/occurrences/{date}/confirm`, `.../skip` | Scheduling, Ledger | Occurrence, Operation | OperationPosted | D | — |
| Compartidos | GetSummary, GetGroups, GetGroupDetail | `GET W/shared-expenses/summary`, `GET W/shared-groups[/{id}]` | SharedExpenses | SharedGroup, Share, Settlement | — | P | Señal |
| Compartidos | CreateGroup | `POST W/shared-groups` | SharedExpenses | SharedGroup, GroupMember | — | M | — |
| Compartidos | CreateExpense, PublishExpense | `POST W/shared-groups/{id}/expenses`, `.../publish` | SharedExpenses, Ledger | SharedExpense, Share | SharedExpensePublished | P | Señal |
| Compartidos | RecordSettlement | `POST W/shared-groups/{id}/settlements` | SharedExpenses, Ledger | Settlement, Allocation, Operation | SettlementRecorded | Parte | Señal |
| Compartidos | LinkSettlementToAccount | `POST .../settlements/{sid}/link-account` | SharedExpenses, Ledger | Settlement, Operation | — | Parte | — |
| Compartidos | RemindMember | `POST .../members/{mid}/remind` | SharedExpenses, Notifications | Notification | — | Acreedor | — |
| Inversiones | GetPortfolio, GetHolding | `GET W/investments/summary`, `GET W/holdings[/{id}]` | Investments | Holding, Asset | — | D | — |
| Inversiones | OpenHolding, Contribute, Withdraw | `POST W/holdings`, `.../contributions`, `.../withdrawals` | Investments, Ledger | Holding, InvestmentTransaction, Operation | OperationPosted | D | — |
| Reportes | GetReportSummary, ExportReport | `GET W/reports/summary`, `GET W/reports/export` | Reporting | — | — | D | — |
| Integraciones | UploadImport, GetImport | `POST W/imports`, `GET W/imports/{id}` | Integrations | ImportSession | — | D | Sondeo |
| Integraciones | ReviewRecord, CommitImport | `PATCH .../records/{rid}`, `POST W/imports/{id}/commit` | Integrations, Ledger | ImportRecord, Operation | OperationPosted | D | — |
| Integraciones | CRUD de reglas | `W/categorization-rules` | Integrations | CategorizationRule | — | D | — |
| Notificaciones | ListNotifications, MarkRead, MarkAllRead | `GET /me/notifications`, `POST .../{id}/read`, `POST .../read-all` | Notifications | Notification | — | Propio | Sí |
| Notificaciones | UpdatePreferences, UpdateSettings | `PUT /me/notification-preferences`, `PUT /me/notification-settings` | Notifications | Preference, Settings | — | Propio | — |
| Notificaciones | Verificar WhatsApp | `POST /me/channels/whatsapp/verify-start`, `.../verify-confirm` | Notifications | ChannelEndpoint | — | Propio | — |
| Configuración | UpdateProfile, UpdatePreferences | `PATCH /me/profile`, `PUT /me/preferences` | Identity | User, UserPreferences | UserProfileChanged | Propio | — |
| Configuración | ListMembers, ChangeMemberRole, RemoveMember | `GET W/members`, `PATCH`, `DELETE W/members/{userId}` | Workspaces | Membership | MemberRemoved | M / A | — |
| Configuración | InviteMember, RevokeInvitation | `POST W/invitations`, `DELETE W/invitations/{id}` | Workspaces | Invitation | MemberInvited | A | — |
| Invitación | AcceptInvitation | `POST /invitations/{token}/accept` | Workspaces | Membership | MemberJoined | Destinatario | — |

Todas las rutas de la tabla llevan el prefijo `/api/v1`.
