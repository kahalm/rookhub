import { MatDialogRef } from '@angular/material/dialog';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData } from './puzzle-settings-dialog.component';

describe('PuzzleSettingsDialogComponent', () => {
  function create(overrides: Partial<PuzzleSettingsDialogData> = {}): PuzzleSettingsDialogComponent {
    const data: PuzzleSettingsDialogData = {
      mode: 'standard',
      boardTheme: 'brown',
      pieceSet: 'cburnett',
      themeMode: 'fixed',
      visualizationMode: 0,
      vizArrowEnabled: true,
      ...overrides,
    };
    const dialogRef = {} as MatDialogRef<PuzzleSettingsDialogComponent>;
    return new PuzzleSettingsDialogComponent(dialogRef, data);
  }

  it('vizLevelOptions liefert i18n-Keys statt hartcodierter Texte', () => {
    const opts = create().vizLevelOptions;
    expect(opts.length).toBe(5);
    opts.forEach((opt, i) => {
      expect(opt.value).toBe(i);
      expect(opt.label).toBe(`puzzles.viz.level${i}Name`);
      expect(opt.description).toBe(`puzzles.viz.level${i}Desc`);
      // darf keinen rohen Anzeigetext mehr enthalten (regressionsschutz gegen de/en-Mischstrings)
      expect(opt.description).not.toContain(' ');
    });
  });

  it('difficultyInfoOptions liefert i18n-Keys für Label und Beschreibung', () => {
    const opts = create().difficultyInfoOptions;
    expect(opts.length).toBe(5);
    opts.forEach(opt => {
      expect(opt.label.startsWith('puzzles.difficulty.')).toBeTrue();
      expect(opt.description.startsWith('puzzles.difficulty.')).toBeTrue();
      expect(opt.description.endsWith('Desc')).toBeTrue();
      expect(opt.description).not.toContain(' ');
    });
  });
});
