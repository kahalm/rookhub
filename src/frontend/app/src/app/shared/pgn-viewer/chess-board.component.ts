import {
  Component, ElementRef, EventEmitter, Input, OnChanges, OnDestroy, Output,
  AfterViewInit, SimpleChanges, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { Chessground } from 'chessground';
import { Api } from 'chessground/api';
import { Config } from 'chessground/config';
import { Key } from 'chessground/types';
import { BoardFullscreenButtonComponent } from '../fullscreen/board-fullscreen-button.component';
import { applyUserMove, legalDests, turnColorOf } from './board-moves.util';

/** Vom Nutzer auf einem `playable`-Brett ausgeführter Zug (FEN = Stellung DANACH). */
export interface UserBoardMove { from: string; to: string; san: string; fen: string; }

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-chess-board',
  standalone: true,
  imports: [BoardFullscreenButtonComponent],
  template: `
    <div #fsHost class="cb-fs-host">
      <app-board-fullscreen-button [target]="fsHost" />
      <div class="cb-wrap">
        <div #boardEl [class]="'cg-square board-theme-' + boardTheme + ' piece-set-' + pieceSet"></div>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; width: 100%; }
    .cb-fs-host { width: 100%; }
    .cb-wrap { position: relative; width: 100%; }
    /* Vollbild: die Größe des Vollbild-Elements erzwingt der Browser (UA-!important schlägt
       Author-!important) — zentriert wird das Brett DARIN: Quadrat aus der kleineren
       Bildschirmseite, drumherum schwarze Balken. Die Brett-Pixelgröße zieht der
       ResizeObserver aus der Wrapper-Breite nach. */
    .cb-fs-host:fullscreen {
      display: flex;
      align-items: center;
      justify-content: center;
      background: #000;
    }
    .cb-fs-host:fullscreen .cb-wrap {
      flex: 0 0 auto;
      width: min(100vw, 100vh);
      height: min(100vw, 100vh);
      max-width: 100%;
      max-height: 100%;
    }
    .cb-fs-host:fullscreen::backdrop { background: #000; }
    /* Verschließ-Sicher: der Boden-Div bleibt immer quadratisch, egal welche width
       chess-board.component per JS setzt oder ob die JS-Größenberechnung verpasst wird.
       Ohne aspect-ratio zieht Chessground die Squares horizontal, das Brett wirkt
       gequetscht (siehe Games-Dialog-Bug 0.244.x). */
    .cg-square { aspect-ratio: 1 / 1; }
  `]
})
export class ChessBoardComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() fen = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  @Input() lastMove?: [string, string];
  @Input() flipped = false;
  /** Brett-Theme aus den User-Preferences (styles.scss `.board-theme-*` cg-board Regeln). */
  @Input() boardTheme = 'brown';
  /** Figurenset aus den User-Preferences (styles.scss `.piece-set-*` cg-board piece Regeln). */
  @Input() pieceSet = 'cburnett';
  /** Opt-in: legale Züge der Seite am Zug per Drag/Klick erlauben (Umwandlung immer Dame).
   * Der Aufrufer MUSS auf (userMove) reagieren und die neue FEN zurückbinden — das Brett
   * selbst bleibt zustandslos (Anzeige der [fen]-Bindung). */
  @Input() playable = false;
  @Output() userMove = new EventEmitter<UserBoardMove>();

  @ViewChild('boardEl') boardEl!: ElementRef<HTMLElement>;

  private ground?: Api;
  private resizeObserver?: ResizeObserver;
  private initAttempts = 0;
  private rafId?: number;
  /** Nachmess-Anlaeufe waehrend einer Dialog-Einblendung (siehe fitToHost). */
  private settleTimers: number[] = [];
  /** Zuletzt gesetzte Kantenlaenge — verhindert unnoetiges Neuzeichnen (siehe fitToHost). */
  private appliedWidth = 0;
  private destroyed = false;

  ngAfterViewInit(): void {
    this.initBoard();
  }

  private initBoard(): void {
    // Während der Breite-0-Retries zerstört? Dann kein Chessground/ResizeObserver mehr
    // auf einem abgekoppelten Element aufbauen (würde sonst nie disconnected).
    if (this.destroyed) return;
    const el = this.boardEl.nativeElement;
    const hostWidth = (this.boardEl.nativeElement.parentElement as HTMLElement)?.clientWidth
      || el.clientWidth;

    if (hostWidth === 0 && this.initAttempts < 10) {
      this.initAttempts++;
      this.rafId = requestAnimationFrame(() => this.initBoard());
      return;
    }

    const size = hostWidth || 400;
    el.style.width = `${size}px`;
    el.style.height = `${size}px`;

    // WICHTIG: NICHT mit viewOnly:true initialisieren — Chessground bindet die
    // Maus-/Touch-Listener (inkl. Rechtsklick-Zeichnen) nur beim Init und
    // überspringt sie bei viewOnly=true (bindBoard: `if (s.viewOnly) return;`).
    // Damit man auf diesem reinen Anzeige-Brett trotzdem Pfeile/Kreise per
    // Rechtsklick ziehen kann, initialisieren wir interaktiv, schalten aber
    // jegliche Figuren-Interaktion (Ziehen/Auswählen/Zug) aus.
    this.ground = Chessground(el, {
      fen: this.fen,
      viewOnly: false,
      turnColor: turnColorOf(this.fen),
      orientation: this.flipped ? 'black' : 'white',
      lastMove: this.lastMove as Key[] | undefined,
      animation: { enabled: true, duration: 200 },
      highlight: { lastMove: true, check: true },
      coordinates: true,
      ...this.interactionConfig(),
      // Pfeile/Kreise per Rechtsklick-Ziehen (wie im Analyse-/Puzzle-Brett).
      drawable: { enabled: true, visible: true },
    });

    this.resizeObserver = new ResizeObserver(() => this.fitToHost());
    this.resizeObserver.observe(el.parentElement || el);
    // Nachmessen, bis die Groesse steht. Grund ist eine Falle, die JEDES Brett in einem DIALOG
    // trifft: Material faehrt den Dialog mit `transform: scale()` auf, und Chessground misst mit
    // `getBoundingClientRect()` — das liefert die SKALIERTE Groesse. Ein 300-px-Kasten misst
    // waehrend der Einblendung 240, und diese 240 schreibt Chessground als feste Pixelzahl in sein
    // inneres Element. Danach meldet der ResizeObserver nichts mehr: die Layout-Groesse des
    // Wrappers hat sich nie geaendert, nur seine Darstellung. Ergebnis war ein Brett, das
    // sechzig Pixel kleiner blieb als sein Kasten — Vollbild-Knopf daneben, Luecke darunter.
    // Drei Anlaeufe decken die Einblendung ab; jeder ist ein Vergleich und tut nichts, wenn es
    // passt.
    this.rafId = requestAnimationFrame(() => this.fitToHost());
    this.settleTimers = [200, 500].map(ms => window.setTimeout(() => this.fitToHost(), ms));
  }

  /**
   * Die Brettgroesse an den Wrapper angleichen und neu zeichnen (Chessground rechnet in Pixeln).
   *
   * <p>Gemessen werden muss das INNERE Element (`cg-container`), nicht unser aeusseres: Chessground
   * setzt seine Groesse EINMAL beim Aufbau als feste Pixelzahl, und keine spaetere Zeichnung
   * aendert sie. Unser Kasten wuchs also mit, das gezeichnete Brett nicht — im Stellungsfilter
   * gemessen: aussen 300 × 300, `cg-container` 240 × 240. Sichtbar war das als Vollbild-Knopf
   * sechzig Pixel neben dem Brett und einer gleich grossen Luecke darunter. Der frueher hier
   * stehende Vergleich gegen `el.clientWidth` sah nur den aeusseren Kasten und fand deshalb
   * nichts zu tun.</p>
   */
  private fitToHost(): void {
    const el = this.boardEl?.nativeElement;
    if (!el || this.destroyed) return;
    const hostEl = el.parentElement as HTMLElement;
    const w = hostEl?.clientWidth || el.clientWidth;
    if (w <= 0) return;
    const container = el.querySelector('cg-container') as HTMLElement | null;
    const ist = container?.clientWidth || el.clientWidth;
    // Chessground rastet auf ein Vielfaches von acht (acht Linien, ganze Felder): in einem
    // 300-px-Kasten steht es auf 296, und das ist RICHTIG. Nur diese Rasterung zu dulden haelt
    // das Nachmessen ruhig — ohne die Schranke rechnete jeder Anlauf 296 gegen 300 und zeichnete
    // das Brett endlos neu; ohne sie ganz wegzulassen bliebe ein wirklich verrutschtes Brett
    // (die 240 aus der Einblendung) fuer immer stehen.
    if (w === this.appliedWidth && Math.abs(ist - w) <= 8) return;
    this.appliedWidth = w;
    el.style.width = `${w}px`;
    el.style.height = `${w}px`;
    if (container) {
      container.style.width = `${w}px`;
      container.style.height = `${w}px`;
    }
    this.ground?.redrawAll();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.ground) return;
    if (changes['fen'] || changes['lastMove'] || changes['flipped'] || changes['playable']) {
      this.ground.set({
        fen: this.fen,
        // MUSS mitgegeben werden: Chessground dreht `turnColor` nach jedem Nutzerzug selbst um und
        // stellt ihn beim Setzen einer FEN nicht wieder her (siehe `turnColorOf`). Ohne diese Zeile
        // steht er nach dem ersten geratenen Zug auf der Gegenseite, und `isMovable` weist jeden
        // weiteren Zug ab — das Brett markiert dann noch die Felder, fuehrt den Zug aber nicht aus.
        turnColor: turnColorOf(this.fen),
        orientation: this.flipped ? 'black' : 'white',
        lastMove: this.lastMove as Key[] | undefined,
        ...this.interactionConfig(),
      });
    }
  }

  /** Figuren-Interaktion je nach `playable`: aus (reine Anzeige) oder legale Züge der Seite am Zug. */
  private interactionConfig(): Pick<Config, 'movable' | 'draggable' | 'selectable'> {
    const legal = this.playable ? legalDests(this.fen) : null;
    if (!legal) {
      return {
        movable: { free: false, color: undefined },
        draggable: { enabled: false },
        selectable: { enabled: false },
      };
    }
    return {
      movable: {
        free: false,
        color: legal.color,
        dests: legal.dests as Map<Key, Key[]>,
        events: { after: (orig, dest) => this.onBoardMove(orig, dest) },
      },
      draggable: { enabled: true },
      selectable: { enabled: true },
    };
  }

  private onBoardMove(orig: Key, dest: Key): void {
    const applied = applyUserMove(this.fen, orig, dest);
    if (!applied) {
      // Sollte bei dests-beschränkten Zügen nicht passieren — Brett auf die Bindung zurücksetzen.
      this.ground?.set({ fen: this.fen });
      return;
    }
    this.userMove.emit({ from: orig, to: dest, san: applied.san, fen: applied.fen });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    if (this.rafId !== undefined) cancelAnimationFrame(this.rafId);
    this.settleTimers.forEach(t => clearTimeout(t));
    this.settleTimers = [];
    this.resizeObserver?.disconnect();
    this.ground?.destroy();
  }
}
