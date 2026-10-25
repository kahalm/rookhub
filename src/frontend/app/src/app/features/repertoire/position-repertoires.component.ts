import { Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import {
  RepertoireService, RepertoirePositionMatch, RepertoireLineMatch,
  RepertoirePositionTree, PositionTreeNode,
  SimilarPositionMatch,
} from '../../core/repertoire.service';
import { ChapterGroup, LineKeyResolver, chapterGroups, treeAsMatch, treeNodeAsLine } from './repertoire-line-link';
import { PositionTreeComponent } from './position-tree.component';
import { SimilarPositionsPanelComponent } from './similar-positions-panel.component';
import { SimilarPositionsSearch } from './similar-positions-search';
import { localStore, readRaw, writeRaw } from '../../core/local-json-store';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { whiteToMove } from '../../shared/chess/fen-side';

type PanelMode = 'list' | 'tree' | 'similar';
const MODE_KEY = 'rookhub_position_reps_mode';

/**
 * „In welchen Repertoires kommt diese Stellung vor?" — wiederverwendbarer Knopf + Ergebnis-Panel.
 * Nimmt die aktuelle Brett-Stellung als `fen` und zeigt die Treffer in zwei umschaltbaren Sichten:
 *
 * - **Liste** (`POST /api/repertoires/position-lookup`): Repertoire → Kapitel → Linie, pro Linie
 *   „Trainieren" (Trainer) und „Ansehen" (Detail).
 * - **Baum** (`POST /api/repertoires/position-tree`): „wie geht mein Repertoire ab hier weiter?" —
 *   alle Vorkommen zu einem Zugbaum je Repertoire zusammengeführt, Varianten inklusive. Der Baum
 *   kommt deshalb vom Server: der Client-Parser (`parsePgnText`) wirft Varianten weg.
 * - **Ähnlich** (`POST /api/repertoires/similar-positions`): dieselbe Frage unscharf — „wo steht
 *   etwas, das SO AUSSIEHT?". Gewichtet Bauerngerüst/Material/Figurenplatzierung/König
 *   (Voreinstellung wählbar) und wertet optional auch den Farbtausch. Die Rechnung liegt komplett
 *   beim Server; hier werden nur Filter gestellt und Treffer samt Aufschlüsselung gezeigt.
 *   Optional kommt ein ZUG dazu — Zustand und Zug-Regeln: `SimilarPositionsSearch`.
 *
 * Die gewählte Sicht wird pro Gerät gemerkt. Hört ein Consumer auf `playMoves` (heute die Analyse),
 * spielt ein Klick im Baum die Zugfolge aufs Brett — die Stellung ändert sich, das Panel lädt neu
 * und der Baum wächst mit; so klickt man sich durchs eigene Repertoire.
 *
 * Baum und Ähnlich rechnen je Anfrage über ALLE Linien (Server: Zeitbudget, je Konto ein Fenster und
 * höchstens zwei gleichzeitige Scans). Deshalb bestellt jede neue Stellung die noch laufende Anfrage
 * ab — HttpClient bricht sie ab, der Server beendet den Durchlauf —, statt beim Durchklicken eine
 * Schlange veralteter Scans aufzubauen. Ein abgelaufenes Zeitbudget (`truncated`) und eine Absage
 * wegen zu vieler Anfragen (429) bekommen je einen eigenen Hinweis.
 *
 * Der Ziel-`lineKey` kommt aus dem CLIENT-Parse des Repertoire-PGN (`repertoire-line-link.ts`).
 * Eingebunden in Analyse, PGN-Viewer (Recap-Dialog) und die geteilte-Partie-Seite; blendet sich für
 * nicht eingeloggte Besucher komplett aus (Repertoires sind pro-Nutzer).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-position-repertoires',
  standalone: true,
  imports: [IconLabelDirective, CommonModule, MatButtonModule, MatIconModule, MatTooltipModule, MatProgressSpinnerModule, TranslatePipe,
            PositionTreeComponent, SimilarPositionsPanelComponent],
  template: `
    @if (auth.isLoggedIn) {
      <div class="pos-reps" [class.pr-as-row]="appearance === 'row'">
        @if (appearance === 'row') {
          <!-- Als Zeile (Seitenspalte der Partieseite): gleich wie „Ähnliche Meisterpartien" darunter. -->
          <button type="button" class="pr-toggle pr-row" (click)="toggle()" [disabled]="!fen" [attr.aria-expanded]="open">
            <mat-icon>menu_book</mat-icon>
            <span class="pr-row-label">{{ 'positionInReps.button' | translate }}</span>
            <mat-icon class="pr-chevron">{{ open ? 'expand_less' : 'expand_more' }}</mat-icon>
          </button>
        } @else {
          <button mat-stroked-button class="pr-toggle" (click)="toggle()" [disabled]="!fen">
            <mat-icon>menu_book</mat-icon>
            {{ 'positionInReps.button' | translate }}
          </button>
        }

        @if (open) {
          <div class="pr-panel">
            <div class="pr-modes">
              <button class="pr-mode" type="button" [class.pr-mode-on]="mode === 'list'" (click)="setMode('list')"
                      [matTooltip]="'positionInReps.mode.list' | translate">
                <mat-icon>format_list_bulleted</mat-icon>
              </button>
              <button class="pr-mode" type="button" [class.pr-mode-on]="mode === 'tree'" (click)="setMode('tree')"
                      [matTooltip]="'positionInReps.mode.tree' | translate">
                <mat-icon>account_tree</mat-icon>
              </button>
              <button class="pr-mode" type="button" [class.pr-mode-on]="mode === 'similar'" (click)="setMode('similar')"
                      [matTooltip]="'positionInReps.mode.similar' | translate">
                <mat-icon>compare</mat-icon>
              </button>
            </div>

            @if (loading) {
              <div class="pr-muted"><mat-spinner diameter="16"></mat-spinner> {{ 'positionInReps.loading' | translate }}</div>
            } @else if (error) {
              <div class="pr-muted pr-error">{{ (rateLimited ? 'positionInReps.rateLimited' : 'positionInReps.error') | translate }}</div>
            } @else if (mode === 'similar') {
              <app-similar-positions-panel
                [truncated]="truncated"
                [matches]="sim.matches"
                [options]="sim.options"
                [selected]="sim.selected"
                [preset]="sim.preset"
                [includeMirrored]="sim.mirrored"
                [sameSideToMove]="sim.sameSide"
                [moveText]="sim.moveText"
                [moveSan]="sim.moveSan"
                [moveInvalid]="sim.moveInvalid"
                [onlyWithMove]="sim.onlyWithMove"
                (presetChange)="sim.setPreset($event)"
                (mirroredChange)="sim.setMirrored($event)"
                (sameSideToMoveChange)="sim.setSameSide($event)"
                (moveTextChange)="sim.setMoveText($event)"
                (onlyWithMoveChange)="sim.setOnlyWithMove($event)"
                (selectionChange)="sim.setSelection($event)"
                (openMatch)="openMatch($event)" />
            } @else if (mode === 'tree') {
              @if (truncated) {
                <div class="pr-muted pr-note">{{ 'positionInReps.truncated' | translate }}</div>
              }
              @if (trees.length === 0) {
                <div class="pr-muted">{{ 'positionInReps.none' | translate }}</div>
              } @else {
                <div class="pr-count">{{ (trees.length === 1 ? 'positionInReps.repsCountOne' : 'positionInReps.repsCount') | translate:{ count: trees.length } }} · {{ (totalOccurrences === 1 ? 'positionInReps.tree.occurrencesCountOne' : 'positionInReps.tree.occurrencesCount') | translate:{ count: totalOccurrences } }}</div>
                @for (tree of trees; track tree.repertoireId) {
                  <div class="pr-rep">
                    <button class="pr-rep-head" (click)="toggleRep(tree.repertoireId)">
                      <mat-icon>{{ isRepOpen(tree.repertoireId) ? 'expand_more' : 'chevron_right' }}</mat-icon>
                      <span class="pr-rep-name">{{ tree.repertoireName }}</span>
                      @if (tree.shared) { <span class="pr-shared" [matTooltip]="'positionInReps.sharedHint' | translate"><mat-icon>group</mat-icon>{{ 'positionInReps.sharedBadge' | translate }}</span> }
                      <span class="pr-badge">{{ tree.occurrences }}</span>
                    </button>
                    @if (isRepOpen(tree.repertoireId)) {
                      @if (tree.moves.length === 0) {
                        <div class="pr-muted pr-tree-empty">{{ 'positionInReps.tree.noContinuation' | translate }}</div>
                      } @else {
                        <app-position-tree class="pr-tree"
                          [nodes]="tree.moves"
                          [startMoveNumber]="startMoveNumber"
                          [blackToMove]="blackToMove"
                          [canPlay]="canPlay"
                          (playPath)="playMoves.emit($event)"
                          (train)="trainNode(tree, $event)"
                          (view)="viewNode(tree, $event)" />
                      }
                      @if (tree.truncated) {
                        <div class="pr-muted pr-tree-empty">{{ 'positionInReps.tree.truncated' | translate }}</div>
                      }
                    }
                  </div>
                }
              }
            } @else if (repertoires.length === 0) {
              <div class="pr-muted">{{ 'positionInReps.none' | translate }}</div>
            } @else {
              <div class="pr-count">{{ (repertoires.length === 1 ? 'positionInReps.repsCountOne' : 'positionInReps.repsCount') | translate:{ count: repertoires.length } }} · {{ (totalLines === 1 ? 'positionInReps.linesCountOne' : 'positionInReps.linesCount') | translate:{ count: totalLines } }}</div>
              @for (rep of repertoires; track rep.repertoireId) {
                <div class="pr-rep">
                  <button class="pr-rep-head" (click)="toggleRep(rep.repertoireId)">
                    <mat-icon>{{ isRepOpen(rep.repertoireId) ? 'expand_more' : 'chevron_right' }}</mat-icon>
                    <span class="pr-rep-name">{{ rep.repertoireName }}</span>
                    @if (rep.shared) { <span class="pr-shared" [matTooltip]="'positionInReps.sharedHint' | translate"><mat-icon>group</mat-icon>{{ 'positionInReps.sharedBadge' | translate }}</span> }
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
                                      [appIconLabel]="'positionInReps.train' | translate">
                                <mat-icon>fitness_center</mat-icon>
                              </button>
                              <button mat-icon-button (click)="view(rep, line)" [disabled]="busyLine === line.gameIndex + ':' + rep.repertoireId"
                                      [appIconLabel]="'positionInReps.view' | translate">
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
  styleUrls: ['./position-repertoires.component.scss'],
})
export class PositionRepertoiresComponent implements OnChanges, OnDestroy {
  @Input() fen = '';
  /** `button` = umrandeter Knopf (Vorgabe), `row` = Zeile mit Pfeil für eine Seitenspalte mit eigenem Rahmen. */
  @Input() appearance: 'button' | 'row' = 'button';
  /** Feuert vor jeder Navigation — z. B. damit ein umschließender Dialog sich schließt. */
  @Output() navigated = new EventEmitter<void>();
  /** Baummodus: SAN-Zugfolge ab der aktuellen Stellung, die aufs Brett gespielt werden soll.
   * Nur Consumer mit eigenem Brett hören darauf (heute die Analyse) — sonst klappt ein Klick
   * den Knoten nur auf/zu. */
  @Output() playMoves = new EventEmitter<string[]>();

  open = false;
  loading = false;
  error = false;
  /** Der Fehler war eine Absage wegen zu vieler Anfragen (429) — eigener Hinweis statt „fehlgeschlagen". */
  rateLimited = false;
  /** Baum/Ähnlich: der Server hat am Zeitbudget aufgehört — das Ergebnis ist unvollständig. */
  truncated = false;
  mode: PanelMode = 'list';
  repertoires: RepertoirePositionMatch[] = [];
  trees: RepertoirePositionTree[] = [];
  totalLines = 0;
  totalOccurrences = 0;
  busyLine: string | null = null;

  private openReps = new Set<number>();
  private loadedFen = '';
  private loadedMode: PanelMode | null = null;
  private reqId = 0;
  /** Die laufende Anfrage — eine neue Stellung bestellt sie ab (siehe Klassen-Doku). */
  private pending: Subscription | null = null;
  /** lineKey einer gemeldeten Linie aus dem (gecachten) Repertoire-PGN. */
  private readonly lineKeys = new LineKeyResolver(() => this.repertoireService);
  /** Sicht „Ähnlich": Filter, Zug, Treffer (überlebt Zuklappen und Sichtwechsel wie bisher die Felder hier). */
  readonly sim = new SimilarPositionsSearch({
    api: () => this.repertoireService, fen: () => this.fen, reload: () => this.reloadSimilar(),
  });

  constructor(public auth: AuthService, private repertoireService: RepertoireService, private router: Router) {
    const saved = readRaw(localStore(), MODE_KEY);
    if (saved === 'tree' || saved === 'list' || saved === 'similar') this.mode = saved;
  }

  /** Nur wo ein Consumer zuhört, ist „Zug aufs Brett spielen" sinnvoll. */
  get canPlay(): boolean { return this.playMoves.observed; }

  /** Zugnummer/Seite am Zug aus der FEN — für die Nummerierung im Baum. */
  get startMoveNumber(): number {
    const n = parseInt((this.fen || '').split(' ')[5], 10);
    return Number.isFinite(n) && n > 0 ? n : 1;
  }
  get blackToMove(): boolean { return !whiteToMove(this.fen); }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['fen']) return;
    // Der eingegebene Zug hängt an der ANKER-Stellung: „Nd5" ist nach dem Durchklicken ein
    // anderer Zug — oder gar keiner mehr. Also VOR dem Laden neu auflösen.
    this.sim.reanchor();
    // Wenn das Panel offen ist und sich die Stellung ändert (Durchklicken), neu laden.
    if (this.open && this.fen !== this.loadedFen) this.load();
  }

  ngOnDestroy(): void {
    this.pending?.unsubscribe();
  }

  toggle(): void {
    this.open = !this.open;
    if (this.open) this.load();
  }

  setMode(mode: PanelMode): void {
    if (this.mode === mode) return;
    this.mode = mode;
    writeRaw(localStore(), MODE_KEY, mode);   // Privatmodus: Auswahl gilt nur für die Sitzung
    if (this.open) this.load();
  }

  /** `force` = die Sicht selbst hat sich geändert (Voreinstellung/Spiegel/Auswahl), nicht die Stellung. */
  private load(force = false): void {
    if (!this.fen) return;
    // Alle Sichten hängen an derselben Stellung; nur neu laden, wenn Stellung ODER Sicht wechselt.
    if (!force && this.fen === this.loadedFen && this.mode === this.loadedMode && !this.error) return;
    this.loadedFen = this.fen;
    this.loadedMode = this.mode;
    this.loading = true;
    this.error = false;
    this.rateLimited = false;
    this.truncated = false;
    // Die vorige Frage ist überholt: abbestellen statt nur ihre Antwort zu verwerfen — sonst rechnet der
    // Server sie zu Ende und belegt dabei einen der zwei Scan-Plätze des Kontos.
    this.pending?.unsubscribe();
    this.pending = null;
    const myReq = ++this.reqId;
    const fail = (err?: unknown) => {
      if (myReq !== this.reqId) return;
      this.error = true;
      this.rateLimited = (err as { status?: number } | null)?.status === 429;
      this.loading = false;
      this.loadedMode = null;
    };

    if (this.mode === 'similar') {
      // Erst die Repertoire-Liste (einmalig), dann die Suche — EINE Kette, das Abbestellen bricht beides ab.
      this.pending = this.sim.search().subscribe({
        next: (res) => {
          if (myReq !== this.reqId) return; // veraltete Antwort verwerfen
          this.sim.applyResult(res);        // null = nichts ausgewählt → nichts gefragt
          if (res) this.truncated = res.truncated === true;
          this.loading = false;
        },
        error: fail,
      });
      return;
    }

    if (this.mode === 'tree') {
      this.pending = this.repertoireService.lookupPositionTree(this.fen).subscribe({
        next: (res) => {
          if (myReq !== this.reqId) return; // veraltete Antwort verwerfen
          this.trees = res.repertoires ?? [];
          this.truncated = res.truncated === true;
          this.totalOccurrences = this.trees.reduce((s, r) => s + r.occurrences, 0);
          this.openReps = new Set(this.trees.map(r => r.repertoireId)); // alle aufgeklappt
          this.loading = false;
        },
        error: fail,
      });
      return;
    }

    this.pending = this.repertoireService.lookupPosition(this.fen).subscribe({
      next: (res) => {
        if (myReq !== this.reqId) return; // veraltete Antwort verwerfen
        this.repertoires = res.repertoires ?? [];
        this.totalLines = this.repertoires.reduce((s, r) => s + r.lines.length, 0);
        this.openReps = new Set(this.repertoires.map(r => r.repertoireId)); // alle aufgeklappt
        this.loading = false;
      },
      error: fail,
    });
  }

  // ===== Ähnliche Stellungen =====

  /** Ein Treffer öffnet die Stellung genau wie in der Listen-Sicht („Ansehen"). */
  openMatch(match: SimilarPositionMatch): void {
    this.navigateToLine(
      { repertoireId: match.repertoireId, repertoireName: match.repertoireName, kind: '', shared: false, lines: [] },
      { chapter: match.chapter ?? '', lineName: match.lineName ?? '', gameIndex: match.gameIndex, ply: match.ply },
      'view');
  }

  private reloadSimilar(): void {
    if (this.open && this.mode === 'similar') this.load(true);
  }

  toggleRep(id: number): void {
    if (this.openReps.has(id)) this.openReps.delete(id); else this.openReps.add(id);
  }
  isRepOpen(id: number): boolean { return this.openReps.has(id); }

  chaptersOf(rep: RepertoirePositionMatch): ChapterGroup[] { return chapterGroups(rep); }

  train(rep: RepertoirePositionMatch, line: RepertoireLineMatch): void {
    this.navigateToLine(rep, line, 'train');
  }
  view(rep: RepertoirePositionMatch, line: RepertoireLineMatch): void {
    this.navigateToLine(rep, line, 'view');
  }

  /** Baummodus: Trainieren/Ansehen für einen Knoten, der eindeutig zu EINER Linie gehört. */
  trainNode(tree: RepertoirePositionTree, node: PositionTreeNode): void {
    this.navigateToLine(treeAsMatch(tree), treeNodeAsLine(node), 'train');
  }
  viewNode(tree: RepertoirePositionTree, node: PositionTreeNode): void {
    this.navigateToLine(treeAsMatch(tree), treeNodeAsLine(node), 'view');
  }

  private navigateToLine(rep: RepertoirePositionMatch, line: RepertoireLineMatch, target: 'train' | 'view'): void {
    const busy = line.gameIndex + ':' + rep.repertoireId;
    this.busyLine = busy;
    this.lineKeys.resolve(rep.repertoireId, line).subscribe({
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
}
