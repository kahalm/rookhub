import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { GameSources } from '../core/league.models';
import { sourceGroups } from '../core/game-sources';
import { tn } from '../core/league-format';

/**
 * Partien im Bestand je Quelle als kleine Tabelle (0.628.0, Fassung B des Entwurfs vom 01.10.2026): Quelle | Gesamt | Liga |
 * Begegnung, nach Brett/Online gruppiert, Anteil an „Gesamt" als Balken. Auf der Startseite und auf dem Teilen-Link; die Spalten
 * „Liga" und „Begegnung" nur, wenn der Server sie mitgeschickt hat.
 */
@Component({
  selector: 'lh-game-sources',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="src-scroll">
      <table class="src-tbl">
        <caption>Partien im Bestand</caption>
        <thead>
          <tr>
            <th scope="col">Quelle</th>
            <th scope="col" class="num">Gesamt<span class="src-sub">alle Ligen</span></th>
            @if (sources().league; as l) {
              <th scope="col" class="num">Liga<span class="src-sub">{{ leagueName() ? leagueName() + ' · ' : '' }}{{ l.players }} Spieler</span></th>
            }
            @if (sources().opponent; as o) {
              <th scope="col" class="num">Begegnung<span class="src-sub">{{ oppName() ? oppName() + ' · ' : '' }}{{ o.players }} Spieler</span></th>
            }
          </tr>
        </thead>
        <tbody>
          @for (g of groups(); track g.title) {
            <tr class="src-group">
              <th scope="rowgroup">{{ g.title }}</th>
              <td class="num">{{ g.games }}</td>
              @if (sources().league) { <td class="num" [class.none]="g.league === '–'" [attr.title]="g.league === '–' ? noAccount('der Liga', '') : null">{{ g.league }}</td> }
              @if (sources().opponent) { <td class="num" [class.none]="g.opp === '–'" [attr.title]="g.opp === '–' ? noAccount(oppName(), '') : null">{{ g.opp }}</td> }
            </tr>
            @for (r of g.rows; track r.key) {
              <tr>
                <td>
                  <div class="src-share">{{ r.label }}<i aria-hidden="true"><b [class.top]="r.top" [style.width.%]="r.share"></b></i></div>
                </td>
                <td class="num">{{ r.games }}</td>
                @if (sources().league) { <td class="num" [class.none]="r.league === '–'" [attr.title]="r.league === '–' ? noAccount('der Liga', r.label) : null">{{ r.league }}</td> }
                @if (sources().opponent) { <td class="num" [class.none]="r.opp === '–'" [attr.title]="r.opp === '–' ? noAccount(oppName(), r.label) : null">{{ r.opp }}</td> }
              </tr>
            }
          }
        </tbody>
      </table>
    </div>
  `,
})
export class GameSourcesComponent {
  readonly sources = input.required<GameSources>();
  /** Name der gewählten Liga (Kopf der Spalte „Liga"). */
  readonly league = input<string | null | undefined>(null);
  /** Name des Gegners (Kopf der Spalte „Begegnung"), wie in der Begegnung. */
  readonly opponent = input<string | null | undefined>(null);

  readonly groups = computed(() => sourceGroups(this.sources()));
  readonly leagueName = computed(() => this.league() ?? '');
  readonly oppName = computed(() => tn(this.opponent()));

  /** „Für die Spieler von Freibauer Innsbruck ist kein Lichess-Konto eingetragen". */
  noAccount(who: string, site: string): string {
    const whom = who === 'der Liga' ? 'der Liga' : `von ${who || 'diesem Gegner'}`;
    return `Für die Spieler ${whom} ist kein ${site ? site + '-' : 'Online-'}Konto eingetragen`;
  }
}
