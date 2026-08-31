import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EMPTY, expand, delay as rxDelay } from 'rxjs';
import { BackgroundJobsApi } from '../services/background-jobs-api';
import { BackgroundJob, BackgroundJobStatus } from '../models/background-job.model';
import { AuthSession } from '../auth/auth-session';
import { AppError } from '../http/app-error.model';

const POLL_INTERVAL_MS = 1000;

const STATE_LABELS: Record<BackgroundJobStatus, string> = {
  Queued: 'Queued',
  Running: 'Running',
  Completed: 'Completed',
  Failed: 'Failed',
};

function isSettled(job: BackgroundJob): boolean {
  return job.status === 'Completed' || job.status === 'Failed';
}

/**
 * Demonstrates the Day-18 BackgroundService queue from the client side: this
 * panel POSTs /api/quotes/import (returns 202 immediately — the actual
 * import runs on the API's background queue, never on this request), then
 * polls GET /api/jobs/{id} until the job settles. Enqueue several at once
 * to watch QueuedHostedService drain them one at a time.
 */
@Component({
  selector: 'app-background-jobs-panel',
  imports: [FormsModule],
  templateUrl: './background-jobs-panel.html',
  styleUrl: './background-jobs-panel.css',
})
export class BackgroundJobsPanel {
  private readonly api = inject(BackgroundJobsApi);

  protected readonly authSession = inject(AuthSession);
  protected readonly expanded = signal(false);
  protected readonly jobs = signal<BackgroundJob[]>([]);
  protected readonly requestedCount = signal(3);
  protected readonly enqueueError = signal<string | null>(null);

  protected readonly activeCount = () =>
    this.jobs().filter((job) => !isSettled(job)).length;

  toggle(): void {
    this.expanded.update((value) => !value);
  }

  stateLabel(status: BackgroundJobStatus): string {
    return STATE_LABELS[status];
  }

  enqueue(): void {
    this.enqueueError.set(null);

    this.api.enqueueImport(this.requestedCount()).subscribe({
      next: (job) => {
        this.jobs.update((list) => [job, ...list]);
        this.poll(job.id);
      },
      error: (err: AppError) => this.enqueueError.set(err.message),
    });
  }

  private poll(id: string): void {
    this.api
      .getJob(id)
      .pipe(
        expand((job) => (isSettled(job) ? EMPTY : this.api.getJob(id).pipe(rxDelay(POLL_INTERVAL_MS)))),
      )
      .subscribe((job) => {
        this.jobs.update((list) => list.map((j) => (j.id === job.id ? job : j)));
      });
  }
}
