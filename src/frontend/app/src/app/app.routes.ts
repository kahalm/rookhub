import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { adminGuard } from './core/admin.guard';
import { courseAccessGuard } from './core/course-access.guard';
import { menuGuard } from './core/menu.guard';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'login', loadComponent: () => import('./features/auth/login.component').then(m => m.LoginComponent) },
  { path: 'register', loadComponent: () => import('./features/auth/register.component').then(m => m.RegisterComponent) },
  { path: 'forgot-password', loadComponent: () => import('./features/auth/forgot-password.component').then(m => m.ForgotPasswordComponent) },
  { path: 'reset-password', loadComponent: () => import('./features/auth/reset-password.component').then(m => m.ResetPasswordComponent) },
  { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent), canActivate: [authGuard, menuGuard('dashboard')] },
  { path: 'profile', loadComponent: () => import('./features/profile/profile.component').then(m => m.ProfileComponent), canActivate: [authGuard] },
  { path: 'friends', loadComponent: () => import('./features/friends/friends.component').then(m => m.FriendsComponent), canActivate: [authGuard, menuGuard('friends')] },
  { path: 'friends/:userId/stats', loadComponent: () => import('./features/friends/friend-stats.component').then(m => m.FriendStatsComponent), canActivate: [authGuard, menuGuard('friends')] },
  { path: 'friends/:userId/revenge', loadComponent: () => import('./features/friends/friend-revenge.component').then(m => m.FriendRevengeComponent), canActivate: [authGuard, menuGuard('friends')] },
  { path: 'repertoires', loadComponent: () => import('./features/repertoire/repertoire-list.component').then(m => m.RepertoireListComponent), canActivate: [authGuard, menuGuard('repertoires')] },
  { path: 'repertoires/:id', loadComponent: () => import('./features/repertoire/repertoire-detail.component').then(m => m.RepertoireDetailComponent), canActivate: [authGuard, menuGuard('repertoires')] },
  { path: 'tournaments', loadComponent: () => import('./features/tournaments/tournament-list.component').then(m => m.TournamentListComponent), canActivate: [authGuard, menuGuard('tournaments')] },
  { path: 'tournaments/:id', loadComponent: () => import('./features/tournaments/tournament-detail.component').then(m => m.TournamentDetailComponent), canActivate: [authGuard, menuGuard('tournaments')] },
  { path: 'puzzles/endless/history', loadComponent: () => import('./features/puzzles/endless-history.component').then(m => m.EndlessHistoryComponent), canActivate: [authGuard] },
  { path: 'puzzles/endless', loadComponent: () => import('./features/puzzles/endless-puzzle.component').then(m => m.EndlessPuzzleComponent) },
  { path: 'puzzles/book/:id', loadComponent: () => import('./features/puzzles/book-puzzle.component').then(m => m.BookPuzzleComponent) },
  { path: 'puzzles/daily/:date', loadComponent: () => import('./features/puzzles/book-puzzle.component').then(m => m.BookPuzzleComponent) },
  { path: 'puzzles/:id', loadComponent: () => import('./features/puzzles/puzzle.component').then(m => m.PuzzleComponent) },
  { path: 'puzzles', loadComponent: () => import('./features/puzzles/puzzle.component').then(m => m.PuzzleComponent), canActivate: [menuGuard('puzzles')] },
  { path: 'weekly', loadComponent: () => import('./features/weekly/weekly-list.component').then(m => m.WeeklyListComponent), canActivate: [authGuard, menuGuard('weekly')] },
  { path: 'weekly/:weeklyId', loadComponent: () => import('./features/puzzles/book-puzzle.component').then(m => m.BookPuzzleComponent), canActivate: [authGuard, menuGuard('weekly')] },
  { path: 'analysis', loadComponent: () => import('./features/analysis/analysis.component').then(m => m.AnalysisComponent), canActivate: [menuGuard('analysis')] },
  { path: 'stats', loadComponent: () => import('./features/stats/stats.component').then(m => m.StatsComponent), canActivate: [authGuard, menuGuard('stats')] },
  { path: 'training-goals', loadComponent: () => import('./features/training-goals/training-goals.component').then(m => m.TrainingGoalsComponent), canActivate: [authGuard, menuGuard('training-goals')] },
  { path: 'notifications', loadComponent: () => import('./features/notifications/notifications.component').then(m => m.NotificationsComponent), canActivate: [authGuard] },
  { path: 'courses', loadComponent: () => import('./features/courses/course-list.component').then(m => m.CourseListComponent), canActivate: [courseAccessGuard, menuGuard('courses')] },
  { path: 'courses/:bookId/chapter/:chapterIndex/:mode', loadComponent: () => import('./features/puzzles/book-puzzle.component').then(m => m.BookPuzzleComponent), canActivate: [courseAccessGuard, menuGuard('courses')] },
  { path: 'courses/:bookId/:mode', loadComponent: () => import('./features/puzzles/book-puzzle.component').then(m => m.BookPuzzleComponent), canActivate: [courseAccessGuard, menuGuard('courses')] },
  { path: 'chessable', loadComponent: () => import('./features/chessable/chessable.component').then(m => m.ChessableComponent), canActivate: [authGuard, menuGuard('chessable')] },
  { path: 'admin', loadComponent: () => import('./features/admin/admin.component').then(m => m.AdminComponent), canActivate: [adminGuard] },
  { path: 't/:id', loadComponent: () => import('./features/tournaments/public-tournament.component').then(m => m.PublicTournamentComponent) },
  { path: 'help', loadComponent: () => import('./features/help/help.component').then(m => m.HelpComponent), canActivate: [menuGuard('help')] },
  { path: 'install', loadComponent: () => import('./features/install/install.component').then(m => m.InstallComponent), canActivate: [menuGuard('install')] },
  { path: 'privacy', loadComponent: () => import('./features/legal/privacy.component').then(m => m.PrivacyComponent) },
  { path: 'impressum', loadComponent: () => import('./features/legal/impressum.component').then(m => m.ImpressumComponent) },
  { path: 'account-deletion', loadComponent: () => import('./features/legal/account-deletion.component').then(m => m.AccountDeletionComponent) },
  { path: '**', redirectTo: '/dashboard' }
];
