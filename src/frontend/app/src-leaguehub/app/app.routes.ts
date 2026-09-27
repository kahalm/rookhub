import { Routes } from '@angular/router';
import { authGuard } from '@rh/core/auth.guard';
import { guestGuard } from '@rh/core/guest.guard';

/**
 * `/` ist die Prognose-Seite (nur angemeldet, vorerst nur Admins — `league.view`), `/s/:token` die
 * geteilte Ansicht einer Begegnung OHNE Anmeldung. Keine Route mit `/g`, `/t` oder `/puzzles`: diese
 * Präfixe schickt der gemeinsame nginx an die Link-Vorschau der API.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', canActivate: [authGuard],
    loadComponent: () => import('./features/league/league-page.component').then(m => m.LeaguePageComponent) },
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
