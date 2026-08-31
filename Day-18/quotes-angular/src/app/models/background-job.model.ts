export type BackgroundJobStatus = 'Queued' | 'Running' | 'Completed' | 'Failed';

export interface BackgroundJob {
  id: string;
  requestedCount: number;
  status: BackgroundJobStatus;
  importedCount: number;
  error?: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
}
