using System;
using System.Collections.Generic;
using UnityEngine;

namespace DownRange.Tactical
{
    public static class ImportedMiniatureFactory
    {
        const string ResourceRoot = "Models/OneStar/";

        public static string ModelFor(UnitData unit)
        {
            if (!string.IsNullOrWhiteSpace(unit.modelId)) return unit.modelId;
            var role = ((unit.name ?? "") + " " + (unit.role ?? "") + " " + FirstWeapon(unit)).ToLowerInvariant();
            if (unit.kind == "vehicle" || unit.flying)
            {
                if (unit.side == "blue" && (unit.flying || role.Contains("raven") || role.Contains("hornet"))) return "USMC Black Hornet";
                if (unit.flying) return role.Contains("armed") ? "Generic Armed Quadcopter" : "Generic Quadcopter";
                return role.Contains("eq2050") ? "PLANMC EQ2050" : "PLANMC ZBL-09";
            }
            if (unit.side == "blue")
            {
                if (unit.medicalSkill > 0 || role.Contains("medic") || role.Contains("corpsman") || role.Contains("lifesaver")) return "USMC Corpsman";
                if (role.Contains("maaws") || role.Contains("anti-armor") || role.Contains("anti armor")) return "USMC MAAWS Gunner";
                if (role.Contains("automatic") || role.Contains("m249") || role.Contains("gunner")) return "USMC M249 Gunner";
                if (role.Contains("leader") || role.Contains("officer") || role.Contains("platoon hq")) return "USMC Officer";
                if (unit.ew || role.Contains("radio") || role.Contains("signal") || role.Contains(" ew ")) return "USMC EW Operator";
                return "USMC Rifleman";
            }
            if (role.Contains("mortar")) return "LPM Mortar Team";
            if (role.Contains("automatic") || role.Contains("gunner")) return "LPM Automatic Rifleman";
            if (unit.ew || unit.radio || role.Contains("leader") || role.Contains("officer") || role.Contains("operator")) return "LPM Officer";
            return "LPM Rifleman";
        }

        static string FirstWeapon(UnitData unit)
        {
            return unit.weapons != null && unit.weapons.Length > 0 ? unit.weapons[0].name ?? "" : "";
        }

        public static bool CreateModels(string[] modelNames, string side, string kind, Transform parent)
        {
            if (modelNames == null || modelNames.Length == 0) return false;
            var offsets = modelNames.Length == 1 ? new[] { Vector3.zero } : modelNames.Length == 2
                ? new[] { new Vector3(-.38f, 0f, 0f), new Vector3(.38f, 0f, 0f) }
                : new[] { new Vector3(-.48f, 0f, .18f), new Vector3(.48f, 0f, .18f), new Vector3(0f, 0f, -.42f) };
            var created = false;
            for (var index = 0; index < modelNames.Length; index++)
                created |= CreateModel(modelNames[index], side, kind, parent, offsets[Mathf.Min(index, offsets.Length - 1)]);
            return created;
        }

        public static bool CreateModel(string modelName, string side, string kind, Transform parent, Vector3 offset)
        {
            var resourcePath = ResourceRoot + modelName;
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null) { Debug.LogWarning("Imported miniature is unavailable: " + modelName); return false; }
            var model = UnityEngine.Object.Instantiate(prefab, parent);
            model.name = modelName + " playtest model";
            model.transform.localPosition = offset + Vector3.up * .34f;
            model.transform.localRotation = Quaternion.identity;
            var vehicle = modelName.Contains("ZBL-09") || modelName.Contains("EQ2050");
            model.transform.localScale = Vector3.one * (vehicle ? .62f : kind == "uas" || kind == "vehicle" && modelName.Contains("Hornet") ? .46f : .72f);
            Paint(model, modelName, resourcePath, side, kind, vehicle);
            AddCollider(model);
            return true;
        }

        static void Paint(GameObject model, string modelName, string resourcePath, string side, string kind, bool vehicle)
        {
            var texture = Resources.Load<Texture2D>(resourcePath + " Texture");
            var needsFactionPaint = texture == null || modelName.StartsWith("LPM ", StringComparison.Ordinal);
            var generatedPaint = needsFactionPaint ? Resources.Load<Texture2D>(ResourceRoot + (side == "red" ? "LPM Field Camo Texture" : "USMC Field Camo Texture")) : null;
            var paintShader = generatedPaint == null ? null : Resources.Load<Shader>("Shaders/OneStarTriplanarPaint");
            var standard = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            var useGeneratedPaint = generatedPaint != null && paintShader != null;
            var activeTexture = useGeneratedPaint ? generatedPaint : texture;
            if (useGeneratedPaint) { generatedPaint.wrapMode = TextureWrapMode.Repeat; generatedPaint.filterMode = FilterMode.Bilinear; }
            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length == 0 ? new Bounds(model.transform.position, Vector3.one) : renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            var localSize = model.transform.InverseTransformVector(bounds.size);
            var modelHeight = Mathf.Max(.1f, Mathf.Abs(localSize.y));
            var modelRadius = Mathf.Max(.1f, Mathf.Max(Mathf.Abs(localSize.x), Mathf.Abs(localSize.z)) / 2f);
            var modelColor = side == "blue" ? new Color(.10f, .56f, .78f) : side == "red" ? new Color(.76f, .16f, .12f) : new Color(.75f, .68f, .30f);
            foreach (var renderer in renderers)
            {
                var material = new Material(useGeneratedPaint ? paintShader : standard)
                {
                    name = modelName + " runtime material", color = activeTexture == null ? modelColor : Color.white, mainTexture = activeTexture
                };
                if (useGeneratedPaint)
                {
                    if (material.HasProperty("_CamoScale")) material.SetFloat("_CamoScale", vehicle ? .72f : kind == "uas" ? 2.4f : 1.7f);
                    material.SetFloat("_ModelHeight", modelHeight); material.SetFloat("_ModelRadius", modelRadius);
                    material.SetFloat("_PaintMode", vehicle ? 2f : kind == "uas" ? 3f : 1f);
                    material.SetFloat("_BaseCutoff", modelName.Contains("EW Operator") ? .31f : .17f);
                    material.SetColor("_EquipmentColor", side == "red" ? new Color(.42f, .30f, .16f) : new Color(.50f, .38f, .22f));
                    material.SetColor("_WeaponColor", side == "red" ? new Color(.075f, .085f, .075f) : new Color(.08f, .09f, .095f));
                    material.SetColor("_SkinColor", side == "red" ? new Color(.66f, .43f, .31f) : new Color(.76f, .53f, .41f));
                }
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .12f);
                renderer.material = material;
            }
        }

        static void AddCollider(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(); if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds; for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            var collider = model.AddComponent<BoxCollider>(); collider.center = model.transform.InverseTransformPoint(bounds.center);
            var size = model.transform.InverseTransformVector(bounds.size); collider.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        }
    }

    public sealed class CampaignMiniatureMarker : MonoBehaviour
    {
        public string unitId;
        public Transform modelRoot;
        public GameObject selectionRing;
        public GameObject targetRing;
        public Renderer baseRenderer;
    }

    public sealed class CampaignMiniatureSet
    {
        readonly ProceduralBattleTerrain terrain;
        readonly Dictionary<string, CampaignMiniatureMarker> markers = new Dictionary<string, CampaignMiniatureMarker>();
        readonly Material blueBase;
        readonly Material redBase;
        readonly Material darkBase;
        readonly Material selected;
        readonly Material targeted;

        public CampaignMiniatureSet(ProceduralBattleTerrain terrainSource, UnitData[] units)
        {
            terrain = terrainSource;
            blueBase = MaterialFor("Blue unit base", new Color(.055f, .22f, .29f));
            redBase = MaterialFor("Red unit base", new Color(.34f, .07f, .055f));
            darkBase = MaterialFor("Inactive unit base", new Color(.09f, .10f, .095f));
            selected = MaterialFor("Selected unit ring", new Color(1f, .78f, .10f), true);
            targeted = MaterialFor("Target unit ring", new Color(1f, .18f, .08f), true);
            foreach (var unit in units ?? new UnitData[0]) Spawn(unit);
        }

        void Spawn(UnitData unit)
        {
            var root = new GameObject(unit.name + " - 3D campaign miniature");
            var marker = root.AddComponent<CampaignMiniatureMarker>(); marker.unitId = unit.id;
            marker.selectionRing = Cylinder("Selection ring", root.transform, .07f, 1.05f, selected); marker.selectionRing.SetActive(false);
            marker.targetRing = Cylinder("Target ring", root.transform, .10f, .94f, targeted); marker.targetRing.SetActive(false);
            var unitBase = Cylinder("Faction unit base", root.transform, .18f, .80f, unit.side == "blue" ? blueBase : redBase);
            marker.baseRenderer = unitBase.GetComponent<Renderer>();
            var visual = new GameObject("Facing miniature").transform; visual.SetParent(root.transform); marker.modelRoot = visual;
            var modelName = ImportedMiniatureFactory.ModelFor(unit);
            if (!ImportedMiniatureFactory.CreateModel(modelName, unit.side, unit.flying ? "uas" : unit.kind, visual, Vector3.zero))
                Fallback(unit, visual);
            if (!unit.facingSet) { unit.facing = unit.side == "blue" ? 0f : 180f; unit.facingSet = true; }
            root.transform.rotation = Quaternion.Euler(0f, unit.facing, 0f);
            markers[unit.id] = marker;
        }

        public void Sync(UnitData[] units, string selectedId, string targetId)
        {
            foreach (var unit in units ?? new UnitData[0])
            {
                CampaignMiniatureMarker marker; if (!markers.TryGetValue(unit.id, out marker)) { Spawn(unit); marker = markers[unit.id]; }
                marker.transform.position = terrain.WorldPoint(unit.x, unit.y);
                marker.transform.rotation = Quaternion.Euler(0f, unit.facing, 0f);
                marker.selectionRing.SetActive(unit.id == selectedId);
                marker.targetRing.SetActive(unit.id == targetId && unit.id != selectedId);
                marker.modelRoot.localRotation = unit.status == "downed" || unit.status == "dead" ? Quaternion.Euler(0f, 0f, 78f) : Quaternion.identity;
                marker.baseRenderer.sharedMaterial = unit.status == "dead" ? darkBase : unit.side == "blue" ? blueBase : redBase;
            }
        }

        public void FaceMovement(UnitData unit, float oldX, float oldY, float newX, float newY)
        {
            CampaignMiniatureMarker marker; if (!markers.TryGetValue(unit.id, out marker)) return;
            var from = terrain.WorldPoint(oldX, oldY); var to = terrain.WorldPoint(newX, newY); var direction = to - from; direction.y = 0f;
            if (direction.sqrMagnitude > .001f)
            {
                marker.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                unit.facing = marker.transform.eulerAngles.y; unit.facingSet = true;
            }
        }

        static GameObject Cylinder(string name, Transform parent, float y, float radius, Material material)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cylinder); item.name = name; item.transform.SetParent(parent); item.transform.localPosition = new Vector3(0f, y, 0f); item.transform.localScale = new Vector3(radius, .045f, radius);
            item.GetComponent<Renderer>().sharedMaterial = material; var collider = item.GetComponent<Collider>(); if (collider != null) UnityEngine.Object.Destroy(collider); return item;
        }

        static void Fallback(UnitData unit, Transform parent)
        {
            var item = GameObject.CreatePrimitive(unit.flying ? PrimitiveType.Sphere : PrimitiveType.Capsule); item.name = "Fallback miniature"; item.transform.SetParent(parent);
            item.transform.localPosition = new Vector3(0f, unit.flying ? 1.3f : 1.0f, 0f); item.transform.localScale = unit.flying ? new Vector3(.65f, .20f, .65f) : new Vector3(.48f, .72f, .48f);
            item.GetComponent<Renderer>().material = MaterialFor("Fallback " + unit.side, unit.side == "blue" ? new Color(.10f, .56f, .78f) : new Color(.76f, .16f, .12f));
        }

        static Material MaterialFor(string name, Color color, bool unlit = false)
        {
            var shader = Shader.Find(unlit ? "Unlit/Color" : "Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            var material = new Material(shader) { name = name, color = color }; if (!unlit && material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .12f); return material;
        }
    }
}
