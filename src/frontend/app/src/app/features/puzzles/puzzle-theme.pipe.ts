import { Pipe, PipeTransform, Signal, computed, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

/**
 * UI-Sweep 2026-10-11 (r2-rp-themes-en): Lichess-Themen-Schlüssel („mateIn2", „discoveredCheck")
 * standen roh auf deutschen Seiten. Die Übersetzung kommt aus `puzzleThemes.<key>`; fehlt sie
 * (neues Lichess-Thema, Teilübersetzung ohne en-Eintrag), wird der Schlüssel lesbar gemacht.
 */
export function humanizeThemeKey(key: string): string {
  const spaced = key
    .replace(/([a-z])([A-Z0-9])/g, '$1 $2')
    .replace(/([0-9])([A-Za-z])/g, '$1 $2')
    .toLowerCase()
    .trim();
  return spaced ? spaced.charAt(0).toUpperCase() + spaced.slice(1) : key;
}

/**
 * Lichess-Themen (leer- oder kommagetrennt) als auf `max` gekürzte Schlüsselliste — zur Anzeige je Eintrag
 * durch `puzzleTheme` schicken, nie roh zusammenfügen (UI-Sweep 2026-10-11, d1-rk-revenge-themen-rohschluessel).
 */
export function puzzleThemeList(themes: string | null | undefined, max = 4): string[] {
  if (!themes) return [];
  return themes.split(/[\s,]+/).filter(t => t).slice(0, max);
}

/**
 * `{{ theme | puzzleTheme }}` — übersetzter Name eines Puzzle-Themas. Unrein mit Signal je
 * Schlüssel (wie TranslatePipe in ngx-translate 18), damit ein Sprachwechsel und das späte Laden
 * der Sprachdatei auch in OnPush-Komponenten ankommen.
 */
@Pipe({ name: 'puzzleTheme', standalone: true, pure: false })
export class PuzzleThemePipe implements PipeTransform {
  private readonly translate = inject(TranslateService);
  private lastKey: string | null = null;
  private value: Signal<string> | null = null;

  transform(key: string | null | undefined): string {
    if (!key) return '';
    if (key !== this.lastKey || !this.value) {
      const path = 'puzzleThemes.' + key;
      const raw = this.translate.translate(path);
      this.value = computed(() => {
        const t = raw();
        return typeof t === 'string' && t && t !== path ? t : humanizeThemeKey(key);
      });
      this.lastKey = key;
    }
    return this.value();
  }
}
