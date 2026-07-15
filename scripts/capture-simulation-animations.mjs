#!/usr/bin/env node

import { execFile, spawn } from "node:child_process";
import { once } from "node:events";
import { mkdir, open, readFile, unlink, writeFile } from "node:fs/promises";
import { basename, join, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { promisify } from "node:util";

import {
  deviceCatalog,
  loadCaptureMatrix,
} from "./simulation-capture-matrix.mjs";

const execute = promisify(execFile);
const framesPerSecond = 30;

function parseArguments(arguments_) {
  const values = new Map();
  for (let index = 0; index < arguments_.length; index += 2) {
    const key = arguments_[index];
    const value = arguments_[index + 1];
    if (!key?.startsWith("--") || value === undefined) {
      throw new Error(`Invalid argument near ${key ?? "end of command"}.`);
    }
    values.set(key.slice(2), value);
  }

  const required = (key) => {
    const value = values.get(key);
    if (!value) throw new Error(`Missing --${key}.`);
    return value;
  };

  return {
    baseUrl: required("base-url"),
    browser: required("browser"),
    matrix: resolve(required("matrix")),
    frameTemplate: resolve(required("frame-template")),
    framesDirectory: resolve(required("frames")),
    outputDirectory: resolve(required("output")),
    profileDirectory: resolve(required("profile")),
    browserLog: resolve(required("browser-log")),
    only: values.get("only"),
  };
}

function sleep(milliseconds) {
  return new Promise((resolvePromise) =>
    setTimeout(resolvePromise, milliseconds),
  );
}

class CdpConnection {
  constructor(socket) {
    this.socket = socket;
    this.nextId = 1;
    this.pending = new Map();

    socket.addEventListener("message", (event) => {
      const message = JSON.parse(event.data);
      if (!message.id) return;

      const pending = this.pending.get(message.id);
      if (!pending) return;

      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(message.error.message));
      else pending.resolve(message.result ?? {});
    });

    socket.addEventListener("close", () => {
      for (const pending of this.pending.values()) {
        pending.reject(new Error("Chromium DevTools connection closed."));
      }
      this.pending.clear();
    });
  }

  static async connect(url) {
    const socket = new WebSocket(url);
    await new Promise((resolvePromise, reject) => {
      socket.addEventListener("open", resolvePromise, { once: true });
      socket.addEventListener(
        "error",
        () => reject(new Error("Could not connect to Chromium DevTools.")),
        { once: true },
      );
    });
    return new CdpConnection(socket);
  }

  send(method, parameters = {}) {
    const id = this.nextId++;
    return new Promise((resolvePromise, reject) => {
      this.pending.set(id, { resolve: resolvePromise, reject });
      this.socket.send(JSON.stringify({ id, method, params: parameters }));
    });
  }

  close() {
    this.socket.close();
  }
}

async function waitForDevTools(profileDirectory, browserProcess) {
  const activePortFile = join(profileDirectory, "DevToolsActivePort");
  for (let attempt = 0; attempt < 200; attempt += 1) {
    if (browserProcess.exitCode !== null) {
      throw new Error(`Chromium exited with code ${browserProcess.exitCode}.`);
    }

    try {
      const [port] = (await readFile(activePortFile, "utf8"))
        .trim()
        .split("\n");
      if (port) return Number(port);
    } catch {
      // Chromium creates the file after its DevTools endpoint is ready.
    }
    await sleep(50);
  }

  throw new Error("Chromium did not expose a DevTools port.");
}

async function createPageConnection(port) {
  const endpoint = `http://127.0.0.1:${port}/json/new?${encodeURIComponent("about:blank")}`;
  const response = await fetch(endpoint, { method: "PUT" });
  if (!response.ok) {
    throw new Error(
      `Could not create a Chromium page: HTTP ${response.status}.`,
    );
  }
  const target = await response.json();
  return CdpConnection.connect(target.webSocketDebuggerUrl);
}

async function evaluate(connection, expression) {
  const response = await connection.send("Runtime.evaluate", {
    expression,
    awaitPromise: true,
    returnByValue: true,
  });
  if (response.exceptionDetails) {
    throw new Error(
      response.exceptionDetails.exception?.description ??
        response.exceptionDetails.text ??
        "Browser evaluation failed.",
    );
  }
  return response.result?.value;
}

async function waitForExpression(connection, expression, description) {
  const deadline = Date.now() + 15_000;
  let lastError;
  while (Date.now() < deadline) {
    try {
      if (await evaluate(connection, expression)) return;
    } catch (error) {
      lastError = error;
    }
    await sleep(100);
  }

  throw new Error(
    `Timed out waiting for ${description}.${lastError ? ` ${lastError.message}` : ""}`,
  );
}

async function navigate(connection, url) {
  await connection.send("Page.navigate", { url });
  await waitForExpression(
    connection,
    "document.readyState === 'complete' && Boolean(document.body)",
    `page load at ${url}`,
  );
}

async function setViewport(connection, device) {
  const viewport = device.viewport;
  await connection.send("Emulation.setDeviceMetricsOverride", {
    width: viewport.width,
    height: viewport.height,
    screenWidth: viewport.width,
    screenHeight: viewport.height,
    deviceScaleFactor: 1,
    mobile: device.mobile,
  });
  await connection.send("Emulation.setTouchEmulationEnabled", {
    enabled: device.mobile,
    maxTouchPoints: device.mobile ? 5 : 1,
  });
  await connection.send("Emulation.setDefaultBackgroundColorOverride", {});
}

async function setFrameViewport(connection, device) {
  const viewport = device.frame;
  await connection.send("Emulation.setDeviceMetricsOverride", {
    width: viewport.width,
    height: viewport.height,
    screenWidth: viewport.width,
    screenHeight: viewport.height,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await connection.send("Emulation.setTouchEmulationEnabled", {
    enabled: false,
    maxTouchPoints: 1,
  });
  await connection.send("Emulation.setDefaultBackgroundColorOverride", {
    color: { r: 0, g: 0, b: 0, a: 0 },
  });
}

async function capture(connection, destination) {
  const result = await connection.send("Page.captureScreenshot", {
    format: "png",
    fromSurface: true,
    captureBeyondViewport: false,
  });
  await writeFile(destination, Buffer.from(result.data, "base64"));
}

async function openSimulation(connection, baseUrl, view, theme, device) {
  await setViewport(connection, device);
  await navigate(
    connection,
    `${baseUrl}/simulation/login?view=${encodeURIComponent(view)}&theme=${theme}`,
  );
  await waitForExpression(
    connection,
    "document.body.innerText.includes('Sample Channel') && Boolean(document.querySelector('article'))",
    `${view} simulation content`,
  );
  await sleep(350);
}

async function addTimedFrame(connection, directory, frames, ticks) {
  const path = join(directory, `${String(frames.length).padStart(3, "0")}.png`);
  await capture(connection, path);
  frames.push({ path, ticks });
}

async function installTouchIndicator(connection, enabled) {
  if (!enabled) return;

  await evaluate(
    connection,
    `(() => {
      const indicator = document.createElement("div");
      indicator.id = "blokebot-simulation-touch";
      indicator.setAttribute("aria-hidden", "true");
      indicator.style.cssText = [
        "position:fixed",
        "z-index:2147483647",
        "width:42px",
        "height:42px",
        "border:2px solid rgba(255,255,255,0.92)",
        "border-radius:999px",
        "background:rgba(148,163,184,0.22)",
        "box-shadow:0 0 0 2px rgba(15,23,42,0.68),0 4px 12px rgba(15,23,42,0.3)",
        "opacity:0",
        "pointer-events:none",
        "transform:translate(-50%,-50%) scale(0.9)",
      ].join(";");
      document.body.append(indicator);
    })()`,
  );
}

async function setTouchIndicator(connection, visible, x = 0, y = 0) {
  await evaluate(
    connection,
    `(() => {
      const indicator = document.querySelector("#blokebot-simulation-touch");
      if (!indicator) return;
      indicator.style.left = ${JSON.stringify(`${x}px`)};
      indicator.style.top = ${JSON.stringify(`${y}px`)};
      indicator.style.opacity = ${visible ? '"1"' : '"0"'};
    })()`,
  );
}

async function captureHomeScroll(connection, directory, touchEnabled) {
  const metrics = await evaluate(
    connection,
    `({
      width: window.innerWidth,
      height: window.innerHeight,
      maximumScroll: Math.max(0, document.documentElement.scrollHeight - window.innerHeight),
    })`,
  );
  await installTouchIndicator(connection, touchEnabled);

  const frames = [];
  await setTouchIndicator(connection, false);
  await addTimedFrame(connection, directory, frames, 24);

  const captureGesture = async (startScroll, endScroll) => {
    const startY = metrics.height * 0.78;
    const endY = metrics.height * 0.42;
    const x = metrics.width * 0.78;
    for (let step = 0; step <= 20; step += 1) {
      const progress = step / 20;
      const eased = (1 - Math.cos(Math.PI * progress)) / 2;
      const scroll = Math.round(
        startScroll + (endScroll - startScroll) * eased,
      );
      const touchY = Math.round(startY + (endY - startY) * eased);
      await evaluate(connection, `window.scrollTo(0, ${scroll})`);
      await setTouchIndicator(connection, touchEnabled, Math.round(x), touchY);
      await sleep(20);
      await addTimedFrame(connection, directory, frames, 1);
    }
  };

  const firstStop = Math.round(metrics.maximumScroll * 0.48);
  await captureGesture(0, firstStop);
  await setTouchIndicator(connection, false);
  await addTimedFrame(connection, directory, frames, 8);
  await captureGesture(firstStop, metrics.maximumScroll);
  await setTouchIndicator(connection, false);
  await addTimedFrame(connection, directory, frames, 15);
  return frames;
}

async function clickButton(connection, label, settleMilliseconds = 350) {
  const clicked = await evaluate(
    connection,
    `(() => {
      const button = [...document.querySelectorAll("button")]
        .find(candidate => candidate.textContent.trim() === ${JSON.stringify(label)});
      if (!button) return false;
      button.click();
      return true;
    })()`,
  );
  if (!clicked) throw new Error(`Button not found: ${label}.`);
  await sleep(settleMilliseconds);
}

async function armCssAnimationCapture(connection) {
  await evaluate(
    connection,
    `(() => {
      window.blokeBotAnimationObserver?.disconnect();
      window.blokeBotCapturedAnimations = [];
      let scheduled = false;
      const captureAnimations = () => {
        scheduled = false;
        const animations = document.getAnimations({ subtree: true })
          .filter(animation => {
            const endTime = animation.effect?.getComputedTiming().endTime;
            return animation.playState === "running" && Number.isFinite(endTime) && endTime > 0;
          });
        if (animations.length === 0) return;
        for (const animation of animations) animation.pause();
        window.blokeBotCapturedAnimations = animations;
        window.blokeBotAnimationObserver.disconnect();
      };
      const observer = new MutationObserver(() => {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(captureAnimations);
      });
      window.blokeBotAnimationObserver = observer;
      observer.observe(document.documentElement, {
        attributes: true,
        childList: true,
        subtree: true,
      });
    })()`,
  );
}

async function captureCssTransition(connection, directory, frames) {
  await waitForExpression(
    connection,
    "window.blokeBotCapturedAnimations?.length > 0",
    "guessing dashboard CSS animations",
  );
  const sampleCount = 7;
  for (let frame = 0; frame < sampleCount; frame += 1) {
    const progress = frame / (sampleCount - 1);
    await evaluate(
      connection,
      `(() => {
        for (const animation of window.blokeBotCapturedAnimations) {
          const endTime = animation.effect.getComputedTiming().endTime;
          animation.currentTime = endTime * ${progress};
        }
      })()`,
    );
    await addTimedFrame(connection, directory, frames, 1);
  }
  await evaluate(
    connection,
    `(() => {
      for (const animation of window.blokeBotCapturedAnimations) {
        const endTime = animation.effect.getComputedTiming().endTime;
        animation.currentTime = endTime;
      }
      window.blokeBotCapturedAnimations = [];
    })()`,
  );
}

async function captureGuessingWorkflow(connection, directory) {
  const frames = [];

  await addTimedFrame(connection, directory, frames, 18);
  await armCssAnimationCapture(connection);
  await clickButton(connection, "History", 0);
  await captureCssTransition(connection, directory, frames);
  await waitForExpression(
    connection,
    "Boolean(document.querySelector('#history'))",
    "guessing history",
  );
  await addTimedFrame(connection, directory, frames, 10);
  await armCssAnimationCapture(connection);
  await clickButton(connection, "Leaderboard", 0);
  await captureCssTransition(connection, directory, frames);
  await waitForExpression(
    connection,
    "Boolean(document.querySelector('#leaderboard'))",
    "guessing leaderboard",
  );
  await addTimedFrame(connection, directory, frames, 8);
  const scrollRange = await evaluate(
    connection,
    `(() => {
      const target = document.querySelector("#leaderboard");
      return {
        start: window.scrollY,
        end: target ? target.getBoundingClientRect().top + window.scrollY : window.scrollY,
      };
    })()`,
  );
  for (let step = 1; step <= 18; step += 1) {
    const progress = step / 18;
    const eased = (1 - Math.cos(Math.PI * progress)) / 2;
    const scroll = Math.round(
      scrollRange.start + (scrollRange.end - scrollRange.start) * eased,
    );
    await evaluate(connection, `window.scrollTo(0, ${scroll})`);
    await sleep(20);
    await addTimedFrame(connection, directory, frames, 1);
  }
  await evaluate(
    connection,
    `(() => {
      const input = document.querySelector('#leaderboardUsername');
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
      setter.call(input, "nightowl");
      input.dispatchEvent(new Event("input", { bubbles: true }));
    })()`,
  );
  await sleep(500);
  await addTimedFrame(connection, directory, frames, 14);
  await armCssAnimationCapture(connection);
  await clickButton(connection, "Live", 0);
  await captureCssTransition(connection, directory, frames);
  await waitForExpression(
    connection,
    "document.body.innerText.includes('Run a round')",
    "live guessing dashboard",
  );
  await evaluate(connection, "window.scrollTo(0, 0)");
  await sleep(150);
  await addTimedFrame(connection, directory, frames, 18);
  return frames;
}

async function setFieldByLabel(connection, label, value) {
  const changed = await evaluate(
    connection,
    `(() => {
      const label = [...document.querySelectorAll("label")]
        .find(candidate => candidate.textContent.trim() === ${JSON.stringify(label)});
      const input = label && document.getElementById(label.htmlFor);
      if (!(input instanceof HTMLInputElement)) return false;
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
      setter.call(input, ${JSON.stringify(value)});
      input.dispatchEvent(new Event("input", { bubbles: true }));
      return true;
    })()`,
  );
  if (!changed) throw new Error(`Field not found: ${label}.`);
  await sleep(180);
}

async function elementCentre(connection, selectorExpression) {
  const centre = await evaluate(
    connection,
    `(() => {
      const element = ${selectorExpression};
      if (!element) return null;
      const bounds = element.getBoundingClientRect();
      return { x: bounds.left + bounds.width / 2, y: bounds.top + bounds.height / 2 };
    })()`,
  );
  if (!centre) throw new Error("Animation interaction target not found.");
  return centre;
}

async function capturePointsWorkflow(connection, directory, touchEnabled) {
  const frames = [];
  await installTouchIndicator(connection, touchEnabled);

  await evaluate(
    connection,
    `(() => {
      const heading = [...document.querySelectorAll("h2")]
        .find(candidate => candidate.textContent.trim() === "Viewer points");
      const section = heading?.closest("section");
      const topbarHeight = document.querySelector(".app-shell__topbar")?.getBoundingClientRect().height ?? 0;
      const pageHeaderHeight = document.querySelector(".page-header")?.getBoundingClientRect().height ?? 0;
      if (section) {
        const sectionTop = section.getBoundingClientRect().top + window.scrollY;
        window.scrollTo(0, Math.max(0, sectionTop - topbarHeight - pageHeaderHeight - 16));
      }
    })()`,
  );
  await sleep(150);
  await addTimedFrame(connection, directory, frames, 18);

  const fieldCentre = await elementCentre(
    connection,
    `(() => {
      const label = [...document.querySelectorAll("label")]
        .find(candidate => candidate.textContent.trim() === "Find viewer");
      return label && document.getElementById(label.htmlFor);
    })()`,
  );
  await setTouchIndicator(
    connection,
    touchEnabled,
    fieldCentre.x,
    fieldCentre.y,
  );
  await addTimedFrame(connection, directory, frames, 3);
  await setTouchIndicator(connection, false);
  await setFieldByLabel(connection, "Find viewer", "n");
  await addTimedFrame(connection, directory, frames, 4);
  await setFieldByLabel(connection, "Find viewer", "night");
  await addTimedFrame(connection, directory, frames, 4);
  await setFieldByLabel(connection, "Find viewer", "nightowl");
  await addTimedFrame(connection, directory, frames, 8);

  const searchCentre = await elementCentre(
    connection,
    `[...document.querySelectorAll("button")]
      .find(candidate => candidate.textContent.trim() === "Search")`,
  );
  await setTouchIndicator(
    connection,
    touchEnabled,
    searchCentre.x,
    searchCentre.y,
  );
  await addTimedFrame(connection, directory, frames, 3);
  await clickButton(connection, "Search");
  await setTouchIndicator(connection, false);
  await waitForExpression(
    connection,
    "document.body.innerText.includes('1,840 points')",
    "viewer point balance",
  );
  await addTimedFrame(connection, directory, frames, 24);
  return frames;
}

async function frameAnimation(
  connection,
  frameTemplate,
  deviceName,
  device,
  rawFrames,
  destination,
) {
  await setFrameViewport(connection, device);
  const framedDirectory = join(destination, "framed");
  await mkdir(framedDirectory, { recursive: true });

  const pageUrl = new URL(pathToFileURL(frameTemplate));
  pageUrl.searchParams.set("device", deviceName);
  pageUrl.searchParams.set("image", pathToFileURL(rawFrames[0].path).href);
  pageUrl.searchParams.set("animation", "true");
  await navigate(connection, pageUrl.href);
  await waitForExpression(
    connection,
    "document.documentElement.dataset.frameReady === 'true'",
    "animation frame canvas",
  );

  const framed = [];
  for (const [index, rawFrame] of rawFrames.entries()) {
    const rawData = await readFile(rawFrame.path, "base64");
    const dataUrl = `data:image/png;base64,${rawData}`;
    await evaluate(
      connection,
      `window.blokeBotSetCapture(${JSON.stringify(dataUrl)})`,
    );
    const frame = join(
      framedDirectory,
      `${String(index).padStart(3, "0")}.png`,
    );
    await capture(connection, frame);
    framed.push({ path: frame, ticks: rawFrame.ticks });
  }
  return framed;
}

async function assembleWebp(frames, output) {
  let frameIndex = 0;
  let elapsedMilliseconds = 0;
  const inputs = [];
  for (const frame of frames) {
    for (let tick = 0; tick < frame.ticks; tick += 1) {
      frameIndex += 1;
      const nextElapsedMilliseconds = Math.round(
        (frameIndex * 1000) / framesPerSecond,
      );
      const duration = nextElapsedMilliseconds - elapsedMilliseconds;
      elapsedMilliseconds = nextElapsedMilliseconds;
      inputs.push("-d", String(duration), frame.path);
    }
  }
  const encodedOutput = `${output}.encoded`;
  try {
    await execute(
      "img2webp",
      [
        "-loop",
        "0",
        "-mixed",
        "-q",
        "82",
        "-m",
        "6",
        ...inputs,
        "-o",
        encodedOutput,
      ],
      { maxBuffer: 4 * 1024 * 1024 },
    );
    await execute(
      "webpmux",
      ["-set", "bgcolor", "0,0,0,0", encodedOutput, "-o", output],
      { maxBuffer: 4 * 1024 * 1024 },
    );
  } finally {
    await unlink(encodedOutput).catch(() => {});
  }
}

async function captureWorkflow(connection, directory, kind, touchEnabled) {
  switch (kind) {
    case "scroll":
      return captureHomeScroll(connection, directory, touchEnabled);
    case "guessing":
      return captureGuessingWorkflow(connection, directory);
    case "points":
      return capturePointsWorkflow(connection, directory, touchEnabled);
    default:
      throw new Error(`Unknown animation workflow: ${kind}.`);
  }
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const matrix = await loadCaptureMatrix(options.matrix);
  await mkdir(options.profileDirectory, { recursive: true });
  await mkdir(options.framesDirectory, { recursive: true });
  await mkdir(options.outputDirectory, { recursive: true });

  const browserLog = await open(options.browserLog, "a");
  const browserProcess = spawn(
    options.browser,
    [
      "--headless=new",
      "--disable-background-networking",
      "--disable-background-mode",
      "--disable-component-update",
      "--disable-default-apps",
      "--disable-sync",
      "--force-device-scale-factor=1",
      "--host-resolver-rules=MAP * 0.0.0.0, EXCLUDE 127.0.0.1",
      "--hide-scrollbars",
      "--metrics-recording-only",
      "--no-first-run",
      "--no-sandbox",
      "--password-store=basic",
      "--use-mock-keychain",
      "--allow-file-access-from-files",
      "--remote-debugging-port=0",
      `--user-data-dir=${options.profileDirectory}`,
      "about:blank",
    ],
    { stdio: ["ignore", browserLog.fd, browserLog.fd] },
  );

  let connection;
  try {
    const port = await waitForDevTools(
      options.profileDirectory,
      browserProcess,
    );
    connection = await createPageConnection(port);
    await connection.send("Page.enable");
    await connection.send("Runtime.enable");

    const manifest = [];
    const selectedScenarios = options.only
      ? matrix.animations.filter((scenario) => scenario.name === options.only)
      : matrix.animations;
    if (selectedScenarios.length === 0) {
      throw new Error(`Unknown animation scenario: ${options.only}.`);
    }

    for (const scenario of selectedScenarios) {
      const device = deviceCatalog[scenario.device];
      const scenarioDirectory = join(options.framesDirectory, scenario.name);
      const rawDirectory = join(scenarioDirectory, "raw");
      await mkdir(rawDirectory, { recursive: true });
      await openSimulation(
        connection,
        options.baseUrl,
        scenario.view,
        scenario.theme,
        device,
      );

      const rawFrames = await captureWorkflow(
        connection,
        rawDirectory,
        scenario.kind,
        device.mobile,
      );
      const framedFrames = await frameAnimation(
        connection,
        options.frameTemplate,
        scenario.device,
        device,
        rawFrames,
        scenarioDirectory,
      );
      const output = join(options.outputDirectory, `${scenario.name}.webp`);
      await assembleWebp(framedFrames, output);
      const durationTicks = framedFrames.reduce(
        (total, frame) => total + frame.ticks,
        0,
      );
      manifest.push({
        name: scenario.name,
        device: scenario.device,
        theme: scenario.theme,
        view: scenario.view,
        kind: scenario.kind,
        sourceFrameCount: framedFrames.length,
        timelineFrameCount: durationTicks,
        framesPerSecond,
        durationSeconds: durationTicks / framesPerSecond,
        file: basename(output),
      });
    }

    await writeFile(
      join(options.outputDirectory, "manifest.json"),
      `${JSON.stringify(manifest, null, 2)}\n`,
    );
  } finally {
    connection?.close();
    if (browserProcess.exitCode === null) {
      browserProcess.kill("SIGTERM");
      await Promise.race([once(browserProcess, "exit"), sleep(5_000)]);
    }
    if (browserProcess.exitCode === null) browserProcess.kill("SIGKILL");
    await browserLog.close();
  }
}

main().catch((error) => {
  process.stderr.write(`${error.stack ?? error.message}\n`);
  process.exitCode = 1;
});
