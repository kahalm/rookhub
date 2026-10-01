import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { NotFoundComponent } from '../../shared/not-found/not-found.component';
import { CourseService, PublicSlugChapterTarget, PublicSlugTarget } from './course.service';

/**
 * Kurz-URL für öffentliche Kurse: `/{slug}` (z. B. `/mate1`) und `/{slug}/{kapitel}`
 * (z. B. `/noel/KW46`). Löst den Alias serverseitig auf und springt in den Modus, der zu diesem
 * Buch gehört. Unbekannter Alias oder Kapitel → „Seite nicht gefunden“ an Ort und Stelle, die Adresse bleibt
 * stehen (wie der Catch-all, UX-026) — vorher sprang er still aufs Dashboard, und niemand erkannte, dass etwa eine
 * Kapitel-Kurz-URL nach dem Umbenennen veraltet war. Auch jeder einteilige Tippfehler (/profil) landet hier.
 *
 * **Die Verzweigung ist kein Kosmetik-Detail**: die Stellungen eines Kalkulationsbuchs sind
 * `IsInfoOnly` und damit aus allen Solver-Pools ausgeschlossen — der Solver meldete dort sofort
 * „abgeschlossen", der Link liefe ins Leere. Deshalb liefert die Auflösung `isCalculation` mit
 * (die Information, nicht nur die Verzweigung) und wir springen nach `/courses/{id}/calc`.
 *
 * Der zweite Pfadteil IST der Kapitelname — kein zweites Konzept, der Link bleibt lesbar.
 * Für Solver-Kurse braucht es dafür den SOLVER-Kapitelindex (nur Quiz-Kapitel, `chapterIndex`);
 * ist das Kapitel dort nicht startbar (`null`), fällt der Sprung auf das ganze Buch zurück,
 * statt auf einer toten Kapitel-Route zu landen. Kalkulationsbücher bekommen den Kapitelnamen
 * als Filter (`?chapter=`) mit.
 *
 * Beide Routen stehen ganz am Ende der Routentabelle (vor `**`), hinter ALLEN echten ein- und
 * zweiteiligen Routen — sonst verschluckte `/:slug/:chapter` echte Seiten wie `/courses/403`.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-public-slug',
  standalone: true,
  imports: [NotFoundComponent],
  template: `@if (notFound()) { <app-not-found /> }`,
})
export class PublicSlugComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private courses = inject(CourseService);

  /** Auflösung gescheitert: Hinweisseite statt Weiterleitung. */
  readonly notFound = signal(false);

  ngOnInit(): void {
    const slug = (this.route.snapshot.paramMap.get('slug') || '').trim();
    const chapter = (this.route.snapshot.paramMap.get('chapter') || '').trim();
    if (!slug) { this.showNotFound(); return; }

    if (chapter) {
      this.courses.resolvePublicSlugChapter(slug, chapter).subscribe({
        next: res => this.goChapter(res),
        error: () => this.showNotFound(),
      });
      return;
    }
    this.courses.resolvePublicSlug(slug).subscribe({
      next: res => this.goBook(res),
      error: () => this.showNotFound(),
    });
  }

  /** Ganzes Buch: Kalkulations-Modus oder (wie bisher) der Solver im Zufallsmodus. */
  private goBook(res: PublicSlugTarget): void {
    if (res.isCalculation) {
      this.router.navigate(['/courses', res.bookId, 'calc'], { replaceUrl: true });
      return;
    }
    this.router.navigate(['/courses', res.bookId, 'random'],
      { queryParams: { visualmode: 0 }, replaceUrl: true });
  }

  private goChapter(res: PublicSlugChapterTarget): void {
    // Kalkulationsbuch: das Kapitel ist ein FILTER der Stellungsliste, keine eigene Route.
    if (res.isCalculation) {
      this.router.navigate(['/courses', res.bookId, 'calc'],
        { queryParams: { chapter: res.chapter }, replaceUrl: true });
      return;
    }
    // Solver: die interne Kapitel-Route gibt es schon — sie will den SOLVER-Index (nur
    // Quiz-Kapitel). Ohne Index (reines Info-/Stellungs-Kapitel) wäre sie leer; dann lieber
    // das ganze Buch als eine Seite, die sofort „abgeschlossen" meldet.
    if (res.chapterIndex != null) {
      this.router.navigate(['/courses', res.bookId, 'chapter', res.chapterIndex, 'random'],
        { queryParams: { visualmode: 0 }, replaceUrl: true });
      return;
    }
    this.goBook(res);
  }

  private showNotFound(): void {
    this.notFound.set(true);
  }
}
