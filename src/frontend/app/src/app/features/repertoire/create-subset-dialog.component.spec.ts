import { TestBed } from '@angular/core/testing';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { CreateSubsetDialogComponent, CreateSubsetDialogData } from './create-subset-dialog.component';
import { RepertoireLine } from './repertoire-viewer.service';

const line = (gameIndex: number, chapter: string): RepertoireLine => ({
  gameIndex, summary: '1. e4', opening: '', white: `W${gameIndex}`, black: chapter, result: '*', moveCount: 1, chapter,
  lineKey: `k${gameIndex}`, isInfo: false, startFen: '', lastMoveSide: 'w',
} as RepertoireLine);

describe('CreateSubsetDialogComponent', () => {
  let close: jasmine.Spy;

  function create(data: CreateSubsetDialogData): CreateSubsetDialogComponent {
    close = jasmine.createSpy('close');
    TestBed.configureTestingModule({
      imports: [CreateSubsetDialogComponent],
      providers: [
        provideTranslateService({ fallbackLang: 'en' }), provideNoopAnimations(),
        { provide: MatDialogRef, useValue: { close } },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    });
    const f = TestBed.createComponent(CreateSubsetDialogComponent);
    f.detectChanges();
    return f.componentInstance;
  }

  const lines = [line(0, 'A'), line(1, 'A'), line(2, 'B'), line(3, 'B'), line(4, '')];

  it('wählt die markierten Linien vor und klappt teilweise gewählte Kapitel auf', () => {
    const c = create({ repertoireName: 'Mein Rep', lines, markedKeys: ['k1', 'k2', 'k3'] });
    expect([...c.picked].sort()).toEqual([1, 2, 3]);
    expect(c.state(c.chapters[0])).toBe('some');
    expect(c.state(c.chapters[1])).toBe('all');
    expect(c.state(c.chapters[2])).toBe('none');
    expect(c.open.has('A')).toBeTrue();
    expect(c.open.has('B')).toBeFalse();
    expect(c.name).toBe('Mein Rep (2)');
  });

  it('Kapitel ankreuzen nimmt alle seine Linien, abwählen nimmt sie wieder raus', () => {
    const c = create({ repertoireName: 'R', lines, markedKeys: [] });
    c.setChapter(c.chapters[0], true);
    c.setLine(lines[4], true);
    expect([...c.picked].sort()).toEqual([0, 1, 4]);
    c.setChapter(c.chapters[0], false);
    expect([...c.picked]).toEqual([4]);
  });

  it('gibt Name und gewählte Abschnitte sortiert zurück; ohne Auswahl oder Namen nicht', () => {
    const c = create({ repertoireName: 'R', lines, markedKeys: [] });
    c.confirm();
    expect(close).not.toHaveBeenCalled();
    c.setLine(lines[3], true);
    c.setLine(lines[0], true);
    c.name = '   ';
    c.confirm();
    expect(close).not.toHaveBeenCalled();
    c.name = '  Neu ';
    c.confirm();
    expect(close).toHaveBeenCalledWith({ name: 'Neu', gameIndices: [0, 3] });
  });

  it('Alle / Keine', () => {
    const c = create({ repertoireName: 'R', lines, markedKeys: ['k0'] });
    c.setAll(true);
    expect(c.picked.size).toBe(5);
    c.setAll(false);
    expect(c.picked.size).toBe(0);
  });
});
