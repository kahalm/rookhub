import { Routes } from '@angular/router';
import { guestGuard } from '@rh/core/guest.guard';

/**
 * Wenige, sprechende Wege. Keiner beginnt mit `/g`, `/t` oder `/puzzles`: diese Praefixe schickt
 * der gemeinsame nginx an die Link-Vorschau der API (`/api/og/render`), und die kennt die
 * Kinderseite nicht.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./features/home/kids-home.component').then(m => m.KidsHomeComponent) },
  { path: 'levels', loadComponent: () => import('./features/levels/level-map.component').then(m => m.LevelMapComponent) },
  { path: 'levels/:level', loadComponent: () => import('./features/levels/level-play.component').then(m => m.LevelPlayComponent) },
  { path: 'courses', loadComponent: () => import('./features/courses/course-list.component').then(m => m.CourseListComponent) },
  { path: 'courses/:bookId', loadComponent: () => import('./features/courses/course-play.component').then(m => m.CoursePlayComponent) },
  // Anmelden und Registrieren: dieselben Masken wie in RookHub und auf der Turnierseite — dasselbe
  // Konto. „Passwort vergessen" gehoert dazu, die Maske verlinkt es.
  { path: 'login', loadComponent: () => import('@rh/features/auth/login.component').then(m => m.LoginComponent), canActivate: [guestGuard] },
  { path: 'register', loadComponent: () => import('@rh/features/auth/register.component').then(m => m.RegisterComponent), canActivate: [guestGuard] },
  { path: 'forgot-password', loadComponent: () => import('@rh/features/auth/forgot-password.component').then(m => m.ForgotPasswordComponent) },
  { path: 'reset-password', loadComponent: () => import('@rh/features/auth/reset-password.component').then(m => m.ResetPasswordComponent) },
  // Datenschutz: dieselbe Seite wie in RookHub, mit KidHubs Kontakt (LEGAL_SITE in kidhubConfig). Ein
  // Impressum hat die Kinderseite bewusst nicht (Wunsch 2026-09-27) — /impressum faellt auf die Startseite.
  { path: 'privacy', loadComponent: () => import('@rh/features/legal/privacy.component').then(m => m.PrivacyComponent) },
  // Die Datenschutzerklaerung verlinkt sie; ohne die Route fuehrte der Link still auf die Startseite.
  { path: 'account-deletion', loadComponent: () => import('@rh/features/legal/account-deletion.component').then(m => m.AccountDeletionComponent) },
  { path: '**', redirectTo: '' },
];
