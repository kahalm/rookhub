import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialog } from '@angular/material/dialog';
import { of, throwError } from 'rxjs';
import { RepertoireService } from '../../core/repertoire.service';
import { SnackbarService } from '../../core/snackbar.service';
import { EbookTreePanelComponent } from './ebook-tree-panel.component';
import { EbookOpeningTree, TREE_EXPORT_MAX_NODES } from './ebook-tree.util';
import { EbookTreeSaveDialogData, EbookTreeSaveDialogResult } from './ebook-tree-save-dialog.component';

describe('EbookTreePanelComponent', () => {
  // 1.e4 e5 2.d4 exd4 3.c3 (3.Nf3) dxc3
  const tree = new EbookOpeningTree([[0, 'e4'], [1, 'e5'], [2, 'd4'], [3, 'exd4'], [4, 'Nf3'], [4, 'c3'], [6, 'dxc3'], [6, 'd3']]);
  let repertoires: { create: jasmine.Spy; uploadFile: jasmine.Spy };
  let snackbar: { show: jasmine.Spy; warn: jasmine.Spy };
  let dialogResult: EbookTreeSaveDialogResult | undefined;
  let dialogData: EbookTreeSaveDialogData | undefined;

  function make(cursor: number, canSave = true, t = tree): ComponentFixture<EbookTreePanelComponent> {
    repertoires = { create: jasmine.createSpy('create').and.returnValue(of({ id: 77 })), uploadFile: jasmine.createSpy('upload').and.returnValue(of({})) };
    snackbar = { show: jasmine.createSpy('show').and.returnValue({ onAction: () => of() }), warn: jasmine.createSpy('warn') };
    TestBed.configureTestingModule({
      imports: [EbookTreePanelComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: RepertoireService, useValue: repertoires },
        { provide: SnackbarService, useValue: snackbar },
        { provide: MatDialog, useValue: { open: (_: unknown, cfg: { data: EbookTreeSaveDialogData }) => { dialogData = cfg.data; return { afterClosed: () => of(dialogResult) }; } } },
      ],
    });
    const f = TestBed.createComponent(EbookTreePanelComponent);
    f.componentRef.setInput('tree', t);
    f.componentRef.setInput('cursor', cursor);
    f.componentRef.setInput('bookTitle', 'Danish Dynamite');
    f.componentRef.setInput('canSave', canSave);
    f.detectChanges();
    return f;
  }

  it('zeigt den Weg ab der Grundstellung (aktueller Zug getönt) und die Fortsetzungen mit Linienzahl, meistverzweigte zuerst', () => {
    const f = make(4);
    const el: HTMLElement = f.nativeElement;
    const steps = Array.from(el.querySelectorAll('.etp-step')).map(b => b.textContent!.replace(/\s+/g, ' ').trim());
    expect(steps).toEqual(['1.e4', 'e5', '2.d4', 'exd4']);
    expect(el.querySelector('.etp-step--on')?.textContent).toContain('exd4');
    const kids = Array.from(el.querySelectorAll('.etp-child'));
    expect(kids.map(k => k.querySelector('.etp-child-move')!.textContent!.trim())).toEqual(['3.c3', '3.Nf3']);
    expect(kids[0].querySelector('.etp-lines')!.textContent).toContain('ebooks.tree.lines');
    expect(kids[1].querySelector('.etp-lines')!.textContent).toContain('ebooks.tree.linesOne');
  });

  it('Klicks melden den Zielknoten; ◀ führt zum Elternknoten, ▶ zur Hauptfortsetzung', () => {
    const f = make(4);
    const go = jasmine.createSpy('go');
    f.componentInstance.go.subscribe(go);
    const el: HTMLElement = f.nativeElement;
    (el.querySelectorAll('.etp-step')[1] as HTMLButtonElement).click();
    expect(go).toHaveBeenCalledWith(2);
    (el.querySelector('.etp-child') as HTMLButtonElement).click();
    expect(go).toHaveBeenCalledWith(6);
    (el.querySelector('.etp-back') as HTMLButtonElement).click();
    expect(go).toHaveBeenCalledWith(3);
    (el.querySelector('.etp-forward') as HTMLButtonElement).click();
    expect(go.calls.mostRecent().args).toEqual([6]);
  });

  it('Grundstellung, Buchende und „nicht im Baum“', () => {
    const f = make(0);
    const el: HTMLElement = f.nativeElement;
    expect(el.querySelector('.etp-start')?.textContent).toContain('analysis.setup.startpos');
    expect(el.querySelector('.etp-not-in-tree')).toBeNull();
    f.componentRef.setInput('notInTree', true);
    f.componentRef.setInput('cursor', 5);
    f.detectChanges();
    expect(el.querySelector('.etp-not-in-tree')?.textContent).toContain('ebooks.tree.notInTree');
    expect(el.textContent).toContain('ebooks.tree.end');
  });

  it('Speichern nur mit Konto', () => {
    expect(make(4, false).nativeElement.querySelector('.etp-save')).toBeNull();
  });

  it('„Als Repertoire speichern“: Dialog mit Vorschlag, legt das Repertoire über den Repertoire-Dienst an und lädt das PGN hoch', async () => {
    dialogResult = { name: 'Dänisch c3', color: 'w', scope: 'here' };
    const f = make(6);
    (f.nativeElement.querySelector('.etp-save') as HTMLButtonElement).click();
    expect(dialogData!.name).toBe('Danish Dynamite – 2. d4 exd4 3. c3');
    expect(dialogData!.canHere).toBeTrue();
    expect(dialogData!.counts).toEqual({ all: 8, here: 7 });
    expect(repertoires.create).toHaveBeenCalledWith(jasmine.objectContaining({ name: 'Dänisch c3', isPublic: false, kind: 1 }));
    const [id, form] = repertoires.uploadFile.calls.mostRecent().args as [number, FormData];
    expect(id).toBe(77);
    const file = form.get('file') as File;
    expect(file.name).toBe('buchbaum.pgn');
    const text = await file.text();
    expect(text).toContain('1. e4 e5 2. d4 exd4 3. c3 dxc3 (3... d3) *');
    expect(text).not.toContain('Nf3');
    expect(snackbar.show).toHaveBeenCalled();
  });

  it('Fehler beim Anlegen → Warnung; zu großer Baum → Hinweis statt Anlegen', fakeAsync(() => {
    dialogResult = { name: 'X', color: 'b', scope: 'all' };
    let f = make(0);
    repertoires.create.and.returnValue(throwError(() => ({ status: 500 })));
    f.componentInstance.save();
    tick();
    expect(snackbar.warn).toHaveBeenCalledWith('ebooks.tree.saveFailed');
    expect(f.componentInstance.saving()).toBeFalse();

    TestBed.resetTestingModule();
    const big = new EbookOpeningTree(Array.from({ length: TREE_EXPORT_MAX_NODES + 1 }, (_, i) => [i, i % 4 < 2 ? (i % 2 ? 'Nf6' : 'Nf3') : (i % 2 ? 'Ng8' : 'Ng1')] as [number, string]));
    f = make(0, true, big);
    f.componentInstance.save();
    tick();
    expect(snackbar.warn).toHaveBeenCalledWith('ebooks.tree.tooLarge');
    expect(repertoires.create).not.toHaveBeenCalled();
  }));
});
