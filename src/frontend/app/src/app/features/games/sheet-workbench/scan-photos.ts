import { computed, signal } from '@angular/core';

/** Woher die Fotos kommen und wohin die Maße jeder Seite gehen (für den Ausschnitt der Formular-Korrektur). */
export interface ScanPhotosOptions {
  /** Ein weiteres Foto (`view` ≥ 1) einer Seite — nur bei der Prüfung einer Einlesung. */
  fetchView: (page: number, view: number) => Promise<Blob>;
  /** Natürliche Maße einer Seite ab der zweiten (die erste meldet das große Foto selbst, `onPhotoLoad`). */
  setPageSize: (page: number, width: number, height: number) => void;
}

/**
 * Die Fotos eines Partieformulars in der Formular-Werkbank (`SheetWorkbenchComponent`, RookHub und LeagueHub): je Seite ein
 * Hauptfoto (ein Formular kann über bis zu drei Blätter gehen, 0.690.1), dazu weitere Fotos derselben Seite (0.736.0), die
 * erst bei Bedarf geholt werden. Hält die Objekt-Adressen und gibt sie mit `dispose()` wieder frei; die Dateien selbst bleiben
 * je Seite greifbar (`blob`), für „Foto öffnen/herunterladen" in RookHub.
 */
export class ScanPhotos {
  /** Die Fotos des Formulars je Seite (Objekt-Adressen). */
  readonly photoUrls = signal<(string | null)[]>([]);
  readonly pageCount = signal(1);
  readonly pageNumbers = computed(() => Array.from({ length: this.pageCount() }, (_, i) => i + 1));
  /** Welche Seite im großen Foto steht — folgt dem gewählten Zug, lässt sich mit den Knöpfen wechseln. */
  readonly shownPage = signal(1);
  /** Weitere Fotos derselben Seite (0.736.0): je Seite ihre Zahl, `shownView` 0 = das Hauptfoto. */
  readonly viewCounts = signal<number[]>([]);
  readonly shownView = signal(0);
  readonly viewNumbers = computed(() => Array.from({ length: 1 + (this.viewCounts()[this.shownPage() - 1] ?? 0) }, (_, i) => i));
  private readonly viewUrls = signal<Record<string, string>>({});
  private readonly mainUrl = computed(() => this.photoUrls()[this.shownPage() - 1] ?? this.photoUrls()[0] ?? null);
  readonly photoUrl = computed(() => {
    const view = this.shownView();
    return (view > 0 ? this.viewUrls()[`${this.shownPage()}:${view}`] : null) ?? this.mainUrl();
  });
  private disposed = false;
  private readonly blobs: (Blob | null)[] = [];

  constructor(private readonly opts: ScanPhotosOptions) {}

  showPage(n: number): void {
    this.shownPage.set(n);
    this.shownView.set(0);
  }

  /** Ein weiteres Foto der Seite erst bei Bedarf holen. */
  async showView(v: number): Promise<void> {
    this.shownView.set(v);
    const page = this.shownPage();
    const key = `${page}:${v}`;
    if (v === 0 || this.viewUrls()[key]) return;
    try {
      const blob = await this.opts.fetchView(page, v);
      if (this.disposed) return;
      const url = URL.createObjectURL(blob);
      this.viewUrls.update(m => ({ ...m, [key]: url }));
    } catch { /* dann bleibt das Hauptfoto stehen */ }
  }

  /** Die Datei des Hauptfotos einer Seite, sobald geladen. */
  blob(page: number): Blob | null {
    return this.blobs[page - 1] ?? null;
  }

  pageUrl(page: number): string | null {
    return this.photoUrls()[page - 1] ?? null;
  }

  /** Alle Seiten holen; die Maße jeder Seite braucht der Ausschnitt, auch wenn sie gerade nicht groß angezeigt wird. */
  async load(count: number, fetch: (page: number) => Promise<Blob>, still: () => boolean): Promise<void> {
    this.pageCount.set(Math.max(1, count));
    for (let page = 1; page <= Math.max(1, count); page++) {
      try {
        const blob = await fetch(page);
        if (!still()) return;
        this.blobs[page - 1] = blob;
        const url = URL.createObjectURL(blob);
        this.photoUrls.update(list => { const next = [...list]; next[page - 1] = url; return next; });
        if (page > 1) {
          const img = new Image();
          img.onload = () => this.opts.setPageSize(page, img.naturalWidth, img.naturalHeight);
          img.src = url;
        }
      } catch { /* ohne Foto geht die Korrektur trotzdem */ }
    }
  }

  /** Objekt-Adressen freigeben (Seite verlassen). */
  dispose(): void {
    this.disposed = true;
    for (const url of this.photoUrls()) if (url) URL.revokeObjectURL(url);
    for (const url of Object.values(this.viewUrls())) URL.revokeObjectURL(url);
  }
}
