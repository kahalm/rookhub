import { Component, OnInit } from '@angular/core';
import { RouterOutlet, Router } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { filter } from 'rxjs';
import { NavbarComponent } from './shared/navbar/navbar.component';
import { LocaleService } from './core/locale.service';
import { AuthService } from './core/auth.service';
import { DiscordLinkService } from './core/discord-link.service';
import { OfflineQueueService } from './core/offline-queue.service';
import { OfflinePrefetchService } from './core/offline-prefetch.service';
import { ClientLogService } from './core/client-log.service';
import { StockfishService } from './features/puzzles/stockfish.service';
import { AnalysisEngineService } from './features/analysis/analysis-engine.service';
import { environment } from '../environments/environment';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, NavbarComponent, TranslateModule, MatSnackBarModule],
  template: `
    <app-navbar (changelogClick)="showChangelog = true" (quickstartClick)="showQuickstart = true" />
    <main><router-outlet /></main>
    <footer class="app-footer">
      <span class="version-link" (click)="showChangelog = !showChangelog">v{{ version }}@if (!production) { <span class="dev-badge">dev</span>}</span>
      <span class="footer-sep">·</span>
      <a class="feedback-link" href="https://github.com/kahalm/rookhub/issues" target="_blank" rel="noopener noreferrer">{{ 'app.feedback' | translate }}</a>
    </footer>
    @if (showChangelog) {
      <div class="changelog-overlay" (click)="showChangelog = false">
        <div class="changelog-content" (click)="$event.stopPropagation()">
          <div class="changelog-header">
            <h3>{{ 'app.changelogTitle' | translate }}</h3>
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
            <h3>{{ 'app.quickstartTitle' | translate }}</h3>
            <button (click)="showQuickstart = false">&times;</button>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x2B50;</span>
            <div><strong>{{ 'app.qs.subscribeTitle' | translate }}</strong><br><span class="qs-desc">{{ 'app.qs.subscribeDesc' | translate }}</span></div>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x23F0;</span>
            <div><strong>{{ 'app.qs.monitorTitle' | translate }}</strong><br><span class="qs-desc">{{ 'app.qs.monitorDesc' | translate }}</span></div>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x2764;</span>
            <div><strong>{{ 'app.qs.favoritesTitle' | translate }}</strong><br><span class="qs-desc">{{ 'app.qs.favoritesDesc' | translate }}</span></div>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x265E;</span>
            <div><strong>{{ 'app.qs.chessResultsTitle' | translate }}</strong><br><span class="qs-desc">{{ 'app.qs.chessResultsDesc' | translate }}</span></div>
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
    .footer-sep { margin: 0 6px; color: #aaa; }
    .feedback-link { color: inherit; text-decoration: none; }
    .feedback-link:hover { color: #aaa; text-decoration: underline; }
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

  private dlHandled = false;

  constructor(
    private router: Router,
    locale: LocaleService,
    private auth: AuthService,
    private discordLink: DiscordLinkService,
    private snackBar: MatSnackBar,
    private translate: TranslateService,
    private swUpdate: SwUpdate,
    // App-weit instanziieren, damit der Offline-Queue-Sync ('online'-Listener) immer läuft.
    _offlineQueue: OfflineQueueService,
    private offlinePrefetch: OfflinePrefetchService,
    clientLog: ClientLogService,
    stockfish: StockfishService,
    analysisEngine: AnalysisEngineService
  ) {
    locale.init();
    // Browser-Engine-Crashes/Hänger an die API melden (→ Elasticsearch/Kibana).
    stockfish.reportEngineEvent = (kind, detail) => clientLog.report('engine_stockfish_' + kind, detail);
    analysisEngine.reportEngineEvent = (kind, detail) => clientLog.report('engine_analysis_' + kind, detail);
  }

  ngOnInit(): void {
    // Offline-Pools (Standard + Endless) gleich beim Start vorab laden, sobald online —
    // nicht erst beim ersten Öffnen der Modi. Leicht verzögert, damit der Initial-Load Vorrang hat.
    setTimeout(() => this.offlinePrefetch.prefetchAll(), 3000);
    window.addEventListener('online', () => this.offlinePrefetch.prefetchAll());

    // Service Worker: neue Version verfügbar → Hinweis mit „Neu laden".
    if (this.swUpdate.isEnabled) {
      this.swUpdate.versionUpdates
        .pipe(filter((e): e is VersionReadyEvent => e.type === 'VERSION_READY'))
        .subscribe(() => {
          const ref = this.snackBar.open(
            this.translate.instant('app.updateAvailable'),
            this.translate.instant('app.reload'),
            { duration: 0 }
          );
          ref.onAction().subscribe(() => document.location.reload());
        });
    }

    this.router.events.subscribe(() => {
      const params = new URLSearchParams(window.location.search);
      if (params.get('quickstart') === '1') {
        this.showQuickstart = true;
        // Clean up query param
        window.history.replaceState({}, '', window.location.pathname);
      }
      this.handleDiscordLinkParam(params);
    });
  }

  /**
   * Bot-Link `?dl=<token>`: eingeloggt -> sofort verknüpfen; anonym -> Token vormerken
   * (wird nach Login/Registrierung automatisch eingelöst). Param wird aus der URL entfernt.
   */
  private handleDiscordLinkParam(params: URLSearchParams): void {
    if (this.dlHandled) return;
    const token = params.get('dl');
    if (!token) return;
    this.dlHandled = true;

    // 'dl' aus der URL entfernen, andere Query-Params + Pfad behalten.
    params.delete('dl');
    const qs = params.toString();
    window.history.replaceState({}, '', window.location.pathname + (qs ? '?' + qs : ''));

    const close = this.translate.instant('common.close');
    if (this.auth.isLoggedIn) {
      this.discordLink.link(token).subscribe({
        next: () => this.snackBar.open(this.translate.instant('profile.discord.linked'), close, { duration: 3000 }),
        error: (err) => {
          const key = err?.status === 409 ? 'profile.discord.linkConflict' : 'profile.discord.linkFailed';
          this.snackBar.open(this.translate.instant(key), close, { duration: 4000 });
        }
      });
    } else {
      this.discordLink.stash(token);
      this.snackBar.open(this.translate.instant('profile.discord.stashed'), close, { duration: 5000 });
    }
  }
}
