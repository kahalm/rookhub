import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { LevelMapComponent } from './level-map.component';
import { KidsApiService } from '../../core/kids-api.service';

describe('LevelMapComponent', () => {
  const KEY = 'rh-kids-progress-v1';
  let api: jasmine.SpyObj<KidsApiService>;

  beforeEach(() => {
    localStorage.removeItem(KEY);
    api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['levels']);
    TestBed.configureTestingModule({
      imports: [LevelMapComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }), { provide: KidsApiService, useValue: api }],
    });
  });
  afterEach(() => localStorage.removeItem(KEY));

  /** Codereview 2026-09-29, F7-011: im Fehlerfall stand nur ein Satz da — ein Kind, das nicht liest, kam nicht weiter. */
  it('Ladefehler: Fehlerkachel mit „Nochmal", das die Stufen neu holt', () => {
    api.levels.and.returnValues(
      throwError(() => new Error('500')),
      of([{ level: 1, theme: 'mate1', puzzleCount: 10 }, { level: 2, theme: 'promote', puzzleCount: 10 }]),
    );
    const f = TestBed.createComponent(LevelMapComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const again = el.querySelector<HTMLButtonElement>('kid-error button.again');
    expect(again).withContext('Knopf „Nochmal"').not.toBeNull();
    expect(el.querySelector('.grid')).toBeNull();

    again!.click();
    f.detectChanges();

    expect(api.levels).toHaveBeenCalledTimes(2);
    expect(el.querySelector('kid-error')).toBeNull();
    expect(el.querySelectorAll('.grid li').length).toBe(2);
  });
});
