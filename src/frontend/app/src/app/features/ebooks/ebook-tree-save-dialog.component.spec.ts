import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { EbookTreeSaveDialogComponent, EbookTreeSaveDialogData } from './ebook-tree-save-dialog.component';

describe('EbookTreeSaveDialogComponent', () => {
  function make(data: EbookTreeSaveDialogData) {
    const close = jasmine.createSpy('close');
    TestBed.configureTestingModule({
      imports: [EbookTreeSaveDialogComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close } }, { provide: MAT_DIALOG_DATA, useValue: data }],
    });
    const f = TestBed.createComponent(EbookTreeSaveDialogComponent);
    f.detectChanges();
    return { c: f.componentInstance, el: f.nativeElement as HTMLElement, close };
  }
  const data: EbookTreeSaveDialogData = { name: 'Buch – 3. c3', canHere: true, colors: { all: 'w', here: 'b' }, counts: { all: 900, here: 40 } };

  it('Vorgabe „ab dieser Stellung“; die Farbe folgt dem Umfang, bis man sie selbst wählt', () => {
    const { c, close } = make(data);
    expect(c.scope()).toBe('here');
    expect(c.color()).toBe('b');
    c.setScope('all');
    expect(c.color()).toBe('w');
    c.setColor('b');
    c.setScope('here');
    c.setScope('all');
    expect(c.color()).toBe('b');
    c.submit();
    expect(close).toHaveBeenCalledWith({ name: 'Buch – 3. c3', color: 'b', scope: 'all' });
  });

  it('an der Grundstellung kein Umfang-Schalter; leerer Name speichert nicht', () => {
    const { c, el, close } = make({ ...data, canHere: false });
    expect(el.querySelector('.ets-scope')).toBeNull();
    expect(c.scope()).toBe('all');
    c.name = '  ';
    c.submit();
    expect(close).not.toHaveBeenCalled();
  });
});
