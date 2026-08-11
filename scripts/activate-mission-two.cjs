const fs = require('node:fs');
const path = require('node:path');
const { activateMissionTwo } = require('../src/campaign-missions.cjs');
const { createBattleRequest } = require('../src/unity-bridge.cjs');

const appData = process.env.APPDATA;
if (!appData) throw new Error('Windows APPDATA is unavailable.');

const savePath = path.join(appData, 'down-range-campaign-command', 'campaign-state.json');
if (!fs.existsSync(savePath)) throw new Error(`Campaign save was not found at ${savePath}`);

const current = JSON.parse(fs.readFileSync(savePath, 'utf8'));
const result = activateMissionTwo(current);
if (!result.activated && !result.upgraded) {
  console.log('Mission #2 is already active. No save changes were required.');
  process.exit(0);
}

const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
const backupPath = path.join(path.dirname(savePath), `campaign-state.before-mission-2.${timestamp}.json`);
const temporaryPath = `${savePath}.mission-2.tmp`;
fs.copyFileSync(savePath, backupPath);
fs.writeFileSync(temporaryPath, JSON.stringify(result.state, null, 2), 'utf8');
fs.renameSync(temporaryPath, savePath);

const requestPath = result.state.unityBattle?.requestPath;
if (requestPath && fs.existsSync(requestPath)) {
  const existingRequest = JSON.parse(fs.readFileSync(requestPath, 'utf8'));
  const requestBackupPath = requestPath.replace(/\.json$/i, `.before-rules-${timestamp}.json`);
  const requestTemporaryPath = `${requestPath}.rules.tmp`;
  const upgradedRequest = createBattleRequest(result.state, {
    requestId: result.state.unityBattle.pendingRequestId || existingRequest.requestId,
    createdAt: existingRequest.createdAt,
    seed: Number(existingRequest.settings?.seed || 1),
    mapPath: existingRequest.board?.mapPath || '',
    rulesPdfPath: existingRequest.settings?.rulesPdfPath || path.join(__dirname, '..', 'docs', 'official', 'DownRangeLatest', 'Rules Compressed-278da66fbe36c91eae0252e2830de80b.pdf')
  });
  fs.copyFileSync(requestPath, requestBackupPath);
  fs.writeFileSync(requestTemporaryPath, JSON.stringify(upgradedRequest, null, 2), 'utf8');
  fs.renameSync(requestTemporaryPath, requestPath);
  console.log(`Pending Unity request upgraded: ${requestPath}`);
  console.log(`Request backup: ${requestBackupPath}`);
}

console.log(`${result.activated ? 'Mission #2 activated' : 'Mission #2 rules upgraded'}: ${result.state.mission.title}`);
console.log(`Campaign save: ${savePath}`);
console.log(`Pre-mission backup: ${backupPath}`);
