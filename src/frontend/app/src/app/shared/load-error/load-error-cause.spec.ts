import { HttpErrorResponse } from '@angular/common/http';
import { loadErrorCause } from './load-error-cause';

describe('loadErrorCause', () => {
  it('ohne Fehlerobjekt keine Ursache', () => {
    expect(loadErrorCause(null, true)).toBeNull();
    expect(loadErrorCause(undefined, false)).toBeNull();
  });

  it('Gerät offline geht vor dem Status', () => {
    expect(loadErrorCause(new HttpErrorResponse({ status: 0 }), false)).toEqual({ key: 'common.loadCause.offline' });
    expect(loadErrorCause(new HttpErrorResponse({ status: 500 }), false)).toEqual({ key: 'common.loadCause.offline' });
  });

  it('Status 0 online = Server nicht erreichbar, sonst der HTTP-Status', () => {
    expect(loadErrorCause(new HttpErrorResponse({ status: 0 }), true)).toEqual({ key: 'common.loadCause.unreachable' });
    expect(loadErrorCause(new HttpErrorResponse({ status: 503 }), true)).toEqual({ key: 'common.loadCause.status', params: { status: 503 } });
  });

  it('andere Fehler (kein HTTP) ohne Ursache', () => {
    expect(loadErrorCause(new Error('x'), true)).toBeNull();
  });
});
