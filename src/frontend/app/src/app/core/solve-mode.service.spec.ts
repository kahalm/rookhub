import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { SolveModeService } from './solve-mode.service';
import { PreferencesService } from './preferences.service';

describe('SolveModeService', () => {
  let svc: SolveModeService;
  let prefs: PreferencesService;
  let dialogOpen: jasmine.Spy;

  /** `null` = der Dialog schließt OHNE Wahl (nicht `undefined` — das würde den
   *  Default-Parameter greifen lassen). `applyAll` = Häkchen „für alle Puzzle-Bereiche“. */
  function setup(antwort: 'training' | 'easy' | null = 'training', applyAll = false): void {
    localStorage.removeItem('rookhub_solve_modes');
    dialogOpen = jasmine.createSpy('open')
      .and.returnValue({ afterClosed: () => of(antwort ? { mode: antwort, applyAll } : undefined) });
    TestBed.configureTestingModule({
      providers: [{ provide: MatDialog, useValue: { open: dialogOpen } }],
    });
    svc = TestBed.inject(SolveModeService);
    prefs = TestBed.inject(PreferencesService);
  }

  afterEach(() => localStorage.removeItem('rookhub_solve_modes'));

  it('fragt beim ersten Mal und merkt sich die Wahl', () => {
    setup('easy');
    const gesehen: string[] = [];
    svc.ensure('puzzles').subscribe(m => gesehen.push(m));
    expect(dialogOpen).toHaveBeenCalledTimes(1);
    expect(gesehen).toEqual(['easy']);
    expect(svc.get('puzzles')).toBe('easy');
  });

  it('fragt beim zweiten Mal NICHT mehr', () => {
    setup('easy');
    svc.ensure('puzzles').subscribe();
    const gesehen: string[] = [];
    svc.ensure('puzzles').subscribe(m => gesehen.push(m));
    expect(dialogOpen).toHaveBeenCalledTimes(1);
    expect(gesehen).toEqual(['easy']);
  });

  it('fragt je Bereich getrennt', () => {
    setup('training');
    svc.ensure('puzzles').subscribe();
    svc.ensure(SolveModeService.scopeCourse(7)).subscribe();
    svc.ensure(SolveModeService.scopeCourse(8)).subscribe();
    expect(dialogOpen).toHaveBeenCalledTimes(3);
    svc.ensure(SolveModeService.scopeCourse(7)).subscribe();
    expect(dialogOpen).toHaveBeenCalledTimes(3);   // Kurs 7 ist beantwortet
  });

  // Ohne echte Wahl darf nichts gemerkt werden, sonst fragt die App nie wieder, obwohl der
  // Nutzer sich nie entschieden hat.
  it('merkt sich nichts, wenn der Dialog ohne Wahl schließt', () => {
    setup(null);
    const gesehen: string[] = [];
    svc.ensure('puzzles').subscribe(x => gesehen.push(x));
    expect(gesehen).toEqual(['training']);  // Rückfall auf das bisherige Verhalten
    expect(svc.get('puzzles')).toBeNull(); // aber nicht gemerkt
  });

  it('einfach ist Stufe 0, Training behält die eingestellte Stufe', () => {
    setup();
    prefs.visualization = 3;
    expect(svc.levelFor('easy')).toBe(0);
    expect(svc.levelFor('training')).toBe(3);
  });

  // Wer global auf „Normal" steht und hier Training wählt, muss mindestens Blindspiel bekommen —
  // sonst wäre Training identisch mit Einfach.
  it('Training ist mindestens Stufe 1, auch wenn global 0 eingestellt ist', () => {
    setup();
    prefs.visualization = 0;
    expect(svc.levelFor('training')).toBe(1);
  });

  it('modeForLevel leitet die Spielweise aus einer Stufe ab', () => {
    setup();
    expect(svc.modeForLevel(0)).toBe('easy');
    expect(svc.modeForLevel(1)).toBe('training');
    expect(svc.modeForLevel(4)).toBe('training');
  });

  it('clear lässt den Bereich wieder fragen', () => {
    setup('easy');
    svc.ensure('puzzles').subscribe();
    svc.clear('puzzles');
    expect(svc.get('puzzles')).toBeNull();
    svc.ensure('puzzles').subscribe();
    expect(dialogOpen).toHaveBeenCalledTimes(2);
  });

  it('übersteht kaputten Speicherinhalt', () => {
    setup('training');
    localStorage.setItem('rookhub_solve_modes', 'kein json');
    expect(svc.get('puzzles')).toBeNull();
    expect(() => svc.set('puzzles', 'easy')).not.toThrow();
    expect(svc.get('puzzles')).toBe('easy');
  });

  // Pro Kurs entsteht ein Eintrag; ohne Deckel wüchse der Speicher unbegrenzt.
  it('deckelt die Zahl der gemerkten Bereiche und wirft die ältesten weg', () => {
    setup('training');
    for (let i = 0; i < 210; i++) svc.set(SolveModeService.scopeCourse(i), 'easy');
    const alle = JSON.parse(localStorage.getItem('rookhub_solve_modes') || '{}');
    expect(Object.keys(alle).length).toBe(200);
    expect(svc.get(SolveModeService.scopeCourse(0))).toBeNull();     // ältester ist weg
    expect(svc.get(SolveModeService.scopeCourse(209))).toBe('easy'); // jüngster ist da
  });

  // UX-004: Der Dialog fokussierte die erste Karte („Trainingsmodus“) — sie sah vorausgewählt aus.
  it('öffnet den Dialog mit Fokus auf dem Dialog, nicht auf der ersten Karte', () => {
    setup('easy');
    svc.ensure('daily').subscribe();
    expect(dialogOpen).toHaveBeenCalledWith(jasmine.anything(), jasmine.objectContaining({ autoFocus: 'dialog' }));
  });

  // UX-004: dieselbe Frage kam in Puzzles, Tagespuzzle, Buch-Puzzles und Endless je einzeln.
  it('„für alle Puzzle-Bereiche“: die übrigen Bereiche fragen nicht mehr', () => {
    setup('easy', true);
    svc.ensure('daily').subscribe();
    const gesehen: string[] = [];
    for (const scope of ['puzzles', 'book', 'endless']) svc.ensure(scope).subscribe(m => gesehen.push(m));
    expect(dialogOpen).toHaveBeenCalledTimes(1);
    expect(gesehen).toEqual(['easy', 'easy', 'easy']);
  });

  it('ohne Häkchen gilt die Wahl nur für den eigenen Bereich', () => {
    setup('easy', false);
    svc.ensure('daily').subscribe();
    expect(svc.get('puzzles')).toBeNull();
  });

  it('bietet „für alle“ nur in Bereichen ohne Id an — Kurse fragen weiter einzeln', () => {
    setup('training', true);
    svc.ensure('daily').subscribe();
    expect(dialogOpen.calls.mostRecent().args[1].data.offerApplyAll).toBeTrue();
    svc.ensure(SolveModeService.scopeCourse(7)).subscribe();
    expect(dialogOpen).toHaveBeenCalledTimes(2);              // Kurs fragt trotz Grundwahl
    expect(dialogOpen.calls.mostRecent().args[1].data.offerApplyAll).toBeFalse();
  });

  it('ein Häkchen im Kurs setzt keine Grundwahl', () => {
    setup('easy', true);
    svc.ensure(SolveModeService.scopeCourse(7)).subscribe();
    expect(svc.get('puzzles')).toBeNull();
  });

  it('die eigene Wahl eines Bereichs schlägt die Grundwahl', () => {
    setup('training', true);
    svc.ensure('puzzles').subscribe();
    svc.set('endless', 'easy');                               // in Endless umgeschaltet
    expect(svc.get('endless')).toBe('easy');
    expect(svc.get('daily')).toBe('training');
  });

  it('die Grundwahl fliegt beim Deckeln nicht raus', () => {
    setup('easy', true);
    svc.ensure('puzzles').subscribe();
    for (let i = 0; i < 210; i++) svc.set(SolveModeService.scopeCourse(i), 'training');
    expect(svc.get('daily')).toBe('easy');
  });
});
