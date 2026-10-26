import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { IconLabelDirective } from '../icon-label/icon-label.directive';
import { TranslatePipe } from '@ngx-translate/core';
import { MarkOrigin, MarkedPositionsService, positionKey } from './marked-positions.service';

/**
 * Der „+"-Knopf (0.749.0, Wunsch 2026-10-11: „besonders gute Stellungen mit einem + markieren — in der Analyse und beim
 * Fehler-Nachspielen"). Markiert die übergebene Stellung bzw. nimmt die Markierung zurück; markiert = grün gefüllt.
 * Seit 0.847.0 auch ohne Anmeldung (dann unter der anonymen Kennung, beim Anmelden ins Konto übernommen). Die Stellungen
 * sind der Vorrat für ein späteres „Stellungen für alle nachspielen".
 */
@Component({
  selector: 'app-mark-position-button',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, IconLabelDirective, TranslatePipe],
  template: `
    @if (fen()) {
      <button mat-icon-button type="button" class="mark" [class.on]="marked()" (click)="toggle()"
              [attr.aria-pressed]="marked()"
              [appIconLabel]="(marked() ? 'markPosition.unmark' : 'markPosition.mark') | translate">
        <span class="plus" aria-hidden="true"></span>
      </button>
    }
  `,
  styles: [`
    :host { display: inline-flex; align-items: center; vertical-align: middle; }
    /* Das Kreuz ist gezeichnet, kein „+"-Zeichen: die Glyphe saß je nach Schrift unter der Mitte des Kreises
       (gemeldet 2026-10-26 „das + ist zu niedrig"). Zwei Balken, exakt mittig. */
    .plus { position: relative; display: inline-block; width: 26px; height: 26px; border-radius: 50%;
      border: 2px solid currentColor; box-sizing: border-box; opacity: 0.7; }
    .plus::before, .plus::after { content: ''; position: absolute; left: 50%; top: 50%; background: currentColor;
      border-radius: 1px; transform: translate(-50%, -50%); }
    .plus::before { width: 12px; height: 2.5px; }
    .plus::after { width: 2.5px; height: 12px; }
    /* Zustandsfarbe aus dem Token (docs/DESIGN.md 3.1): --rh-success ist im Dunkelmodus HELL — darum die Schrift in der
       Flächenfarbe statt fest weiß (hell: weiß auf Dunkelgrün, dunkel: dunkel auf Hellgrün, beide ≥ 4,5:1). */
    .mark.on .plus { background: var(--rh-success); border-color: var(--rh-success); color: var(--mat-sys-surface); opacity: 1; }
  `],
})
export class MarkPositionButtonComponent implements OnInit {
  private readonly service = inject(MarkedPositionsService);

  /** Die Stellung, die der Knopf meint (FEN). */
  readonly fen = input<string | null | undefined>(null);
  /** Herkunft der Stellung — geht mit der Markierung an den Server. */
  readonly origin = input<MarkOrigin>({ context: 'analysis' });

  readonly marked = computed(() => {
    const fen = this.fen();
    return !!fen && this.service.keys().has(positionKey(fen));
  });

  ngOnInit(): void {
    this.service.ensureLoaded();
  }

  toggle(): void {
    const fen = this.fen();
    if (!fen) return;
    this.service.toggle(fen, this.origin()).subscribe({ error: () => { /* Knopf ist schon zurückgedreht */ } });
  }
}
