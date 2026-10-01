import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { TreeFilter, TreeSource } from '@lh/core/league.models';
import { TREE_SPEEDS, TREE_YEARS, normalizeTreeFilter, toggleSpeed } from './tree-filter';

/**
 * Filterleiste der Spielerkarte (0.617.0, Wunsch 2026-09-30: „auch an der Stelle will ich die vollen Filtermöglichkeiten"):
 * Brett / Brett + online / Online, Zeitformat der Online-Partien, unsichere Konten, letzte x Jahre. Sie gilt für das
 * Eröffnungsprofil UND den Eröffnungsbaum der Karte; den Stand hält die Karte (gemerkt je Gerät). Die Leiste zeigt den
 * WIRKSAMEN Filter (<see cref="active"/>) und meldet Änderungen am gemerkten (<see cref="filter"/>).
 */
@Component({
  selector: 'lh-tree-filter-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="tree-filter">
      @if (onlineGames > 0) {
        <div class="seg" role="group" aria-label="Welche Partien">
          @for (o of sources; track o.k) {
            <button type="button" [attr.aria-pressed]="active.source === o.k" [disabled]="o.k === 'board' && !boardGames"
                    (click)="setSource(o.k)">{{ o.label }}</button>
          }
        </div>
        @if (active.source !== 'board') {
          <div class="chips" role="group" aria-label="Zeitformat der Online-Partien">
            <button type="button" class="fchip" [attr.aria-pressed]="!active.speeds.length" (click)="setSpeeds([])">Alle Tempi</button>
            @for (sp of speedOptions; track sp.key) {
              <button type="button" class="fchip" [attr.aria-pressed]="active.speeds.includes(sp.key)" (click)="flipSpeed(sp.key)">{{ sp.label }}</button>
            }
          </div>
          @if (!token && unsure) {
            <label class="check small" title="Konten, bei denen nicht sicher ist, dass sie ihm gehören — standardmäßig nicht dabei"><input type="checkbox" [checked]="active.withUnsure" (change)="setWithUnsure($any($event.target).checked)" />
              auch unsichere Konten@if (unsureGames > 0) { ({{ unsureGames }} Partien) }</label>
          }
        }
      }
      <label class="field inline small">Zeitraum
        <select (change)="setYears($any($event.target).value)">
          <option value="" [selected]="!active.years">alle Jahre</option>
          @for (y of yearOptions; track y) {
            <option [value]="y" [selected]="active.years === y">{{ y === 1 ? 'letztes Jahr' : 'letzte ' + y + ' Jahre' }}</option>
          }
        </select>
      </label>
    </div>
  `,
})
export class TreeFilterBarComponent {
  /** Der gemerkte Filter — Änderungen setzen darauf auf. */
  @Input({ required: true }) filter!: TreeFilter;
  /** Der wirksame (ohne Online-Partien nur Brett, ohne Brettpartien gleich online) — den zeigt die Leiste. */
  @Input({ required: true }) active!: TreeFilter;
  @Input() boardGames = 0;
  @Input() onlineGames = 0;
  /** Davon aus unsicheren Konten — zählen nur mit dem Schalter. */
  @Input() unsureGames = 0;
  /** Teilen-Link: nur gesicherte Konten, also kein Schalter. */
  @Input() token: string | null = null;
  /** Den Schalter überhaupt anbieten (Spielervorbereitung: nur mit `prep.manage`). */
  @Input() unsure = true;
  @Output() readonly changed = new EventEmitter<TreeFilter>();

  readonly sources: { k: TreeSource; label: string }[] = [
    { k: 'board', label: 'Brett' }, { k: 'both', label: 'Brett + online' }, { k: 'online', label: 'Online' },
  ];
  readonly speedOptions = TREE_SPEEDS;
  readonly yearOptions = TREE_YEARS;

  setSource(source: TreeSource): void { this.emit({ ...this.filter, source }); }
  setSpeeds(speeds: string[]): void { this.emit({ ...this.filter, speeds }); }
  flipSpeed(key: string): void { this.setSpeeds(toggleSpeed(this.active.speeds, key)); }
  setWithUnsure(withUnsure: boolean): void { this.emit({ ...this.filter, withUnsure }); }

  setYears(value: string): void {
    const years = Number(value);
    this.emit({ ...this.filter, years: TREE_YEARS.includes(years) ? years : null });
  }

  private emit(f: TreeFilter): void {
    this.changed.emit(normalizeTreeFilter(f));
  }
}
