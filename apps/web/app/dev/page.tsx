'use client';

import React from 'react';
import { createClient, MeResponse, TenantSummary } from '@appostolic/sdk';
import {
  forceRefresh,
  startAutoRefresh,
  stopAutoRefresh,
  withAuthFetch,
} from '../../src/lib/authClient';

export default function DevPage() {
  const [me, setMe] = React.useState<MeResponse | null>(null);
  const [tenants, setTenants] = React.useState<TenantSummary[] | null>(null);
  const [error, setError] = React.useState<string | null>(null);

  const baseUrl = process.env.NEXT_PUBLIC_API_BASE ?? '';
  const client = React.useMemo(() => createClient(baseUrl, withAuthFetch), [baseUrl]);

  React.useEffect(() => {
    let active = true;

    if (!baseUrl) {
      setError('Missing NEXT_PUBLIC_API_BASE. Configure the API base URL to enable dev tools.');
      setMe(null);
      setTenants(null);
      return () => {
        active = false;
      };
    }

    setError(null);
    setMe(null);
    setTenants(null);
    startAutoRefresh();

    (async () => {
      try {
        await forceRefresh().catch(() => null);
        const meRes = await client.me();
        if (!active) return;
        setMe(meRes);
        const tenantsRes = await client.tenants();
        if (!active) return;
        setTenants(tenantsRes);
      } catch (e) {
        if (!active) return;
        const msg = e instanceof Error ? e.message : 'Failed to fetch';
        setError(msg);
      }
    })();

    return () => {
      active = false;
      stopAutoRefresh();
    };
  }, [baseUrl, client]);

  return (
    <main className="p-24">
      <h1>Dev tools</h1>
      <nav className="space-x-4">
        <a href="/">Home</a>
        <a className="underline" href="/dev/agents">
          Run Agent (S1-09)
        </a>
      </nav>

      {error && <div className="text-red">Error: {error}</div>}

      <section>
        <h2>/api/me</h2>
        <pre>{me ? JSON.stringify(me, null, 2) : 'Loading...'}</pre>
      </section>

      <section>
        <h2>/api/tenants</h2>
        <pre>{tenants ? JSON.stringify(tenants, null, 2) : 'Loading...'}</pre>
      </section>
    </main>
  );
}
