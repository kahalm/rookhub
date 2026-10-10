import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { MarkOrigin, MarkedPositionsService, positionKey } from './marked-positions.service';

/**
 * Der „+"-Knopf (0.749.0, Wunsch 2026-10-11: „besonders gute Stellungen mit einem + markieren — in der Analyse und beim
 * Fehler-Nachspielen"). Markiert die übergebene Stellung bzw. nimmt die Markierung zurück; markiert = grün gefüllt.
 * Nur angemeldet sichtbar. Die Stellungen sind der Vorrat für ein späteres „Stellungen für alle nachspielen".
 */
@Component({
  selector: 'app-mark-position-button',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatTooltipModule, TranslatePipe],
  template: `
    @if (loggedIn && fen()) {
      <button mat-icon-button type="button" class="mark" [class.on]="marked()" (click)="toggle()"
              [attr.aria-pressed]="marked()"
              [matTooltip]="(marked() ? 'markPosition.unmark' : 'markPosition.mark') | translate"
              [attr.aria-label]="(marked() ? 'markPosition.unmark' : 'markPosition.mark') | translate">
        <span class="plus" aria-hidden="true">+</span>
      </button>
    }
  `,
  styles: [`
    :host { display: inline-flex; }
    .plus { display: inline-flex; align-items: center; justify-content: center; width: 26px; height: 26px; border-radius: 50%;
      font-size: 22px; font-weight: 700; line-height: 1; border: 2px solid currentColor; box-sizing: border-box; opacity: 0.7; }
    .mark.on .plus { background: #2e7d32; border-color: #2e7d32; color: #fff; opacity: 1; }
  `],
})
export class MarkPositionButtonComponent implements OnInit {
  private readonly service = inject(MarkedPositionsService);
  readonly loggedIn = inject(AuthService).isLoggedIn;

  /** Die Stellung, die der Knopf meint (FEN). */
  readonly fen = input<string | null | undefined>(null);
  /** Herkunft der Stellung — geht mit der Markierung an den Server. */
  readonly origin = input<MarkOrigin>({ context: 'analysis' });

  readonly marked = computed(() => {
    const fen = this.fen();
    return !!fen && this.service.keys().has(positionKey(fen));
  });

  ngOnInit(): void {
    if (this.loggedIn) this.service.ensureLoaded();
  }

  toggle(): void {
    const fen = this.fen();
    if (!fen) return;
    this.service.toggle(fen, this.origin()).subscribe({ error: () => { /* Knopf ist schon zurückgedreht */ } });
  }
}
