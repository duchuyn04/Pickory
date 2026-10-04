import type { Permission } from './permissions';

export interface ConsentState {
  personalization: boolean;
  marketing: boolean;
}

export interface Actor {
  anonymousId: string;
  userId: string | null;
  kind: 'guest' | 'customer' | 'staff';
  roles: string[];
  permissions: ReadonlySet<Permission>;
  consent: ConsentState;
  sessionCreatedAt?: Date;
}

/* eslint-disable @typescript-eslint/no-namespace */
declare global {
  namespace Express {
    interface Request {
      actor: Actor;
    }
  }
}
/* eslint-enable @typescript-eslint/no-namespace */
