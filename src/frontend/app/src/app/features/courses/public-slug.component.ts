import { Component, OnInit, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { NotFoundComponent } from '../../shared/not-found/not-found.component';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { CourseService, PublicSlugChapterTarget, PublicSlugTarget } from './course.service';

/**
 * Kurz-URL für öffentliche Kurse: `/{slug}` (z. B. `/mate1`) und `/{slug}/{kapitel}`
 * (z. B. `/noel/KW46`). Löst den Alias serverseitig auf und springt in den Modus, der zu diesem
 * Buch gehört. Unbekannter Alias oder Kapitel → „Seite nicht gefunden“ an Ort und Stelle, die Adresse bleibt
 * stehen (wie der Catch-all, UX-026) — vorher sprang er still aufs Dashboard, und niemand erkannte, dass etwa eine
 * Kapitel-Kurz-URL nach dem Umbenennen veraltet war. Auch jeder einteilige Tippfehler (/profil) landet hier.
 * NUR bei 404: offline (Status 0), 502/503 im nächtlichen Deploy oder ein Timeout sagen nichts über den Link — dort
 * „Konnte nicht geladen werden“ + „Wiederholen“, sonst hielte man einen gültigen Kurslink für tot.
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
  imports: [NotFoundComponent, LoadErrorComponent],
  template: `
    @if (notFound()) {
      <app-not-found />
    } @else if (loadFailed()) {
      <section class="slug-load-error"><app-load-error (retry)="retry()" /></section>
    }
  `,
  styles: [`.slug-load-error { max-width: 560px; margin: 48px auto; padding: 0 16px; }`],
})
export class PublicSlugComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private courses = inject(CourseService);

  /** Alias/Kapitel gibt es nicht (404, leerer Slug): Hinweisseite statt Weiterleitung. */
  readonly notFound = signal(false);
  /** Auflösung aus einem ANDEREN Grund gescheitert (offline, 5xx, Timeout): Ladefehler mit „Wiederholen“. */
  readonly loadFailed = signal(false);

  ngOnInit(): void {
    this.resolve();
  }

  /** „Wiederholen“ nach einem Ladefehler. */
  retry(): void {
    this.resolve();
  }

  private resolve(): void {
    this.loadFailed.set(false);
    const slug = (this.route.snapshot.paramMap.get('slug') || '').trim();
    const chapter = (this.route.snapshot.paramMap.get('chapter') || '').trim();
    if (!slug) { this.showNotFound(); return; }

    if (chapter) {
      this.courses.resolvePublicSlugChapter(slug, chapter).subscribe({
        next: res => this.goChapter(res),
        error: (e: unknown) => this.onError(e),
      });
      return;
    }
    this.courses.resolvePublicSlug(slug).subscribe({
      next: res => this.goBook(res),
      error: (e: unknown) => this.onError(e),
    });
  }

  /** Nur ein 404 heißt „Link veraltet oder vertippt“ — alles andere ist ein Lade-, kein Adressproblem. */
  private onError(e: unknown): void {
    if (e instanceof HttpErrorResponse && e.status === 404) this.showNotFound();
    else this.loadFailed.set(true);
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
