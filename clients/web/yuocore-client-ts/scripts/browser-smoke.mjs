import { spawn } from "node:child_process";
import { createReadStream, existsSync } from "node:fs";
import { mkdtemp, rm } from "node:fs/promises";
import http from "node:http";
import net from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const sdkDir = path.resolve(scriptDir, "..");
const chrome = findChrome();

if (!chrome) {
  console.error("missing Chrome/Chromium executable; set CHROME_BIN to override");
  process.exit(66);
}

if (!existsSync(path.join(sdkDir, "dist", "index.js"))) {
  console.error("missing dist/index.js; run npm run build first");
  process.exit(65);
}

const server = http.createServer((request, response) => {
  const url = request.url ?? "/";

  if (url === "/" || url === "/index.html") {
    response.writeHead(200, { "content-type": "text/html; charset=utf-8" });
    response.end(renderSmokePage());
    return;
  }

  if (url === "/dist/index.js") {
    response.writeHead(200, { "content-type": "text/javascript; charset=utf-8" });
    streamFile(response, path.join(sdkDir, "dist", "index.js"));
    return;
  }

  response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
  response.end("not found");
});

await listen(server);

const address = server.address();
const userDataDir = await mkdtemp(path.join(tmpdir(), "yuocore-web-sdk-browser-"));
const debugPort = await getFreePort();
const url = `http://127.0.0.1:${address.port}/index.html`;
const chromeErrors = [];
let chromeProcess;
let cdpSocket;

try {
  chromeProcess = spawn(
    chrome,
    [
      "--headless=new",
      "--disable-gpu",
      "--no-sandbox",
      "--disable-dev-shm-usage",
      `--remote-debugging-port=${debugPort}`,
      `--user-data-dir=${userDataDir}`,
      url
    ],
    {
      stdio: ["ignore", "ignore", "pipe"]
    }
  );

  chromeProcess.stderr.setEncoding("utf8");
  chromeProcess.stderr.on("data", (chunk) => chromeErrors.push(chunk));

  const target = await waitForPageTarget(debugPort, url);
  cdpSocket = await connectCdp(target.webSocketDebuggerUrl);
  const cdp = createCdpClient(cdpSocket);

  await cdp.call("Runtime.enable");
  await waitForSmokeResult(cdp);

  console.log("yuocore-client-ts browser smoke passed");
} finally {
  if (cdpSocket) {
    cdpSocket.close();
  }
  if (chromeProcess && chromeProcess.exitCode === null) {
    const exited = waitForExit(chromeProcess);
    chromeProcess.kill("SIGTERM");
    await exited;
  }
  server.close();
  await rm(userDataDir, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
}

function findChrome() {
  const candidates = [
    process.env.CHROME_BIN,
    "/usr/bin/google-chrome",
    "/usr/bin/google-chrome-stable",
    "/usr/bin/chromium",
    "/usr/bin/chromium-browser"
  ].filter(Boolean);

  return candidates.find((candidate) => existsSync(candidate));
}

function listen(httpServer) {
  return new Promise((resolve, reject) => {
    httpServer.once("error", reject);
    httpServer.listen(0, "127.0.0.1", () => resolve());
  });
}

function getFreePort() {
  return new Promise((resolve, reject) => {
    const portServer = net.createServer();
    portServer.once("error", reject);
    portServer.listen(0, "127.0.0.1", () => {
      const port = portServer.address().port;
      portServer.close(() => resolve(port));
    });
  });
}

async function waitForPageTarget(port, expectedUrl) {
  const deadline = Date.now() + 15000;

  while (Date.now() < deadline) {
    try {
      const response = await fetch(`http://127.0.0.1:${port}/json/list`);
      if (response.ok) {
        const targets = await response.json();
        const target = targets.find((item) =>
          item.type === "page" &&
          item.webSocketDebuggerUrl &&
          (item.url === expectedUrl || item.url.startsWith(expectedUrl))
        );

        if (target) {
          return target;
        }
      }
    } catch {
      // Chrome DevTools 端口启动需要一点时间，轮询直到超时。
    }

    await delay(100);
  }

  throw new Error(`Chrome DevTools target not found. ${chromeErrors.join("")}`);
}

function connectCdp(webSocketUrl) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(webSocketUrl);
    socket.addEventListener("open", () => resolve(socket), { once: true });
    socket.addEventListener("error", (event) => reject(event.error ?? event), { once: true });
  });
}

function waitForExit(childProcess) {
  return new Promise((resolve) => {
    childProcess.once("exit", () => resolve());
  });
}

function createCdpClient(socket) {
  let nextId = 1;
  const pending = new Map();

  socket.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    if (!message.id) {
      return;
    }

    const waiter = pending.get(message.id);
    if (!waiter) {
      return;
    }

    pending.delete(message.id);
    if (message.error) {
      waiter.reject(new Error(`${message.error.code}: ${message.error.message}`));
    } else {
      waiter.resolve(message.result);
    }
  });

  return {
    call(method, params = {}) {
      const id = nextId++;
      const payload = JSON.stringify({ id, method, params });
      return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        socket.send(payload);
      });
    }
  };
}

async function waitForSmokeResult(cdp) {
  const deadline = Date.now() + 15000;
  let lastText = "";

  while (Date.now() < deadline) {
    const state = await evaluate(cdp, "document.body?.dataset?.yuocoreSmoke ?? ''");
    if (state === "passed") {
      return;
    }

    lastText = await evaluate(cdp, "document.body?.textContent ?? ''");
    if (state === "failed") {
      throw new Error(`browser smoke failed: ${lastText}`);
    }

    await delay(100);
  }

  throw new Error(`browser smoke timed out. body=${lastText} ${chromeErrors.join("")}`);
}

async function evaluate(cdp, expression) {
  const result = await cdp.call("Runtime.evaluate", {
    expression,
    returnByValue: true
  });

  if (result.exceptionDetails) {
    throw new Error(result.exceptionDetails.text);
  }

  return result.result?.value;
}

function streamFile(response, filePath) {
  const stream = createReadStream(filePath);
  stream.once("error", (error) => {
    response.writeHead(500, { "content-type": "text/plain; charset=utf-8" });
    response.end(String(error));
  });
  stream.pipe(response);
}

function renderSmokePage() {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>YuoCore Web SDK browser smoke</title>
</head>
<body>
  yuocore-browser-smoke-pending
  <script type="module">
    import {
      YUO_PROTOCOL_VERSION,
      YuoClientSession,
      YuoProtocolOpCodes,
      YuoStateSyncMultiStreamReceiver,
      YuoStateSyncReceiver,
      shouldRequestStateSnapshot
    } from "/dist/index.js";

    try {
      const session = new YuoClientSession();
      const receiver = new YuoStateSyncReceiver();
      const multiReceiver = new YuoStateSyncMultiStreamReceiver();

      if (YUO_PROTOCOL_VERSION !== 1) {
        throw new Error("unexpected protocol version");
      }

      if (YuoProtocolOpCodes.stateDelta !== 107) {
        throw new Error("unexpected stateDelta opcode");
      }

      if (typeof shouldRequestStateSnapshot !== "function") {
        throw new Error("missing StateSync recovery helper");
      }

      window.__yuocoreSmoke = {
        session,
        receiver,
        multiReceiver
      };

      document.body.dataset.yuocoreSmoke = "passed";
      document.body.textContent = "yuocore-browser-smoke-passed";
    } catch (error) {
      document.body.dataset.yuocoreSmoke = "failed";
      document.body.textContent = String(error?.stack ?? error);
    }
  </script>
</body>
</html>`;
}
