import { Component, EventEmitter, Output, OnInit, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { of, switchMap, catchError, merge, map } from 'rxjs';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatBadgeModule } from '@angular/material/badge';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { CourseService } from '../../features/courses/course.service';
import { MenuService } from '../../core/menu.service';
import { ChallengeService } from '../../core/challenge.service';
import { LocaleService } from '../../core/locale.service';
import { ThemeService, AppTheme } from '../../core/theme.service';

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
          @if (can('friends')) { <button mat-button routerLink="/friends" [matBadge]="challengeCount" [matBadgeHidden]="challengeCount === 0" matBadgeColor="warn" matBadgeSize="small">{{ 'nav.friends' | translate }}</button> }
          @if (can('puzzles')) { <button mat-button routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button> }
          @if (can('training-goals')) { <button mat-button routerLink="/training-goals">{{ 'nav.trainingGoals' | translate }}</button> }
          @if (can('analysis')) { <button mat-button routerLink="/analysis">{{ 'nav.analysis' | translate }}</button> }
          @if (can('weekly')) { <button mat-button routerLink="/weekly">{{ 'nav.weekly' | translate }}</button> }
          @if (showCourses && can('courses')) {
            <button mat-button routerLink="/courses">{{ 'nav.courses' | translate }}</button>
          }
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
          @if (can('friends')) { <button mat-menu-item routerLink="/friends" [matBadge]="challengeCount" [matBadgeHidden]="challengeCount === 0" matBadgeColor="warn" matBadgeSize="small" matBadgeOverlap="false">{{ 'nav.friends' | translate }}</button> }
          @if (can('puzzles')) { <button mat-menu-item routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button> }
          @if (can('training-goals')) { <button mat-menu-item routerLink="/training-goals">{{ 'nav.trainingGoals' | translate }}</button> }
          @if (can('analysis')) { <button mat-menu-item routerLink="/analysis">{{ 'nav.analysis' | translate }}</button> }
          @if (can('weekly')) { <button mat-menu-item routerLink="/weekly">{{ 'nav.weekly' | translate }}</button> }
          @if (showCourses && can('courses')) {
            <button mat-menu-item routerLink="/courses">{{ 'nav.courses' | translate }}</button>
          }
          @if (auth.isAdmin) {
            <button mat-menu-item routerLink="/admin">{{ 'nav.admin' | translate }}</button>
          }
        </mat-menu>
        <button mat-icon-button (click)="theme.toggle()" [matTooltip]="themeTooltip" [attr.aria-label]="themeTooltip">
          <mat-icon>{{ themeIcon }}</mat-icon>
        </button>
        <button mat-icon-button [matMenuTriggerFor]="langMenu" [attr.aria-label]="'nav.language' | translate">
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
    @media (max-width: 768px) {
      .nav-links { display: none; }
      .mobile-menu-btn { display: inline-flex; }
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

  /** Anzahl offener eingehender Puzzle-Challenges (Badge am Freunde-Menü). */
  challengeCount = 0;

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

  constructor(public auth: AuthService, private courseService: CourseService, private menu: MenuService, private challenge: ChallengeService, public locale: LocaleService, public theme: ThemeService, private translate: TranslateService) {}

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

    // Badge offener Challenges: reaktiv aus dem ChallengeService, neu laden bei jedem Login.
    this.challenge.incomingCount$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(c => this.challengeCount = c);
    this.auth.currentUser$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (this.auth.isLoggedIn) this.challenge.refreshCount();
      else this.challengeCount = 0;
    });
  }
}
