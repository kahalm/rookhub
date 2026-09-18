import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { WorksheetComponent, isQuizLine, toSheets, WorksheetTask } from './worksheet.component';
import { CourseService } from '../course.service';
import { BookPuzzleDto } from '../../puzzles/puzzle.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
// Stellung mit Schwarz am Zug + Vorspielzug: die GEFRAGTE Stellung ist die nach 10…Sd4.
const BLACK_TO_MOVE = 'r1bqk2r/1ppp1ppp/p1n3n1/3Np2Q/2B1P3/3P4/PPP2PP1/R1B1K2R b KQkq - 0 10';

function puzzle(id: number, over: Partial<BookPuzzleDto> = {}): BookPuzzleDto {
  return {
    id, lineId: `b.pgn:${id}`, bookFileName: 'b.pgn', round: String(id),
    fen: START, moves: 'e2e4 e7e5', startPly: -1, ...over,
  } as BookPuzzleDto;
}

function make(puzzles: BookPuzzleDto[], query: Record<string, string> = {}, marks: number[] = []) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [WorksheetComponent],
    providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      { provide: CourseService, useValue: {
        getBookPuzzles: () => of(puzzles),
        getFlashcardMarks: () => of({ lineIds: marks }),
        getCourses: () => of([{ bookId: 58, displayName: 'Chess Olympiad 2026' }]),
      } },
      { provide: ActivatedRoute, useValue: { snapshot: {
        paramMap: { get: (k: string) => k === 'bookId' ? '58' : null, has: (k: string) => k === 'bookId' },
        queryParamMap: { get: (k: string) => k in query ? query[k] : null, has: (k: string) => k in query },
      } } },
    ],
  });
  const fixture = TestBed.createComponent(WorksheetComponent);
  fixture.detectChanges();
  return fixture;
}

describe('worksheet helpers', () => {
  it('nimmt nur abgefragte Linien (keine Info-Seiten, keine zuglosen Stellungen)', () => {
    expect(isQuizLine(puzzle(1))).toBeTrue();
    expect(isQuizLine(puzzle(2, { isInfoOnly: true }))).toBeFalse();
    expect(isQuizLine(puzzle(3, { moves: '' }))).toBeFalse();
  });

  it('füllt Seiten mit sechs Aufgaben, die letzte bleibt angebrochen', () => {
    const tasks = Array.from({ length: 8 }, (_, i) => ({ no: i + 1 } as WorksheetTask));
    const sheets = toSheets(tasks);
    expect(sheets.length).toBe(2);
    expect(sheets[0].length).toBe(6);
    expect(sheets[1].length).toBe(2);
    expect(sheets[1][0].no).toBe(7);
  });
});

describe('WorksheetComponent', () => {
  it('zeigt sechs Diagramme je Blatt und zählt fortlaufend durch', () => {
    const fixture = make(Array.from({ length: 8 }, (_, i) => puzzle(i + 1)));
    const c = fixture.componentInstance;
    expect(c.taskCount).toBe(8);
    expect(c.sheets.length).toBe(2);
    expect(c.sheets[0].length).toBe(6);
    expect(c.sheets[0][0].no).toBe(1);
    expect(c.sheets[1][0].no).toBe(7);

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('.ws-sheet').length).toBe(2);
    expect(el.querySelectorAll('.ws-task').length).toBe(8);
    // Je Aufgabe zwei Schreiblinien für die Lösung.
    expect(el.querySelectorAll('.ws-lines span').length).toBe(16);
    // Kopf- und Fußzeile auf JEDER Seite (nicht nur auf der ersten).
    expect(el.querySelectorAll('.ws-head').length).toBe(2);
    expect(el.querySelectorAll('.ws-foot').length).toBe(2);
  });

  it('druckt mit dem Druck-Figurensatz und mit Koordinaten am Brett', () => {
    // merida kommt dem klassischen Diagramm-Ausdruck am nächsten und haengt bewusst NICHT am
    // Bildschirm-Satz des Profils; die Bezeichner braucht man auf Papier (kein Hovern).
    const el: HTMLElement = make([puzzle(1)]).nativeElement;
    const hrefs = Array.from(el.querySelectorAll('app-flashcard-board image')).map(i => i.getAttribute('href'));
    expect(hrefs.length).toBeGreaterThan(0);
    expect(hrefs.every(h => (h ?? '').startsWith('/piece/merida/'))).toBeTrue();
    // 8 Linien + 8 Reihen je Brett.
    expect(el.querySelectorAll('app-flashcard-board text').length).toBe(16);
  });

  it('lässt Info-Seiten weg', () => {
    const fixture = make([puzzle(1), puzzle(2, { isInfoOnly: true, moves: '' }), puzzle(3)]);
    expect(fixture.componentInstance.taskCount).toBe(2);
  });

  it('zeigt die GEFRAGTE Stellung (Vorspielzug ist eingespielt) und wer am Zug ist', () => {
    // startPly 0 ⇒ 10…Sd4 (c6d4) wird vorgespielt, gefragt ist der weiße Zug.
    const fixture = make([puzzle(1, { fen: BLACK_TO_MOVE, moves: 'c6d4 c1g5', startPly: 0 })]);
    const task = fixture.componentInstance.sheets[0][0];
    expect(task.whiteToMove).toBeTrue();
    expect(task.orientation).toBe('white');
    expect(task.fen.split(' ')[1]).toBe('w');
    expect(task.fen).not.toBe(BLACK_TO_MOVE);   // Vorspielzug steckt in der Stellung
  });

  it('filtert auf ein Kapitel und benennt es in der Kopfzeile', () => {
    const fixture = make(
      [puzzle(1, { chapter: 'Round 1' }), puzzle(2, { chapter: 'Round 2' }), puzzle(3, { chapter: 'Round 1' })],
      { chapter: 'Round 1' });
    expect(fixture.componentInstance.taskCount).toBe(2);
    expect(fixture.componentInstance.scopeLabelKey).toBe('courses.worksheet.scopeChapter');
    expect(fixture.componentInstance.chapterName).toBe('Round 1');
  });

  it('kennt die Sammelgruppe „ohne Kapitel"', () => {
    const fixture = make([puzzle(1, { chapter: 'Round 1' }), puzzle(2)], { chapter: '' });
    expect(fixture.componentInstance.taskCount).toBe(1);
    expect(fixture.componentInstance.scopeLabelKey).toBe('courses.worksheet.scopeNoChapter');
  });

  it('nimmt auf Wunsch nur die markierten Linien', () => {
    const fixture = make([puzzle(1), puzzle(2), puzzle(3)], { marked: '1' }, [2, 3]);
    expect(fixture.componentInstance.taskCount).toBe(2);
    expect(fixture.componentInstance.scopeLabelKey).toBe('courses.worksheet.scopeMarked');
  });

  it('sagt es, wenn in der Auswahl nichts abgefragt wird', () => {
    const fixture = make([puzzle(1, { isInfoOnly: true, moves: '' })]);
    expect(fixture.componentInstance.taskCount).toBe(0);
    expect((fixture.nativeElement as HTMLElement).querySelector('.ws-empty')).not.toBeNull();
  });
});
