# ADR-023: Tipos de espacio y libros por tipo

- Estado: aceptado
- Fecha: 2026-10-21
- Cambia: [ADR-021](ADR-021-finanzas-privadas-de-la-persona.md) en parte. Reemplaza a [ADR-022](ADR-022-libros-y-espacios-compartidos.md), que no llegó a aceptarse

## Contexto

El ADR-021 dejó un solo juego de libros por persona, el de su espacio personal, para que quien aporta a una meta o paga un gasto compartido lo haga desde sus cuentas de siempre. Se perdió la separación entre lo personal y un negocio.

El ADR-022 propuso separar «libro» de «espacio compartido» con rutas nuevas (`/books/{id}`), un selector con «Todos» y un libro predeterminado por espacio. Resolvía el problema, pero cambiaba casi todo el frontend y todas las rutas de lo privado.

## Decisión

El espacio sigue siendo la unidad y las rutas no cambian. Cada espacio tiene un **tipo**, que se elige al crearlo y no cambia.

**Personal.** El espacio personal de cada persona y cualquier espacio que se cree para compartir («Hogar», «Viaje»). Puede haber varios.
- No tiene libros propios. Las finanzas privadas que se ven en él son las del espacio personal de quien mira, como en el ADR-021.
- Es el único tipo que admite miembros, invitaciones, metas y gastos compartidos. Un espacio Personal compartido es el **círculo** de personas con quienes se comparte.

**Negocio** y **Otros.** Libros propios de su dueño: cuentas, movimientos, categorías, tags, presupuestos, movimientos programados, inversiones y tipos de cambio, con su propia moneda base y zona horaria.
- Solo los usa el dueño. No admiten miembros, invitaciones, metas ni gastos compartidos (`workspaces.kind_not_shareable`).
- Las categorías sugeridas se siembran al configurarlos. Negocio tiene un conjunto propio.

**Regla de resolución.** Lo privado de una petición está en el espacio personal de la persona si el espacio de la ruta es Personal, y en el propio espacio de la ruta si es Negocio u Otros. El filtro global de `IOwned` no cambia: sigue usando `owner_user_id` y el libro resuelto (`IBook`, antes `IPersonalSpace`).

**Entre libros.** Mover dinero entre dos libros de la misma persona es una transferencia con una pata en cada libro, enlazadas entre sí. No es ingreso ni gasto, y anular una pata anula la otra.

**Metas y Gastos compartidos** muestran lo de todos los círculos de la persona, agrupado por círculo, sin depender del espacio elegido en la barra superior. Al aceptar una invitación se entra como persona: no hay que elegir espacio, porque todos los espacios Personal de alguien comparten sus libros.

**Salir de un círculo**, o que su dueño o un Admin saque a alguien, no se permite mientras la persona deba o le deban algo en un grupo, tenga un gasto compartido sin liquidar o tenga dinero en una meta activa del círculo.

## Alternativas consideradas

- **ADR-022, libros separados de los espacios.** Más general, pero cambia todas las rutas y el modelo del frontend.
- **Un solo espacio Personal por persona e invitar a él.** Obliga a fusionar o archivar los espacios compartidos que ya existen.
- **Invitar directamente a una meta o un grupo, sin círculos.** Más directo para el usuario, pero rehace invitaciones, filtros y autorización.
- **Socios con acceso de lectura a un negocio.** Rompe la regla de que pertenecer no da acceso a lo financiero. Si hace falta, irá en su propio ADR.

## Consecuencias

- **Migración.** Columna `kind`, con `personal` para todos los espacios existentes. No se mueve ningún dato.
- **Privacidad.** No cambia quién ve qué. Los libros de un negocio solo los ve su dueño, y la ruta de un negocio ajeno responde 404.
- **Procesos en segundo plano.** El ciclo de programados corre una vez por cada libro: el personal de cada persona y cada negocio u otro.
- **Cambiar el tipo** no se permite. Un espacio Personal con metas que pasara a ser Negocio dejaría fuera a sus participantes.
- **Pendientes.** Vista consolidada de todos los libros, saldo de un negocio por WhatsApp y archivar un negocio con saldo.
