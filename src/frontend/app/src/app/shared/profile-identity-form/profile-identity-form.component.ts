import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ProfileService } from '@rh/core/profile.service';
import { SnackbarService } from '@rh/core/snackbar.service';

/** Die Felder, die BEIDE Oberflaechen fuehren. Der Aufrufer besitzt das Objekt und speichert es. */
export interface ProfileIdentity {
  username: string;
  email: string | null;
  firstName: string | null;
  lastName: string | null;
  displayName: string | null;
  fideId: string | null;
  chessResultsId: string | null;
}

export interface PlayerSearchItem {
  name: string;
  fideId: string | null;
  chessResultsId: string | null;
  elo: number | null;
  country: string | null;
  title: string | null;
}

export interface PlayerSearchResult {
  chessResultsResults: PlayerSearchItem[];
  fideResults: PlayerSearchItem[];
}

/**
 * Name, Anzeigename, E-Mail und die zwei Spielerkennungen — samt der Spielersuche, die die
 * Kennungen fuellt.
 *
 * <p><b>Warum geteilt.</b> Dieselben sechs Felder standen in ZWEI Komponenten getippt (RookHubs
 * Profilseite und die der Turnierseite). Sie gehen ueber denselben `PUT /api/profile`, also war
 * es schon dieselbe Wahrheit auf dem Server — nur nicht im Code: ein Feld zu ergaenzen oder einen
 * Hinweis zu aendern hiess, es an zwei Stellen zu tun, und die zweite wurde vergessen. Die
 * Turnierseite hatte deshalb auch die SPIELERSUCHE nicht, obwohl gerade dort alles an den
 * Kennungen haengt (der Turnierverlauf sucht ueber den Namen, die Kennung entscheidet bei
 * Namensgleichheit).</p>
 *
 * <p>Die Komponente aendert das uebergebene Objekt DIREKT (wie zuvor die beiden Formulare per
 * `ngModel`) und speichert nichts: wann und was gespeichert wird, entscheidet die Seite — RookHub
 * schickt zusaetzlich chess.com/Lichess mit, die Turnierseite bewusst nicht.</p>
 *
 * <p>`ngModelOptions: standalone` bewusst gesetzt: die Felder liegen im DOM in einem `<form>` der
 * ELTERNkomponente und wuerden sich sonst dort registrieren — die Komponente haengt damit von
 * einem Formular ab, das sie nicht kennt.</p>
 */
@Component({
  selector: 'app-profile-identity-form',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [
    FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatProgressSpinnerModule, TranslatePipe,
  ],
  template: `
    <div class="pif-names">
      <mat-form-field appearance="outline">
        <mat-label>{{ 'profile.firstName' | translate }}</mat-label>
        <input matInput [(ngModel)]="profile.firstName" [ngModelOptions]="{ standalone: true }"
               name="firstName" autocomplete="given-name">
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>{{ 'profile.lastName' | translate }}</mat-label>
        <input matInput [(ngModel)]="profile.lastName" [ngModelOptions]="{ standalone: true }"
               name="lastName" autocomplete="family-name">
      </mat-form-field>
      <button mat-stroked-button type="button" class="pif-search" (click)="searchPlayer()"
              [disabled]="!searchable || searching">
        <!-- Lupe und Spinner stehen je ALLEIN in ihrem Zweig, der Text ausserhalb: nur einen
             Block mit genau einem Wurzelelement projiziert MatButton in seinen Icon-Slot (18 px,
             Aussenabstand). Standen Icon UND Text im selben Zweig, ging der ganze Block in den
             Text-Slot — 24-px-Lupe ohne Abstand, und der Knopf sprang beim Suchen in der Breite
             (Compiler-Warnung NG8011). -->
        @if (searching) {
          <mat-spinner matButtonIcon diameter="18" />
        } @else {
          <mat-icon>search</mat-icon>
        }
        {{ 'profile.searchPlayer' | translate }}
      </button>
    </div>

    @if (results) {
      <div class="pif-results">
        @if (!results.chessResultsResults.length && !results.fideResults.length) {
          <p class="muted">{{ 'profile.noResults' | translate }}</p>
        }
        @if (results.chessResultsResults.length) {
          <h4>ChessResults</h4>
          <div class="pif-hits">
            @for (p of results.chessResultsResults; track p.name + p.chessResultsId) {
              <button type="button" class="pif-hit" (click)="selectChessResultsPlayer(p)">
                <span class="pif-player">
                  @if (p.title) { <strong>{{ p.title }}</strong> }
                  <span class="pif-name">{{ p.name }}</span>
                  @if (p.elo) { <span class="muted">({{ p.elo }})</span> }
                  @if (p.country) { <span class="muted">{{ p.country }}</span> }
                  @if (p.chessResultsId) { <span class="muted">CR: {{ p.chessResultsId }}</span> }
                  @if (p.fideId) { <span class="muted">FIDE: {{ p.fideId }}</span> }
                </span>
                <mat-icon>arrow_forward</mat-icon>
              </button>
            }
          </div>
        }
        @if (results.fideResults.length) {
          <h4>FIDE</h4>
          <div class="pif-hits">
            @for (p of results.fideResults; track p.name + p.fideId) {
              <button type="button" class="pif-hit" (click)="selectFidePlayer(p)">
                <span class="pif-player">
                  @if (p.title) { <strong>{{ p.title }}</strong> }
                  <span class="pif-name">{{ p.name }}</span>
                  @if (p.elo) { <span class="muted">({{ p.elo }})</span> }
                  @if (p.country) { <span class="muted">{{ p.country }}</span> }
                  @if (p.fideId) { <span class="muted">FIDE: {{ p.fideId }}</span> }
                </span>
                <mat-icon>arrow_forward</mat-icon>
              </button>
            }
          </div>
        }
      </div>
    }

    <mat-form-field appearance="outline" class="pif-full" subscriptSizing="dynamic">
      <mat-label>{{ 'profile.displayName' | translate }}</mat-label>
      <input matInput [(ngModel)]="profile.displayName" [ngModelOptions]="{ standalone: true }"
             name="displayName">
      <mat-hint>{{ 'profile.displayNameHint' | translate: { username: profile.username } }}</mat-hint>
    </mat-form-field>

    <mat-form-field appearance="outline" class="pif-full" subscriptSizing="dynamic">
      <mat-label>{{ 'profile.email' | translate }}</mat-label>
      <input matInput type="email" [(ngModel)]="profile.email" [ngModelOptions]="{ standalone: true }"
             name="email" autocomplete="email" inputmode="email">
      <mat-hint>{{ 'profile.emailHint' | translate }}</mat-hint>
    </mat-form-field>

    <div class="pif-ids">
      <mat-form-field appearance="outline">
        <mat-label>{{ 'profile.fideId' | translate }}</mat-label>
        <input matInput [(ngModel)]="profile.fideId" [ngModelOptions]="{ standalone: true }"
               name="fideId" inputmode="numeric">
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>{{ 'profile.chessResultsId' | translate }}</mat-label>
        <input matInput [(ngModel)]="profile.chessResultsId" [ngModelOptions]="{ standalone: true }"
               name="chessResultsId" inputmode="numeric">
      </mat-form-field>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .pif-names, .pif-ids { display: flex; gap: 12px; flex-wrap: wrap; align-items: flex-start; }
    .pif-names mat-form-field, .pif-ids mat-form-field { flex: 1 1 12rem; }
    /* Der Knopf steht neben den Feldern und soll auf HOEHE der Eingabe liegen, nicht an deren
       Oberkante — ein Material-Feld ist 56 px hoch, der Knopf 36 px. Der UNTERE Abstand ist fuer
       den umgebrochenen Fall (Handy): dort ist der Knopf das letzte Kind der Zeile und hat keinen
       Hinweis-Bereich unter sich wie die Felder, und das schwebende Label des Folgefelds
       ("Anzeigename") ragt rund 7 px ueber dessen Oberkante — es lief durch die Unterkante des
       Knopfs. Auf dem Desktop (eine Zeile, Felder 78 px hoch) aendert er nichts. */
    .pif-search { margin-top: 10px; margin-bottom: 14px; align-self: flex-start; }
    /* mat-spinner traegt keine .mat-icon-Klasse, die Icon-Offsets des Outlined-Knopfs (-8/8 px)
       greifen also nicht — nachgestellt, damit der Knopf beim Suchen nicht in der Breite springt. */
    .pif-search mat-spinner { margin: 0 8px 0 -8px; }
    /* Die zwei Felder mit Hinweistext tragen subscriptSizing="dynamic": bei fester Groesse ist
       nur EINE Zeile reserviert, und der laengere Hinweis (E-Mail) lief in das Feld darunter. */
    .pif-full { width: 100%; margin-bottom: 1rem; }
    /* Bei vielen Namensgleichen (bis 50 je Quelle) schob die Trefferliste Anzeigename, E-Mail,
       Kennungen und Speichern auf dem Handy um rund drei Bildschirmhoehen nach unten — nach dem
       Antippen eines Treffers sah man die uebernommenen Kennungen nicht. Deshalb EIN Rollbereich
       um beide Listen (die Ueberschriften rollen mit); Muster .hh-tooltip in styles.scss. */
    .pif-results {
      margin: 4px 0 16px; max-height: min(50vh, 420px); overflow-y: auto; overscroll-behavior: contain;
    }
    .pif-results h4 { margin: 8px 0 4px; }

    .pif-hits { display: flex; flex-direction: column; gap: 4px; }
    /* Eigene Zeile statt mat-list-item: dessen Aufbau erwartet ausgezeichnete Kinder
       (matListItemTitle …); mit blossem Text darin fiel die Zeilenhoehe zusammen und die
       Angaben klebten aneinander. */
    .pif-hit {
      display: flex; align-items: center; justify-content: space-between; gap: 12px;
      width: 100%; padding: 10px 12px; min-height: 44px;
      border: 1px solid color-mix(in srgb, currentColor 20%, transparent);
      border-radius: 8px; background: transparent; color: inherit;
      font: inherit; text-align: left; cursor: pointer;
    }
    .pif-hit:hover { background: color-mix(in srgb, currentColor 8%, transparent); }
    /* Der Pfeil ist ein Flex-Kind mit overflow: hidden und darf deshalb bis auf 0 schrumpfen: in
       Zeilen mit CR- UND FIDE-Kennung war er auf dem Handy nur noch ein 12-px-Strich, in den
       Nachbarzeilen ganz. Nachgeben soll der Text (min-width: 0), nicht der Pfeil. */
    .pif-hit mat-icon { flex-shrink: 0; }
    .pif-player {
      display: flex; gap: 8px; flex-wrap: wrap; align-items: baseline; line-height: 1.5; min-width: 0;
    }
    .pif-name { font-weight: 500; }
    .muted { opacity: 0.7; }
  `],
})
export class ProfileIdentityFormComponent {
  @Input({ required: true }) profile!: ProfileIdentity;

  private readonly profiles = inject(ProfileService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  searching = false;
  results: PlayerSearchResult | null = null;

  /** Die Suche laeuft ueber den NACHNAMEN — unter zwei Zeichen ist sie sinnlos. */
  get searchable(): boolean {
    return (this.profile?.lastName?.trim().length ?? 0) >= 2;
  }

  searchPlayer(): void {
    if (!this.searchable) return;
    this.searching = true;
    this.results = null;

    this.profiles.searchPlayer<PlayerSearchResult>(
      this.profile.lastName!.trim(), this.profile.firstName?.trim() || undefined).subscribe({
      next: results => {
        this.searching = false;
        this.results = results;

        // Genau ein Treffer je Quelle: direkt uebernehmen, das ist der haeufige Fall.
        const cr = results.chessResultsResults.length === 1 ? results.chessResultsResults[0] : null;
        const fide = results.fideResults.length === 1 ? results.fideResults[0] : null;
        if (cr) this.selectChessResultsPlayer(cr);
        // Einen einzelnen FIDE-Treffer nur uebernehmen, wenn der chess-results-Treffer nicht
        // schon eine (zu SEINEM Spieler gehoerende) FIDE-Kennung geliefert hat — sonst
        // ueberschreibt ein fremder Namensgleicher sie.
        if (fide && !(cr && cr.fideId)) this.selectFidePlayer(fide);
      },
      error: () => {
        this.searching = false;
        this.snackbar.info(this.translate.instant('profile.searchFailed'));
      },
    });
  }

  selectChessResultsPlayer(p: PlayerSearchItem): void {
    if (p.chessResultsId) this.profile.chessResultsId = p.chessResultsId;
    if (p.fideId) this.profile.fideId = p.fideId;
    this.snackbar.success(this.translate.instant('profile.chessResultsApplied'));
  }

  selectFidePlayer(p: PlayerSearchItem): void {
    if (p.fideId) this.profile.fideId = p.fideId;
    this.snackbar.success(this.translate.instant('profile.fideApplied'));
  }
}
