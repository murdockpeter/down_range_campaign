const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { resetUnityFiles } = require('../src/unity-reset.cjs');

function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'down-range-reset-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const exchangeRoot = path.join(root, 'unity-battles');
  const oneStarFolder = path.join(root, 'LocalLow', 'Down Range Campaign Command', 'Down Range Tactical Resolver');
  const oneStarSave = path.join(oneStarFolder, 'one-star-state-v1.json');
  return { root, exchangeRoot, oneStarFolder, oneStarSave };
}

test('reset removes the pending Unity exchange and One Star save', t => {
  const paths = fixture(t);
  const requestId = '11111111-1111-4111-8111-111111111111';
  const exchange = path.join(paths.exchangeRoot, requestId);
  const requestPath = path.join(exchange, 'battle-request.json');
  fs.mkdirSync(exchange, { recursive: true });
  fs.writeFileSync(requestPath, '{}');
  fs.writeFileSync(path.join(exchange, 'battle-state.json'), '{}');
  fs.mkdirSync(paths.oneStarFolder, { recursive: true });
  fs.writeFileSync(paths.oneStarSave, '{}');

  const result = resetUnityFiles({
    unityBattle: { pendingRequestId: requestId, requestPath },
    exchangeRoot: paths.exchangeRoot,
    oneStarFolder: paths.oneStarFolder,
    oneStarSave: paths.oneStarSave
  });

  assert.equal(result.reset, true);
  assert.equal(result.removed.length, 2);
  assert.equal(fs.existsSync(exchange), false);
  assert.equal(fs.existsSync(paths.oneStarSave), false);
});

test('reset removes One Star state when no standard Unity battle is pending', t => {
  const paths = fixture(t);
  fs.mkdirSync(paths.oneStarFolder, { recursive: true });
  fs.writeFileSync(paths.oneStarSave, '{}');

  const result = resetUnityFiles({
    exchangeRoot: paths.exchangeRoot,
    oneStarFolder: paths.oneStarFolder,
    oneStarSave: paths.oneStarSave
  });

  assert.deepEqual(result.removed, [path.resolve(paths.oneStarSave)]);
  assert.equal(fs.existsSync(paths.oneStarSave), false);
});

test('reset rejects an unexpected request path before deleting either save', t => {
  const paths = fixture(t);
  const requestId = '22222222-2222-4222-8222-222222222222';
  const exchange = path.join(paths.exchangeRoot, requestId);
  const wrongRequestPath = path.join(paths.root, 'outside', 'battle-request.json');
  fs.mkdirSync(exchange, { recursive: true });
  fs.writeFileSync(path.join(exchange, 'battle-request.json'), '{}');
  fs.mkdirSync(paths.oneStarFolder, { recursive: true });
  fs.writeFileSync(paths.oneStarSave, '{}');

  assert.throws(() => resetUnityFiles({
    unityBattle: { pendingRequestId: requestId, requestPath: wrongRequestPath },
    exchangeRoot: paths.exchangeRoot,
    oneStarFolder: paths.oneStarFolder,
    oneStarSave: paths.oneStarSave
  }), /outside the expected exchange directory/);

  assert.equal(fs.existsSync(exchange), true);
  assert.equal(fs.existsSync(paths.oneStarSave), true);
});
