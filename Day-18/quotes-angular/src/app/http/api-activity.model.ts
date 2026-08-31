export type RequestState = 'pending' | 'retrying' | 'success' | 'error';

export interface ApiActivityEntry {
  id: number;
  method: string;
  path: string;
  state: RequestState;
  status?: number;
  retryAttempt?: number;
  authAttached?: boolean;
  message?: string;
  updatedAt: number;
}
