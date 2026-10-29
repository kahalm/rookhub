import { HandoffService } from '../core/handoff.service';
import { Component, EventEmitter, Output, OnInit, DestroyRef, ElementRef, NgZone, inject, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { of, switchMap, catchError, merge, map, timer } from 'rxjs';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';
import { MatMenuModule } from '@angular/material/menu';
import { IconLabelDirective } from '../shared/icon-label/icon-label.directive';
import { MatBadgeModule } from '@angular/material/badge';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../core/auth.service';
import { CourseService } from '../features/courses/course.service';
import { CatalogService } from '../features/catalog/catalog.service';
import { MenuService } from '../core/menu.service';
import { InAppNotificationService, AppNotification } from '../core/in-app-notification.service';
import { MessageService } from '../core/message.service';
import { notificationText, notificationIcon } from '../core/notification-text';
import { LocaleService } from '../core/locale.service';
import { ThemeService, AppTheme } from '../core/theme.service';
import { EinkService } from '../core/eink.service';
import { DISCORD_INVITE_URL, DISCORD_SVG, FEEDBACK_URL, KOFI_URL } from '../core/community';
import { environment } from '../../environments/environment';
import { LEGAL_SITE } from '../core/legal-site';
import { canOpenAdmin } from '../features/admin/admin-tabs';
import { authLinkQuery, isAuthPage } from '../core/return-url.util';
import {
  fullscreenSupported, isFullscreen, onFullscreenChange, toggleFullscreen,
} from '../shared/fullscreen/fullscreen.util';
import { trackNavEdge } from '../shared/navbar/nav-edge';
import { LanguageMenu, languageMenu } from '../core/language-menu';
import { PctPipe } from '../shared/format/pct';

/** Body-Klasse: die Seite hat eine eigene Werkzeugleiste, die Kopfleiste dockt oben rechts darauf an (0.785.x). */
export const NAV_DOCKED_CLASS = 'nav-docked';
/** Breite der Kopfleiste als CSS-Variable am Wurzelelement — die andockende Seite hält sie frei. */
export const NAV_WIDTH_VAR = '--rh-nav-w';

/** Misst die Breite der Kopfleiste (ResizeObserver) und legt sie als {@link NAV_WIDTH_VAR} ab; ausgeblendet = 0 px. */
export function trackNavWidth(host: HTMLElement, destroyRef: DestroyRef): void {
  if (typeof ResizeObserver === 'undefined') return;
  const root = host.ownerDocument.documentElement;
  const sync = () => root.style.setProperty(NAV_WIDTH_VAR, `${Math.ceil(host.getBoundingClientRect().width)}px`);
  const ro = new ResizeObserver(sync);
  ro.observe(host);
  sync();
  destroyRef.onDestroy(() => { ro.disconnect(); root.style.removeProperty(NAV_WIDTH_VAR); });
}

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule, IconLabelDirective, MatBadgeModule, TranslatePipe, PctPipe],
  template: `
    <mat-toolbar color="primary">
      <!-- Links, keine span/button mit routerLink (UX-059): nur ein Anker ist per Enter bedienbar, fuer Screenreader
           ein Link und laesst sich per Mittelklick in einem neuen Tab oeffnen. mat-button auf <a> sieht gleich aus. -->
      <a class="logo" routerLink="/">RookHub</a>
      <span class="spacer"></span>
      @if (auth.isLoggedIn) {
        <!-- UI-Entrümpelung Navbar: 3 Icons — Vollbild (oft benutzt), Glocke, Menü.
             Nachrichten + alles andere (auch das Konto) liegen im einen ☰-Menü;
             ungelesene Nachrichten färben das ☰ und tragen ein Badge. -->
        @if (fsSupported) {
          <button mat-icon-button (click)="toggleAppFullscreen()"
                  [appIconLabel]="fsLabel">
            <mat-icon>{{ fsActive ? 'fullscreen_exit' : 'fullscreen' }}</mat-icon>
          </button>
        }
        <button mat-icon-button [matMenuTriggerFor]="notifMenu" (menuOpened)="onBellOpened()"
                class="notif-bell" [class.has-unseen]="notifCount > 0"
                matBadge="!" [matBadgeHidden]="notifCount === 0" matBadgeColor="warn" matBadgeSize="medium"
                [appIconLabel]="('notifications.title' | translate) + (notifCount > 0 ? ' (' + notifCount + ')' : '')">
          <mat-icon>notifications</mat-icon>
        </button>
        <mat-menu #notifMenu="matMenu" class="notif-menu">
          <div class="notif-header">
            <span class="notif-header-title">{{ 'notifications.title' | translate }}</span>
            @if (hasUnseen()) {
              <button mat-icon-button class="notif-mark-all" (click)="markAllRead($event)"
                      [appIconLabel]="'notifications.markAllRead' | translate">
                <mat-icon>done_all</mat-icon>
              </button>
            }
          </div>
          @if (notifications.length === 0) {
            <div class="notif-empty">{{ 'notifications.empty' | translate }}</div>
          } @else {
            @for (n of notifications; track n.id) {
              <button mat-menu-item class="notif-item" [class.notif-unseen]="!n.seen" (click)="openNotification(n)">
                <mat-icon>{{ iconFor(n) }}</mat-icon>
                <span class="notif-text">{{ textFor(n) }}</span>
              </button>
            }
          }
          <button mat-menu-item class="notif-all" routerLink="/notifications">
            <mat-icon>history</mat-icon>
            <span>{{ 'notifications.viewAll' | translate }}</span>
          </button>
        </mat-menu>
        <button mat-icon-button [matMenuTriggerFor]="navMenu" class="msg-mail" [class.has-unseen]="messagesUnread > 0"
                [matBadge]="messagesUnread" [matBadgeHidden]="messagesUnread === 0" matBadgeColor="warn" matBadgeSize="small"
                [attr.aria-label]="'nav.menu' | translate">
          <mat-icon>menu</mat-icon>
        </button>
        <!-- ☰: kurzes Hauptmenü mit Untermenüs je Kategorie statt 19 flacher Einträge. -->
        <mat-menu #navMenu="matMenu" class="nav-main-menu">
          @if (can('dashboard')) {
            <button mat-menu-item routerLink="/dashboard">
              <mat-icon>dashboard</mat-icon><span>{{ 'nav.dashboard' | translate }}</span>
            </button>
          }
          <button mat-menu-item routerLink="/messages" [class.msg-item-unseen]="messagesUnread > 0">
            <mat-icon>mail</mat-icon>
            <span>{{ 'messages.title' | translate }}@if (messagesUnread > 0) { ({{ messagesUnread }})}</span>
          </button>
          @if (anyTraining) {
            <button mat-menu-item [matMenuTriggerFor]="trainingMenu">
              <mat-icon>fitness_center</mat-icon><span>{{ 'nav.groupTraining' | translate }}</span>
            </button>
          }
          @if (anyLibrary) {
            <button mat-menu-item [matMenuTriggerFor]="libraryMenu">
              <mat-icon>collections_bookmark</mat-icon><span>{{ 'nav.groupLibrary' | translate }}</span>
            </button>
          }
          <button mat-menu-item [matMenuTriggerFor]="communityMenu">
            <mat-icon>groups</mat-icon><span>{{ 'nav.groupCommunity' | translate }}</span>
          </button>
          <button mat-menu-item [matMenuTriggerFor]="userMenu">
            <mat-icon>account_circle</mat-icon><span>{{ 'nav.account' | translate }}</span>
          </button>
          @if (showAdmin) {
            <button mat-menu-item routerLink="/admin">
              <mat-icon>admin_panel_settings</mat-icon><span>{{ 'nav.admin' | translate }}</span>
            </button>
          }
        </mat-menu>
        <mat-menu #trainingMenu="matMenu" class="nav-submenu">
          @if (can('puzzles')) { <button mat-menu-item routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button> }
          @if (showCourses && can('courses')) {
            <button mat-menu-item routerLink="/courses">{{ 'nav.courses' | translate }}</button>
          }
          @if (can('woodpecker')) { <button mat-menu-item routerLink="/woodpecker">{{ 'nav.woodpecker' | translate }}</button> }
          @if (can('weekly')) { <button mat-menu-item routerLink="/weekly">{{ 'nav.weekly' | translate }}</button> }
          @if (can('training-goals')) { <button mat-menu-item routerLink="/training-goals">{{ 'nav.trainingGoals' | translate }}</button> }
          @if (can('repertoires')) { <button mat-menu-item routerLink="/repertoires">{{ 'nav.repertoires' | translate }}</button> }
          @if (can('guess')) { <button mat-menu-item routerLink="/guess">{{ 'nav.guess' | translate }}</button> }
          @if (can('favorites')) { <button mat-menu-item routerLink="/favorites">{{ 'nav.favorites' | translate }}</button> }
          @if (can('worksheets')) { <button mat-menu-item routerLink="/worksheets">{{ 'nav.worksheets' | translate }}</button> }
        </mat-menu>
        <mat-menu #libraryMenu="matMenu" class="nav-submenu">
          @if (can('analysis')) { <button mat-menu-item routerLink="/analysis">{{ 'nav.analysis' | translate }}</button> }
          @if (auth.has('prep.view')) { <button mat-menu-item routerLink="/prep">{{ 'nav.prep' | translate }}</button> }
          @if (can('games')) { <button mat-menu-item routerLink="/games">{{ 'nav.games' | translate }}</button> }
          @if (can('scoresheet')) { <button mat-menu-item routerLink="/games/scoresheet">{{ 'nav.scoresheet' | translate }}</button> }
          @if (can('reconstruct')) { <button mat-menu-item routerLink="/reconstruct">{{ 'nav.reconstruct' | translate }}</button> }
          @if (can('remembered')) { <button mat-menu-item routerLink="/remembered">{{ 'nav.remembered' | translate }}</button> }
          @if (can('ebooks')) { <button mat-menu-item routerLink="/ebooks">{{ 'nav.ebooks' | translate }}</button> }
          @if (showCatalog && can('catalog')) {
            <button mat-menu-item routerLink="/catalog">{{ 'nav.catalog' | translate }}</button>
          }
        </mat-menu>
        <mat-menu #communityMenu="matMenu" class="nav-submenu">
          @if (can('friends')) { <button mat-menu-item routerLink="/friends">{{ 'nav.friends' | translate }}</button> }
          <!-- Turniere leben seit der Herausloesung auf der eigenen Seite; der Sprung nimmt die
               Anmeldung mit (Einmal-Code, siehe HandoffService). -->
          @if (turnierUrl) {
            <button mat-menu-item (click)="toTurnier()">
              <mat-icon>open_in_new</mat-icon> {{ 'nav.tournaments' | translate }}
            </button>
          }
          @if (can('leaderboards')) { <button mat-menu-item routerLink="/leaderboards">{{ 'nav.leaderboards' | translate }}</button> }
          <a mat-menu-item [href]="discordUrl" target="_blank" rel="noopener noreferrer">
            <mat-icon svgIcon="discord"></mat-icon>
            <span>{{ 'nav.discord' | translate }}</span>
          </a>
          <a mat-menu-item [href]="kofiUrl" target="_blank" rel="noopener noreferrer">
            <mat-icon>local_cafe</mat-icon>
            <span>{{ 'nav.support' | translate }}</span>
          </a>
        </mat-menu>
        <!-- Konto-Untermenü — einziger Ort für Profil/Statistik/Einstellungen/Abmelden. -->
        <mat-menu #userMenu="matMenu" class="nav-submenu">
          <button mat-menu-item routerLink="/profile">{{ 'nav.profile' | translate }}</button>
          @if (can('stats')) { <button mat-menu-item routerLink="/stats">{{ 'nav.stats' | translate }}</button> }
          <button mat-menu-item routerLink="/puzzles/endless/history">{{ 'nav.puzzleHistory' | translate }}</button>
          @if (can('chessable')) { <button mat-menu-item routerLink="/chessable">{{ 'nav.chessable' | translate }}</button> }
          @if (can('install')) { <button mat-menu-item routerLink="/install">{{ 'nav.installApp' | translate }}</button> }
          @if (can('help')) { <button mat-menu-item routerLink="/help">{{ 'nav.help' | translate }}</button> }
          <!-- Rueckmeldung und Version: am Handy ist die Fusszeile aus, das Menue ist der einzige Weg (UX-053). -->
          <a mat-menu-item class="feedback-item" [href]="feedbackUrl" target="_blank" rel="noopener noreferrer">{{ 'app.feedback' | translate }}</a>
          <!-- Eingeloggt der einzige Weg zu den Rechtsseiten am Handy (die Fusszeile ist dort aus, UX-017). -->
          <button mat-menu-item class="legal-item" routerLink="/privacy">{{ 'legal.privacy.link' | translate }}</button>
          @if (imprint) { <button mat-menu-item class="legal-item" routerLink="/impressum">{{ 'legal.impressum.title' | translate }}</button> }
          <!-- d5-rk-konto-menue-sprache-icon: ohne Globus wie alle Nachbarn im Konto-Menü (fluchtet mit Profil, Statistik …);
               das Untermenü zeigt der Pfeil ▸. Im Gast-Menü bleibt der Globus — dort tragen die Nachbarn Symbole. -->
          <button mat-menu-item class="lang-item" [matMenuTriggerFor]="langMenu">{{ 'nav.language' | translate }}</button>
          <button mat-menu-item class="changelog-item" (click)="changelogClick.emit()">{{ 'nav.changelog' | translate }} <span class="menu-version">v{{ version }}</span></button>
          <button mat-menu-item (click)="auth.logout()">{{ 'nav.logout' | translate }}</button>
        </mat-menu>
      } @else {
        <!-- Ausgeloggt (entrümpelt wie die eingeloggte Leiste): nur Puzzles/Analyse als
             Text-Links (≤768px im ☰), alles Sekundäre (Hilfe/Info/Discord/Theme/Vollbild/
             Sprache) liegt IMMER im ☰-Menü statt als Icon-Reihe in der Leiste. -->
        @if (can('puzzles')) { <a mat-button class="nav-anon" routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</a> }
        @if (can('analysis')) { <a mat-button class="nav-anon" routerLink="/analysis">{{ 'nav.analysis' | translate }}</a> }
        <!-- Auf den Anmelde-/Passwortseiten kein Vollbild (nutzt dort nichts) und unten keine Kopie der Karten-Links
             „Anmelden"/„Registrieren" — auf /register standen sonst zwei gleiche Knoepfe (Codereview W5 UX-052). -->
        @if (fsSupported && !onAuthPage) {
          <button mat-icon-button class="anon-wide" (click)="toggleAppFullscreen()"
                  [appIconLabel]="fsLabel">
            <mat-icon>{{ fsActive ? 'fullscreen_exit' : 'fullscreen' }}</mat-icon>
          </button>
        }
        <!-- Unter 480px nur „Anmelden" (kompakt, gefuellt) und ☰ in der Leiste; „Registrieren" und Vollbild stehen dann
             im ☰-Menue (.anon-narrow) — vier Ziele drängten sich sonst ohne Abstand bis an den Rand. -->
        @if (!onAuthPage) {
          <a mat-button class="nav-login" routerLink="/login" [queryParams]="authQuery">{{ 'nav.login' | translate }}</a>
          <!-- color="primary": die Einladung an Gaeste ist die Primaeraktion der Leiste (gefuellt, styles.scss).
               b1-rk-navbar-gast-menue-mitte: steht direkt neben „Anmelden", das ☰ ganz am Rand (wie angemeldet). -->
          <a mat-flat-button color="primary" class="anon-wide nav-register" routerLink="/register" [queryParams]="authQuery">{{ 'nav.register' | translate }}</a>
        }
        <button mat-icon-button class="anon-menu" [matMenuTriggerFor]="anonMenu" [attr.aria-label]="'nav.menu' | translate">
          <mat-icon>menu</mat-icon>
        </button>
        <mat-menu #anonMenu="matMenu" class="nav-main-menu">
          @if (!onAuthPage) {
            <a mat-menu-item class="anon-narrow" routerLink="/register" [queryParams]="authQuery">
              <mat-icon>person_add</mat-icon>
              <span>{{ 'nav.register' | translate }}</span>
            </a>
            @if (fsSupported) {
              <button mat-menu-item class="anon-narrow" (click)="toggleAppFullscreen()">
                <mat-icon>{{ fsActive ? 'fullscreen_exit' : 'fullscreen' }}</mat-icon>
                <span>{{ fsLabel }}</span>
              </button>
            }
          }
          @if (can('puzzles')) { <button mat-menu-item routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button> }
          @if (can('analysis')) { <button mat-menu-item routerLink="/analysis">{{ 'nav.analysis' | translate }}</button> }
          @if (can('help')) { <button mat-menu-item routerLink="/help">{{ 'nav.help' | translate }}</button> }
          <button mat-menu-item class="legal-item" routerLink="/privacy">{{ 'legal.privacy.link' | translate }}</button>
          @if (imprint) { <button mat-menu-item class="legal-item" routerLink="/impressum">{{ 'legal.impressum.title' | translate }}</button> }
          <button mat-menu-item (click)="quickstartClick.emit()">
            <mat-icon>info_outline</mat-icon>
            <span>{{ 'nav.info' | translate }}</span>
          </button>
          <!-- Rueckmeldung und Version: am Handy ist die Fusszeile aus, das Menue ist der einzige Weg (UX-053). -->
          <a mat-menu-item class="feedback-item" [href]="feedbackUrl" target="_blank" rel="noopener noreferrer">
            <mat-icon>bug_report</mat-icon>
            <span>{{ 'app.feedback' | translate }}</span>
          </a>
          <button mat-menu-item class="changelog-item" (click)="changelogClick.emit()">
            <mat-icon>history</mat-icon>
            <span>{{ 'nav.changelog' | translate }} <span class="menu-version">v{{ version }}</span></span>
          </button>
          <a mat-menu-item [href]="discordUrl" target="_blank" rel="noopener noreferrer">
            <mat-icon svgIcon="discord"></mat-icon>
            <span>{{ 'nav.discord' | translate }}</span>
          </a>
          <a mat-menu-item [href]="kofiUrl" target="_blank" rel="noopener noreferrer">
            <mat-icon>local_cafe</mat-icon>
            <span>{{ 'nav.support' | translate }}</span>
          </a>
          <!-- FALLE: Der Theme-Umschalter sitzt sonst NUR in der Profil-Theme-Karte, und die
               setzt einen Login voraus. Da der ThemeService als Default hart „dark" fährt,
               steckten Anonyme auf den offenen Seiten (/puzzles, /analysis, /g/, /help)
               unumkehrbar im Dunkel-Theme. Für Eingeloggte bleibt das Profil die einzige Stelle. -->
          <!-- d3-rk-theme-menu-label-zustand: Untermenü System/Hell/Dunkel mit Häkchen (wie Sprache) statt eines
               Umschalters, der den IST-Zustand nannte und beim Tippen zum nächsten weiterschaltete.
               d3-rk-helles-design-bei-eink: im E-Ink-Modus bleibt die App immer hell (DESIGN 3.6) — statt des Eintrags
               steht der Grund da, keine wirkungslose Wahl. -->
          @if (eink.enabled()) {
            <!-- Kein gesperrter mat-menu-item: der wäre 38 % blass (Bedeutung in hellem Grau, DESIGN 3.6). -->
            <div class="theme-eink-note" role="note">
              <mat-icon>contrast</mat-icon>
              <span>{{ 'profile.theme.einkForcesLight' | translate }}</span>
            </div>
          } @else {
            <button mat-menu-item [matMenuTriggerFor]="themeMenu" class="theme-menu-trigger">
              <mat-icon>contrast</mat-icon>
              <span>{{ 'nav.theme' | translate }}</span>
            </button>
          }
          <button mat-menu-item [matMenuTriggerFor]="langMenu">
            <mat-icon>language</mat-icon>
            <span>{{ 'nav.language' | translate }}</span>
          </button>
        </mat-menu>
      }
      <mat-menu #themeMenu="matMenu" class="nav-submenu">
        <div class="lang-menu-label">{{ 'nav.theme' | translate }}</div>
        @for (t of themeOptions; track t) {
          <button mat-menu-item class="theme-option" (click)="theme.setPreference(t)"
                  [attr.aria-checked]="theme.preference === t" role="menuitemradio">
            <mat-icon>{{ theme.preference === t ? 'check' : themeIcons[t] }}</mat-icon>
            <span>{{ 'profile.theme.' + t | translate }}</span>
          </button>
        }
      </mat-menu>
      <mat-menu #langMenu="matMenu" class="nav-submenu">
        <div class="lang-menu-label">{{ 'nav.language' | translate }}</div>
        @for (l of langGroups.full; track l.code) {
          <button mat-menu-item (click)="locale.use(l.code)">
            <mat-icon>{{ locale.current === l.code ? 'check' : 'translate' }}</mat-icon>
            <span>{{ l.label }}</span>
          </button>
        }
        <!-- F8-009: Teilübersetzungen mit Anteil, darunter fällt der Text auf Englisch zurück. -->
        <div class="lang-menu-label lang-partial">{{ 'nav.languagePartial' | translate }}</div>
        @for (l of langGroups.partial; track l.code) {
          <button mat-menu-item class="lang-partial-item" (click)="locale.use(l.code)">
            <mat-icon>{{ locale.current === l.code ? 'check' : 'translate' }}</mat-icon>
            <span>{{ l.label }} ({{ l.coverage | pct }})</span>
          </button>
        }
      </mat-menu>
    </mat-toolbar>
  `,
  styles: [`
    .logo { cursor: pointer; font-weight: bold; font-size: 1.3em; color: inherit; text-decoration: none; }
    .spacer { flex: 1 1 auto; }
    .menu-version { margin-left: 4px; font-size: 0.85em; color: color-mix(in srgb, currentColor 60%, transparent); }
    .theme-eink-note {
      display: flex; align-items: flex-start; gap: 16px; padding: 12px 16px; max-width: 280px;
      font-size: 0.875rem; line-height: 1.3; color: var(--mat-sys-on-surface-variant);
    }
    .theme-eink-note mat-icon { flex: none; }
    .lang-menu-label { padding: 8px 16px 4px; font-size: 0.75rem; color: color-mix(in srgb, currentColor 47%, transparent); text-transform: uppercase; }
    /* F8-009: Trennlinie vor den Teilübersetzungen. */
    .lang-menu-label.lang-partial { margin-top: 4px; padding-top: 12px; border-top: 1px solid color-mix(in srgb, currentColor 15%, transparent); }
    .notif-header { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 4px 8px 4px 16px; }
    .notif-header-title { font-size: 0.75rem; font-weight: 600; color: color-mix(in srgb, currentColor 47%, transparent); text-transform: uppercase; }
    .notif-mark-all { width: 32px; height: 32px; line-height: 32px; padding: 0; }
    .notif-mark-all mat-icon { font-size: 19px; width: 19px; height: 19px; }
    .notif-empty { padding: 8px 16px 12px; font-size: 0.9rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .notif-item .notif-text { white-space: normal; line-height: 1.25; }
    .notif-item.notif-unseen { font-weight: 600; }
    .notif-item.notif-unseen mat-icon { color: var(--mat-sys-primary); }
    /* Auffälliger Hinweis bei neuen Benachrichtigungen: fettes rotes „!" + leichtes Pulsieren. */
    .notif-bell.has-unseen mat-icon { color: var(--rh-error); }
    /* Mail-Symbol ebenfalls rot, sobald ungelesene Nachrichten vorliegen. */
    .msg-mail.has-unseen mat-icon { color: var(--rh-error); }
    .msg-item-unseen { font-weight: 600; }
    .msg-item-unseen mat-icon { color: var(--rh-error); }
    .notif-bell ::ng-deep .mat-badge-content {
      background: #d32f2f; color: #fff; font-weight: 800; font-size: 15px;
      width: 19px; height: 19px; line-height: 19px;
      box-shadow: 0 0 0 2px var(--mat-sys-surface, #fff);
    }
    .notif-bell.has-unseen ::ng-deep .mat-badge-content { animation: bell-pulse 1.5s ease-in-out infinite; }
    @keyframes bell-pulse {
      0%, 100% { transform: scale(1); }
      50% { transform: scale(1.28); }
    }
    @media (prefers-reduced-motion: reduce) {
      .notif-bell.has-unseen ::ng-deep .mat-badge-content { animation: none; }
    }
    @media (max-width: 768px) {
      /* Ausgeloggte Text-Links (Puzzles/Analyse) wandern auf schmalen Schirmen ins ☰-Menü —
         sonst läuft die Toolbar über die Viewport-Breite und die ganze Seite scrollt horizontal. */
      .nav-anon { display: none; }
    }
    /* Gast-Leiste am Handy: „Registrieren" und Vollbild wandern ins ☰ (dort .anon-narrow, breiter ausgeblendet),
       „Anmelden" wird der eine gefuellte Knopf — Tippziele 44px, 8px Abstand, Rand zum Bildschirm. */
    /* b1-rk-nav-konto-untermenue-handy: am Handy passt ein Untermenü weder links noch rechts neben das ☰-Menü — die CDK
       schob es an x = 0 und es schnitt die Symbole des Hauptmenüs an. Unter 600 px liegt es deckungsgleich über dem
       Hauptmenü (8 px vom Rand, gleiche Breite). Die Regel greift nur, wenn die CDK die Lage fest setzt (geschoben). */
    /* d8-rk-konto-menue-doppelt: die Höhe legt weiter die CDK fest (an der Zeile „Konto“, nach oben geschoben) — oben ragte
       das Hauptmenü heraus, seitlich standen zwei Rahmen versetzt. Solange ein Untermenü offen ist, sind alle Menüs darunter
       (☰, bei Konto → Sprache auch Konto) unsichtbar: eine Fläche. Ein Klick daneben schließt wie bisher alle. */
    @media (max-width: 599.98px) {
      ::ng-deep .cdk-overlay-pane:has(> .nav-submenu) { left: auto !important; right: 8px !important; }
      ::ng-deep .nav-submenu.mat-mdc-menu-panel { min-width: 232px; }
      ::ng-deep .cdk-overlay-container
        :is(.cdk-overlay-connected-position-bounding-box, .cdk-overlay-pane):has(.nav-main-menu, .nav-submenu):has(~ * .nav-submenu) {
        visibility: hidden;
      }
    }
    @media (min-width: 481px) { .anon-narrow { display: none; } .nav-register { margin-inline: 8px 4px; } }
    @media (max-width: 480px) {
      .anon-wide { display: none; }
      mat-toolbar { padding: 0 8px 0 14px; }
      .nav-login {
        min-height: 44px; padding: 0 14px; margin-right: 8px;
        background: var(--mat-sys-primary);
        --mat-button-text-label-text-color: var(--mat-sys-on-primary);
        --mat-button-text-state-layer-color: var(--mat-sys-on-primary);
      }
      /* Die Toolbar setzt die Schriftfarbe ihrer Knöpfe selbst — ohne diese Regel stand helle Schrift auf dem hellen
         Primär-Hintergrund (geprüft per UI-Sweep 2026-10-10). */
      .nav-login.mat-mdc-button { color: var(--mat-sys-on-primary); }
      .anon-menu { width: 44px; height: 44px; padding: 10px; }
    }
    /* UI-Sweep u1 (u1-rk-navbar-raender): Breitbild-Stufe ab 2400 px (DESIGN 4.1) — der Hintergrund bleibt randlos,
       Logo und Symbole rücken auf die Inhaltskante (statt 1 100 px vom Inhalt entfernt am Fensterrand).
       b1-rk-kopfleiste-ultrawide-flucht: die Kante ist die der JEWEILIGEN Seite — --nav-edge misst trackNavEdge
       (nav-edge.ts) am Seiteninhalt in <main>; Rückfall --page-max-width-wide (1 880 px).
       Kalkulations-Modus: die schwebende Leiste behält ihr 4-px-Polster — die Regel in styles.scss
       (body.calc-mode app-navbar mat-toolbar) ist spezifischer und gewinnt. */
    @media (min-width: 2400px) {
      mat-toolbar { padding-inline: max(16px, calc((100% - var(--nav-edge, var(--page-max-width-wide, 1880px))) / 2 + 16px)); }
    }
    :host { display: block; }
    /* EINE Leiste (0.785.x, E-Book-Leser kompakt, DESIGN 4.5): Seiten mit eigener Werkzeugleiste setzen body.nav-docked
       (NAV_DOCKED_CLASS) — die Kopfleiste verliert Logo und Hintergrund und legt sich als 48 px hoher Block oben rechts
       AUF die Leiste der Seite (Glocke, ☰, Vollbild bleiben dieselben Knöpfe und Menüs). Die Seite hält rechts so viel
       frei, wie die Leiste breit ist (--rh-nav-w, gemessen in ngOnInit). z-index unter den CDK-Overlays (1000) und unter
       dem Handy-Vollbild-Brett des Lesers (900), das sie verdecken darf (es hat einen eigenen Schließen-Knopf).
       Spezifischer als die 2400-px-Regel oben: keine Flucht-Polsterung, die Leiste steht wie die der Seite am Rand. */
    :host-context(body.nav-docked) { position: fixed; top: 0; right: 0; z-index: 800; width: auto; }
    :host-context(body.nav-docked) mat-toolbar {
      --mat-toolbar-standard-height: 48px; --mat-toolbar-mobile-height: 48px;
      width: auto; height: 48px; min-height: 48px; padding: 0 4px; gap: 0; background: transparent; box-shadow: none;
    }
    :host-context(body.nav-docked) .logo,
    :host-context(body.nav-docked) .spacer,
    :host-context(body.nav-docked) .nav-anon { display: none; }
    /* d3-rk-leser-glocken-abzeichen-abgeschnitten: angedockt sitzt die Leiste am Fensterrand (y = 0) — das Abzeichen
       (über dem Knopf, Rand −12 px, pulsiert ×1,28) ragte darüber hinaus und wurde oben abgeschnitten. */
    :host-context(body.nav-docked) .notif-bell.mat-badge-overlap ::ng-deep .mat-badge-content,
    :host-context(body.nav-docked) .msg-mail.mat-badge-overlap ::ng-deep .mat-badge-content { top: 4px; bottom: auto; margin-top: 0; }
    /* Niedrige Fenster (styles.scss, DESIGN 4.6 „Niedrige Fenster“): die Leiste ist dort 48 px hoch — das Abzeichen
       ragte wie angedockt über den Fensterrand und wurde oben abgeschnitten.
       d7-rk-glocke-abzeichen-angeschnitten: Materials „.mat-badge-medium.mat-badge-overlap .mat-badge-content
       { margin: -12px }“ hat dieselbe Spezifität und lädt später (erst beim ersten Abzeichen) — margin-top blieb −12 px.
       :host + .mat-badge-overlap heben die Regel darüber; die waagerechte Überlappung (margin-left) bleibt. */
    @media (max-width: 768px) and (max-height: 720px) {
      :host .notif-bell.mat-badge-overlap ::ng-deep .mat-badge-content,
      :host .msg-mail.mat-badge-overlap ::ng-deep .mat-badge-content { top: 4px; bottom: auto; margin-top: 0; }
    }
  `]
})
export class NavbarComponent implements OnInit {
  /** Turniere sind eine eigene Seite — Sprung mit Anmelde-Uebergabe statt Router-Link. */
  private handoff = inject(HandoffService);
  get turnierUrl(): string | null { return this.handoff.partnerUrl; }
  // Ziel ist der KALENDER, nicht die Liste der schon geholten Turniere — dasselbe Startziel,
  // das die Turnierseite auch beim direkten Aufruf waehlt (siehe deren app.routes).
  toTurnier(): void { void this.handoff.jump('tournaments/calendar'); }

  /** Impressum im ☰-Menue (neben dem Datenschutz) — nur, wo LEGAL_SITE eins vorsieht (UX-017). */
  readonly imprint = inject(LEGAL_SITE).imprint;

  /** „Anmelden“/„Registrieren“ fuehren hierher zurueck — auf der Maske selbst zu deren Ziel (UX-020). */
  get authQuery(): { returnUrl?: string } { return authLinkQuery(this.router.url); }
  /** Auf den Auth-Seiten blendet die Gast-Leiste Vollbild und „Anmelden“/„Registrieren“ aus (UX-052). */
  get onAuthPage(): boolean { return isAuthPage(this.router.url); }

  @Output() changelogClick = new EventEmitter<void>();
  @Output() quickstartClick = new EventEmitter<void>();

  /** Kurse-Menü sichtbar: Admin (sofort) oder Nicht-Admin mit mind. einem freigegebenen Kurs. */
  showCourses = false;
  showCatalog = false;

  /** Admin-konfigurierte Sichtbarkeit der Menüeinträge (Snapshot für synchrones Binding). */
  visible = new Set<string>();
  can(key: string): boolean { return this.visible.has(key); }

  /** Kategorie-Untermenüs nur zeigen, wenn mindestens ein Eintrag darin sichtbar ist.
   *  (Community braucht keinen Getter — der Discord-Link ist immer da.) */
  get anyTraining(): boolean {
    return this.can('puzzles') || this.can('weekly') || this.can('training-goals')
      || this.can('repertoires') || this.can('guess') || this.can('favorites')
      || this.can('worksheets') || this.can('woodpecker') || (this.showCourses && this.can('courses'));
  }
  get anyLibrary(): boolean {
    return this.can('analysis') || this.can('games') || this.can('scoresheet') || this.can('reconstruct') || this.can('remembered')
      || this.can('ebooks') || (this.showCatalog && this.can('catalog')) || this.auth.has('prep.view');
  }

  /** Glocken-Badge: Anzahl ungelesener In-App-Benachrichtigungen. */
  notifCount = 0;
  /** Im Dropdown angezeigte Benachrichtigungen (beim Öffnen geladen). */
  notifications: AppNotification[] = [];

  /** Mail-Badge: ungelesene Admin-Nachrichten des Users. */
  messagesUnread = 0;

  private destroyRef = inject(DestroyRef);
  /** Optional: einige Specs bauen die Navbar ohne Host-Element (reiner Injektor). */
  private host = inject<ElementRef<HTMLElement>>(ElementRef, { optional: true });
  private zone = inject(NgZone);

  /** Einladungslink zum öffentlichen RookHub-Discord (Community). */
  readonly discordUrl = DISCORD_INVITE_URL;
  readonly kofiUrl = KOFI_URL;
  /** Issue-Tracker und Versionsnummer im ☰-Menü — am Handy ist die Fußzeile aus (UX-053). */
  readonly feedbackUrl = FEEDBACK_URL;
  readonly version = environment.version;

  // ===== App-Vollbild ======================================================
  // Schaltet die GANZE GUI (documentElement) ins echte Vollbild — anders als der Brett-
  // Vollbild-Knopf, der nur das Brett zeigt. Da das gesamte Dokument im Vollbild-Teilbaum
  // liegt, funktionieren hier auch CDK-Overlays (Tooltips/Menüs/Dialoge) normal weiter.
  readonly fsSupported = fullscreenSupported();
  fsActive = false;

  get fsLabel(): string {
    return this.translate.instant(this.fsActive ? 'nav.fullscreenExit' : 'nav.fullscreen');
  }

  toggleAppFullscreen(): void {
    void toggleFullscreen(document.documentElement);
  }

  // ===== Theme (nur im ☰-Menü der Ausgeloggten) ============================
  /** Wahlmöglichkeiten im Untermenü „Design“; die aktive trägt das Häkchen. */
  readonly themeOptions: readonly AppTheme[] = ['system', 'light', 'dark'];
  readonly themeIcons: Record<AppTheme, string> = { system: 'brightness_auto', light: 'light_mode', dark: 'dark_mode' };
  /** E-Ink-Modus: Design-Wahl wirkungslos (immer hell) — der Menüeintrag sagt das statt sie anzubieten. */
  readonly eink = inject(EinkService);

  /** Sprachmenü: gepflegte Sprachen oben, Teilübersetzungen mit Prozent darunter (F8-009) — erst beim Zeichnen. */
  private langGroupsCache?: LanguageMenu;
  get langGroups(): LanguageMenu { return this.langGroupsCache ??= languageMenu(this.locale.languages); }

  constructor(public auth: AuthService, private courseService: CourseService, private catalogService: CatalogService, private menu: MenuService, private notif: InAppNotificationService, private messages: MessageService, public locale: LocaleService, public theme: ThemeService, private translate: TranslateService, private router: Router, iconRegistry: MatIconRegistry, sanitizer: DomSanitizer) {
    // Discord-Markenlogo als SVG-Icon registrieren (nicht im Material-Standardsatz enthalten).
    iconRegistry.addSvgIconLiteral('discord', sanitizer.bypassSecurityTrustHtml(DISCORD_SVG));
  }

  ngOnInit(): void {
    // App-Vollbild-Zustand nachführen (auch bei Esc). Der Knopf zählt nur dann als „aktiv",
    // wenn wirklich das GANZE Dokument im Vollbild ist — nicht ein einzelnes Brett.
    if (this.fsSupported) {
      const off = onFullscreenChange(() => this.fsActive = isFullscreen(document.documentElement));
      this.destroyRef.onDestroy(off);
    }

    // b1-rk-kopfleiste-ultrawide-flucht: ab 2400 px fluchtet der Kopfinhalt mit der Inhaltskante der Seite.
    if (this.host) trackNavEdge(this.host.nativeElement, this.router, this.zone, this.destroyRef);

    // EINE Leiste (body.nav-docked): die Seite hält rechts genau die Breite der angedockten Kopfleiste frei.
    if (this.host) trackNavWidth(this.host.nativeElement, this.destroyRef);

    // Admin-konfigurierte Menü-Sichtbarkeit live übernehmen.
    this.menu.visible$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(set => this.visible = set);

    // Bei jedem Login/Logout neu bestimmen, ob das Kurse-Menü gezeigt wird. switchMap bricht
    // einen laufenden checkAccess()-Call bei erneutem Login-State-Wechsel ab (kein Leak/Race);
    // takeUntilDestroyed räumt die Subscription auf.
    // Neu bestimmen bei Login/Logout UND wenn sich der Kurs-Zugriff ändert (z. B. nach Buch-Import).
    merge(
      this.auth.currentUser$.pipe(map(() => undefined)),
      this.courseService.accessChanged$,
    ).pipe(
      switchMap(() => {
        if (!this.auth.isLoggedIn) return of(false);
        if (this.auth.isAdmin) return of(true);
        return this.courseService.checkAccess().pipe(
          map(r => r.hasAccess),
          catchError(() => of(false)),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(show => this.showCourses = show);

    // Katalog-Link nur zeigen, wenn dem User ein Katalog freigegeben ist (oder Admin).
    this.auth.currentUser$.pipe(
      switchMap(() => this.auth.isLoggedIn
        ? this.catalogService.access().pipe(map(r => r.hasAccess), catchError(() => of(false)))
        : of(false)),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(show => this.showCatalog = show);

    // Glocken-Badge: ungelesene In-App-Benachrichtigungen. Zähler-Strom binden, bei Login/Logout
    // sofort aktualisieren und im Hintergrund alle 60 s nachziehen (zeigt „Neues" ohne Reload).
    this.notif.unseenCount$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(c => this.notifCount = c);
    this.messages.userUnread$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(c => this.messagesUnread = c);
    this.auth.currentUser$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (this.auth.isLoggedIn) { this.notif.refreshCount(); this.messages.refreshUserUnread(); }
      else { this.notif.reset(); this.messages.reset(); this.notifications = []; }
    });
    timer(0, 60000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (this.auth.isLoggedIn) { this.notif.refreshCount(); this.messages.refreshUserUnread(); }
    });
  }

  /** Glocke geöffnet: nur die UNGELESENEN laden — gelesene verschwinden aus der Glocke und
   * bleiben nur über „Alle anzeigen" (History) sichtbar. */
  onBellOpened(): void {
    this.notif.list(20, true).subscribe({ next: list => this.notifications = list, error: () => {} });
  }

  /** Gibt es im aktuell geladenen Dropdown ungelesene Benachrichtigungen? (steuert den „Alle als gelesen"-Button) */
  hasUnseen(): boolean {
    return this.notifications.some(n => !n.seen);
  }

  /** „Alle als gelesen markieren": Badge leeren + die Glocke leeren (gelesene bleiben über
   * „Alle anzeigen" sichtbar), ohne das Menü zu schließen. */
  markAllRead(event: Event): void {
    event.stopPropagation();
    this.notif.markAllSeen().subscribe({ error: () => {} });
    this.notifications = [];
  }

  /** Admin-Eintrag: dieselbe Regel wie adminAreaGuard (F5-005) — wer irgendeinen Admin-Tab sehen darf. */
  get showAdmin(): boolean { return canOpenAdmin(this.auth); }

  /** Klick auf eine Benachrichtigung → als gelesen markieren (verschwindet aus der Glocke)
   * + zur hinterlegten Route navigieren. Admin-Deep-Links (/admin?tab=…) prüft adminAreaGuard je Recht. */
  openNotification(n: AppNotification): void {
    if (!n.seen) { this.notif.markSeen(n.id).subscribe({ error: () => {} }); }
    this.notifications = this.notifications.filter(x => x.id !== n.id);
    if (n.link) this.router.navigateByUrl(n.link);
  }

  /** Material-Icon je Benachrichtigungstyp. */
  iconFor(n: AppNotification): string {
    return notificationIcon(n);
  }

  /** Lokalisierter Text; Typen mit Ausgang (gelöst/gescheitert) wählen die passende Variante. */
  textFor(n: AppNotification): string {
    return notificationText(this.translate, n);
  }
}
