import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { RepertoireEditComponent } from './repertoire-edit.component';

describe('RepertoireEditComponent batch upload', () => {
  it('emits fileUploaded once after all uploads finish (not per file)', () => {
    TestBed.configureTestingModule({
      imports: [RepertoireEditComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
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
});
