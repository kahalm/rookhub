import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'login', loadComponent: () => import('./features/auth/login.component').then(m => m.LoginComponent) },
  { path: 'register', loadComponent: () => import('./features/auth/register.component').then(m => m.RegisterComponent) },
  { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent), canActivate: [authGuard] },
  { path: 'profile', loadComponent: () => import('./features/profile/profile.component').then(m => m.ProfileComponent), canActivate: [authGuard] },
  { path: 'friends', loadComponent: () => import('./features/friends/friends.component').then(m => m.FriendsComponent), canActivate: [authGuard] },
  { path: 'repertoires', loadComponent: () => import('./features/repertoire/repertoire-list.component').then(m => m.RepertoireListComponent), canActivate: [authGuard] },
  { path: 'repertoires/:id', loadComponent: () => import('./features/repertoire/repertoire-detail.component').then(m => m.RepertoireDetailComponent), canActivate: [authGuard] },
  { path: 'tournaments', loadComponent: () => import('./features/tournaments/tournament-list.component').then(m => m.TournamentListComponent), canActivate: [authGuard] },
  { path: 'tournaments/:id', loadComponent: () => import('./features/tournaments/tournament-detail.component').then(m => m.TournamentDetailComponent), canActivate: [authGuard] },
  { path: '**', redirectTo: '/dashboard' }
];
