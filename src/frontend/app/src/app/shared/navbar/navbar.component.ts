import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule],
  template: `
    <mat-toolbar color="primary">
      <span class="logo" routerLink="/dashboard">RookHub</span>
      <span class="spacer"></span>
      @if (auth.isLoggedIn) {
        <button mat-button routerLink="/dashboard">Dashboard</button>
        <button mat-button routerLink="/repertoires">Repertoires</button>
        <button mat-button routerLink="/tournaments">Tournaments</button>
        <button mat-button routerLink="/friends">Friends</button>
        <button mat-icon-button [matMenuTriggerFor]="userMenu">
          <mat-icon>account_circle</mat-icon>
        </button>
        <mat-menu #userMenu="matMenu">
          <button mat-menu-item routerLink="/profile">Profile</button>
          <button mat-menu-item (click)="auth.logout()">Logout</button>
        </mat-menu>
      } @else {
        <button mat-button routerLink="/login">Login</button>
        <button mat-raised-button routerLink="/register">Register</button>
      }
    </mat-toolbar>
  `,
  styles: [`
    .logo { cursor: pointer; font-weight: bold; font-size: 1.3em; }
    .spacer { flex: 1 1 auto; }
  `]
})
export class NavbarComponent {
  constructor(public auth: AuthService) {}
}
