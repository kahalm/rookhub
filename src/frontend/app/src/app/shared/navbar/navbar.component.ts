import { Component, EventEmitter, Output, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslateModule } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { CourseService } from '../../features/courses/course.service';
import { LocaleService } from '../../core/locale.service';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule, TranslateModule],
  template: `
    <mat-toolbar color="primary">
      <span class="logo" routerLink="/dashboard">RookHub</span>
      <span class="spacer"></span>
      @if (auth.isLoggedIn) {
        <div class="nav-links">
          <button mat-button routerLink="/dashboard">{{ 'nav.dashboard' | translate }}</button>
          <button mat-button routerLink="/repertoires">{{ 'nav.repertoires' | translate }}</button>
          <button mat-button routerLink="/tournaments">{{ 'nav.tournaments' | translate }}</button>
          <button mat-button routerLink="/friends">{{ 'nav.friends' | translate }}</button>
          <button mat-button routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button>
          <button mat-button routerLink="/weekly">{{ 'nav.weekly' | translate }}</button>
          @if (showCourses) {
            <button mat-button routerLink="/courses">{{ 'nav.courses' | translate }}</button>
          }
          @if (auth.isAdmin) {
            <button mat-button routerLink="/admin">{{ 'nav.admin' | translate }}</button>
          }
        </div>
        <button mat-icon-button class="mobile-menu-btn" [matMenuTriggerFor]="navMenu" aria-label="Menu">
          <mat-icon>menu</mat-icon>
        </button>
        <mat-menu #navMenu="matMenu">
          <button mat-menu-item routerLink="/dashboard">{{ 'nav.dashboard' | translate }}</button>
          <button mat-menu-item routerLink="/repertoires">{{ 'nav.repertoires' | translate }}</button>
          <button mat-menu-item routerLink="/tournaments">{{ 'nav.tournaments' | translate }}</button>
          <button mat-menu-item routerLink="/friends">{{ 'nav.friends' | translate }}</button>
          <button mat-menu-item routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button>
          <button mat-menu-item routerLink="/weekly">{{ 'nav.weekly' | translate }}</button>
          @if (showCourses) {
            <button mat-menu-item routerLink="/courses">{{ 'nav.courses' | translate }}</button>
          }
          @if (auth.isAdmin) {
            <button mat-menu-item routerLink="/admin">{{ 'nav.admin' | translate }}</button>
          }
        </mat-menu>
        <button mat-icon-button [matMenuTriggerFor]="langMenu" aria-label="Language">
          <mat-icon>language</mat-icon>
        </button>
        <button mat-icon-button [matMenuTriggerFor]="userMenu" aria-label="Account">
          <mat-icon>account_circle</mat-icon>
        </button>
        <mat-menu #userMenu="matMenu">
          <button mat-menu-item routerLink="/profile">{{ 'nav.profile' | translate }}</button>
          <button mat-menu-item routerLink="/puzzles/endless/history">{{ 'nav.puzzleHistory' | translate }}</button>
          <button mat-menu-item (click)="changelogClick.emit()">{{ 'nav.changelog' | translate }}</button>
          <button mat-menu-item (click)="auth.logout()">{{ 'nav.logout' | translate }}</button>
        </mat-menu>
      } @else {
        <button mat-button routerLink="/puzzles">{{ 'nav.puzzles' | translate }}</button>
        <button mat-button routerLink="/weekly">{{ 'nav.weekly' | translate }}</button>
        <button mat-icon-button (click)="quickstartClick.emit()" aria-label="Info">
          <mat-icon>info_outline</mat-icon>
        </button>
        <button mat-icon-button [matMenuTriggerFor]="langMenu" aria-label="Language">
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
    .lang-menu-label { padding: 8px 16px 4px; font-size: 0.75rem; color: #888; text-transform: uppercase; }
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

  constructor(public auth: AuthService, private courseService: CourseService, public locale: LocaleService) {}

  ngOnInit(): void {
    // Bei jedem Login/Logout neu bestimmen, ob das Kurse-Menü gezeigt wird.
    this.auth.currentUser$.subscribe(user => {
      if (!user) { this.showCourses = false; return; }
      if (this.auth.isAdmin) { this.showCourses = true; return; }
      this.courseService.checkAccess().subscribe({
        next: r => this.showCourses = r.hasAccess,
        error: () => this.showCourses = false,
      });
    });
  }
}
