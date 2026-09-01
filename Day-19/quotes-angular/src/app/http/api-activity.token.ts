import { HttpContextToken } from '@angular/common/http';

/**
 * Client-side-only correlation id (never sent over the wire) that lets the
 * auth/retry/activity interceptors report against the same activity-log
 * entry for a single logical request, even across req.clone() calls.
 */
export const API_ACTIVITY_ID = new HttpContextToken<number | null>(() => null);
