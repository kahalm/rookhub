import { ChangeDetectionStrategy, Component, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * Fehlerkachel der Kinderseite: ein Bild, ein kurzer Satz und ein grosser Knopf „↻ Nochmal", der neu laedt. Vorher
 * stand auf Startseite, Stufenkarte, Stufe und Kurs nur der Satz — ein Kind, das (noch) nicht liest, kam dort nicht
 * weiter, und die Kursliste zeigte einen Fehler als „Noch keine Kurse" (Codereview 2026-09-29, F7-011).
 */
@Component({
  selector: 'kid-error',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  template: `
    <div class="card" role="alert">
      <span class="pic" aria-hidden="true">🙈</span>
      <p class="text">{{ 'kids.loadError' | translate }}</p>
      <button type="button" class="again" (click)="retry.emit()">↻ {{ 'kids.retry' | translate }}</button>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .card {
      display: flex; flex-direction: column; align-items: center; gap: 10px; max-width: 420px; margin: 12px auto;
      padding: 22px 18px; border-radius: 28px; background: var(--kid-card); box-shadow: 0 6px 0 var(--kid-shadow);
      text-align: center;
    }
    .pic { font-size: 3.4rem; line-height: 1.1; }
    .text { margin: 0; font-size: 1.2rem; font-weight: 700; }
    .again {
      font: inherit; font-size: 1.4rem; font-weight: 800; min-height: 52px; padding: 12px 28px; border: 0;
      border-radius: 999px; cursor: pointer; background: var(--kid-green-strong); color: #fff;
      box-shadow: 0 5px 0 var(--kid-shadow);
    }
    .again:active { transform: translateY(3px); box-shadow: 0 2px 0 var(--kid-shadow); }
  `],
})
export class KidsErrorComponent {
  /** „Nochmal" gedrueckt — die Seite laedt neu. */
  readonly retry = output<void>();
}
