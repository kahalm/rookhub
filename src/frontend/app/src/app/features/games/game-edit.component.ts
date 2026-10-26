import { scrollIntoContainer } from '../../shared/pgn-viewer/move-list.component';
import { ElementRef, ChangeDetectionStrategy, Component, DestroyRef, HostListener, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GamesService, SavedGameDetail } from './games.service';
import { GameEvals } from './game-review.util';
import { ScoresheetPhotoDialogComponent } from './scoresheet-photo-dialog.component';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { ScoresheetService, openPhotoBlob, photoFileName } from './scoresheet.service';
import { distinctClassifiers, seasonOf } from './classifier.util';
import { addTag, distinctTags, MAX_TAG_LENGTH, MAX_TAGS } from './tags.util';
import { commentsForSave, headersOf, isoDateOf, pliesOfPgn, startFenOf, stripSheetNotes, toServer } from './game-edit.util';
import { SheetEditSession } from './sheet-edit-session';
import { LeaveConfirm } from '../../core/unsaved-changes.guard';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { IsoDateFieldComponent } from '../../shared/date-field/iso-date-field.component';
import { ScanPhotos } from './sheet-workbench/scan-photos';
import { SheetWorkbenchComponent } from './sheet-workbench/sheet-workbench.component';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { loadErrorKind } from '../../shared/load-error/load-error-cause';

/**
 * Partie korrigieren (`/games/:id/edit`, 0.529.0). Für jede eigene Partie: Züge und Kopfdaten. Bei einer aus
 * einem Formular-Foto eingelesenen Partie zusätzlich das Foto daneben, was auf dem Formular stand, die
 * unsicheren Stellen mit ihren wahrscheinlichsten Lesarten — und nach jeder Änderung wird der REST aus den
 * Formular-Einträgen neu aufbereitet (Server, ohne Modell-Aufruf), statt ihn einfach stehen zu lassen.
 *
 * <para>Das Brett zeigt immer die Stellung VOR dem gewählten Halbzug (dem „Cursor"): wer dort einen Zug spielt,
 * ersetzt ihn (oder fügt davor ein). Der bisherige Zug steht als gelber Pfeil darauf.</para>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-game-edit',
  standalone: true,
  imports: [IconLabelDirective,
    CommonModule, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatProgressSpinnerModule, MatSelectModule, MatDialogModule, TranslatePipe, IsoDateFieldComponent, HelpHintComponent, SheetWorkbenchComponent, LoadErrorComponent,
  ],
  template: `
    <div class="edit-page">
      @if (loading()) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (loadError(); as err) {
        <mat-card><app-load-error [messageKey]="'games.loadError'" [error]="err" (retry)="reload()" /></mat-card>
      } @else if (notFound()) {
        <mat-card class="empty-state"><mat-icon>link_off</mat-icon><p>{{ 'games.edit.notFound' | translate }}</p></mat-card>
      } @else {
        <div class="header">
          <a mat-icon-button [routerLink]="['/games', gameId]" [appIconLabel]="'common.back' | translate">
            <mat-icon>arrow_back</mat-icon>
          </a>
          <h1>{{ 'games.edit.title' | translate }}</h1>
          <!-- Der Hinweis zu Seite/Liga und Tags steht mit hinter dem ⓘ (d2-rk-edit-handy-angaben). -->
          <app-help-hint [text]="((isScoresheet() ? 'games.edit.helpScoresheet' : 'games.edit.help') | translate) + paraSep
            + ('games.edit.classifierHint' | translate) + ' · ' + ('games.tags.hint' | translate: { max: maxTags })" />
          <span class="spacer"></span>
          <button mat-flat-button color="primary" (click)="save()" [disabled]="saving() || busy()">
            <mat-icon>{{ saving() ? 'hourglass_top' : 'save' }}</mat-icon> {{ 'common.save' | translate }}
          </button>
        </div>
        @if (clubGameId()) {
          <!-- Kopie einer Vereinspartie (0.660.0) -->
          <p class="club-linked">{{ 'games.edit.clubLinked' | translate }}</p>
        }

        <mat-card class="headers" [class.open]="headersOpen()">
          <!-- Handy (d2-rk-edit-handy-angaben): Angaben als zugeklappte Zeile, damit das Brett über der Falz beginnt; offen nur,
               wenn ein Name fehlt. Ab 769 px stehen die Felder immer offen, der Knopf ist dort ausgeblendet. -->
          <!-- z2-rk-edit-angaben-ergebnis: Namen und Ergebnis getrennt — gekürzt werden nur die Namen, das Ergebnis bleibt
               sichtbar; der volle Satz „Angaben: … · 1-0" bleibt als Vorlesetext. -->
          <button type="button" class="headers-toggle" (click)="headersOpen.set(!headersOpen())" [attr.aria-expanded]="headersOpen()"
                  [attr.aria-label]="'games.edit.headersSummary' | translate: { white: header.white || '?', black: header.black || '?', result: header.result }">
            <span class="ht-names" [title]="(header.white || '?') + ' – ' + (header.black || '?')">{{ header.white || '?' }} – {{ header.black || '?' }}</span>
            <span class="ht-result">{{ header.result }}</span>
            <mat-icon>{{ headersOpen() ? 'expand_less' : 'expand_more' }}</mat-icon>
          </button>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.edit.white' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.white" name="white" maxlength="120" (ngModelChange)="dirty.set(true)" /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.edit.black' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.black" name="black" maxlength="120" (ngModelChange)="dirty.set(true)" /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="short"><mat-label>{{ 'games.edit.result' | translate }}</mat-label>
            <mat-select [(ngModel)]="header.result" name="result" (ngModelChange)="dirty.set(true)">
              @for (r of results; track r) { <mat-option [value]="r">{{ r }}</mat-option> }
            </mat-select></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.edit.event' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.event" name="event" maxlength="200" (ngModelChange)="dirty.set(true)" /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.edit.site' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.site" name="site" maxlength="200" (ngModelChange)="dirty.set(true)" /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="short"><mat-label>{{ 'games.edit.round' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.round" name="round" maxlength="40" (ngModelChange)="dirty.set(true)" /></mat-form-field>
          <!-- b1-rk-edit-datum-us-format: Material-Datepicker in der App-Sprache („10.10.2026“) statt <input type="date">,
               das nach der Browsersprache formatierte („10/10/2026“). header.date bleibt "YYYY-MM-DD". -->
          <app-iso-date-field class="date-field" [label]="'games.edit.date' | translate" [value]="header.date"
                              (valueChange)="header.date = $event; dirty.set(true); headerDate.set($event)" />
          <!-- Meine Seite: dreht Partieseite, Teilen-Link und Vorschaubild — und hier gleich das Brett. -->
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="short"><mat-label>{{ 'games.edit.ownerSide' | translate }}</mat-label>
            <mat-select [(ngModel)]="header.ownerSide" name="ownerSide" (ngModelChange)="onSide($event)">
              <mat-option value="">{{ 'games.edit.sideNone' | translate }}</mat-option>
              <mat-option value="white">{{ 'scoresheet.sideWhite' | translate }}</mat-option>
              <mat-option value="black">{{ 'scoresheet.sideBlack' | translate }}</mat-option>
            </mat-select></mat-form-field>
          <!-- Eigene Zeile (0.673.x, Screenshot 06.10.): Klassifizierer + Tags. In der Kopfzeile mit den Pflichtfeldern wurden die
               Beschriftungen abgeschnitten und die Hinweise zogen jedes Feld der Zeile auf drei Zeilen Höhe. Bei Online-Partien steht
               der abgeleitete Wert als Platzhalter da; leer lassen = er gilt weiter. Vorschläge aus den eigenen Partien.
               d3-rk-edit-seite-liga-beschriftung: „Plattform / Liga“ statt „Seite / Liga“ (neben „Meine Seite“ las man
               Farbe); ohne abgeleiteten Wert ein Beispiel als Platzhalter. -->
          <div class="class-row">
            <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.classifier.first' | translate }}</mat-label>
              <input matInput [(ngModel)]="header.classifier1" name="classifier1" maxlength="80" list="cls1-options"
                     [placeholder]="derived1() || ('games.classifier.firstPlaceholder' | translate)" (ngModelChange)="dirty.set(true)" /></mat-form-field>
            <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.classifier.second' | translate }}</mat-label>
              <input matInput [(ngModel)]="header.classifier2" name="classifier2" maxlength="80" list="cls2-options"
                     [placeholder]="derived2() || ('games.classifier.secondPlaceholder' | translate)" (ngModelChange)="dirty.set(true)" /></mat-form-field>
            <!-- Eigene Tags (0.662.0): Freitext, Enter oder Komma fügt hinzu; die Vorschläge sind die schon vergebenen. -->
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="tags-input"><mat-label>{{ 'games.tags.label' | translate }}</mat-label>
              <input matInput [(ngModel)]="tagInput" name="tagInput" [maxlength]="maxTagLength" list="tag-options"
                     [disabled]="tags().length >= maxTags" (keydown.enter)="$event.preventDefault(); commitTag()"
                     (input)="onTagInput()" (change)="commitTag()" /></mat-form-field>
            @if (tags().length) {
              <span class="tag-chips">
                @for (t of tags(); track t) {
                  <span class="tag-chip">#{{ t }}<button type="button" (click)="removeTag(t)" [attr.aria-label]="'games.tags.remove' | translate: { tag: t }">×</button></span>
                }
              </span>
            }
          </div>
          <datalist id="tag-options">@for (v of tagSuggestions(); track v) { <option [value]="v"></option> }</datalist>
          <datalist id="cls1-options">@for (v of options1(); track v) { <option [value]="v"></option> }</datalist>
          <datalist id="cls2-options">@for (v of options2(); track v) { <option [value]="v"></option> }</datalist>
        </mat-card>

        <!-- Formular-Werkbank (0.816.0): dasselbe Bauteil wie in LeagueHub — Foto | Brett | Prüfen + Zugliste. -->
        <app-sheet-workbench [session]="session" [photos]="ph" [(flipped)]="flipped" [evals]="evals()" [savedSans]="savedSans() ?? []"
                             [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" [materialControls]="true">
          <span sheetPhotoTools class="photo-tools">
            <button mat-icon-button (click)="openPhoto(false)" [appIconLabel]="'games.photo.show' | translate"><mat-icon>open_in_full</mat-icon></button>
            <button mat-icon-button (click)="openPhoto(true)" [appIconLabel]="'games.photo.download' | translate"><mat-icon>download</mat-icon></button>
          </span>
          <!-- Kommentar zum Zug — nur RookHub („Meine Partien"). -->
          <mat-form-field sheetPlyExtra appearance="outline" subscriptSizing="dynamic" class="comment">
            <mat-label>{{ 'games.edit.comment' | translate }}</mat-label>
            <textarea matInput rows="2" maxlength="2000" [ngModel]="current()?.comment ?? ''" (ngModelChange)="setComment($event)"></textarea>
          </mat-form-field>
        </app-sheet-workbench>
      }
    </div>
  `,
  styles: [`
    .club-linked { margin: 0 0 12px; padding: 8px 12px; border-radius: 6px; font-size: 14px;
      background: color-mix(in srgb, var(--rh-info, #1976d2) 10%, transparent); }
    /* Werkbank-Seite: am PC mehr Breite als die üblichen 1240 px (Foto + Brett + Zugliste nebeneinander). */
    .edit-page { max-width: min(1800px, 98vw); margin: 0 auto; padding: 12px 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty-state { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; }
    .header { display: flex; align-items: center; gap: 4px; margin-bottom: 8px; }
    .header h1 { margin: 0; font-size: 1.4rem; }
    .spacer { flex: 1; }
    /* Kopfdaten: kompakt, am PC in EINER Zeile (vorher zwei, die den Arbeitsbereich unter den Rand schoben). */
    /* UI-Sweep 2026-10-11 (r2-rg-edit-fields): auto-FIT statt auto-fill — die Felder teilen sich die ganze Kartenbreite,
       statt bei 150 px stehen zu bleiben (Namen und „nicht festgelegt" wurden abgeschnitten, rechts war die Zeile leer). */
    .headers { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 8px 12px; padding: 10px 12px; align-items: start;
      margin-bottom: 12px; --mat-form-field-container-height: 44px; --mat-form-field-container-vertical-padding: 10px; }
    /* PC: EINE Zeile mit Gewichten — Namen breit, Ergebnis/Runde schmal, „Meine Seite" so breit, dass „nicht festgelegt" passt. */
    @media (min-width: 1101px) {
      /* z1-rk-edit-ergebnis: „Ergebnis" samt Auswahlpfeil braucht ~112 px; Ort und Runde geben dafür ab.
         e1-rk-edit-round-cut: bei 72 px stand „Rou…" — die Runde bekommt 88 px, der Ort (1fr statt 1.2fr) gibt ab.
         b1-rk-edit-ergebnis-regression: 112 px reichten bei 1 366 px nicht („Ergebni“) — 128 px, die Veranstaltung gibt ab
         (1.4fr statt 1.6fr). Datum 164 px: Material-Datumsfeld mit Kalenderknopf (b1-rk-edit-datum-us-format). */
      .headers { grid-template-columns: minmax(0, 2fr) minmax(0, 2fr) minmax(128px, 0.9fr) minmax(0, 1.4fr) minmax(0, 1fr)
        minmax(88px, 0.6fr) minmax(164px, 1fr) minmax(180px, 1.2fr); }
    }
    /* Zweite Zeile: Seite/Liga, Modus/Jahrgang, Tags — ein gemeinsamer Hinweis darunter statt drei umbrechender. */
    .class-row { grid-column: 1 / -1; display: grid; grid-template-columns: minmax(150px, 220px) minmax(150px, 220px) minmax(180px, 1fr);
      gap: 8px 12px; align-items: center; }
    .class-row .tag-chips { grid-column: 1 / -1; }
    /* Angaben-Zeile nur am Handy; zugeklappt bleibt von der Karte nur sie (d2-rk-edit-handy-angaben). */
    .headers-toggle { display: none; }
    @media (max-width: 768px) {
      .headers-toggle { grid-column: 1 / -1; display: flex; align-items: center; gap: 8px; width: 100%; min-height: 44px;
        padding: 4px 2px; border: 0; background: transparent; color: inherit; font: inherit; text-align: left; cursor: pointer; }
      .headers-toggle .ht-names { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .headers-toggle .ht-result { flex: none; font-weight: 600; font-variant-numeric: tabular-nums; }
      .headers:not(.open) > :not(.headers-toggle) { display: none; }
    }
    @media (max-width: 768px) { .class-row { grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); } .class-row .tags-input { grid-column: 1 / -1; } }
    /* UI-Sweep 2026-10-11 (r2-rg-edit-fields): am schmalen Handy passt „Modus / Jahrgang" nicht in die halbe Breite. */
    @media (max-width: 420px) { .class-row { grid-template-columns: minmax(0, 1fr); } }
    .tag-chips { display: flex; flex-wrap: wrap; gap: 6px; }
    .tag-chip { display: inline-flex; align-items: center; gap: 4px; padding: 3px 4px 3px 10px; border-radius: 14px; font-size: 0.85rem;
      background: color-mix(in srgb, var(--rh-info, #1976d2) 14%, transparent); }
    .tag-chip button { border: 0; background: transparent; color: inherit; cursor: pointer; font-size: 1.1rem; line-height: 1; padding: 0 6px; }
    .comment { width: 100%; }
    .photo-tools { display: inline-flex; }
  `],
})
export class GameEditComponent implements OnInit, OnDestroy, LeaveConfirm {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private games = inject(GamesService);
  private sheets = inject(ScoresheetService);
  private snackbar = inject(SnackbarService);
  private confirmDialog = inject(ConfirmService);
  private translate = inject(TranslateService);
  private destroyRef = inject(DestroyRef);
  private dialog = inject(MatDialog);
  private host = inject(ElementRef);
  readonly preferences = inject(PreferencesService);

  readonly results = ['*', '1-0', '0-1', '1/2-1/2'];

  gameId = 0;
  readonly loading = signal(true);
  readonly notFound = signal(false);
  /** Laden gescheitert (Netz/Server, nicht 4xx) — „Erneut versuchen" statt „gibt es nicht" (Codereview F4-011). */
  readonly loadError = signal<unknown>(null);
  readonly saving = signal(false);
  readonly dirty = signal(false);
  /** Angaben-Karte am Handy aufgeklappt? Vorgabe zu; offen, wenn beim Laden ein Name fehlt. */
  readonly headersOpen = signal(false);
  /** Absatztrenner für zusammengesetzte Hilfetexte (app-help-hint: Absätze mit Leerzeile). */
  readonly paraSep = '\n\n';
  readonly flipped = signal(false);
  /** Die Formular-Fotos (Seiten) — dieselbe Verwaltung wie in LeagueHub (Formular-Werkbank, 0.816.0). */
  readonly ph = new ScanPhotos({
    fetchView: () => Promise.reject(new Error('RookHub hat keine weiteren Fotos je Seite')),
    setPageSize: (page, w, h) => this.session.setPageSize(page, w, h),
  });
  private destroyed = false;

  /** Der Arbeitsstand (geteilt mit der Formular-Korrektur in LeagueHub). */
  readonly session = new SheetEditSession({
    resolve: (prefix, writtenFrom) => this.sheets.resolve(this.gameId, prefix, writtenFrom),
    changed: () => this.dirty.set(true),
    moved: () => this.revealCursor(),
    resolveFailed: () => this.snackbar.info(this.translate.instant('games.edit.resolveFailed')),
    bind: o => o.pipe(takeUntilDestroyed(this.destroyRef)),
  });
  readonly plies = this.session.plies;
  readonly cursor = this.session.cursor;
  readonly mode = this.session.mode;
  readonly unresolved = this.session.unresolved;
  readonly isScoresheet = this.session.isScoresheet;
  /** Kopie einer Vereinspartie (0.660.0) — Hinweis, dass eine Zugkorrektur dorthin geht oder die Kopie löst. */
  readonly clubGameId = signal<number | null>(null);
  readonly photoSize = this.session.photoSize;
  readonly busy = this.session.busy;
  readonly legalCount = this.session.legalCount;
  readonly illegalCount = this.session.illegalCount;
  readonly cursorFen = this.session.cursorFen;
  readonly current = this.session.current;
  readonly lastMove = this.session.lastMove;
  readonly arrows = this.session.arrows;
  readonly crop = this.session.crop;
  readonly uncertainLeft = this.session.uncertainLeft;
  readonly rows = this.session.rows;
  /** Die Züge, wie sie gespeichert sind — nur zu ihnen passt die Analyse der Partie (Bewertungskurve der Werkbank). */
  readonly savedSans = signal<string[] | null>(null);
  readonly movesChanged = computed(() => {
    const saved = this.savedSans();
    const now = this.plies();
    return !!saved && (saved.length !== now.length || now.some((p, i) => p.illegal || p.san !== saved[i]));
  });
  /** Die Analyse der gespeicherten Partie (falls gerechnet) — die Werkbank zeichnet daraus die Kurve. */
  readonly evals = signal<GameEvals | null>(null);

  header = { white: '', black: '', result: '*', event: '', site: '', round: '', date: '', ownerSide: '', classifier1: '', classifier2: '' };

  /** Der abgeleitete Wert (nur Online-Partien) — Platzhalter, solange der Nutzer nichts einträgt. */
  readonly derived1 = signal('');
  readonly derived2 = signal('');
  /** Vorschläge: Werte der eigenen Partien; beim Jahrgang zusätzlich die Saison zum Spieldatum. */
  private readonly known1 = signal<string[]>([]);
  private readonly known2 = signal<string[]>([]);
  readonly options1 = computed(() => this.known1());
  readonly options2 = computed(() => {
    const season = seasonOf(this.headerDate());
    const all = this.known2();
    return season && !all.includes(season) ? [season, ...all] : all;
  });
  readonly headerDate = signal('');

  /** Eigene Tags der Partie + das Eingabefeld; Vorschläge sind die schon vergebenen, soweit noch nicht an dieser Partie. */
  readonly tags = signal<string[]>([]);
  tagInput = '';
  readonly maxTags = MAX_TAGS;
  readonly maxTagLength = MAX_TAG_LENGTH;
  private readonly knownTags = signal<string[]>([]);
  readonly tagSuggestions = computed(() => {
    const own = this.tags().map(t => t.toLowerCase());
    return this.knownTags().filter(t => !own.includes(t.toLowerCase()));
  });

  commitTag(): void {
    const next = addTag(this.tags(), this.tagInput);
    if (next) { this.tags.set(next); this.dirty.set(true); }
    this.tagInput = '';
  }

  /** Ein Komma schließt den Tag ab, wie Enter. */
  onTagInput(): void {
    if (this.tagInput.includes(',')) this.commitTag();
  }

  removeTag(tag: string): void {
    this.tags.set(this.tags().filter(t => t !== tag));
    this.dirty.set(true);
  }

  ngOnInit(): void {
    this.gameId = Number(this.route.snapshot.paramMap.get('id'));
    this.fetch();
  }

  /** „Erneut versuchen" nach einem Ladefehler. */
  reload(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.fetch();
  }

  private fetch(): void {
    this.games.get(this.gameId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: game => this.load(game),
      error: e => {
        if (loadErrorKind(e) === 'failed') this.loadError.set(e ?? true);
        else this.notFound.set(true);
        this.loading.set(false);
      },
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.ph.dispose();
  }

  /** Vor dem Verlassen fragen, solange es ungespeicherte Korrekturen gibt (`unsavedChangesGuard`). Speichern setzt
   *  `dirty` vor dem Weiterleiten zurück — dann ohne Rückfrage. */
  canLeave(): boolean | Observable<boolean> {
    return !this.dirty() || this.confirmDialog.ask('games.edit.discardChanges');
  }

  /** Neu laden oder Tab schließen: der Browser fragt mit seinem eigenen Text. */
  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(e: BeforeUnloadEvent): void {
    if (!this.dirty()) return;
    e.preventDefault();
    e.returnValue = true;   // ältere Browser fragen nur so
  }


  private load(game: SavedGameDetail): void {
    const h = headersOf(game.pgn);
    this.header = {
      white: game.white ?? '', black: game.black ?? '', result: game.result || '*',
      event: h['Event'] ?? '', site: h['Site'] ?? '', round: h['Round'] ?? '', date: isoDateOf(h['Date']),
      ownerSide: game.ownerSide ?? '',
      classifier1: game.classifier1Set ?? '', classifier2: game.classifier2Set ?? '',
    };
    this.headerDate.set(this.header.date);
    this.headersOpen.set(!this.header.white.trim() || !this.header.black.trim());
    this.tags.set(game.tags ?? []);
    // Abgeleitet = geltender Wert, solange nichts gesetzt ist (der Server liefert nur den geltenden).
    this.derived1.set(game.classifier1Set ? '' : game.classifier1 ?? '');
    this.derived2.set(game.classifier2Set ? '' : game.classifier2 ?? '');
    this.games.list(500).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => { this.known1.set(distinctClassifiers(list, 1)); this.known2.set(distinctClassifiers(list, 2)); this.knownTags.set(distinctTags(list)); },
      error: () => { /* Vorschläge sind Zugabe */ },
    });
    // RepCheck-Partien tragen „RepCheck saved game" als Veranstaltung — das ist keine Angabe des Nutzers.
    if (this.header.event === 'RepCheck saved game') this.header.event = '';
    const fromPgn = pliesOfPgn(game.pgn);
    this.savedSans.set(fromPgn.map(p => p.san));
    this.games.evals(this.games.evalsUrl(this.gameId)).pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe(e => this.evals.set(e));
    this.session.startFen.set(startFenOf(game.pgn));   // Stellungspartie (FEN-Kopf): Brett und Legalität ab dort
    this.flipped.set(game.ownerSide === 'black');

    this.clubGameId.set(game.clubGameId ?? null);
    // Kopie einer Vereinspartie, deren Formular noch aufbewahrt ist (0.660.0): wie eine eingelesene Partie korrigieren
    if (!game.scanId && !game.clubSheet) {
      this.plies.set(fromPgn);
      this.loading.set(false);
      return;
    }
    this.isScoresheet.set(true);
    this.sheets.editState(this.gameId).pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef)).subscribe(state => {
      const pages = Math.max(1, state?.pageCount ?? 1);
      void this.ph.load(pages, page => firstValueFrom(this.sheets.photo(this.gameId, page)), () => !this.destroyed);
      const comments = fromPgn.map(p => stripSheetNotes(p.comment));
      // Der gespeicherte Stand gilt nur, wenn er zu den Zügen der Partie passt (sie kann anderswo geändert worden sein).
      const matches = state && state.plies.length === fromPgn.length && state.plies.every((p, i) => p.san === fromPgn[i].san);
      if (matches) {
        this.session.loadSheet(state!, comments);
      } else {
        this.plies.set(fromPgn.map((p, i) => ({ ...p, comment: comments[i] })));
        this.session.boxes.set(state?.boxes ?? []);
        this.session.sheetEntries.set(state?.written ?? []);
        this.session.entryPages.set(state?.pages ?? []);
        this.session.goToFirstUncertain();
      }
      this.loading.set(false);
    });
  }

  onSide(side: string): void {
    this.dirty.set(true);
    this.flipped.set(side === 'black');
  }

  go(i: number): void {
    this.session.go(i);
  }

  /** Der gewählte Halbzug soll in der Zugliste sichtbar bleiben (Pfeiltasten, „nächste unsichere Stelle") — NUR in deren
   *  eigenem Rollbereich. `scrollIntoView` rollte am Handy die ganze Seite, und das Brett wanderte bei jedem Zug nach oben
   *  (gemeldet 2026-09-28, dieselbe Falle wie 0.514.1 auf der Partieseite). */
  private revealCursor(): void {
    setTimeout(() => {
      const el = (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>('.moves .ply.cursor');
      if (el) scrollIntoContainer(el);
    });
  }

  nextUncertain(): void {
    this.session.nextUncertain();
  }

  plyLabel(i: number): string {
    return this.session.plyLabel(i);
  }

  plyClass(i: number): Record<string, boolean> {
    return this.session.plyClass(i);
  }

  /** Ein Zug am Brett: ersetzt den Halbzug am Cursor (oder fügt davor ein). */
  onBoardMove(m: UserBoardMove): void {
    this.session.play(m.san);
  }

  choose(o: Parameters<SheetEditSession['choose']>[0]): void {
    this.session.choose(o);
  }

  remove(): void {
    this.session.remove();
  }

  confirm(): void {
    this.session.confirm();
  }

  setComment(text: string): void {
    this.session.setComment(text);
  }

  openPhoto(download: boolean): void {
    const page = this.ph.shownPage();
    const blob = this.ph.blob(page);
    if (!blob) return;
    if (download) openPhotoBlob(blob, photoFileName(this.gameId, blob, page));
    else ScoresheetPhotoDialogComponent.open(this.dialog, this.gameId, page);
  }

  save(): void {
    if (this.illegalCount() > 0) {
      this.confirmDialog.ask('games.edit.dropIllegal', { count: this.illegalCount() })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(ok => { if (ok) this.doSave(); });
      return;
    }
    this.doSave();
  }

  private doSave(): void {
    this.commitTag();   // ein noch nicht bestätigter Tag geht beim Speichern nicht verloren
    const legal = this.plies().filter(p => !p.illegal);
    const comments = this.isScoresheet()
      ? commentsForSave(legal, this.unresolved())
      : legal.map(p => p.comment?.trim() || null);
    this.saving.set(true);
    this.sheets.update(this.gameId, {
      moves: legal.map((p, i) => ({ san: p.san, comment: comments[i] })),
      white: this.header.white || null,
      black: this.header.black || null,
      result: this.header.result || '*',
      event: this.header.event || null,
      site: this.header.site || null,
      round: this.header.round || null,
      date: this.header.date || null,
      ownerSide: this.header.ownerSide,
      classifier1: this.header.classifier1.trim(),
      classifier2: this.header.classifier2.trim(),
      tags: this.tags(),
      scoresheetPlies: this.isScoresheet() ? toServer(legal) : null,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.saving.set(false);
        this.dirty.set(false);
        this.plies.set(legal);
        this.snackbar.success(this.translate.instant('games.edit.saved'));
        this.router.navigate(['/games', this.gameId]);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.snackbar.info(err.error?.message ?? this.translate.instant('games.edit.saveFailed'));
      },
    });
  }
}
