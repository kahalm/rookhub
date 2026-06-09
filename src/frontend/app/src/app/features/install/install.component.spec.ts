import { APK_DOWNLOAD_URL } from './install.component';

describe('APK_DOWNLOAD_URL', () => {
  it('zeigt auf das jeweils neueste GitHub-Release (kein hartkodierter Versions-Tag)', () => {
    expect(APK_DOWNLOAD_URL).toBe(
      'https://github.com/kahalm/rookhub/releases/latest/download/app-release-signed.apk',
    );
    expect(APK_DOWNLOAD_URL).not.toMatch(/v\d+\.\d+\.\d+/);
  });
});
