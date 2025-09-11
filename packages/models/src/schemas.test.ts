import { describe, expect, it } from 'vitest';
import { z } from 'zod';
import { randomUUID } from 'node:crypto';
import { User, Tenant, Membership, Blocks, Lesson } from './schemas';

const nowIso = new Date().toISOString();

describe('schemas', () => {
  it('validates a User', () => {
    const data = { id: randomUUID(), email: 'a@b.com', displayName: 'A', createdAt: nowIso };
    expect(() => User.parse(data)).not.toThrow();
    const badEmail = { ...data, email: 'bad' } as unknown;
    expect(User.safeParse(badEmail).success).toBe(false);
  });

  it('validates a Tenant', () => {
    const data = {
      id: randomUUID(),
      slug: 'app',
      name: 'App',
      kind: 'org',
      plan: 'pro',
      createdAt: nowIso,
    };
    expect(() => Tenant.parse(data)).not.toThrow();
    const wrongPlan = { ...data, plan: 'enterprise' } as unknown;
    expect(Tenant.safeParse(wrongPlan).success).toBe(false);
  });

  it('validates a Membership', () => {
    const data = {
      tenantId: randomUUID(),
      userId: randomUUID(),
      role: 'admin',
      status: 'active',
      createdAt: nowIso,
    };
    expect(() => Membership.parse(data)).not.toThrow();
    const wrongRole = { ...data, role: 'guest' } as unknown;
    expect(Membership.safeParse(wrongRole).success).toBe(false);
  });

  it('validates Blocks and Lesson', () => {
    const blocks: z.infer<typeof Blocks> = [
      { id: randomUUID(), kind: 'intro', minutes: 5, content: 'Welcome' },
      { id: randomUUID(), kind: 'exposition', minutes: 15, content: 'Teaching', citations: [] },
    ];

    const lesson = {
      id: randomUUID(),
      tenantId: randomUUID(),
      createdBy: randomUUID(),
      topic: 'Faith',
      audience: 'adults',
      durationMin: 30,
      status: 'draft',
      blocks,
      createdAt: nowIso,
      updatedAt: nowIso,
    };

    expect(() => Lesson.parse(lesson)).not.toThrow();
    const negativeDuration = { ...lesson, durationMin: -5 } as unknown;
    expect(Lesson.safeParse(negativeDuration).success).toBe(false);
    const badBlocks = {
      ...lesson,
      blocks: [{ id: 'x', kind: 'intro', minutes: 5, content: 'ok' }],
    } as unknown;
    expect(Lesson.safeParse(badBlocks).success).toBe(false);
  });
});
