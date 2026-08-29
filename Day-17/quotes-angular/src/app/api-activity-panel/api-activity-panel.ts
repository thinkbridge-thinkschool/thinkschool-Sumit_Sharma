import { Component, inject, signal } from '@angular/core';
import { ApiActivityService } from '../http/api-activity.service';
import { RequestState } from '../http/api-activity.model';

const STATE_LABELS: Record<RequestState, string> = {
  pending: 'Loading',
  retrying: 'Retrying',
  success: 'Success',
  error: 'Error',
};

@Component({
  selector: 'app-api-activity-panel',
  imports: [],
  templateUrl: './api-activity-panel.html',
  styleUrl: './api-activity-panel.css',
})
export class ApiActivityPanel {
  protected readonly activity = inject(ApiActivityService);
  protected readonly expanded = signal(false);

  protected readonly entries = this.activity.recent;
  protected readonly connectionStatus = this.activity.connectionStatus;

  toggle(): void {
    this.expanded.update((value) => !value);
  }

  stateLabel(state: RequestState): string {
    return STATE_LABELS[state];
  }
}
