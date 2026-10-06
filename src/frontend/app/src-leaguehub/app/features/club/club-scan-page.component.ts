import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, OnDestroy, OnInit, computed, inject, signal, effect, untracked, viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgClass } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { ChessBoardComponent, UserBoardMove } from '@rh/shared/pgn-viewer/chess-board.component';
import { scrollIntoContainer } from '@rh/shared/pgn-viewer/move-list.component';
import { isBoardHotkey } from '@rh/shared/keyboard.util';
import { SheetEditSession } from '@rh/features/games/sheet-edit-session';
import { SECONDS_PER_MOVE, SecondsTicker, formatClock, readingSeconds } from '@rh/features/games/scoresheet-timing';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { ClubGameDetail, ClubPairing, ClubSheetState, LeagueScanState, RosterPerson, SideMatch } from '../../core/club.models';
import { pliesOfPgn, toServer } from '@rh/features/games/game-edit.util';
import { ANON_NAME, SheetPgnInput, isTransientError, loadErrorText, normalizeResult, presetYear, reasonText, sheetPgn, sheetPgnFileName } from '../../core/club-format';
import { rookHubUrlForLeagueHub } from '@rh/core/partner-site';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { firstValueFrom } from 'rxjs';
import { downloadBlob } from '@rh/shared/download.util';
import { rememberAnonKey } from './club-add-page.component';
import { PlayerSearchComponent } from './player-search.component';
import { pairingText } from './import-review';
import { AccessGateComponent } from '../../shared/access-gate.component';
import { de } from '../../core/league-format';

type Side = 'white' | 'black';
const POLL_MS = 3000;
const MATCH_DEBOUNCE_MS = 400;
/** So viele Abrufe hintereinander dürfen still scheitern (Neustart, Funkloch), bevor die Seite es sagt (UX-034). */
const SILENT_FAILURES = 3;

/**
 * Ein eingelesenes Partieformular prüfen und in die Vereins-Datenbank übernehmen (`/verein/formular/:id`).
 * Die Korrektur selbst ist DIESELBE wie in RookHub (`SheetEditSession`: Cursor-Brett, Lesarten, Ersetzen/Einfügen/
 * Löschen, nach jeder Änderung wird der Rest aus den Formular-Einträgen neu aufbereitet); dazu die Namen mit
 * Ligaspieler-Prüfung und „durch Schwaz ersetzen" je Seite (Vorgabe: Spieler von Schwaz und die eigene Seite). Angemeldet
 * unter `/verein/formular/:id`, ohne Konto über einen Teilen-Link unter `/s/:token/formular/:key`. Übernehmen oder
 * Verwerfen schließt die Einlesung — das Foto verschwindet.
 */
/** Gemerkte Abwahl von „Automatisch zu meinen Partien hinzufügen" (je Gerät). */
export const AUTO_MINE_KEY = 'lh-auto-my-games';
function readAutoMine(): boolean {
  try { return localStorage.getItem(AUTO_MINE_KEY) !== '0'; } catch { return true; }
}

@Component({
  selector: 'lh-club-scan-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgClass, ChessBoardComponent, PlayerSearchComponent, AccessGateComponent],
  template: `
    @if (!allowed) {
      <!-- UX-033: jetzt mit „Angemeldet als" (falsches Konto?) und dem nächsten Schritt. -->
      @if (canRead) {
        <lh-access-gate text="Du kannst die Vereinspartien lesen. Zum Einlesen von Partieformularen fehlt dir noch die Freigabe — frag einen Verwalter."
                        purpose="das Einlesen von Partieformularen" [back]="{ link: '/verein', label: 'Zu den Vereinspartien' }" />
      } @else {
        <lh-access-gate text="Partieformulare einlesen dürfen Admins und die Vereinsgruppe von SK Schwaz." />
      }
    } @else if (notFound() && gameId != null) {
      <section class="gate"><h2>Partie nicht gefunden</h2>
        <p>Sie ist gelöscht oder du darfst sie nicht korrigieren (das dürfen, wer sie hochgeladen hat, und die Verwalter).
          <a routerLink="/verein">Zu den Vereinspartien</a></p></section>
    } @else if (notFound()) {
      <section class="gate"><h2>Formular nicht gefunden</h2>
        <p>Es wurde schon übernommen oder verworfen. <a [routerLink]="backLink" [queryParams]="{ art: 'formular' }">Zu deinen Formularen</a></p></section>
    } @else if (loadError(); as e) {
      <!-- UX-034: früher stand hier minutenlang „Lade …", während die Seite still alle 3 s nachfragte. -->
      <section class="gate"><h2>Das Formular lässt sich gerade nicht laden</h2>
        <p>{{ e }} Dein Formular geht dadurch nicht verloren.</p>
        <div class="actions">
          <button type="button" class="btn-sec" (click)="reload()">Neu laden</button>
          <a class="back-link" [routerLink]="backLink" [queryParams]="{ art: 'formular' }">← Deine Formulare</a>
        </div></section>
    } @else if (state(); as st) {
      <section class="club-intro">
        @if (gameId != null) {
          <p><a class="back-link" routerLink="/verein">← Vereinspartien</a></p>
          <h2>Vereinspartie korrigieren</h2>
          <p class="muted">{{ gameTitle() }}@if (!photoUrl()) { — das Formular ist nicht mehr aufbewahrt, korrigiert wird am Brett. }</p>
        } @else {
          <p><a class="back-link" [routerLink]="backLink" [queryParams]="{ art: 'formular' }">← Deine Formulare</a></p>
          <h2>Partieformular prüfen</h2>
        }
        @if (st.scan.status !== 'done') {
          @if (st.scan.status === 'failed') { <p class="muted" role="status">Das Formular ließ sich nicht lesen.</p> }
          @else {
            <p class="muted" role="status">Das Formular wird noch gelesen … <b class="scan-clock">{{ clock() }}</b>
              <span class="small"> — das dauert meist ein paar Minuten.</span></p>
          }
        } @else {
          <p class="muted">Orange markiert sind unsichere Stellen: dort die richtige Lesart wählen oder den Zug am Brett spielen
            — danach wird der Rest neu gelesen. Pfeiltasten blättern.</p>
          <!-- UX-036: Namen und „Übernehmen" stehen unter Zugliste (und Foto) — am Handy ≈ 2 700 px tiefer, ohne Hinweis. -->
          <p class="small"><button type="button" class="btn-link to-save" (click)="toSave()">{{ gameId != null ? 'Weiter zum Speichern ↓' : 'Weiter zu Namen & Übernehmen ↓' }}</button></p>
        }
      </section>

      @if (st.scan.status === 'done') {
        <div class="scan-wrap"><div class="scan-layout" [class.with-photo]="!!photoUrl()">
          @if (photoUrl(); as src) {
            <section class="panel scan-photo" [class.open]="photoOpen()">
              <p class="photo-toggle"><button type="button" class="btn-link" [attr.aria-expanded]="photoOpen()" (click)="photoOpen.set(!photoOpen())">
                {{ photoOpen() ? 'Ganzes Foto ausblenden' : 'Ganzes Foto zeigen' }}</button></p>
              @if (pageCount() > 1) {
                <div class="seg pager" role="group" aria-label="Seite des Formulars">
                  @for (n of pageNumbers(); track n) {
                    <button type="button" [attr.aria-pressed]="shownPage() === n" (click)="shownPage.set(n)">Seite {{ n }}</button>
                  }
                </div>
              }
              <div class="photo-scroll" [class.zoom]="zoom()">
                <div class="photo-frame">
                  <img [src]="src" alt="Foto des Partieformulars" (load)="onPhotoLoad($event)" />
                  @if (s.mark(); as m) { @if (m.page === shownPage()) {
                    <div class="photo-mark" [class.uncertain]="m.uncertain" [style.left.%]="m.left" [style.top.%]="m.top"
                         [style.width.%]="m.width" [style.height.%]="m.height" aria-hidden="true"></div>
                  } }
                </div>
              </div>
              <p class="photo-zoom"><button type="button" class="btn-link" (click)="zoom.set(!zoom())">{{ zoom() ? 'Kleiner' : 'Größer' }}</button></p>
            </section>
          }

          <section class="panel scan-board">
            <app-chess-board [fen]="s.cursorFen()" [lastMove]="s.lastMove()" [arrows]="s.arrows()" [flipped]="ownerSide() === 'black'"
                             [playable]="!s.busy()" (userMove)="onBoardMove($event)" />
            <div class="board-nav">
              <button type="button" class="btn-sec" (click)="s.go(0)" [disabled]="s.cursor() === 0" aria-label="Zum Anfang">⏮</button>
              <button type="button" class="btn-sec" (click)="s.go(s.cursor() - 1)" [disabled]="s.cursor() === 0" aria-label="Zug zurück">◀</button>
              <button type="button" class="btn-sec" (click)="s.go(s.cursor() + 1)" [disabled]="s.cursor() >= s.legalCount()" aria-label="Zug vor">▶</button>
              <button type="button" class="btn-sec" (click)="s.go(s.legalCount())" [disabled]="s.cursor() >= s.legalCount()" aria-label="Zum Ende">⏭</button>
            </div>
            <div class="board-nav">
              <div class="seg" role="group" aria-label="Zug am Brett">
                <button type="button" [attr.aria-pressed]="s.mode() === 'replace'" (click)="s.mode.set('replace')">Ersetzen</button>
                <button type="button" [attr.aria-pressed]="s.mode() === 'insert'" (click)="s.mode.set('insert')">Davor einfügen</button>
              </div>
              @if (s.uncertainLeft() > 0) {
                <button type="button" class="btn-sec warn" (click)="s.nextUncertain()">Nächste unsichere Stelle ({{ s.uncertainLeft() }})</button>
              }
            </div>
          </section>

          <!-- Am Handy steht dieser Teil OBEN (Wunsch 2026-09-28): die Zeile des Formulars, die Lesarten und „Stimmt so"
               auf einem Bildschirm. -->
          <section class="panel scan-check">
            <div class="cursor-panel" aria-live="polite">
              @if (photoUrl(); as src) {
                @if (s.crop(); as c) {
                  <div class="crop">
                    <div class="crop-frame" [class.uncertain]="c.uncertain" [style.aspect-ratio]="c.view.aspect">
                      <img [src]="pageUrl(c.page) ?? src" alt="Ausschnitt des Formulars" [style.width.%]="c.view.imgW" [style.height.%]="c.view.imgH"
                           [style.left.%]="c.view.left" [style.top.%]="c.view.top" />
                      <div class="crop-mark" [style.left.%]="c.view.markLeft" [style.top.%]="c.view.markTop"
                           [style.width.%]="c.view.markW" [style.height.%]="c.view.markH"></div>
                    </div>
                  </div>
                }
              }
              @if (s.busy()) { <p class="muted small">Lese den Rest neu …</p> }
              @if (s.canUndoRemove()) {
                <p class="undo-line small">Zug gelöscht.
                  <button type="button" class="btn-sec" [disabled]="s.busy()" (click)="s.undoRemove()">Rückgängig</button></p>
              }
              @if (s.current(); as p) {
                <p class="where">
                  <b>{{ s.plyLabel(s.cursor()) }}</b> <span class="san" [class.bad]="p.illegal">{{ de(p.san) }}</span>
                  @if (p.written) { <span class="muted small">auf dem Formular: {{ p.written }}</span> }
                  @if (p.match === 'inserted') { <span class="chip warn">nicht auf dem Formular</span> }
                  @if (p.uncertain && !p.confirmed) { <span class="chip warn">unsicher</span> }
                  @if (p.check && !p.confirmed) {
                    <span class="chip warn" title="Nach diesem Zug sprang die Bewertung hin und her — meist ein falsch gelesener Zug. Die Lesart ohne das Zickzack steht bei den Lesarten.">{{ p.check === 'replaced' ? 'von der Engine korrigiert' : 'Engine zweifelt' }}</span>
                  }
                  @if (p.confirmed) { <span class="chip ok">bestätigt</span> }
                  @if (p.illegal) { <span class="chip bad">nicht legal</span> }
                </p>
                @if (p.options?.length && !p.illegal) {
                  <div class="options">
                    <span class="muted small">Mögliche Lesarten</span>
                    @for (o of p.options; track o.uci) {
                      <button type="button" class="option" [class.chosen]="o.uci === p.uci" [disabled]="s.busy()" (click)="s.choose(o)">
                        <b>{{ de(o.san) }}</b> <span class="small">passt zu {{ o.reach }} folgenden Einträgen</span>
                        @if (o.preview.length) { <span class="small muted">→ {{ de(o.preview.join(' ')) }}</span> }
                      </button>
                    }
                  </div>
                }
                <div class="actions">
                  @if (!p.illegal && !p.confirmed) { <button type="button" class="btn-sec" (click)="s.confirm()">Stimmt so</button> }
                  <!-- Abgesetzt von „Stimmt so" (UX-070): Link-Optik in Rot, ganz rechts — „Stimmt so" ist die häufigste
                       Aktion der Seite, ein Fehlgriff daneben nahm den Zug heraus. -->
                  <button type="button" class="btn-link del" [disabled]="s.busy()" (click)="s.remove()">Zug löschen</button>
                </div>
                <p class="muted small">{{ s.mode() === 'insert' ? 'Ein Zug am Brett wird VOR diesem eingefügt.' : 'Ein Zug am Brett ersetzt diesen.' }}</p>
              } @else {
                <p class="muted small">Ende der Partie — ein Zug am Brett hängt einen an.</p>
                @if (s.unresolved().length) { <p class="small">Als Nächstes auf dem Formular: <b>{{ s.unresolved()[0] }}</b></p> }
                <!-- UX-036: ohne unsichere Stellen kommt der Abschlusshinweis nie — am Ende der Partie geht es hier weiter. -->
                <div class="actions"><button type="button" class="btn-sec to-save" (click)="toSave()">Weiter zu den Namen</button></div>
              }
            </div>
          </section>

          <section class="panel scan-moves">
            <div class="moves">
              @for (row of s.rows(); track row.no) {
                <span class="no">{{ row.no }}.</span>
                <button type="button" class="ply" [ngClass]="s.plyClass(row.white)" (click)="s.go(row.white)">{{ de(s.plies()[row.white].san) }}</button>
                @if (row.black !== null) {
                  <button type="button" class="ply" [ngClass]="s.plyClass(row.black)" (click)="s.go(row.black)">{{ de(s.plies()[row.black].san) }}</button>
                } @else { <span></span> }
              }
              <span class="no"></span>
              <button type="button" class="ply end" [class.cursor]="s.cursor() === s.plies().length" (click)="s.go(s.legalCount())">Ende</button>
            </div>
            @if (s.unresolved().length) {
              <p class="err small">{{ s.unresolved().length }} Einträge ließen sich nicht zuordnen: {{ s.unresolved().join(' ') }}</p>
            }
            @if (s.illegalCount() > 0) {
              <p class="err small">{{ s.illegalCount() }} Züge am Ende sind nicht legal — sie fallen beim Übernehmen weg.</p>
            }
          </section>
        </div></div>

        @if (gameId != null) {
          <section class="panel save-panel" #savePanel>
            <h3 class="club-h3">Korrektur speichern</h3>
            <p>{{ gameTitle() }}</p>
            <p class="muted small">Die Züge werden in der Vereins-Datenbank ersetzt — und in jeder Kopie in „Meine Partien“. Die
              Analyse wird danach neu gerechnet. Namen und Ergebnis änderst du in der Liste unter „Bearbeiten“.</p>
            @if (saved()) {
              <p class="ok-text" role="status"><b>Korrektur gespeichert.</b> <a routerLink="/verein">Zu den Vereinspartien</a></p>
            } @else {
              <div class="actions">
                <button type="button" class="btn-pri" [disabled]="saving() || s.busy()" (click)="save()">
                  {{ saving() ? 'Speichere …' : 'Korrektur speichern' }}</button>
                <span class="update-msg" [class.err]="!!saveError()" role="status">{{ saveError() ?? '' }}</span>
              </div>
            }
          </section>
        } @else {
        <section class="panel save-panel" #savePanel>
          <h3 class="club-h3">Partie</h3>
          <div class="save-grid">
            @for (k of sides; track k) {
              <div class="field">{{ k === 'white' ? 'Weiß' : 'Schwarz' }}
                <lh-player-search [client]="client" [text]="name(k)()" [label]="k === 'white' ? 'Weiß' : 'Schwarz'" [searchMega]="false"
                                  (textChange)="setName(k, $event)" (picked)="pickPerson(k, $event)" />
                <span class="match" [class.ok]="(match(k)()?.league || match(k)()?.mega) && !replace(k)()">{{ matchText(k) }}</span>
                @if (similarOf(k).length) {
                  <span class="quick-pick small"><span class="muted">Meintest du</span>
                    @for (c of similarOf(k); track c.name + (c.fide ?? '')) {
                      <button type="button" class="btn-chip" (click)="pickPerson(k, c)" [attr.title]="c.teams.join(', ')"
                              [attr.aria-label]="(k === 'white' ? 'Weiß' : 'Schwarz') + ': ' + c.name + ' übernehmen'">{{ c.name }}</button>
                    }
                  </span>
                }
                <label class="replace-row" title="Nach außen steht dann „Schwaz“. Nur im Kurs „Taktiken aus Vereinspartien“ (sieht nur der Verein) steht der Name aus der öffentlichen Paarung der Ligarunde.">
                  <input type="checkbox" [checked]="replace(k)()" (change)="setReplace(k, $any($event.target).checked)" />
                  durch „{{ anon }}“ ersetzen</label>
              </div>
              <label class="field narrow">Elo<input type="text" inputmode="numeric" pattern="[0-9]*" maxlength="4" autocomplete="off" [value]="elo(k)() ?? ''"
                                               (input)="elo(k).set(num($any($event.target).value))" [disabled]="replace(k)()" /></label>
            }
            <label class="field narrow">Jahr<input type="number" inputmode="numeric" min="1900" [max]="maxYear" [value]="year() ?? ''"
                                              (input)="year.set(num($any($event.target).value)); yearRead.set(null); schedulePairings()" />
              @if (yearRead(); as r) { <span class="small muted year-hint">gelesen {{ r }} — auf heuer gesetzt, bitte prüfen</span> }
            </label>
            <label class="field narrow">Ergebnis
              <select (change)="result.set($any($event.target).value)">
                @for (r of results; track r) { <option [value]="r" [selected]="result() === r">{{ r === '*' ? 'unbekannt' : r }}</option> }
              </select>
            </label>
            <label class="field wide">Veranstaltung <span class="muted small">(fällt weg, sobald jemand „{{ anon }}“ heißt)</span>
              <input [value]="event()" (input)="event.set($any($event.target).value)" maxlength="200" [disabled]="anyReplaced()" />
            </label>
            @if (pairings().length) {
              <label class="field wide">Ligapartie <span class="muted small">(Brett einer Ligarunde — Spieler und Jahr kommen dann aus dem Spielplan)</span>
                <select class="pairing-pick" (change)="choosePairing($any($event.target).value)">
                  <option value="" [selected]="pairingId() == null">keine Ligapartie</option>
                  @for (p of pairings(); track p.id) {
                    <option [value]="p.id" [selected]="pairingId() === p.id">{{ pairingText(p) }}</option>
                  }
                </select>
              </label>
            }
          </div>

          <div class="field-row">
            <span class="muted small">Ich spiele</span>
            <div class="seg" role="group" aria-label="Ich spiele">
              <button type="button" [attr.aria-pressed]="ownerSide() === 'white'" (click)="setOwner('white')">Weiß</button>
              <button type="button" [attr.aria-pressed]="ownerSide() === 'black'" (click)="setOwner('black')">Schwarz</button>
            </div>
            <span class="muted small">Spieler von Schwaz und deine Seite werden standardmäßig durch „{{ anon }}“ ersetzt — dann wird weder
              gespeichert, wer dahinter steht, noch wer hochgeladen hat.</span>
          </div>

          <p class="preview"><span class="muted">Gespeichert wird:</span> <b>{{ preview() }}</b></p>
          @if (problem(); as pr) { <p class="err small">{{ pr }}</p> }

          @if (saved()) {
            <p class="ok-text" role="status"><b>In die Vereins-Datenbank übernommen.</b>@if (replacedOld()) { Die schon vorhandene Fassung dieser Partie ist archiviert und erscheint nicht mehr. } Foto und Lesung bleiben 365 Tage aufbewahrt — so kannst du die Züge später unter „Korrigieren“ noch nachbessern, und wir verbessern damit das Einlesen.
              <a [routerLink]="backLink" [queryParams]="{ art: 'formular' }">Zu deinen Formularen</a></p>
          } @else {
            @if (loggedIn && gameId == null) {
              <!-- 0.672.7, Wunsch 2026-10-05: standardmäßig an, ein Abwählen merkt sich das Gerät. -->
              <label class="auto-mine small"><input type="checkbox" [checked]="autoMine()" (change)="setAutoMine($any($event.target).checked)" />
                Automatisch zu meinen Partien hinzufügen</label>
            }
            <div class="actions">
              <button type="button" class="btn-pri" [disabled]="saving() || s.busy()" (click)="save()">
                {{ saving() ? 'Übernehme …' : 'In die Vereins-Datenbank übernehmen' }}</button>
              <button type="button" class="btn-link" [disabled]="saving()" (click)="discard()">Formular verwerfen</button>
              <span class="update-msg" [class.err]="!!saveError()" role="status">{{ saveError() ?? '' }}</span>
            </div>
          }
          <div class="actions pgn-actions">
            <span class="muted small">Die geprüfte Partie:</span>
            <button type="button" class="btn-sec" (click)="downloadPgn()">PGN herunterladen</button>
            <button type="button" class="btn-sec" (click)="copyPgn()">PGN kopieren</button>
            @if (loggedIn) {
              <button type="button" class="btn-sec" [disabled]="adding() || !!myGameId()" (click)="addToMyGames()">
                {{ adding() ? 'Speichere …' : 'Zu meinen Partien hinzufügen' }}</button>
            }
            @if (myGameId(); as id) {
              @if (rookHub) { <a class="small" [href]="rookHub + '/games/' + id" target="_blank" rel="noopener">In RookHub öffnen</a> }
            }
            <span class="update-msg" [class.err]="pgnMsg()?.err" role="status">{{ pgnMsg()?.text ?? '' }}</span>
          </div>
        </section>
        }

        <!-- Wunsch 2026-09-28: nach der letzten unsicheren Stelle darauf hinweisen und gleich Speichern anbieten. -->
        <dialog #doneDlg class="card done-dlg" aria-labelledby="done-title">
          <div class="card-body">
            <h3 id="done-title">Alle unsicheren Stellen geprüft</h3>
            @if (problem(); as pr) {
              <p class="err small">{{ pr }}</p>
              <div class="actions">
                <button type="button" class="btn-pri" (click)="closeDone(); toSave()">Zu den Namen</button>
                <button type="button" class="btn-link" (click)="closeDone()">Weiter bearbeiten</button>
              </div>
            } @else {
              <p>Gespeichert wird: <b>{{ gameId != null ? gameTitle() : preview() }}</b></p>
              <div class="actions">
                <button type="button" class="btn-pri" [disabled]="saving()" (click)="closeDone(); save()">{{ gameId != null ? 'Korrektur speichern' : 'In die Vereins-Datenbank übernehmen' }}</button>
                <button type="button" class="btn-link" (click)="closeDone()">Weiter bearbeiten</button>
              </div>
            }
          </div>
        </dialog>
      }
    } @else {
      <p class="muted">Lade …</p>
    }
  `,
})
export class ClubScanPageComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject(ElementRef);

  /** Token des Teilen-Links (ohne Anmeldung) — sonst `null`. */
  readonly share = this.route.snapshot.paramMap.get('token');
  private readonly clubApi = inject(ClubApiService);
  private readonly confirm = inject(ConfirmService);
  readonly client: ClubClient = this.clubApi.client(this.share);
  private readonly api = this.client;
  readonly allowed = !!this.share || this.auth.has('league.contribute');
  readonly canRead = this.auth.has('league.view');
  readonly backLink: unknown[] = this.share ? ['/s', this.share, 'hochladen'] : ['/verein/neu'];
  readonly results = ['1-0', '0-1', '1/2-1/2', '*'];
  readonly sides: Side[] = ['white', 'black'];
  readonly maxYear = new Date().getFullYear() + 1;
  readonly anon = ANON_NAME;
  readonly de = de;

  /** Nummer (angemeldet) bzw. geheimer Schlüssel (ohne Konto) der Einlesung. */
  scanRef = '';
  /** Korrektur einer schon übernommenen Vereinspartie (Route `verein/partie/:id/korrigieren`, 0.660.0) — sonst `null`. */
  readonly gameId: number | null = this.route.snapshot.data['game'] ? Number(this.route.snapshot.paramMap.get('id')) : null;
  /** „Weiß – Schwarz (Jahr) · Ergebnis" der Vereinspartie (Korrektur-Modus). */
  readonly gameTitle = signal('');
  readonly state = signal<LeagueScanState | null>(null);
  readonly perMove = SECONDS_PER_MOVE;
  /** Uhr seit dem Hochladen, solange gelesen wird (Wunsch 2026-09-28: „ein Timer, der raufzählt"). */
  private readonly ticker = new SecondsTicker();
  readonly clock = computed(() => formatClock(readingSeconds(this.state()?.scan.createdAt, this.ticker.now())));
  readonly notFound = signal(false);
  /** Der Abruf scheitert (anderer Code als 404, oder vorübergehend mehrmals hintereinander) — Klartext statt „Lade …". */
  readonly loadError = signal<string | null>(null);
  private failures = 0;
  /** Die Fotos des Formulars je Seite (Objekt-Adressen); ein Formular kann über bis zu drei Blätter gehen (0.690.1). */
  readonly photoUrls = signal<(string | null)[]>([]);
  readonly pageCount = signal(1);
  readonly pageNumbers = computed(() => Array.from({ length: this.pageCount() }, (_, i) => i + 1));
  /** Welche Seite im großen Foto steht — folgt dem gewählten Zug, lässt sich mit den Knöpfen wechseln. */
  readonly shownPage = signal(1);
  readonly photoUrl = computed(() => this.photoUrls()[this.shownPage() - 1] ?? this.photoUrls()[0] ?? null);
  private readonly followPage = effect(() => {
    const page = this.s.currentPage();
    if (page) untracked(() => this.shownPage.set(Math.min(page, this.pageCount())));
  });
  readonly zoom = signal(false);
  /** Am Handy ist das ganze Foto eingeklappt (UX-036), am PC steht es immer da. */
  readonly photoOpen = signal(false);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  /** Wie viele ältere Fassungen derselben Partie das Übernehmen archiviert hat (2026-10-06). */
  readonly replacedOld = signal(0);
  private readonly doneDlg = viewChild<ElementRef<HTMLDialogElement>>('doneDlg');
  private readonly savePanel = viewChild<ElementRef<HTMLElement>>('savePanel');
  /** Wie viele unsichere Stellen es vorher gab — geht die Zahl von >0 auf 0, kommt der Hinweis. */
  private lastUncertain = -1;
  private readonly doneWatch = effect(() => {
    const n = this.s.uncertainLeft();
    if (this.state()?.scan.status !== 'done' || this.s.busy()) return;
    untracked(() => {
      if (this.lastUncertain > 0 && n === 0 && !this.saved()) {
        const d = this.doneDlg()?.nativeElement;
        if (d && !d.open) d.showModal();
      }
      this.lastUncertain = n;
    });
  });
  /** Übernommen — die Seite bleibt stehen, damit PGN und „Meine Partien" noch gehen. */
  readonly saved = signal(false);
  readonly adding = signal(false);
  readonly myGameId = signal<number | null>(null);
  readonly pgnMsg = signal<{ text: string; err: boolean } | null>(null);
  get loggedIn(): boolean { return this.auth.isLoggedIn; }
  /** „Automatisch zu meinen Partien hinzufügen": an, solange auf diesem Gerät nicht abgewählt. */
  readonly autoMine = signal(readAutoMine());
  setAutoMine(on: boolean): void {
    this.autoMine.set(on);
    try { if (on) localStorage.removeItem(AUTO_MINE_KEY); else localStorage.setItem(AUTO_MINE_KEY, '0'); } catch { /* nur Bequemlichkeit */ }
  }
  readonly rookHub = rookHubUrlForLeagueHub();

  private readonly sideState = {
    white: { name: signal(''), fide: signal<string | null>(null), elo: signal<number | null>(null),
      match: signal<SideMatch | null>(null), replace: signal(false), touched: false },
    black: { name: signal(''), fide: signal<string | null>(null), elo: signal<number | null>(null),
      match: signal<SideMatch | null>(null), replace: signal(false), touched: false },
  };
  readonly name = (k: Side) => this.sideState[k].name;
  readonly elo = (k: Side) => this.sideState[k].elo;
  readonly match = (k: Side) => this.sideState[k].match;
  readonly replace = (k: Side) => this.sideState[k].replace;
  readonly year = signal<number | null>(null);
  /** Die Erkennung las ein älteres Jahr, vorbelegt ist heuer (0.655.0) — Hinweis unter dem Feld, bis man es ändert. */
  readonly yearRead = signal<number | null>(null);
  readonly result = signal('*');
  readonly event = signal('');
  readonly ownerSide = signal<Side | null>(null);
  readonly anyReplaced = computed(() => this.replace('white')() || this.replace('black')());
  /** Der Tag laut Formular — nur für die Erkennung der Ligapaarung (gespeichert wird nur das Jahr). */
  private sheetDate: string | null = null;
  /** Brettpaarungen, die diese Partie sein könnten (0.678.0); `pairingId` = die gewählte. */
  readonly pairings = signal<ClubPairing[]>([]);
  readonly pairingId = signal<number | null>(null);
  readonly pairingText = pairingText;
  /** Der Nutzer hat selbst gewählt — dann ändert ein neuer Vorschlag die Wahl nicht mehr. */
  private pairingTouched = false;
  /** Vorschläge wurden geholt — erst dann heißt „keine" auch keine (sonst entscheidet der Server). */
  private pairingsLoaded = false;
  private pairingTimer: ReturnType<typeof setTimeout> | null = null;
  private pairingSeq = 0;

  readonly s = new SheetEditSession({
    resolve: (prefix, writtenFrom) => this.gameId != null
      ? this.api.clubResolve(this.gameId, prefix, writtenFrom)
      : this.api.resolve(this.scanRef, prefix, writtenFrom),
    moved: () => this.revealCursor(),
    resolveFailed: () => this.saveError.set('Den Rest neu zu lesen hat nicht geklappt — der bisherige Stand bleibt.'),
    bind: o => o.pipe(takeUntilDestroyed(this.destroyRef)),
  });

  /** Wie die Partie in der Datenbank steht (Jahr · Weiß – Schwarz · Ergebnis). */
  readonly preview = computed(() => {
    const shown = (k: Side) => this.replace(k)() ? ANON_NAME : this.match(k)()?.name || this.name(k)().trim() || '?';
    return [this.year() ?? 'ohne Jahr', `${shown('white')} – ${shown('black')}`, this.result() === '*' ? 'Ergebnis offen' : this.result()]
      .join(' · ');
  });

  /** Was die Partie unübernehmbar macht — dieselbe Regel wie am Server (`LeagueClubService.Build`): bekannt ist, wer in
   * der Liga ODER im Megabase-Verzeichnis steht. */
  readonly problem = computed(() => {
    const w = this.match('white')(), b = this.match('black')();
    if (!w || !b) return null;
    const known = (m: SideMatch) => m.league || !!m.mega;
    if (!known(w) && !known(b))
      return 'Kein Spieler erkannt (weder in der Liga noch in der Megabase) — so wird die Partie nicht angenommen. Namen prüfen (Vorschläge beim Tippen).';
    if (!(known(w) && !this.replace('white')()) && !(known(b) && !this.replace('black')()))
      return 'Nach dem Ersetzen bleibt kein bekannter Gegner übrig — so wird die Partie nicht angenommen.';
    return null;
  });

  /** Wartendes Nachfragen — `null`, sobald der Takt abgelaufen ist (dann läuft der Abruf schon). */
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  /** Nur der jüngste `load()` zählt: „Neu laden" mitten in einem laufenden Abruf (der retryInterceptor hält einen 502
   * über 3 s offen) startete sonst eine zweite Nachfrage-Kette — doppelte Last, und nach dem Lesen liefen loadSheet,
   * Namen und Foto zweimal (Korrekturen überschrieben). Ein überholter Abruf tut nichts mehr. */
  private loadSeq = 0;
  private matchTimer: ReturnType<typeof setTimeout> | null = null;
  private matchSeq = 0;
  private destroyed = false;

  ngOnInit(): void {
    if (!this.allowed) return;
    this.scanRef = this.route.snapshot.paramMap.get('id') ?? this.route.snapshot.paramMap.get('key') ?? '';
    void (this.gameId != null ? this.loadGame() : this.load());
  }

  /** Zurück auf der Seite (Handy): ein wartendes Nachfragen gleich ausführen. */
  @HostListener('document:visibilitychange')
  onVisible(): void {
    if (document.visibilityState !== 'visible' || !this.pollTimer || this.state()?.scan.status === 'done') return;
    clearTimeout(this.pollTimer);
    this.pollTimer = null;
    void this.load();
  }

  ngOnDestroy(): void {
    this.ticker.stop();
    this.destroyed = true;
    for (const t of [this.pollTimer, this.matchTimer, this.pairingTimer]) if (t) clearTimeout(t);
    for (const url of this.photoUrls()) if (url) URL.revokeObjectURL(url);
  }

  pageUrl(page: number): string | null {
    return this.photoUrls()[page - 1] ?? null;
  }

  onPhotoLoad(e: Event): void {
    const img = e.target as HTMLImageElement;
    if (this.shownPage() === 1) this.s.onPhotoLoad(e);
    else this.s.setPageSize(this.shownPage(), img.naturalWidth, img.naturalHeight);
  }

  /** Alle Seiten holen; die Maße jeder Seite braucht der Ausschnitt, auch wenn sie gerade nicht groß angezeigt wird. */
  private async loadPhotos(count: number, fetch: (page: number) => Promise<Blob>, still: () => boolean): Promise<void> {
    this.pageCount.set(Math.max(1, count));
    for (let page = 1; page <= Math.max(1, count); page++) {
      try {
        const blob = await fetch(page);
        if (!still()) return;
        const url = URL.createObjectURL(blob);
        this.photoUrls.update(list => { const next = [...list]; next[page - 1] = url; return next; });
        if (page > 1) {
          const img = new Image();
          img.onload = () => this.s.setPageSize(page, img.naturalWidth, img.naturalHeight);
          img.src = url;
        }
      } catch { /* ohne Foto geht die Korrektur trotzdem */ }
    }
  }

  /** „Neu laden" auf der Fehlerkarte: gleich nachfragen, ein wartendes Nachfragen entfällt, ein laufender Abruf ist überholt. */
  reload(): void {
    if (this.pollTimer) clearTimeout(this.pollTimer);
    this.pollTimer = null;
    this.failures = 0;
    this.loadError.set(null);
    void this.load();
  }

  private poll(): void {
    this.pollTimer = setTimeout(() => { this.pollTimer = null; void this.load(); }, POLL_MS);
  }

  private async load(): Promise<void> {
    const my = ++this.loadSeq;
    let st: LeagueScanState;
    try {
      st = await this.api.scan(this.scanRef);
    } catch (err) {
      if (this.destroyed || my !== this.loadSeq) return;
      if (err instanceof HttpErrorResponse && err.status === 404) { this.notFound.set(true); return; }
      // Nur was von selbst vorbeigeht (0/502/503/504), wird im Hintergrund weiter nachgefragt — nach ein paar stillen
      // Fehlversuchen mit Hinweis. Jeder andere Code (500, 403, …) sagt es gleich und wartet auf „Neu laden".
      const transient = isTransientError(err);
      if (!transient || ++this.failures >= SILENT_FAILURES)
        this.loadError.set(loadErrorText(err) + (transient ? ' LeagueHub versucht es im Hintergrund weiter.' : ''));
      if (transient) this.poll();
      return;
    }
    if (this.destroyed || my !== this.loadSeq) return;
    this.failures = 0;
    this.loadError.set(null);
    this.state.set(st);
    const reading = st.scan.status === 'pending' || st.scan.status === 'running';
    this.ticker.run(reading);
    if (reading) {
      this.poll();
      return;
    }
    if (st.scan.status !== 'done') return;
    this.s.loadSheet({ plies: st.plies, unresolved: st.unresolved, unresolvedFrom: st.unresolvedFrom, boxes: st.boxes, written: st.written,
      pages: st.pages ?? [] });
    this.name('white').set(st.white ?? '');
    this.name('black').set(st.black ?? '');
    const preset = presetYear(st.date);
    this.sheetDate = st.date;
    this.year.set(preset.year);
    this.yearRead.set(preset.read);
    this.result.set(normalizeResult(st.result));
    this.event.set(st.event ?? '');
    this.ownerSide.set(st.ownerSide);
    void this.runMatch();
    await this.loadPhotos(st.pageCount ?? 1, page => this.api.photo(this.scanRef, page), () => !this.destroyed && my === this.loadSeq);
  }

  /**
   * Korrektur-Modus (0.660.0): die Vereinspartie laden und — solange aufbewahrt — ihr Formular mit Foto und Lesung, wie beim
   * ersten Prüfen. Der gespeicherte Stand je Halbzug gilt nur, wenn er zu den Zügen der Partie passt; sonst die Züge der
   * Partie mit den Formular-Einträgen daneben. Ohne Formular: nur Brett und Zugliste.
   */
  private async loadGame(): Promise<void> {
    const id = this.gameId!;
    let game: ClubGameDetail;
    try {
      game = await this.api.game(id);
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.status === 404) this.notFound.set(true);
      else this.loadError.set(loadErrorText(err));
      return;
    }
    if (!game.canDelete) { this.notFound.set(true); return; }
    this.gameTitle.set(`${game.white} – ${game.black}${game.year ? ` (${game.year})` : ''} · ${game.result === '*' ? 'Ergebnis offen' : game.result}`);
    const fromPgn = pliesOfPgn(game.pgn);
    let sheet: ClubSheetState | null = null;
    try { sheet = await this.api.clubSheet(id); } catch { /* nichts aufbewahrt — Korrektur am Brett */ }
    const matches = !!sheet && sheet.plies.length === fromPgn.length && sheet.plies.every((p, i) => p.san === fromPgn[i].san);
    if (sheet && matches) {
      this.s.loadSheet(sheet);
    } else {
      this.s.loadSheet({ plies: toServer(fromPgn), boxes: sheet?.boxes ?? [], written: sheet?.written ?? [], pages: sheet?.pages ?? [] });
    }
    this.state.set({ scan: { id, status: 'done' } } as unknown as LeagueScanState);
    if (sheet) await this.loadPhotos(sheet.pageCount ?? 1, page => this.api.clubSheetPhoto(id, page), () => !this.destroyed);
  }

  /** Vorgabe „ersetzen": Spieler von Schwaz und die eigene Seite — bis der Nutzer das Häkchen selbst anfasst. */
  private applyDefault(k: Side): void {
    const st = this.sideState[k];
    if (st.touched) return;
    st.replace.set(!!st.match()?.club || this.ownerSide() === k);
  }

  setOwner(k: Side): void {
    this.ownerSide.set(k);
    for (const x of this.sides) this.applyDefault(x);
  }

  setReplace(k: Side, on: boolean): void {
    this.sideState[k].touched = true;
    this.sideState[k].replace.set(on);
  }

  setName(k: Side, value: string): void {
    const st = this.sideState[k];
    st.name.set(value);
    st.fide.set(null);
    if (this.matchTimer) clearTimeout(this.matchTimer);
    this.matchTimer = setTimeout(() => void this.runMatch(), MATCH_DEBOUNCE_MS);
  }

  /** Ein Treffer der Suche (Liga oder Megabase) bringt Namen und FIDE-ID mit — so ist der Spieler eindeutig. */
  pickPerson(k: Side, p: RosterPerson): void {
    const st = this.sideState[k];
    if (this.matchTimer) clearTimeout(this.matchTimer);
    st.name.set(p.name);
    st.fide.set(p.fide);
    // Die Elo des gewählten Spielers gleich mit (Wunsch 2026-10-05) — aus der jüngsten Meldeliste, sonst bleibt sie.
    if (p.elo) st.elo.set(p.elo);
    const league = p.league ?? true;
    st.match.set({ league, ambiguous: false, name: p.name, fide: p.fide, club: p.club, candidates: [], mega: !league });
    this.applyDefault(k);
    this.schedulePairings();
  }

  /** Vorschläge für die Ligapaarung neu holen (gedrosselt) — nach jedem Abgleich der Namen und jedem Jahr. */
  schedulePairings(): void {
    if (this.gameId != null) return;
    if (this.pairingTimer) clearTimeout(this.pairingTimer);
    this.pairingTimer = setTimeout(() => void this.loadPairings(), MATCH_DEBOUNCE_MS);
  }

  private async loadPairings(): Promise<void> {
    const my = ++this.pairingSeq;
    const fide = (k: Side) => this.sideState[k].fide() ?? (this.match(k)()?.ambiguous ? null : this.match(k)()?.fide ?? null);
    try {
      const list = await this.api.pairings({
        white: this.name('white')().trim() || null, whiteFide: fide('white'),
        black: this.name('black')().trim() || null, blackFide: fide('black'),
        date: this.sheetDate, year: this.year(),
      });
      if (my !== this.pairingSeq || this.destroyed) return;
      // Eine schon gewählte Paarung bleibt in der Auswahl, auch wenn sie nach einer Namensänderung nicht mehr vorkäme.
      const chosen = this.pairings().find(p => p.id === this.pairingId());
      this.pairings.set(chosen && !list.some(p => p.id === chosen.id) ? [chosen, ...list] : list);
      this.pairingsLoaded = true;
      if (!this.pairingTouched) {
        const exact = list.filter(p => p.exact);
        this.pairingId.set(exact.length === 1 ? exact[0].id : null);
      }
    } catch { /* ohne Vorschläge entscheidet der Server beim Übernehmen selbst */ }
  }

  /** Eine Paarung gewählt: die Spieler kommen aus dem Spielplan (eine Seite von Schwaz wird wie sonst ersetzt). */
  choosePairing(value: string): void {
    this.pairingTouched = true;
    const p = value ? this.pairings().find(x => x.id === Number(value)) : undefined;
    this.pairingId.set(p?.id ?? null);
    if (!p) return;
    const person = (name: string, fide: string | null, club: boolean): RosterPerson => ({ name, fide, teams: [], club, league: true });
    this.pickPerson('white', person(p.white, p.whiteFide, p.whiteOwnClub));
    this.pickPerson('black', person(p.black, p.blackFide, p.blackOwnClub));
    const year = Number(p.label.match(/\((\d\d)\.(\d\d)\.(\d{4})\)$/)?.[3]);
    if (year) { this.year.set(year); this.yearRead.set(null); }
  }

  private async runMatch(): Promise<void> {
    const my = ++this.matchSeq;
    try {
      const m = await this.api.match(this.name('white')(), this.name('black')());
      if (my !== this.matchSeq) return;
      for (const k of this.sides) {
        if (this.sideState[k].fide()) continue;             // aus der Meldeliste gewählt: bleibt
        this.sideState[k].match.set(m[k]);
        this.applyDefault(k);
      }
      this.schedulePairings();
    } catch { /* die Prüfung macht der Server beim Übernehmen ohnehin */ }
  }

  /** Nicht erkannt: ähnlich geschriebene Ligaspieler zum Anklicken (0.596.0, derselbe Abgleich wie in der PGN-Übersicht). */
  similarOf(k: Side): RosterPerson[] {
    const m = this.match(k)();
    return m && !this.replace(k)() && !m.league && !m.mega && !m.ambiguous ? m.similar ?? [] : [];
  }

  matchText(k: Side): string {
    const m = this.match(k)();
    if (this.replace(k)()) return m?.club ? `Spieler von Schwaz — wird „${ANON_NAME}“` : `wird „${ANON_NAME}“`;
    if (!m) return '';
    if (m.ambiguous) return 'Ligaspieler (mehrere dieses Namens — bitte aus den Vorschlägen wählen)';
    if (m.league && m.lastNameOnly) return `nur über den Nachnamen: ${m.name} — bitte prüfen`;
    const kept = m.alias ? ' (gemerkt)' : '';
    if (m.league) return `Ligaspieler: ${m.name}${m.fide ? '' : ' (ohne FIDE-ID)'}${m.club ? ' — Schwaz, nicht ersetzt' : ''}${kept}`;
    if (m.mega) return `nicht in Liga — aus der Megabase: ${m.name}${m.fide ? ` (FIDE ${m.fide})` : ''}${kept}`;
    return 'nicht erkannt';
  }

  num(v: string): number | null {
    const n = Number(v);
    return v.trim() && Number.isFinite(n) ? Math.round(n) : null;
  }

  onBoardMove(m: UserBoardMove): void {
    this.s.play(m.san);
  }

  @HostListener('document:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (!isBoardHotkey(e)) return;
    if (e.key === 'ArrowLeft') { this.s.go(this.s.cursor() - 1); e.preventDefault(); }
    if (e.key === 'ArrowRight') { this.s.go(this.s.cursor() + 1); e.preventDefault(); }
  }

  /** Den Zug in der Zugliste sichtbar machen — NUR in deren eigenem Rollbereich. `scrollIntoView` rollte auch die Seite,
   * und am Handy verschwand damit nach jedem „Stimmt so" der Prüfteil oben (Ausschnitt, Lesarten, Knopf). */
  private revealCursor(): void {
    setTimeout(() => {
      const host = this.host.nativeElement as HTMLElement;
      const el = host.querySelector<HTMLElement>('.moves .ply.cursor');
      if (el) scrollIntoContainer(el);
      const mark = host.querySelector<HTMLElement>('.photo-mark');       // vergrößertes Foto: die Zeile ins Bild
      if (mark) scrollIntoContainer(mark);
    });
  }

  async save(): Promise<void> {
    const legal = this.s.plies().filter(p => !p.illegal);
    if (this.s.illegalCount() > 0
      && !(await firstValueFrom(this.confirm.ask(`${this.s.illegalCount()} Züge am Ende sind nicht legal und fallen weg. Trotzdem übernehmen?`)))) return;
    this.saving.set(true);
    this.saveError.set(null);
    if (this.gameId != null) {
      try {
        await this.api.correctMoves(this.gameId, legal.map(p => p.san), toServer(legal));
        this.saved.set(true);
      } catch (err) {
        const e = err instanceof HttpErrorResponse ? err : null;
        this.saveError.set(e?.error?.reason ? reasonText(e.error.reason) : 'Speichern hat nicht geklappt.');
      } finally {
        this.saving.set(false);
      }
      return;
    }
    try {
      const added = await this.api.addGame({
        moves: legal.map(p => p.san),
        white: this.name('white')().trim() || null,
        black: this.name('black')().trim() || null,
        whiteFide: this.sideState.white.fide(),
        blackFide: this.sideState.black.fide(),
        whiteElo: this.elo('white')(),
        blackElo: this.elo('black')(),
        whiteReplace: this.replace('white')(),
        blackReplace: this.replace('black')(),
        result: this.result(),
        event: this.event().trim() || null,
        year: this.year(),
        scanId: null,
        // Vorschläge geholt: die Wahl gilt (0 = keine); sonst entscheidet der Server über den eindeutigen Treffer.
        leagueGameId: this.pairingsLoaded ? this.pairingId() ?? 0 : null,
        date: this.sheetDate,
      }, this.scanRef);
      if (this.share) rememberAnonKey(this.share, this.scanRef, false);
      this.replacedOld.set(added?.replaced ?? 0);
      this.saved.set(true);
      if (this.loggedIn && this.autoMine() && !this.myGameId()) void this.addToMyGames();
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.saveError.set(e?.error?.reason === 'illegal' ? `Ein Zug ist nicht legal: ${e.error.message}`
        : e?.error?.reason ? reasonText(e.error.reason) : 'Übernehmen hat nicht geklappt.');
    } finally {
      this.saving.set(false);
    }
  }

  closeDone(): void {
    this.doneDlg()?.nativeElement.close();
  }

  toSave(): void {
    this.savePanel()?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  /** Die geprüfte Partie: legale Züge, Namen wie im Formular. */
  private pgnInput(): SheetPgnInput {
    return {
      moves: this.s.plies().filter(p => !p.illegal).map(p => p.san),
      white: this.name('white')().trim() || null, black: this.name('black')().trim() || null,
      result: this.result(), event: this.event().trim() || null, year: this.year(),
    };
  }

  downloadPgn(): void {
    const g = this.pgnInput();
    downloadBlob(new Blob([sheetPgn(g)], { type: 'application/x-chess-pgn' }), sheetPgnFileName(g));
  }

  async copyPgn(): Promise<void> {
    try {
      await navigator.clipboard.writeText(sheetPgn(this.pgnInput()));
      this.pgnMsg.set({ text: 'PGN kopiert.', err: false });
    } catch {
      this.pgnMsg.set({ text: 'Kopieren ging nicht — bitte „PGN herunterladen".', err: true });
    }
  }

  /** In RookHubs „Meine Partien" (nur angemeldet) — mit den Namen wie im Formular, ohne „Schwaz". */
  async addToMyGames(): Promise<void> {
    this.adding.set(true);
    this.pgnMsg.set(null);
    try {
      const r = await this.clubApi.addToMyGames(sheetPgn(this.pgnInput()));
      this.myGameId.set(r.ids[0] ?? null);
      this.pgnMsg.set({ text: r.imported ? 'In deinen Partien gespeichert.' : 'Die Partie war schon in deinen Partien.', err: false });
    } catch {
      this.pgnMsg.set({ text: 'Speichern in deinen Partien hat nicht geklappt.', err: true });
    } finally {
      this.adding.set(false);
    }
  }

  async discard(): Promise<void> {
    if (!(await firstValueFrom(this.confirm.ask('Formular verwerfen? Foto und Lesung werden gelöscht.')))) return;
    try {
      await this.api.discard(this.scanRef);
      if (this.share) rememberAnonKey(this.share, this.scanRef, false);
      void this.router.navigate(this.backLink, { queryParams: { art: 'formular' } });
    } catch {
      this.saveError.set('Verwerfen hat nicht geklappt.');
    }
  }
}
