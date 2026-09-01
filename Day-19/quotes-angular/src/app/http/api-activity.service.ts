import { Injectable, computed, signal } from '@angular/core';
import { ApiActivityEntry, RequestState } from './api-activity.model';

const MAX_ENTRIES = 20;

export type ConnectionStatus = 'unknown' | 'online' | 'offline';

@Injectable({ providedIn: 'root' })
export class ApiActivityService {
  private readonly entries = signal<ApiActivityEntry[]>([]);
  private nextId = 0;

  readonly recent = computed(() => [...this.entries()].reverse());

  readonly connectionStatus = computed<ConnectionStatus>(() => {
    const settled = this.recent().find((entry) => entry.state === 'success' || entry.state === 'error');
    if (!settled) {
      return 'unknown';
    }
    if (settled.state === 'success') {
      return 'online';
    }
    return settled.status === 0 ? 'offline' : 'online';
  });

  start(method: string, path: string): number {
    const id = ++this.nextId;
    const entry: ApiActivityEntry = { id, method, path, state: 'pending', updatedAt: Date.now() };

    this.entries.update((list) => {
      const next = [...list, entry];
      return next.length > MAX_ENTRIES ? next.slice(next.length - MAX_ENTRIES) : next;
    });

    return id;
  }

  setAuthAttached(id: number, attached: boolean): void {
    this.patch(id, { authAttached: attached });
  }

  retrying(id: number, attempt: number): void {
    this.patch(id, { state: 'retrying', retryAttempt: attempt });
  }

  succeed(id: number, status: number): void {
    this.patch(id, { state: 'success', status });
  }

  fail(id: number, status: number, message: string): void {
    this.patch(id, { state: 'error', status, message });
  }

  private patch(id: number, changes: Partial<ApiActivityEntry>): void {
    this.entries.update((list) =>
      list.map((entry) => (entry.id === id ? { ...entry, ...changes, updatedAt: Date.now() } : entry)),
    );
  }
}
