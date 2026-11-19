import { Directive, ElementRef, Input, OnChanges, inject } from '@angular/core';
import { MatTooltip } from '@angular/material/tooltip';

/** Verzögerung des Tooltips am Hauptziel einer ganz klickbaren Kachel (`rh-tile-target`, DESIGN 5.2). */
export const TILE_TOOLTIP_DELAY_MS = 600;

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
 *
 * Ist der Symbolknopf das Hauptziel einer ganz klickbaren Kachel (Klasse `rh-tile-target`, DESIGN 5.2), gilt der Zeiger
 * über der GANZEN Kachel als „über dem Knopf“ — der Tooltip erscheint dann erst nach {@link TILE_TOOLTIP_DELAY_MS}, statt
 * bei jeder Mausbewegung über die Karte aufzublitzen.
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  ngOnChanges(): void {
    this.tooltip.message = this.appIconLabel;
    if (this.host.nativeElement.classList.contains('rh-tile-target')) this.tooltip.showDelay = TILE_TOOLTIP_DELAY_MS;
  }
}
