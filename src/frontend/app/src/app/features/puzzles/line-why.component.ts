import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LineWhy, lineWhyText } from './line-why.util';

/**
 * „Warum diese Aufgabe:“ — eine leise Zeile mit dem Vermerk einer Linie des Kurses „Persönliche Taktiken“ (1.001.0, DESIGN 8.1
 * Kurs-Löser): Kontext-Karte des Lösers und „Linien durchsehen“. Ohne Vermerk (andere Kurse, nicht zuzuordnender Bestand) nichts.
 */
@Component({
  selector: 'app-line-why',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (text(); as t) {
      <p class="line-why"><span class="line-why-label">{{ 'book.why.label' | translate }}</span> {{ t }}</p>
    }
  `,
  styles: [`
    :host { display: block; }
    .line-why { margin: 4px 0 0; font-size: 0.85rem; line-height: 1.4; color: var(--mat-sys-on-surface-variant); overflow-wrap: anywhere; }
    .line-why-label { font-weight: 600; }
  `],
})
export class LineWhyComponent {
  private readonly translate = inject(TranslateService);
  readonly why = input<LineWhy | null | undefined>(null);
  /** Neu bauen, sobald eine Sprachdatei da ist bzw. gewechselt wird (der Satz wird aus mehreren Schlüsseln zusammengesetzt). */
  private readonly langTick = toSignal(this.translate.onLangChange, { initialValue: null });

  readonly text = computed(() => {
    this.langTick();
    const lang = this.translate.currentLang();
    return lineWhyText(this.why(), (key, params) => this.translate.instant(key, params) as string, lang);
  });
}
