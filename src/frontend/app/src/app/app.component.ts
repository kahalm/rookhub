import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NavbarComponent } from './shared/navbar/navbar.component';
import { environment } from '../environments/environment';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, NavbarComponent],
  template: `
    <app-navbar />
    <main><router-outlet /></main>
    <footer class="app-footer">v{{ version }}</footer>
  `,
  styles: [`
    :host { display: block; }
    .app-footer { text-align: center; padding: 8px; color: #888; font-size: 0.75rem; }
    @media (max-width: 768px) { .app-footer { display: none; } }
  `]
})
export class AppComponent {
  version = environment.version;
}
