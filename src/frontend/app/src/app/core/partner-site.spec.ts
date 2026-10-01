import { accountHomeUrl, leagueHubUrl, partnerSiteUrl, rookHubUrlForLeagueHub, sharedCookieDomain, siteKindOf } from './partner-site';

/**
 * Die Turnierseite heisst auf Prod `tournament`, auf Dev weiter `turnier-dev` — und ein alter
 * `turnier`-Link soll nicht ins Leere laufen. Erkannt werden muss also jede Schreibweise, und der
 * SPRUNG muss je Umgebung die richtige treffen.
 */
describe('partner-site', () => {
  it('erkennt beide Seiten in allen Schreibweisen', () => {
    expect(siteKindOf('rookhub.oberschmid.homes')).toBe('rookhub');
    expect(siteKindOf('rookhub-dev.oberschmid.homes')).toBe('rookhub');
    expect(siteKindOf('tournament.oberschmid.homes')).toBe('turnier');
    expect(siteKindOf('turnier.oberschmid.homes')).toBe('turnier');
    expect(siteKindOf('turnier-dev.oberschmid.homes')).toBe('turnier');
    expect(siteKindOf('localhost')).toBeNull();
    expect(siteKindOf('10.24.13.6')).toBeNull();
  });

  it('springt je Umgebung auf die richtige Schwesterseite', () => {
    expect(partnerSiteUrl('rookhub.oberschmid.homes', 'https:'))
      .toBe('https://tournament.oberschmid.homes');
    expect(partnerSiteUrl('tournament.oberschmid.homes', 'https:'))
      .toBe('https://rookhub.oberschmid.homes');
    expect(partnerSiteUrl('rookhub-dev.oberschmid.homes', 'https:'))
      .toBe('https://turnier-dev.oberschmid.homes');
    expect(partnerSiteUrl('turnier-dev.oberschmid.homes', 'https:'))
      .toBe('https://rookhub-dev.oberschmid.homes');
  });

  it('LeagueHub gehört zu RookHub derselben Umgebung — sonst gibt es keins', () => {
    expect(leagueHubUrl('rookhub.oberschmid.homes', 'https:')).toBe('https://leaguehub.oberschmid.homes');
    expect(leagueHubUrl('rookhub-dev.oberschmid.homes', 'https:')).toBe('https://leaguehub-dev.oberschmid.homes');
    expect(leagueHubUrl('tournament.oberschmid.homes', 'https:')).toBeNull();
    expect(leagueHubUrl('localhost', 'http:')).toBeNull();
    expect(rookHubUrlForLeagueHub('leaguehub.oberschmid.homes', 'https:')).toBe('https://rookhub.oberschmid.homes');
    expect(rookHubUrlForLeagueHub('leaguehub-dev.oberschmid.homes', 'https:')).toBe('https://rookhub-dev.oberschmid.homes');
    expect(rookHubUrlForLeagueHub('rookhub.oberschmid.homes', 'https:')).toBeNull();
  });

  it('kennt zu jeder Oberfläche das RookHub derselben Umgebung — dort wird das Konto gelöscht (UX-023)', () => {
    for (const [host, home] of [
      ['kidhub.oberschmid.homes', 'https://rookhub.oberschmid.homes'],
      ['kidhub-dev.oberschmid.homes', 'https://rookhub-dev.oberschmid.homes'],
      ['leaguehub.oberschmid.homes', 'https://rookhub.oberschmid.homes'],
      ['leaguehub-dev.oberschmid.homes', 'https://rookhub-dev.oberschmid.homes'],
      ['clubhub.oberschmid.homes', 'https://rookhub.oberschmid.homes'],
      ['clubhub-dev.oberschmid.homes', 'https://rookhub-dev.oberschmid.homes'],
      ['tournament.oberschmid.homes', 'https://rookhub.oberschmid.homes'],
      ['turnier-dev.oberschmid.homes', 'https://rookhub-dev.oberschmid.homes'],
    ]) {
      expect(accountHomeUrl(host, 'https:')).withContext(host).toBe(home);
    }
    // RookHub selbst und Adressen ohne Familie: kein Link statt einer geratenen Adresse.
    expect(accountHomeUrl('rookhub.oberschmid.homes', 'https:')).toBeNull();
    expect(accountHomeUrl('localhost', 'http:')).toBeNull();
    expect(accountHomeUrl('10.24.13.6', 'http:')).toBeNull();
  });

  it('bietet ohne gemeinsame Elterndomaene keinen Sprung und kein Cookie', () => {
    expect(partnerSiteUrl('localhost', 'http:')).toBeNull();
    expect(sharedCookieDomain('localhost')).toBeNull();
    expect(sharedCookieDomain('tournament.oberschmid.homes')).toBe('.oberschmid.homes');
  });

  it('KidHub teilt die Anzeige-Einstellungen, ist aber keine Partnerseite', () => {
    expect(sharedCookieDomain('kidhub.oberschmid.homes')).toBe('.oberschmid.homes');
    expect(sharedCookieDomain('kidhub-dev.oberschmid.homes')).toBe('.oberschmid.homes');
    expect(siteKindOf('kidhub.oberschmid.homes')).toBeNull();
    expect(partnerSiteUrl('kidhub.oberschmid.homes', 'https:')).toBeNull();
    // LeagueHub genauso: teilt Sprache/Design, ist aber keine Partnerseite (kein Sprung)
    expect(sharedCookieDomain('leaguehub.oberschmid.homes')).toBe('.oberschmid.homes');
    expect(sharedCookieDomain('leaguehub-dev.oberschmid.homes')).toBe('.oberschmid.homes');
    expect(sharedCookieDomain('clubhub.oberschmid.homes')).toBe('.oberschmid.homes');
    expect(sharedCookieDomain('clubhub-dev.oberschmid.homes')).toBe('.oberschmid.homes');
    expect(siteKindOf('clubhub.oberschmid.homes')).toBeNull();
    expect(siteKindOf('leaguehub.oberschmid.homes')).toBeNull();
    expect(partnerSiteUrl('leaguehub.oberschmid.homes', 'https:')).toBeNull();
  });
});
