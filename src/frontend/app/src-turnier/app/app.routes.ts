import { Routes } from '@angular/router';
import { authGuard } from '@rh/core/auth.guard';
import { guestGuard } from '@rh/core/guest.guard';
import { adminGuard } from '@rh/core/admin.guard';

/**
 * Die Turnierseite hat bewusst wenige Wege: Liste, Kalender, Detail — plus die geteilte
 * oeffentliche Ansicht und die Anmeldung (dieselben Komponenten wie in RookHub, ueber `@rh/*`).
 */
export const routes: Routes = [
  { path: 'login', loadComponent: () => import('@rh/features/auth/login.component').then(m => m.LoginComponent), canActivate: [guestGuard] },
  { path: 'register', loadComponent: () => import('@rh/features/auth/register.component').then(m => m.RegisterComponent), canActivate: [guestGuard] },
  // Was die geteilte Anmeldemaske und die Rechtsseiten verlinken. Ohne diese Wege fiel jeder Link
  // auf '**' → Kalender → authGuard → zurueck auf /login: der Passwort-Reset war von hier aus
  // unmoeglich, die Datenschutzerklaerung unerreichbar. Ohne authGuard — sie gelten auch abgemeldet.
  { path: 'forgot-password', loadComponent: () => import('@rh/features/auth/forgot-password.component').then(m => m.ForgotPasswordComponent) },
  { path: 'reset-password', loadComponent: () => import('@rh/features/auth/reset-password.component').then(m => m.ResetPasswordComponent) },
  // Die Turnierseite nimmt die Vorgabe von LEGAL_SITE (mit Impressum), anders als KidHub.
  { path: 'privacy', loadComponent: () => import('@rh/features/legal/privacy.component').then(m => m.PrivacyComponent) },
  { path: 'impressum', loadComponent: () => import('@rh/features/legal/impressum.component').then(m => m.ImpressumComponent) },
  { path: 'account-deletion', loadComponent: () => import('@rh/features/legal/account-deletion.component').then(m => m.AccountDeletionComponent) },

  { path: 'tournaments', loadComponent: () => import('./features/tournaments/tournament-list.component').then(m => m.TournamentListComponent), canActivate: [authGuard] },
  // Literal vor Parameter: /tournaments/calendar darf nicht als Turnier-Id gelesen werden.
  // OHNE Anmeldung (0.643.0): Kalender, Kalendereintrag und Turnier sind öffentlich — nur Speichern (Filter,
  // Merken, Favoriten, Beobachten) verlangt ein Konto und führt dorthin.
  { path: 'tournaments/calendar', loadComponent: () => import('./features/tournament-directory/tournament-directory.component').then(m => m.TournamentDirectoryComponent) },
  // Gespielte und kommende Turniere des eigenen Kontos, umschaltbar auf Freunde. Literal, muss
  // also vor 'tournaments/:id' stehen.
  { path: 'tournaments/history', loadComponent: () => import('./features/tournament-history/tournament-history.component').then(m => m.TournamentHistoryComponent), canActivate: [authGuard] },

  // Ein Turnier aus dem Verzeichnis. Drei Segmente, kollidiert also nicht mit 'tournaments/:id'
  // (das ist die Ansicht eines schon GEHOLTEN Turniers mit Teilnehmern und Paarungen).
  { path: 'tournaments/calendar/:id', loadComponent: () => import('./features/tournament-directory/tournament-directory-detail.component').then(m => m.TournamentDirectoryDetailComponent) },
  // reloadOnParamChange: die Gruppen-Umschaltung wechselt nur die Id — die Seite muss neu laden.
  { path: 'tournaments/:id', loadComponent: () => import('./features/tournaments/tournament-detail.component').then(m => m.TournamentDetailComponent), data: { reloadOnParamChange: true } },

  // Name, Anzeigename, E-Mail und die Spielerkennungen. Dieselbe API wie in RookHub, aber ohne
  // deren Chessable-/Engine-/Token-Sammlung: die hat auf einer Turnierseite nichts zu tun.
  { path: 'profile', loadComponent: () => import('./features/profile/turnier-profile.component').then(m => m.TurnierProfileComponent), canActivate: [authGuard] },

  // Fuer Admins: als ein Nutzer einsteigen. Bewusst NICHT RookHubs zehn-Laschen-Panel — dessen
  // Laschen fuehren zu Bereichen, die es hier nicht gibt (siehe TurnierAdminComponent).
  { path: 'admin', loadComponent: () => import('./features/admin/turnier-admin.component').then(m => m.TurnierAdminComponent), canActivate: [authGuard, adminGuard] },

  // Geteilter Turnier-Link, ohne Anmeldung lesbar.
  { path: 't/:id', loadComponent: () => import('./features/tournaments/public-tournament.component').then(m => m.PublicTournamentComponent) },

  // Startziel ist der KALENDER, nicht die Liste der importierten Turniere: wer die Turnierseite
  // aufruft, will wissen, was ansteht — die Liste zeigt nur, was schon jemand geholt hat.
  { path: '', pathMatch: 'full', redirectTo: 'tournaments/calendar' },
  { path: '**', redirectTo: 'tournaments/calendar' },
];
