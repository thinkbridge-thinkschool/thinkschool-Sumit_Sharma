import { Component } from '@angular/core';
import { Quotes } from './quotes/quotes';

@Component({
  selector: 'app-root',
  imports: [Quotes],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {}
