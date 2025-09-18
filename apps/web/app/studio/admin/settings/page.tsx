import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { computeBooleansForTenant } from '../../../../src/lib/roles';
import { fetchFromProxy } from '../../../lib/serverFetch';
import TenantSettingsForm from './TenantSettingsForm';
import { TenantLogoUpload } from './TenantLogoUpload';
import TenantGuardrailsForm from './TenantGuardrailsForm';
import TenantBioEditor from './TenantBioEditor';

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

  // Flags-only admin gating: legacy role string ignored.
  const { isAdmin } = computeBooleansForTenant(
    memberships.map((m) => ({
      tenantId: m.tenantId,
      tenantSlug: m.tenantSlug,
      role: 'Viewer',
      roles: m.roles || [],
    })) as unknown as Parameters<typeof computeBooleansForTenant>[0],
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
  let guardrailsInitial: {
    denominationAlignment?: string;
    favoriteAuthors?: string[];
    favoriteBooks?: string[];
    notes?: string;
    lessonFormat?: string;
    denominations?: string[];
  } = {};
  let bioInitial: { format?: string; content?: string } | undefined = undefined;
  let denominationPresets: Array<{ id: string; name: string; notes?: string }> | undefined =
    undefined;
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
      guardrailsInitial = {
        denominationAlignment:
          (get(json, ['settings', 'guardrails', 'denominationAlignment']) as string) || '',
        favoriteAuthors:
          (get(json, ['settings', 'guardrails', 'favoriteAuthors']) as string[]) || [],
        favoriteBooks: (get(json, ['settings', 'guardrails', 'favoriteBooks']) as string[]) || [],
        notes: (get(json, ['settings', 'guardrails', 'notes']) as string) || '',
        lessonFormat: (get(json, ['settings', 'preferences', 'lessonFormat']) as string) || '',
        denominations: (get(json, ['settings', 'presets', 'denominations']) as string[]) || [],
      };
      const bio = get(json, ['settings', 'bio']) as
        | { format?: string; content?: string }
        | null
        | undefined;
      if (bio && typeof bio === 'object') {
        bioInitial = {
          format: (bio.format as string) || 'markdown',
          content: (bio.content as string) || '',
        };
      } else {
        bioInitial = undefined;
      }
    }
  } catch {
    // Leave defaults; form will show blank values
  }
  // Load denomination presets used by guardrails section
  try {
    const res = await fetchFromProxy('/api-proxy/metadata/denominations');
    if (res.ok) {
      const js = (await res.json()) as {
        presets?: Array<{ id: string; name: string; notes?: string }>;
      };
      if (Array.isArray(js.presets)) denominationPresets = js.presets;
    }
  } catch {
    // ignore preset load failures; section will show without options
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

      <section
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
        aria-labelledby="tenant-guardrails-heading"
      >
        <h2 id="tenant-guardrails-heading" className="text-lg font-medium">
          Guardrails & Preferences
        </h2>
        <TenantGuardrailsForm initial={guardrailsInitial} presets={denominationPresets} />
      </section>

      <section
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
        aria-labelledby="tenant-bio-heading"
      >
        <h2 id="tenant-bio-heading" className="text-lg font-medium">
          Organization Bio
        </h2>
        <TenantBioEditor initial={bioInitial} />
      </section>
    </main>
  );
}
