using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DownRange.Tactical
{
    public sealed class BattleRuntime : MonoBehaviour
    {
        BattleRequest request;
        BattleState state;
        DeterministicDice dice;
        Texture2D mapTexture;
        Texture2D pixel;
        Texture2D infantrySprite;
        Texture2D medicSprite;
        Texture2D uasSprite;
        TacticalAudio audio;
        ProceduralBattleTerrain terrain;
        bool showHelp;
        bool losTool;
        bool losStartSet;
        bool losEndSet;
        Vector2 losStart;
        Vector2 losEnd;
        string requestPath;
        string statePath;
        string resultPath;
        Vector2 rosterScroll;
        Vector2 logScroll;
        string notice = "Select an active unit, then click the map to move or an opposing token to target.";
        GUIStyle titleStyle, smallStyle, panelStyle, tokenStyle, selectedTokenStyle, logStyle, guideStyle, tooltipStyle, objectiveStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<BattleRuntime>() != null || FindFirstObjectByType<OneStarRuntime>() != null) return;
            var oneStar = Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "--one-star", StringComparison.OrdinalIgnoreCase));
            var host = new GameObject(oneStar ? "One Star 3D Runtime" : "Down Range Tactical Runtime");
            if (oneStar) host.AddComponent<OneStarRuntime>(); else host.AddComponent<BattleRuntime>();
        }

        void Awake()
        {
            Application.runInBackground = true;
            pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply();
            audio = new TacticalAudio(gameObject);
            LoadSprites();
            requestPath = FindArgument("--battle-request");
            if (string.IsNullOrWhiteSpace(requestPath)) requestPath = Path.Combine(Application.streamingAssetsPath, "sample-battle-request.json");
            try { LoadBattle(); }
            catch (Exception error) { notice = "Unable to load battle request: " + error.Message; Debug.LogException(error); }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) { showHelp = !showHelp; audio.Play(SoundCue.Click); }
            if (showHelp && Input.GetKeyDown(KeyCode.Escape)) { showHelp = false; audio.Play(SoundCue.Click); }
            if (state != null && !showHelp) terrain?.UpdateInput();
            if (state == null || state.completed || showHelp) return;
            if (Input.GetKeyDown(KeyCode.L)) ToggleLosTool();
            if (Input.GetKeyDown(KeyCode.Space)) EndTurn();
        }

        void ToggleLosTool()
        {
            losTool = !losTool;
            losStartSet = false;
            losEndSet = false;
            notice = losTool ? "LOS tool active: click the first point, then the second. Right-click to reset." : "LOS tool closed.";
            audio.Play(SoundCue.Click);
        }

        void LoadSprites()
        {
            var folder = Path.Combine(Application.streamingAssetsPath, "Sprites");
            infantrySprite = LoadTexture(Path.Combine(folder, "infantry.png"));
            medicSprite = LoadTexture(Path.Combine(folder, "medic.png"));
            uasSprite = LoadTexture(Path.Combine(folder, "uas.png"));
        }

        Texture2D LoadTexture(string path)
        {
            if (!File.Exists(path)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.name = Path.GetFileNameWithoutExtension(path);
            return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
        }

        string FindArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) return Path.GetFullPath(args[i + 1]);
            return string.Empty;
        }

        void LoadBattle()
        {
            if (!File.Exists(requestPath)) throw new FileNotFoundException("Battle request not found.", requestPath);
            request = JsonUtility.FromJson<BattleRequest>(File.ReadAllText(requestPath));
            if (request == null || request.contractVersion != 1 || string.IsNullOrEmpty(request.requestId)) throw new InvalidDataException("Unsupported or incomplete battle request.");
            var exchange = Path.GetDirectoryName(requestPath) ?? Application.persistentDataPath;
            statePath = Path.Combine(exchange, "battle-state.json"); resultPath = Path.Combine(exchange, "battle-result.json");
            if (File.Exists(statePath))
            {
                var restored = JsonUtility.FromJson<BattleState>(File.ReadAllText(statePath));
                if (restored != null && restored.requestId == request.requestId) state = restored;
            }
            if (state == null)
            {
                state = new BattleState { requestId = request.requestId, units = request.units, objectives = request.objectives, events = new BattleEvent[0] };
                state.selectedId = state.units.FirstOrDefault(unit => unit.side == "blue")?.id;
                state.targetId = state.units.FirstOrDefault(unit => unit.side == "red")?.id;
                dice = new DeterministicDice(request.settings.seed);
                RollInitiative();
                AddEvent(string.Format("Initiative: BLUE {0}, RED {1}. {2} acts first.", state.blueInitiative, state.redInitiative, state.activeSide.ToUpperInvariant()), "system");
                Save();
            }
            dice = new DeterministicDice(request.settings.seed, state.rollCount);
            LoadMap();
            terrain = new ProceduralBattleTerrain(request.board);
        }

        void LoadMap()
        {
            if (request.board == null || string.IsNullOrWhiteSpace(request.board.mapPath) || !File.Exists(request.board.mapPath)) return;
            mapTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!mapTexture.LoadImage(File.ReadAllBytes(request.board.mapPath))) mapTexture = null;
        }

        void RollInitiative()
        {
            do { state.blueInitiative = dice.Roll(6); state.redInitiative = dice.Roll(6); } while (state.blueInitiative == state.redInitiative);
            state.activeSide = state.blueInitiative > state.redInitiative ? "blue" : "red";
            state.firstSide = state.activeSide; state.firstSideFinished = false;
        }

        void BuildStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = new Color(.88f, .92f, .88f) } };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true, normal = { textColor = new Color(.58f, .64f, .60f) } };
            panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 9, 9), normal = { background = MakeTexture(new Color(.055f, .078f, .068f, .98f)) } };
            tokenStyle = new GUIStyle(GUI.skin.button) { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { background = pixel, textColor = Color.white }, hover = { background = pixel, textColor = Color.white } };
            selectedTokenStyle = new GUIStyle(tokenStyle) { fontSize = 11 };
            logStyle = new GUIStyle(smallStyle) { fontSize = 9 };
            guideStyle = new GUIStyle(smallStyle) { fontSize = 11, richText = true, normal = { textColor = new Color(.78f, .84f, .79f) } };
            tooltipStyle = new GUIStyle(GUI.skin.box) { fontSize = 11, wordWrap = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(9, 9, 7, 7), normal = { background = MakeTexture(new Color(.035f, .052f, .045f, .98f)), textColor = new Color(.92f, .95f, .91f) } };
            objectiveStyle = new GUIStyle(GUI.skin.box) { fontSize = 9, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 6, 4, 4) };
        }

        Texture2D MakeTexture(Color color) { var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture; }
        UnitData Unit(string id) { return state?.units?.FirstOrDefault(unit => unit.id == id); }
        bool Effective(UnitData unit) { return unit != null && unit.status != "downed" && unit.status != "dead"; }
        bool CanAct(UnitData unit) { return Effective(unit) && ((unit.side == state.activeSide && !unit.actionUsed) || unit.reaction); }
        void ConsumeAction(UnitData unit) { if (unit.reaction) unit.reaction = false; else unit.actionUsed = true; }
        string RollText(DieRoll roll) { return roll.second > 0 ? string.Format("[{0},{1}]→{2}", roll.first, roll.second, roll.result) : roll.result.ToString(CultureInfo.InvariantCulture); }

        void OnGUI()
        {
            BuildStyles();
            GUI.backgroundColor = new Color(.12f, .16f, .14f); GUI.contentColor = new Color(.9f, .93f, .9f);
            var width = Screen.width; var height = Screen.height; const float header = 66f; const float footer = 132f; const float left = 210f; const float right = 286f;
            GUI.Box(new Rect(0, 0, width, header), GUIContent.none, panelStyle);
            GUI.Label(new Rect(16, 9, width * .45f, 23), request == null ? "DOWN RANGE TACTICAL" : request.mission.title.ToUpperInvariant(), titleStyle);
            if (state != null)
            {
                GUI.Label(new Rect(17, 34, 650, 20), string.Format("ROUND {0}  ·  {1} TURN  ·  {2}  ·  INIT B{3}/R{4}  ·  RULES {5}", state.round, state.activeSide.ToUpperInvariant(), request.mission.locationName, state.blueInitiative, state.redInitiative, request.rulesVersion), smallStyle);
                if (GUI.Button(new Rect(width - 492, 16, 68, 34), new GUIContent(audio.Enabled ? "SOUND" : "MUTED", "Toggle all tactical sound cues. This preference is saved."))) { audio.Enabled = !audio.Enabled; audio.Play(SoundCue.Click); }
                if (GUI.Button(new Rect(width - 416, 16, 82, 34), new GUIContent("TURN HELP", "Open the turn sequence and action reference. Shortcut: F1."))) { showHelp = true; audio.Play(SoundCue.Click); }
                if (GUI.Button(new Rect(width - 326, 16, 112, 34), new GUIContent("END TURN", "Finish this side's turn. Units that did not act will automatically hold a reaction. Shortcut: Space."))) EndTurn();
                if (GUI.Button(new Rect(width - 206, 16, 190, 34), new GUIContent(state.completed ? "QUIT TO TRACKER" : "END MISSION", state.completed ? "Close the tactical game and return to Campaign Command." : "Score objectives and export the battle result to the campaign tracker."))) { audio.Play(SoundCue.Click); if (state.completed) Application.Quit(); else FinishBattle(); }
            }
            if (request == null || state == null) { GUI.Label(new Rect(30, 100, width - 60, 80), notice, titleStyle); return; }

            var rosterRect = new Rect(0, header, left, height - header - footer);
            var inspectorRect = new Rect(width - right, header, right, height - header - footer);
            var stageRect = new Rect(left, header, width - left - right, height - header - footer);
            if (showHelp) { DrawHelpOverlay(); return; }
            DrawRoster(rosterRect); DrawBoard(stageRect); DrawInspector(inspectorRect); DrawFooter(new Rect(0, height - footer, width, footer));
            DrawTooltip();
        }

        void DrawRoster(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle); GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16));
            GUILayout.Label("ORDER OF BATTLE", smallStyle); rosterScroll = GUILayout.BeginScrollView(rosterScroll);
            foreach (var side in new[] { "blue", "red" })
            {
                var old = GUI.contentColor; GUI.contentColor = side == "blue" ? new Color(.48f, .78f, .86f) : new Color(.9f, .48f, .41f);
                GUILayout.Label(side.ToUpperInvariant(), titleStyle); GUI.contentColor = old;
                foreach (var unit in state.units.Where(item => item.side == side))
                {
                    var marker = unit.id == state.selectedId ? "▶ " : "  ";
                    var acted = unit.actionUsed ? " · acted" : unit.reaction ? " · reaction" : "";
                    if (GUILayout.Button(new GUIContent(marker + unit.name + "\n    " + unit.role + " · " + unit.status + acted, UnitTooltip(unit)), GUILayout.Height(43))) { state.selectedId = unit.id; audio.Play(SoundCue.Click); }
                }
            }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        void DrawBoard(Rect stage)
        {
            if (terrain == null || !terrain.Ready) GUI.Box(stage, GUIContent.none, panelStyle);
            var available = new Rect(stage.x + 9, stage.y + 9, stage.width - 18, stage.height - 30);
            var board = FitAspect(available, request.board.widthInches / request.board.heightInches);
            terrain?.SetViewport(board);
            if (terrain != null && terrain.Ready) { }
            else if (mapTexture != null) GUI.DrawTexture(board, mapTexture, ScaleMode.StretchToFill, false);
            else { GUI.color = new Color(.16f, .2f, .15f); GUI.DrawTexture(board, pixel); GUI.color = Color.white; GUI.Label(new Rect(board.x + 20, board.y + 20, 300, 40), "MAP ASSET UNAVAILABLE\nGameplay remains functional.", smallStyle); }
            if (terrain == null || !terrain.Ready) { GUI.color = new Color(1f, .78f, .3f, .75f); GUI.Box(new Rect(board.x + board.width * .62f, board.y + board.height * .07f, board.width * .25f, board.height * .31f), "RELAY ZONE"); GUI.color = Color.white; }

            if (losTool) { HandleLosTool(board); DrawLosTool(board); }

            var selected = Unit(state.selectedId); var target = Unit(state.targetId);
            if (!losTool && selected != null && target != null)
            {
                var a = Point(board, selected); var b = Point(board, target); DrawLine(a, b, new Color(1f, .82f, .34f), 1.5f);
                GUI.Label(new Rect((a.x + b.x) / 2f, (a.y + b.y) / 2f, 70, 20), TacticalRules.Distance(selected, target, request.board).ToString("0.0") + "\"", smallStyle);
            }
            foreach (var unit in state.units) DrawToken(board, unit);

            var current = Event.current;
            if (!losTool && current.type == EventType.MouseUp && board.Contains(current.mousePosition))
            {
                var tokenHit = state.units.Any(unit => TokenRect(board, unit).Contains(current.mousePosition));
                if (!tokenHit && TryMoveSelected(board, current.mousePosition)) current.Use();
            }
            var boardHint = losTool ? "LOS TOOL · left-click two points · right-click resets · L closes" : "ONE-INCH TERRAIN GRID · middle-drag rotates/skews · wheel zooms · select a unit, then click its destination";
            GUI.Label(new Rect(stage.x + 12, stage.yMax - 20, stage.width - 24, 18), boardHint, smallStyle);
        }

        void HandleLosTool(Rect board)
        {
            var current = Event.current;
            if (current.type != EventType.MouseDown || !board.Contains(current.mousePosition)) return;
            if (current.button == 1)
            {
                losStartSet = false; losEndSet = false; notice = "LOS measurement reset. Click the first point."; audio.Play(SoundCue.Click); current.Use(); return;
            }
            if (current.button != 0) return;
            Vector2 point;
            if (terrain != null && terrain.Ready)
            {
                float x, y; if (!terrain.TryPercent(current.mousePosition, out x, out y)) return; point = new Vector2(x / 100f, y / 100f);
            }
            else point = new Vector2(Mathf.Clamp01((current.mousePosition.x - board.x) / board.width), Mathf.Clamp01((current.mousePosition.y - board.y) / board.height));
            if (!losStartSet || losEndSet)
            {
                losStart = point; losStartSet = true; losEndSet = false; notice = "LOS start set. Click the second point.";
            }
            else
            {
                losEnd = point; losEndSet = true; notice = string.Format("LOS {0}: {1:0.0}\".", state.cover.ToUpperInvariant(), LosDistance(losStart, losEnd));
            }
            audio.Play(SoundCue.Click); current.Use();
        }

        void DrawLosTool(Rect board)
        {
            if (!losStartSet) return;
            var start = LosPoint(board, losStart);
            var hasPreview = board.Contains(Event.current.mousePosition);
            if (!losEndSet && !hasPreview) { DrawLosEndpoint(start); return; }
            Vector2 endPercent;
            if (losEndSet) endPercent = losEnd;
            else if (terrain != null && terrain.Ready)
            {
                float x, y; if (!terrain.TryPercent(Event.current.mousePosition, out x, out y)) { DrawLosEndpoint(start); return; } endPercent = new Vector2(x / 100f, y / 100f);
            }
            else endPercent = new Vector2(Mathf.Clamp01((Event.current.mousePosition.x - board.x) / board.width), Mathf.Clamp01((Event.current.mousePosition.y - board.y) / board.height));
            var end = LosPoint(board, endPercent);
            var color = state.cover == "blocked" ? new Color(.94f, .26f, .19f) : state.cover == "partial" ? new Color(1f, .68f, .18f) : new Color(.28f, .92f, .56f);
            DrawLine(start, end, color, 3f); DrawLosEndpoint(start); DrawLosEndpoint(end);
            var label = string.Format("LOS {0}\n{1:0.0}\"", state.cover.ToUpperInvariant(), LosDistance(losStart, endPercent));
            var midpoint = (start + end) * .5f; var labelRect = new Rect(midpoint.x - 54f, midpoint.y - 42f, 108f, 38f);
            labelRect.x = Mathf.Clamp(labelRect.x, board.x + 4f, board.xMax - labelRect.width - 4f); labelRect.y = Mathf.Clamp(labelRect.y, board.y + 4f, board.yMax - labelRect.height - 4f);
            GUI.Box(labelRect, label, tooltipStyle);
        }

        Vector2 LosPoint(Rect board, Vector2 percent) { return terrain != null && terrain.Ready ? terrain.GuiPoint(percent.x * 100f, percent.y * 100f) : new Vector2(board.x + board.width * percent.x, board.y + board.height * percent.y); }
        float LosDistance(Vector2 a, Vector2 b)
        {
            var dx = (b.x - a.x) * request.board.widthInches; var dy = (b.y - a.y) * request.board.heightInches;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
        void DrawLosEndpoint(Vector2 point)
        {
            var previous = GUI.color; GUI.color = Color.white; GUI.DrawTexture(new Rect(point.x - 5f, point.y - 5f, 10f, 10f), pixel); GUI.color = previous;
        }

        Rect FitAspect(Rect outer, float aspect)
        {
            var width = outer.width; var height = width / aspect;
            if (height > outer.height) { height = outer.height; width = height * aspect; }
            return new Rect(outer.x + (outer.width - width) / 2f, outer.y + (outer.height - height) / 2f, width, height);
        }
        Vector2 Point(Rect board, UnitData unit) { return terrain != null && terrain.Ready ? terrain.GuiPoint(unit.x, unit.y) : new Vector2(board.x + board.width * unit.x / 100f, board.y + board.height * unit.y / 100f); }
        Rect TokenRect(Rect board, UnitData unit) { var point = Point(board, unit); var size = unit.id == state.selectedId ? 48f : 40f; return new Rect(point.x - size / 2f, point.y - size / 2f, size, size); }
        void DrawToken(Rect board, UnitData unit)
        {
            var rect = TokenRect(board, unit); var previous = GUI.color;
            GUI.color = unit.status == "dead" ? Color.gray : unit.side == "blue" ? new Color(.18f, .68f, .82f) : new Color(.82f, .24f, .18f);
            if (GUI.Button(rect, new GUIContent(string.Empty, UnitTooltip(unit)), unit.id == state.selectedId ? selectedTokenStyle : tokenStyle)) SelectToken(unit);
            GUI.color = previous;
            var sprite = SpriteFor(unit);
            if (sprite != null)
            {
                var inset = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
                if (unit.status == "downed")
                {
                    var matrix = GUI.matrix; GUIUtility.RotateAroundPivot(90f, inset.center); GUI.DrawTexture(inset, sprite, ScaleMode.ScaleToFit, true); GUI.matrix = matrix;
                }
                else GUI.DrawTexture(inset, sprite, ScaleMode.ScaleToFit, true);
            }
            var badge = unit.status == "downed" ? "DOWN" : unit.suppressed ? "SUP" : unit.reaction ? "REACT" : unit.side.ToUpperInvariant();
            GUI.Label(new Rect(rect.x - 2, rect.y - 11, rect.width + 4, 14), badge, new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } });
            GUI.Label(new Rect(rect.x - 45, rect.yMax + 1, 120, 17), unit.name, new GUIStyle(smallStyle) { alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } });
        }
        Texture2D SpriteFor(UnitData unit)
        {
            if (unit.kind == "vehicle") return uasSprite ?? infantrySprite;
            if (unit.medicalSkill > 0 || (!string.IsNullOrEmpty(unit.role) && unit.role.ToLowerInvariant().Contains("medic"))) return medicSprite ?? infantrySprite;
            return infantrySprite;
        }
        string UnitTooltip(UnitData unit)
        {
            var allowance = TacticalRules.MovementAllowance(unit, state.impairedMovement);
            var weapon = unit.weapons != null && unit.weapons.Length > 0 ? unit.weapons[0].name : "Unarmed";
            var availability = unit.side != state.activeSide ? "Waiting for its side's turn" : !Effective(unit) ? "Cannot act while " + unit.status : unit.actionUsed ? "Action already used" : "Ready to act";
            return string.Format("{0} — {1}\n{2} | Move {3:0.#}\" | Skill d{4} | Defense {5}\nWeapon: {6}\n{7}", unit.name, unit.role, unit.status.ToUpperInvariant(), allowance, unit.skill, unit.defense, weapon, availability);
        }
        void SelectToken(UnitData clicked)
        {
            var selected = Unit(state.selectedId);
            if (selected != null && clicked.id != selected.id && (clicked.side != selected.side || clicked.status == "downed")) state.targetId = clicked.id;
            else state.selectedId = clicked.id;
            audio.Play(SoundCue.Click);
        }

        bool TryMoveSelected(Rect board, Vector2 mouse)
        {
            var unit = Unit(state.selectedId);
            if (unit == null || unit.side != state.activeSide || unit.moved || unit.focused || !Effective(unit)) { notice = "That unit cannot move now."; audio.Play(SoundCue.Error); return false; }
            float x, y;
            if (terrain != null && terrain.Ready) { if (!terrain.TryPercent(mouse, out x, out y)) { notice = "Choose a point on the tabletop."; return false; } }
            else { x = Mathf.Clamp((mouse.x - board.x) / board.width * 100f, 0f, 100f); y = Mathf.Clamp((mouse.y - board.y) / board.height * 100f, 0f, 100f); }
            var distance = TacticalRules.Distance(unit.x, unit.y, x, y, request.board); var allowance = TacticalRules.MovementAllowance(unit, state.impairedMovement);
            if (distance > allowance + .05f) { notice = string.Format("Move is {0:0.0}\"; allowance is {1:0.0}\".", distance, allowance); audio.Play(SoundCue.Error); return false; }
            unit.x = x; unit.y = y; unit.moved = true; AddEvent(string.Format("{0} moves {1:0.0}\"{2}.", unit.name, distance, state.impairedMovement ? " through impaired terrain" : ""), "move"); audio.Play(SoundCue.Move); Save(); return true;
        }

        void DrawInspector(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle); GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 9, rect.width - 20, rect.height - 18));
            GUILayout.Label("UNIT CONTROL", smallStyle); var unit = Unit(state.selectedId); var target = Unit(state.targetId);
            if (unit == null) { GUILayout.Label("Select a unit.", titleStyle); GUILayout.EndArea(); return; }
            GUILayout.Label(unit.name, titleStyle); GUILayout.Label(unit.role + " · " + unit.status, smallStyle);
            GUILayout.Label(string.Format("MOVE {0:0.#}\"     SKILL d{1}     DEF {2}", TacticalRules.MovementAllowance(unit, state.impairedMovement), unit.skill, unit.defense), smallStyle);
            GUILayout.Space(8); GUILayout.Label(target == null ? "TARGET: none" : string.Format("TARGET: {0} · {1:0.0}\"", target.name, TacticalRules.Distance(unit, target, request.board)), smallStyle);
            GUILayout.Label("LINE OF SIGHT / COVER", smallStyle);
            var covers = new[] { new GUIContent("Open", "Clear line of sight: no defense modifier."), new GUIContent("Partial", "Target is partly concealed: harder to hit."), new GUIContent("Blocked", "No line of sight: attacks cannot be made.") }; var coverIndex = state.cover == "partial" ? 1 : state.cover == "blocked" ? 2 : 0;
            coverIndex = GUILayout.SelectionGrid(coverIndex, covers, 3); state.cover = coverIndex == 1 ? "partial" : coverIndex == 2 ? "blocked" : "open";
            if (ActionButton(losTool ? "LOS TOOL · ON" : "LOS TOOL · OFF", "Measure sight lines and tabletop distance between any two points. Shortcut: L.", 27)) ToggleLosTool();
            state.impairedMovement = GUILayout.Toggle(state.impairedMovement, new GUIContent("Impaired movement", "Use for mud, climbing, crawling, or other terrain that reduces movement."));
            GUILayout.Space(8);
            var canAct = CanAct(unit); GUI.enabled = canAct && target != null && unit.weapons != null && unit.weapons.Length > 0;
            if (ActionButton(unit.weapons != null && unit.weapons.Length > 0 ? "FIRE · " + unit.weapons[0].name : "FIRE · UNARMED", "Attack the selected opposing target. Uses this unit's action.", 30)) Fire(false);
            if (ActionButton("SUPPRESS", "Attack to apply suppression instead of causing a casualty. Uses this unit's action.", 27)) Fire(true);
            GUI.enabled = canAct && !unit.reaction; if (ActionButton("HOLD REACTION", "Reserve the action. This unit may react during the opposing side's turn.")) { unit.actionUsed = true; unit.reaction = true; AddEvent(unit.name + " holds a reaction.", "action"); audio.Play(SoundCue.Click); Save(); }
            GUI.enabled = canAct && unit.kind == "troop" && !unit.sprint; if (ActionButton("SPRINT", "Trade the action for extra mobility this turn.")) { unit.actionUsed = true; unit.sprint = true; AddEvent(unit.name + " sacrifices its action to sprint.", "move"); audio.Play(SoundCue.Move); Save(); }
            GUI.enabled = canAct; if (ActionButton("FOCUS", "Give up movement and use the action to focus.")) { ConsumeAction(unit); unit.focused = true; unit.moved = true; AddEvent(unit.name + " focuses and gives up movement.", "action"); audio.Play(SoundCue.Click); Save(); }
            GUI.enabled = canAct && unit.radio && target != null; if (ActionButton("RADIO FIRES OBSERVATION", "Mark the selected target for friendly fires. Requires a radio.")) { ConsumeAction(unit); target.observedBy = unit.side; target.observedRound = state.round; AddEvent(unit.name + " observes " + target.name + " for friendly fires.", "signal"); audio.Play(SoundCue.Objective); Save(); }
            GUI.enabled = canAct && target != null && target.side == unit.side && target.status == "downed"; if (ActionButton("TREAT CASUALTY", "Treat an adjacent downed friendly. Select the casualty as the target first.")) Treat(unit, target);
            GUI.enabled = canAct && unit.side == "blue"; if (ActionButton("OBSERVE RELAY", "Make progress on the relay objective while within 18 inches.")) ObserveRelay(unit);
            GUI.enabled = true;
            GUILayout.Space(8); DrawTurnGuide(unit, target);
            GUILayout.Space(5); GUILayout.Label(notice, smallStyle); GUILayout.EndArea();
        }

        bool ActionButton(string label, string tooltip, float height = 23f)
        {
            return GUILayout.Button(new GUIContent(label, tooltip), GUILayout.Height(height));
        }

        void DrawTurnGuide(UnitData unit, UnitData target)
        {
            GUILayout.Label("WHAT CAN THIS UNIT DO?", smallStyle);
            if (losTool)
            {
                var step = !losStartSet ? "1. Click the LOS starting point." : !losEndSet ? "2. Click the LOS endpoint." : "Measurement complete. Click again to start a new line.";
                GUILayout.Box("<color=#91d6be>LOS TOOL ACTIVE</color>\n" + step + "\nOpen = green · Partial = amber · Blocked = red\nRight-click resets · L closes the tool.", guideStyle);
                return;
            }
            string move;
            if (!Effective(unit)) move = "<color=#e18a75>× Cannot move while " + unit.status + ".</color>";
            else if (unit.side != state.activeSide) move = "<color=#9aa39c>○ Wait for the " + unit.side.ToUpperInvariant() + " turn.</color>";
            else if (unit.moved || unit.focused) move = "<color=#9aa39c>✓ Movement used.</color>";
            else move = "<color=#91d6be>1. MOVE: click the map, up to " + TacticalRules.MovementAllowance(unit, state.impairedMovement).ToString("0.#") + "\".</color>";
            string action;
            if (!Effective(unit)) action = "<color=#e18a75>× Cannot take actions.</color>";
            else if (unit.side != state.activeSide && !unit.reaction) action = "<color=#9aa39c>○ No action during this side's turn.</color>";
            else if (unit.reaction) action = "<color=#f1c66b>2. REACTION READY: select an enemy and Fire.</color>";
            else if (unit.actionUsed) action = "<color=#9aa39c>✓ Action used.</color>";
            else if (target == null) action = "<color=#91d6be>2. ACTION: select a target or choose a utility action.</color>";
            else action = "<color=#91d6be>2. ACTION: target " + target.name + " is selected; choose an action.</color>";
            GUILayout.Box(move + "\n" + action + "\n3. END TURN when all units are finished (Space).", guideStyle);
            GUILayout.Label("Hover any token or button for details · F1 opens the full guide", smallStyle);
        }

        void Fire(bool suppress)
        {
            var attacker = Unit(state.selectedId); var target = Unit(state.targetId); var weapon = attacker?.weapons?.FirstOrDefault();
            var result = TacticalRules.Attack(attacker, target, weapon, request.board, state.round, state.cover, suppress, dice);
            if (!result.valid) { notice = result.reason; audio.Play(SoundCue.Error); return; }
            audio.Play(suppress ? SoundCue.Suppress : SoundCue.Fire);
            ConsumeAction(attacker); state.alarm = true;
            if (!result.hit) AddEvent(string.Format("{0} misses {1} (skill {2} vs {3}).", attacker.name, target.name, RollText(result.skill), weapon.difficulty), "miss");
            else if (suppress) { target.suppressed = true; target.suppressedBySide = attacker.side; AddEvent(attacker.name + " suppresses " + target.name + ".", "suppress"); }
            else if (result.casualty) { target.status = "downed"; target.reaction = false; AddEvent(string.Format("{0} downs {1} (damage {2} vs defense {3}).", attacker.name, target.name, RollText(result.damage), result.defense), "hit"); audio.Play(SoundCue.Hit); }
            else AddEvent(string.Format("{0} hits {1}, but causes no casualty (damage {2} vs defense {3}).", attacker.name, target.name, RollText(result.damage), result.defense), "hit");
            Save();
        }

        void Treat(UnitData medic, UnitData target)
        {
            var range = TacticalRules.Distance(medic, target, request.board); if (range > 1.5f) { notice = string.Format("Move adjacent first ({0:0.0}\" away).", range); audio.Play(SoundCue.Error); return; }
            ConsumeAction(medic); medic.focused = true; medic.moved = true; DieRoll roll; target.status = TacticalRules.Medicine(medic, dice, out roll);
            AddEvent(string.Format("{0} treats {1}: {2} — {3}.", medic.name, target.name, RollText(roll), target.status), "medical"); audio.Play(SoundCue.Medical); Save();
        }
        void ObserveRelay(UnitData unit)
        {
            var range = TacticalRules.Distance(unit.x, unit.y, 73f, 22f, request.board); if (range > 18f) { notice = string.Format("Move within 18\" of the relay ({0:0.0}\" now).", range); audio.Play(SoundCue.Error); return; }
            ConsumeAction(unit); state.observationTurns = Mathf.Clamp(state.observationTurns + 1, 0, 2); AddEvent(string.Format("{0} completes relay observation ({1}/2).", unit.name, state.observationTurns), "objective"); audio.Play(SoundCue.Objective); Save();
        }

        void EndTurn()
        {
            var side = state.activeSide;
            foreach (var unit in state.units.Where(item => item.side == side && Effective(item))) if (!unit.actionUsed) unit.reaction = true;
            if (!state.firstSideFinished) { state.firstSideFinished = true; StartSide(side == "blue" ? "red" : "blue"); }
            else { state.round++; RollInitiative(); StartSide(state.activeSide); AddEvent(string.Format("Initiative: BLUE {0}, RED {1}. {2} acts first.", state.blueInitiative, state.redInitiative, state.activeSide.ToUpperInvariant()), "system"); }
            audio.Play(SoundCue.Turn); Save();
        }
        void StartSide(string side)
        {
            state.activeSide = side;
            foreach (var unit in state.units.Where(item => item.side == side)) { unit.actionUsed = false; unit.moved = false; unit.focused = false; unit.sprint = false; unit.reaction = false; }
            foreach (var unit in state.units.Where(item => item.suppressedBySide == side)) { unit.suppressed = false; unit.suppressedBySide = ""; }
            foreach (var unit in state.units.Where(item => item.observedBy == side && item.observedRound < state.round)) { unit.observedBy = ""; unit.observedRound = 0; }
            AddEvent(side.ToUpperInvariant() + " turn begins.", "system");
        }

        void FinishBattle()
        {
            SetObjective("o1", state.observationTurns >= 2); SetObjective("o2", state.observationTurns >= 2);
            var blue = state.units.Where(unit => unit.side == "blue").ToArray(); var effective = blue.Count(Effective);
            SetObjective("o3", blue.Length > 0 && (float)effective / blue.Length >= .75f); SetObjective("o4", !state.alarm);
            var scoreAvailable = state.objectives.Sum(objective => objective.points); var scoreEarned = state.objectives.Where(objective => objective.complete).Sum(objective => objective.points);
            var outcome = scoreAvailable <= 0 ? "Mission complete" : scoreEarned == scoreAvailable ? "Decisive success" : scoreEarned * 2 >= scoreAvailable ? "Partial success" : "Mission setback";
            var result = new BattleResult
            {
                requestId = request.requestId, resultId = Guid.NewGuid().ToString("N"), completedAt = DateTime.UtcNow.ToString("O"), missionNumber = request.mission.number,
                rounds = state.round, alarm = state.alarm, observationTurns = state.observationTurns, events = state.events,
                scoreEarned = scoreEarned, scoreAvailable = scoreAvailable, outcome = outcome, terrainLocationId = request.mission.locationId,
                units = state.units.Select(unit => new UnitResult { id = unit.id, x = unit.x, y = unit.y, status = unit.status }).ToArray(),
                objectives = state.objectives.Select(objective => new ObjectiveResult { id = objective.id, complete = objective.complete }).ToArray(),
                casualties = blue.Where(unit => unit.status == "downed" || unit.status == "dead").Select(unit => new CasualtyResult { unitId = unit.id, category = unit.status == "dead" ? "KIA" : "WIA-S" }).ToArray()
            };
            File.WriteAllText(resultPath, JsonUtility.ToJson(result, true)); state.completed = true; AddEvent(string.Format("Battle result exported: {0}, {1}/{2} objective points.", outcome, scoreEarned, scoreAvailable), "objective"); audio.Play(SoundCue.Objective); Save(); notice = "Result exported and ready for automatic campaign import. You may return to Campaign Command.";
        }
        void SetObjective(string id, bool complete) { var objective = state.objectives.FirstOrDefault(item => item.id == id); if (objective != null) objective.complete = complete; }
        void AddEvent(string text, string kind)
        {
            var events = new List<BattleEvent>(state.events ?? new BattleEvent[0]) { new BattleEvent { round = state.round, text = text, kind = kind } }; state.events = events.ToArray(); notice = text;
        }
        void Save() { if (state == null || string.IsNullOrEmpty(statePath)) return; state.rollCount = dice?.RollCount ?? state.rollCount; File.WriteAllText(statePath, JsonUtility.ToJson(state, true)); }

        void DrawFooter(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle); var left = new Rect(rect.x + 10, rect.y + 8, rect.width * .48f, rect.height - 16); var right = new Rect(rect.x + rect.width * .49f, rect.y + 8, rect.width * .5f, rect.height - 16);
            GUILayout.BeginArea(left); GUILayout.Label("MISSION OBJECTIVES", smallStyle); GUILayout.BeginHorizontal(); var objectiveWidth = Mathf.Max(70f, (left.width - 8f) / Mathf.Max(1, state.objectives.Length)); foreach (var objective in state.objectives) GUILayout.Box((objective.complete ? "✓ " : "○ ") + objective.text, objectiveStyle, GUILayout.Width(objectiveWidth), GUILayout.Height(50)); GUILayout.EndHorizontal(); GUILayout.EndArea();
            GUILayout.BeginArea(right); GUILayout.Label("COMBAT LOG", smallStyle); logScroll = GUILayout.BeginScrollView(logScroll); foreach (var entry in state.events.Reverse().Take(8)) GUILayout.Label("R" + entry.round + "  " + entry.text, logStyle); GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        void DrawTooltip()
        {
            if (string.IsNullOrWhiteSpace(GUI.tooltip)) return;
            var width = Mathf.Min(330f, Screen.width - 24f);
            var height = tooltipStyle.CalcHeight(new GUIContent(GUI.tooltip), width);
            var point = Event.current.mousePosition + new Vector2(16f, 18f);
            if (point.x + width > Screen.width - 8f) point.x = Screen.width - width - 8f;
            if (point.y + height > Screen.height - 8f) point.y -= height + 24f;
            GUI.depth = -100; GUI.Box(new Rect(point.x, point.y, width, height), GUI.tooltip, tooltipStyle);
        }

        void DrawHelpOverlay()
        {
            GUI.depth = -200;
            var shade = GUI.color; GUI.color = new Color(0f, 0f, 0f, .72f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), pixel); GUI.color = shade;
            var width = Mathf.Min(670f, Screen.width - 40f); var height = Mathf.Min(600f, Screen.height - 40f);
            var rect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Box(rect, GUIContent.none, panelStyle);
            GUILayout.BeginArea(new Rect(rect.x + 24, rect.y + 20, rect.width - 48, rect.height - 40));
            GUILayout.BeginHorizontal(); GUILayout.Label("HOW A TURN WORKS", titleStyle); GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("CLOSE  ×", "Close this guide. Shortcuts: F1 or Escape."), GUILayout.Width(95), GUILayout.Height(30))) { showHelp = false; audio.Play(SoundCue.Click); }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("1  SELECT", titleStyle);
            GUILayout.Label("Choose a unit on the map or in the roster. Only the active side can move and take normal actions. Clicking an enemy makes it the target; clicking a downed friendly lets a medic target that casualty.", guideStyle);
            GUILayout.Space(8); GUILayout.Label("2  MOVE", titleStyle);
            GUILayout.Label("Click an empty map position within the selected unit's allowance. A unit normally moves once per turn. Toggle Impaired movement before moving through mud, climbing, or crawling.", guideStyle);
            GUILayout.Space(8); GUILayout.Label("3  ACT", titleStyle);
            GUILayout.Label("Select a target, set Open / Partial / Blocked cover, then Fire or Suppress. Use the LOS tool (L) to measure any sight line and range. Utility actions include Focus, Radio observation, casualty treatment, and relay observation. Sprint trades the action for mobility.", guideStyle);
            GUILayout.Space(8); GUILayout.Label("4  REACT OR END", titleStyle);
            GUILayout.Label("Hold Reaction deliberately, or simply end the turn: every effective unit that has not acted automatically holds a reaction. Reaction fire is available during the opposing side's turn.", guideStyle);
            GUILayout.Space(10); GUILayout.Box("CURRENT SIDE: " + state.activeSide.ToUpperInvariant() + "    ·    ROUND " + state.round + "    ·    L: LOS tool    ·    Space: end turn    ·    F1: help", guideStyle);
            GUILayout.Space(8); GUILayout.Label("Tip: hover over every token, roster entry, cover setting, and action button for contextual details. The right-hand panel always explains the selected unit's next legal step.", smallStyle);
            GUILayout.FlexibleSpace();
            var enabled = audio.Enabled; var next = GUILayout.Toggle(enabled, new GUIContent(" Tactical sound", "Enable or mute the procedural offline sound cues.")); if (next != enabled) { audio.Enabled = next; audio.Play(SoundCue.Click); }
            GUILayout.EndArea();
        }

        void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            var matrix = GUI.matrix; var angle = Vector3.Angle(end - start, Vector2.right); if (start.y > end.y) angle = -angle;
            GUI.color = color; GUIUtility.RotateAroundPivot(angle, start); GUI.DrawTexture(new Rect(start.x, start.y, (end - start).magnitude, width), pixel); GUI.matrix = matrix; GUI.color = Color.white;
        }
    }
}
