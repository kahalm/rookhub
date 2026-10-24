import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { Observable } from 'rxjs';
import { CourseService } from './course.service';

/**
 * HTTP-Inventar der Kurs-Schnittstelle (Charakterisierung vor Zerlegung Z33): JEDE Methode einmal mit
 * Beispielwerten aufgerufen, festgehalten werden Methode, URL samt Query, Rumpf und Antworttyp. Die Liste
 * muss nach jedem Umbau Zeile für Zeile gleich bleiben — URLs, Parameter und Header sind API-Vertrag.
 */
describe('CourseService HTTP-Inventar', () => {
  let svc: CourseService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    svc = TestBed.inject(CourseService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function describeBody(body: unknown): string {
    if (body instanceof FormData) {
      return 'form:' + [...body.entries()].map(([k, v]) => `${k}=${v instanceof File ? 'file:' + v.name : v}`).join(',');
    }
    return JSON.stringify(body ?? null);
  }

  function record(call: () => Observable<unknown>): string {
    call().subscribe({ error: () => { /* egal */ } });
    const reqs: TestRequest[] = http.match(() => true);
    expect(reqs.length).withContext('genau eine Anfrage').toBe(1);
    const r = reqs[0].request;
    const line = `${r.method} ${r.urlWithParams} body=${describeBody(r.body)} rt=${r.responseType}`;
    reqs[0].flush(r.responseType === 'blob' ? new Blob(['x']) : {});
    return line;
  }

  it('bleibt Methode für Methode gleich', () => {
    const file = new File(['1. e4 *'], 'kurs.pgn');
    const lines = [
      record(() => svc.getCourses()),
      record(() => svc.createCourse(' Mein Kurs ', file)),
      record(() => svc.createCourse('   ')),
      record(() => svc.deleteCourse(7)),
      record(() => svc.shareCourse(7, [3, 4])),
      record(() => svc.getShareRecipients(7)),
      record(() => svc.unshareCourse(7, 3)),
      record(() => svc.linkCourse(7, 8)),
      record(() => svc.getEbookRefs(7)),
      record(() => svc.linkEbook(7)),
      record(() => svc.linkEbook(7, 12)),
      record(() => svc.getLink(7)),
      record(() => svc.unlinkCourse(7)),
      record(() => svc.convertToRepertoire(7)),
      record(() => svc.pinCourse(7)),
      record(() => svc.unpinCourse(7)),
      record(() => svc.setCourseThemes(7, ['tactics', 'endgame'])),
      record(() => svc.getBookPuzzles(7)),
      record(() => svc.getBookPuzzles(7, 'de')),
      record(() => svc.getPublicCourse(7)),
      record(() => svc.getPublicCourse(7, 0, 50, 'hr')),
      record(() => svc.resolvePublicSlug('matt in 1')),
      record(() => svc.resolvePublicSlugChapter('mate1', 'Kapitel 2/3')),
      record(() => svc.getLineStatus(7)),
      record(() => svc.downloadPgn(7)),
      record(() => svc.downloadChapterPgn(7, 'K1')),
      record(() => svc.downloadChapterPgn(7, null)),
      record(() => svc.downloadLinePgn(7, 99)),
      record(() => svc.checkAccess()),
      record(() => svc.getChapters(7)),
      record(() => svc.getChapters(7, 'de')),
      record(() => svc.getNext(7, 'sequential')),
      record(() => svc.getNext(7, 'random', 11, 12, 2, 'de')),
      record(() => svc.recordResult(7, 11, true)),
      record(() => svc.recordResult(7, 11, false, 'random', 30, 2, 1, 'easy')),
      record(() => svc.retryFailed(7)),
      record(() => svc.setSkipInfoLines(7, true)),
      record(() => svc.reset(7)),
      record(() => svc.markInfoSeen(7, 11)),
      record(() => svc.reprocessStatus()),
      record(() => svc.reprocess()),
      record(() => svc.getDetail(7)),
      record(() => svc.getDetail(7, 'hu')),
      record(() => svc.getTranslations(7)),
      record(() => svc.requestTranslation(7, 'de')),
      record(() => svc.withdrawTranslation(7, 5)),
      record(() => svc.setCalculation(7, true)),
      record(() => svc.getFlashcardMarks(7)),
      record(() => svc.setFlashcardMark(7, 99, true)),
      record(() => svc.setFlashcardMark(7, 99, false)),
      record(() => svc.getChapterLines(7, 'K1')),
      record(() => svc.getChapterLines(7, null)),
      record(() => svc.addLines(7, null, 'FEN')),
      record(() => svc.deleteLine(7, 99)),
      record(() => svc.renameChapter(7, 'Alt', 'Neu')),
      record(() => svc.deleteChapter(7, null)),
      record(() => svc.resetChapter(7, 'K1')),
    ];
    expect(lines).toEqual([
      'GET /api/courses body=null rt=json',
      'POST /api/courses body=form:name=Mein Kurs,file=file:kurs.pgn rt=json',
      'POST /api/courses body=form: rt=json',
      'DELETE /api/courses/7 body=null rt=json',
      'POST /api/courses/7/share body={"recipientUserIds":[3,4]} rt=json',
      'GET /api/courses/7/shares body=null rt=json',
      'DELETE /api/courses/7/share/3 body=null rt=json',
      'POST /api/courses/7/link body={"linkedBookId":8} rt=json',
      'GET /api/courses/7/ebook-refs body=null rt=json',
      'POST /api/courses/7/ebook-refs body={"ebookId":null} rt=json',
      'POST /api/courses/7/ebook-refs body={"ebookId":12} rt=json',
      'GET /api/courses/7/link body=null rt=json',
      'DELETE /api/courses/7/link body=null rt=json',
      'POST /api/courses/7/convert-to-repertoire body={} rt=json',
      'POST /api/courses/7/pin body={} rt=json',
      'DELETE /api/courses/7/pin body=null rt=json',
      'PUT /api/courses/7/themes body={"themes":["tactics","endgame"]} rt=json',
      'GET /api/courses/7/puzzles body=null rt=json',
      'GET /api/courses/7/puzzles?lang=de body=null rt=json',
      'GET /api/courses/7/public body=null rt=json',
      'GET /api/courses/7/public?skip=0&take=50&lang=hr body=null rt=json',
      'GET /api/courses/by-slug/matt%20in%201 body=null rt=json',
      'GET /api/courses/by-slug/mate1/Kapitel%202%2F3 body=null rt=json',
      'GET /api/courses/7/line-status body=null rt=json',
      'GET /api/courses/7/pgn body=null rt=blob',
      'GET /api/courses/7/chapter-pgn?chapter=K1 body=null rt=blob',
      'GET /api/courses/7/chapter-pgn?chapter= body=null rt=blob',
      'GET /api/courses/7/lines/99/pgn body=null rt=blob',
      'GET /api/courses/access body=null rt=json',
      'GET /api/courses/7/chapters body=null rt=json',
      'GET /api/courses/7/chapters?lang=de body=null rt=json',
      'GET /api/courses/7/next?mode=sequential body=null rt=json',
      'GET /api/courses/7/next?mode=random&after=11&exclude=12&chapterIndex=2&lang=de body=null rt=json',
      'POST /api/courses/7/results body={"bookPuzzleId":11,"solved":true,"timeSeconds":0,"hintsUsed":0} rt=json',
      'POST /api/courses/7/results body={"bookPuzzleId":11,"solved":false,"mode":"random","timeSeconds":30,"chapterIndex":2,"hintsUsed":1,"solveMode":"easy"} rt=json',
      'POST /api/courses/7/retry-failed body={} rt=json',
      'PUT /api/courses/7/skip-info-lines body={"skip":true} rt=json',
      'POST /api/courses/7/reset body={} rt=json',
      'POST /api/courses/7/info-seen body={"bookPuzzleId":11} rt=json',
      'GET /api/courses/reprocess/status body=null rt=json',
      'POST /api/courses/reprocess body={} rt=json',
      'GET /api/courses/7 body=null rt=json',
      'GET /api/courses/7?lang=hu body=null rt=json',
      'GET /api/courses/7/translations body=null rt=json',
      'POST /api/courses/7/translations body={"language":"de"} rt=json',
      'DELETE /api/courses/7/translations/5 body=null rt=json',
      'PUT /api/courses/7/calculation body={"isCalculation":true} rt=json',
      'GET /api/courses/7/flashcards body=null rt=json',
      'POST /api/courses/7/flashcards/99 body={} rt=json',
      'DELETE /api/courses/7/flashcards/99 body=null rt=json',
      'GET /api/courses/7/lines?chapter=K1 body=null rt=json',
      'GET /api/courses/7/lines?chapter= body=null rt=json',
      'POST /api/courses/7/lines body={"chapter":null,"text":"FEN"} rt=json',
      'DELETE /api/courses/7/lines/99 body=null rt=json',
      'PUT /api/courses/7/chapters/rename body={"chapter":"Alt","newName":"Neu"} rt=json',
      'POST /api/courses/7/chapters/delete body={"chapter":null} rt=json',
      'POST /api/courses/7/chapters/reset body={"chapter":"K1"} rt=json',
    ]);
  });

  it('meldet Zugriffsänderungen über accessChanged$', () => {
    let n = 0;
    svc.accessChanged$.subscribe(() => n++);
    svc.notifyAccessChanged();
    expect(n).toBe(1);
  });
});
