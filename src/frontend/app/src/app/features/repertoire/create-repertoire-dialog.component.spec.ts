import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { CreateRepertoireDialogComponent } from './create-repertoire-dialog.component';
import { Repertoire } from '../../core/models';

describe('CreateRepertoireDialogComponent', () => {
  function build(data: Repertoire | null): CreateRepertoireDialogComponent {
    TestBed.configureTestingModule({
      providers: [
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    });
    return TestBed.runInInjectionContext(() => new CreateRepertoireDialogComponent(
      TestBed.inject(MatDialogRef),
      TestBed.inject(MAT_DIALOG_DATA),
    ));
  }

  it('neue Repertoires sind standardmäßig für die Extension aktiviert', () => {
    const c = build(null);
    expect(c.editMode).toBeFalse();
    expect(c.useForExtension).toBeTrue();
  });

  it('übernimmt useForExtension aus den Bearbeiten-Daten', () => {
    const rep = { id: 1, name: 'R', description: null, isPublic: false, kind: 1,
      fileCount: 0, useForExtension: false, createdAt: '', updatedAt: '' } as Repertoire;
    const c = build(rep);
    expect(c.editMode).toBeTrue();
    expect(c.useForExtension).toBeFalse();
  });

  describe('Layout (gemeldet 2026-10-07)', () => {
    function render(): HTMLElement {
      TestBed.configureTestingModule({
        imports: [CreateRepertoireDialogComponent],
        providers: [
          provideNoopAnimations(),
          provideTranslateService({ fallbackLang: 'en' }),
          { provide: MatDialogRef, useValue: { close: () => {} } },
          { provide: MAT_DIALOG_DATA, useValue: null },
        ],
      });
      const f = TestBed.createComponent(CreateRepertoireDialogComponent);
      f.detectChanges();
      return f.nativeElement as HTMLElement;
    }

    it('der Hinweis zur Chessable-Kurs-ID nimmt Platz im Fluss ein, statt über die Checkbox zu laufen', () => {
      const el = render();
      const field = el.querySelector('mat-form-field.course-id');
      expect(field).withContext('Feld der Kurs-ID').not.toBeNull();
      // feste Unterzeile = Hinweis absolut positioniert und überlappt alles darunter
      expect(field!.querySelector('.mat-mdc-form-field-subscript-dynamic-size')).not.toBeNull();
      // und nachgemessen: der (mehrzeilige) Hinweis endet über der Checkbox „Öffentlich"
      const hint = field!.querySelector('mat-hint') as HTMLElement;
      const checkbox = el.querySelector('mat-checkbox[name="isPublic"]') as HTMLElement;
      hint.textContent = 'Numerische Kurs-ID aus der Chessable-URL (z. B. 12345). Verknüpft dieses Repertoire mit einem Chessable-Kurs.';
      (el.querySelector('.dialog-form') as HTMLElement).style.width = '300px';
      expect(hint.getBoundingClientRect().bottom).toBeLessThanOrEqual(checkbox.getBoundingClientRect().top);
    });

    it('der Inhalt ist der Scroll-Bereich und zeigt am Rand, dass noch etwas kommt', () => {
      const el = render();
      const content = el.querySelector('mat-dialog-content');
      expect(content?.classList).toContain('scroll-cue');
      // der Hinweis unter „Für Extension und Vorbereitung verwenden" steht IM Scroll-Bereich, nicht in der Knopfleiste
      expect(content?.querySelector('.ext-note')).not.toBeNull();
      expect(el.querySelector('mat-dialog-actions .ext-note')).toBeNull();
    });
  });
});
