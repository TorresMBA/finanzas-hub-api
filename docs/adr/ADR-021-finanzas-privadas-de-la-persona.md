# ADR-021: Las finanzas privadas son de la persona, no del espacio

- Estado: aceptado
- Fecha: 2026-10-13
- Cambia: el alcance de "privado" en [security.md](../architecture/security.md) y la regla de Privacidad de `AGENTS.md`

## Contexto

Lo privado (cuentas, movimientos, categorías, tags, presupuestos, movimientos programados y tipos de cambio) se guardaba por la pareja persona + espacio. Cada persona tenía un juego de libros distinto en cada espacio al que pertenecía.

Al probar las metas compartidas, el dueño vio la consecuencia: quien es invitado al espacio de otra persona empieza ahí en cero aunque tenga todo registrado en su espacio personal, y para aportar a una meta de ese espacio tiene que volver a crear sus cuentas. El mismo dinero habría quedado en dos sitios.

## Decisión

Las finanzas privadas pertenecen a la persona. Viven en su **espacio personal**, el que recibe con su cuenta, y son las mismas tenga abierto el espacio que tenga. Un espacio compartido solo añade lo colaborativo: miembros, metas y gastos compartidos.

- Un espacio lleva la marca `is_personal`. Hay uno por persona y no se traspasa.
- Lo privado sigue llevando `workspace_id` y `owner_user_id` e implementando `IOwned`. El filtro global usa siempre el espacio personal del dueño (`IPersonalSpace`), no el de la ruta.
- Los módulos que guardan lo privado piden `IPersonalSpace` (espacio, moneda base y zona horaria de la persona). `IWorkspaceContext` queda para lo colaborativo y para los roles.
- Las rutas no cambian: `W/accounts`, `W/transactions` y las demás responden lo personal con cualquier espacio del que la persona sea miembro.
- Una meta es del espacio en que se creó. Quien aporta lo hace desde sus cuentas de siempre, y su fondo de la meta se abre en su espacio personal.
- La moneda en que se suman las cifras privadas es la del espacio personal. Quien se une a un espacio con el suyo aún sin configurar hereda la moneda y la zona horaria del espacio al que se une.
- Las categorías sugeridas se siembran solo en el espacio personal. Unirse a un espacio ya no siembra nada.
- El ciclo de movimientos programados corre una vez por persona, en su espacio personal.

## Alternativas consideradas

- **Dejar los libros por espacio.** Es lo que había. Obliga a registrar dos veces el mismo dinero.
- **Libros por espacio con cuentas enlazadas.** Traer una cuenta del espacio personal a otro. Más piezas y dos lugares donde mirar el mismo saldo: contradice "evitar múltiples fuentes de verdad sobre el dinero".

## Consecuencias

- La privacidad no cambia: lo privado es solo de su dueño y ningún rol ve lo ajeno. Cambia dónde vive, no quién lo ve.
- Un espacio creado para compartir no tiene libros propios: no hay "cuentas del hogar". Lo común se lleva con metas y gastos compartidos.
- Las cifras privadas de una persona están en la moneda de su espacio personal aunque tenga abierto un espacio con otra moneda base.
- Lo privado que alguien registró dentro del espacio de otra persona antes de este cambio no se borra, pero deja de verse: no está en su espacio personal. El dueño decidió dejarlo así porque eran datos de prueba. Un fondo de meta abierto entonces en un espacio ajeno ya no admite retiros.
- La migración `AddPersonalWorkspaces` marca como personal el espacio más antiguo que posee cada persona.
