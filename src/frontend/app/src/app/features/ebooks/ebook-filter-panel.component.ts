import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { EBOOK_LANGUAGE_UNKNOWN, EbookLanguageFacet, EbookTagFacet } from './ebook.service';
import { EbookTagLabelPipe, ebookTagLabel } from './ebook-tags';

/** Zugeklappt zeigt die Schlagwort-Gruppe nur die häufigsten (der Server liefert sie nach Anzahl), c1-eb-tag-filter-lead. */
export const EBOOK_TAGS_COLLAPSED = 8;

/** Ein Wahlpunkt der Sprach-Gruppe: Filterwert + Anzeigename + Anzahl. */
export interface EbookLanguageOption { value: string; label: string; count: number; }

/** Sprachname in der Oberflächensprache (Intl); `null`/`unknown` → „Unbekannt“. */
export function ebookLanguageLabel(code: string | null | undefined, translate: Pick<TranslateService, 'instant'>, uiLang: string): string {
  if (!code || code === EBOOK_LANGUAGE_UNKNOWN) return translate.instant('ebooks.filter.languageUnknown');
  try {
    return new Intl.DisplayNames([uiLang || 'en'], { type: 'language', fallback: 'none' }).of(code) ?? code.toUpperCase();
  } catch {
    return code.toUpperCase();
  }
}

/**
 * Filter der E-Book-Bibliothek in EINER aufklappbaren Komponente (0.877.0, Nutzerwunsch 29.10.: „versteck die tags in
 * einer filterkomponente … elobereich … sprachen“): Schlagwörter (übersetzte Beschriftung, Filterwert bleibt das
 * gespeicherte Schlagwort), Sprache des Buchs bzw. fertige Übersetzung, Elo-Spanne. Die Komponente merkt sich nichts —
 * die Liste hält den Stand (Adresse + Sitzung) und lädt.
 */
@Component({
  selector: 'app-ebook-filter-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslatePipe, EbookTagLabelPipe],
  template: `
    @if (tags().length > 0) {
      <section class="ebf-group">
        <h3 class="ebf-head" id="ebf-tags">{{ 'ebooks.meta.tags' | translate }}</h3>
        <div class="eb-tags" role="group" aria-labelledby="ebf-tags">
          @for (t of visibleTags(); track t.tag) {
            <button type="button" class="eb-chip" [class.eb-chip--on]="tag() === t.tag" [attr.aria-pressed]="tag() === t.tag"
                    [attr.data-tag]="t.tag" (click)="tagChange.emit(t.tag)">{{ t.tag | ebookTagLabel }} <span class="eb-chip-count">{{ t.count }}</span></button>
          }
          @if (tags().length > collapsedCount) {
            <button type="button" class="eb-chip eb-tags-more" [attr.aria-expanded]="tagsOpen()" (click)="tagsOpen.set(!tagsOpen())">
              {{ (tagsOpen() ? 'ebooks.filter.fewerTags' : 'ebooks.filter.allTags') | translate: { n: tags().length } }}
              <mat-icon>{{ tagsOpen() ? 'expand_less' : 'expand_more' }}</mat-icon>
            </button>
          }
        </div>
      </section>
    }
    @if (languageOptions().length > 0 || translationOptions().length > 0) {
      <section class="ebf-group">
        <h3 class="ebf-head" id="ebf-lang">{{ 'ebooks.filter.language' | translate }}</h3>
        <div class="eb-langs" role="group" aria-labelledby="ebf-lang">
          @for (o of languageOptions(); track o.value) {
            <button type="button" class="eb-chip" [class.eb-chip--on]="language() === o.value" [attr.aria-pressed]="language() === o.value"
                    [attr.data-lang]="o.value" (click)="languageChange.emit(o.value)">{{ o.label }} <span class="eb-chip-count">{{ o.count }}</span></button>
          }
          @for (o of translationOptions(); track o.value) {
            <button type="button" class="eb-chip" [class.eb-chip--on]="translation() === o.value" [attr.aria-pressed]="translation() === o.value"
                    [attr.data-translation]="o.value" (click)="translationChange.emit(o.value)">
              <mat-icon>translate</mat-icon>{{ 'ebooks.filter.translatedInto' | translate: { lang: o.label } }} <span class="eb-chip-count">{{ o.count }}</span>
            </button>
          }
        </div>
      </section>
    }
    <!-- 0.822.0: Elo-Spanne — Mindest-Elo des Buchs von … bis …, leer = offen. -->
    <section class="ebf-group">
      <h3 class="ebf-head" id="ebf-elo">{{ 'ebooks.filter.elo' | translate }}</h3>
      <div class="eb-elo" role="group" aria-labelledby="ebf-elo">
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="eb-elo-field">
          <mat-label>{{ 'ebooks.filter.eloFrom' | translate }}</mat-label>
          <input matInput type="number" inputmode="numeric" min="0" max="3500" step="100"
                 [ngModel]="minElo()" (ngModelChange)="eloChange.emit({ which: 'min', value: $event })" />
        </mat-form-field>
        <span class="eb-elo-dash" aria-hidden="true">–</span>
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="eb-elo-field">
          <mat-label>{{ 'ebooks.filter.eloTo' | translate }}</mat-label>
          <input matInput type="number" inputmode="numeric" min="0" max="3500" step="100"
                 [ngModel]="maxElo()" (ngModelChange)="eloChange.emit({ which: 'max', value: $event })" />
        </mat-form-field>
      </div>
    </section>
  `,
  styles: [`
    :host {
      display: flex; flex-direction: column; gap: 12px; min-width: 0;
      padding: 12px; margin-bottom: 12px; border-radius: 8px;
      border: 1px solid var(--mat-sys-outline-variant);
    }
    .ebf-group { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
    .ebf-head { margin: 0; font-size: .8rem; font-weight: 500; opacity: .75; }
    .eb-tags, .eb-langs { display: flex; flex-wrap: wrap; gap: 6px; min-width: 0; }
    .eb-chip {
      display: inline-flex; align-items: center; gap: 4px; max-width: 100%;
      min-height: 32px; padding: 2px 10px; border-radius: 999px;
      border: 1px solid color-mix(in srgb, var(--mat-sys-on-surface) 25%, transparent);
      background: transparent; color: inherit; font: inherit; font-size: .85rem; cursor: pointer;
      mat-icon { font-size: 18px; width: 18px; height: 18px; }
    }
    .eb-chip--on { background: color-mix(in srgb, var(--mat-sys-primary) 18%, transparent); border-color: var(--mat-sys-primary); }
    .eb-chip-count { opacity: .6; font-size: .75rem; }
    .eb-tags-more { border-style: dashed; }
    .eb-elo { display: inline-flex; align-items: center; gap: 4px; }
    .eb-elo-field { width: 104px; }
    .eb-elo-dash { opacity: .6; }
    @media (max-width: 599.98px) {
      :host { padding: 8px; }
      .eb-chip { min-height: 44px; }
    }
  `],
})
export class EbookFilterPanelComponent {
  readonly tags = input<EbookTagFacet[]>([]);
  readonly languages = input<EbookLanguageFacet[]>([]);
  readonly translations = input<EbookLanguageFacet[]>([]);
  readonly tag = input<string | null>(null);
  readonly language = input<string | null>(null);
  readonly translation = input<string | null>(null);
  readonly minElo = input<number | null>(null);
  readonly maxElo = input<number | null>(null);

  /** Gespeichertes (kanonisches) Schlagwort — derselbe Wert noch einmal hebt den Filter in der Liste auf. */
  readonly tagChange = output<string>();
  readonly languageChange = output<string>();
  readonly translationChange = output<string>();
  readonly eloChange = output<{ which: 'min' | 'max'; value: string | number | null }>();

  /** Schlagwort-Gruppe ganz aufgeklappt (sonst nur die häufigsten + das aktive). */
  readonly tagsOpen = signal(false);
  readonly collapsedCount = EBOOK_TAGS_COLLAPSED;

  private translate = inject(TranslateService);
  /** Sprachwechsel sortiert und beschriftet neu (currentLang ist in ngx-translate 18 ein Signal). */
  private readonly uiLang = computed(() => this.translate.currentLang() || 'en');

  /**
   * Zugeklappt die häufigsten (Server-Reihenfolge) plus das aktive; aufgeklappt ALLE nach der angezeigten Beschriftung in
   * der Oberflächensprache sortiert — „Eröffnung“ steht im Englischen als „Opening“ unter O.
   */
  readonly visibleTags = computed<EbookTagFacet[]>(() => {
    const all = this.tags();
    const lang = this.uiLang();
    if (this.tagsOpen()) {
      const label = new Map(all.map(t => [t.tag, ebookTagLabel(t.tag, this.translate)]));
      return [...all].sort((a, b) => label.get(a.tag)!.localeCompare(label.get(b.tag)!, lang, { sensitivity: 'base' }));
    }
    if (all.length <= EBOOK_TAGS_COLLAPSED) return all;
    const active = this.tag();
    return all.filter((t, i) => i < EBOOK_TAGS_COLLAPSED || t.tag === active);
  });

  readonly languageOptions = computed<EbookLanguageOption[]>(() => {
    const lang = this.uiLang();
    return this.languages().map(f => ({
      value: f.language ?? EBOOK_LANGUAGE_UNKNOWN,
      label: ebookLanguageLabel(f.language, this.translate, lang),
      count: f.count,
    }));
  });

  readonly translationOptions = computed<EbookLanguageOption[]>(() => {
    const lang = this.uiLang();
    return this.translations().filter(f => !!f.language && f.count > 0).map(f => ({
      value: f.language!,
      label: ebookLanguageLabel(f.language, this.translate, lang),
      count: f.count,
    }));
  });
}
