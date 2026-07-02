import { DestroyRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AdminMessagesComponent } from './admin-messages.component';

/** Instanziierung im Injection-Context (die Komponente nutzt `inject(DestroyRef)` als Feld). */
function make() {
  const messageService = {
    getThreads: jasmine.createSpy('getThreads').and.returnValue(of([])),
    getAdminThread: jasmine.createSpy('getAdminThread').and.returnValue(of([])),
  } as any;
  const adminService = {} as any;
  const snackbar = { show: () => {} } as any;
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
});
