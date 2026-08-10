const fs = require('node:fs');
const path = require('node:path');
const { activateMissionTwo } = require('../src/campaign-missions.cjs');

const appData = process.env.APPDATA;
if (!appData) throw new Error('Windows APPDATA is unavailable.');

const savePath = path.join(appData, 'down-range-campaign-command', 'campaign-state.json');
if (!fs.existsSync(savePath)) throw new Error(`Campaign save was not found at ${savePath}`);

const current = JSON.parse(fs.readFileSync(savePath, 'utf8'));
const result = activateMissionTwo(current);
if (!result.activated) {
  console.log('Mission #2 is already active. No save changes were required.');
  process.exit(0);
}

const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
const backupPath = path.join(path.dirname(savePath), `campaign-state.before-mission-2.${timestamp}.json`);
const temporaryPath = `${savePath}.mission-2.tmp`;
fs.copyFileSync(savePath, backupPath);
fs.writeFileSync(temporaryPath, JSON.stringify(result.state, null, 2), 'utf8');
fs.renameSync(temporaryPath, savePath);

console.log(`Mission #2 activated: ${result.state.mission.title}`);
console.log(`Campaign save: ${savePath}`);
console.log(`Pre-mission backup: ${backupPath}`);
