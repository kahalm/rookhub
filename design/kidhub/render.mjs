// Rendert die Symbole der Kinderseite aus den SVG-Vorlagen dieses Ordners nach
// src/frontend/app/public-kidhub/ (Rezept: public-kidhub/ASSETS.md).
// Aufruf aus src/frontend/app:  node ../../../design/kidhub/render.mjs
import { readFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
// playwright-core liegt in den node_modules des Frontends, nicht neben diesem Skript.
const { chromium } = await import(join(here, '..', '..', 'src', 'frontend', 'app', 'node_modules', 'playwright-core', 'index.mjs'));
const out = join(here, '..', '..', 'src', 'frontend', 'app', 'public-kidhub');
mkdirSync(join(out, 'icons'), { recursive: true });

const jobs = [
  ['icon.svg', 512, 512, 'icons/icon-512.png'],
  ['icon.svg', 192, 192, 'icons/icon-192.png'],
  ['icon.svg', 180, 180, 'icons/apple-touch-icon.png'],
  ['icon.svg', 48, 48, 'favicon-48.png'],
  ['icon-maskable.svg', 512, 512, 'icons/icon-512-maskable.png'],
  ['icon-maskable.svg', 192, 192, 'icons/icon-192-maskable.png'],
  ['og.svg', 1200, 630, 'og-image.png'],
];

const browser = await chromium.launch();
const page = await browser.newPage();
for (const [src, w, h, target] of jobs) {
  const svg = readFileSync(join(here, src), 'utf8');
  await page.setViewportSize({ width: w, height: h });
  await page.setContent(`<html><body style="margin:0;background:transparent">${svg
    .replace(/width="\d+" height="\d+"/, `width="${w}" height="${h}"`)}</body></html>`);
  await page.screenshot({ path: join(out, target), omitBackground: true, clip: { x: 0, y: 0, width: w, height: h } });
  console.log(target);
}
await browser.close();
