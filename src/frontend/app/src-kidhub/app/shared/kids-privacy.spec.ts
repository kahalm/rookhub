import { KID_PAGE_WIDTH, kidPageWidth } from './kids-layout';
import { foldPrivacy, isPrivacyUrl } from './kids-privacy';

describe('kidPageWidth (k-logo: Kopfzeile in der Spalte des Inhalts)', () => {
  it('Aufgabenseiten richten sich nach --kid-row, die uebrigen nach ihrer Seitenbreite', () => {
    const row = 'calc(var(--kid-row, 1068px) + 32px)';
    expect(kidPageWidth('/levels/3')).toBe(row);
    expect(kidPageWidth('/stars/free')).toBe(row);
    expect(kidPageWidth('/stars/12?x=1')).toBe(row);
    expect(kidPageWidth('/endless')).toBe(row);
    expect(kidPageWidth('/courses/339')).toBe(row);
    expect(kidPageWidth('/')).toBe('1160px');
    expect(kidPageWidth('/?quickstart=1')).toBe('1160px');
    expect(kidPageWidth('/levels')).toBe('980px');
    expect(kidPageWidth('/stars')).toBe('980px');
    expect(kidPageWidth('/courses')).toBe('820px');
    expect(kidPageWidth('/privacy#privacy-data')).toBe('840px');
    expect(kidPageWidth('/login')).toBe('980px');
    expect(KID_PAGE_WIDTH).toEqual({ home: 1160, map: 980, list: 820, legal: 840 });
  });
});

describe('foldPrivacy (k-privacy: Eltern-Abschnitte zum Aufklappen)', () => {
  /** Nachbau der gezeichneten Seite: Inhaltsverzeichnis, Kinder-Teil, Eltern-Abschnitte, Ruecklink. */
  function page(): HTMLElement {
    const root = document.createElement('div');
    root.innerHTML = `
      <app-privacy><mat-card-content>
        <p class="muted">Stand</p>
        <nav class="toc"><ul><li>x</li></ul></nav>
        <p class="intro">Einfach erklaert</p>
        <h2 id="privacy-kid">Das Wichtigste in Kuerze</h2>
        <ul class="kid-list"><li>Nur auf diesem Geraet</li></ul>
        <p class="details">Fuer Eltern: …</p>
        <h2 id="privacy-controller">Verantwortlicher</h2>
        <p class="c1">kidhub@…</p>
        <h2 id="privacy-data">Welche Daten</h2>
        <p class="d1">Je nach Nutzung</p>
        <ul class="d2"><li>Kontodaten</li></ul>
        <p class="back"><a>Zurueck</a></p>
      </mat-card-content></app-privacy>`;
    return root;
  }

  it('Kinder-Teil als Karte, jeder Eltern-Abschnitt als <details> mit der Ueberschrift im <summary>', () => {
    const root = page();
    expect(foldPrivacy(root)).toBeTrue();

    const card = root.querySelector('.kid-summary')!;
    expect(card.querySelector('h2#privacy-kid')).not.toBeNull();
    expect(card.querySelector('.kid-list')).not.toBeNull();
    expect(card.querySelector('.details')).withContext('„Fuer Eltern" bleibt unter der Karte').toBeNull();

    const sections = Array.from(root.querySelectorAll('details.kid-parent'));
    expect(sections.map(d => d.querySelector('summary > h2')!.id)).toEqual(['privacy-controller', 'privacy-data']);
    expect(sections.every(d => !d.hasAttribute('open'))).withContext('zugeklappt').toBeTrue();
    expect(sections[1].querySelector('.d1')).not.toBeNull();
    expect(sections[1].querySelector('.d2')).not.toBeNull();
    // Der Ruecklink bleibt am Ende stehen, ausserhalb der Abschnitte.
    expect(root.querySelector('details .back')).toBeNull();
    expect(root.querySelector('mat-card-content')!.lastElementChild!.classList).toContain('back');
  });

  it('ein zweiter Aufruf aendert nichts; ohne Datenschutzseite passiert nichts', () => {
    const root = page();
    foldPrivacy(root);
    expect(foldPrivacy(root)).toBeFalse();
    expect(root.querySelectorAll('details').length).toBe(2);
    expect(foldPrivacy(document.createElement('div'))).toBeFalse();
  });

  it('isPrivacyUrl: nur /privacy, auch mit Anker oder Abfrage', () => {
    expect(isPrivacyUrl('/privacy')).toBeTrue();
    expect(isPrivacyUrl('/privacy#privacy-data')).toBeTrue();
    expect(isPrivacyUrl('/privacy?x=1')).toBeTrue();
    expect(isPrivacyUrl('/account-deletion')).toBeFalse();
    expect(isPrivacyUrl('/')).toBeFalse();
  });
});
