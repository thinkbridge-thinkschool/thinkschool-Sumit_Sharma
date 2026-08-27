import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ApiActivityPanel } from './api-activity-panel/api-activity-panel';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, ApiActivityPanel],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}
