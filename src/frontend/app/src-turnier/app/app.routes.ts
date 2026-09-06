import { Routes } from '@angular/router';
import { authGuard } from '@rh/core/auth.guard';

/**
 * Die Turnierseite hat bewusst wenige Wege: Liste, Kalender, Detail — plus die geteilte
 * oeffentliche Ansicht und die Anmeldung (dieselben Komponenten wie in RookHub, ueber `@rh/*`).
 */
export const routes: Routes = [
  { path: 'login', loadComponent: () => import('@rh/features/auth/login.component').then(m => m.LoginComponent) },
  { path: 'register', loadComponent: () => import('@rh/features/auth/register.component').then(m => m.RegisterComponent) },

  { path: 'tournaments', loadComponent: () => import('./features/tournaments/tournament-list.component').then(m => m.TournamentListComponent), canActivate: [authGuard] },
  // Literal vor Parameter: /tournaments/calendar darf nicht als Turnier-Id gelesen werden.
  { path: 'tournaments/calendar', loadComponent: () => import('./features/tournament-directory/tournament-directory.component').then(m => m.TournamentDirectoryComponent), canActivate: [authGuard] },
  { path: 'tournaments/:id', loadComponent: () => import('./features/tournaments/tournament-detail.component').then(m => m.TournamentDetailComponent), canActivate: [authGuard] },

  // Geteilter Turnier-Link, ohne Anmeldung lesbar.
  { path: 't/:id', loadComponent: () => import('./features/tournaments/public-tournament.component').then(m => m.PublicTournamentComponent) },

  { path: '', pathMatch: 'full', redirectTo: 'tournaments' },
  { path: '**', redirectTo: 'tournaments' },
];
