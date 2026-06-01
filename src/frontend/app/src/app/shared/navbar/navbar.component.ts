import { Component, EventEmitter, Output, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { AuthService } from '../../core/auth.service';
import { CourseService } from '../../features/courses/course.service';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule],
  template: `
    <mat-toolbar color="primary">
      <span class="logo" routerLink="/dashboard">RookHub</span>
      <span class="spacer"></span>
      @if (auth.isLoggedIn) {
        <div class="nav-links">
          <button mat-button routerLink="/dashboard">Dashboard</button>
          <button mat-button routerLink="/repertoires">Repertoires</button>
          <button mat-button routerLink="/tournaments">Tournaments</button>
          <button mat-button routerLink="/friends">Friends</button>
          <button mat-button routerLink="/puzzles">Puzzles</button>
          <button mat-button routerLink="/weekly">Wochenpost</button>
          @if (showCourses) {
            <button mat-button routerLink="/courses">Kurse</button>
          }
          @if (auth.isAdmin) {
            <button mat-button routerLink="/admin">Admin</button>
          }
        </div>
        <button mat-icon-button class="mobile-menu-btn" [matMenuTriggerFor]="navMenu">
          <mat-icon>menu</mat-icon>
        </button>
        <mat-menu #navMenu="matMenu">
          <button mat-menu-item routerLink="/dashboard">Dashboard</button>
          <button mat-menu-item routerLink="/repertoires">Repertoires</button>
          <button mat-menu-item routerLink="/tournaments">Tournaments</button>
          <button mat-menu-item routerLink="/friends">Friends</button>
          <button mat-menu-item routerLink="/puzzles">Puzzles</button>
          <button mat-menu-item routerLink="/weekly">Wochenpost</button>
          @if (showCourses) {
            <button mat-menu-item routerLink="/courses">Kurse</button>
          }
          @if (auth.isAdmin) {
            <button mat-menu-item routerLink="/admin">Admin</button>
          }
        </mat-menu>
        <button mat-icon-button [matMenuTriggerFor]="userMenu">
          <mat-icon>account_circle</mat-icon>
        </button>
        <mat-menu #userMenu="matMenu">
          <button mat-menu-item routerLink="/profile">Profile</button>
          <button mat-menu-item routerLink="/puzzles/endless/history">Puzzle History</button>
          <button mat-menu-item (click)="changelogClick.emit()">Changelog</button>
          <button mat-menu-item (click)="auth.logout()">Logout</button>
        </mat-menu>
      } @else {
        <button mat-button routerLink="/puzzles">Puzzles</button>
        <button mat-button routerLink="/weekly">Wochenpost</button>
        <button mat-icon-button (click)="quickstartClick.emit()" aria-label="Info">
          <mat-icon>info_outline</mat-icon>
        </button>
        <button mat-button routerLink="/login">Login</button>
        <button mat-raised-button routerLink="/register">Register</button>
      }
    </mat-toolbar>
  `,
  styles: [`
    .logo { cursor: pointer; font-weight: bold; font-size: 1.3em; }
    .spacer { flex: 1 1 auto; }
    .mobile-menu-btn { display: none; }
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

  constructor(public auth: AuthService, private courseService: CourseService) {}

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
