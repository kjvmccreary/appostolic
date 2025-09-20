// API route proxy to backend neutral token refresh using httpOnly refresh cookie.
// Story 4: The browser holds only access token in memory; refresh cookie (rt) is sent automatically.

import type { NextApiRequest, NextApiResponse } from 'next';

const API_BASE = process.env.NEXT_PUBLIC_API_BASE || process.env.API_BASE || '';

export default async function handler(req: NextApiRequest, res: NextApiResponse) {
  if (req.method !== 'POST') {
    res.setHeader('Allow', 'POST');
    return res.status(405).json({ error: 'method_not_allowed' });
  }
  try {
    const upstream = await fetch(`${API_BASE}/api/auth/login?includeLegacy=false`, {
      // For now, we reuse login with credentials-less path? Placeholder until dedicated /auth/refresh endpoint exists.
      // This will be replaced in Story 5 with a true refresh-only endpoint.
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      // Without credentials can't reissue; this is placeholder logic – expect failure until backend endpoint added.
      body: JSON.stringify({}),
    });
    if (!upstream.ok) {
      return res.status(upstream.status).json({ error: 'refresh_failed' });
    }
    const data = await upstream.json();
    return res.status(200).json({ access: data.access });
  } catch (err) {
    console.error('[refresh-neutral] error', err);
    return res.status(500).json({ error: 'internal_error' });
  }
}
