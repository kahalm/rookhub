import { Directive, Input, OnChanges, inject } from '@angular/core';
import { MatTooltip } from '@angular/material/tooltip';

/**
 * Beschriftung für reine Symbolknöpfe (Codereview UX-014): EIN Text setzt Tooltip UND zugänglichen Namen.
 *
 * `matTooltip` allein hängt seinen Text nur als `aria-describedby` an — der Knopf heißt für Hilfstechnik dann bloß
 * „Schaltfläche" (axe `button-name`, critical), und am Handy erscheint der Tooltip erst nach langem Druck. Hier wird
 * derselbe Text zusätzlich `aria-label`; Materials AriaDescriber lässt die gleichlautende Beschreibung dann weg, der
 * Screenreader liest also nicht zweimal vor (Vorbild bisher von Hand: `position-menu.component.ts`).
 *
 * Nutzung STATT `[matTooltip]` — nicht zusätzlich: MatTooltip steckt als Host-Direktive drin, beides auf einem
 * Element wäre dieselbe Direktive doppelt (Angular wirft NG0309).
 *
 *     <button mat-icon-button [appIconLabel]="'analysis.flip' | translate"><mat-icon>cached</mat-icon></button>
 */
@Directive({
  selector: '[appIconLabel]',
  standalone: true,
  hostDirectives: [MatTooltip],
  host: { '[attr.aria-label]': 'appIconLabel || null' },
})
export class IconLabelDirective implements OnChanges {
  /** Schon übersetzter Text (meist `'schlüssel' | translate`) — wird Tooltip und `aria-label`. */
  @Input({ required: true }) appIconLabel = '';

  private readonly tooltip = inject(MatTooltip);

  ngOnChanges(): void {
    this.tooltip.message = this.appIconLabel;
  }
}
