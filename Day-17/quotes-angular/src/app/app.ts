import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ApiActivityPanel } from './api-activity-panel/api-activity-panel';
import { AuthSession } from './auth/auth-session';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, ApiActivityPanel],
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
