import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ClubClient } from '../../core/club-api.service';
import { PlayerSearchComponent } from './player-search.component';
import { ScanHeaderForm, SIDES } from './scan-header-form';

/**
 * Die Kopfdaten eines eingelesenen Formulars vor dem Übernehmen (Formular-Prüfseite): je Seite Name mit
 * Ligaspieler-Prüfung, „Meintest du …", „durch … ersetzen" und Elo; dazu Jahr, Ergebnis, Veranstaltung, Ligapaarung,
 * „Ich spiele" und die Vorschau „Gespeichert wird". Der Zustand liegt in {@link ScanHeaderForm} der Seite.
 */
@Component({
  selector: 'lh-club-side-form',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PlayerSearchComponent],
  // Ohne eigenen Kasten: Raster und Zeilen stehen weiter direkt im Speicherteil der Seite.
  styles: [':host { display: contents; }'],
  template: `
    <div class="save-grid">
      @for (k of sides; track k) {
        <div class="field">{{ k === 'white' ? 'Weiß' : 'Schwarz' }}
          <lh-player-search [client]="client()" [text]="form().name(k)()" [label]="k === 'white' ? 'Weiß' : 'Schwarz'" [searchMega]="false"
                            (textChange)="form().setName(k, $event)" (picked)="form().pickPerson(k, $event)" />
          <span class="match" [class.ok]="(form().match(k)()?.league || form().match(k)()?.mega) && !form().replace(k)()">{{ form().matchText(k) }}</span>
          @if (form().similarOf(k).length) {
            <span class="quick-pick small"><span class="muted">Meintest du</span>
              @for (c of form().similarOf(k); track c.name + (c.fide ?? '')) {
                <button type="button" class="btn-chip" (click)="form().pickPerson(k, c)" [attr.title]="c.teams.join(', ')"
                        [attr.aria-label]="(k === 'white' ? 'Weiß' : 'Schwarz') + ': ' + c.name + ' übernehmen'">{{ c.name }}</button>
              }
            </span>
          }
          <label class="replace-row" [attr.title]="'Nach außen steht dann „' + anon() + '“. Nur im Kurs „Taktiken aus Vereinspartien“ (sieht nur der Verein) steht der Name aus der öffentlichen Paarung der Ligarunde.'">
            <input type="checkbox" [checked]="form().replace(k)()" (change)="form().setReplace(k, $any($event.target).checked)" />
            durch „{{ anon() }}“ ersetzen</label>
        </div>
        <label class="field narrow">Elo<input type="text" inputmode="numeric" pattern="[0-9]*" maxlength="4" autocomplete="off" [value]="form().elo(k)() ?? ''"
                                         (input)="form().elo(k).set(num($any($event.target).value))" [disabled]="form().replace(k)()" /></label>
      }
      <label class="field narrow">Jahr<input type="number" inputmode="numeric" min="1900" [max]="maxYear" [value]="form().year() ?? ''"
                                        (input)="form().year.set(num($any($event.target).value)); form().yearRead.set(null); form().schedulePairings()" />
        @if (form().yearRead(); as r) { <span class="small muted year-hint">gelesen {{ r }} — auf heuer gesetzt, bitte prüfen</span> }
      </label>
      <label class="field narrow">Ergebnis
        <select (change)="form().result.set($any($event.target).value)">
          @for (r of results; track r) { <option [value]="r" [selected]="form().result() === r">{{ r === '*' ? 'unbekannt' : r }}</option> }
        </select>
      </label>
      <label class="field wide">Veranstaltung <span class="muted small">(fällt weg, sobald jemand „{{ anon() }}“ heißt)</span>
        <input [value]="form().event()" (input)="form().event.set($any($event.target).value)" maxlength="200" [disabled]="form().anyReplaced()" />
      </label>
      @if (form().pairings().length) {
        <label class="field wide">Ligapartie <span class="muted small">(Brett einer Ligarunde — Spieler und Jahr kommen dann aus dem Spielplan)</span>
          <select class="pairing-pick" (change)="form().choosePairing($any($event.target).value)">
            <option value="" [selected]="form().pairingId() == null">keine Ligapartie</option>
            @for (p of form().pairings(); track p.id) {
              <option [value]="p.id" [selected]="form().pairingId() === p.id">{{ form().pairingText(p) }}</option>
            }
          </select>
        </label>
      }
    </div>

    <div class="field-row">
      <span class="muted small">Ich spiele</span>
      <div class="seg" role="group" aria-label="Ich spiele">
        <button type="button" [attr.aria-pressed]="form().ownerSide() === 'white'" (click)="form().setOwner('white')">Weiß</button>
        <button type="button" [attr.aria-pressed]="form().ownerSide() === 'black'" (click)="form().setOwner('black')">Schwarz</button>
      </div>
      <span class="muted small">Spieler von {{ clubName() }} und deine Seite werden standardmäßig durch „{{ anon() }}“ ersetzt — dann wird weder
        gespeichert, wer dahinter steht, noch wer hochgeladen hat.</span>
    </div>

    <p class="preview"><span class="muted">Gespeichert wird:</span> <b>{{ form().preview() }}</b></p>
    @if (form().problem(); as pr) { <p class="err small">{{ pr }}</p> }
  `,
})
export class ClubSideFormComponent {
  readonly form = input.required<ScanHeaderForm>();
  readonly client = input.required<ClubClient>();
  /** Name anonymisierter Spieler (der Verein bzw. der des Teilen-Links). */
  readonly anon = input.required<string>();
  readonly clubName = input.required<string>();

  readonly results = ['1-0', '0-1', '1/2-1/2', '*'];
  readonly sides = SIDES;
  readonly maxYear = new Date().getFullYear() + 1;

  num(v: string): number | null {
    const n = Number(v);
    return v.trim() && Number.isFinite(n) ? Math.round(n) : null;
  }
}
