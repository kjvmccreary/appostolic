CREATE EXTENSION IF NOT EXISTS "pg_trgm";
CREATE EXTENSION IF NOT EXISTS vector;

CREATE SCHEMA IF NOT EXISTS app AUTHORIZATION CURRENT_USER;

CREATE OR REPLACE FUNCTION app.set_tenant(tenant uuid)
RETURNS void LANGUAGE sql AS $$
  SELECT set_config('app.tenant_id', tenant::text, true)
$$;
