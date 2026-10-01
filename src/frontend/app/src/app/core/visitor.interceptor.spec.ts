import { HttpEvent, HttpHandlerFn, HttpRequest } from '@angular/common/http';
import { of } from 'rxjs';
import { visitorInterceptor } from './visitor.interceptor';
import { getOrCreateAnonSessionId } from './anon-session';

describe('visitorInterceptor', () => {
  let captured: HttpRequest<unknown> | null;
  const next: HttpHandlerFn = (req) => { captured = req; return of({} as HttpEvent<unknown>); };

  beforeEach(() => { captured = null; localStorage.removeItem('rookhub_puzzle_session'); });

  it('sets X-Visitor-Id on /api requests and creates+persists an id if missing', () => {
    visitorInterceptor(new HttpRequest('GET', '/api/x'), next).subscribe();
    const id = captured!.headers.get('X-Visitor-Id');
    expect(id).toBeTruthy();
    expect(localStorage.getItem('rookhub_puzzle_session')).toBe(id);
  });

  it('reuses the existing localStorage session id', () => {
    localStorage.setItem('rookhub_puzzle_session', 'abcdef12-3456');
    visitorInterceptor(new HttpRequest('GET', '/api/y'), next).subscribe();
    expect(captured!.headers.get('X-Visitor-Id')).toBe('abcdef12-3456');
  });

  it('does not add the header to non-/api requests', () => {
    visitorInterceptor(new HttpRequest('GET', '/i18n/en.json'), next).subscribe();
    expect(captured!.headers.has('X-Visitor-Id')).toBeFalse();
  });

  // F1-014: derselbe Erzeuger wie die Puzzle-Versuche — vorher fehlte der Header ohne randomUUID
  // (HTTP-Dev-Stack) und bei gesperrtem Speicher ganz, obwohl eine Kennung existierte.
  it('vergibt die Kennung auch OHNE crypto.randomUUID (HTTP-Dev-Stack) im Muster des Servers', () => {
    const real = (crypto as { randomUUID?: () => string }).randomUUID;
    try {
      (crypto as { randomUUID?: () => string }).randomUUID = undefined;
      visitorInterceptor(new HttpRequest('GET', '/api/z'), next).subscribe();
    } finally {
      (crypto as { randomUUID?: () => string }).randomUUID = real;
    }
    const id = captured!.headers.get('X-Visitor-Id');
    expect(id).toMatch(/^[a-fA-F0-9-]{32,36}$/);
    expect(localStorage.getItem('rookhub_puzzle_session')).toBe(id);
  });

  it('schickt bei gesperrtem Speicher dieselbe Kennung wie die anonymen Puzzle-Versuche', () => {
    spyOn(Storage.prototype, 'getItem').and.throwError('SecurityError');
    spyOn(Storage.prototype, 'setItem').and.throwError('SecurityError');
    visitorInterceptor(new HttpRequest('GET', '/api/w'), next).subscribe();
    const id = captured!.headers.get('X-Visitor-Id');
    expect(id).toBeTruthy();
    expect(id).toBe(getOrCreateAnonSessionId('rookhub_puzzle_session'));
  });
});
