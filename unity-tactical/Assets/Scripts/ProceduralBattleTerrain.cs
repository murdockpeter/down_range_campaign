using System;
using System.Collections.Generic;
using UnityEngine;

namespace DownRange.Tactical
{
    public sealed class ProceduralBattleTerrain
    {
        readonly BoardInfo board;
        readonly TerrainInfo profile;
        readonly System.Random random;
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        readonly Transform root;
        readonly float noiseX;
        readonly float noiseZ;
        readonly Dictionary<int, TerrainCellData> authoredCells = new Dictionary<int, TerrainCellData>();
        float[,] surfaceHeights;
        int surfaceColumns;
        int surfaceRows;
        Camera camera;
        Vector3 focus;
        float yaw;
        float pitch = 64f;
        float distance = 47f;

        public bool Ready { get { return camera != null; } }

        public ProceduralBattleTerrain(BoardInfo boardInfo)
        {
            board = boardInfo;
            profile = board?.terrain ?? new TerrainInfo { archetype = "farmland", seed = 1, elevation = .2f, treeDensity = .2f, buildingDensity = .1f, roadPattern = "farm-lanes" };
            random = new System.Random(profile.seed == 0 ? 1 : profile.seed);
            noiseX = (float)random.NextDouble() * 80f;
            noiseZ = (float)random.NextDouble() * 80f;
            if (profile.cells != null) foreach (var cell in profile.cells) authoredCells[cell.y * 10000 + cell.x] = cell;
            root = new GameObject("Generated terrain · " + (profile.locationName ?? profile.archetype)).transform;
            BuildLighting();
            BuildGround();
            BuildRoadNetwork();
            BuildWater();
            BuildStructures();
            BuildVegetation();
            BuildSignatureFeature();
            BuildCamera();
        }

        void BuildLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.43f, .49f, .52f);
            RenderSettings.ambientEquatorColor = new Color(.26f, .30f, .27f);
            RenderSettings.ambientGroundColor = new Color(.10f, .12f, .10f);
            var sun = new GameObject("Terrain sun").AddComponent<Light>();
            sun.transform.SetParent(root); sun.type = LightType.Directional; sun.intensity = 1.1f;
            sun.color = new Color(1f, .91f, .77f); sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -38f, 0f);
        }

        void BuildCamera()
        {
            var clearCamera = new GameObject("Tactical frame clear camera").AddComponent<Camera>();
            clearCamera.depth = -10f; clearCamera.cullingMask = 0; clearCamera.clearFlags = CameraClearFlags.SolidColor;
            clearCamera.backgroundColor = new Color(.018f, .027f, .023f); clearCamera.rect = new Rect(0f, 0f, 1f, 1f);
            camera = Camera.main;
            if (camera == null)
            {
                var host = new GameObject("Procedural tactical camera"); host.tag = "MainCamera"; camera = host.AddComponent<Camera>();
            }
            if (camera.GetComponent<AudioListener>() == null) camera.gameObject.AddComponent<AudioListener>();
            camera.orthographic = true; camera.orthographicSize = 25f; camera.nearClipPlane = .05f; camera.farClipPlane = 300f;
            camera.depth = 0f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .075f, .07f);
            camera.allowHDR = true;
            const float header = 66f, footer = 132f, left = 210f, right = 286f;
            var available = new Rect(left + 9f, header + 9f, Mathf.Max(200f, Screen.width - left - right - 18f), Mathf.Max(160f, Screen.height - header - footer - 30f));
            var aspect = board.widthInches / board.heightInches; var width = available.width; var height = width / aspect;
            if (height > available.height) { height = available.height; width = height * aspect; }
            SetViewport(new Rect(available.x + (available.width - width) * .5f, available.y + (available.height - height) * .5f, width, height));
            camera.orthographicSize = distance * .43f; UpdateCameraTransform();
        }

        void BuildGround()
        {
            var cellSize = profile.gridCellSize <= 0f ? 1f : profile.gridCellSize;
            var columns = Mathf.Max(1, Mathf.RoundToInt(board.widthInches / cellSize));
            var rows = Mathf.Max(1, Mathf.RoundToInt(board.heightInches / cellSize));
            var heights = new float[columns + 1, rows + 1];
            for (var x = 0; x <= columns; x++) for (var z = 0; z <= rows; z++)
            {
                var worldX = -board.widthInches * .5f + board.widthInches * x / columns;
                var worldZ = -board.heightInches * .5f + board.heightInches * z / rows;
                heights[x, z] = HeightAt(worldX, worldZ);
                TerrainCellData cell; if (authoredCells.TryGetValue(Mathf.Min(rows - 1, z) * 10000 + Mathf.Min(columns - 1, x), out cell)) heights[x, z] += cell.elevation;
            }
            for (var pass = 0; pass < Mathf.Clamp(profile.smoothingPasses, 0, 8); pass++)
            {
                var smoothed = (float[,])heights.Clone();
                for (var x = 1; x < columns; x++) for (var z = 1; z < rows; z++)
                    smoothed[x, z] = heights[x, z] * .5f + (heights[x - 1, z] + heights[x + 1, z] + heights[x, z - 1] + heights[x, z + 1]) * .125f;
                heights = smoothed;
            }
            surfaceHeights = heights; surfaceColumns = columns; surfaceRows = rows;
            var vertices = new Vector3[(columns + 1) * (rows + 1)]; var colors = new Color[vertices.Length];
            var triangles = new int[columns * rows * 6];
            for (var z = 0; z <= rows; z++) for (var x = 0; x <= columns; x++)
            {
                var index = z * (columns + 1) + x;
                var worldX = -board.widthInches * .5f + board.widthInches * x / columns;
                var worldZ = -board.heightInches * .5f + board.heightInches * z / rows;
                vertices[index] = new Vector3(worldX, heights[x, z], worldZ);
                colors[index] = GroundColorAt(x, z, columns, rows);
            }
            var triangle = 0;
            for (var z = 0; z < rows; z++) for (var x = 0; x < columns; x++)
            {
                var a = z * (columns + 1) + x; var b = a + 1; var c = a + columns + 1; var d = c + 1;
                triangles[triangle++] = a; triangles[triangle++] = c; triangles[triangle++] = b;
                triangles[triangle++] = b; triangles[triangle++] = c; triangles[triangle++] = d;
            }
            var mesh = new Mesh { name = "one-inch smoothed terrain grid" }; mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var terrainObject = new GameObject("one-inch terrain mesh"); terrainObject.transform.SetParent(root);
            terrainObject.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = terrainObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = TerrainMaterial(); renderer.receiveShadows = true;
            terrainObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            var terrainLos = terrainObject.AddComponent<BattleLosObstacle>(); terrainLos.label = "terrain rise"; terrainLos.classification = "blocked";
            CreateBox("table edge", new Vector3(0f, -.48f, 0f), new Vector3(board.widthInches + 1.2f, .72f, board.heightInches + 1.2f), Material("table", new Color(.20f, .15f, .11f)));
        }

        Color GroundColorAt(int x, int z, int columns, int rows)
        {
            TerrainCellData authored;
            if (authoredCells.TryGetValue(Mathf.Min(rows - 1, z) * 10000 + Mathf.Min(columns - 1, x), out authored))
            {
                switch ((authored.type ?? "").ToLowerInvariant())
                {
                    case "woods": return new Color(.16f, .31f, .17f);
                    case "water": return new Color(.12f, .31f, .36f);
                    case "road": return new Color(.22f, .23f, .21f);
                    case "wet": return new Color(.25f, .32f, .25f);
                    case "built": return new Color(.43f, .42f, .36f);
                }
            }
            var broad = Mathf.PerlinNoise(noiseX + x * .11f, noiseZ + z * .11f);
            var field = ((x / 8) + (z / 6)) % 2 == 0;
            var open = field ? new Color(.35f, .43f, .29f) : new Color(.30f, .39f, .27f);
            var wooded = new Color(.19f, .34f, .20f);
            var wet = new Color(.25f, .32f, .25f);
            var color = Color.Lerp(open, wooded, Mathf.SmoothStep(.48f, .78f, broad) * profile.treeDensity);
            return Color.Lerp(color, wet, profile.wetGround * Mathf.SmoothStep(.12f, .34f, 1f - broad));
        }

        public float HeightAt(float x, float z)
        {
            var scale = .055f;
            var broad = Mathf.PerlinNoise(noiseX + x * scale, noiseZ + z * scale);
            var detail = Mathf.PerlinNoise(noiseZ + x * .12f, noiseX + z * .12f);
            return .06f + profile.elevation * (broad * 1.45f + detail * .28f);
        }

        public float SurfaceHeightAt(float x, float z)
        {
            if (surfaceHeights == null) return HeightAt(x, z);
            var sampleX = Mathf.Clamp((x / board.widthInches + .5f) * surfaceColumns, 0f, surfaceColumns);
            var sampleZ = Mathf.Clamp((z / board.heightInches + .5f) * surfaceRows, 0f, surfaceRows);
            var x0 = Mathf.Clamp(Mathf.FloorToInt(sampleX), 0, surfaceColumns); var x1 = Mathf.Min(surfaceColumns, x0 + 1);
            var z0 = Mathf.Clamp(Mathf.FloorToInt(sampleZ), 0, surfaceRows); var z1 = Mathf.Min(surfaceRows, z0 + 1);
            var tx = sampleX - x0; var tz = sampleZ - z0;
            var a = surfaceHeights[x0, z0]; var b = surfaceHeights[x1, z0]; var c = surfaceHeights[x0, z1]; var d = surfaceHeights[x1, z1];
            return tx + tz <= 1f ? a + tx * (b - a) + tz * (c - a) : d + (1f - tx) * (c - d) + (1f - tz) * (b - d);
        }

        void BuildRoadNetwork()
        {
            var asphalt = Material("asphalt", new Color(.20f, .23f, .22f));
            var dirt = Material("dirt road", new Color(.42f, .36f, .25f));
            var roadMaterial = profile.roadPattern == "farm-lanes" || profile.roadPattern == "trail" ? dirt : asphalt;
            if (profile.roadPattern == "street-grid" || profile.roadPattern == "junction" || profile.roadPattern == "base-loop")
            {
                Road(new Vector3(0f, 0f, 0f), new Vector3(board.widthInches, .12f, 5f), roadMaterial);
                Road(new Vector3(8f, 0f, 0f), new Vector3(5f, .12f, board.heightInches), roadMaterial);
                if (profile.roadPattern == "street-grid") Road(new Vector3(-14f, 0f, 0f), new Vector3(4f, .12f, board.heightInches), roadMaterial);
            }
            else if (profile.roadPattern == "rail-yard")
            {
                Road(new Vector3(0f, 0f, -3f), new Vector3(board.widthInches, .10f, 4.5f), dirt);
                for (var line = -1; line <= 1; line++) Rail(-7f + line * 4.2f);
            }
            else if (profile.roadPattern == "causeway") Road(Vector3.zero, new Vector3(board.widthInches, .28f, 5.5f), asphalt);
            else
            {
                Road(new Vector3(0f, 0f, 3f), new Vector3(board.widthInches, .10f, profile.roadPattern == "service-road" ? 4.2f : 2.6f), roadMaterial);
                Road(new Vector3(14f, 0f, 0f), new Vector3(2.8f, .10f, board.heightInches), roadMaterial);
            }
        }

        void Road(Vector3 center, Vector3 size, Material material)
        {
            CreateTerrainStrip("segmented terrain-conforming road", center, size.x, size.z, .045f, material);
        }

        void Rail(float z)
        {
            var steel = Material("rail", new Color(.16f, .17f, .16f));
            CreateTerrainStrip("segmented terrain-conforming rail", new Vector3(0f, 0f, z - .45f), board.widthInches, .16f, .10f, steel);
            CreateTerrainStrip("segmented terrain-conforming rail", new Vector3(0f, 0f, z + .45f), board.widthInches, .16f, .10f, steel);
        }

        void CreateTerrainStrip(string name, Vector3 center, float width, float depth, float surfaceOffset, Material material)
        {
            var columns = Mathf.Max(1, Mathf.CeilToInt(width)); var rows = Mathf.Max(1, Mathf.CeilToInt(depth));
            var vertices = new Vector3[(columns + 1) * (rows + 1)]; var uv = new Vector2[vertices.Length]; var triangles = new int[columns * rows * 6];
            for (var z = 0; z <= rows; z++) for (var x = 0; x <= columns; x++)
            {
                var index = z * (columns + 1) + x;
                var worldX = center.x - width * .5f + width * x / columns; var worldZ = center.z - depth * .5f + depth * z / rows;
                vertices[index] = new Vector3(worldX, SurfaceHeightAt(worldX, worldZ) + surfaceOffset, worldZ);
                uv[index] = new Vector2(width * x / columns, depth * z / rows);
            }
            var triangle = 0;
            for (var z = 0; z < rows; z++) for (var x = 0; x < columns; x++)
            {
                var a = z * (columns + 1) + x; var b = a + 1; var c = a + columns + 1; var d = c + 1;
                triangles[triangle++] = a; triangles[triangle++] = c; triangles[triangle++] = b;
                triangles[triangle++] = b; triangles[triangle++] = c; triangles[triangle++] = d;
            }
            var mesh = new Mesh { name = name + " one-inch mesh" }; mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var strip = new GameObject(name); strip.transform.SetParent(root); strip.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = strip.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.receiveShadows = true; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void BuildWater()
        {
            if (profile.water < .08f) return;
            var water = Material("water", new Color(.12f, .31f, .36f));
            var crossing = profile.archetype == "dam-crossing";
            var size = crossing ? new Vector3(board.widthInches, .16f, board.heightInches * .34f) : new Vector3(board.widthInches * Mathf.Lerp(.16f, .34f, profile.water), .14f, board.heightInches * .22f);
            var center = crossing ? new Vector3(0f, .18f, -board.heightInches * .28f) : new Vector3(-board.widthInches * .32f, .16f, board.heightInches * .30f);
            CreateBox("water", center, size, water);
        }

        void BuildStructures()
        {
            var count = Mathf.Clamp(Mathf.RoundToInt(profile.buildingDensity * 34f), 1, 26);
            var walls = Material("walls", profile.archetype == "forward-base" ? new Color(.33f, .39f, .31f) : new Color(.58f, .51f, .39f));
            var roof = Material("roofs", new Color(.20f, .23f, .22f));
            for (var i = 0; i < count; i++)
            {
                var x = Range(-board.widthInches * .39f, board.widthInches * .39f); var z = Range(-board.heightInches * .38f, board.heightInches * .38f);
                if (Mathf.Abs(z - 3f) < 4f) z += z < 3f ? -5f : 5f;
                var width = Range(2.4f, profile.archetype == "small-town" ? 5.8f : 4.7f); var depth = Range(2.2f, 4.5f); var height = Range(1.5f, profile.archetype == "small-town" ? 4.8f : 3.2f);
                var ground = SurfaceHeightAt(x, z);
                CreateBox("building", new Vector3(x, ground + height * .5f, z), new Vector3(width, height, depth), walls);
                CreateBox("roof", new Vector3(x, ground + height + .12f, z), new Vector3(width + .18f, .24f, depth + .18f), roof);
            }
        }

        void BuildVegetation()
        {
            var heavyWoods = profile.woodland == "heavy" || profile.treeDensity >= .55f;
            var count = Mathf.Clamp(Mathf.RoundToInt(profile.treeDensity * (heavyWoods ? 96f : 82f)), 0, heavyWoods ? 76 : 58);
            var clusterCount = heavyWoods ? Mathf.Clamp(Mathf.RoundToInt(profile.treeDensity * 10f), 4, 8) : 0;
            var clusters = new Vector2[clusterCount];
            for (var cluster = 0; cluster < clusterCount; cluster++) clusters[cluster] = new Vector2(Range(-board.widthInches * .42f, board.widthInches * .42f), Range(-board.heightInches * .40f, board.heightInches * .40f));
            for (var i = 0; i < count; i++)
            {
                float x, z;
                if (heavyWoods && clusters.Length > 0 && random.NextDouble() < .82)
                {
                    var center = clusters[random.Next(clusters.Length)]; x = center.x + Range(-5.4f, 5.4f); z = center.y + Range(-4.2f, 4.2f);
                }
                else { x = Range(-board.widthInches * .47f, board.widthInches * .47f); z = Range(-board.heightInches * .45f, board.heightInches * .45f); }
                x = Mathf.Clamp(x, -board.widthInches * .47f, board.widthInches * .47f); z = Mathf.Clamp(z, -board.heightInches * .45f, board.heightInches * .45f);
                if (VegetationConflictsWithRoad(x, z)) continue;
                var height = Range(2.0f, heavyWoods ? 4.4f : 3.8f); var speciesRoll = (float)random.NextDouble();
                if (speciesRoll < .30f) CreateBroadleafTree(x, z, height, heavyWoods);
                else if (speciesRoll < .56f) CreateConiferTree(x, z, height, heavyWoods);
                else if (speciesRoll < .76f) CreateBirchTree(x, z, height, heavyWoods);
                else if (speciesRoll < .92f) CreateColumnarTree(x, z, height, heavyWoods);
                else CreateDeadSnag(x, z, height);
                if (heavyWoods && i % 2 == 0) CreateUndergrowth(x + Range(-1.25f, 1.25f), z + Range(-1.1f, 1.1f), true);
                else if (!heavyWoods && i % 7 == 0) CreateUndergrowth(x + Range(-.8f, .8f), z + Range(-.7f, .7f), false);
            }
        }

        bool VegetationConflictsWithRoad(float x, float z)
        {
            if (Mathf.Abs(z - 3f) < (profile.roadPattern == "service-road" ? 3.2f : 2.25f)) return true;
            if (Mathf.Abs(x - 14f) < 1.9f) return true;
            if ((profile.roadPattern == "street-grid" || profile.roadPattern == "junction" || profile.roadPattern == "base-loop") && Mathf.Abs(z) < 3.2f) return true;
            if (profile.roadPattern == "street-grid" && Mathf.Abs(x + 14f) < 2.6f) return true;
            return false;
        }

        void CreateBroadleafTree(float x, float z, float height, bool heavy)
        {
            var ground = SurfaceHeightAt(x, z); var trunkHeight = height * .61f;
            CreateTreeTrunk(x, z, ground, trunkHeight, .18f + height * .018f, Material("oak trunks", new Color(.22f, .13f, .07f)));
            var leaf = Material(heavy ? "heavy broadleaf" : "light broadleaf", heavy ? new Color(.12f, .27f, .13f) : new Color(.25f, .43f, .22f));
            CreateCanopyCollider(x, ground + height * .77f, z, new Vector3(1.05f, height * .25f, .95f), leaf, heavy);
            CreateFoliageDetail("broadleaf crown detail", new Vector3(x - .62f, ground + height * .72f, z + .15f), new Vector3(.72f, height * .20f, .68f), leaf);
            CreateFoliageDetail("broadleaf crown detail", new Vector3(x + .57f, ground + height * .76f, z - .22f), new Vector3(.68f, height * .21f, .72f), leaf);
            CreateFoliageDetail("broadleaf crown detail", new Vector3(x, ground + height * .91f, z), new Vector3(.66f, height * .17f, .63f), leaf);
        }

        void CreateConiferTree(float x, float z, float height, bool heavy)
        {
            var ground = SurfaceHeightAt(x, z); var trunkHeight = height * .80f;
            CreateTreeTrunk(x, z, ground, trunkHeight, .13f + height * .012f, Material("conifer trunks", new Color(.20f, .12f, .065f)));
            var needle = Material(heavy ? "heavy conifer" : "light conifer", heavy ? new Color(.075f, .22f, .12f) : new Color(.12f, .34f, .19f));
            CreateCanopyCollider(x, ground + height * .59f, z, new Vector3(.88f, height * .31f, .88f), needle, heavy);
            CreateFoliageDetail("conifer lower boughs", new Vector3(x, ground + height * .42f, z), new Vector3(1.18f, height * .12f, 1.18f), needle);
            CreateFoliageDetail("conifer middle boughs", new Vector3(x, ground + height * .64f, z), new Vector3(.86f, height * .11f, .86f), needle);
            CreateFoliageDetail("conifer upper boughs", new Vector3(x, ground + height * .82f, z), new Vector3(.50f, height * .10f, .50f), needle);
        }

        void CreateBirchTree(float x, float z, float height, bool heavy)
        {
            var ground = SurfaceHeightAt(x, z); var trunkHeight = height * .76f;
            CreateTreeTrunk(x, z, ground, trunkHeight, .11f + height * .009f, Material("birch trunks", new Color(.70f, .69f, .59f)));
            var leaf = Material(heavy ? "heavy birch leaves" : "birch leaves", heavy ? new Color(.18f, .32f, .13f) : new Color(.38f, .52f, .22f));
            CreateCanopyCollider(x, ground + height * .80f, z, new Vector3(.66f, height * .22f, .61f), leaf, heavy);
            CreateFoliageDetail("birch crown detail", new Vector3(x - .28f, ground + height * .72f, z), new Vector3(.48f, height * .18f, .46f), leaf);
            CreateFoliageDetail("birch crown detail", new Vector3(x + .25f, ground + height * .89f, z + .08f), new Vector3(.43f, height * .16f, .42f), leaf);
        }

        void CreateColumnarTree(float x, float z, float height, bool heavy)
        {
            var ground = SurfaceHeightAt(x, z); var trunkHeight = height * .70f;
            CreateTreeTrunk(x, z, ground, trunkHeight, .12f, Material("young trunks", new Color(.27f, .17f, .08f)));
            var leaf = Material(heavy ? "heavy columnar leaves" : "columnar leaves", heavy ? new Color(.13f, .29f, .12f) : new Color(.30f, .46f, .20f));
            CreateCanopyCollider(x, ground + height * .74f, z, new Vector3(.52f, height * .31f, .52f), leaf, heavy);
            CreateFoliageDetail("columnar crown detail", new Vector3(x, ground + height * .92f, z), new Vector3(.36f, height * .16f, .36f), leaf);
        }

        void CreateDeadSnag(float x, float z, float height)
        {
            var ground = SurfaceHeightAt(x, z); var wood = Material("dead wood", new Color(.34f, .29f, .20f));
            CreateTreeTrunk(x, z, ground, height * .72f, .13f, wood);
            var branch = CreateBox("dead branch detail", new Vector3(x + .23f, ground + height * .53f, z), new Vector3(.55f, .09f, .09f), wood); branch.transform.rotation = Quaternion.Euler(0f, Range(0f, 180f), Range(-34f, 34f));
            var second = CreateBox("dead branch detail", new Vector3(x - .18f, ground + height * .66f, z), new Vector3(.42f, .075f, .075f), wood); second.transform.rotation = Quaternion.Euler(0f, Range(0f, 180f), Range(-28f, 28f));
        }

        void CreateTreeTrunk(float x, float z, float ground, float trunkHeight, float radius, Material material)
        {
            CreatePrimitive(PrimitiveType.Cylinder, "tree trunk", new Vector3(x, ground + trunkHeight * .5f, z), new Vector3(radius, trunkHeight * .5f, radius), material);
        }

        void CreateCanopyCollider(float x, float y, float z, Vector3 scale, Material material, bool heavy)
        {
            CreatePrimitive(PrimitiveType.Sphere, heavy ? "heavy foliage canopy" : "tree crown", new Vector3(x, y, z), scale, material);
        }

        void CreateFoliageDetail(string name, Vector3 position, Vector3 scale, Material material)
        {
            CreatePrimitive(PrimitiveType.Sphere, name, position, scale, material);
        }

        void CreateUndergrowth(float x, float z, bool heavy)
        {
            var ground = SurfaceHeightAt(x, z); var leaf = Material(heavy ? "heavy undergrowth" : "light undergrowth", heavy ? new Color(.10f, .24f, .10f) : new Color(.27f, .41f, .18f));
            CreatePrimitive(PrimitiveType.Sphere, heavy ? "heavy foliage undergrowth" : "tree crown undergrowth", new Vector3(x, ground + .34f, z), new Vector3(.72f, .34f, .62f), leaf);
            CreateFoliageDetail("undergrowth detail", new Vector3(x + .46f, ground + .26f, z - .15f), new Vector3(.46f, .25f, .43f), leaf);
        }

        void BuildSignatureFeature()
        {
            if (profile.archetype == "relay-compound" || profile.archetype == "wooded-ridge")
            {
                var steel = Material("antenna", new Color(.18f, .20f, .19f));
                var x = 17f; var z = -7f; var ground = SurfaceHeightAt(x, z);
                CreatePrimitive(PrimitiveType.Cylinder, "relay mast", new Vector3(x, ground + 4.5f, z), new Vector3(.18f, 4.5f, .18f), steel);
                CreateBox("relay hardstand", new Vector3(x, ground + .08f, z), new Vector3(7f, .16f, 7f), Material("hardstand", new Color(.38f, .39f, .36f)));
            }
            if (profile.archetype == "dam-crossing")
                CreateBox("dam wall", new Vector3(0f, 1.1f, -4f), new Vector3(board.widthInches, 2.2f, 2.4f), Material("dam concrete", new Color(.42f, .44f, .42f)));
            var marker = Material("objective", new Color(.83f, .58f, .15f));
            CreatePrimitive(PrimitiveType.Cylinder, "objective marker", new Vector3(17f, SurfaceHeightAt(17f, -7f) + .18f, -7f), new Vector3(2.2f, .12f, 2.2f), marker);
        }

        public void UpdateInput()
        {
            var speed = 18f * Time.unscaledDeltaTime * Mathf.Lerp(.15f, 1.6f, Mathf.InverseLerp(12f, 105f, distance));
            var horizontal = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            var vertical = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            focus += new Vector3(horizontal, 0f, vertical) * speed;
            if (Input.GetMouseButton(2))
            {
                yaw += Input.GetAxis("Mouse X") * 3.2f;
                pitch += Input.GetAxis("Mouse Y") * 2.8f;
            }
            distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 2.2f, 12f, 105f);
            focus.x = Mathf.Clamp(focus.x, -board.widthInches * .5f, board.widthInches * .5f);
            focus.z = Mathf.Clamp(focus.z, -board.heightInches * .5f, board.heightInches * .5f);
            camera.orthographicSize = Mathf.Clamp(distance * .43f, 5.2f, 47f);
            UpdateCameraTransform();
        }

        void UpdateCameraTransform()
        {
            if (camera == null) return;
            var rotation = Quaternion.Euler(pitch, yaw, 0f); camera.transform.position = focus + rotation * new Vector3(0f, 0f, -distance); camera.transform.LookAt(focus + Vector3.up * .7f);
        }

        public void SetViewport(Rect guiRect)
        {
            if (camera == null) return;
            camera.rect = new Rect(guiRect.x / Screen.width, (Screen.height - guiRect.yMax) / Screen.height, guiRect.width / Screen.width, guiRect.height / Screen.height);
        }

        public Vector2 GuiPoint(float xPercent, float yPercent)
        {
            var world = WorldPoint(xPercent, yPercent); world.y += 1.05f;
            var screen = camera.WorldToScreenPoint(world); return new Vector2(screen.x, Screen.height - screen.y);
        }

        public bool TryPercent(Vector2 guiPoint, out float xPercent, out float yPercent)
        {
            var ray = camera.ScreenPointToRay(new Vector3(guiPoint.x, Screen.height - guiPoint.y));
            var plane = new Plane(Vector3.up, Vector3.zero); float distanceToPlane;
            if (!plane.Raycast(ray, out distanceToPlane)) { xPercent = yPercent = 0f; return false; }
            var point = ray.GetPoint(distanceToPlane);
            xPercent = (point.x / board.widthInches + .5f) * 100f;
            yPercent = (.5f - point.z / board.heightInches) * 100f;
            return xPercent >= 0f && xPercent <= 100f && yPercent >= 0f && yPercent <= 100f;
        }

        public BattleLosResult EvaluateLineOfSight(float startXPercent, float startYPercent, float endXPercent, float endYPercent, string originUnitId = "", string targetUnitId = "")
        {
            Physics.SyncTransforms();
            return BattleLineOfSight.Evaluate(EyePoint(startXPercent, startYPercent, false), EyePoint(endXPercent, endYPercent, false), originUnitId, targetUnitId);
        }

        public BattleLosResult EvaluateLineOfSight(UnitData origin, UnitData target)
        {
            if (origin == null || target == null) return new BattleLosResult();
            Physics.SyncTransforms();
            return BattleLineOfSight.Evaluate(EyePoint(origin.x, origin.y, origin.flying), EyePoint(target.x, target.y, target.flying), origin.id, target.id);
        }

        Vector3 EyePoint(float xPercent, float yPercent, bool flying)
        {
            return WorldPoint(xPercent, yPercent) + Vector3.up * (flying ? 2.25f : 1.45f);
        }

        public Vector3 WorldPoint(float xPercent, float yPercent)
        {
            var x = (xPercent / 100f - .5f) * board.widthInches; var z = (.5f - yPercent / 100f) * board.heightInches;
            return new Vector3(x, SurfaceHeightAt(x, z), z);
        }

        GameObject CreateBox(string name, Vector3 position, Vector3 scale, Material material) { return CreatePrimitive(PrimitiveType.Cube, name, position, scale, material); }
        GameObject CreatePrimitive(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
        {
            var item = GameObject.CreatePrimitive(type); item.name = name; item.transform.SetParent(root); item.transform.position = position; item.transform.localScale = scale;
            var renderer = item.GetComponent<Renderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; renderer.receiveShadows = true;
            var collider = item.GetComponent<Collider>(); var classification = LosClassification(name);
            if (collider != null && classification == null) UnityEngine.Object.Destroy(collider);
            else if (collider != null)
            {
                var obstacle = item.AddComponent<BattleLosObstacle>(); obstacle.classification = classification; obstacle.label = LosLabel(name);
            }
            return item;
        }

        string LosClassification(string name)
        {
            var value = (name ?? "").ToLowerInvariant();
            if (value.Contains("heavy foliage")) return "blocked";
            if (value.Contains("tree crown")) return "partial";
            if (value.Contains("building") || value.Contains("roof") || value.Contains("tree trunk") || value.Contains("relay mast") || value.Contains("dam wall")) return "blocked";
            return null;
        }

        string LosLabel(string name)
        {
            var value = (name ?? "").ToLowerInvariant();
            if (value.Contains("heavy foliage")) return "dense foliage";
            if (value.Contains("tree crown")) return "foliage";
            if (value.Contains("tree trunk")) return "tree trunk";
            if (value.Contains("relay mast")) return "relay mast";
            if (value.Contains("dam wall")) return "dam wall";
            if (value.Contains("roof") || value.Contains("building")) return "building";
            return name;
        }

        Material Material(string key, Color color)
        {
            Material found; if (materials.TryGetValue(key, out found)) return found;
            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse"); found = new Material(shader) { name = key, color = color }; found.SetFloat("_Glossiness", .06f); materials[key] = found; return found;
        }

        Material TerrainMaterial()
        {
            Material found; if (materials.TryGetValue("terrain grid", out found)) return found;
            var shader = Resources.Load<Shader>("Shaders/DownRangeTerrainGrid") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            found = new Material(shader) { name = "one-inch blended terrain" }; materials["terrain grid"] = found; return found;
        }

        float Range(float minimum, float maximum) { return Mathf.Lerp(minimum, maximum, (float)random.NextDouble()); }
    }
}
