import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { EbookEditDialogComponent, EbookEditDialogData } from './ebook-edit-dialog.component';

describe('EbookEditDialogComponent', () => {
  async function make(data: EbookEditDialogData) {
    await TestBed.configureTestingModule({
      imports: [EbookEditDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(EbookEditDialogComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('Hochladen braucht Datei UND Titel; der Titel kommt aus dem Dateinamen', async () => {
    const c = await make({ ebook: null, groups: [], maxFileBytes: 10_000 });
    expect(c.isNew).toBeTrue();
    expect(c.canSave).toBeFalse();
    const file = new File([new Uint8Array(10)], 'My_Endgame_Book.pdf');
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [file] });
    c.onFile({ target: input } as unknown as Event);
    expect(c.file).toBe(file);
    expect(c.title).toBe('My Endgame Book');
    expect(c.canSave).toBeTrue();
  });

  it('eine zu große Datei wird nicht übernommen', async () => {
    const c = await make({ ebook: null, groups: [], maxFileBytes: 5 });
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [new File([new Uint8Array(10)], 'big.pdf')] });
    c.onFile({ target: input } as unknown as Event);
    expect(c.file).toBeNull();
  });

  it('Bearbeiten übernimmt die Felder und die freigegebenen Gruppen', async () => {
    const c = await make({
      ebook: {
        id: 3, title: 'T', author: 'A', year: 1999, tags: ['x', 'y'], description: 'd', targetMinElo: null, source: 'upload',
        pageCount: null, hasCover: false, diagramState: 'unsupported', diagramCount: 0, ownerUserId: 1, ownerName: null, isOwner: true, canManage: true, groupIds: [7],
        files: [], primaryFormat: null, progress: null, createdAt: '', updatedAt: '',
      },
      groups: [{ id: 7, name: 'G', isEveryone: false }], maxFileBytes: 1,
    });
    expect(c.isNew).toBeFalse();
    expect(c.tags).toBe('x, y');
    expect(c.selected.has(7)).toBeTrue();
    c.toggle(7, false);
    expect(c.selected.size).toBe(0);
    expect(c.canSave).toBeTrue();
  });
});
