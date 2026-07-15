#!/usr/bin/env node

import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

export const deviceCatalog = {
  laptop: {
    viewport: { width: 1180, height: 720 },
    frame: { width: 1360, height: 900 },
    mobile: false,
  },
  phone: {
    viewport: { width: 390, height: 844 },
    frame: { width: 560, height: 1040 },
    mobile: true,
  },
};

function selectedDevices(names) {
  return names.map((name) => {
    if (!deviceCatalog[name])
      throw new Error(`Unknown capture device: ${name}.`);
    return name;
  });
}

function requireUniqueNames(cases, label) {
  if (cases.length === 0) throw new Error(`${label} matrix is empty.`);
  if (new Set(cases.map(({ name }) => name)).size !== cases.length) {
    throw new Error(`${label} matrix expands to duplicate file names.`);
  }
}

export async function loadCaptureMatrix(path) {
  const document = JSON.parse(await readFile(path, "utf8"));
  const screenshots = document.screenshots.flatMap(
    ({ themes, views, devices }) =>
      themes.flatMap((theme) =>
        views.flatMap((view) =>
          selectedDevices(devices).map((device) => ({
            name: `${device}-${theme}-${view}`,
            device,
            theme,
            view,
          })),
        ),
      ),
  );
  const animations = document.animations.flatMap(({ themes, entries }) =>
    themes.flatMap((theme) =>
      entries.flatMap(({ name, view, kind, devices }) =>
        selectedDevices(devices).map((device) => ({
          name: `${device}-${theme}-${name}`,
          device,
          theme,
          view,
          kind,
        })),
      ),
    ),
  );

  requireUniqueNames(screenshots, "Screenshot");
  requireUniqueNames(animations, "Animation");
  return { screenshots, animations };
}

async function main() {
  const [kind, path] = process.argv.slice(2);
  if (!path || !["screenshots", "animations"].includes(kind)) {
    throw new Error(
      "Usage: simulation-capture-matrix.mjs screenshots|animations MATRIX",
    );
  }

  const matrix = await loadCaptureMatrix(resolve(path));
  if (kind === "animations") {
    process.stdout.write(
      `${matrix.animations.map(({ name }) => name).join("\n")}\n`,
    );
    return;
  }

  const rows = matrix.screenshots.map(({ name, device, theme, view }) => {
    const { viewport, frame } = deviceCatalog[device];
    return [
      name,
      device,
      theme,
      view,
      viewport.width,
      viewport.height,
      frame.width,
      frame.height,
    ].join("\t");
  });
  process.stdout.write(`${rows.join("\n")}\n`);
}

const invokedPath =
  process.argv[1] && pathToFileURL(resolve(process.argv[1])).href;
if (import.meta.url === invokedPath) {
  main().catch((error) => {
    process.stderr.write(`${error.stack ?? error.message}\n`);
    process.exitCode = 1;
  });
}
