import { HttpErrorResponse } from '@angular/common/http';
import { TranslateService } from '@ngx-translate/core';
import { apiErrorCodeText, apiErrorText } from './api-error';

/** Fake TranslateService: kennt nur die Schlüssel aus `known`, sonst kommt der Schlüssel zurück (wie ngx-translate). */
function fakeTranslate(known: Record<string, string>): TranslateService {
  return { instant: (key: string) => known[key] ?? key } as unknown as TranslateService;
}

const t = fakeTranslate({
  'apiErrors.friendship_exists': 'Ihr seid bereits befreundet, oder eine Anfrage ist schon offen.',
  'friends.errors.sendRequest': 'Anfrage konnte nicht gesendet werden',
});

function httpError(body: unknown, status = 409): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: body });
}

describe('apiErrorText (F5-019)', () => {
  it('übersetzt einen bekannten Code statt die englische Servermeldung zu zeigen', () => {
    const err = httpError({ message: 'A friendship or request already exists.', code: 'friendship_exists' });
    expect(apiErrorText(err, t, 'friends.errors.sendRequest'))
      .toBe('Ihr seid bereits befreundet, oder eine Anfrage ist schon offen.');
  });

  it('unbekannter Code → wie bisher die Servermeldung', () => {
    const err = httpError({ message: 'Something new.', code: 'not_yet_translated' });
    expect(apiErrorText(err, t, 'friends.errors.sendRequest')).toBe('Something new.');
  });

  it('ohne Code → Servermeldung, ohne beides → Ersatztext', () => {
    expect(apiErrorText(httpError({ message: 'Group not found.' }, 404), t, 'friends.errors.sendRequest'))
      .toBe('Group not found.');
    expect(apiErrorText(httpError(null, 500), t, 'friends.errors.sendRequest'))
      .toBe('Anfrage konnte nicht gesendet werden');
    expect(apiErrorText(httpError({ message: '  ' }, 500), t, 'friends.errors.sendRequest'))
      .toBe('Anfrage konnte nicht gesendet werden');
    expect(apiErrorText(undefined, t, 'friends.errors.sendRequest')).toBe('Anfrage konnte nicht gesendet werden');
  });

  it('nimmt nur snake_case-Codes als Schlüssel (kein Pfad in den i18n-Baum)', () => {
    expect(apiErrorCodeText(httpError({ code: 'friends.errors' }), fakeTranslate({ 'apiErrors.friends.errors': 'x' })))
      .toBeNull();
    expect(apiErrorCodeText(httpError({ code: 42 }), t)).toBeNull();
    expect(apiErrorCodeText(httpError({ code: 'friendship_exists' }), t))
      .toBe('Ihr seid bereits befreundet, oder eine Anfrage ist schon offen.');
  });
});
