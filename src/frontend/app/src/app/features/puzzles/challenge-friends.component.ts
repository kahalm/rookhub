import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { ChallengeService, PuzzleChallengeSource } from '../../core/challenge.service';
import { SnackbarService } from '../../core/snackbar.service';
import { Friend } from '../../core/models';

/**
 * Wiederverwendbares „An Freund(e) schicken"-Menü für alle Puzzle-Modi (Standard/Endless = `standard`,
 * Buch/Kurs/Tagespuzzle = `book`). Multi-Select: pro Freund eine Checkbox, „Alle auswählen" und
 * „Senden (n)". Lädt die Freundesliste faul beim Öffnen des Menüs.
 */
@Component({
  selector: 'app-challenge-friends',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatMenuModule, MatIconModule, MatCheckboxModule, TranslateModule],
  template: `
    <button mat-stroked-button class="challenge-btn" [matMenuTriggerFor]="friendMenu" (menuOpened)="loadFriends()">
      <mat-icon>send</mat-icon>
      {{ 'puzzles.actions.challengeFriend' | translate }}
    </button>
    <mat-menu #friendMenu="matMenu" class="challenge-menu">
      @if (friends.length === 0) {
        <button mat-menu-item disabled>{{ 'puzzles.actions.noFriends' | translate }}</button>
      } @else {
        <div class="challenge-menu-row challenge-menu-all" (click)="$event.stopPropagation()">
          <mat-checkbox [checked]="allSelected" [indeterminate]="someSelected" (change)="toggleAll($event.checked)">
            {{ 'puzzles.challenge.selectAll' | translate }}
          </mat-checkbox>
        </div>
        @for (f of friends; track f.userId) {
          <div class="challenge-menu-row" (click)="$event.stopPropagation()">
            <mat-checkbox [checked]="selected.has(f.userId)" (change)="toggle(f.userId, $event.checked)">
              {{ f.displayName || f.username }}
            </mat-checkbox>
          </div>
        }
        <div class="challenge-menu-send" (click)="$event.stopPropagation()">
          <button mat-flat-button color="primary" [disabled]="selected.size === 0 || sending" (click)="send()">
            {{ 'puzzles.challenge.send' | translate: { count: selected.size } }}
          </button>
        </div>
      }
    </mat-menu>
  `,
  styles: [`
    .challenge-menu-row { padding: 4px 16px; }
    .challenge-menu-all { border-bottom: 1px solid rgba(0,0,0,.12); margin-bottom: 4px; padding-bottom: 8px; }
    .challenge-menu-send { padding: 8px 16px; }
    .challenge-menu-send button { width: 100%; }
  `]
})
export class ChallengeFriendsComponent {
  /** ID des Puzzles, das verschickt wird (Puzzles.Id bzw. BookPuzzles.Id je nach `source`). */
  @Input() puzzleId!: number;
  /** Quelle des Puzzles — bestimmt Tabelle + Deep-Link beim Empfänger. */
  @Input() source: PuzzleChallengeSource = 'standard';

  friends: Friend[] = [];
  selected = new Set<number>();
  sending = false;
  private loaded = false;

  constructor(
    private http: HttpClient,
    private challengeService: ChallengeService,
    private snackbar: SnackbarService,
    private translate: TranslateService
  ) {}

  get allSelected(): boolean { return this.friends.length > 0 && this.selected.size === this.friends.length; }
  get someSelected(): boolean { return this.selected.size > 0 && this.selected.size < this.friends.length; }

  loadFriends(): void {
    if (this.loaded) return;
    this.loaded = true;
    this.http.get<Friend[]>('/api/friends').subscribe({
      next: f => this.friends = f,
      error: () => { this.loaded = false; }
    });
  }

  toggle(userId: number, checked: boolean): void {
    if (checked) this.selected.add(userId); else this.selected.delete(userId);
  }

  toggleAll(checked: boolean): void {
    if (checked) this.friends.forEach(f => this.selected.add(f.userId));
    else this.selected.clear();
  }

  send(): void {
    if (this.selected.size === 0 || this.sending) return;
    this.sending = true;
    this.challengeService.sendMany([...this.selected], this.puzzleId, this.source).subscribe({
      next: res => {
        this.sending = false;
        this.selected.clear();
        const skipped = res.skipped?.length ?? 0;
        if (skipped > 0) {
          this.snackbar.info(this.translate.instant('puzzles.challenge.batchResult', { sent: res.sent, skipped }));
        } else {
          this.snackbar.success(this.translate.instant('puzzles.challenge.sentCount', { count: res.sent }));
        }
      },
      error: err => {
        this.sending = false;
        this.snackbar.info(err.error?.message || this.translate.instant('puzzles.challenge.error'));
      }
    });
  }
}
