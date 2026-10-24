import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  CourseTranslationJob, CourseTranslations,
} from './course.types';

/** Kurs-Übersetzung (Stufe B, 0.548.0): Sprachen, Aufträge anfordern und zurückziehen. */
@Injectable({ providedIn: 'root' })
export class CourseTranslationApi {
  private readonly http = inject(HttpClient);

  /** Welche Sprachen es gibt, was wartet/läuft, ob der Aufrufer anfordern darf. Anonym nur bei
   *  öffentlichen Kursen (sonst 404). */
  getTranslations(bookId: number): Observable<CourseTranslations> {
    return this.http.get<CourseTranslations>(`/api/courses/${bookId}/translations`);
  }

  /** Übersetzung anfordern. 202 = neuer Auftrag, 200 = für (Kurs, Sprache) wartete schon einer —
   *  deshalb die ganze Antwort. Absagen (400/409/503) tragen `reason`. */
  requestTranslation(bookId: number, language: string): Observable<HttpResponse<CourseTranslationJob>> {
    return this.http.post<CourseTranslationJob>(`/api/courses/${bookId}/translations`, { language },
      { observe: 'response' });
  }

  /** Eigenen wartenden Auftrag zurückziehen (Admin: jeden offenen). */
  withdrawTranslation(bookId: number, jobId: number): Observable<void> {
    return this.http.delete<void>(`/api/courses/${bookId}/translations/${jobId}`);
  }
}
