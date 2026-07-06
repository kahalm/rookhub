import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SwPush } from '@angular/service-worker';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { PushService } from './push.service';

describe('PushService', () => {
  let svc: PushService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PushService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SwPush, useValue: { isEnabled: false, notificationClicks: of(), subscription: of(null) } },
        { provide: Router, useValue: { navigateByUrl: jasmine.createSpy('nav') } },
      ],
    });
    svc = TestBed.inject(PushService);
    http = TestBed.inject(HttpTestingController);
  });

  it('loads push config', () => {
    let cfg: any;
    svc.getConfig().subscribe(c => cfg = c);
    const req = http.expectOne('/api/notifications/push/config');
    expect(req.request.method).toBe('GET');
    req.flush({ publicKey: 'K', enabledCategories: ['courses'] });
    expect(cfg).toEqual({ publicKey: 'K', enabledCategories: ['courses'] });
  });

  it('saves preferences (PUT) and returns effective categories', () => {
    let res: any;
    svc.setPreferences(['courses', 'admin']).subscribe(r => res = r);
    const req = http.expectOne('/api/notifications/push/preferences');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ categories: ['courses', 'admin'] });
    req.flush({ categories: ['courses'] });   // Server verwarf „admin" (Nicht-Admin)
    expect(res.categories).toEqual(['courses']);
  });
});
