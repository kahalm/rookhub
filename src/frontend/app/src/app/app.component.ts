import { Component, OnInit } from '@angular/core';
import { RouterOutlet, Router } from '@angular/router';
import { NavbarComponent } from './shared/navbar/navbar.component';
import { environment } from '../environments/environment';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, NavbarComponent],
  template: `
    <app-navbar (changelogClick)="showChangelog = true" (quickstartClick)="showQuickstart = true" />
    <main><router-outlet /></main>
    <footer class="app-footer">
      <span class="version-link" (click)="showChangelog = !showChangelog">v{{ version }}@if (!production) { <span class="dev-badge">dev</span>}</span>
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
    @if (showQuickstart) {
      <div class="changelog-overlay" (click)="showQuickstart = false">
        <div class="changelog-content quickstart-content" (click)="$event.stopPropagation()">
          <div class="changelog-header">
            <h3>Quickstart</h3>
            <button (click)="showQuickstart = false">&times;</button>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x2B50;</span>
            <div><strong>Subscribe</strong><br><span class="qs-desc">Abonniere Turniere für schnellen Zugriff auf Spieler, Paarungen und Ergebnisse.</span></div>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x23F0;</span>
            <div><strong>Monitor</strong><br><span class="qs-desc">Aktiviere den Monitor bei einem Turnier &ndash; neue Runden werden automatisch erkannt und gecrawlt.</span></div>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x2764;</span>
            <div><strong>Favoriten</strong><br><span class="qs-desc">Markiere Spieler oder Teams als Favorit für schnellen Zugriff in der Turnieransicht.</span></div>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x265E;</span>
            <div><strong>ChessResults-ID</strong><br><span class="qs-desc">Hinterlege deine ChessResults-ID im Profil &ndash; anstehende Turniere werden automatisch abonniert.</span></div>
          </div>
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
    .dev-badge { color: #ff9800; font-weight: bold; margin-left: 4px; }
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
    .qs-item { display: flex; gap: 12px; align-items: flex-start; margin-bottom: 14px; }
    .qs-icon { font-size: 1.4rem; min-width: 28px; text-align: center; }
    .qs-desc { font-size: 0.85rem; color: #aaa; }
  `]
})
export class AppComponent implements OnInit {
  version = environment.version;
  production = environment.production;
  changelog = environment.changelog;
  showChangelog = false;
  showQuickstart = false;

  constructor(private router: Router) {}

  ngOnInit(): void {
    this.router.events.subscribe(() => {
      const params = new URLSearchParams(window.location.search);
      if (params.get('quickstart') === '1') {
        this.showQuickstart = true;
        // Clean up query param
        window.history.replaceState({}, '', window.location.pathname);
      }
    });
  }
}
