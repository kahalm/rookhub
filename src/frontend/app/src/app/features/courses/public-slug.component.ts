import { Component, OnInit, inject, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CourseService } from './course.service';

/**
 * Kurz-URL für öffentliche Kurse: `/{slug}` (z. B. `/mate1`). Löst den Alias serverseitig auf die
 * BookId auf und leitet auf den öffentlichen Kurs weiter. Erster Schritt: fester Visualisierungs-
 * modus 0 (später konfigurierbar). Unbekannter Alias → Dashboard (wie der bisherige Catch-all).
 *
 * Die Route steht ganz am Ende (vor `**`), fängt also nur einzelne unbekannte Pfadsegmente ab —
 * echte Routen (login/dashboard/courses/…) matchen vorher.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-public-slug',
  standalone: true,
  template: '',
})
export class PublicSlugComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private courses = inject(CourseService);

  ngOnInit(): void {
    const slug = (this.route.snapshot.paramMap.get('slug') || '').trim();
    if (!slug) { this.router.navigate(['/dashboard'], { replaceUrl: true }); return; }
    this.courses.resolvePublicSlug(slug).subscribe({
      next: res => this.router.navigate(['/courses', res.bookId, 'random'],
        { queryParams: { visualmode: 0 }, replaceUrl: true }),
      error: () => this.router.navigate(['/dashboard'], { replaceUrl: true }),
    });
  }
}
