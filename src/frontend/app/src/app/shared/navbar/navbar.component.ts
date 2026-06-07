import { Component, EventEmitter, Output, OnInit, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { of, switchMap, catchError } from 'rxjs';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { CourseService } from '../../features/courses/course.service';
import { LocaleService } from '../../core/locale.service';
import { ThemeService, AppTheme } from '../../core/theme.service';
import { AppInstallDialogComponent } from '../app-install-dialog/app-install-dialog.component';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslateModule],
  template: `
    <mat-toolbar color="primary">
      <span class="logo" routerLink="/dashboard">RookHub</span>
      <span class="spacer"></span>
      @if (auth.isLoggedIn) {
        <div class="nav-links">
          <button mat-button routerLink="/dashboard">{{ 'nav.dashboard' | translate }}</button>
          @if (auth.isAdmin) {
            <button mat-button routerLink="/repertoires">{{ 'nav.repertoires' | translate }}</button>
          }
          <button mat-button routerLink="/tournaments">{{ 'nav.tournaments' | translate }}</button>
          <button mat-button routerLink="/friends">{{ 'nav.friends' | translate }}</button>
          <button mat-button routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button>
          <button mat-button routerLink="/training-goals">{{ 'nav.trainingGoals' | translate }}</button>
          <button mat-button routerLink="/analysis">{{ 'nav.analysis' | translate }}</button>
          <button mat-button routerLink="/weekly">{{ 'nav.weekly' | translate }}</button>
          @if (showCourses) {
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
          <button mat-menu-item routerLink="/dashboard">{{ 'nav.dashboard' | translate }}</button>
          @if (auth.isAdmin) {
            <button mat-menu-item routerLink="/repertoires">{{ 'nav.repertoires' | translate }}</button>
          }
          <button mat-menu-item routerLink="/tournaments">{{ 'nav.tournaments' | translate }}</button>
          <button mat-menu-item routerLink="/friends">{{ 'nav.friends' | translate }}</button>
          <button mat-menu-item routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button>
          <button mat-menu-item routerLink="/training-goals">{{ 'nav.trainingGoals' | translate }}</button>
          <button mat-menu-item routerLink="/analysis">{{ 'nav.analysis' | translate }}</button>
          <button mat-menu-item routerLink="/weekly">{{ 'nav.weekly' | translate }}</button>
          @if (showCourses) {
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
          <button mat-menu-item routerLink="/stats">{{ 'nav.stats' | translate }}</button>
          <button mat-menu-item routerLink="/puzzles/endless/history">{{ 'nav.puzzleHistory' | translate }}</button>
          <button mat-menu-item (click)="openInstall()">{{ 'nav.installApp' | translate }}</button>
          <button mat-menu-item (click)="changelogClick.emit()">{{ 'nav.changelog' | translate }}</button>
          <button mat-menu-item (click)="auth.logout()">{{ 'nav.logout' | translate }}</button>
        </mat-menu>
      } @else {
        <button mat-button routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button>
        <button mat-button routerLink="/analysis">{{ 'nav.analysis' | translate }}</button>
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

  constructor(public auth: AuthService, private courseService: CourseService, public locale: LocaleService, private dialog: MatDialog, public theme: ThemeService, private translate: TranslateService) {}

  /** Öffnet den Dialog mit Android-Installationsanleitung + APK-Download-Link. */
  openInstall(): void {
    this.dialog.open(AppInstallDialogComponent, { maxWidth: 480 });
  }

  ngOnInit(): void {
    // Bei jedem Login/Logout neu bestimmen, ob das Kurse-Menü gezeigt wird. switchMap bricht
    // einen laufenden checkAccess()-Call bei erneutem Login-State-Wechsel ab (kein Leak/Race);
    // takeUntilDestroyed räumt die Subscription auf.
    this.auth.currentUser$.pipe(
      switchMap(user => {
        if (!user) return of(false);
        if (this.auth.isAdmin) return of(true);
        return this.courseService.checkAccess().pipe(
          switchMap(r => of(r.hasAccess)),
          catchError(() => of(false)),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(show => this.showCourses = show);
  }
}
