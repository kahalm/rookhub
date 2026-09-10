import { HandoffService } from './core/handoff.service';
import { Component, OnInit, HostBinding, HostListener, DestroyRef, inject, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { A11yModule } from '@angular/cdk/a11y';
import { RouterOutlet, RouterLink, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';
import { NavbarComponent } from './shared/navbar/navbar.component';
import { DISCORD_SVG } from './core/community';
import { ImpersonationBannerComponent } from './shared/impersonation-banner/impersonation-banner.component';
import { AppFooterComponent } from './shared/app-footer/app-footer.component';
import { LocaleService } from './core/locale.service';
import { AuthService } from './core/auth.service';
import { MenuService } from './core/menu.service';
import { DiscordLinkService } from './core/discord-link.service';
import { OfflineQueueService } from './core/offline-queue.service';
import { FullscreenOverlayService } from './shared/fullscreen/fullscreen-overlay.service';
import { OfflinePrefetchService } from './core/offline-prefetch.service';
import { PwaInstallService } from './core/pwa-install.service';
import { ClientLogService } from './core/client-log.service';
import { AppUpdateService } from './core/app-update.service';
import { ConnectivityService } from './core/connectivity.service';
import { SnackbarService } from './core/snackbar.service';
import { StockfishService } from './features/puzzles/stockfish.service';
import { AnalysisEngineService } from './features/analysis/analysis-engine.service';
import { ThemeService } from './core/theme.service';
import {
  exitFullscreen, isFullscreen, onFullscreenChange,
} from './shared/fullscreen/fullscreen.util';
import { environment } from '../environments/environment';
import { APK_VERSION } from '../environments/changelog';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, NavbarComponent, TranslatePipe, A11yModule, MatIconModule, AppFooterComponent, ImpersonationBannerComponent],
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
    <app-impersonation-banner />
    <app-navbar (changelogClick)="footer.openChangelog()" (quickstartClick)="showQuickstart = true" />
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
    <app-footer #footer />
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
    /* App-Vollbild: Kopf- und Fußleiste weg, der Inhalt (v. a. das Brett) bekommt den ganzen
       Schirm. Gesteuert über die Host-Klasse (JS-Flag), nicht über :root:fullscreen — so zählt
       ein einzelnes Brett im Vollbild nicht mit. */
    :host(.app-fullscreen) app-navbar,
    /* Die Fusszeile ist eine eigene Komponente (AppFooterComponent) — hier zaehlt ihr
       HOST-Element. Die Klasse .app-footer liegt in deren eigener, gekapselter Ansicht und
       waere von hier aus nicht erreichbar. */
    :host(.app-fullscreen) app-footer { display: none; }
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
  private handoff = inject(HandoffService);

  // Version, Changelog-Overlay und die Fusszeilen-Links liegen in AppFooterComponent —
  // dieselbe Fusszeile benutzt auch die Turnierseite.
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
    { key: 'guess',   icon: '\u{1F3AF}', link: '/guess' },
    { key: 'daily',   icon: '\u{1F4C5}', link: '/puzzles/daily/today' },
    { key: 'weekly',  icon: '\u{1F4F0}', link: '/weekly' },
  ];

  showApkUpdate = false;
  /** Details-Panel des Verbindungs-Banners (Offline-Info bzw. VPN/DNS-Hinweise) ausgeklappt? */
  showConnDetails = false;
  private readonly APK_UPDATE_LS_KEY = 'rookhub_apk_seen_version';

  /**
   * Escape schliesst den Schnellstart — Tastatur-Bedienbarkeit. Das Changelog-Overlay schliesst
   * sich selbst (es gehoert zur Fusszeile, siehe `AppFooterComponent`).
   */
  @HostListener('document:keydown.escape')
  onEscape(): void { this.showQuickstart = false; }

  private dlHandled = false;

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
    private appUpdate: AppUpdateService,
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
    // Kommt der Aufrufer per Sprung von der Turnierseite, bringt er einen Einmal-Code mit — den
    // gegen eine eigene Anmeldung tauschen, bevor die erste Seite ihre Daten holt.
    void this.handoff.consumeIncoming();

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

    // Service Worker: neue Fassung, gescheiterte Installation, kaputter Zustand und die
    // aktive Suche - alles im geteilten AppUpdateService, den auch die Turnierseite nutzt.
    this.appUpdate.start(this.destroyRef);

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

  /** Laufende SW-Selbstheilung - Testhaken; die Arbeit macht der geteilte Dienst. */
  get swRecovery(): Promise<void> | undefined { return this.appUpdate.recovery; }

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

}
