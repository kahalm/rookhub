import { of, throwError } from 'rxjs';
import { MessagesComponent } from './messages.component';
import { ChatMessage } from '../../core/message.service';

/** Direkt instanziiert (ohne TestBed/Template) — testet die Komponenten-Logik. */
function msg(id: number, fromAdmin: boolean, readByRecipient = false, body = 'hi'): ChatMessage {
  return { id, fromAdmin, readByRecipient, body, createdAt: '2026-06-17T00:00:00Z' } as ChatMessage;
}

function makeService(overrides: any = {}): any {
  return {
    getThread: jasmine.createSpy('getThread').and.returnValue(of([])),
    markUserSeen: jasmine.createSpy('markUserSeen').and.returnValue(of({})),
    send: jasmine.createSpy('send').and.callFake((b: string) => of(msg(99, false, false, b))),
    ...overrides,
  };
}

const snackbar = { warn: jasmine.createSpy('warn') };
const translate = { instant: (k: string) => k };
function newComp(svc: any): MessagesComponent {
  snackbar.warn.calls.reset();
  return new MessagesComponent(svc, snackbar as any, translate as any);
}

describe('MessagesComponent', () => {
  it('ngOnInit loads the thread and marks admin messages as seen when some are unread', () => {
    const svc = makeService({ getThread: jasmine.createSpy('getThread').and.returnValue(of([msg(1, true, false)])) });
    const c = newComp(svc);

    c.ngOnInit();

    expect(c.messages.length).toBe(1);
    expect(c.loading).toBeFalse();
    expect(svc.markUserSeen).toHaveBeenCalledTimes(1);
  });

  it('does not mark seen when there are no unread admin messages', () => {
    const svc = makeService({ getThread: jasmine.createSpy('getThread').and.returnValue(of([msg(1, true, true), msg(2, false, false)])) });
    const c = newComp(svc);

    c.ngOnInit();

    expect(svc.markUserSeen).not.toHaveBeenCalled();
  });

  it('send trims the draft, posts it, appends the reply and clears the input', () => {
    const svc = makeService();
    const c = newComp(svc);
    c.messages = [];
    c.draft = '  hallo  ';

    c.send();

    expect(svc.send).toHaveBeenCalledOnceWith('hallo');
    expect(c.messages.length).toBe(1);
    expect(c.messages[0].body).toBe('hallo');
    expect(c.draft).toBe('');
    expect(c.sending).toBeFalse();
  });

  it('send is a no-op for an empty/whitespace draft', () => {
    const svc = makeService();
    const c = newComp(svc);
    c.draft = '   ';

    c.send();

    expect(svc.send).not.toHaveBeenCalled();
  });

  it('send clears the sending flag on error', () => {
    const svc = makeService({ send: jasmine.createSpy('send').and.returnValue(throwError(() => new Error('x'))) });
    const c = newComp(svc);
    c.draft = 'x';

    c.send();

    expect(c.sending).toBeFalse();
  });

  // F5-017: ein gescheitertes Senden darf nicht still bleiben (sah aus wie versendet / Knopf kaputt).
  it('send reports the failure and keeps the draft', () => {
    const svc = makeService({ send: jasmine.createSpy('send').and.returnValue(throwError(() => ({ status: 500 }))) });
    const c = newComp(svc);
    c.draft = 'wichtig';

    c.send();

    expect(snackbar.warn).toHaveBeenCalledOnceWith('messages.sendError');
    expect(c.draft).toBe('wichtig');
    expect(c.messages.length).toBe(0);
  });

  // F5-017: ein Ladefehler darf nicht wie „noch keine Nachrichten" aussehen.
  it('a failing initial load shows the error state, and retry loads again', () => {
    const getThread = jasmine.createSpy('getThread').and.returnValues(
      throwError(() => ({ status: 500 })), of([msg(1, false, true)]));
    const c = newComp(makeService({ getThread }));

    c.ngOnInit();
    expect(c.loading).toBeFalse();
    expect(c.loadError).toBeTrue();

    c.retry();
    expect(getThread).toHaveBeenCalledTimes(2);
    expect(c.loadError).toBeFalse();
    expect(c.messages.length).toBe(1);
  });

  it('a failing background refresh (window focus) keeps the shown thread instead of the error state', () => {
    const getThread = jasmine.createSpy('getThread').and.returnValues(
      of([msg(1, false, true)]), throwError(() => ({ status: 0 })));
    const c = newComp(makeService({ getThread }));

    c.ngOnInit();
    c.onWindowFocus();

    expect(c.loadError).toBeFalse();
    expect(c.messages.length).toBe(1);
  });

  it('onWindowFocus reloads the thread (so a new admin reply shows without a manual reload)', () => {
    const svc = makeService();
    const c = newComp(svc);
    c.ngOnInit();                       // 1. Laden
    c.onWindowFocus();                  // Rückkehr zum Tab → erneut laden
    expect(svc.getThread).toHaveBeenCalledTimes(2);
  });

  it('onWindowFocus does not reload while a send is in flight', () => {
    const svc = makeService();
    const c = newComp(svc);
    c.ngOnInit();
    c.sending = true;
    c.onWindowFocus();
    expect(svc.getThread).toHaveBeenCalledTimes(1);   // kein zusätzliches Laden
  });
});
