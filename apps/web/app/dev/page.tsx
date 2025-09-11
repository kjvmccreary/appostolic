'use client';

import React from 'react';
import Link from 'next/link';
import { createClient, MeResponse, TenantSummary } from '@appostolic/sdk';

export default function DevPage() {
  const [me, setMe] = React.useState<MeResponse | null>(null);
  const [tenants, setTenants] = React.useState<TenantSummary[] | null>(null);
  const [error, setError] = React.useState<string | null>(null);

  React.useEffect(() => {
    const baseUrl = process.env.NEXT_PUBLIC_API_URL || '';
    const devUser = process.env.NEXT_PUBLIC_DEV_USER || '';
    const devTenant = process.env.NEXT_PUBLIC_DEV_TENANT || '';

    const client = createClient(baseUrl);
    const headers = {
      'x-dev-user': devUser,
      'x-tenant': devTenant,
    } as Record<string, string>;

    (async () => {
      try {
        const meRes = await client.me({ headers });
        setMe(meRes);
        const tenantsRes = await client.tenants({ headers });
        setTenants(tenantsRes);
      } catch (e) {
        const msg = e instanceof Error ? e.message : 'Failed to fetch';
        setError(msg);
      }
    })();
  }, []);

  return (
    <main className="p-24">
      <h1>Dev tools</h1>
      <p>
        <Link href="/">Home</Link>
      </p>

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
