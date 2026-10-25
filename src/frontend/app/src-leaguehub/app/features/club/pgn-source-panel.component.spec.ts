import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ClubClient } from '../../core/club-api.service';
import { ClubDraftStore } from './club-draft-store';
import { PgnSourcePanelComponent } from './pgn-source-panel.component';

describe('PgnSourcePanelComponent', () => {
  let fixture: ComponentFixture<PgnSourcePanelComponent>;
  let api: jasmine.SpyObj<ClubClient>;
  let store: ClubDraftStore;

  beforeEach(() => {
    api = jasmine.createSpyObj<ClubClient>('ClubClient', ['preview', 'createDraft', 'drafts', 'lichess', 'chessBase', 'deleteDraft']);
    store = new ClubDraftStore({ client: api, share: null, isManager: false, allDrafts: async () => [],
      savedGame: async () => ({ pgn: '' }), confirm: async () => true });
    spyOn(store, 'startPreview').and.resolveTo();
    TestBed.configureTestingModule({ imports: [PgnSourcePanelComponent] });
    fixture = TestBed.createComponent(PgnSourcePanelComponent);
    fixture.componentRef.setInput('store', store);
    fixture.detectChanges();
  });

  it('Text einfügen schaltet „Partien prüfen" frei, der Knopf startet die Übersicht', () => {
    const el = fixture.nativeElement as HTMLElement;
    const btn = el.querySelector('.btn-pri') as HTMLButtonElement;
    expect(btn.disabled).toBeTrue();
    expect(el.querySelector('.pick-reason')).not.toBeNull();
    const ta = el.querySelector('textarea') as HTMLTextAreaElement;
    ta.value = '1. e4 *';
    ta.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(store.pgn()).toBe('1. e4 *');
    expect(btn.disabled).toBeFalse();
    btn.click();
    expect(store.startPreview).toHaveBeenCalled();
  });

  it('eine PGN-Datei: Herkunft „datei" mit Dateinamen, Feld wieder leer', async () => {
    const input = { files: [new File(['1. d4 *'], 'liga.pgn')], value: 'C:\\fakepath\\liga.pgn' };
    const loaded = spyOn(store, 'loaded').and.callThrough();
    await fixture.componentInstance.pickFile({ target: input } as unknown as Event);
    expect(input.value).toBe('');
    expect(store.pgn()).toBe('1. d4 *');
    expect(loaded).toHaveBeenCalledWith('1. d4 *', 'datei', 'liga.pgn');
    expect(store.startPreview).toHaveBeenCalled();
    expect(store.readingFile()).toBeFalse();
  });

  it('Lichess-Studie: Absage als Satz; solange etwas läuft, startet kein zweiter Weg', async () => {
    store.studyUrl.set(' https://lichess.org/study/x ');
    api.lichess.and.rejectWith(new HttpErrorResponse({ status: 502 }));
    await fixture.componentInstance.loadStudy();
    expect(api.lichess).toHaveBeenCalledWith('https://lichess.org/study/x');
    expect(store.importError()).toBe('Die Studie ließ sich nicht laden.');
    store.previewing.set(true);
    await fixture.componentInstance.loadStudy();
    expect(api.lichess).toHaveBeenCalledTimes(1);
  });

  it('ChessBase ohne Kopfdatei: nichts hochgeladen, Hinweis', async () => {
    await fixture.componentInstance.pickChessBase({ target: { files: [new File(['x'], 'a.2cbg')], value: '' } } as unknown as Event);
    expect(api.chessBase).not.toHaveBeenCalled();
    expect(store.importError()).toContain('.cbh oder .2cbh');
  });
});
