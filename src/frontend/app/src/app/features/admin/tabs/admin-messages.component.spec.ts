import { DestroyRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminMessagesComponent } from './admin-messages.component';

/** Instanziierung im Injection-Context (die Komponente nutzt `inject(DestroyRef)` als Feld). */
function make(svc: Record<string, unknown> = {}, snackbar: any = { show: () => {}, warn: () => {} }) {
  const messageService = {
    getThreads: jasmine.createSpy('getThreads').and.returnValue(of([])),
    getAdminThread: jasmine.createSpy('getAdminThread').and.returnValue(of([])),
    ...svc,
  } as any;
  const adminService = {} as any;
  const translate = { instant: (k: string) => k } as any;
  const auth = { currentUser: { userId: 7 } } as any;
  const route = { queryParamMap: of({ get: (_: string) => null }) } as any;
  return TestBed.runInInjectionContext(() =>
    new AdminMessagesComponent(messageService, adminService, snackbar, translate, auth, route));
}

describe('AdminMessagesComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [{ provide: DestroyRef, useValue: { onDestroy: () => () => {} } }],
  }));

  it('selectedThread returns the summary of the open thread', () => {
    const c = make();
    c.threads = [{ userId: 1, username: 'a' }, { userId: 2, username: 'b' }] as any;
    c.selectedThreadUserId = 2;
    expect(c.selectedThread?.username).toBe('b');
  });

  it('myId reflects the logged-in admin id', () => {
    expect(make().myId).toBe(7);
  });

  it('startConversation opens an existing thread instead of starting a new one', () => {
    const c = make();
    c.threads = [{ userId: 5, username: 'existing' }] as any;
    c.startConversation({ id: 5, username: 'existing' } as any);
    expect(c.selectedThreadUserId).toBe(5);
    expect((c as any).messageService.getAdminThread).toHaveBeenCalledWith(5);
  });

  // F5-017: Uebernehmen/Freigeben scheiterten still — nichts passierte, keine Meldung.
  it('claimThread and releaseThread report a failure instead of swallowing it', () => {
    const snackbar = { show: () => {}, warn: jasmine.createSpy('warn') };
    const fail = () => throwError(() => ({ status: 500 }));
    const c = make({ claimThread: jasmine.createSpy('claim').and.callFake(fail),
                     releaseThread: jasmine.createSpy('release').and.callFake(fail) }, snackbar);
    c.claimThread(3);
    c.releaseThread(3);
    expect(snackbar.warn).toHaveBeenCalledTimes(2);
    expect(snackbar.warn).toHaveBeenCalledWith('admin.messages.assignFailed');
  });

  // F5-017: ein Ladefehler der Thread-Liste darf nicht wie „keine Threads" aussehen.
  it('loadThreads sets the error flag on failure and clears it on the next success', () => {
    const getThreads = jasmine.createSpy('getThreads').and.returnValues(throwError(() => ({ status: 500 })), of([]));
    const c = make({ getThreads });
    c.loadThreads();
    expect(c.threadsLoading).toBeFalse();
    expect(c.threadsLoadError).toBeTrue();
    c.loadThreads();
    expect(c.threadsLoadError).toBeFalse();
  });
});
