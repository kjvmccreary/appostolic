import { z } from 'zod';

// Common primitives
export const UUID = z.string().uuid();
export const ISODate = z
  .union([z.string().datetime(), z.date()])
  .transform((v) => (v instanceof Date ? v.toISOString() : v));

// User
export const User = z.object({
  id: UUID,
  email: z.string().email(),
  displayName: z.string().min(1).optional(),
  createdAt: ISODate,
});
export type User = z.infer<typeof User>;

// Tenant
export const TenantKind = z.enum(['personal', 'org']);
export const Plan = z.enum(['free', 'pro', 'org']);
export const Tenant = z.object({
  id: UUID,
  slug: z.string().min(1),
  name: z.string().min(1),
  kind: TenantKind,
  plan: Plan,
  createdAt: ISODate,
});
export type Tenant = z.infer<typeof Tenant>;

// Membership
export const Role = z.enum(['owner', 'admin', 'editor', 'reviewer', 'viewer']);
export const MembershipStatus = z.enum(['active', 'invited', 'suspended']);
export const Membership = z.object({
  tenantId: UUID,
  userId: UUID,
  role: Role,
  status: MembershipStatus,
  createdAt: ISODate,
});
export type Membership = z.infer<typeof Membership>;

// Content Blocks
export const BlockKind = z.enum(['intro', 'exposition', 'discussion', 'activity', 'prayer']);
export const Block = z.object({
  id: UUID,
  kind: BlockKind,
  minutes: z.number().int().positive(),
  content: z.string(),
  citations: z.array(z.string()).optional(),
});
export type Block = z.infer<typeof Block>;
export const Blocks = z.array(Block).min(0);
export type Blocks = z.infer<typeof Blocks>;

// Lesson
export const Audience = z.enum(['kids', 'youth', 'adults']);
export const LessonStatus = z.enum(['draft', 'needs_review', 'approved', 'archived']);
export const Lesson = z.object({
  id: UUID,
  tenantId: UUID,
  createdBy: UUID,
  topic: z.string().min(1),
  audience: Audience,
  durationMin: z.number().int().positive(),
  status: LessonStatus,
  blocks: Blocks,
  createdAt: ISODate,
  updatedAt: ISODate,
});
export type Lesson = z.infer<typeof Lesson>;
