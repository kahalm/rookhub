import { Component, OnInit, HostBinding, HostListener, DestroyRef, inject, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { A11yModule } from '@angular/cdk/a11y';
import { RouterOutlet, RouterLink, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SwUpdate, VersionReadyEvent, VersionInstallationFailedEvent } from '@angular/service-worker';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';
import { filter, interval } from 'rxjs';
import { NavbarComponent } from './shared/navbar/navbar.component';
import { DISCORD_INVITE_URL, DISCORD_SVG, KOFI_URL } from './core/community';
import { LocaleService } from './core/locale.service';
import { AuthService } from './core/auth.service';
import { MenuService } from './core/menu.service';
import { DiscordLinkService } from './core/discord-link.service';
import { OfflineQueueService } from './core/offline-queue.service';
import { FullscreenOverlayService } from './shared/fullscreen/fullscreen-overlay.service';
import { OfflinePrefetchService } from './core/offline-prefetch.service';
import { PwaInstallService } from './core/pwa-install.service';
import { ClientLogService } from './core/client-log.service';
import { ConnectivityService } from './core/connectivity.service';
import { SnackbarService } from './core/snackbar.service';
import { StockfishService } from './features/puzzles/stockfish.service';
import { AnalysisEngineService } from './features/analysis/analysis-engine.service';
import { ThemeService } from './core/theme.service';
import {
  exitFullscreen, isFullscreen, onFullscreenChange,
} from './shared/fullscreen/fullscreen.util';
import { environment } from '../environments/environment';
import { APK_VERSION, ChangelogEntry } from '../environments/changelog';

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
    @if (connectivity.problem(); as connProblem) {
      <div class="conn-banner" [class.conn-offline]="connProblem === 'offline'">
        <div class="conn-row">
          <span class="conn-icon" aria-hidden="true">{{ connProblem === 'offline' ? '&#x1F4F4;' : '&#x26A0;&#xFE0F;' }}</span>
          <span>{{ 'app.connectivity.' + connProblem | translate }}</span>
          <button class="conn-btn" (click)="showConnDetails = !showConnDetails">{{ 'app.connectivity.details' | translate }}</button>
          @if (connProblem === 'unreachable') {
            <button class="conn-btn" (click)="connectivity.checkNow()">{{ 'app.connectivity.retry' | translate }}</button>
          }
        </div>
        @if (showConnDetails) {
          <div class="conn-details">
            @if (connProblem === 'offline') {
              <p>{{ 'app.connectivity.offlineInfo' | translate }}</p>
            } @else {
              <p>{{ 'app.connectivity.hintsTitle' | translate }}</p>
              <ul>
                <li>{{ 'app.connectivity.hint1' | translate }}</li>
                <li>{{ 'app.connectivity.hint2' | translate }}</li>
                <li>{{ 'app.connectivity.hint3' | translate }}</li>
                <li>{{ 'app.connectivity.hint4' | translate }}</li>
              </ul>
            }
          </div>
        }
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
    <app-navbar (changelogClick)="openChangelog()" (quickstartClick)="showQuickstart = true" />
    @if (appFullscreen) {
      <!-- Im App-Vollbild sind Navbar + Fußzeile ausgeblendet (maximaler Platz fürs Brett) —
           dieser schwebende Knopf ist neben Esc der Weg zurück. -->
      <button class="app-fs-exit" (click)="exitAppFullscreen()"
              [attr.title]="'nav.fullscreenExit' | translate"
              [attr.aria-label]="'nav.fullscreenExit' | translate">
        <mat-icon>fullscreen_exit</mat-icon>
      </button>
    }
    <main><router-outlet /></main>
    <footer class="app-footer">
      <span class="version-link" role="button" tabindex="0"
            [attr.aria-label]="'app.changelogTitle' | translate"
            (click)="toggleChangelog()"
            (keydown.enter)="toggleChangelog()" (keydown.space)="$event.preventDefault(); toggleChangelog()">v{{ version }}@if (!production) { <span class="dev-badge">dev</span>}</span>
      <span class="footer-sep">·</span>
      <a class="feedback-link" routerLink="/help">{{ 'nav.help' | translate }}</a>
      <span class="footer-sep">·</span>
      <a class="feedback-link" href="https://github.com/kahalm/rookhub/issues" target="_blank" rel="noopener noreferrer">{{ 'app.feedback' | translate }}</a>
      <span class="footer-sep">·</span>
      <a class="discord-link" [href]="discordUrl" target="_blank" rel="noopener noreferrer"
         [attr.aria-label]="'nav.discord' | translate">
        <mat-icon svgIcon="discord" aria-hidden="true"></mat-icon><span>{{ 'nav.discord' | translate }}</span>
      </a>
      <span class="footer-sep">·</span>
      <a class="kofi-link" [href]="kofiUrl" target="_blank" rel="noopener noreferrer"
         [attr.aria-label]="'nav.support' | translate">
        <mat-icon aria-hidden="true">local_cafe</mat-icon><span>{{ 'nav.support' | translate }}</span>
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
          @for (item of quickstartItems; track item.key) {
            <a class="qs-item" [routerLink]="item.link" (click)="showQuickstart = false">
              <span class="qs-icon" aria-hidden="true">{{ item.icon }}</span>
              <div>
                <strong>{{ 'app.qs.' + item.key + 'Title' | translate }}</strong><br>
                <span class="qs-desc">{{ 'app.qs.' + item.key + 'Desc' | translate }}</span>
              </div>
            </a>
          }
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
    .conn-banner {
      background: #e65100; color: #fff; padding: 6px 14px; font-size: 0.85rem; font-weight: 500;
      position: sticky; top: 0; z-index: 1100;
    }
    .conn-banner.conn-offline { background: #455a64; }
    .conn-row { display: flex; align-items: center; justify-content: center; gap: 12px; flex-wrap: wrap; }
    .conn-btn {
      background: rgba(255,255,255,0.18); color: #fff; border: 1px solid rgba(255,255,255,0.5);
      border-radius: 4px; padding: 3px 10px; cursor: pointer; font: inherit; font-weight: 600;
    }
    .conn-btn:hover { background: rgba(255,255,255,0.3); }
    .conn-details { max-width: 640px; margin: 6px auto 2px; font-weight: 400; text-align: left; }
    .conn-details p { margin: 4px 0; }
    .conn-details ul { margin: 4px 0 4px 18px; padding: 0; }
    .conn-details li { margin-bottom: 3px; }
    .app-footer { text-align: center; padding: 8px; color: color-mix(in srgb, currentColor 47%, transparent); font-size: 0.75rem; }
    /* App-Vollbild: Kopf- und Fußleiste weg, der Inhalt (v. a. das Brett) bekommt den ganzen
       Schirm. Gesteuert über die Host-Klasse (JS-Flag), nicht über :root:fullscreen — so zählt
       ein einzelnes Brett im Vollbild nicht mit. */
    :host(.app-fullscreen) app-navbar,
    :host(.app-fullscreen) .app-footer { display: none; }
    .app-fs-exit {
      position: fixed;
      top: 6px; right: 6px;
      z-index: 1000;
      width: 30px; height: 30px;
      display: grid; place-items: center;
      padding: 0; border: 0; border-radius: 6px;
      cursor: pointer;
      background: rgba(0, 0, 0, 0.35);
      color: #fff;
      opacity: 0.35;
      transition: opacity 0.12s ease-in-out;
    }
    .app-fs-exit:hover, .app-fs-exit:focus-visible { opacity: 1; background: rgba(0, 0, 0, 0.6); }
    .app-fs-exit mat-icon { font-size: 20px; width: 20px; height: 20px; }
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
    .kofi-link {
      display: inline-flex; align-items: center; gap: 4px; vertical-align: middle;
      color: #ff5e5b; font-weight: 600; text-decoration: none;
    }
    .kofi-link:hover { color: #e04b48; text-decoration: underline; }
    .kofi-link mat-icon {
      font-size: 1.05rem; width: 1.05rem; height: 1.05rem; line-height: 1.05rem;
    }
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
    /* Jeder Eintrag ist ein Link direkt in den Modus — der Schnellstart erscheint nach der
       Registrierung, da ist „wo klicke ich jetzt?" die eigentliche Frage. */
    .qs-item {
      display: flex; gap: 12px; align-items: flex-start; margin-bottom: 6px;
      color: inherit; text-decoration: none;
      padding: 8px 10px; margin-left: -10px; margin-right: -10px;
      border-radius: 6px; border: 1px solid transparent;
    }
    .qs-item:hover, .qs-item:focus-visible {
      background: rgba(255,255,255,0.06); border-color: rgba(255,255,255,0.18);
    }
    .qs-item:focus-visible { outline: 2px solid #90caf9; outline-offset: 1px; }
    .qs-item strong { color: #90caf9; }
    .qs-icon { font-size: 1.4rem; min-width: 28px; text-align: center; }
    .qs-desc { font-size: 0.85rem; color: #aaa; }
  `]
})
export class AppComponent implements OnInit {
  version = environment.version;
  production = environment.production;
  /** Changelog-Eintraege — LEER bis zum ersten Oeffnen des Overlays: das Array (~0,9 MB Prosa,
   *  changelog-data.ts) wird bewusst per dynamic import() nachgeladen statt eager gebundelt,
   *  sonst laege die komplette Versionshistorie im Initial-Bundle (groesster Perf-Hebel). */
  changelog: ChangelogEntry[] = [];
  /** Einladungslink zum öffentlichen RookHub-Discord (Community) — prominent in der Fußzeile. */
  readonly discordUrl = DISCORD_INVITE_URL;
  readonly kofiUrl = KOFI_URL;
  showChangelog = false;
  showQuickstart = false;

  /**
   * Die Einträge des Schnellstarts — Titel/Beschreibung kommen aus `app.qs.<key>Title|Desc`.
   * Jeder Eintrag führt DIREKT in den Modus (der Schnellstart poppt nach der Registrierung auf,
   * dort hilft ein Link mehr als eine Beschreibung).
   *
   * `mate` zeigt auf den öffentlichen Kurs „Mate in 1/2/3" (Polgar 5334, Prod-Buch 340) und startet
   * ihn SEQUENZIELL — die Aufgaben stehen dort nach Schwierigkeit, das ist für Neulinge der
   * sinnvolle Einstieg. Die Buch-Id ist umgebungsabhängig: existiert sie nicht (Dev), fängt der
   * `courseAccessGuard` das ab und leitet weiter, statt eine leere Seite zu zeigen.
   */
  /** Buch-Id des öffentlichen Kurses „Mate in 1/2/3" (Prod). Eine Stelle, falls sie sich ändert.
   *  MUSS vor `quickstartItems` stehen — sonst nutzt der Feld-Initialisierer sie vor der Deklaration. */
  static readonly MateCourseBookId = 340;

  readonly quickstartItems: { key: string; icon: string; link: string }[] = [
    { key: 'random',  icon: '\u{1F3B2}', link: '/puzzles' },
    { key: 'mate',    icon: '\u265B',    link: `/courses/${AppComponent.MateCourseBookId}/sequential` },
    { key: 'endless', icon: '\u267E',    link: '/puzzles/endless' },
    { key: 'daily',   icon: '\u{1F4C5}', link: '/puzzles/daily/today' },
    { key: 'weekly',  icon: '\u{1F4F0}', link: '/weekly' },
  ];

  showApkUpdate = false;
  /** Details-Panel des Verbindungs-Banners (Offline-Info bzw. VPN/DNS-Hinweise) ausgeklappt? */
  showConnDetails = false;
  private readonly APK_UPDATE_LS_KEY = 'rookhub_apk_seen_version';

  /** Escape schließt das offene Overlay (Changelog/Quickstart) — Tastatur-Bedienbarkeit. */
  @HostListener('document:keydown.escape')
  onEscape(): void { this.showChangelog = false; this.showQuickstart = false; }

  /** Laufender Changelog-Nachlade-Vorgang — Feld, damit Tests den async-Ablauf awaiten können. */
  changelogLoad?: Promise<void>;

  /** Overlay öffnen (Navbar-Menü) — lädt die Einträge beim ersten Öffnen nach. */
  openChangelog(): void {
    this.showChangelog = true;
    this.changelogLoad = this.loadChangelog();
  }

  /** Overlay per Footer-Versionslink auf-/zuklappen. */
  toggleChangelog(): void {
    this.showChangelog = !this.showChangelog;
    if (this.showChangelog) this.changelogLoad = this.loadChangelog();
  }

  /**
   * Changelog-Daten lazy laden: erst beim Öffnen des Overlays, genau einmal. Fehlschlag
   * (offline ohne SW-Cache / Chunk nach Deploy weg) bleibt still — das Overlay zeigt dann nur
   * den Kopf, der nächste Öffnen-Versuch lädt erneut (kein „loaded"-Flag bei Fehler).
   */
  private async loadChangelog(): Promise<void> {
    if (this.changelog.length > 0) return;
    try {
      const m = await import('../environments/changelog-data');
      this.changelog = m.CHANGELOG;
    } catch { /* naechstes Oeffnen versucht es erneut */ }
  }

  private dlHandled = false;

  /** Changelog-Eintrag in der aktiven UI-Sprache (de → Deutsch, sonst Englisch als Default/Fallback). */
  changeText(change: { en: string; de: string }): string {
    return this.translate.currentLang() === 'de' ? change.de : change.en;
  }

  private destroyRef = inject(DestroyRef);

  /**
   * App-Vollbild aktiv (der Navbar-Schalter hat das GANZE Dokument ins Vollbild geschickt)?
   * Blendet als Host-Klasse Navbar + Fußzeile aus — der Inhalt bekommt den maximalen Platz.
   * Ein einzelnes Brett im Vollbild zählt bewusst NICHT (dort bleibt die App unsichtbar,
   * es gibt nichts auszublenden).
   */
  @HostBinding('class.app-fullscreen') appFullscreen = false;

  exitAppFullscreen(): void {
    void exitFullscreen();
  }

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
    // App-weit instanziieren: holt den CDK-Overlay-Container im Vollbild ins Vollbild-Element,
    // sonst sind Dialoge/Snackbars/Menüs dort unsichtbar (modale hingen die App fest).
    _fullscreenOverlay: FullscreenOverlayService,
    private offlinePrefetch: OfflinePrefetchService,
    private clientLog: ClientLogService,
    // Verbindungs-Banner (offline / Server unerreichbar) in der App-Shell.
    readonly connectivity: ConnectivityService,
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
    // Wiederhergestellte Verbindung (nach „Server unerreichbar") als Diagnose-Event melden —
    // macht Client-seitige Ausfälle (VPN-/DNS-Blockade) in Kibana sichtbar.
    connectivity.reportRecovery = (kind, detail) => clientLog.report(kind, detail);
  }

  ngOnInit(): void {
    // App-Vollbild-Zustand nachführen (Navbar-Schalter, Esc, F11-Wechsel).
    const offFs = onFullscreenChange(() => this.appFullscreen = isFullscreen(document.documentElement));
    this.destroyRef.onDestroy(offFs);

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
      // Gescheiterte Update-Installation (Hash-Mismatch, fehlende Chunks nach Deploy, Abbruch)
      // an die API melden (→ Kibana) — die Vorstufe des unrecoverable-Zustands. Nur Telemetrie.
      this.swUpdate.versionUpdates
        .pipe(filter((e): e is VersionInstallationFailedEvent => e.type === 'VERSION_INSTALLATION_FAILED'), takeUntilDestroyed(this.destroyRef))
        .subscribe(e => this.clientLog.report('sw_install_failed', e.error));
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

    // Persistenten Storage anfordern: Android evict bei Speicherdruck einzelne SW-Cache-Einträge
    // (z. B. einen Lazy-Chunk, während die referenzierende index.html bleibt) — die deploy-
    // unabhängige Hauptursache des unrecoverable-Zustands (angular/angular#36539). persist()
    // nimmt unseren Storage von dieser Auto-Eviction aus; installierte PWAs/TWAs bekommen das
    // auf Android i. d. R. ohne Nutzer-Prompt. Fire-and-forget, Ablehnung nur Telemetrie.
    this.storagePersist = this.requestPersistentStorage();

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

  /** Laufende Persistenz-Anfrage — Feld, damit Tests den async-Ablauf deterministisch awaiten können. */
  storagePersist?: Promise<void>;

  /** In Methode gekapselt, damit Tests den StorageManager stubben können. */
  protected storageManager(): StorageManager | undefined {
    return typeof navigator !== 'undefined' ? navigator.storage : undefined;
  }

  /** Best-effort `navigator.storage.persist()`; nur eine ABLEHNUNG wird gemeldet (Grant = Normalfall). */
  private async requestPersistentStorage(): Promise<void> {
    try {
      const storage = this.storageManager();
      if (!storage?.persist || !storage.persisted) return; // API nicht verfügbar (alte Browser)
      if (await storage.persisted()) return;               // schon persistent → nichts zu tun
      const granted = await storage.persist();
      if (!granted) this.clientLog.report('storage_persist_denied');
    } catch { /* optionale API — Fehler bewusst still */ }
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
