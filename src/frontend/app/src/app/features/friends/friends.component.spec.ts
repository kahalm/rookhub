import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { FriendsComponent } from './friends.component';
import { ChallengeService } from '../../core/challenge.service';
import { RevengeService } from '../../core/revenge.service';
import { SnackbarService } from '../../core/snackbar.service';

describe('FriendsComponent search race', () => {
  let component: FriendsComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FriendsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ChallengeService, useValue: { getIncoming: () => of([]), getOutgoing: () => of([]) } },
        { provide: RevengeService, useValue: { getNotifications: () => of([]), markSeen: () => of(null) } },
        { provide: SnackbarService, useValue: { info: () => {}, success: () => {} } },
      ],
    });
    // Template entfernen — wir testen nur die Such-Logik der Klasse, nicht das Rendering.
    TestBed.overrideComponent(FriendsComponent, { set: { template: '' } });
    const fixture = TestBed.createComponent(FriendsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges(); // ngOnInit
    httpMock = TestBed.inject(HttpTestingController);
    // ngOnInit-Loads abräumen (Challenges/Revenge sind gestubbt → kein HTTP).
    httpMock.expectOne('/api/friends').flush([]);
    httpMock.expectOne('/api/friends/requests').flush([]);
  });

  afterEach(() => httpMock.verify());

  it('cancels an in-flight search when a newer one starts (latest wins)', () => {
    component.searchQuery = 'ab';
    component.search();
    const stale = httpMock.expectOne(r => r.url.endsWith('q=ab'));

    component.searchQuery = 'abc';
    component.search();
    const latest = httpMock.expectOne(r => r.url.endsWith('q=abc'));

    expect(stale.cancelled).toBeTrue();          // switchMap brach die alte Anfrage ab
    latest.flush([{ userId: 1, username: 'abc' } as any]);
    expect(component.searchResults.length).toBe(1);
    expect(component.searchResults[0].username).toBe('abc');
  });

  it('ignores queries shorter than 2 chars (no request)', () => {
    component.searchQuery = 'a';
    component.search();
    httpMock.expectNone(r => r.url.includes('/friends/search'));
    expect(component.searchResults).toEqual([]);
  });
});
