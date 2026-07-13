import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslatePipe } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { LeaderboardService, Leaderboards, LeaderboardEntry, LeaderboardPeriod } from './leaderboard.service';

interface CategoryDef {
  key: 'puzzles' | 'endlessRuns' | 'courseLines' | 'dailyPuzzles';
  titleKey: string;
  icon: string;
  unitKey: string;
}

/**
 * Bestenlisten-Seite: drei Kategorien (einzigartige Standard-Puzzles, Endlos-Läufe,
 * gelöste Kurs-Linien) je Periode (Woche/Monat/gesamt). Nur eingeloggt (Route-Guard).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-leaderboards',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatButtonToggleModule, TranslatePipe, LoadingSpinnerComponent],
  template: `
    <div class="lb-page">
      <h1 class="lb-title"><mat-icon>leaderboard</mat-icon> {{ 'leaderboards.title' | translate }}</h1>

      <mat-button-toggle-group class="lb-period" [value]="period" (change)="onPeriod($event.value)" aria-label="Period">
        <mat-button-toggle value="weekly">{{ 'leaderboards.period.weekly' | translate }}</mat-button-toggle>
        <mat-button-toggle value="monthly">{{ 'leaderboards.period.monthly' | translate }}</mat-button-toggle>
        <mat-button-toggle value="alltime">{{ 'leaderboards.period.alltime' | translate }}</mat-button-toggle>
      </mat-button-toggle-group>

      @if (loading) {
        <app-loading-spinner />
      } @else if (error) {
        <p class="lb-error">{{ 'leaderboards.error' | translate }}</p>
      } @else {
        <div class="lb-grid">
          @for (cat of categories; track cat.key) {
            <mat-card class="lb-card">
              <mat-card-header>
                <mat-card-title><mat-icon>{{ cat.icon }}</mat-icon> {{ cat.titleKey | translate }}</mat-card-title>
              </mat-card-header>
              <mat-card-content>
                @if (rows(cat.key).length === 0) {
                  <p class="lb-empty">{{ 'leaderboards.empty' | translate }}</p>
                } @else {
                  <ol class="lb-list">
                    @for (e of rows(cat.key); track e.rank; let i = $index) {
                      @if (i > 0 && e.rank > rows(cat.key)[i - 1].rank + 1) {
                        <li class="lb-gap" aria-hidden="true">⋯</li>
                      }
                      <li class="lb-row" [class.lb-me]="e.isMe">
                        <span class="lb-rank" [class.lb-medal]="e.rank <= 3" [attr.data-rank]="e.rank">
                          @if (e.rank === 1) { 🥇 } @else if (e.rank === 2) { 🥈 } @else if (e.rank === 3) { 🥉 } @else { {{ e.rank }} }
                        </span>
                        <span class="lb-name">{{ e.name }}</span>
                        <span class="lb-count">{{ e.count }} <small>{{ cat.unitKey | translate }}</small></span>
                      </li>
                    }
                  </ol>
                }
              </mat-card-content>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .lb-page { max-width: 1100px; margin: 0 auto; padding: 1rem; }
    .lb-title { display: flex; align-items: center; gap: 0.5rem; }
    .lb-period { margin-bottom: 1rem; flex-wrap: wrap; }
    .lb-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 1rem; }
    .lb-card mat-card-title { display: flex; align-items: center; gap: 0.5rem; font-size: 1.1em; }
    .lb-empty { color: color-mix(in srgb, currentColor 55%, transparent); font-style: italic; }
    .lb-list { list-style: none; margin: 0.5rem 0 0; padding: 0; }
    .lb-row { display: flex; align-items: center; gap: 0.75rem; padding: 0.4rem 0.25rem; border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 6px; }
    .lb-row:last-child { border-bottom: none; }
    .lb-row.lb-me { background: color-mix(in srgb, var(--mat-sys-primary, #3f51b5) 14%, transparent); font-weight: 600; }
    .lb-gap { text-align: center; color: color-mix(in srgb, currentColor 45%, transparent); letter-spacing: 0.2em; padding: 0.1rem 0; user-select: none; }
    .lb-rank { flex: 0 0 2rem; text-align: center; font-variant-numeric: tabular-nums; font-weight: 600; color: color-mix(in srgb, currentColor 60%, transparent); }
    .lb-rank.lb-medal { font-size: 1.2em; }
    .lb-name { flex: 1 1 auto; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lb-count { flex: 0 0 auto; font-weight: 600; font-variant-numeric: tabular-nums; }
    .lb-count small { font-weight: 400; opacity: 0.6; }
    .lb-error { color: #f44336; }
  `],
})
export class LeaderboardsComponent implements OnInit {
  period: LeaderboardPeriod = 'weekly';
  loading = true;
  error = false;
  data: Leaderboards | null = null;

  readonly categories: CategoryDef[] = [
    { key: 'puzzles', titleKey: 'leaderboards.category.puzzles', icon: 'extension', unitKey: 'leaderboards.unit.puzzles' },
    { key: 'dailyPuzzles', titleKey: 'leaderboards.category.dailyPuzzles', icon: 'today', unitKey: 'leaderboards.unit.daily' },
    { key: 'endlessRuns', titleKey: 'leaderboards.category.endlessRuns', icon: 'all_inclusive', unitKey: 'leaderboards.unit.runs' },
    { key: 'courseLines', titleKey: 'leaderboards.category.courseLines', icon: 'menu_book', unitKey: 'leaderboards.unit.lines' },
  ];

  constructor(private service: LeaderboardService) {}

  ngOnInit(): void { this.load(); }

  onPeriod(p: LeaderboardPeriod): void {
    if (p === this.period) return;
    this.period = p;
    this.load();
  }

  rows(key: CategoryDef['key']): LeaderboardEntry[] {
    return this.data ? this.data[key] : [];
  }

  private load(): void {
    this.loading = true;
    this.error = false;
    this.service.get(this.period).subscribe({
      next: data => { this.data = data; this.loading = false; },
      error: () => { this.error = true; this.loading = false; },
    });
  }
}
