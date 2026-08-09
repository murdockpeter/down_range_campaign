const clamp = (value, min, max) => Math.min(max, Math.max(min, value));

function resolveAar(state, report) {
  const next = structuredClone(state);
  const score = Number(report.objectiveScore || 0);
  const casualties = Number(report.kia || 0) + Number(report.serious || 0);
  const equipmentLoss = Number(report.assetsLost || 0);
  const momentumDelta = clamp(score - Math.ceil(casualties / 2) - equipmentLoss, -2, 2);
  next.campaign.momentum = clamp(next.campaign.momentum + momentumDelta, -3, 3);
  next.campaign.turn += 1;
  next.campaign.lastUpdated = new Date().toISOString();
  next.history.unshift({
    id: `aar-${Date.now()}`,
    turn: state.campaign.turn,
    mission: state.mission.title,
    outcome: report.outcome,
    summary: report.summary,
    momentumDelta,
    timestamp: next.campaign.lastUpdated
  });
  next.mission.status = 'complete';
  next.mission.aar = report;
  return { state: next, momentumDelta };
}

module.exports = { clamp, resolveAar };
