import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { CourseListComponent } from './course-list.component';
import { KidsApiService } from '../../core/kids-api.service';

describe('CourseListComponent', () => {
  it('fragt die Kurse in der Sprache der Seite (Kindertitel je Sprache) und zeigt ihre Titel', async () => {
    const api = jasmine.createSpyObj<KidsApiService>('KidsApiService', ['courses']);
    api.courses.and.returnValue(of([{ bookId: 339, title: 'Matt in einem Zug', description: null, puzzleCount: 498 }]));
    TestBed.configureTestingModule({
      imports: [CourseListComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }), { provide: KidsApiService, useValue: api }],
    });
    await TestBed.inject(TranslateService).use('de');

    const f = TestBed.createComponent(CourseListComponent);
    f.detectChanges();

    expect(api.courses).toHaveBeenCalledWith('de');
    expect((f.nativeElement as HTMLElement).textContent).toContain('Matt in einem Zug');
  });
});
