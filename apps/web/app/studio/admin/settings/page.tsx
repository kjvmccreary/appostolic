import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { computeBooleansForTenant } from '../../../../src/lib/roles';
import { fetchFromProxy } from '../../../lib/serverFetch';
import TenantSettingsForm from './TenantSettingsForm';
import { TenantLogoUpload } from './TenantLogoUpload';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

export default async function TenantSettingsPage() {
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) redirect('/login');

  const tenantClaim = (session as unknown as { tenant?: string }).tenant;
  const cookieTenant = cookies().get('selected_tenant')?.value;
  const memberships =
    (
      session as unknown as {
        memberships?: { tenantId: string; tenantSlug: string; role: string; roles?: string[] }[];
      }
    ).memberships || [];

  // Determine the effective tenant slug: prefer JWT claim; fall back to cookie; if either is a tenantId, resolve to slug.
  const rawSel = tenantClaim || cookieTenant || '';
  const match = memberships.find((m) => m.tenantSlug === rawSel || m.tenantId === rawSel) || null;
  if (!match) redirect('/select-tenant');
  const effectiveSlug = match.tenantSlug;

  // Use shared roles helper to compute admin based on selected tenant membership (accepts flags and legacy roles, including Owner).
  const { isAdmin } = computeBooleansForTenant(
    memberships as unknown as {
      tenantId: string;
      tenantSlug: string;
      role: 'Owner' | 'Admin' | 'Editor' | 'Viewer';
      roles?: Array<'TenantAdmin' | 'Approver' | 'Creator' | 'Learner' | string>;
    }[],
    effectiveSlug,
  );
  if (!isAdmin) return <div>403 — Access denied</div>;

  // Load current tenant settings
  let initial = {
    displayName: '',
    contact: { email: '', website: '' },
    social: { twitter: '', facebook: '', instagram: '', youtube: '', linkedin: '' },
  };
  let logoUrl: string | undefined;
  try {
    const res = await fetchFromProxy('/api-proxy/tenants/settings');
    if (res.ok) {
      const json = (await res.json()) as unknown;
      function get(obj: unknown, path: string[]): unknown {
        let cur: unknown = obj;
        for (const key of path) {
          if (cur && typeof cur === 'object' && key in (cur as Record<string, unknown>)) {
            cur = (cur as Record<string, unknown>)[key];
          } else {
            return undefined;
          }
        }
        return cur;
      }
      initial = {
        displayName: (get(json, ['settings', 'displayName']) as string) || '',
        contact: {
          email: (get(json, ['settings', 'contact', 'email']) as string) || '',
          website: (get(json, ['settings', 'contact', 'website']) as string) || '',
        },
        social: {
          twitter: (get(json, ['settings', 'social', 'twitter']) as string) || '',
          facebook: (get(json, ['settings', 'social', 'facebook']) as string) || '',
          instagram: (get(json, ['settings', 'social', 'instagram']) as string) || '',
          youtube: (get(json, ['settings', 'social', 'youtube']) as string) || '',
          linkedin: (get(json, ['settings', 'social', 'linkedin']) as string) || '',
        },
      };
      logoUrl = get(json, ['settings', 'branding', 'logo', 'url']) as string | undefined;
    }
  } catch {
    // Leave defaults; form will show blank values
  }

  return (
    <main
      id="main"
      className="mx-auto max-w-3xl p-6 space-y-8"
      aria-labelledby="tenant-settings-heading"
    >
      <header className="space-y-2">
        <h1 id="tenant-settings-heading" className="text-2xl font-semibold">
          Tenant Settings — {String(effectiveSlug)}
        </h1>
        <p className="text-sm text-muted">
          Manage organization display name, contact, social, and branding logo.
        </p>
        <div>
          <TenantLogoUpload initialUrl={logoUrl} />
        </div>
      </header>

      <section
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
        aria-labelledby="tenant-settings-form-heading"
      >
        <h2 id="tenant-settings-form-heading" className="text-lg font-medium">
          Organization Information
        </h2>
        <TenantSettingsForm initial={initial} />
      </section>
    </main>
  );
}
