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
  // Impressum und Datenschutz: dieselben Seiten wie in RookHub (eine oeffentliche Seite braucht beides).
  { path: 'impressum', loadComponent: () => import('@rh/features/legal/impressum.component').then(m => m.ImpressumComponent) },
  { path: 'privacy', loadComponent: () => import('@rh/features/legal/privacy.component').then(m => m.PrivacyComponent) },
  { path: '**', redirectTo: '' },
];
