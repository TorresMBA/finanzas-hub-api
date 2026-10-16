# ADR-022: Libros privados y espacios compartidos

- Estado: propuesto
- Fecha: 2026-10-16
- Cambia: [ADR-021](ADR-021-finanzas-privadas-de-la-persona.md) en parte. Lo privado sigue siendo de la persona, pero una persona puede tener varios libros

## Contexto

Antes del ADR-021, cada espacio tenía sus propios libros. Eso permitía separar las finanzas personales de las de un negocio o un trabajo, pero cada invitación a una meta o a un gasto compartido dejaba al invitado en un espacio vacío, con sus cuentas en otro sitio.

El ADR-021 lo resolvió dejando un solo juego de libros por persona: el de su espacio personal. Así se perdió la separación entre personal, negocio y trabajo. Además, el selector de espacio de la barra superior dejó de tener sentido: cuentas, movimientos, presupuestos y reportes son los mismos en cualquier espacio, y solo cambian las metas y los gastos compartidos.

El problema de fondo es que «espacio» nombra dos cosas distintas: **dónde vive el dinero** de una persona y **con quién comparte**.

## Decisión

Se separan en dos conceptos.

**Libro.** Contenedor privado de las finanzas de una persona: cuentas, movimientos, categorías, tags, presupuestos, movimientos programados, inversiones y tipos de cambio.
- Pertenece a una sola persona. Ningún rol de ningún espacio lo ve.
- Una persona tiene uno o más libros. Todos reciben uno, «Personal», que no se puede archivar mientras sea el único.
- Cada libro tiene nombre, **tipo** (`personal`, `business`, `work` u `other`), moneda base y zona horaria.
- El tipo solo decide el ícono y las categorías sugeridas al crearlo. No cambia ninguna regla del dominio.
- Las categorías y los tags son de cada libro.

**Espacio compartido.** Personas con roles, metas y grupos de gastos compartidos. **No tiene libros ni cuentas.** Entrar a uno no obliga a registrar nada de nuevo.

**Participación.**
- Al aceptar una invitación, la persona elige su **libro predeterminado** para ese espacio. Lo puede cambiar después.
- Cada aporte a una meta, cada gasto llevado a una cuenta y cada pago de un gasto compartido puede usar una cuenta de cualquiera de sus libros. El selector muestra las cuentas agrupadas por libro, con el predeterminado preseleccionado.
- El fondo de una meta se abre en el libro de la cuenta desde la que se aporta.

**Entre libros.**
- Mover dinero entre dos libros de la misma persona es una **transferencia**. No es ingreso ni gasto en ninguno de los dos.
- En cada libro se ve como transferencia desde o hacia el otro libro. En la vista «Todos» se cancela.

**Vista «Todos».**
- El Inicio y el patrimonio pueden sumar todos los libros de la persona, convertidos a una moneda que ella elige, con los tipos de cambio de cada libro.
- Transacciones, Presupuestos, Categorías, Calendario, Reportes e Inversiones siguen siendo por libro.

**Navegación.**
- El selector global elige el libro, con la opción «Todos» donde aplica.
- Metas y Gastos compartidos no dependen del selector: muestran lo de todos los espacios de la persona, filtrable por espacio.
- Configuración separa «Mis libros» de «Espacios compartidos».

**Backend.**
- Las rutas de lo privado llevan el libro: `/api/v1/books/{bookId}/accounts` y las demás. Las de lo colaborativo llevan el espacio, como hoy.
- `IPersonalSpace` se convierte en el contexto del libro actual (`IBookContext`: id, moneda base y zona horaria). Se resuelve una vez por petición, validando que el libro sea de la persona. Un libro ajeno responde 404.
- El filtro global de `IOwned` usa el libro actual: `owner_user_id = persona` y `workspace_id = libro`. La columna `workspace_id` de lo privado pasa a significar el libro.
- Lo que corre fuera de una petición itera sobre los libros de cada persona: el ciclo de movimientos programados, las reglas de importación y el chat de WhatsApp (que responde con el libro predeterminado de la persona o el que se nombre).

## Alternativas consideradas

- **Quedarse con el ADR-021.** Un solo libro por persona. Es simple, pero mezcla personal, negocio y trabajo, y deja el selector de espacio sin sentido.
- **Entrar al espacio compartido «con» un libro.** El libro elegido quedaría dentro del espacio. Mezcla de nuevo dónde vive el dinero con con quién se comparte, y obliga a elegir un único libro para todo lo de ese espacio.
- **El tipo de libro decide reglas.** Por ejemplo, que un negocio cuente los aportes como ingreso. Cada tipo nuevo pediría código nuevo, y la regla de que una transferencia no es ingreso ni gasto tendría excepciones.
- **Libros compartidos** (un socio o un contador que ve el libro de un negocio). No se descarta: un libro tiene dueño y más adelante podría tener miembros con rol de lectura. Queda fuera de esta decisión.

## Consecuencias

- **Migración.** El espacio personal de cada persona pasa a ser su libro «Personal», sin mover datos. Los espacios no personales pasan a ser espacios compartidos. Lo privado que quedó en espacios ajenos antes del ADR-021 sigue sin verse, igual que hoy.
- **Privacidad.** No cambia quién ve qué. Los tests de privacidad cubren además el libro de otra persona con la ruta adivinada y el libro propio desde un espacio compartido.
- **Reportes y presupuestos.** Se calculan dentro de un libro. Solo el Inicio y el patrimonio consolidan.
- **Gastos compartidos por WhatsApp.** El chat deja de depender del espacio que la persona tenga abierto, porque los grupos se buscan en todos sus espacios y las cuentas en sus libros.
- **Frontend.** Selector de libro con «Todos», cuentas agrupadas por libro en los selectores, elección de libro al aceptar una invitación y la pantalla «Mis libros».
- **Documentos que cambian:** `AGENTS.md` (regla de Privacidad), [security.md](../backend/security.md), [data-model.md](../backend/data-model.md) y [frontend/architecture.md](../frontend/architecture.md).
