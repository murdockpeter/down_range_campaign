const { app, BrowserWindow, ipcMain, shell, dialog, safeStorage } = require('electron');
const fs = require('fs');
const fsp = require('fs/promises');
const http = require('http');
const path = require('path');
const { spawn } = require('child_process');
const { createBattleRequest, applyBattleResult } = require('./unity-bridge.cjs');
const { resetUnityFiles } = require('./unity-reset.cjs');
const { activateMissionTwo } = require('./campaign-missions.cjs');

const seedPath = path.join(__dirname, '..', 'data', 'campaign-seed.json');
const libraryPath = app.isPackaged ? path.join(process.resourcesPath, 'docs', 'official') : path.join(__dirname, '..', 'docs', 'official');
const docsPath = path.join(__dirname, '..', 'docs');
const rendererPath = path.join(__dirname, 'renderer');
const APP_HOST = '127.0.0.1';
const APP_PORT = 43118;
let localServer;

function unityEditorPath() { return 'C:\\Program Files\\Unity\\Hub\\Editor\\6000.2.12f1\\Editor\\Unity.exe'; }
function unityProjectPath() { return path.join(__dirname, '..', 'unity-tactical'); }
function unityPlayerPath() {
  const candidates = app.isPackaged
    ? [path.join(process.resourcesPath, 'unity-tactical', 'DownRangeTactical.exe')]
    : [path.join(unityProjectPath(), 'Build', 'DownRangeTactical.exe')];
  return candidates.find(candidate => fs.existsSync(candidate)) || candidates[0];
}
function unityExchangeRoot() { return path.join(app.getPath('userData'), 'unity-battles'); }
function rulesPdfPath() { return path.join(libraryPath, 'DownRangeLatest', 'Rules Compressed-278da66fbe36c91eae0252e2830de80b.pdf'); }
function oneStarSavePath() { return path.join(app.getPath('home'), 'AppData', 'LocalLow', 'Down Range Campaign Command', 'Down Range Tactical Resolver', 'one-star-state-v1.json'); }
function unityStatus() {
  const player = unityPlayerPath();
  return { editorInstalled: fs.existsSync(unityEditorPath()), projectInstalled: fs.existsSync(unityProjectPath()), playerInstalled: fs.existsSync(player), playerPath: player };
}
function resetUnityState(state) {
  const savePath = oneStarSavePath();
  return resetUnityFiles({
    unityBattle: state?.unityBattle,
    exchangeRoot: unityExchangeRoot(),
    oneStarFolder: path.dirname(savePath),
    oneStarSave: savePath
  });
}
function launchUnityOneStar() {
  const player = unityPlayerPath();
  if (!fs.existsSync(player)) throw new Error('Unity tactical player is not built. Run the Unity build step first.');
  const child = spawn(player, ['--one-star'], { cwd: path.dirname(player), detached: true, stdio: 'ignore', windowsHide: false });
  child.unref();
  return { launched: true, scenario: 'one-star' };
}
function launchUnityBattle(state) {
  const player = unityPlayerPath();
  if (!fs.existsSync(player)) throw new Error('Unity tactical player is not built. Run the Unity build step first.');
  if (state?.tactical?.committed || state?.mission?.status === 'complete') throw new Error('This mission already has committed tactical results.');
  const pendingPath = state?.unityBattle?.requestPath;
  if (state?.unityBattle?.pendingRequestId && pendingPath && fs.existsSync(pendingPath)) {
    const existingRequest = JSON.parse(fs.readFileSync(pendingPath, 'utf8'));
    const upgraded = createBattleRequest(state, {
      requestId: state.unityBattle.pendingRequestId,
      createdAt: existingRequest.createdAt,
      seed: Number(existingRequest?.settings?.seed || 1),
      mapPath: '', rulesPdfPath: rulesPdfPath()
    });
    const objectiveRules = objectives => (objectives || []).map(({id,type,actionLabel,side,x,y,radius,requiredProgress,difficulty,uninterrupted,requiresLos,threshold,edge,depth,targetUnitIds}) => ({id,type,actionLabel,side,x,y,radius,requiredProgress,difficulty,uninterrupted,requiresLos,threshold,edge,depth,targetUnitIds}));
    const requiresRulesUpgrade = !existingRequest?.board?.terrain || !existingRequest?.settings?.rulesPdfPath || JSON.stringify(objectiveRules(existingRequest.objectives)) !== JSON.stringify(objectiveRules(upgraded.objectives));
    if (requiresRulesUpgrade) {
      fs.writeFileSync(pendingPath, JSON.stringify(upgraded, null, 2), 'utf8');
    }
    const resumed = spawn(player, ['--battle-request', pendingPath], { cwd: path.dirname(player), detached: true, stdio: 'ignore', windowsHide: false });
    resumed.unref();
    return { launched: true, resumed: true, requestId: state.unityBattle.pendingRequestId, unityBattle: state.unityBattle };
  }
  const requestId = require('crypto').randomUUID();
  const exchange = path.join(unityExchangeRoot(), requestId);
  fs.mkdirSync(exchange, { recursive: true });
  const request = createBattleRequest(state, { requestId, mapPath: '', rulesPdfPath: rulesPdfPath() });
  const requestPath = path.join(exchange, 'battle-request.json');
  fs.writeFileSync(requestPath, JSON.stringify(request, null, 2), 'utf8');
  state.unityBattle ||= {};
  state.unityBattle.pendingRequestId = requestId;
  state.unityBattle.requestPath = requestPath;
  state.unityBattle.launchedAt = new Date().toISOString();
  saveState(state);
  const child = spawn(player, ['--battle-request', requestPath], { cwd: path.dirname(player), detached: true, stdio: 'ignore', windowsHide: false });
  child.unref();
  return { launched: true, requestId, unityBattle: state.unityBattle };
}
function importUnityBattle(state) {
  const requestId = state?.unityBattle?.pendingRequestId;
  if (!requestId || !/^[a-f0-9-]{20,}$/i.test(requestId)) throw new Error('There is no pending Unity battle.');
  const resultPath = path.join(unityExchangeRoot(), requestId, 'battle-result.json');
  if (!fs.existsSync(resultPath)) return { ready: false, message: 'The Unity battle has not exported a result yet.' };
  const result = JSON.parse(fs.readFileSync(resultPath, 'utf8'));
  const applied = applyBattleResult(state, result);
  saveState(applied.state);
  return { ready: true, state: applied.state, alreadyImported: applied.alreadyImported };
}

function statePath() {
  return path.join(app.getPath('userData'), 'campaign-state.json');
}

function loadState() {
  const target = statePath();
  const source = fs.existsSync(target) ? target : seedPath;
  return JSON.parse(fs.readFileSync(source, 'utf8'));
}

function saveState(state) {
  const target = statePath();
  const temp = `${target}.tmp`;
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(temp, JSON.stringify(state, null, 2), 'utf8');
  fs.renameSync(temp, target);
  return { ok: true, savedAt: new Date().toISOString() };
}

function settingsPath() { return path.join(app.getPath('userData'), 'settings.json'); }
async function readSettings() {
  try { return JSON.parse(await fsp.readFile(settingsPath(), 'utf8')); } catch { return {}; }
}
async function writeSettings(settings) {
  await fsp.mkdir(path.dirname(settingsPath()), { recursive: true });
  await fsp.writeFile(settingsPath(), JSON.stringify(settings, null, 2), 'utf8');
}
async function getMapsKey() {
  const settings = await readSettings();
  if (!settings.mapsKey || !safeStorage.isEncryptionAvailable()) return '';
  try { return safeStorage.decryptString(Buffer.from(settings.mapsKey, 'base64')); } catch { return ''; }
}
async function setMapsKey(key) {
  const settings = await readSettings();
  const value = String(key || '').trim();
  if (!value) {
    settings.mapsKey = '';
    await writeSettings(settings);
    return { saved: true, encrypted: false };
  }
  if (!safeStorage.isEncryptionAvailable()) return { saved: false, encrypted: false, error: 'OS-backed encryption is unavailable; the key was not saved.' };
  settings.mapsKey = safeStorage.encryptString(value).toString('base64');
  await writeSettings(settings);
  return { saved: true, encrypted: true };
}

function contentType(filePath) {
  return ({ '.html':'text/html; charset=utf-8', '.js':'text/javascript; charset=utf-8', '.css':'text/css; charset=utf-8', '.svg':'image/svg+xml', '.png':'image/png', '.jpg':'image/jpeg', '.jpeg':'image/jpeg' })[path.extname(filePath)] || 'application/octet-stream';
}

function startLocalServer() {
  return new Promise((resolve, reject) => {
    localServer = http.createServer(async (request, response) => {
      try {
        const requestPath = new URL(request.url, `http://${APP_HOST}`).pathname;
        const relative = requestPath === '/' ? 'index.html' : decodeURIComponent(requestPath.slice(1));
        const basePath = relative.startsWith('assets/') ? path.join(__dirname, '..') : rendererPath;
        const filePath = path.resolve(basePath, relative);
        const allowedRoot = path.resolve(basePath);
        if (!filePath.startsWith(`${allowedRoot}${path.sep}`) && filePath !== path.join(rendererPath, 'index.html')) {
          response.writeHead(403).end('Forbidden'); return;
        }
        const body = await fsp.readFile(filePath);
        response.writeHead(200, {
          'Content-Type': contentType(filePath), 'Cache-Control': 'no-store',
          'Content-Security-Policy': [
            "default-src 'self'", "script-src 'self' https://maps.googleapis.com https://maps.gstatic.com",
            "style-src 'self' 'unsafe-inline'", "img-src 'self' data: blob: https://*.googleapis.com https://*.gstatic.com https://*.google.com https://*.ggpht.com",
            "connect-src 'self' https://maps.googleapis.com https://maps.gstatic.com https://*.googleapis.com", "worker-src 'self' blob:"
          ].join('; ')
        });
        response.end(body);
      } catch { response.writeHead(404).end('Not found'); }
    });
    localServer.once('error', reject);
    localServer.listen(APP_PORT, APP_HOST, resolve);
  });
}

function createWindow() {
  const win = new BrowserWindow({
    width: 1540,
    height: 980,
    minWidth: 1120,
    minHeight: 720,
    backgroundColor: '#090d0c',
    title: 'Down Range Campaign Command',
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true
    }
  });
  win.removeMenu();
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.webContents.on('will-navigate', (event, url) => {
    if (!url.startsWith(`http://${APP_HOST}:${APP_PORT}/`)) event.preventDefault();
  });
  win.loadURL(`http://${APP_HOST}:${APP_PORT}/`);
}

app.whenReady().then(async () => {
  ipcMain.handle('campaign:load', () => loadState());
  ipcMain.handle('campaign:save', (_event, state) => saveState(state));
  ipcMain.handle('campaign:reset', () => {
    const target = statePath();
    if (fs.existsSync(target)) fs.unlinkSync(target);
    return loadState();
  });
  ipcMain.handle('campaign:export', async (_event, state) => {
    const result = await dialog.showSaveDialog({
      title: 'Export campaign',
      defaultPath: `${state.campaign.slug || 'down-range-campaign'}.json`,
      filters: [{ name: 'Campaign JSON', extensions: ['json'] }]
    });
    if (result.canceled || !result.filePath) return { canceled: true };
    fs.writeFileSync(result.filePath, JSON.stringify(state, null, 2), 'utf8');
    return { canceled: false, filePath: result.filePath };
  });
  ipcMain.handle('campaign:import', async () => {
    const result = await dialog.showOpenDialog({
      title: 'Import campaign', properties: ['openFile'],
      filters: [{ name: 'Campaign JSON', extensions: ['json'] }]
    });
    if (result.canceled || !result.filePaths[0]) return { canceled: true };
    const state = JSON.parse(fs.readFileSync(result.filePaths[0], 'utf8'));
    saveState(state);
    return { canceled: false, state };
  });
  ipcMain.handle('campaign:activate-mission-two', (_event, state) => {
    const result = activateMissionTwo(state);
    saveState(result.state);
    return result;
  });
  ipcMain.handle('library:open', async (_event, fileName) => {
    const resolved = path.resolve(libraryPath, fileName);
    if (!resolved.startsWith(path.resolve(libraryPath)) || !fs.existsSync(resolved)) {
      throw new Error('Reference file is unavailable.');
    }
    const error = await shell.openPath(resolved);
    if (error) throw new Error(error);
    return { ok: true };
  });
  ipcMain.handle('library:folder', () => shell.openPath(libraryPath));
  ipcMain.handle('planning:open-reference', async (_event, fileName) => {
    const allowed = new Set(['MCPP-References.pdf', 'MCWP 5-10 (1) (SECURED).pdf']);
    if (!allowed.has(fileName)) throw new Error('Planning reference is unavailable.');
    const error = await shell.openPath(path.join(docsPath, fileName));
    if (error) throw new Error(error);
    return { ok: true };
  });
  ipcMain.handle('maps-key:get', getMapsKey);
  ipcMain.handle('maps-key:set', (_event, key) => setMapsKey(key));
  ipcMain.handle('unity:status', unityStatus);
  ipcMain.handle('unity:launch', (_event, state) => launchUnityBattle(state));
  ipcMain.handle('unity:launch-one-star', () => launchUnityOneStar());
  ipcMain.handle('unity:reset', (_event, state) => resetUnityState(state));
  ipcMain.handle('unity:import-result', (_event, state) => importUnityBattle(state));
  await startLocalServer();
  createWindow();
  app.on('activate', () => BrowserWindow.getAllWindows().length || createWindow());
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
app.on('before-quit', () => localServer?.close());
