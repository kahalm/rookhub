import { Routes } from '@angular/router';
import { authGuard } from '@rh/core/auth.guard';
import { guestGuard } from '@rh/core/guest.guard';

/**
 * `/` ist die Prognose-Seite (nur angemeldet, `league.view`: Admins und die Vereinsgruppe), `/verein*` die
 * Vereins-Datenbank (lesen `league.view`, hinzufügen `league.contribute`), `/konten` die Konto-Vorschläge und `/uebertragungen` die Lichess-Übertragungen (`league.manage`),
 * `/s/:token` die geteilte Ansicht einer
 * Begegnung OHNE Anmeldung. Keine Route mit `/g`, `/t` oder `/puzzles`: diese
 * Präfixe schickt der gemeinsame nginx an die Link-Vorschau der API.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', canActivate: [authGuard],
    loadComponent: () => import('./features/league/league-page.component').then(m => m.LeaguePageComponent) },
  { path: 'verein', pathMatch: 'full', canActivate: [authGuard],
    loadComponent: () => import('./features/club/club-games-page.component').then(m => m.ClubGamesPageComponent) },
  // „Meine Partien" (0.652.0): dieselbe Liste, nur die eigenen — dort jederzeit bearbeiten oder löschen.
  { path: 'verein/meine', canActivate: [authGuard], data: { mine: true },
    loadComponent: () => import('./features/club/club-games-page.component').then(m => m.ClubGamesPageComponent) },
  { path: 'verein/neu', canActivate: [authGuard],
    loadComponent: () => import('./features/club/club-add-page.component').then(m => m.ClubAddPageComponent) },
  // Vereinspartie korrigieren (0.660.0): dieselbe Seite wie beim ersten Prüfen eines Formulars, mit dem aufbewahrten Foto
  { path: 'verein/partie/:id/korrigieren', canActivate: [authGuard], data: { game: true },
    loadComponent: () => import('./features/club/club-scan-page.component').then(m => m.ClubScanPageComponent) },
  { path: 'verein/formular/:id', canActivate: [authGuard],
    loadComponent: () => import('./features/club/club-scan-page.component').then(m => m.ClubScanPageComponent) },
  { path: 'konten', canActivate: [authGuard],
    loadComponent: () => import('./features/accounts/account-suggestions-page.component').then(m => m.AccountSuggestionsPageComponent) },
  { path: 'uebertragungen', canActivate: [authGuard],
    loadComponent: () => import('./features/broadcasts/broadcasts-page.component').then(m => m.BroadcastsPageComponent) },
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
  // Unbekannte Adressen (UX-026): Hinweisseite ohne Guard. Vorher leitete '**' auf '' um, BEVOR der authGuard lief —
  // Gäste bekamen die Anmeldemaske mit returnUrl „/“ (das Ziel war verloren), Angemeldete standen stumm auf den Prognosen.
  { path: '**', loadComponent: () => import('@rh/shared/not-found/not-found.component').then(m => m.NotFoundComponent) },
];
