import { Routes } from '@angular/router';

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
  // Impressum und Datenschutz: dieselben Seiten wie in RookHub (eine oeffentliche Seite braucht beides).
  { path: 'impressum', loadComponent: () => import('@rh/features/legal/impressum.component').then(m => m.ImpressumComponent) },
  { path: 'privacy', loadComponent: () => import('@rh/features/legal/privacy.component').then(m => m.PrivacyComponent) },
  { path: '**', redirectTo: '' },
];
