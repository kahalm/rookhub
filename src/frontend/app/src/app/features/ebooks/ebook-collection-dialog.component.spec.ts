import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { EbookCollectionDialogComponent } from './ebook-collection-dialog.component';
import { EbookService } from './ebook.service';

/** d8-rk-sammlung-speichern-ohne-grund / d8-rk-sammlung-dialog-speichern-ohne-grund (DESIGN 2 Nr. 8, 5.6). */
describe('EbookCollectionDialogComponent', () => {
  function make(collections = [{ id: 3, name: 'Kongress', bookCount: 2 }]) {
    TestBed.configureTestingModule({
      imports: [EbookCollectionDialogComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { ebookIds: [1, 2] } },
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: EbookService, useValue: { collections: () => of(collections), assignCollection: () => of(null) } },
        { provide: SnackbarService, useValue: { warn: () => {} } },
      ],
    });
    const fixture = TestBed.createComponent(EbookCollectionDialogComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const save = () => Array.from(el.querySelectorAll<HTMLButtonElement>('mat-dialog-actions button')).pop()!;
    return { fixture, c: fixture.componentInstance, el, save };
  }
  afterEach(() => TestBed.resetTestingModule());

  it('leerer Name: „Speichern“ gesperrt, der Grund steht links in der Knopfleiste, das Feld trägt das Sternchen', () => {
    const { fixture, c, el, save } = make();
    expect(save().disabled).toBeTrue();
    const why = el.querySelector('mat-dialog-actions .why');
    expect(why?.textContent).toContain('ebooks.collection.needName');
    expect(why?.getAttribute('role')).toBe('status');
    expect(el.querySelector<HTMLInputElement>('mat-form-field input')!.required).toBeTrue();
    expect(el.querySelector('.mat-mdc-form-field-required-marker')).not.toBeNull();

    c.name = 'Neu';
    fixture.detectChanges();
    expect(el.querySelector('mat-dialog-actions .why')).toBeNull();
    expect(save().disabled).toBeFalse();
  });

  it('bestehende Sammlung ohne Auswahl nennt „Erst eine Sammlung wählen.“', () => {
    const { fixture, c, el } = make();
    c.mode = 'existing';
    fixture.detectChanges();
    expect(c.blockReason).toBe('ebooks.collection.needPick');
    expect(el.querySelector('mat-dialog-actions .why')?.textContent).toContain('ebooks.collection.needPick');
    expect(el.querySelector('mat-select')!.getAttribute('aria-required')).toBe('true');
    c.collectionId = 3;
    fixture.detectChanges();
    expect(c.blockReason).toBeNull();
  });

  it('Hinweis im Ton von mat-hint statt abgeblasst (DESIGN 5.6)', () => {
    const { el } = make();
    document.body.appendChild(el);
    try {
      const hint = el.querySelector<HTMLElement>('.hint')!;
      expect(getComputedStyle(hint).opacity).toBe('1');
    } finally { el.remove(); }
  });
});
