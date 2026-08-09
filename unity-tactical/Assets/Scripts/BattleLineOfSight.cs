using System;
using UnityEngine;

namespace DownRange.Tactical
{
    public sealed class BattleLosObstacle : MonoBehaviour
    {
        public string label;
        public string classification = "blocked";
    }

    [Serializable]
    public sealed class BattleLosResult
    {
        public string classification = "open";
        public string blocker = "";
        public float distance;
        public Vector3 start;
        public Vector3 end;
    }

    public static class BattleLineOfSight
    {
        public static BattleLosResult Evaluate(Vector3 start, Vector3 end, string originUnitId = "", string targetUnitId = "")
        {
            var result = new BattleLosResult { start = start, end = end, distance = Vector3.Distance(start, end) };
            var direction = end - start; if (direction.sqrMagnitude < .0001f) return result;
            var hits = Physics.RaycastAll(start, direction.normalized, result.distance, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                var marker = hit.collider.GetComponentInParent<CampaignMiniatureMarker>();
                if (marker != null)
                {
                    if (marker.unitId == originUnitId || marker.unitId == targetUnitId) continue;
                    return Blocked(result, "intervening unit " + marker.gameObject.name.Replace(" - 3D campaign miniature", ""));
                }
                var obstacle = hit.collider.GetComponentInParent<BattleLosObstacle>();
                if (obstacle == null) continue;
                if (obstacle.classification == "partial")
                {
                    if (result.classification == "open") { result.classification = "partial"; result.blocker = obstacle.label; }
                    continue;
                }
                return Blocked(result, obstacle.label);
            }
            return result;
        }

        static BattleLosResult Blocked(BattleLosResult result, string blocker)
        {
            result.classification = "blocked"; result.blocker = blocker ?? "terrain"; return result;
        }
    }
}
