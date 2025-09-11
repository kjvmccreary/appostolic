-- Enable useful extensions
CREATE EXTENSION IF NOT EXISTS "pg_trgm";
CREATE EXTENSION IF NOT EXISTS vector;

-- Create application schema
CREATE SCHEMA IF NOT EXISTS app AUTHORIZATION CURRENT_USER;

-- Set tenant id helper
CREATE OR REPLACE FUNCTION app.set_tenant(tenant uuid)
RETURNS void LANGUAGE sql AS $$
  SELECT set_config('app.tenant_id', tenant::text, true)
$$;
