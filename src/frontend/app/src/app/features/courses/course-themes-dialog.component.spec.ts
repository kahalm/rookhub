import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { ALL_THEMES, CourseThemesDialogComponent } from './course-themes-dialog.component';

describe('CourseThemesDialogComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [CourseThemesDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CourseThemesDialogComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
});

// ===== Spiegel-Pin: Buch-Themen ==============================================================
// Dieselben fünf Keys stehen ein zweites Mal im Backend (Services/BookThemeTags.ValidKeys, =
// ChessableTheme-Namen kleingeschrieben). Laufen sie auseinander, zeigt dieser Dialog eine
// Checkbox, deren Haken der Server beim Speichern still wegwirft — oder er lässt ein gültiges
// Thema gar nicht erst wählen. BEIDE Seiten prüfen literale Werte, nicht die jeweils andere
// Implementierung. Wer hier etwas ändert, ändert BookThemeTagsTests mit.
// Die REIHENFOLGE darf abweichen (hier Anzeige-Reihenfolge, dort Enum-Reihenfolge) — die MENGE nicht.
describe('ALL_THEMES (Spiegel von BookThemeTags.ValidKeys)', () => {
  it('holds exactly the five backend theme keys', () => {
    expect([...ALL_THEMES]).toEqual(['tactics', 'endgame', 'opening', 'middlegame', 'other']);
    expect([...ALL_THEMES].sort()).toEqual(['endgame', 'middlegame', 'opening', 'other', 'tactics']);
  });
});
