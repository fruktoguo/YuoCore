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
const rootDir = path.resolve(sdkDir, "../../..");
const backendProject = path.join(rootDir, "src", "YuoCore.Backend", "YuoCore.Backend.csproj");
const chrome = findChrome();

if (!chrome) {
  console.error("missing Chrome/Chromium executable; set CHROME_BIN to override");
  process.exit(66);
}

if (!existsSync(path.join(sdkDir, "dist", "index.js"))) {
  console.error("missing dist/index.js; run npm run build first");
  process.exit(65);
}

if (!existsSync(backendProject)) {
  console.error(`missing backend project: ${backendProject}`);
  process.exit(66);
}

const backendPort = await getFreePort();
const pagePort = await getFreePort();
const debugPort = await getFreePort();
const backendUrl = `http://127.0.0.1:${backendPort}`;
const backendWsUrl = `ws://127.0.0.1:${backendPort}/ws`;
const pageUrl = `http://127.0.0.1:${pagePort}/index.html`;
const userDataDir = await mkdtemp(path.join(tmpdir(), "yuocore-web-backend-browser-"));
const backendLogs = [];
const chromeErrors = [];
let backendProcess;
let chromeProcess;
let cdpSocket;

const pageServer = http.createServer((request, response) => {
  const url = request.url ?? "/";

  if (url === "/" || url === "/index.html") {
    response.writeHead(200, { "content-type": "text/html; charset=utf-8" });
    response.end(renderSmokePage(backendWsUrl));
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

try {
  backendProcess = spawn(
    "dotnet",
    [
      "run",
      "--no-build",
      "--project",
      backendProject,
      "--urls",
      backendUrl
    ],
    {
      cwd: rootDir,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: "Development",
        DOTNET_ENVIRONMENT: "Development",
        Storage__PostgresEnabled: "false",
        Storage__RedisEnabled: "false",
        Storage__DurableOutboxEnabled: "false",
        Game__GatewayRequestsUseCommandQueue: "true",
        Gateway__WebSocketPush__PollIntervalMs: "5"
      },
      stdio: ["ignore", "pipe", "pipe"]
    }
  );

  backendProcess.stdout.setEncoding("utf8");
  backendProcess.stderr.setEncoding("utf8");
  backendProcess.stdout.on("data", (chunk) => backendLogs.push(chunk));
  backendProcess.stderr.on("data", (chunk) => backendLogs.push(chunk));

  await waitForBackend(backendUrl);
  await listen(pageServer, pagePort);

  chromeProcess = spawn(
    chrome,
    [
      "--headless=new",
      "--disable-gpu",
      "--no-sandbox",
      "--disable-dev-shm-usage",
      `--remote-debugging-port=${debugPort}`,
      `--user-data-dir=${userDataDir}`,
      pageUrl
    ],
    {
      stdio: ["ignore", "ignore", "pipe"]
    }
  );

  chromeProcess.stderr.setEncoding("utf8");
  chromeProcess.stderr.on("data", (chunk) => chromeErrors.push(chunk));

  const target = await waitForPageTarget(debugPort, pageUrl);
  cdpSocket = await connectCdp(target.webSocketDebuggerUrl);
  const cdp = createCdpClient(cdpSocket);

  await cdp.call("Runtime.enable");
  await waitForSmokeResult(cdp);

  console.log("yuocore web backend browser smoke passed");
} finally {
  if (cdpSocket) {
    cdpSocket.close();
  }
  if (chromeProcess && chromeProcess.exitCode === null) {
    const exited = waitForExit(chromeProcess);
    chromeProcess.kill("SIGTERM");
    await exited;
  }
  if (backendProcess && backendProcess.exitCode === null) {
    const exited = waitForExit(backendProcess);
    backendProcess.kill("SIGTERM");
    await exited;
  }
  pageServer.close();
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

function getFreePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const port = server.address().port;
      server.close(() => resolve(port));
    });
  });
}

async function waitForBackend(url) {
  const deadline = Date.now() + 30000;

  while (Date.now() < deadline) {
    if (backendProcess.exitCode !== null) {
      throw new Error(`Backend exited before ready. ${backendLogs.join("")}`);
    }

    try {
      const response = await fetch(url);
      if (response.ok) {
        return;
      }
    } catch {
      // Backend 启动期间端口可能还没监听，继续轮询。
    }

    await delay(200);
  }

  throw new Error(`Backend did not become ready. ${backendLogs.join("")}`);
}

function listen(httpServer, port) {
  return new Promise((resolve, reject) => {
    httpServer.once("error", reject);
    httpServer.listen(port, "127.0.0.1", () => resolve());
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
  const deadline = Date.now() + 30000;
  let lastText = "";

  while (Date.now() < deadline) {
    const state = await evaluate(cdp, "document.body?.dataset?.yuocoreSmoke ?? ''");
    if (state === "passed") {
      return;
    }

    lastText = await evaluate(cdp, "document.body?.textContent ?? ''");
    if (state === "failed") {
      throw new Error(`browser backend smoke failed: ${lastText}`);
    }

    await delay(100);
  }

  throw new Error(`browser backend smoke timed out. body=${lastText} chrome=${chromeErrors.join("")} backend=${backendLogs.join("")}`);
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

function renderSmokePage(wsUrl) {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>YuoCore Web Backend browser smoke</title>
</head>
<body>
  yuocore-web-backend-smoke-pending
  <script type="module">
    import {
      YuoMmoWebSocketSession
    } from "/dist/index.js";

    try {
      const session = new YuoMmoWebSocketSession({
        url: ${JSON.stringify(wsUrl)},
        requestTimeoutMs: 5000
      });

      const welcome = await session.connect();
      const enter = await session.client.enterMap(welcome.playerId, 1001n);
      const moved = await session.client.move(welcome.playerId, 1n, 3, 0, 4);
      const state = await session.client.requestStateSnapshot(welcome.playerId, 0n);
      const disconnected = await session.disconnect();

      if (!welcome.playerId) {
        throw new Error("missing player id");
      }

      if (enter.mapId !== 1001n || moved.mapId !== 1001n) {
        throw new Error("unexpected map id");
      }

      const movedPlayer = moved.players.find(player => player.playerId === welcome.playerId);
      if (!movedPlayer || movedPlayer.x !== 3 || movedPlayer.z !== 4 || movedPlayer.lastInputSeq !== 1n) {
        throw new Error("move response did not contain moved player");
      }

      if (state.entities.length === 0) {
        throw new Error("state snapshot did not include entities");
      }

      if (disconnected.online !== false || disconnected.mapId !== 1001n) {
        throw new Error("disconnect response did not preserve player state");
      }

      const reconnected = await session.reconnect();
      const restored = await session.client.getSnapshot(reconnected.playerId);

      if (reconnected.playerId !== welcome.playerId) {
        throw new Error("reconnect returned a different player id");
      }

      if (restored.online !== true || restored.mapId !== 1001n) {
        throw new Error("reconnect did not restore online map state");
      }

      if (restored.x !== 3 || restored.z !== 4 || restored.lastInputSeq !== 1n) {
        throw new Error("reconnect did not restore last movement state");
      }
      session.close();

      document.body.dataset.yuocoreSmoke = "passed";
      document.body.textContent = "yuocore-web-backend-smoke-passed:" + welcome.playerId;
    } catch (error) {
      document.body.dataset.yuocoreSmoke = "failed";
      document.body.textContent = String(error?.stack ?? error);
    }
  </script>
</body>
</html>`;
}
