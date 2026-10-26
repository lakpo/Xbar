// Regenere src/Xbar/Icons/mdi.txt.gz depuis le paquet npm @mdi/js (Material Design Icons).
//
//   cd build/icons && npm pack @mdi/js && tar xzf mdi-js-*.tgz && node generate-icons.mjs
//
// Format : une ligne par icone, "nom<TAB>donnees de chemin SVG" (viewBox 24x24), compressee en gzip.
import { readFileSync, writeFileSync, copyFileSync } from 'node:fs';
import { gzipSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, 'package', 'mdi.js'), 'utf8');
const version = /Material Design Icons v([\d.]+)/.exec(source)?.[1] ?? '?';

const toKebab = (id) => id
    .replace(/^mdi/, '')
    .replace(/([a-z])([A-Z0-9])/g, '$1-$2')
    .replace(/([0-9])([A-Z])/g, '$1-$2')
    .toLowerCase();

const lines = [];
for (const [, id, path] of source.matchAll(/export var (\w+) = "([^"]+)";/g))
    lines.push(`${toKebab(id)}\t${path}`);

const out = join(here, '..', '..', 'src', 'Xbar', 'Icons');
writeFileSync(join(out, 'mdi.txt.gz'), gzipSync(lines.join('\n'), { level: 9 }));
copyFileSync(join(here, 'package', 'LICENSE'), join(out, 'MDI-LICENSE.txt'));
console.log(`Material Design Icons ${version} : ${lines.length} icones`);
