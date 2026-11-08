-- Rol de base de datos con el que corren la API y el Worker: lee y escribe los datos, pero no cambia el esquema ni
-- puede borrar o modificar la auditoría. Las migraciones siguen corriendo con el rol dueño de las tablas.
--
-- Una vez por base, conectado como el dueño de las tablas (el rol con el que se aplicaron las migraciones):
--   psql "<conexión del dueño>" -v app_role=personalhub_app -v app_password='...' -f tools/db-roles.sql
-- Es idempotente: se puede repetir tras cada migración que cree un esquema nuevo (los de tablas nuevas en un esquema
-- existente ya los cubren los privilegios por defecto).
-- Después: la cadena de conexión de API y Worker usa este rol, y la del `migrate` sigue usando la del dueño
-- (docs/devops/secrets.md, "Usuario de base de datos propio").

\set ON_ERROR_STOP on

SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'app_role', :'app_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'app_role') \gexec
SELECT format('ALTER ROLE %I LOGIN PASSWORD %L', :'app_role', :'app_password') \gexec

SELECT format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), :'app_role') \gexec

-- Un esquema por módulo, más platform. Se leen de la base: cada esquema que no es del sistema es de PersonalHub.
SELECT format('GRANT USAGE ON SCHEMA %I TO %I', nspname, :'app_role')
FROM pg_namespace
WHERE nspname NOT LIKE 'pg\_%' AND nspname NOT IN ('information_schema', 'public') \gexec

SELECT format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', nspname, :'app_role')
FROM pg_namespace
WHERE nspname NOT LIKE 'pg\_%' AND nspname NOT IN ('information_schema', 'public') \gexec

SELECT format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I', nspname, :'app_role')
FROM pg_namespace
WHERE nspname NOT LIKE 'pg\_%' AND nspname NOT IN ('information_schema', 'public') \gexec

-- Las tablas que creen las migraciones futuras nacen con los mismos permisos.
SELECT format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I', nspname, :'app_role')
FROM pg_namespace
WHERE nspname NOT LIKE 'pg\_%' AND nspname NOT IN ('information_schema', 'public') \gexec

SELECT format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO %I', nspname, :'app_role')
FROM pg_namespace
WHERE nspname NOT LIKE 'pg\_%' AND nspname NOT IN ('information_schema', 'public') \gexec

-- La auditoría es de solo inserción: ahora lo impide la base, no solo la convención.
SELECT format('REVOKE UPDATE, DELETE, TRUNCATE ON platform.audit_log FROM %I', :'app_role') \gexec

-- El historial de migraciones solo se lee (el health check /health/ready mira si falta alguna).
SELECT format('REVOKE INSERT, UPDATE, DELETE ON platform.__ef_migrations_history FROM %I', :'app_role') \gexec
