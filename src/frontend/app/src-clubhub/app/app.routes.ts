import { Routes } from '@angular/router';
import { authGuard } from '@rh/core/auth.guard';
import { guestGuard } from '@rh/core/guest.guard';

/**
 * `/` ist die Kartei, `/kind/…` ein Karteiblatt, `/gruppen…` die Trainingsgruppen samt Anwesenheitsliste — alles nur
 * angemeldet; ob das Konto ClubHub sehen darf (`club.manage` / `club.trainer`), entscheidet die Seite selbst und zeigt
 * sonst „Nicht freigeschaltet". `/verknuepfen` braucht nur ein Konto: dort löst ein Kind den Code seines Trainers ein.
 * Keine Route mit `/g`, `/t` oder `/puzzles`: diese Präfixe schickt der gemeinsame nginx an die Link-Vorschau der API
 * (`/gruppen` ist ein anderes Segment als `/g`).
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', canActivate: [authGuard],
    loadComponent: () => import('./features/members/members-page.component').then(m => m.MembersPageComponent) },
  { path: 'kind/neu', canActivate: [authGuard],
    loadComponent: () => import('./features/members/member-page.component').then(m => m.MemberPageComponent) },
  { path: 'kind/:id', canActivate: [authGuard],
    loadComponent: () => import('./features/members/member-page.component').then(m => m.MemberPageComponent) },
  { path: 'gruppen', pathMatch: 'full', canActivate: [authGuard],
    loadComponent: () => import('./features/groups/groups-page.component').then(m => m.GroupsPageComponent) },
  { path: 'gruppen/:id', pathMatch: 'full', canActivate: [authGuard],
    loadComponent: () => import('./features/groups/group-page.component').then(m => m.GroupPageComponent) },
  { path: 'gruppen/:id/anwesenheit', canActivate: [authGuard],
    loadComponent: () => import('./features/groups/attendance-page.component').then(m => m.AttendancePageComponent) },
  { path: 'verknuepfen', canActivate: [authGuard],
    loadComponent: () => import('./features/link/link-page.component').then(m => m.LinkPageComponent) },
  { path: 'login', loadComponent: () => import('@rh/features/auth/login.component').then(m => m.LoginComponent), canActivate: [guestGuard] },
  { path: 'register', loadComponent: () => import('@rh/features/auth/register.component').then(m => m.RegisterComponent), canActivate: [guestGuard] },
  { path: 'forgot-password', loadComponent: () => import('@rh/features/auth/forgot-password.component').then(m => m.ForgotPasswordComponent) },
  { path: 'reset-password', loadComponent: () => import('@rh/features/auth/reset-password.component').then(m => m.ResetPasswordComponent) },
  { path: 'impressum', loadComponent: () => import('@rh/features/legal/impressum.component').then(m => m.ImpressumComponent) },
  { path: 'privacy', loadComponent: () => import('@rh/features/legal/privacy.component').then(m => m.PrivacyComponent) },
  { path: 'account-deletion', loadComponent: () => import('@rh/features/legal/account-deletion.component').then(m => m.AccountDeletionComponent) },
  { path: '**', redirectTo: '' },
];
