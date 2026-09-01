import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ApiActivityPanel } from './api-activity-panel/api-activity-panel';
import { BackgroundJobsPanel } from './background-jobs-panel/background-jobs-panel';
import { ServiceBusEventsPanel } from './service-bus-events-panel/service-bus-events-panel';
import { AuthSession } from './auth/auth-session';

@Component({
  selector: 'app-root',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    ApiActivityPanel,
    BackgroundJobsPanel,
    ServiceBusEventsPanel,
  ],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly router = inject(Router);

  readonly authSession = inject(AuthSession);

  signOut(): void {
    this.authSession.signOut();
    this.router.navigateByUrl('/quotes');
  }
}
