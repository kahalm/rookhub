import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, OnDestroy, OnInit, computed, inject, signal, effect, untracked, viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgClass } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { BoardArrow, ChessBoardComponent, UserBoardMove } from '@rh/shared/pgn-viewer/chess-board.component';
import { scrollIntoContainer } from '@rh/shared/pgn-viewer/move-list.component';
import { isBoardHotkey } from '@rh/shared/keyboard.util';
import { ScanEngineComponent } from './scan-engine.component';
import { SheetEditSession } from '@rh/features/games/sheet-edit-session';
import { SECONDS_PER_MOVE, SecondsTicker, formatClock, readingSeconds } from '@rh/features/games/scoresheet-timing';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { ClubGameDetail, ClubSheetState, LeagueScanState, ScanRef } from '../../core/club.models';
import { alignSheetPlies, pliesOfPgn, toServer } from '@rh/features/games/game-edit.util';
import { ClubContextService } from '../../core/club-context.service';
import { SheetPgnInput, isTransientError, loadErrorText, reasonText, sheetPgn, sheetPgnFileName } from '../../core/club-format';
import { rookHubUrlForLeagueHub } from '@rh/core/partner-site';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { firstValueFrom } from 'rxjs';
import { downloadBlob } from '@rh/shared/download.util';
import { rememberAnonKey } from './club-add-page.component';
import { PlayerSearchComponent } from './player-search.component';
import { ScanHeaderForm, SIDES } from './scan-header-form';
import { ScanPhotos } from './scan-photos';
import { AccessGateComponent } from '../../shared/access-gate.component';
import { de } from '../../core/league-format';

const POLL_MS = 3000;
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
  imports: [RouterLink, NgClass, ChessBoardComponent, PlayerSearchComponent, AccessGateComponent, ScanEngineComponent],
  template: `
    @if (!allowed) {
      <!-- UX-033: jetzt mit „Angemeldet als" (falsches Konto?) und dem nächsten Schritt. -->
      @if (canRead) {
        <lh-access-gate text="Du kannst die Vereinspartien lesen. Zum Einlesen von Partieformularen fehlt dir noch die Freigabe — frag einen Verwalter."
                        purpose="das Einlesen von Partieformularen" [back]="{ link: '/verein', label: 'Zu den Vereinspartien' }" />
      } @else {
        <lh-access-gate text="Partieformulare einlesen dürfen Admins und die Vereinsgruppen der teilnehmenden Vereine." />
      }
    } @else if (notFound() && gameId != null) {
      <section class="gate"><h2>Partie nicht gefunden</h2>
        <p>Sie ist gelöscht oder du darfst sie nicht korrigieren (das dürfen, wer sie hochgeladen hat, und die Verwalter).</p>
        <div class="actions"><a class="btn-sec" routerLink="/verein">← Zu den Vereinspartien</a></div></section>
    } @else if (notFound()) {
      <section class="gate"><h2>Formular nicht gefunden</h2>
        <p>Es wurde schon übernommen oder verworfen.</p>
        <div class="actions"><a class="btn-sec" [routerLink]="backLink" [queryParams]="{ art: 'formular' }">← Zu deinen Formularen</a></div></section>
    } @else if (loadError(); as e) {
      <!-- UX-034: früher stand hier minutenlang „Lade …", während die Seite still alle 3 s nachfragte. -->
      <section class="gate" role="alert"><h2>Das Formular lässt sich gerade nicht laden</h2>
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
          <p class="muted">{{ gameTitle() }}@if (!ph.photoUrl()) { — das Formular ist nicht mehr aufbewahrt, korrigiert wird am Brett. }</p>
        } @else {
          <p><a class="back-link" [routerLink]="backLink" [queryParams]="{ art: 'formular' }">← Deine Formulare</a></p>
          <h2>Partieformular prüfen</h2>
        }
        @if (st.scan.status !== 'done') {
          @if (st.scan.status === 'failed') { <p class="muted" role="status">Das Formular ließ sich nicht lesen.</p> }
          @else {
            @if (st.scan.status === 'pending') {
              <p class="muted" role="status">Das Formular wartet aufs Einlesen … <b class="scan-clock">{{ clock() }}</b>
                <span class="small"> — der Leser schaut alle 5 Minuten nach neuen Formularen.</span></p>
            } @else {
              <p class="muted" role="status">Das Formular wird gelesen … <b class="scan-clock">{{ clock() }}</b>
                <span class="small"> — das dauert meist ein paar Minuten.</span></p>
            }
          }
        } @else {
          <p class="muted">Orange markiert sind unsichere Stellen: dort die richtige Lesart wählen oder den Zug am Brett spielen
            — danach wird der Rest neu gelesen. Pfeiltasten blättern.</p>
          <!-- UX-036: Namen und „Übernehmen" stehen unter Zugliste (und Foto) — am Handy ≈ 2 700 px tiefer, ohne Hinweis. -->
          <p class="small"><button type="button" class="btn-link to-save" (click)="toSave()">{{ gameId != null ? 'Weiter zum Speichern ↓' : 'Weiter zu Namen & Übernehmen ↓' }}</button></p>
        }
      </section>

      @if (st.scan.status === 'done') {
        <div class="scan-wrap"><div class="scan-layout" [class.with-photo]="!!ph.photoUrl()">
          @if (ph.photoUrl(); as src) {
            <section class="panel scan-photo" [class.open]="photoOpen()">
              <p class="photo-toggle"><button type="button" class="btn-link" [attr.aria-expanded]="photoOpen()" (click)="photoOpen.set(!photoOpen())">
                {{ photoOpen() ? 'Ganzes Foto ausblenden' : 'Ganzes Foto zeigen' }}</button></p>
              @if (ph.pageCount() > 1) {
                <div class="seg pager" role="group" aria-label="Seite des Formulars">
                  @for (n of ph.pageNumbers(); track n) {
                    <button type="button" [attr.aria-pressed]="ph.shownPage() === n" (click)="ph.showPage(n)">Seite {{ n }}</button>
                  }
                </div>
              }
              @if (ph.viewNumbers().length > 1) {
                <!-- 0.736.0: weitere Fotos derselben Seite — Ausschnitt und Markierung gehören zum ersten -->
                <div class="seg pager views" role="group" aria-label="Foto dieser Seite">
                  @for (v of ph.viewNumbers(); track v) {
                    <button type="button" [attr.aria-pressed]="ph.shownView() === v" (click)="ph.showView(v)">Foto {{ v + 1 }}</button>
                  }
                </div>
              }
              <div class="photo-scroll" [class.zoom]="zoom()">
                <div class="photo-frame">
                  <img [src]="src" alt="Foto des Partieformulars" (load)="onPhotoLoad($event)" />
                  @if (s.mark(); as m) { @if (m.page === ph.shownPage() && ph.shownView() === 0) {
                    <div class="photo-mark" [class.uncertain]="m.uncertain" [style.left.%]="m.left" [style.top.%]="m.top"
                         [style.width.%]="m.width" [style.height.%]="m.height" aria-hidden="true"></div>
                  } }
                </div>
              </div>
              <p class="photo-zoom"><button type="button" class="btn-link" (click)="zoom.set(!zoom())">{{ zoom() ? 'Kleiner' : 'Größer' }}</button></p>
            </section>
          }

          <section class="panel scan-board">
            <app-chess-board [fen]="s.cursorFen()" [lastMove]="s.lastMove()" [arrows]="boardArrows()" [flipped]="f.ownerSide() === 'black'"
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
            <lh-scan-engine [fen]="s.cursorFen()" (arrowsChange)="engineArrows.set($event)" />
          </section>

          <!-- Am Handy steht dieser Teil OBEN (Wunsch 2026-09-28): die Zeile des Formulars, die Lesarten und „Stimmt so"
               auf einem Bildschirm. -->
          <section class="panel scan-check">
            <div class="cursor-panel" aria-live="polite">
              @if (ph.photoUrl(); as src) {
                @if (s.crop(); as c) {
                  <div class="crop">
                    <div class="crop-frame" [class.uncertain]="c.uncertain" [style.aspect-ratio]="c.view.aspect">
                      <img [src]="ph.pageUrl(c.page) ?? src" alt="Ausschnitt des Formulars" [style.width.%]="c.view.imgW" [style.height.%]="c.view.imgH"
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
                <lh-player-search [client]="client" [text]="f.name(k)()" [label]="k === 'white' ? 'Weiß' : 'Schwarz'" [searchMega]="false"
                                  (textChange)="f.setName(k, $event)" (picked)="f.pickPerson(k, $event)" />
                <span class="match" [class.ok]="(f.match(k)()?.league || f.match(k)()?.mega) && !f.replace(k)()">{{ f.matchText(k) }}</span>
                @if (f.similarOf(k).length) {
                  <span class="quick-pick small"><span class="muted">Meintest du</span>
                    @for (c of f.similarOf(k); track c.name + (c.fide ?? '')) {
                      <button type="button" class="btn-chip" (click)="f.pickPerson(k, c)" [attr.title]="c.teams.join(', ')"
                              [attr.aria-label]="(k === 'white' ? 'Weiß' : 'Schwarz') + ': ' + c.name + ' übernehmen'">{{ c.name }}</button>
                    }
                  </span>
                }
                <label class="replace-row" [attr.title]="'Nach außen steht dann „' + anon + '“. Nur im Kurs „Taktiken aus Vereinspartien“ (sieht nur der Verein) steht der Name aus der öffentlichen Paarung der Ligarunde.'">
                  <input type="checkbox" [checked]="f.replace(k)()" (change)="f.setReplace(k, $any($event.target).checked)" />
                  durch „{{ anon }}“ ersetzen</label>
              </div>
              <label class="field narrow">Elo<input type="text" inputmode="numeric" pattern="[0-9]*" maxlength="4" autocomplete="off" [value]="f.elo(k)() ?? ''"
                                               (input)="f.elo(k).set(num($any($event.target).value))" [disabled]="f.replace(k)()" /></label>
            }
            <label class="field narrow">Jahr<input type="number" inputmode="numeric" min="1900" [max]="maxYear" [value]="f.year() ?? ''"
                                              (input)="f.year.set(num($any($event.target).value)); f.yearRead.set(null); f.schedulePairings()" />
              @if (f.yearRead(); as r) { <span class="small muted year-hint">gelesen {{ r }} — auf heuer gesetzt, bitte prüfen</span> }
            </label>
            <label class="field narrow">Ergebnis
              <select (change)="f.result.set($any($event.target).value)">
                @for (r of results; track r) { <option [value]="r" [selected]="f.result() === r">{{ r === '*' ? 'unbekannt' : r }}</option> }
              </select>
            </label>
            <label class="field wide">Veranstaltung <span class="muted small">(fällt weg, sobald jemand „{{ anon }}“ heißt)</span>
              <input [value]="f.event()" (input)="f.event.set($any($event.target).value)" maxlength="200" [disabled]="f.anyReplaced()" />
            </label>
            @if (f.pairings().length) {
              <label class="field wide">Ligapartie <span class="muted small">(Brett einer Ligarunde — Spieler und Jahr kommen dann aus dem Spielplan)</span>
                <select class="pairing-pick" (change)="f.choosePairing($any($event.target).value)">
                  <option value="" [selected]="f.pairingId() == null">keine Ligapartie</option>
                  @for (p of f.pairings(); track p.id) {
                    <option [value]="p.id" [selected]="f.pairingId() === p.id">{{ f.pairingText(p) }}</option>
                  }
                </select>
              </label>
            }
          </div>

          <div class="field-row">
            <span class="muted small">Ich spiele</span>
            <div class="seg" role="group" aria-label="Ich spiele">
              <button type="button" [attr.aria-pressed]="f.ownerSide() === 'white'" (click)="f.setOwner('white')">Weiß</button>
              <button type="button" [attr.aria-pressed]="f.ownerSide() === 'black'" (click)="f.setOwner('black')">Schwarz</button>
            </div>
            <span class="muted small">Spieler von {{ clubName() }} und deine Seite werden standardmäßig durch „{{ anon }}“ ersetzt — dann wird weder
              gespeichert, wer dahinter steht, noch wer hochgeladen hat.</span>
          </div>

          <p class="preview"><span class="muted">Gespeichert wird:</span> <b>{{ f.preview() }}</b></p>
          @if (f.problem(); as pr) { <p class="err small">{{ pr }}</p> }

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
            @if (canMerge) {
              <!-- 0.736.0, Wunsch 2026-10-10: zwei Fotos derselben Seite getrennt eingelesen → zu einer Einlesung vereinen -->
              <div class="merge small">
                @if (mergeCandidates(); as list) {
                  @if (list.length === 0) {
                    <span class="muted">Keine andere offene Einlesung zum Zusammenführen.</span>
                  } @else {
                    <span class="muted">Welche Einlesung zeigt dieselbe Partie? Ihre Fotos kommen hierher, die Partie wird neu gelesen.</span>
                    <ul class="merge-list">
                      @for (c of list; track c.ref) {
                        <li><button type="button" class="btn-sec" [disabled]="merging()" (click)="merge(c)">{{ candidateLabel(c) }}</button></li>
                      }
                    </ul>
                  }
                  <button type="button" class="btn-link" (click)="mergeCandidates.set(null)">Abbrechen</button>
                } @else {
                  <button type="button" class="btn-link merge-open" (click)="openMerge()">Mit anderem Foto derselben Seite zusammenführen …</button>
                }
                @if (mergeError(); as e) { <span class="err" role="status">{{ e }}</span> }
              </div>
            }
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
            @if (f.problem(); as pr) {
              <p class="err small">{{ pr }}</p>
              <div class="actions">
                <button type="button" class="btn-pri" (click)="closeDone(); toSave()">Zu den Namen</button>
                <button type="button" class="btn-link" (click)="closeDone()">Weiter bearbeiten</button>
              </div>
            } @else {
              <p>Gespeichert wird: <b>{{ gameId != null ? gameTitle() : f.preview() }}</b></p>
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
  readonly sides = SIDES;
  readonly maxYear = new Date().getFullYear() + 1;
  private readonly clubCtx = inject(ClubContextService);
  /** Name anonymisierter Spieler und des Vereins — der angemeldete bzw. der des Teilen-Links (0.698.0). */
  get anon(): string { return this.clubCtx.anonName(); }
  readonly clubName = this.clubCtx.clubName;
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
  /** Die Fotos des Formulars (Seiten, weitere Fotos je Seite). */
  readonly ph = new ScanPhotos({
    fetchView: (page, view) => this.api.photo(this.scanRef, page, view),
    setPageSize: (page, w, h) => this.s.setPageSize(page, w, h),
  });
  /** Zusammenführen (nur angemeldet, beim Prüfen einer Einlesung): `null` = Auswahl zu. */
  readonly mergeCandidates = signal<ScanRef[] | null>(null);
  readonly merging = signal(false);
  readonly mergeError = signal<string | null>(null);
  private readonly followPage = effect(() => {
    const page = this.s.currentPage();
    if (page) untracked(() => { const p = Math.min(page, this.ph.pageCount()); if (p !== this.ph.shownPage()) this.ph.showPage(p); });
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

  /** Namen, Elo, Jahr, Ergebnis, Veranstaltung und Ligapaarung vor dem Übernehmen. */
  readonly f = new ScanHeaderForm({
    api: this.api,
    anon: () => this.anon,
    clubName: () => this.clubName(),
    pairingsEnabled: this.gameId == null,
  });

  /** Bester Zug der zugeschalteten Engine (blau) — zum gelben Pfeil des bisherigen Zugs dazu (0.722.0). */
  readonly engineArrows = signal<BoardArrow[]>([]);
  readonly boardArrows = computed<BoardArrow[]>(() => [...this.s.arrows(), ...this.engineArrows()]);
  readonly s = new SheetEditSession({
    resolve: (prefix, writtenFrom) => this.gameId != null
      ? this.api.clubResolve(this.gameId, prefix, writtenFrom)
      : this.api.resolve(this.scanRef, prefix, writtenFrom),
    moved: () => this.revealCursor(),
    resolveFailed: () => this.saveError.set('Den Rest neu zu lesen hat nicht geklappt — der bisherige Stand bleibt.'),
    bind: o => o.pipe(takeUntilDestroyed(this.destroyRef)),
  });

  /** Wartendes Nachfragen — `null`, sobald der Takt abgelaufen ist (dann läuft der Abruf schon). */
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  /** Nur der jüngste `load()` zählt: „Neu laden" mitten in einem laufenden Abruf (der retryInterceptor hält einen 502
   * über 3 s offen) startete sonst eine zweite Nachfrage-Kette — doppelte Last, und nach dem Lesen liefen loadSheet,
   * Namen und Foto zweimal (Korrekturen überschrieben). Ein überholter Abruf tut nichts mehr. */
  private loadSeq = 0;
  private destroyed = false;

  ngOnInit(): void {
    if (!this.allowed) return;
    if (this.share) this.clubCtx.useShare(this.share);   // ohne Konto: der Verein des Links (0.698.0)
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
    if (this.pollTimer) clearTimeout(this.pollTimer);
    this.f.dispose();
    this.ph.dispose();
  }

  get canMerge(): boolean { return !this.share && this.loggedIn && this.gameId == null; }

  async openMerge(): Promise<void> {
    this.mergeError.set(null);
    try {
      const list = await this.api.scans();
      this.mergeCandidates.set(list.filter(x => x.ref !== this.scanRef && x.scan.status !== 'running'));
    } catch {
      this.mergeError.set('Die Einlesungen ließen sich gerade nicht laden.');
    }
  }

  candidateLabel(c: ScanRef): string {
    const names = c.scan.white || c.scan.black ? `${c.scan.white ?? '?'} – ${c.scan.black ?? '?'}` : (c.scan.fileName ?? 'Formular');
    const moves = c.scan.moveCount ? ` · ${c.scan.moveCount} Halbzüge` : '';
    return `#${c.ref} · ${names}${moves}`;
  }

  async merge(c: ScanRef): Promise<void> {
    const ok = await firstValueFrom(this.confirm.ask(`Einlesung #${c.ref} hier dazunehmen? Ihre Fotos werden weitere Fotos dieser `
      + 'Einlesung, sie selbst wird geschlossen, und die Partie wird mit allen Fotos neu gelesen — Änderungen hier gehen dabei verloren.'));
    if (!ok) return;
    this.merging.set(true);
    this.mergeError.set(null);
    try {
      await this.api.merge(this.scanRef, c.ref);
      this.mergeCandidates.set(null);
      this.reload();
    } catch (err) {
      const reason = err instanceof HttpErrorResponse ? (err.error as { reason?: string } | null)?.reason : undefined;
      this.mergeError.set(reason === 'tooManyViews' ? 'Zu viele Fotos für diese Seite (höchstens vier).'
        : reason === 'busy' ? 'Eine der beiden wird gerade gelesen — bitte kurz warten.'
        : 'Zusammenführen hat nicht geklappt.');
    } finally {
      this.merging.set(false);
    }
  }

  onPhotoLoad(e: Event): void {
    if (this.ph.shownView() > 0) return;   // die Maße für den Ausschnitt kommen nur vom Hauptfoto
    const img = e.target as HTMLImageElement;
    if (this.ph.shownPage() === 1) this.s.onPhotoLoad(e);
    else this.s.setPageSize(this.ph.shownPage(), img.naturalWidth, img.naturalHeight);
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
    this.ph.viewCounts.set(st.viewCounts ?? []);
    const reading = st.scan.status === 'pending' || st.scan.status === 'running';
    this.ticker.run(reading);
    if (reading) {
      this.poll();
      return;
    }
    if (st.scan.status !== 'done') return;
    this.s.loadSheet({ plies: st.plies, unresolved: st.unresolved, unresolvedFrom: st.unresolvedFrom, boxes: st.boxes, written: st.written,
      pages: st.pages ?? [] });
    this.f.fill(st);
    await this.ph.load(st.pageCount ?? 1, page => this.api.photo(this.scanRef, page), () => !this.destroyed && my === this.loadSeq);
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
      this.s.loadSheet({ plies: sheet ? alignSheetPlies(sheet.plies, toServer(fromPgn)) : toServer(fromPgn), boxes: sheet?.boxes ?? [],
        written: sheet?.written ?? [], pages: sheet?.pages ?? [] });
    }
    this.state.set({ scan: { id, status: 'done' } } as unknown as LeagueScanState);
    if (sheet) await this.ph.load(sheet.pageCount ?? 1, page => this.api.clubSheetPhoto(id, page), () => !this.destroyed);
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
        white: this.f.name('white')().trim() || null,
        black: this.f.name('black')().trim() || null,
        whiteFide: this.f.fide('white')(),
        blackFide: this.f.fide('black')(),
        whiteElo: this.f.elo('white')(),
        blackElo: this.f.elo('black')(),
        whiteReplace: this.f.replace('white')(),
        blackReplace: this.f.replace('black')(),
        result: this.f.result(),
        event: this.f.event().trim() || null,
        year: this.f.year(),
        scanId: null,
        // Vorschläge geholt: die Wahl gilt (0 = keine); sonst entscheidet der Server über den eindeutigen Treffer.
        leagueGameId: this.f.pairingsLoaded ? this.f.pairingId() ?? 0 : null,
        date: this.f.sheetDate,
        // Der Stand je Zug (Zuordnung zum Formular, Lesarten) geht ins Archiv — „Korrigieren" zeigt ihn wie beim ersten Mal.
        plies: toServer(legal),
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
      white: this.f.name('white')().trim() || null, black: this.f.name('black')().trim() || null,
      result: this.f.result(), event: this.f.event().trim() || null, year: this.f.year(),
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
