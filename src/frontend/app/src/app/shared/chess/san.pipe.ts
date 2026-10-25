import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { localizeSan, localizeSanText } from './keyboard-move.util';

/**
 * UI-Sweep 2026-10-11 (r2-rg-notation): ein englischer SAN-Zug (bzw. mit `text` eine ganze Zugfolge)
 * in den Figurenbuchstaben der Oberflächensprache — „Nf3" → „Sf3" auf Deutsch/Kroatisch, „Hf3" auf
 * Ungarisch. Eine Stelle für alle Anzeige-Zuglisten, damit auf einer Seite nicht deutsch und englisch
 * durcheinander stehen. Eingabe und PGN-Export bleiben englisch (dort nie diese Pipe benutzen).
 *
 * <p>Unrein, weil die Sprache zur Laufzeit wechselt; die Arbeit je Aufruf ist ein Regex-Ersatz. Ohne
 * TranslateService (Specs ohne Übersetzung) bleibt der Zug englisch.</p>
 */
@Pipe({ name: 'san', standalone: true, pure: false })
export class SanPipe implements PipeTransform {
  private readonly translate = inject(TranslateService, { optional: true });

  transform(san: string | null | undefined, mode: 'move' | 'text' = 'move'): string {
    if (!san) return san ?? '';
    const lang = this.translate?.currentLang() ?? null;
    return mode === 'text' ? localizeSanText(san, lang) : localizeSan(san, lang);
  }
}
