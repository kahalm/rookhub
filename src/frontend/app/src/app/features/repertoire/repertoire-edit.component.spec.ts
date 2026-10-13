import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { RepertoireEditComponent } from './repertoire-edit.component';

describe('RepertoireEditComponent batch upload', () => {
  it('emits fileUploaded once after all uploads finish (not per file)', () => {
    TestBed.configureTestingModule({
      imports: [RepertoireEditComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideTranslateService()],
    });
    const fixture = TestBed.createComponent(RepertoireEditComponent);
    const component = fixture.componentInstance;
    component.repertoireId = 7;

    let emitCount = 0;
    component.fileUploaded.subscribe(() => emitCount++);

    const files = [new File(['1. e4 *'], 'a.pgn'), new File(['1. d4 *'], 'b.pgn')];
    (component as unknown as { uploadFiles: (f: unknown) => void }).uploadFiles(files);

    const http = TestBed.inject(HttpTestingController);
    const reqs = http.match('/api/repertoires/7/files');
    expect(reqs.length).toBe(2);
    reqs.forEach(r => r.flush({}));

    expect(emitCount).toBe(1); // vorher: 2 (ein Emit pro Datei)
    http.verify();
  });

  it('skips non-.pgn files (client-side validation)', () => {
    TestBed.configureTestingModule({
      imports: [RepertoireEditComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideTranslateService()],
    });
    const component = TestBed.createComponent(RepertoireEditComponent).componentInstance;
    component.repertoireId = 9;

    const files = [new File(['1. e4 *'], 'ok.pgn'), new File(['nope'], 'evil.txt')];
    (component as unknown as { uploadFiles: (f: unknown) => void }).uploadFiles(files);

    const http = TestBed.inject(HttpTestingController);
    const reqs = http.match('/api/repertoires/9/files');
    expect(reqs.length).toBe(1); // nur ok.pgn wird hochgeladen
    reqs.forEach(r => r.flush({}));
    http.verify();
  });
});

/** UI-Sweep a1: Ablage im hellen Design sichtbar (a1-rk-dropzone-rahmen-hell), ab 900 px zweispaltig
 *  (a1-rk-repertoire-edit-breite). Geprüft am kompilierten CSS — Karma läuft 1 400 px breit. */
describe('RepertoireEditComponent Gestaltung (a1)', () => {
  const css = ((RepertoireEditComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s+/g, ' ');

  it('Ablage-Rand aus der Textfarbe (≥ 50 %), kein fester Grauwert; Symbol 60 %', () => {
    expect(css).not.toContain('#ccc');
    expect(css).toMatch(/\.upload-area[^{]*\{ ?border: 2px dashed color-mix\(in srgb, currentColor 50%, transparent\)/);
    expect(css).toMatch(/\.upload-area[^{]* mat-icon[^{]*\{[^}]*currentColor 60%/);
  });

  it('ab 900 px Ablage und Dateiliste nebeneinander', () => {
    const at = css.indexOf('@media (min-width: 900px)');
    expect(at).toBeGreaterThan(-1);
    expect(css.slice(at)).toMatch(/\.edit-container[^{]*\{ ?display: grid; grid-template-columns: minmax\(0, 1fr\) minmax\(0, 1fr\)/);
  });

  it('Dateiliste steht in eigenem Abschnitt; Ziehen über die Ablage färbt den Rand', () => {
    TestBed.configureTestingModule({
      imports: [RepertoireEditComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideTranslateService()],
    });
    const fixture = TestBed.createComponent(RepertoireEditComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.edit-container > section.files > h3')).not.toBeNull();
    const area = el.querySelector('.upload-area') as HTMLElement;
    area.dispatchEvent(new Event('dragover', { cancelable: true }));
    fixture.detectChanges();
    expect(area.classList).toContain('dragover');
    area.dispatchEvent(new Event('dragleave'));
    fixture.detectChanges();
    expect(area.classList).not.toContain('dragover');
  });
});
