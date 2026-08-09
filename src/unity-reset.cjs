const fs = require('fs');
const path = require('path');

function comparablePath(value) {
  const resolved = path.resolve(value);
  return process.platform === 'win32' ? resolved.toLowerCase() : resolved;
}

function samePath(left, right) {
  return comparablePath(left) === comparablePath(right);
}

function resolveResetTargets({ unityBattle, exchangeRoot, oneStarFolder, oneStarSave }) {
  const resolvedExchangeRoot = path.resolve(exchangeRoot);
  const requestId = unityBattle?.pendingRequestId;
  const requestPath = unityBattle?.requestPath;
  let exchange;

  if (requestId || requestPath) {
    if (!requestId || !/^[a-f0-9-]{20,}$/i.test(requestId) || !requestPath) {
      throw new Error('The pending Unity battle reference is incomplete; reset was not performed.');
    }

    exchange = path.resolve(resolvedExchangeRoot, requestId);
    const expectedRequest = path.join(exchange, 'battle-request.json');
    if (!samePath(path.dirname(exchange), resolvedExchangeRoot) || !samePath(requestPath, expectedRequest)) {
      throw new Error('The pending Unity battle path is outside the expected exchange directory; reset was not performed.');
    }
  }

  const resolvedOneStarFolder = path.resolve(oneStarFolder);
  const resolvedOneStarSave = path.resolve(oneStarSave);
  if (!samePath(path.dirname(resolvedOneStarSave), resolvedOneStarFolder) || path.basename(resolvedOneStarSave) !== 'one-star-state-v1.json') {
    throw new Error('The One Star save path could not be verified; reset was not performed.');
  }

  return { exchange, oneStarSave: resolvedOneStarSave };
}

function resetUnityFiles(options) {
  const targets = resolveResetTargets(options);
  const removed = [];

  if (targets.exchange && fs.existsSync(targets.exchange)) {
    fs.rmSync(targets.exchange, { recursive: true, force: true });
    removed.push(targets.exchange);
  }
  if (fs.existsSync(targets.oneStarSave)) {
    fs.unlinkSync(targets.oneStarSave);
    removed.push(targets.oneStarSave);
  }

  return { reset: true, removed };
}

module.exports = { resetUnityFiles, resolveResetTargets };
