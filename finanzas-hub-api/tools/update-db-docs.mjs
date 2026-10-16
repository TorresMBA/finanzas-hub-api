// Regenerates the generated part of docs/backend/database.md from the EF Core model snapshot:
// tables, columns, keys, indexes, check constraints, foreign keys and the ER diagrams.
// Run after creating a migration:  node tools/update-db-docs.mjs   (from finanzas-hub-api)
import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const snapshotPath = join(root, 'src/PersonalFinance.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs');
const docPath = join(root, '../docs/backend/database.md');
const START = '<!-- generated:start -->';
const END = '<!-- generated:end -->';

// ---------- Parsing the snapshot ----------

function matchClose(s, i, open, close) {
  let depth = 0, inStr = false;
  for (let j = i; j < s.length; j++) {
    const c = s[j];
    if (inStr) { if (c === '\\') { j++; continue; } if (c === '"') inStr = false; continue; }
    if (c === '"') { inStr = true; continue; }
    if (c === open) depth++;
    else if (c === close && --depth === 0) return j;
  }
  throw new Error('Unbalanced snapshot');
}

function splitStatements(body) {
  const out = []; let depth = 0, inStr = false, start = 0;
  for (let j = 0; j < body.length; j++) {
    const c = body[j];
    if (inStr) { if (c === '\\') { j++; continue; } if (c === '"') inStr = false; continue; }
    if (c === '"') { inStr = true; continue; }
    if (c === '{' || c === '(') depth++;
    else if (c === '}' || c === ')') depth--;
    else if (c === ';' && depth === 0) { out.push(body.slice(start, j).trim()); start = j + 1; }
  }
  return out.filter(Boolean);
}

const strings = (s) => [...s.matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((m) => m[1].replace(/\\"/g, '"'));
const one = (s, re) => (s.match(re) || [])[1];
const shortName = (t) => t.replace(/<.*$/, '').split('.').pop();

const entities = {};
const entity = (name) => (entities[name] ??= { name, table: null, schema: null, cols: {}, pk: null, indexes: [], fks: [], checks: [], ownedBy: null });

function parseBlock(name, body, v, owner) {
  const e = entity(name);
  if (owner) e.ownedBy = owner;
  for (const st of splitStatements(body)) {
    const kind = one(st, new RegExp(`^${v}\\.(\\w+)`));
    if (kind === 'Property' || kind === 'PrimitiveCollection') {
      const clr = one(st, /(?:Property|PrimitiveCollection)<([^>]+)>/) ?? '';
      const valueType = !clr.endsWith('?') && !/^(string|byte\[\])$/.test(clr) && !clr.includes('[]') && !clr.startsWith('List');
      e.cols[strings(st)[0]] = {
        col: one(st, /HasColumnName\("([^"]+)"\)/),
        type: one(st, /HasColumnType\("([^"]+)"\)/),
        required: /IsRequired\(\)/.test(st) || valueType,
        concurrency: /IsConcurrencyToken\(\)/.test(st),
        identity: /UseIdentityByDefaultColumn/.test(st),
        computed: one(st, /HasComputedColumnSql\("((?:[^"\\]|\\.)*)"/),
      };
    } else if (kind === 'HasKey') {
      const name = one(st, /HasName\("([^"]+)"\)/);
      e.pk = { cols: strings(st).filter((x) => x !== name), name };
    } else if (kind === 'HasIndex') {
      const open = st.indexOf('(');
      e.indexes.push({
        cols: strings(st.slice(open, matchClose(st, open, '(', ')'))),
        name: one(st, /HasDatabaseName\("([^"]+)"\)/),
        unique: /IsUnique\(\)/.test(st),
        filter: one(st, /HasFilter\("((?:[^"\\]|\\.)*)"\)/)?.replace(/\\"/g, '"'),
        descending: /IsDescending/.test(st),
      });
    } else if (kind === 'ToTable') {
      const [table, schema] = strings(st.slice(0, st.indexOf(')') + 1));
      e.table = table; e.schema = schema ?? 'public';
      for (const m of st.matchAll(/HasCheckConstraint\("([^"]+)",\s*"((?:[^"\\]|\\.)*)"\)/g)) e.checks.push({ name: m[1], sql: m[2].replace(/\\"/g, '"') });
    } else if (kind === 'HasOne') {
      const fk = one(st, /HasForeignKey\(([^)]*)\)/);
      e.fks.push({
        to: strings(st)[0], cols: fk ? strings(fk).filter((x) => !x.includes('.')) : [],
        name: one(st, /HasConstraintName\("([^"]+)"\)/), onDelete: one(st, /DeleteBehavior\.(\w+)/) ?? 'Cascade',
        oneToOne: /WithOne/.test(st), required: /IsRequired\(\)/.test(st),
      });
    } else if (kind === 'WithOwner') {
      const fk = one(st, /HasForeignKey\(([^)]*)\)/);
      e.fks.push({ to: owner, cols: fk ? strings(fk) : [], name: one(st, /HasConstraintName\("([^"]+)"\)/), onDelete: 'Cascade', required: true, owned: true });
    } else if (kind === 'OwnsMany' || kind === 'OwnsOne') {
      const lambda = st.match(/,\s*(\w+)\s*=>\s*\{/);
      const open = st.indexOf('{', lambda.index);
      const ownedName = strings(st)[0];
      parseBlock(ownedName, st.slice(open + 1, matchClose(st, open, '{', '}')), lambda[1], name);
      if (kind === 'OwnsOne') entity(ownedName).oneToOne = true;
    }
  }
}

const snapshot = readFileSync(snapshotPath, 'utf8');
const entityRe = /modelBuilder\.Entity\("([^"]+)",\s*b\s*=>\s*\{/g;
for (let m; (m = entityRe.exec(snapshot)); ) {
  const open = m.index + m[0].length - 1;
  const close = matchClose(snapshot, open, '{', '}');
  parseBlock(m[1], snapshot.slice(open + 1, close), 'b', null);
  entityRe.lastIndex = close;
}

// An owned type without a table of its own (or split onto the owner's table) adds columns to its owner.
for (const e of Object.values(entities)) {
  const owner = entities[e.ownedBy];
  if (owner && (!e.table || (e.table === owner.table && e.schema === owner.schema))) {
    e.table = null;
    for (const [prop, c] of Object.entries(e.cols)) {
      if (!Object.values(owner.cols).some((x) => x.col === c.col)) owner.cols[`${shortName(e.name)}.${prop}`] = { ...c, required: false };
    }
    owner.indexes.push(...e.indexes);
  }
}

const tables = Object.values(entities).filter((e) => e.table).map((e) => {
  const colOf = (p) => e.cols[p]?.col ?? p;
  return {
    entity: e.name, key: `${e.schema}.${e.table}`, table: e.table, schema: e.schema, oneToOneOwned: e.oneToOne,
    columns: Object.values(e.cols).filter((c) => c.col).map((c) => ({ ...c, name: c.col })),
    pk: e.pk && { name: e.pk.name, cols: e.pk.cols.map(colOf) },
    indexes: e.indexes.map((i) => ({ ...i, cols: i.cols.map(colOf) })),
    fks: e.fks.map((f) => ({ ...f, cols: f.cols.map(colOf), toKey: entities[f.to] && `${entities[f.to].schema}.${entities[f.to].table}` })),
    checks: e.checks,
  };
});
const byKey = Object.fromEntries(tables.map((t) => [t.key, t]));

// ---------- Descriptions from the XML comments of the domain classes ----------

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path, out); else if (name.endsWith('.cs')) out.push(path);
  }
  return out;
}

const summaries = {};
for (const file of walk(join(root, 'src'))) {
  const text = readFileSync(file, 'utf8');
  for (const m of text.matchAll(/\/\/\/ <summary>((?:(?!<summary>|<\/summary>)[\s\S])*)<\/summary>(?:[ \t]*\r?\n[ \t]*\/\/\/[^\n]*)*\s*(?:\[[^\]\n]*\]\s*)*(?:public|internal) (?:sealed |abstract |static )*(?:partial )?(?:class|record) (\w+)/g)) {
    summaries[m[2]] ??= m[1].replace(/\/\/\/\s?/g, ' ').replace(/<see cref="([^"]+)"\s*\/>/g, '`$1`').replace(/<c>(.*?)<\/c>/g, '`$1`').replace(/<[^>]+>/g, '').replace(/\s+/g, ' ').trim();
  }
}
const frameworkSummaries = {
  DataProtectionKey: 'Claves de ASP.NET Core Data Protection: firman la cookie de sesión y los enlaces de los correos.',
  IdentityUserClaim: 'Claims de ASP.NET Core Identity. Sin uso propio hoy.',
  IdentityUserLogin: 'Inicios de sesión externos de ASP.NET Core Identity. Sin uso propio hoy.',
  IdentityUserToken: 'Tokens de ASP.NET Core Identity.',
  GroupMember: 'Someone in a group that splits expenses. Leaving keeps the row, so past expenses stay attributable.',
};
const describe = (t) => frameworkSummaries[shortName(t.entity)] ?? summaries[shortName(t.entity)] ?? '';

// ---------- Logical references between modules (by id, no foreign key) ----------

const logicalTargets = [
  [/(^|_)user_id$/, 'identity.users'],
  [/(^|_)workspace_id$/, 'workspaces.workspaces'],
  [/(^|_)category_id$/, 'categories.categories'],
  [/^tag_id$/, 'categories.tags'],
  [/(^|_)account_id$/, 'ledger.accounts'],
  [/(^|_)operation_id$/, 'ledger.operations'],
  [/^notification_id$/, 'notifications.notifications'],
  [/^expense_id$/, 'shared_expenses.shared_expenses'],
];
for (const t of tables) {
  const withFk = new Set(t.fks.flatMap((f) => f.cols));
  t.refs = t.columns
    .filter((c) => !withFk.has(c.name) && !(t.pk?.cols.length === 1 && t.pk.cols[0] === c.name))
    .map((c) => ({ col: c.name, nullable: !c.required, toKey: logicalTargets.find(([re]) => re.test(c.name))?.[1] }))
    .filter((r) => r.toKey && byKey[r.toKey]);
}

// ---------- Rendering ----------

const schemaOrder = ['identity', 'workspaces', 'categories', 'ledger', 'budgets', 'notifications', 'scheduling', 'goals', 'shared_expenses', 'integrations', 'investments', 'platform'];
const schemas = [...new Set(tables.map((t) => t.schema))].sort((a, b) => (schemaOrder.indexOf(a) + 99) % 99 - (schemaOrder.indexOf(b) + 99) % 99);
const ent = (key) => key.replace('.', '__');
const anchor = (key) => key.replace('.', '').replace(/_/g, '_');
const cell = (s) => String(s ?? '').replace(/\|/g, '\\|');

function mermaidType(type) {
  if (!type) return 'unknown';
  if (type.startsWith('character varying')) return 'varchar';
  if (/^char(acter)?\(/.test(type)) return 'char';
  if (type.startsWith('numeric')) return 'numeric';
  if (type === 'timestamp with time zone') return 'timestamptz';
  return type.replace(/[^\w]/g, '_');
}

function relation(fromKey, toKey, { required, oneToOne, logical, label }) {
  const left = required ? '||' : '|o';
  const right = oneToOne ? 'o|' : 'o{';
  return `    ${ent(toKey)} ${left}${logical ? '..' : '--'}${right} ${ent(fromKey)} : "${label}"`;
}

function diagram(schema) {
  const own = tables.filter((t) => t.schema === schema);
  const lines = ['```mermaid', 'erDiagram'];
  for (const t of own) {
    const fkCols = new Set(t.fks.flatMap((f) => f.cols));
    const refCols = new Set(t.refs.map((r) => r.col));
    lines.push(`    ${ent(t.key)} {`);
    for (const c of t.columns) {
      const keys = [t.pk?.cols.includes(c.name) && 'PK', fkCols.has(c.name) && 'FK'].filter(Boolean).join(',');
      const note = refCols.has(c.name) && !fkCols.has(c.name) ? ' "ref"' : '';
      lines.push(`        ${mermaidType(c.type)} ${c.name}${keys ? ` ${keys}` : ''}${note}`);
    }
    lines.push('    }');
  }
  for (const t of own) {
    for (const f of t.fks) {
      if (!f.toKey) continue;
      const oneToOne = f.oneToOne || t.oneToOneOwned || (t.pk && f.cols.join() === t.pk.cols.join());
      lines.push(relation(t.key, f.toKey, { required: f.required, oneToOne, label: f.cols.join(', ') }));
    }
    // The owner and the personal workspace of private rows are omitted: almost every table has them.
    for (const r of t.refs) {
      if (r.col === 'owner_user_id' || (r.col === 'workspace_id' && schema !== 'workspaces')) continue;
      lines.push(relation(t.key, r.toKey, { required: !r.nullable, logical: true, label: r.col }));
    }
  }
  lines.push('```');
  return lines.join('\n');
}

function overview() {
  const lines = ['```mermaid', 'erDiagram'];
  for (const t of tables) {
    for (const f of t.fks) if (f.toKey) lines.push(relation(t.key, f.toKey, { required: f.required, oneToOne: f.oneToOne || t.oneToOneOwned || (t.pk && f.cols.join() === t.pk.cols.join()), label: f.cols.join(', ') }));
    for (const r of t.refs) {
      if (/user_id$/.test(r.col) || /workspace_id$/.test(r.col)) continue;
      lines.push(relation(t.key, r.toKey, { required: !r.nullable, logical: true, label: r.col }));
    }
  }
  const mentioned = new Set(lines.join('\n').match(/\b\w+__\w+\b/g));
  for (const t of tables) if (!mentioned.has(ent(t.key))) lines.push(`    ${ent(t.key)}`);
  lines.push('```');
  return lines.join('\n');
}

function tableSection(t) {
  const out = [`#### \`${t.key}\``, ''];
  const description = describe(t);
  if (description) out.push(description, '');
  const fkByCol = Object.fromEntries(t.fks.flatMap((f) => f.cols.map((c) => [c, f])));
  const refByCol = Object.fromEntries(t.refs.map((r) => [r.col, r]));
  out.push('| Columna | Tipo | Nulo | Notas |', '|---|---|---|---|');
  for (const c of t.columns) {
    const notes = [];
    if (t.pk?.cols.includes(c.name)) notes.push('PK');
    if (fkByCol[c.name]?.toKey) notes.push(`FK → \`${fkByCol[c.name].toKey}\``);
    else if (refByCol[c.name]) notes.push(`ref. → \`${refByCol[c.name].toKey}\` (sin FK)`);
    if (c.identity) notes.push('identity');
    if (c.concurrency) notes.push('concurrencia optimista');
    if (c.computed) notes.push(`calculada: \`${cell(c.computed)}\``);
    out.push(`| \`${c.name}\` | ${cell(c.type)} | ${c.required ? 'no' : 'sí'} | ${notes.join('; ')} |`);
  }
  out.push('');
  if (t.indexes.length) {
    out.push('Índices (además de la PK `' + (t.pk?.name ?? '') + '`):', '', '| Nombre | Columnas | Único | Filtro |', '|---|---|---|---|');
    for (const i of t.indexes) out.push(`| \`${i.name}\` | ${i.cols.map((c) => `\`${c}\``).join(', ')}${i.descending ? ' (con orden descendente)' : ''} | ${i.unique ? 'sí' : 'no'} | ${i.filter ? `\`${cell(i.filter)}\`` : ''} |`);
    out.push('');
  }
  if (t.fks.some((f) => f.toKey)) {
    out.push('| Clave foránea | Columnas | Referencia | ON DELETE |', '|---|---|---|---|');
    for (const f of t.fks.filter((x) => x.toKey)) out.push(`| \`${f.name}\` | ${f.cols.map((c) => `\`${c}\``).join(', ')} | \`${f.toKey}\` | ${f.onDelete.toUpperCase()} |`);
    out.push('');
  }
  if (t.checks.length) {
    out.push('| Restricción CHECK | Condición |', '|---|---|');
    for (const c of t.checks) out.push(`| \`${c.name}\` | \`${cell(c.sql)}\` |`);
    out.push('');
  }
  return out.join('\n');
}

const totals = {
  tables: tables.length,
  indexes: tables.reduce((n, t) => n + t.indexes.length, 0),
  fks: tables.reduce((n, t) => n + t.fks.filter((f) => f.toKey).length, 0),
  checks: tables.reduce((n, t) => n + t.checks.length, 0),
};
const migrations = readdirSync(dirname(snapshotPath)).filter((f) => /^\d+_.*\.cs$/.test(f) && !f.endsWith('.Designer.cs')).sort();

const generated = [
  START,
  '',
  `> Generado por \`finanzas-hub-api/tools/update-db-docs.mjs\` desde \`AppDbContextModelSnapshot.cs\`. No editar a mano entre las marcas. Última migración: \`${migrations.at(-1)?.replace(/\.cs$/, '')}\`.`,
  '',
  `**${totals.tables} tablas** en ${schemas.length} esquemas, **${totals.indexes} índices** secundarios, **${totals.fks} claves foráneas** y **${totals.checks} restricciones CHECK**.`,
  '',
  '### Tablas por esquema',
  '',
  '| Esquema | Tablas |',
  '|---|---|',
  ...schemas.map((s) => `| \`${s}\` | ${tables.filter((t) => t.schema === s).map((t) => `[\`${t.table}\`](#${anchor(t.key)})`).join(', ')} |`),
  '',
  '### Vista general',
  '',
  'Relaciones entre tablas: línea continua, clave foránea; punteada, referencia por id entre módulos (sin FK). Para no saturar el diagrama se omiten las referencias a `identity.users` y `workspaces.workspaces`, que tienen casi todas las tablas.',
  '',
  overview(),
  '',
  '### Detalle por esquema',
  '',
  ...schemas.flatMap((s) => [`### Esquema \`${s}\``, '', diagram(s), '', ...tables.filter((t) => t.schema === s).map(tableSection)]),
  END,
].join('\n');

// Anchors: GitHub turns "#### `ledger.accounts`" into "#ledgeraccounts".
const doc = readFileSync(docPath, 'utf8');
const start = doc.indexOf(START), end = doc.indexOf(END);
if (start < 0 || end < 0) throw new Error(`Missing ${START} / ${END} in ${docPath}`);
writeFileSync(docPath, doc.slice(0, start) + generated + doc.slice(end + END.length));
console.log(`docs/backend/database.md: ${totals.tables} tables, ${totals.indexes} indexes, ${totals.fks} foreign keys, ${totals.checks} checks`);
