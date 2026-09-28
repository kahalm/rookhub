import { Routes } from '@angular/router';
import { authGuard } from '@rh/core/auth.guard';
import { guestGuard } from '@rh/core/guest.guard';

/**
 * `/` ist die Prognose-Seite (nur angemeldet, `league.view`: Admins und die Vereinsgruppe), `/verein*` die
 * Vereins-Datenbank (lesen `league.view`, hinzufügen `league.contribute`), `/s/:token` die geteilte Ansicht einer
 * Begegnung OHNE Anmeldung. Keine Route mit `/g`, `/t` oder `/puzzles`: diese
 * Präfixe schickt der gemeinsame nginx an die Link-Vorschau der API.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', canActivate: [authGuard],
    loadComponent: () => import('./features/league/league-page.component').then(m => m.LeaguePageComponent) },
  { path: 'verein', pathMatch: 'full', canActivate: [authGuard],
    loadComponent: () => import('./features/club/club-games-page.component').then(m => m.ClubGamesPageComponent) },
  { path: 'verein/neu', canActivate: [authGuard],
    loadComponent: () => import('./features/club/club-add-page.component').then(m => m.ClubAddPageComponent) },
  { path: 'verein/formular/:id', canActivate: [authGuard],
    loadComponent: () => import('./features/club/club-scan-page.component').then(m => m.ClubScanPageComponent) },
  // Ohne Anmeldung über einen Teilen-Link: Partien hochladen (Wunsch 2026-09-28).
  { path: 's/:token/hochladen',
    loadComponent: () => import('./features/club/club-add-page.component').then(m => m.ClubAddPageComponent) },
  { path: 's/:token/formular/:key',
    loadComponent: () => import('./features/club/club-scan-page.component').then(m => m.ClubScanPageComponent) },
  { path: 's/:token', loadComponent: () => import('./features/share/share-page.component').then(m => m.SharePageComponent) },
  { path: 'login', loadComponent: () => import('@rh/features/auth/login.component').then(m => m.LoginComponent), canActivate: [guestGuard] },
  { path: 'register', loadComponent: () => import('@rh/features/auth/register.component').then(m => m.RegisterComponent), canActivate: [guestGuard] },
  { path: 'forgot-password', loadComponent: () => import('@rh/features/auth/forgot-password.component').then(m => m.ForgotPasswordComponent) },
  { path: 'reset-password', loadComponent: () => import('@rh/features/auth/reset-password.component').then(m => m.ResetPasswordComponent) },
  { path: 'impressum', loadComponent: () => import('@rh/features/legal/impressum.component').then(m => m.ImpressumComponent) },
  { path: 'privacy', loadComponent: () => import('@rh/features/legal/privacy.component').then(m => m.PrivacyComponent) },
  { path: 'account-deletion', loadComponent: () => import('@rh/features/legal/account-deletion.component').then(m => m.AccountDeletionComponent) },
  { path: '**', redirectTo: '' },
];
