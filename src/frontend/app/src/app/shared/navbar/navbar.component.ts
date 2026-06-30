import { Component, EventEmitter, Output, OnInit, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { of, switchMap, catchError, merge, map, timer } from 'rxjs';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatBadgeModule } from '@angular/material/badge';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { CourseService } from '../../features/courses/course.service';
import { MenuService } from '../../core/menu.service';
import { InAppNotificationService, AppNotification } from '../../core/in-app-notification.service';
import { MessageService } from '../../core/message.service';
import { notificationText, notificationIcon } from '../../core/notification-text';
import { LocaleService } from '../../core/locale.service';
import { ThemeService, AppTheme } from '../../core/theme.service';
import { DISCORD_INVITE_URL } from '../../core/community';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, MatBadgeModule, TranslateModule],
  template: `
    <mat-toolbar color="primary">
      <span class="logo" routerLink="/dashboard">RookHub</span>
      <span class="spacer"></span>
      @if (auth.isLoggedIn) {
        <div class="nav-links">
          @if (can('dashboard')) { <button mat-button routerLink="/dashboard">{{ 'nav.dashboard' | translate }}</button> }
          @if (can('repertoires')) { <button mat-button routerLink="/repertoires">{{ 'nav.repertoires' | translate }}</button> }
          @if (can('tournaments')) { <button mat-button routerLink="/tournaments">{{ 'nav.tournaments' | translate }}</button> }
          @if (can('friends')) { <button mat-button routerLink="/friends">{{ 'nav.friends' | translate }}</button> }
          @if (can('puzzles')) { <button mat-button routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button> }
          @if (can('favorites')) { <button mat-button routerLink="/favorites">{{ 'nav.favorites' | translate }}</button> }
          @if (can('training-goals')) { <button mat-button routerLink="/training-goals">{{ 'nav.trainingGoals' | translate }}</button> }
          @if (can('analysis')) { <button mat-button routerLink="/analysis">{{ 'nav.analysis' | translate }}</button> }
          @if (can('games')) { <button mat-button routerLink="/games">{{ 'nav.games' | translate }}</button> }
          @if (can('weekly')) { <button mat-button routerLink="/weekly">{{ 'nav.weekly' | translate }}</button> }
          @if (showCourses && can('courses')) {
            <button mat-button routerLink="/courses">{{ 'nav.courses' | translate }}</button>
          }
          @if (can('leaderboards')) { <button mat-button routerLink="/leaderboards">{{ 'nav.leaderboards' | translate }}</button> }
          @if (auth.isAdmin) {
            <button mat-button routerLink="/admin">{{ 'nav.admin' | translate }}</button>
          }
        </div>
        <button mat-icon-button class="mobile-menu-btn" [matMenuTriggerFor]="navMenu" [attr.aria-label]="'nav.menu' | translate">
          <mat-icon>menu</mat-icon>
        </button>
        <mat-menu #navMenu="matMenu">
          @if (can('dashboard')) { <button mat-menu-item routerLink="/dashboard">{{ 'nav.dashboard' | translate }}</button> }
          @if (can('repertoires')) { <button mat-menu-item routerLink="/repertoires">{{ 'nav.repertoires' | translate }}</button> }
          @if (can('tournaments')) { <button mat-menu-item routerLink="/tournaments">{{ 'nav.tournaments' | translate }}</button> }
          @if (can('friends')) { <button mat-menu-item routerLink="/friends">{{ 'nav.friends' | translate }}</button> }
          @if (can('puzzles')) { <button mat-menu-item routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button> }
          @if (can('favorites')) { <button mat-menu-item routerLink="/favorites">{{ 'nav.favorites' | translate }}</button> }
          @if (can('training-goals')) { <button mat-menu-item routerLink="/training-goals">{{ 'nav.trainingGoals' | translate }}</button> }
          @if (can('analysis')) { <button mat-menu-item routerLink="/analysis">{{ 'nav.analysis' | translate }}</button> }
          @if (can('games')) { <button mat-menu-item routerLink="/games">{{ 'nav.games' | translate }}</button> }
          @if (can('weekly')) { <button mat-menu-item routerLink="/weekly">{{ 'nav.weekly' | translate }}</button> }
          @if (showCourses && can('courses')) {
            <button mat-menu-item routerLink="/courses">{{ 'nav.courses' | translate }}</button>
          }
          @if (can('leaderboards')) { <button mat-menu-item routerLink="/leaderboards">{{ 'nav.leaderboards' | translate }}</button> }
          @if (auth.isAdmin) {
            <button mat-menu-item routerLink="/admin">{{ 'nav.admin' | translate }}</button>
          }
          <a mat-menu-item [href]="discordUrl" target="_blank" rel="noopener noreferrer">
            <mat-icon svgIcon="discord"></mat-icon>
            <span>{{ 'nav.discord' | translate }}</span>
          </a>
          <button mat-menu-item (click)="theme.toggle()">
            <mat-icon>{{ themeIcon }}</mat-icon>
            <span>{{ themeTooltip }}</span>
          </button>
          <button mat-menu-item [matMenuTriggerFor]="langMenu">
            <mat-icon>language</mat-icon>
            <span>{{ 'nav.language' | translate }}</span>
          </button>
        </mat-menu>
        <button mat-icon-button routerLink="/messages" class="msg-mail" [class.has-unseen]="messagesUnread > 0"
                [matBadge]="messagesUnread" [matBadgeHidden]="messagesUnread === 0" matBadgeColor="warn" matBadgeSize="small"
                [matTooltip]="('messages.title' | translate) + (messagesUnread > 0 ? ' (' + messagesUnread + ')' : '')"
                [attr.aria-label]="'messages.title' | translate">
          <mat-icon>mail</mat-icon>
        </button>
        <button mat-icon-button [matMenuTriggerFor]="notifMenu" (menuOpened)="onBellOpened()"
                class="notif-bell" [class.has-unseen]="notifCount > 0"
                matBadge="!" [matBadgeHidden]="notifCount === 0" matBadgeColor="warn" matBadgeSize="medium"
                [matTooltip]="('notifications.title' | translate) + (notifCount > 0 ? ' (' + notifCount + ')' : '')"
                [attr.aria-label]="'notifications.title' | translate">
          <mat-icon>notifications</mat-icon>
        </button>
        <mat-menu #notifMenu="matMenu" class="notif-menu">
          <div class="notif-header">
            <span class="notif-header-title">{{ 'notifications.title' | translate }}</span>
            @if (hasUnseen()) {
              <button mat-icon-button class="notif-mark-all" (click)="markAllRead($event)"
                      [matTooltip]="'notifications.markAllRead' | translate"
                      [attr.aria-label]="'notifications.markAllRead' | translate">
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
        <a mat-icon-button [href]="discordUrl" target="_blank" rel="noopener noreferrer"
           class="discord-link nav-extra" [matTooltip]="'nav.discord' | translate" [attr.aria-label]="'nav.discord' | translate">
          <mat-icon svgIcon="discord"></mat-icon>
        </a>
        <button mat-icon-button class="nav-extra" (click)="theme.toggle()" [matTooltip]="themeTooltip" [attr.aria-label]="themeTooltip">
          <mat-icon>{{ themeIcon }}</mat-icon>
        </button>
        <button mat-icon-button class="nav-extra" [matMenuTriggerFor]="langMenu" [attr.aria-label]="'nav.language' | translate">
          <mat-icon>language</mat-icon>
        </button>
        <button mat-icon-button [matMenuTriggerFor]="userMenu" [attr.aria-label]="'nav.account' | translate">
          <mat-icon>account_circle</mat-icon>
        </button>
        <mat-menu #userMenu="matMenu">
          <button mat-menu-item routerLink="/profile">{{ 'nav.profile' | translate }}</button>
          @if (can('stats')) { <button mat-menu-item routerLink="/stats">{{ 'nav.stats' | translate }}</button> }
          <button mat-menu-item routerLink="/puzzles/endless/history">{{ 'nav.puzzleHistory' | translate }}</button>
          @if (can('chessable')) { <button mat-menu-item routerLink="/chessable">{{ 'nav.chessable' | translate }}</button> }
          @if (can('install')) { <button mat-menu-item routerLink="/install">{{ 'nav.installApp' | translate }}</button> }
          @if (can('help')) { <button mat-menu-item routerLink="/help">{{ 'nav.help' | translate }}</button> }
          <button mat-menu-item (click)="changelogClick.emit()">{{ 'nav.changelog' | translate }}</button>
          <button mat-menu-item (click)="auth.logout()">{{ 'nav.logout' | translate }}</button>
        </mat-menu>
      } @else {
        @if (can('puzzles')) { <button mat-button routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button> }
        @if (can('analysis')) { <button mat-button routerLink="/analysis">{{ 'nav.analysis' | translate }}</button> }
        @if (can('help')) {
        <button mat-icon-button routerLink="/help" [matTooltip]="'nav.help' | translate" [attr.aria-label]="'nav.help' | translate">
          <mat-icon>help_outline</mat-icon>
        </button>
        }
        <button mat-icon-button (click)="quickstartClick.emit()" [attr.aria-label]="'nav.info' | translate">
          <mat-icon>info_outline</mat-icon>
        </button>
        <a mat-icon-button [href]="discordUrl" target="_blank" rel="noopener noreferrer"
           class="discord-link" [matTooltip]="'nav.discord' | translate" [attr.aria-label]="'nav.discord' | translate">
          <mat-icon svgIcon="discord"></mat-icon>
        </a>
        <button mat-icon-button (click)="theme.toggle()" [matTooltip]="themeTooltip" [attr.aria-label]="themeTooltip">
          <mat-icon>{{ themeIcon }}</mat-icon>
        </button>
        <button mat-icon-button [matMenuTriggerFor]="langMenu" [attr.aria-label]="'nav.language' | translate">
          <mat-icon>language</mat-icon>
        </button>
        <button mat-button routerLink="/login">{{ 'nav.login' | translate }}</button>
        <button mat-raised-button routerLink="/register">{{ 'nav.register' | translate }}</button>
      }
      <mat-menu #langMenu="matMenu">
        <div class="lang-menu-label">{{ 'nav.language' | translate }}</div>
        @for (l of locale.languages; track l.code) {
          <button mat-menu-item (click)="locale.use(l.code)">
            <mat-icon>{{ locale.current === l.code ? 'check' : 'translate' }}</mat-icon>
            <span>{{ l.label }}</span>
          </button>
        }
      </mat-menu>
    </mat-toolbar>
  `,
  styles: [`
    .logo { cursor: pointer; font-weight: bold; font-size: 1.3em; }
    .spacer { flex: 1 1 auto; }
    .mobile-menu-btn { display: none; }
    .lang-menu-label { padding: 8px 16px 4px; font-size: 0.75rem; color: color-mix(in srgb, currentColor 47%, transparent); text-transform: uppercase; }
    .notif-header { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 4px 8px 4px 16px; }
    .notif-header-title { font-size: 0.75rem; font-weight: 600; color: color-mix(in srgb, currentColor 47%, transparent); text-transform: uppercase; }
    .notif-mark-all { width: 32px; height: 32px; line-height: 32px; padding: 0; }
    .notif-mark-all mat-icon { font-size: 19px; width: 19px; height: 19px; }
    .notif-empty { padding: 8px 16px 12px; font-size: 0.9rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .notif-item .notif-text { white-space: normal; line-height: 1.25; }
    .notif-item.notif-unseen { font-weight: 600; }
    .notif-item.notif-unseen mat-icon { color: var(--mat-sys-primary, #3f51b5); }
    /* Auffälliger Hinweis bei neuen Benachrichtigungen: fettes rotes „!" + leichtes Pulsieren. */
    .notif-bell.has-unseen mat-icon { color: #d32f2f; }
    /* Mail-Symbol ebenfalls rot, sobald ungelesene Nachrichten vorliegen. */
    .msg-mail.has-unseen mat-icon { color: #d32f2f; }
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
      .nav-links { display: none; }
      .mobile-menu-btn { display: inline-flex; }
      /* Sekundäraktionen (Discord/Theme/Sprache) wandern auf Mobil ins Hamburger-Menü,
         damit die Toolbar nicht überläuft und das Profil-Icon nicht aus dem Bild geschoben wird. */
      .nav-extra { display: none; }
    }
  `]
})
export class NavbarComponent implements OnInit {
  @Output() changelogClick = new EventEmitter<void>();
  @Output() quickstartClick = new EventEmitter<void>();

  /** Kurse-Menü sichtbar: Admin (sofort) oder Nicht-Admin mit mind. einem freigegebenen Kurs. */
  showCourses = false;

  /** Admin-konfigurierte Sichtbarkeit der Menüeinträge (Snapshot für synchrones Binding). */
  visible = new Set<string>();
  can(key: string): boolean { return this.visible.has(key); }

  /** Glocken-Badge: Anzahl ungelesener In-App-Benachrichtigungen. */
  notifCount = 0;
  /** Im Dropdown angezeigte Benachrichtigungen (beim Öffnen geladen). */
  notifications: AppNotification[] = [];

  /** Mail-Badge: ungelesene Admin-Nachrichten des Users. */
  messagesUnread = 0;

  private destroyRef = inject(DestroyRef);

  get themeIcon(): string {
    const icons: Record<AppTheme, string> = { system: 'brightness_auto', light: 'light_mode', dark: 'dark_mode' };
    return icons[this.theme.preference];
  }

  get themeTooltip(): string {
    const labels: Record<AppTheme, string> = {
      system: this.translate.instant('nav.themeSystem'),
      light: this.translate.instant('nav.themeLight'),
      dark: this.translate.instant('nav.themeDark'),
    };
    return labels[this.theme.preference];
  }

  /** Einladungslink zum öffentlichen RookHub-Discord (Community). */
  readonly discordUrl = DISCORD_INVITE_URL;

  constructor(public auth: AuthService, private courseService: CourseService, private menu: MenuService, private notif: InAppNotificationService, private messages: MessageService, public locale: LocaleService, public theme: ThemeService, private translate: TranslateService, private router: Router, iconRegistry: MatIconRegistry, sanitizer: DomSanitizer) {
    // Discord-Markenlogo als SVG-Icon registrieren (nicht im Material-Standardsatz enthalten).
    iconRegistry.addSvgIconLiteral('discord', sanitizer.bypassSecurityTrustHtml(
      '<svg viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">' +
      '<path d="M20.317 4.369a19.79 19.79 0 0 0-4.885-1.515.074.074 0 0 0-.079.037c-.21.375-.444.864-.608 1.249a18.27 18.27 0 0 0-5.487 0 12.64 12.64 0 0 0-.617-1.25.077.077 0 0 0-.079-.036A19.736 19.736 0 0 0 3.677 4.37a.07.07 0 0 0-.032.027C.533 9.046-.32 13.58.099 18.057a.082.082 0 0 0 .031.057 19.9 19.9 0 0 0 5.993 3.03.078.078 0 0 0 .084-.028c.462-.63.874-1.295 1.226-1.994a.076.076 0 0 0-.041-.106 13.107 13.107 0 0 1-1.872-.892.077.077 0 0 1-.008-.128 10.2 10.2 0 0 0 .372-.291.074.074 0 0 1 .077-.01c3.928 1.793 8.18 1.793 12.062 0a.074.074 0 0 1 .078.009c.12.099.246.198.373.292a.077.077 0 0 1-.006.127 12.3 12.3 0 0 1-1.873.891.077.077 0 0 0-.041.107c.36.698.772 1.362 1.225 1.993a.076.076 0 0 0 .084.028 19.839 19.839 0 0 0 6.002-3.03.077.077 0 0 0 .032-.054c.5-5.177-.838-9.674-3.549-13.66a.061.061 0 0 0-.031-.03zM8.02 15.331c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.956-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.956 2.418-2.157 2.418zm7.975 0c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.955-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.946 2.418-2.157 2.418z"/>' +
      '</svg>'
    ));
  }

  ngOnInit(): void {
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

  /** Klick auf eine Benachrichtigung → als gelesen markieren (verschwindet aus der Glocke)
   * + zur hinterlegten Route navigieren. */
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
