import { HttpEvent, HttpHandlerFn, HttpRequest } from '@angular/common/http';
import { of } from 'rxjs';
import { visitorInterceptor } from './visitor.interceptor';

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
});
