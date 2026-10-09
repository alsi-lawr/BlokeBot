import { mkdir, mkdtemp, readFile, readdir, rename, rm, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const destination = join(root, 'wwwroot/vendor/full-overlay-editor');
const files = new Map();

async function collectModules(source, target) {
    for (const entry of await readdir(source, { withFileTypes: true })) {
        const from = join(source, entry.name), to = join(target, entry.name);
        if (entry.isDirectory()) await collectModules(from, to);
        else if (entry.name.endsWith('.js')) files.set(to, await readFile(from));
    }
}

await collectModules(join(root, 'node_modules/parse5/dist'), join(destination, 'parse5'));
await collectModules(join(root, 'node_modules/entities/dist'), join(destination, 'entities'));
files.set(join(destination, 'css-tree/csstree.esm.js'), await readFile(join(root, 'node_modules/css-tree/dist/csstree.esm.js')));

const notices = [];
for (const name of ['parse5', 'entities', 'css-tree', 'mdn-data', 'source-map-js']) {
    const directory = join(root, 'node_modules', name);
    const manifest = JSON.parse(await readFile(join(directory, 'package.json'), 'utf8'));
    const license = (await readdir(directory)).find(file => /^licen[cs]e(?:\.|$)/i.test(file));
    notices.push(`${manifest.name} ${manifest.version} (${manifest.license})\n${await readFile(join(directory, license), 'utf8')}`);
}
files.set(join(destination, 'THIRD-PARTY-NOTICES.txt'), Buffer.from(notices.join('\n\n')));

let staging;
async function writeChanged(file, content) {
    let current;
    try { current = await readFile(file); }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
    if (current?.equals(content)) return;
    if (!staging) {
        await mkdir(join(root, 'obj'), { recursive: true });
        staging = await mkdtemp(join(root, 'obj/full-overlay-editor-assets-'));
    }
    const temporary = join(staging, 'asset');
    await writeFile(temporary, content);
    await mkdir(dirname(file), { recursive: true });
    await rename(temporary, file);
}

async function pruneObsolete(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
        const file = join(directory, entry.name);
        if (entry.isDirectory()) await pruneObsolete(file);
        else if (entry.isFile() && !files.has(file)) await rm(file, { force: true });
    }
}

try {
    await mkdir(destination, { recursive: true });
    for (const [file, content] of files) await writeChanged(file, content);
    await pruneObsolete(destination);
} finally {
    if (staging) await rm(staging, { recursive: true, force: true });
}
