# Sistema de diseño

Cómo se ve la aplicación y con qué piezas se construye. La fuente visual son los mocks de `Mocks_finanzas-hub-angular/` (raíz del repositorio); el código los traduce a tokens, mixins, clases globales y componentes de `shared/ui`. Las rutas son relativas a `finanzas-hub-web/src/`.

## Tokens (`styles/_tokens.scss`)

Variables CSS definidas dos veces, para `[data-theme='dark']` y `[data-theme='light']`. `ThemeStore` pone el atributo en el documento.

| Token | Uso |
|---|---|
| `--bg` | Fondo de la página |
| `--sf`, `--sf2`, `--sf3` | Superficies: tarjetas, filas, controles (de menos a más elevadas) |
| `--bd` | Bordes y separadores |
| `--tx`, `--tx2`, `--mu` | Texto principal, secundario y apagado |
| `--ac`, `--acs`, `--onac` | Acento (verde), su fondo suave y el texto sobre el acento |
| `--bl`, `--bls` | Azul: información |
| `--rd`, `--rds` | Rojo: gasto, error, excedido |
| `--am`, `--ams` | Ámbar: aviso, en riesgo |
| `--vi`, `--vis` | Violeta: categorías y gráficos |
| `--sh`, `--ov` | Sombra y fondo de los diálogos |

**Regla:** ningún color, sombra ni medida suelta en un `.scss`. Si falta un token, se añade a `_tokens.scss` en los dos temas.

## Responsive (`styles/_mixins.scss`)

Tres disposiciones, como el mock:

| Mixin | Ancho | Marco |
|---|---|---|
| `@include mobile` | < 700 px | Barra de pestañas inferior y hoja «Más» |
| `@include tablet` | 700–1099 px | Riel de iconos lateral |
| `@include tablet-up` | ≥ 700 px | — |
| `@include desktop` | ≥ 1100 px | Barra lateral completa y panel derecho de detalle |
| `@include truncate` | — | Texto en una línea con «…» |

Toda pantalla se revisa en los tres tamaños y en los dos temas antes de darla por terminada.

## Tipografía e iconos

- Manrope (`styles/_typography.scss`).
- Iconos Material Symbols Rounded mediante `<app-icon name="…" />`. El nombre es el del catálogo de Google Fonts.

## Clases globales (`styles/_forms.scss`)

| Clase | Para qué |
|---|---|
| `.card` | Contenedor con superficie y borde |
| `.button`, `--primary`, `--danger`, `--small`, `--block`; `.icon-button`; `.link` | Acciones |
| `.input`, `.search-box`, `.form-stack`, `.form-pair`, `.form-check`, `.form-note`, `.form-actions` | Formularios |
| `.segmented`, `.chip-toggle` | Filtros y selectores de opción |
| `.alert` (`--info`, `--ok`), `.badge` (`--accent`, `--info`, `--warn`), `.tag` | Mensajes y etiquetas |
| `.meter` | Barra de progreso (presupuestos, metas) |

## Componentes de `shared/ui`

UI reutilizable **sin negocio**: no llama a la API ni conoce features.

| Componente | Selector / export | Entradas principales |
|---|---|---|
| Diálogo modal | `<app-dialog>` | `title` (obligatorio); `(closed)` |
| Campo de formulario | `<app-field>` | `label`, `hint`, `state` (el campo de Signal Forms), `serverError` |
| Cabecera de página | `<app-page-header>` | `eyebrow`, `title`, `subtitle` |
| Tarjeta de cifra | `<app-stat-card>` | `label`, `value`, `icon`, `color` |
| Esqueleto de carga | `<app-skeleton>` | `width`, `height` |
| Estado vacío | `<app-empty-state>` | `icon`, `title`, `message`; contenido proyectado para la acción |
| Estado de error | `<app-error-state>` | `title`, `message`; `(retry)` |
| Icono | `<app-icon>` | `name`, `size`, `filled` |
| Icono con fondo de color | `<app-icon-tile>` | `icon`, `color` (`TileColor`), `size` |
| Logotipo | `<app-brand-mark>` | — |
| Dinero | `MoneyPipe` (`amount \| money: currency`), `formatMoney`, `parseAmount`, `sumAmounts` | Formato `es-PE` con dos decimales y símbolo (`S/`, `US$`, `€`) |
| Fechas | `formatShortDate`, `formatDay`, `todayIso`, `toIsoDate`, `monthRange`, `sameDayNextMonth` | Sin librerías de fechas |
| Tags | `parseTags`, `isValidTag` | — |

Antes de crear un componente nuevo, busca aquí. Si dos features necesitan la misma pieza visual, va a `shared/ui`; si además necesita datos o estado, la lógica se queda en la feature y el componente recibe `input`s.

## Gráficos

Barras, anillos y donas se dibujan con CSS (`conic-gradient`, alturas en porcentaje) o SVG propio, como en los mocks. No hay librería de gráficos: no añadas una sin un ADR.

## Accesibilidad

- Controles nativos (`button`, `select`, `input`) en vez de `div` clicables.
- `aria-busy` y `aria-label` en los contenedores de carga; `role="alert"` en los errores de un formulario.
- Contraste de los tokens revisado en los dos temas: usa `--tx`/`--tx2` sobre superficies, nunca `--mu` para texto importante.
- Todo se puede usar con teclado; los diálogos se cierran con Escape.
