import { Component, OnInit, HostListener, DestroyRef, inject, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { A11yModule } from '@angular/cdk/a11y';
import { RouterOutlet, RouterLink, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';
import { filter, interval } from 'rxjs';
import { NavbarComponent } from './shared/navbar/navbar.component';
import { DISCORD_INVITE_URL, DISCORD_SVG } from './core/community';
import { LocaleService } from './core/locale.service';
import { AuthService } from './core/auth.service';
import { MenuService } from './core/menu.service';
import { DiscordLinkService } from './core/discord-link.service';
import { OfflineQueueService } from './core/offline-queue.service';
import { OfflinePrefetchService } from './core/offline-prefetch.service';
import { PwaInstallService } from './core/pwa-install.service';
import { ClientLogService } from './core/client-log.service';
import { SnackbarService } from './core/snackbar.service';
import { StockfishService } from './features/puzzles/stockfish.service';
import { AnalysisEngineService } from './features/analysis/analysis-engine.service';
import { ThemeService } from './core/theme.service';
import { environment } from '../environments/environment';
import { APK_VERSION } from '../environments/changelog';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, NavbarComponent, TranslatePipe, A11yModule, MatIconModule],
  changeDetection: ChangeDetectionStrategy.Default,
  template: `
    @if (showApkUpdate) {
      <div class="apk-banner">
        <span>{{ 'app.apkUpdate.banner' | translate }}</span>
        <a routerLink="/install" (click)="dismissApkUpdate()">{{ 'app.apkUpdate.install' | translate }}</a>
        <button class="apk-dismiss" (click)="dismissApkUpdate()" [attr.aria-label]="'common.close' | translate">&times;</button>
      </div>
    }
    @if (auth.isImpersonating) {
      <div class="imp-banner">
        <span class="imp-text">
          <span class="imp-icon">&#x1F464;</span>
          {{ 'app.impersonation.banner' | translate: { user: auth.currentUser?.username, admin: auth.impersonatorUsername } }}
        </span>
        <button class="imp-exit" (click)="exitImpersonation()">{{ 'app.impersonation.exit' | translate }}</button>
      </div>
    }
    <app-navbar (changelogClick)="showChangelog = true" (quickstartClick)="showQuickstart = true" />
    <main><router-outlet /></main>
    <footer class="app-footer">
      <span class="version-link" role="button" tabindex="0"
            [attr.aria-label]="'app.changelogTitle' | translate"
            (click)="showChangelog = !showChangelog"
            (keydown.enter)="showChangelog = !showChangelog" (keydown.space)="$event.preventDefault(); showChangelog = !showChangelog">v{{ version }}@if (!production) { <span class="dev-badge">dev</span>}</span>
      <span class="footer-sep">·</span>
      <a class="feedback-link" routerLink="/help">{{ 'nav.help' | translate }}</a>
      <span class="footer-sep">·</span>
      <a class="feedback-link" href="https://github.com/kahalm/rookhub/issues" target="_blank" rel="noopener noreferrer">{{ 'app.feedback' | translate }}</a>
      <span class="footer-sep">·</span>
      <a class="discord-link" [href]="discordUrl" target="_blank" rel="noopener noreferrer"
         [attr.aria-label]="'nav.discord' | translate">
        <mat-icon svgIcon="discord" aria-hidden="true"></mat-icon><span>{{ 'nav.discord' | translate }}</span>
      </a>
    </footer>
    @if (showChangelog) {
      <div class="changelog-overlay" (click)="showChangelog = false">
        <div class="changelog-content" (click)="$event.stopPropagation()"
             role="dialog" aria-modal="true" [attr.aria-label]="'app.changelogTitle' | translate" cdkTrapFocus>
          <div class="changelog-header">
            <h3>{{ 'app.changelogTitle' | translate }}</h3>
            <button (click)="showChangelog = false" [attr.aria-label]="'common.close' | translate" cdkFocusInitial>&times;</button>
          </div>
          @for (entry of changelog; track entry.version) {
            <div class="changelog-entry">
              <strong>v{{ entry.version }}</strong> <span class="changelog-date">{{ entry.date }}</span>
              <ul>
                @for (change of entry.changes; track change.en) {
                  <li>{{ changeText(change) }}</li>
                }
              </ul>
            </div>
          }
        </div>
      </div>
    }
    @if (showQuickstart) {
      <div class="changelog-overlay" (click)="showQuickstart = false">
        <div class="changelog-content quickstart-content" (click)="$event.stopPropagation()"
             role="dialog" aria-modal="true" [attr.aria-label]="'app.quickstartTitle' | translate" cdkTrapFocus>
          <div class="changelog-header">
            <h3>{{ 'app.quickstartTitle' | translate }}</h3>
            <button (click)="showQuickstart = false" [attr.aria-label]="'common.close' | translate" cdkFocusInitial>&times;</button>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x1F3B2;</span>
            <div><strong>{{ 'app.qs.randomTitle' | translate }}</strong><br><span class="qs-desc">{{ 'app.qs.randomDesc' | translate }}</span></div>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x267E;</span>
            <div><strong>{{ 'app.qs.endlessTitle' | translate }}</strong><br><span class="qs-desc">{{ 'app.qs.endlessDesc' | translate }}</span></div>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x1F4C5;</span>
            <div><strong>{{ 'app.qs.dailyTitle' | translate }}</strong><br><span class="qs-desc">{{ 'app.qs.dailyDesc' | translate }}</span></div>
          </div>
          <div class="qs-item">
            <span class="qs-icon">&#x1F4F0;</span>
            <div><strong>{{ 'app.qs.weeklyTitle' | translate }}</strong><br><span class="qs-desc">{{ 'app.qs.weeklyDesc' | translate }}</span></div>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }
    .imp-banner {
      display: flex; align-items: center; justify-content: center; gap: 12px; flex-wrap: wrap;
      background: #b71c1c; color: #fff; padding: 6px 12px; font-size: 0.85rem; font-weight: 500;
      position: sticky; top: 0; z-index: 1100;
    }
    .imp-icon { margin-right: 4px; }
    .imp-exit {
      background: rgba(255,255,255,0.18); color: #fff; border: 1px solid rgba(255,255,255,0.5);
      border-radius: 4px; padding: 3px 10px; cursor: pointer; font: inherit; font-weight: 600;
    }
    .imp-exit:hover { background: rgba(255,255,255,0.3); }
    .apk-banner {
      display: flex; align-items: center; justify-content: center; gap: 12px; flex-wrap: wrap;
      background: #e65100; color: #fff; padding: 6px 14px; font-size: 0.85rem; font-weight: 500;
      position: sticky; top: 0; z-index: 1100;
    }
    .apk-banner a { color: #fff; font-weight: 700; text-decoration: underline; }
    .apk-banner a:hover { opacity: 0.85; }
    .apk-dismiss {
      background: rgba(255,255,255,0.18); color: #fff; border: 1px solid rgba(255,255,255,0.5);
      border-radius: 4px; padding: 3px 10px; cursor: pointer; font: inherit; font-weight: 600;
    }
    .apk-dismiss:hover { background: rgba(255,255,255,0.3); }
    .app-footer { text-align: center; padding: 8px; color: color-mix(in srgb, currentColor 47%, transparent); font-size: 0.75rem; }
    @media (max-width: 768px) { .app-footer { display: none; } }
    .version-link { cursor: pointer; }
    .version-link:hover { color: color-mix(in srgb, currentColor 65%, transparent); text-decoration: underline; }
    .footer-sep { margin: 0 6px; color: color-mix(in srgb, currentColor 40%, transparent); }
    .discord-link {
      display: inline-flex; align-items: center; gap: 4px; vertical-align: middle;
      color: #5865F2; font-weight: 600; text-decoration: none;
    }
    .discord-link:hover { color: #4752c4; text-decoration: underline; }
    .discord-link mat-icon {
      font-size: 1.05rem; width: 1.05rem; height: 1.05rem; line-height: 1.05rem;
    }
    .discord-link mat-icon svg { display: block; width: 100%; height: 100%; }
    .feedback-link { color: inherit; text-decoration: none; }
    .feedback-link:hover { color: color-mix(in srgb, currentColor 65%, transparent); text-decoration: underline; }
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
      background: none; border: none; color: color-mix(in srgb, currentColor 47%, transparent); font-size: 1.5rem; cursor: pointer;
    }
    .changelog-header button:hover { color: inherit; }
    .changelog-entry { margin-bottom: 12px; }
    .changelog-date { color: color-mix(in srgb, currentColor 60%, transparent); font-size: 0.85rem; margin-left: 8px; }
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
  /** Einladungslink zum öffentlichen RookHub-Discord (Community) — prominent in der Fußzeile. */
  readonly discordUrl = DISCORD_INVITE_URL;
  showChangelog = false;
  showQuickstart = false;
  showApkUpdate = false;
  private readonly APK_UPDATE_LS_KEY = 'rookhub_apk_seen_version';

  /** Escape schließt das offene Overlay (Changelog/Quickstart) — Tastatur-Bedienbarkeit. */
  @HostListener('document:keydown.escape')
  onEscape(): void { this.showChangelog = false; this.showQuickstart = false; }

  private dlHandled = false;

  /** Changelog-Eintrag in der aktiven UI-Sprache (de → Deutsch, sonst Englisch als Default/Fallback). */
  changeText(change: { en: string; de: string }): string {
    return this.translate.currentLang() === 'de' ? change.de : change.en;
  }

  private destroyRef = inject(DestroyRef);

  constructor(
    private router: Router,
    locale: LocaleService,
    public auth: AuthService,
    private menu: MenuService,
    private discordLink: DiscordLinkService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private swUpdate: SwUpdate,
    // App-weit instanziieren, damit der Offline-Queue-Sync ('online'-Listener) immer läuft.
    _offlineQueue: OfflineQueueService,
    private offlinePrefetch: OfflinePrefetchService,
    private clientLog: ClientLogService,
    stockfish: StockfishService,
    analysisEngine: AnalysisEngineService,
    _theme: ThemeService,
    // App-weit instanziieren, damit beforeinstallprompt zuverlässig gefangen wird.
    readonly pwa: PwaInstallService,
    iconRegistry: MatIconRegistry,
    sanitizer: DomSanitizer
  ) {
    locale.init();
    // Discord-Markenlogo als SVG-Icon registrieren (auch hier, damit der Footer-Link unabhängig
    // von der Init-Reihenfolge der Navbar das Icon hat).
    iconRegistry.addSvgIconLiteral('discord', sanitizer.bypassSecurityTrustHtml(DISCORD_SVG));
    // Browser-Engine-Crashes/Hänger an die API melden (→ Elasticsearch/Kibana).
    stockfish.reportEngineEvent = (kind, detail) => clientLog.report('engine_stockfish_' + kind, detail);
    analysisEngine.reportEngineEvent = (kind, detail) => clientLog.report('engine_analysis_' + kind, detail);
  }

  ngOnInit(): void {
    // APK-Update-Banner: nur auf Android im Standalone-Modus (= TWA-App).
    if (this.pwa.isAndroid && this.pwa.isInstalled()) {
      const seen = parseInt(localStorage.getItem(this.APK_UPDATE_LS_KEY) ?? '0', 10);
      this.showApkUpdate = APK_VERSION > seen;
    }

    // Offline-Pools (Standard + Endless) gleich beim Start vorab laden, sobald online —
    // nicht erst beim ersten Öffnen der Modi. Leicht verzögert, damit der Initial-Load Vorrang hat.
    setTimeout(() => this.offlinePrefetch.prefetchAll(), 3000);
    window.addEventListener('online', () => this.offlinePrefetch.prefetchAll());

    // Service Worker: neue Version verfügbar → Hinweis mit „Neu laden".
    if (this.swUpdate.isEnabled) {
      this.swUpdate.versionUpdates
        .pipe(filter((e): e is VersionReadyEvent => e.type === 'VERSION_READY'), takeUntilDestroyed(this.destroyRef))
        .subscribe(() => {
          const ref = this.snackbar.show(this.translate.instant('app.updateAvailable'), { action: 'app.reload', duration: 0 });
          ref.onAction().subscribe(() => document.location.reload());
        });
      // Kaputter SW-Zustand (UNRECOVERABLE_STATE: gecachtes Asset fehlt im Cache UND ist nach einem
      // Deploy auch am Server weg). Ein blinder reload() heilt den Zustand nicht — er feuerte sofort
      // wieder und die App hing in einer Endlos-Reload-Schleife (Prod-Vorfall 2026-07-15). Stattdessen:
      // Event melden, SW deregistrieren + ngsw-Caches löschen, genau EINMAL neu laden.
      this.swUpdate.unrecoverable
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(ev => { this.swRecovery = this.recoverFromBrokenServiceWorker(ev.reason); });

      // Aktiv nach neuen Versionen suchen — sonst merkt ein lange offener Tab / eine installierte PWA
      // ein Deploy nie (der SW prüft von sich aus nur beim (Neu-)Start). checkForUpdate() lädt ngsw.json
      // neu → bei neuer Version feuert oben VERSION_READY und der „Neu laden"-Hinweis erscheint.
      // Auslöser: gleich beim Start, alle 15 min, und wann immer der Tab wieder in den Vordergrund kommt.
      this.checkForAppUpdate();
      interval(15 * 60 * 1000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.checkForAppUpdate());
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') this.checkForAppUpdate();
      });
    }

    this.router.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      const params = new URLSearchParams(window.location.search);
      if (params.get('quickstart') === '1') {
        this.showQuickstart = true;
        // Clean up query param
        window.history.replaceState({}, '', window.location.pathname);
      }
      this.handleDiscordLinkParam(params);
    });
  }

  /** Guard-Key: pro Tab-Session höchstens EIN automatischer Recovery-Reload. */
  private static readonly SW_RECOVERY_GUARD_KEY = 'rookhub_sw_recovery_reload';
  /** Laufende SW-Selbstheilung — Feld, damit Tests den async-Ablauf deterministisch awaiten können. */
  swRecovery?: Promise<void>;

  /**
   * SW-Selbstheilung bei UNRECOVERABLE_STATE: Diagnose-Event an die API melden, alle Service-Worker-
   * Registrierungen entfernen und die ngsw-Caches löschen, danach genau einmal neu laden
   * (sessionStorage-Guard verhindert die Reload-Schleife). Schlägt der Guard fehl oder lief der
   * Reload schon, läuft die App ohne SW weiter (Netz-direkt) statt endlos zu reloaden.
   */
  private async recoverFromBrokenServiceWorker(reason: string): Promise<void> {
    this.clientLog.report('sw_unrecoverable', reason);

    let alreadyReloaded = true; // Default: NICHT reloaden (sicher gegen Schleife), außer Guard ist setzbar
    try {
      alreadyReloaded = sessionStorage.getItem(AppComponent.SW_RECOVERY_GUARD_KEY) === '1';
      if (!alreadyReloaded) sessionStorage.setItem(AppComponent.SW_RECOVERY_GUARD_KEY, '1');
    } catch { /* Storage nicht verfügbar → kein Auto-Reload */ }

    try {
      const regs = await navigator.serviceWorker?.getRegistrations?.() ?? [];
      await Promise.all(regs.map(r => r.unregister()));
      if (typeof caches !== 'undefined') {
        const keys = await caches.keys();
        await Promise.all(keys.filter(k => k.startsWith('ngsw:')).map(k => caches.delete(k)));
      }
    } catch { /* best effort — Reload unten registriert den SW ohnehin frisch */ }

    if (!alreadyReloaded) this.reloadApp();
  }

  /** In Methode gekapselt, damit Tests den harten Reload abfangen können. */
  protected reloadApp(): void {
    document.location.reload();
  }

  /** Nach einer neuen App-Version suchen (fehlertolerant; SW evtl. noch nicht registriert). */
  private checkForAppUpdate(): void {
    if (!this.swUpdate.isEnabled) return;
    this.swUpdate.checkForUpdate().catch(() => { /* SW noch nicht bereit / offline → ignorieren */ });
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

    if (this.auth.isLoggedIn) {
      this.discordLink.link(token).subscribe({
        next: () => this.snackbar.info(this.translate.instant('profile.discord.linked')),
        error: (err) => {
          const key = err?.status === 409 ? 'profile.discord.linkConflict' : 'profile.discord.linkFailed';
          this.snackbar.info(this.translate.instant(key), { duration: 4000 });
        }
      });
    } else {
      this.discordLink.stash(token);
      this.snackbar.warn(this.translate.instant('profile.discord.stashed'));
    }
  }

  dismissApkUpdate(): void {
    localStorage.setItem(this.APK_UPDATE_LS_KEY, String(APK_VERSION));
    this.showApkUpdate = false;
  }

  /** Impersonation beenden, Menü neu laden und zurück ins Admin-Panel. */
  exitImpersonation(): void {
    this.auth.stopImpersonation();
    this.menu.refresh();
    this.router.navigate(['/admin']);
  }
}
