import { z } from 'zod';

export const UserId = z.string().uuid();
export const User = z.object({
  id: UserId,
  email: z.string().email(),
  name: z.string().min(1),
});

export type User = z.infer<typeof User>;
