import { KID_HOME_WIDTH, KID_MAP_WIDTH, KID_PAGE_WIDTH, kidPageWidth } from './kids-layout';
import { foldAccountDeletion, foldPrivacy, isAccountDeletionUrl, isLegalUrl, isPrivacyUrl, setAccountDeletionTexts } from './kids-privacy';

describe('kidPageWidth (k-logo: Kopfzeile in der Spalte des Inhalts)', () => {
  it('Aufgabenseiten richten sich nach --kid-row, die uebrigen nach ihrer Seitenbreite', () => {
    const row = 'calc(var(--kid-row, 1068px) + 32px)';
    expect(kidPageWidth('/levels/3')).toBe(row);
    expect(kidPageWidth('/stars/free')).toBe(row);
    expect(kidPageWidth('/stars/12?x=1')).toBe(row);
    expect(kidPageWidth('/endless')).toBe(row);
    expect(kidPageWidth('/courses/339')).toBe(row);
    // z2-k-start-kacheln-breit / z2-k-karten-breit: Start und Karten wachsen am großen Bildschirm mit.
    expect(kidPageWidth('/')).toBe(KID_HOME_WIDTH);
    expect(kidPageWidth('/?quickstart=1')).toBe(KID_HOME_WIDTH);
    expect(kidPageWidth('/levels')).toBe(KID_MAP_WIDTH);
    expect(kidPageWidth('/stars')).toBe(KID_MAP_WIDTH);
    expect(kidPageWidth('/courses')).toBe('820px');
    expect(kidPageWidth('/privacy#privacy-data')).toBe('620px');   // z1-k-legal-line: ≤ ~75 Zeichen je Zeile
    expect(kidPageWidth('/account-deletion')).toBe('620px');   // d1-k-account-deletion-style
    // z2-k-auth-logo: Logo, „← Start" und Karte der Anmelde-Masken auf einer Kante.
    expect(kidPageWidth('/login')).toBe('432px');
    expect(kidPageWidth('/register?x=1')).toBe('432px');
    expect(kidPageWidth('/reset-password?token=x')).toBe('432px');
    expect(KID_PAGE_WIDTH).toEqual({ home: 1160, map: 980, list: 820, legal: 620 });
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

// UI-Sweep 2026-10-11 (d1-k-account-deletion-jargon): Fachbegriff-Liste fuer Eltern zugeklappt, davor ein Kindersatz.
describe('foldAccountDeletion (Konto loeschen in der Kinder-Fassung)', () => {
  const texts = {
    short: 'Alles von dir wird geloescht.', parents: 'Fuer Eltern: was genau geloescht wird',
    howTo: 'Fuer Eltern: so wird das Konto geloescht',
  };

  function page(): HTMLElement {
    const root = document.createElement('div');
    root.innerHTML = `
      <app-account-deletion><mat-card-content>
        <p class="intro">Dasselbe Konto wie RookHub</p>
        <h2 class="how-title">So loeschst du dein Konto</h2>
        <p class="how">In RookHub anmelden, ☰ → Konto → Profil</p>
        <p class="backup"><strong>Vorher sichern:</strong> PGN, Repertoires</p>
        <p class="backup-links"><a>Kurse</a><a>Repertoires</a></p>
        <p class="action"><a class="delete-now">Loeschen</a></p>
        <h2 class="removed-title">Was entfernt wird</h2>
        <ul class="removed"><li>API-Tokens</li><li>Chessable-Zugang</li></ul>
        <h2 class="kept-title">Was bleibt</h2>
        <p class="kept">Anonym</p>
        <h2 class="contact-title">Kontakt</h2>
        <p class="contact">kidhub@example.org</p>
        <p class="back"><a>Zurueck</a></p>
      </mat-card-content></app-account-deletion>`;
    return root;
  }

  it('Liste in ein zugeklapptes <details class="kid-parent">, Kindersatz unter „Was entfernt wird"', () => {
    const root = page();
    expect(foldAccountDeletion(root, texts)).toBeTrue();
    const details = root.querySelector('details.kid-parent:has(ul.removed)')!;
    expect(details.hasAttribute('open')).withContext('zugeklappt').toBeFalse();
    expect(details.querySelector('ul.removed li')).not.toBeNull();
    expect(details.querySelector('summary .kid-parent-label')!.textContent).toBe(texts.parents);
    const short = root.querySelector('p.kid-short')!;
    expect(short.textContent).toBe(texts.short);
    // d3-k-delete-button-before-info: der Kindersatz steht unter „Was entfernt wird", direkt vor dem Eltern-Aufklapper.
    expect(short.previousElementSibling!.classList).toContain('removed-title');
    expect(short.nextElementSibling).toBe(details);
    // Uebriges bleibt stehen: Knopf, „Was bleibt", Ruecklink am Ende.
    expect(root.querySelector('details .delete-now, details .kept, details .back')).toBeNull();
    expect(root.querySelector('mat-card-content')!.lastElementChild!.classList).toContain('back');
  });

  // a1-k-deletion-fachjargon: Anleitung + „Vorher sichern" (PGN, Repertoires, ☰-Pfad) über dem Knopf zugeklappt.
  it('Anleitung samt „Vorher sichern" in ein zweites zugeklapptes Eltern-<details>, der Knopf bleibt sichtbar', () => {
    const root = page();
    foldAccountDeletion(root, texts);
    const content = root.querySelector('mat-card-content')!;
    const how = content.querySelector('details.kid-parent:has(.kid-parent-howto-label)')!;
    expect(how).not.toBeNull();
    expect(how.hasAttribute('open')).toBeFalse();
    expect(how.querySelector('summary .kid-parent-howto-label')!.textContent).toBe(texts.howTo);
    expect(Array.from(how.children).slice(1).map(c => c.className)).toEqual(['how-title', 'how', 'backup', 'backup-links']);
    expect(Array.from(content.children).map(c => c.tagName === 'DETAILS' ? 'details' : c.className))
      .toEqual(['intro', 'details', 'removed-title', 'kid-short', 'details', 'kept-title', 'kept', 'action', 'contact-title', 'contact', 'back']);
    // Fachbegriffe sind nicht sichtbar, solange zu (gerendert im Dokument gemessen).
    document.body.appendChild(root);
    try {
      for (const sel of ['.how', '.backup', '.backup-links', 'ul.removed']) {
        expect((root.querySelector(sel) as HTMLElement).checkVisibility()).withContext(sel).toBeFalse();
      }
      expect((root.querySelector('.delete-now') as HTMLElement).checkVisibility()).toBeTrue();
    } finally {
      root.remove();
    }
  });

  // d3-k-delete-button-before-info: erst was verloren geht, dann der Knopf — ohne Kontakt-Abschnitt vor dem Rücklink.
  it('Knopf nach „Was bleibt"; fehlt der Kontakt-Abschnitt, steht er vor dem Rücklink', () => {
    const root = page();
    root.querySelector('.contact-title')!.remove();
    root.querySelector('.contact')!.remove();
    foldAccountDeletion(root, texts);
    const kids = Array.from(root.querySelector('mat-card-content')!.children).map(c => c.tagName === 'DETAILS' ? 'details' : c.className);
    expect(kids.slice(-3)).toEqual(['kept', 'action', 'back']);
  });

  it('Sprachwechsel setzt die Texte neu; ein zweiter Aufruf aendert nichts; andere Seiten bleiben unberuehrt', () => {
    const root = page();
    foldAccountDeletion(root, texts);
    expect(foldAccountDeletion(root, texts)).toBeFalse();
    expect(root.querySelectorAll('details').length).toBe(2);
    setAccountDeletionTexts(root, { short: 'Everything is deleted.', parents: 'For parents', howTo: 'For parents: how' });
    expect(root.querySelector('.kid-short')!.textContent).toBe('Everything is deleted.');
    expect(root.querySelector('.kid-parent-label')!.textContent).toBe('For parents');
    expect(root.querySelector('.kid-parent-howto-label')!.textContent).toBe('For parents: how');
    expect(foldAccountDeletion(document.createElement('div'), texts)).toBeFalse();
  });

  it('isAccountDeletionUrl / isLegalUrl', () => {
    expect(isAccountDeletionUrl('/account-deletion')).toBeTrue();
    expect(isAccountDeletionUrl('/account-deletion?x=1')).toBeTrue();
    expect(isAccountDeletionUrl('/privacy')).toBeFalse();
    expect(isLegalUrl('/privacy#a')).toBeTrue();
    expect(isLegalUrl('/account-deletion/')).toBeTrue();
    expect(isLegalUrl('/login')).toBeFalse();
  });
});
