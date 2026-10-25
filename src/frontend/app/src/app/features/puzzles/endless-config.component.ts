import { ChangeDetectionStrategy, Component, inject, input, model, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipInputEvent, MatChipsModule } from '@angular/material/chips';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { COMMA, ENTER, SPACE } from '@angular/cdk/keycodes';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { EndlessConfig } from './endless-storage.service';
import { EndlessFasttrackState } from './endless-fasttrack-state';
import { chainRatingAt, CHAIN_T1_INDEX, CHAIN_T2_INDEX, ENDLESS_CHAIN_BLOCK, FIRST_RUN_ANCHOR1_INDEX, FIRST_RUN_ANCHOR2_INDEX } from './endless-prefetch.util';
import { PuzzleRatingRange } from './puzzle.service';
import { PUZZLE_THEME_PRESETS, ThemePreset, isThemePresetActive } from './puzzle-theme-presets';
import { PuzzleThemePipe, humanizeThemeKey } from './puzzle-theme.pipe';

/** Der Ausschnitt des gespeicherten Spielstands, den der Fortsetzen-Kasten zeigt. */
export interface EndlessResumeInfo {
  level: number;
  solved: number;
  lives: number;
  maxRatingReached: number;
}

/**
 * Start-Bildschirm des Endlos-Modus (Zerlegung Z19): Fortsetzen/Archivieren eines offenen Laufs, Start, Start-Rating,
 * Themen (Schnellwahl, durchsuchbare Mehrfachauswahl, „schwächste Themen“), Feineinstellungen mit Kurvenvorschau.
 *
 * Wie vorher bearbeiten die Felder `config` und `fasttrack` DIREKT (ngModel auf das Objekt des Containers); Änderungen
 * der Themen meldet `configSave` (der Container speichert). Damit die OnPush-Ansicht Änderungen sieht, die der
 * Container selbst asynchron vornimmt, kommt mit jeder solchen Änderung ein neuer Input-Wert: `themes` (= config.themes,
 * „schwächste Themen“ werden nachgeladen), `puzzleRange` (Start-Rating wird geklemmt), `config` (Server-Abgleich).
 */
@Component({
  selector: 'app-endless-config',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSlideToggleModule, MatChipsModule, MatAutocompleteModule, IconLabelDirective, TranslatePipe, PuzzleThemePipe,
  ],
  templateUrl: './endless-config.component.html',
  styleUrls: ['./endless-config.component.scss'],
})
export class EndlessConfigComponent {
  private readonly translate = inject(TranslateService);

  readonly config = input.required<EndlessConfig>();
  /** = `config.themes`; nur Auslöser fürs Neuzeichnen (siehe Klassenkommentar). */
  readonly themes = input('');
  /** Alle verfügbaren Themen (vom Backend, alphabetisch) — Optionen des Autocomplete-Dropdowns. */
  readonly allThemes = input<readonly string[]>([]);
  readonly activeGameState = input<EndlessResumeInfo | null>(null);
  readonly puzzleRange = input<PuzzleRatingRange>({ min: 100, max: 3000 });
  readonly fasttrack = input.required<EndlessFasttrackState>();
  readonly isFirstRun = input(false);
  readonly highscore = input(0);
  readonly sessionCount = input(0);
  readonly isLoggedIn = input(false);
  /** Feineinstellungen aufgeklappt (Stockfish-Tiefe, Schwellen, Kurvenvorschau); standardmäßig zu. */
  readonly showAdvanced = model(false);

  readonly start = output<void>();
  readonly resume = output<void>();
  readonly archiveAndNew = output<void>();
  readonly help = output<void>();
  readonly settings = output<void>();
  readonly history = output<void>();
  /** Themen geändert → Container speichert die Konfiguration. */
  readonly configSave = output<void>();
  /** Schalter „5 schwächste Themen“ umgelegt (config.worstTags ist schon gesetzt). */
  readonly worstTagsToggle = output<boolean>();
  /** Eine Schwelle wurde von Hand geändert (fasttrack.avgFirst/avgSecond ist schon gesetzt). */
  readonly thresholdChange = output<void>();
  readonly thresholdReset = output<1 | 2>();

  // ── Themen-Auswahl (durchsuchbare Multiselect-Dropdown) ───────────────────────────
  /** Freitext im Sucheingabefeld der Themen-Auswahl (filtert das Dropdown). */
  themeInput = '';
  /** Mit diesen Tasten wird der getippte Text als Chip übernommen (erlaubt freie Themen). */
  readonly themeSeparatorKeys: number[] = [ENTER, COMMA, SPACE];
  /** Kuratierte Themen-Bündel; ein Klick setzt `config.themes` (Endless filtert mit ODER). */
  readonly themePresets = PUZZLE_THEME_PRESETS;

  /** Aktuell gewählte Themen als Liste (Backing-Store bleibt der leerzeichengetrennte `config.themes`-String). */
  get selectedThemes(): string[] {
    return (this.config().themes || '').trim().split(/\s+/).filter(Boolean);
  }
  private setSelectedThemes(themes: string[]): void {
    // Duplikate raus, Reihenfolge erhalten.
    this.config().themes = [...new Set(themes)].join(' ');
    this.configSave.emit();
  }

  /** Preset anwenden: überschreibt die Themen. „Schwächste Themen" wird dabei deaktiviert
   *  (es überschriebe sonst die Themen-Auswahl). Erneuter Klick auf einen bereits aktiven Preset
   *  löscht die Auswahl wieder (Toggle-off — der User kann so alles freischalten). */
  applyThemePreset(preset: ThemePreset): void {
    const config = this.config();
    if (config.worstTags) config.worstTags = false;
    if (this.isThemePresetActive(preset)) {
      this.setSelectedThemes([]);
    } else {
      this.setSelectedThemes(preset.themes);
    }
  }

  /** Löscht die gesamte Themen-Auswahl (Endless zeigt danach ALLE Themen). */
  clearThemes(): void {
    const config = this.config();
    if (config.worstTags) config.worstTags = false;
    this.setSelectedThemes([]);
  }

  /** True, wenn der Preset gerade exakt aktiv ist (für die Chip-Hervorhebung). */
  isThemePresetActive(preset: ThemePreset): boolean {
    return !this.config().worstTags && isThemePresetActive(preset, this.selectedThemes);
  }

  /** Optionen fürs Dropdown: noch nicht gewählte Themen, nach dem Suchtext gefiltert. */
  get filteredThemes(): string[] {
    const q = this.themeInput.trim().toLowerCase();
    const selected = new Set(this.selectedThemes);
    return this.allThemes()
      .filter(t => !selected.has(t))
      .filter(t => !q || t.toLowerCase().includes(q) || this.themeLabel(t).toLowerCase().includes(q));
  }

  /** UI-Sweep 2026-10-11 (r2-rp-themes-en): angezeigter (übersetzter) Name eines Themen-Schlüssels —
   *  die Suche im Dropdown findet „Gabel" genauso wie „fork". */
  private themeLabel(key: string): string {
    const path = 'puzzleThemes.' + key;
    const t = this.translate.instant(path);
    return typeof t === 'string' && t && t !== path ? t : humanizeThemeKey(key);
  }

  /** Thema aus dem Dropdown übernommen. */
  onThemeSelected(event: MatAutocompleteSelectedEvent): void {
    this.addThemeValue(event.option.value);
    this.themeInput = '';
  }

  /** Frei getipptes Thema per Enter/Komma/Leertaste übernehmen (nicht in der Vorschlagsliste). */
  onThemeInputTokenEnd(event: MatChipInputEvent): void {
    const value = (event.value || '').trim();
    // Getippter übersetzter Name („Matt in 2") → Schlüssel („mateIn2"), sonst Freitext wie bisher.
    const byLabel = value ? this.allThemes().find(t => this.themeLabel(t).toLowerCase() === value.toLowerCase()) : undefined;
    if (value) this.addThemeValue(byLabel ?? value);
    event.chipInput?.clear();
    this.themeInput = '';
  }

  private addThemeValue(theme: string): void {
    const normalized = theme.trim();
    if (!normalized || this.selectedThemes.includes(normalized)) return;
    this.setSelectedThemes([...this.selectedThemes, normalized]);
  }

  /** Thema-Chip entfernen. */
  removeTheme(theme: string): void {
    this.setSelectedThemes(this.selectedThemes.filter(t => t !== theme));
  }

  /** Vorschau der Ketten-Kurve: Rating an markanten Stellen (Start, T1 ≈ Puzzle 6, T2 ≈ Puzzle 21, Block-Ende).
   *  Marken werden auf den Block geklemmt, aufsteigend sortiert und dedupliziert — sonst zeigte der
   *  erste Lauf „Puzzle 1 / 16 / 31 / 30": Anker 2 liegt bei Index 30 und damit AUSSERHALB des
   *  30er-Blocks (0–29), landete vor dem Block-Ende und mit höherem Rating als die Zeile darunter. */
  get chainPreview(): { puzzle: number; rating: number }[] {
    const s = this.config().startElo, t1 = this.fasttrack().avgFirst, t2 = this.fasttrack().avgSecond;
    const fr = this.isFirstRun();
    const last = ENDLESS_CHAIN_BLOCK - 1;
    const raw = fr
      ? [0, FIRST_RUN_ANCHOR1_INDEX, FIRST_RUN_ANCHOR2_INDEX, last]
      : [0, CHAIN_T1_INDEX, CHAIN_T2_INDEX, last];
    const marks = [...new Set(raw.map(i => Math.min(Math.max(i, 0), last)))].sort((a, b) => a - b);
    return marks.map(i => ({ puzzle: i + 1, rating: chainRatingAt(i, s, t1, t2, fr) }));
  }
}
