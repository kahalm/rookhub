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
    <footer class="app-footer">
      <span class="version-link" (click)="showChangelog = !showChangelog">v{{ version }}</span>
    </footer>
    @if (showChangelog) {
      <div class="changelog-overlay" (click)="showChangelog = false">
        <div class="changelog-content" (click)="$event.stopPropagation()">
          <div class="changelog-header">
            <h3>Changelog</h3>
            <button (click)="showChangelog = false">&times;</button>
          </div>
          @for (entry of changelog; track entry.version) {
            <div class="changelog-entry">
              <strong>v{{ entry.version }}</strong> <span class="changelog-date">{{ entry.date }}</span>
              <ul>
                @for (change of entry.changes; track change) {
                  <li>{{ change }}</li>
                }
              </ul>
            </div>
          }
        </div>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }
    .app-footer { text-align: center; padding: 8px; color: #888; font-size: 0.75rem; }
    @media (max-width: 768px) { .app-footer { display: none; } }
    .version-link { cursor: pointer; }
    .version-link:hover { color: #aaa; text-decoration: underline; }
    .changelog-overlay {
      position: fixed; inset: 0; background: rgba(0,0,0,0.5);
      display: flex; align-items: center; justify-content: center; z-index: 1000;
    }
    .changelog-content {
      background: #1e1e1e; color: #ccc; border-radius: 8px; padding: 24px;
      max-width: 500px; width: 90%; max-height: 80vh; overflow-y: auto;
    }
    .changelog-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
    .changelog-header h3 { margin: 0; color: #fff; }
    .changelog-header button {
      background: none; border: none; color: #888; font-size: 1.5rem; cursor: pointer;
    }
    .changelog-header button:hover { color: #fff; }
    .changelog-entry { margin-bottom: 12px; }
    .changelog-date { color: #666; font-size: 0.85rem; margin-left: 8px; }
    .changelog-entry ul { margin: 4px 0 0 20px; padding: 0; }
    .changelog-entry li { font-size: 0.85rem; margin-bottom: 2px; }
  `]
})
export class AppComponent {
  version = environment.version;
  changelog = environment.changelog;
  showChangelog = false;
}
