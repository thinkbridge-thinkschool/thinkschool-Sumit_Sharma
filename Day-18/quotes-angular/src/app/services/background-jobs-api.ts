import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BackgroundJob } from '../models/background-job.model';

@Injectable({
  providedIn: 'root',
})
export class BackgroundJobsApi {
  private readonly http = inject(HttpClient);

  /** Returns immediately (202 Accepted) — the import itself runs on the API's background queue. */
  enqueueImport(count: number): Observable<BackgroundJob> {
    return this.http.post<BackgroundJob>('/api/quotes/import', { count });
  }

  getJob(id: string): Observable<BackgroundJob> {
    return this.http.get<BackgroundJob>(`/api/jobs/${id}`);
  }
}
