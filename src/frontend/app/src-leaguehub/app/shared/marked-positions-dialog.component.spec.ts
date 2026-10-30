import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { ReplayMark } from '@rh/shared/player-card/game-replay.component';
import { LineupsApiService } from '../core/lineups';
import { MarkedPositionsDialogComponent, openMarkedPositions } from './marked-positions-dialog.component';
import { of } from 'rxjs';

/** „+" in LeagueHub (0.862.0, Wunsch 2026-10-27): Diagramm, wer am Zug, Klassifizierung + Bemerkung, ‹ › durch die Stellungen. */
describe('MarkedPositionsDialogComponent', () => {
  const MAIN: ReplayMark = { fen: 'rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2', positionKey: 'k1', ply: 3,
    variationFromPly: null, variationUci: null, count: 2, tags: ['tactic', 'Motiv'], comments: ['von b'], myTags: ['tactic'],
    myComment: 'meins', mine: true };
  const VAR: ReplayMark = { fen: 'rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 1 2', positionKey: 'k2', ply: null,
    variationFromPly: 1, variationUci: 'c7c5 g1f3', count: 1, tags: ['opening'], comments: [], myTags: [], myComment: null, mine: false };

  async function setup(api: Partial<LineupsApiService>, games = [{ clubGameId: 9, subtitle: 'A – B' }]) {
    const ref = { close: jasmine.createSpy('close') };
    TestBed.configureTestingModule({
      imports: [MarkedPositionsDialogComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), { provide: LineupsApiService, useValue: api },
        { provide: MAT_DIALOG_DATA, useValue: { games } }, { provide: MatDialogRef, useValue: ref }],
    });
    const fixture = TestBed.createComponent(MarkedPositionsDialogComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, ref, c: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
  }

  it('Partie-Stellung: Überschrift, wer am Zug, eigene Notizen, fremde Bemerkung; › zur Variante', async () => {
    const { fixture, c, el } = await setup({ clubGameMarks: () => Promise.resolve([MAIN, VAR]) });
    const text = () => el.textContent!.replace(/\s+/g, ' ');
    expect(text()).toContain('Interessante Stellung aus Partie');
    expect(text()).toContain('Schwarz am Zug');
    expect(text()).toContain('nach Zug 2');
    expect(text()).toContain('1 / 2');
    expect(text()).toContain('von b');
    expect(c.tags()).toEqual(['tactic']);
    expect(c.comment()).toBe('meins');
    expect(el.querySelector('app-chess-board')).not.toBeNull();

    (el.querySelectorAll<HTMLButtonElement>('.mp-nav')[1]).click();
    fixture.detectChanges();
    expect(text()).toContain('Interessante Stellung aus Variante');
    expect(text()).toContain('Weiß am Zug');
    expect(text()).toContain('Variante ab Zug 1');
    expect(c.tags()).toEqual([]);
    expect((el.querySelectorAll<HTMLButtonElement>('.mp-nav')[1]).disabled).toBeTrue();
  });

  // 0.865.0, Wunsch 2026-10-28: „zeig beim Klicken auf + normal die hinterlegte Klassifizierung + Kommentar, irgendwo klein ein
  // Bleistift zum Bearbeiten, dann stehen alle zum Auswählen + Edit für Kommentar"
  it('erst lesen (Schlagworte + Bemerkungen, kein Speichern), Bleistift → alle Schlagworte, Textfeld und Speichern', async () => {
    const { fixture, el } = await setup({ clubGameMarks: () => Promise.resolve([MAIN, VAR]) });
    const chips = () => [...el.querySelectorAll('.mn-chip')].map(x => x.textContent!.trim());
    expect(chips()).toEqual(['markPosition.tag.tactic', 'Motiv']);
    expect(el.querySelector('.mn-mine')?.textContent).toBe('meins');
    expect(el.querySelector('textarea')).toBeNull();
    expect(el.querySelector('.mp-save')).toBeNull();
    (el.querySelector('.mn-pencil') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(chips().length).toBeGreaterThan(14);
    expect(el.querySelector('textarea')).not.toBeNull();
    expect(el.querySelector('.mp-save')).not.toBeNull();
    (el.querySelectorAll<HTMLButtonElement>('.mp-nav')[1]).click();      // nächste Stellung wieder zum Lesen
    fixture.detectChanges();
    expect(el.querySelector('textarea')).toBeNull();
    expect(chips()).toEqual(['markPosition.tag.opening']);
  });

  // d9-l-plus-fenster-speichern-abstand: gesperrtes Speichern nennt den Grund, ist Hauptknopf, „Schließen“ mit 8 px Abstand
  it('Bearbeiten: „Speichern“ gesperrt mit Grund, Hauptknopf, Abstand zu „Schließen“; Grund weg, sobald etwas geändert ist', async () => {
    const { fixture, c, el } = await setup({ clubGameMarks: () => Promise.resolve([MAIN]) });
    (el.querySelector('.mn-pencil') as HTMLButtonElement).click();
    fixture.detectChanges();
    const save = el.querySelector('.mp-save') as HTMLButtonElement;
    expect(save.disabled).toBeTrue();
    expect(save.classList).toContain('btn-pri');
    expect(el.querySelector('#mp-why')?.textContent).toContain('erst eine Klassifizierung oder Bemerkung ändern');
    expect(save.getAttribute('aria-describedby')).toBe('mp-why');
    expect(getComputedStyle(el.querySelector('.mp-actions')!).columnGap).toBe('8px');
    expect(getComputedStyle(save).cursor).not.toBe('progress');
    c.comment.set('anders');
    fixture.detectChanges();
    expect((el.querySelector('.mp-save') as HTMLButtonElement).disabled).toBeFalse();
    expect(el.querySelector('#mp-why')).toBeNull();
    expect(el.querySelector('.mp-save')!.getAttribute('aria-describedby')).toBeNull();
  });

  it('Speichern schickt die eigenen Notizen und bleibt auf der Stellung; „In der Partie zeigen" schließt mit ihr', async () => {
    const saved = { ...VAR, myTags: ['trap'], myComment: 'neu', mine: true, count: 2 };
    const put = jasmine.createSpy('put').and.returnValue(Promise.resolve([MAIN, saved]));
    const { fixture, c, ref, el } = await setup({ clubGameMarks: () => Promise.resolve([MAIN, VAR]), setClubGameMarkNotes: put });
    c.go(1);
    c.tags.set(['trap']);
    c.comment.set(' neu ');
    fixture.detectChanges();
    await c.save();
    expect(put).toHaveBeenCalledWith(9, VAR.fen, ['trap'], 'neu');
    expect(c.index()).toBe(1);
    expect(c.dirty()).toBeFalse();
    fixture.detectChanges();
    expect(el.textContent).toContain('Gespeichert.');

    c.show(c.current()!);
    expect(ref.close).toHaveBeenCalledWith({ clubGameId: 9, mark: saved });
  });

  // Nachtrag (Wunsch 2026-10-27: „je Runde und je Liga +30, Klick bringt diese 30 Stellungen, jeweils mit ‚in der Partie zeigen'").
  it('mehrere Partien: Stellungen hintereinander, Untertitel je Partie, Speichern frischt nur die eigene Partie auf', async () => {
    const byGame: Record<number, ReplayMark[]> = { 9: [MAIN], 11: [VAR] };
    const put = jasmine.createSpy('put').and.callFake((id: number) => Promise.resolve([{ ...byGame[id][0], myTags: ['trap'] }]));
    const { fixture, c, ref, el } = await setup({ clubGameMarks: (id: number) => Promise.resolve(byGame[id]), setClubGameMarkNotes: put },
      [{ clubGameId: 9, subtitle: 'Runde 1 · Brett 1' }, { clubGameId: 11, subtitle: 'Runde 2 · Brett 3' }]);
    expect(el.textContent).toContain('Runde 1 · Brett 1');
    expect(el.textContent).toContain('1 / 2');
    c.go(1);
    fixture.detectChanges();
    expect(el.textContent).toContain('Runde 2 · Brett 3');
    c.tags.set(['trap']);
    await c.save();
    expect(put).toHaveBeenCalledWith(11, VAR.fen, ['trap'], null);
    expect(c.items().map(i => i.mark.myTags)).toEqual([['tactic'], ['trap']]);
    c.show(c.current()!);
    expect(ref.close).toHaveBeenCalledWith(jasmine.objectContaining({ clubGameId: 11 }));
  });

  // d8-l-plus-dialog-fremde-flaeche: bei nur einer Stellung keine blassen, nutzlosen ‹ ›; der Titel bleibt mittig
  it('eine Stellung: keine Blätterpfeile, zwei Platzhalter', async () => {
    const { el } = await setup({ clubGameMarks: () => Promise.resolve([MAIN]) });
    expect(el.querySelectorAll('.mp-nav').length).toBe(0);
    expect(el.querySelectorAll('.mp-nav-slot').length).toBe(2);
    expect(el.querySelector('.mp-board')).not.toBeNull();
  });

  it('Ladefehler → Meldung statt leerer Liste', async () => {
    const { el } = await setup({ clubGameMarks: () => Promise.reject(new Error('x')) });
    expect(el.querySelector('[role=alert]')?.textContent).toContain('nicht geladen');
  });

  // 0.866.0: „In der Partie zeigen" stand einen Halbzug zu weit (markiert nach 6…O-O, Nachspielen auf 7.Dc2)
  it('„In der Partie zeigen": Halbzug der Marke → Zug-Index des Nachspielens (ply − 1), Variante → erste Marke', async () => {
    const open = (mark: ReplayMark) => ({ open: () => ({ afterClosed: () => of({ clubGameId: 9, mark }) }) }) as any;
    expect(await openMarkedPositions(open(MAIN), [])).toEqual({ clubGameId: 9, start: { kind: 'ply', ply: 2 } });
    expect(await openMarkedPositions(open({ ...MAIN, ply: 0 }), [])).toEqual({ clubGameId: 9, start: { kind: 'ply', ply: -1 } });
    expect(await openMarkedPositions(open(VAR), [])).toEqual({ clubGameId: 9, start: { kind: 'mark' } });
  });

  it('öffnet mit panelClass lh-dialog (LeagueHub-Fläche statt Material-Grau, d8)', async () => {
    const dialog = { open: jasmine.createSpy('open').and.returnValue({ afterClosed: () => of(undefined) }) };
    await openMarkedPositions(dialog as never, []);
    expect(dialog.open.calls.mostRecent().args[1]).toEqual(jasmine.objectContaining({ panelClass: 'lh-dialog' }));
  });
});
