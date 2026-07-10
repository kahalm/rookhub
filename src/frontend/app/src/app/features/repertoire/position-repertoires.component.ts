import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, of } from 'rxjs';
import { map, tap } from 'rxjs/operators';
import { AuthService } from '../../core/auth.service';
import { RepertoireService, RepertoirePositionMatch, RepertoireLineMatch } from '../../core/repertoire.service';
import { ParsedGame, parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import { lineKeyFromSans } from './repertoire-line-key.util';

interface ChapterGroup { name: string; lines: RepertoireLineMatch[]; }

/**
 * „In welchen Repertoires kommt diese Stellung vor?" — wiederverwendbarer Knopf + Ergebnis-Panel.
 * Nimmt die aktuelle Brett-Stellung als `fen`, fragt `POST /api/repertoires/position-lookup` und zeigt
 * die Treffer als Repertoire → Kapitel → Linie. Pro Linie „Trainieren" (Trainer) und „Ansehen" (Detail).
 *
 * Der Ziel-`lineKey` wird bewusst aus dem CLIENT-Parse des Repertoire-PGN berechnet (identisch zu
 * Trainer/Linienliste), nicht aus Server-SAN — so ist die Linien-Identität garantiert konsistent.
 * Eingebunden in Analyse, PGN-Viewer (Recap-Dialog) und die geteilte-Partie-Seite; blendet sich für
 * nicht eingeloggte Besucher komplett aus (Repertoires sind pro-Nutzer).
 */
@Component({
  selector: 'app-position-repertoires',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatTooltipModule, MatProgressSpinnerModule, TranslateModule],
  template: `
    @if (auth.isLoggedIn) {
      <div class="pos-reps">
        <button mat-stroked-button class="pr-toggle" (click)="toggle()" [disabled]="!fen">
          <mat-icon>menu_book</mat-icon>
          {{ 'positionInReps.button' | translate }}
        </button>

        @if (open) {
          <div class="pr-panel">
            @if (loading) {
              <div class="pr-muted"><mat-spinner diameter="16"></mat-spinner> {{ 'positionInReps.loading' | translate }}</div>
            } @else if (error) {
              <div class="pr-muted pr-error">{{ 'positionInReps.error' | translate }}</div>
            } @else if (repertoires.length === 0) {
              <div class="pr-muted">{{ 'positionInReps.none' | translate }}</div>
            } @else {
              <div class="pr-count">{{ 'positionInReps.foundCount' | translate:{ reps: repertoires.length, lines: totalLines } }}</div>
              @for (rep of repertoires; track rep.repertoireId) {
                <div class="pr-rep">
                  <button class="pr-rep-head" (click)="toggleRep(rep.repertoireId)">
                    <mat-icon>{{ isRepOpen(rep.repertoireId) ? 'expand_more' : 'chevron_right' }}</mat-icon>
                    <span class="pr-rep-name">{{ rep.repertoireName }}</span>
                    <span class="pr-badge">{{ rep.lines.length }}</span>
                  </button>
                  @if (isRepOpen(rep.repertoireId)) {
                    @for (ch of chaptersOf(rep); track ch.name) {
                      <div class="pr-chapter">
                        @if (ch.name) { <div class="pr-chapter-name">{{ ch.name }}</div> }
                        @for (line of ch.lines; track line.gameIndex) {
                          <div class="pr-line">
                            <span class="pr-line-name">{{ line.lineName || ('positionInReps.unnamedLine' | translate) }}</span>
                            <span class="pr-line-actions">
                              <button mat-icon-button (click)="train(rep, line)" [disabled]="busyLine === line.gameIndex + ':' + rep.repertoireId"
                                      [matTooltip]="'positionInReps.train' | translate">
                                <mat-icon>fitness_center</mat-icon>
                              </button>
                              <button mat-icon-button (click)="view(rep, line)" [disabled]="busyLine === line.gameIndex + ':' + rep.repertoireId"
                                      [matTooltip]="'positionInReps.view' | translate">
                                <mat-icon>visibility</mat-icon>
                              </button>
                            </span>
                          </div>
                        }
                      </div>
                    }
                  }
                </div>
              }
            }
          </div>
        }
      </div>
    }
  `,
  styles: [`
    .pos-reps { display: block; }
    .pr-toggle { width: 100%; }
    .pr-panel { margin-top: 8px; border: 1px solid color-mix(in srgb, currentColor 14%, transparent); border-radius: 6px; padding: 8px; max-height: 46vh; overflow: auto; }
    .pr-muted { color: color-mix(in srgb, currentColor 55%, transparent); font-style: italic; display: flex; align-items: center; gap: 8px; }
    .pr-error { color: #c62828; }
    .pr-count { font-size: .82rem; color: color-mix(in srgb, currentColor 60%, transparent); margin-bottom: 6px; }
    .pr-rep { margin-bottom: 4px; }
    .pr-rep-head { display: flex; align-items: center; gap: 6px; width: 100%; background: none; border: none; color: inherit; cursor: pointer; padding: 4px 2px; text-align: left; font: inherit; }
    .pr-rep-head:hover { background: color-mix(in srgb, currentColor 7%, transparent); border-radius: 4px; }
    .pr-rep-name { flex: 1; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .pr-badge { font-size: .72rem; background: #1976d2; color: #fff; border-radius: 10px; padding: 1px 7px; }
    .pr-chapter { margin: 2px 0 6px 22px; }
    .pr-chapter-name { font-size: .78rem; color: color-mix(in srgb, currentColor 60%, transparent); margin: 4px 0 2px; }
    .pr-line { display: flex; align-items: center; gap: 6px; padding-left: 6px; }
    .pr-line-name { flex: 1; font-size: .88rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .pr-line-actions { display: flex; flex: 0 0 auto; }
    .pr-line-actions .mat-mdc-icon-button { --mdc-icon-button-state-layer-size: 32px; --mdc-icon-button-icon-size: 18px; width: 32px; height: 32px; padding: 4px; }
  `]
})
export class PositionRepertoiresComponent implements OnChanges {
  @Input() fen = '';
  /** Feuert vor jeder Navigation — z. B. damit ein umschließender Dialog sich schließt. */
  @Output() navigated = new EventEmitter<void>();

  open = false;
  loading = false;
  error = false;
  repertoires: RepertoirePositionMatch[] = [];
  totalLines = 0;
  busyLine: string | null = null;

  private openReps = new Set<number>();
  private loadedFen = '';
  private reqId = 0;
  private pgnCache = new Map<number, ParsedGame[]>();

  constructor(public auth: AuthService, private repertoireService: RepertoireService, private router: Router) {}

  ngOnChanges(changes: SimpleChanges): void {
    // Wenn das Panel offen ist und sich die Stellung ändert (Durchklicken), neu laden.
    if (changes['fen'] && this.open && this.fen !== this.loadedFen) this.load();
  }

  toggle(): void {
    this.open = !this.open;
    if (this.open) this.load();
  }

  private load(): void {
    if (!this.fen) return;
    this.loadedFen = this.fen;
    this.loading = true;
    this.error = false;
    const myReq = ++this.reqId;
    this.repertoireService.lookupPosition(this.fen).subscribe({
      next: (res) => {
        if (myReq !== this.reqId) return; // veraltete Antwort verwerfen
        this.repertoires = res.repertoires ?? [];
        this.totalLines = this.repertoires.reduce((s, r) => s + r.lines.length, 0);
        this.openReps = new Set(this.repertoires.map(r => r.repertoireId)); // alle aufgeklappt
        this.loading = false;
      },
      error: () => { if (myReq === this.reqId) { this.error = true; this.loading = false; } },
    });
  }

  toggleRep(id: number): void {
    if (this.openReps.has(id)) this.openReps.delete(id); else this.openReps.add(id);
  }
  isRepOpen(id: number): boolean { return this.openReps.has(id); }

  chaptersOf(rep: RepertoirePositionMatch): ChapterGroup[] {
    const groups: ChapterGroup[] = [];
    const byName = new Map<string, ChapterGroup>();
    for (const line of rep.lines) {
      const name = line.chapter || '';
      let g = byName.get(name);
      if (!g) { g = { name, lines: [] }; byName.set(name, g); groups.push(g); }
      g.lines.push(line);
    }
    return groups;
  }

  train(rep: RepertoirePositionMatch, line: RepertoireLineMatch): void {
    this.navigateToLine(rep, line, 'train');
  }
  view(rep: RepertoirePositionMatch, line: RepertoireLineMatch): void {
    this.navigateToLine(rep, line, 'view');
  }

  private navigateToLine(rep: RepertoirePositionMatch, line: RepertoireLineMatch, target: 'train' | 'view'): void {
    const busy = line.gameIndex + ':' + rep.repertoireId;
    this.busyLine = busy;
    this.resolveLineKey(rep.repertoireId, line).subscribe({
      next: (lineKey) => {
        this.busyLine = null;
        this.navigated.emit();
        if (target === 'train') {
          this.router.navigate(['/repertoires', rep.repertoireId, 'train'],
            { queryParams: { chapter: line.chapter || null, line: lineKey } });
        } else {
          this.router.navigate(['/repertoires', rep.repertoireId],
            { queryParams: { line: lineKey, ply: line.ply >= 0 ? line.ply : null } });
        }
      },
      error: () => { this.busyLine = null; },
    });
  }

  /** Lädt (gecacht) das Repertoire-PGN, findet die passende Linie und berechnet deren lineKey. */
  private resolveLineKey(repId: number, line: RepertoireLineMatch): Observable<string> {
    const cached = this.pgnCache.get(repId);
    const games$ = cached
      ? of(cached)
      : this.repertoireService.getPgnText(repId).pipe(
          map(pgn => parsePgnText(pgn)),
          tap(games => this.pgnCache.set(repId, games)),
        );
    return games$.pipe(map(games => {
      const g = this.findGame(games, line);
      return g ? lineKeyFromSans(g.moves.map(m => m.san)) : '';
    }));
  }

  private findGame(games: ParsedGame[], line: RepertoireLineMatch): ParsedGame | null {
    const matches = (g: ParsedGame) =>
      (g.headers['Black'] || '').trim() === line.chapter && (g.headers['White'] || '').trim() === line.lineName;
    // Bevorzugt exakt am gemeldeten Index (falls Kapitel/Name dort passen),
    // sonst der erste Treffer nach (Kapitel, Linienname), sonst der Index als Fallback.
    const atIndex = games[line.gameIndex];
    if (atIndex && matches(atIndex)) return atIndex;
    return games.find(matches) ?? atIndex ?? null;
  }
}
