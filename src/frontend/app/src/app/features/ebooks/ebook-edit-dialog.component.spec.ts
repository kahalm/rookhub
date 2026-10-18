import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
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

  it('c1-eb-upload-disabled-no-reason: der gesperrte Knopf nennt, was fehlt — erst die Datei, dann der Titel', async () => {
    const c = await make({ ebook: null, groups: [], maxFileBytes: 10_000 });
    const fixture = TestBed.createComponent(EbookEditDialogComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(fixture.componentInstance.blockReason).toBe('ebooks.dialog.needFile');
    expect(el.querySelector('.why')?.textContent).toContain('ebooks.dialog.needFile');
    c.file = new File([new Uint8Array(1)], 'a.pdf');
    c.title = '  ';
    expect(c.blockReason).toBe('ebooks.dialog.needTitle');
    c.title = 'A';
    expect(c.blockReason).toBeNull();
    // Bearbeiten ohne Datei: nur der Titel zählt.
    TestBed.resetTestingModule();
    const edit = await make({
      ebook: {
        id: 3, title: '', author: '', year: null, tags: [], description: null, targetMinElo: null, source: 'upload',
        pageCount: null, hasCover: false, diagramState: 'unsupported', diagramCount: 0, ownerUserId: 1, ownerName: null, isOwner: true, canManage: true, groupIds: [],
        files: [], primaryFormat: null, progress: null, createdAt: '', updatedAt: '',
      },
      groups: [], maxFileBytes: 1,
    });
    expect(edit.blockReason).toBe('ebooks.dialog.needTitle');
  });

  it('c1-eb-upload-disabled-no-reason: Scrollkante sichtbar, Autor + Jahr am Handy nebeneinander', () => {
    const css = (((EbookEditDialogComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
    expect(css).toMatch(/mat-dialog-content(\[[^\]]*\])?\{[^}]*border-bottom:1pxsolidvar\(--mat-sys-outline-variant\)/);
    expect(css.slice(css.indexOf('@media(max-width:480px)'))).toMatch(/\.two(\[[^\]]*\])?\{grid-template-columns:1frminmax\(88px,110px\)/);
  });
  it('c2-eb-upload-limit-315-300: Grenze oben und im Hinweis ohne Konto aus derselben Zahl (300 MiB = „300 MB“)', async () => {
    await make({ ebook: null, groups: [], maxFileBytes: 300 * 1024 * 1024, anonymous: true });
    const fixture = TestBed.createComponent(EbookEditDialogComponent);
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { ebooks: { dialog: { fileHint: 'up to {{max}}' }, anonymous: { uploadHint: 'device ({{max}})' } } });
    translate.use('en');
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('p.hint')?.textContent?.trim()).toBe('up to 300 MB');
    expect(el.querySelector('.anon-hint')?.textContent?.trim()).toBe('person_outlinedevice (300 MB)');
  });

  it('c2-eb-upload-jahr-drehpfeile: Jahr ohne Drehpfeile (WebKit) und mit Ziffernblock', async () => {
    await make({ ebook: null, groups: [], maxFileBytes: 1 });
    const fixture = TestBed.createComponent(EbookEditDialogComponent);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.year input')?.getAttribute('inputmode')).toBe('numeric');
    const css = (((EbookEditDialogComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
    expect(css).toMatch(/\.year(\[[^\]]*\])?input(\[[^\]]*\])?::-webkit-inner-spin-button/);
    expect(css).toMatch(/::-webkit-inner-spin-button[^{]*\{[^}]*appearance:none/);
    expect(css).toMatch(/\.year(\[[^\]]*\])?input(\[[^\]]*\])?\{[^}]*appearance:textfield/);
  });

  it('d3-rk-ebook-upload-hint-ueberlappt-feld: Hinweis zum Brett-Modus wächst mit (auch zweizeilig) und liegt nicht auf „Beschreibung“', async () => {
    await make({ ebook: null, groups: [], maxFileBytes: 1 });
    const fixture = TestBed.createComponent(EbookEditDialogComponent);
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { ebooks: { meta: { boardModeHint: 'Which mode the board next to the book starts with (default: solve). '.repeat(2) } } });
    translate.use('en');
    const el: HTMLElement = fixture.nativeElement;
    el.style.display = 'block';
    el.style.width = '300px';
    document.body.appendChild(el);
    fixture.detectChanges();
    await fixture.whenStable();
    const field = el.querySelector<HTMLElement>('mat-form-field.board-mode')!;
    expect(field.querySelector('.mat-mdc-form-field-subscript-dynamic-size')).not.toBeNull();
    const hint = field.querySelector<HTMLElement>('mat-hint')!;
    expect(hint.getBoundingClientRect().height).toBeGreaterThan(20);   // wirklich zweizeilig
    const next = field.nextElementSibling as HTMLElement;
    expect(next.tagName.toLowerCase()).toBe('mat-form-field');
    expect(next.getBoundingClientRect().top - hint.getBoundingClientRect().bottom).toBeGreaterThanOrEqual(8);
    fixture.destroy();
  });
});

