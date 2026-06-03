import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { adminGuard } from './core/admin.guard';
import { courseAccessGuard } from './core/course-access.guard';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'login', loadComponent: () => import('./features/auth/login.component').then(m => m.LoginComponent) },
  { path: 'register', loadComponent: () => import('./features/auth/register.component').then(m => m.RegisterComponent) },
  { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent), canActivate: [authGuard] },
  { path: 'profile', loadComponent: () => import('./features/profile/profile.component').then(m => m.ProfileComponent), canActivate: [authGuard] },
  { path: 'friends', loadComponent: () => import('./features/friends/friends.component').then(m => m.FriendsComponent), canActivate: [authGuard] },
  { path: 'repertoires', loadComponent: () => import('./features/repertoire/repertoire-list.component').then(m => m.RepertoireListComponent), canActivate: [adminGuard] },
  { path: 'repertoires/:id', loadComponent: () => import('./features/repertoire/repertoire-detail.component').then(m => m.RepertoireDetailComponent), canActivate: [adminGuard] },
  { path: 'tournaments', loadComponent: () => import('./features/tournaments/tournament-list.component').then(m => m.TournamentListComponent), canActivate: [authGuard] },
  { path: 'tournaments/:id', loadComponent: () => import('./features/tournaments/tournament-detail.component').then(m => m.TournamentDetailComponent), canActivate: [authGuard] },
  { path: 'puzzles/endless/history', loadComponent: () => import('./features/puzzles/endless-history.component').then(m => m.EndlessHistoryComponent), canActivate: [authGuard] },
  { path: 'puzzles/endless', loadComponent: () => import('./features/puzzles/endless-puzzle.component').then(m => m.EndlessPuzzleComponent) },
  { path: 'puzzles/book/:id', loadComponent: () => import('./features/puzzles/book-puzzle.component').then(m => m.BookPuzzleComponent) },
  { path: 'puzzles/:id', loadComponent: () => import('./features/puzzles/puzzle.component').then(m => m.PuzzleComponent) },
  { path: 'puzzles', loadComponent: () => import('./features/puzzles/puzzle.component').then(m => m.PuzzleComponent) },
  { path: 'weekly', loadComponent: () => import('./features/weekly/weekly-list.component').then(m => m.WeeklyListComponent), canActivate: [adminGuard] },
  { path: 'weekly/:weeklyId', loadComponent: () => import('./features/puzzles/book-puzzle.component').then(m => m.BookPuzzleComponent), canActivate: [adminGuard] },
  { path: 'analysis', loadComponent: () => import('./features/analysis/analysis.component').then(m => m.AnalysisComponent) },
  { path: 'stats', loadComponent: () => import('./features/stats/stats.component').then(m => m.StatsComponent), canActivate: [authGuard] },
  { path: 'courses', loadComponent: () => import('./features/courses/course-list.component').then(m => m.CourseListComponent), canActivate: [courseAccessGuard] },
  { path: 'courses/:bookId/:mode', loadComponent: () => import('./features/puzzles/book-puzzle.component').then(m => m.BookPuzzleComponent), canActivate: [courseAccessGuard] },
  { path: 'admin', loadComponent: () => import('./features/admin/admin.component').then(m => m.AdminComponent), canActivate: [adminGuard] },
  { path: 't/:id', loadComponent: () => import('./features/tournaments/public-tournament.component').then(m => m.PublicTournamentComponent) },
  { path: 'account-deletion', loadComponent: () => import('./features/legal/account-deletion.component').then(m => m.AccountDeletionComponent) },
  { path: '**', redirectTo: '/dashboard' }
];
