using Studio;
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;

using UILib;
using UILib.EventHandlers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

#if IPA
using Harmony;
using IllusionPlugin;
#elif BEPINEX
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
#endif
#if AISHOUJO || HONEYSELECT2
using AIChara;
using KKAPI.Studio;
#endif

namespace BakeClothVertex
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
#endif
    public class BakeClothVertex : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "BakeClothVertex";
        public const string Version = "0.1.0";
        public const string GUID = "com.alton.illusionplugins.bakeclothvertex";
        internal const string _ownerId = "Alton";

        private const int TopClothIndex = 0;
        private const int BottomClothIndex = 1;
        private const int BraClothIndex = 2;
        private const int UnderwearClothIndex = 3;
        private static readonly string[] ClothSlotLabels = new[]
        {
            "Top", "Bottom", "Bra", "Pants", "Gloves", "Stockings", "Shoes"
        };
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types
        private sealed class VertexBulgeSession
        {
            public OCIChar ociChar;
            public int outerClothIndex;
            public int innerClothIndex;
            public SkinnedMeshRenderer outerSmr;
            public SkinnedMeshRenderer innerSmr;

            public GameObject previewObject;
            public MeshFilter previewFilter;
            public MeshRenderer previewRenderer;
            public Mesh previewMesh;

            public Mesh bakedOuter = new Mesh();
            public Mesh bakedInner = new Mesh();

            public Vector3[] smoothedWorldVertices;
            public int[] cachedTriangles;
            public Vector2[] cachedUv;
        }

        private struct ClothPairOption
        {
            public int outerClothIndex;
            public int innerClothIndex;
        }
        #endregion

        #region Private Variables
        internal static new ManualLogSource Logger;
        internal static BakeClothVertex _self;

        private bool _loaded;
        private readonly List<VertexBulgeSession> _sessions = new List<VertexBulgeSession>();
        private OCIChar _activeCharacter;
        private readonly List<ClothPairOption> _pairOptions = new List<ClothPairOption>();
        private Rect _windowRect = new Rect(120, 40, 460, 310);
        private Vector2 _pairScroll = Vector2.zero;
        private int _pendingOuter = BottomClothIndex;
        private int _pendingInner = UnderwearClothIndex;

        internal static ConfigEntry<KeyboardShortcut> ConfigToggleShortcut { get; private set; }
        internal static ConfigEntry<float> ConfigInfluenceDistance { get; private set; }
        internal static ConfigEntry<float> ConfigPushStrength { get; private set; }
        internal static ConfigEntry<float> ConfigSmoothing { get; private set; }
        internal static ConfigEntry<bool> ConfigBackfaceOnly { get; private set; }
        #endregion

        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            _self = this;
            Logger = base.Logger;

            ConfigToggleShortcut = Config.Bind("Hotkey", "Toggle Bulge", new KeyboardShortcut(KeyCode.B, KeyCode.LeftControl, KeyCode.LeftShift));
            ConfigInfluenceDistance = Config.Bind("Bulge", "InfluenceDistance", 0.015f, "Max influence distance between outer and inner vertices (meters)");
            ConfigPushStrength = Config.Bind("Bulge", "PushStrength", 0.007f, "Max bulge push offset (meters)");
            ConfigSmoothing = Config.Bind("Bulge", "Smoothing", 0.35f, "Frame smoothing factor (0~1)");
            ConfigBackfaceOnly = Config.Bind("Bulge", "BackfaceOnly", true, "Process only back-facing normals (z<0)");
            EnsureDefaultPairs();

            HarmonyExtensions.CreateInstance(GUID).PatchAll(GetType().Assembly);
        }

#if HONEYSELECT
        protected override void LevelLoaded(int level)
        {
            if (level == 3)
                Init();
        }
#elif SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }
#elif KOIKATSU
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 1)
                Init();
        }
#endif

        protected override void Update()
        {
            if (!_loaded)
                return;

            if (Input.anyKeyDown)
            {
                if (ConfigToggleShortcut.Value.IsDown())
                    ToggleBulgeForCurrentSelection();
            }
        }

        protected override void OnGUI()
        {
            if (!_loaded || Studio.Studio.Instance == null)
                return;

            _windowRect = GUILayout.Window(146231, _windowRect, DrawWindow, "BakeClothVertex Pair UI");
        }

        private void LateUpdate()
        {
            if (!_loaded || _sessions.Count == 0)
                return;

            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                VertexBulgeSession session = _sessions[i];
                if (!IsSessionValid(session))
                {
                    StopSession(session);
                    _sessions.RemoveAt(i);
                    continue;
                }

                UpdateBulgePreview(session);
            }

            if (_sessions.Count == 0)
                _activeCharacter = null;
        }

        protected void OnDestroy()
        {
            StopAllSessions();
        }
        #endregion

        #region Private Methods
        private void Init()
        {
            _loaded = true;
            UIUtility.Init();
            EnsureDefaultPairs();
        }

        private void ToggleBulgeForCurrentSelection()
        {
            // 선택된 캐릭터의 런타임 벌지 미리보기를 토글한다.
            OCIChar current = GetCurrentOCI();
            if (current == null)
            {
                Logger.LogMessage("No selected character.");
                return;
            }

            if (_activeCharacter == current && _sessions.Count > 0)
            {
                StopAllSessions();
                Logger.LogMessage("BakeClothVertex preview stopped.");
                return;
            }

            StopAllSessions();

            List<VertexBulgeSession> created = CreateSessionsForCharacter(current);
            if (created.Count == 0)
            {
                Logger.LogMessage("No available cloth pair found.");
                return;
            }

            _sessions.AddRange(created);
            _activeCharacter = current;
            Logger.LogMessage($"BakeClothVertex preview started. (pairs: {created.Count})");
        }

        private static OCIChar GetCurrentOCI()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null)
                return null;

            TreeNodeObject node = Studio.Studio.Instance.treeNodeCtrl.selectNodes.LastOrDefault();
            return node != null ? Studio.Studio.GetCtrlInfo(node) as OCIChar : null;
        }

        private List<VertexBulgeSession> CreateSessionsForCharacter(OCIChar ociChar)
        {
            List<VertexBulgeSession> sessions = new List<VertexBulgeSession>(_pairOptions.Count);
            HashSet<int> usedOuterSlots = new HashSet<int>();
            for (int i = 0; i < _pairOptions.Count; i++)
            {
                ClothPairOption option = _pairOptions[i];
                if (!usedOuterSlots.Add(option.outerClothIndex))
                    continue;

                if (TryCreateSession(ociChar, option.outerClothIndex, option.innerClothIndex, out VertexBulgeSession created))
                    sessions.Add(created);
            }

            return sessions;
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label("Pair Options (Outer -> Inner)");
            DrawPairSelectorRow("Outer", ref _pendingOuter);
            DrawPairSelectorRow("Inner", ref _pendingInner);

            GUILayout.BeginHorizontal();
            GUI.enabled = _pendingOuter != _pendingInner;
            if (GUILayout.Button("Add Pair"))
                AddPairOption(_pendingOuter, _pendingInner);
            GUI.enabled = true;

            if (GUILayout.Button("Add Default Pairs"))
                EnsureDefaultPairs();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Configured Pairs");
            _pairScroll = GUILayout.BeginScrollView(_pairScroll, GUI.skin.box, GUILayout.Height(110));
            for (int i = 0; i < _pairOptions.Count; i++)
            {
                ClothPairOption option = _pairOptions[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{i + 1}. {GetClothSlotLabel(option.outerClothIndex)} -> {GetClothSlotLabel(option.innerClothIndex)}");
                if (GUILayout.Button("Remove", GUILayout.Width(72)))
                {
                    _pairOptions.RemoveAt(i);
                    i--;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start/Restart Preview"))
                StartOrRestartPreview();
            if (GUILayout.Button("Stop Preview"))
                StopAllSessions();
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        private static void DrawPairSelectorRow(string label, ref int slotIndex)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(50));
            for (int i = 0; i < ClothSlotLabels.Length; i++)
            {
                if (GUILayout.Toggle(slotIndex == i, ClothSlotLabels[i], GUI.skin.button, GUILayout.Width(58)))
                    slotIndex = i;
            }
            GUILayout.EndHorizontal();
        }

        private void StartOrRestartPreview()
        {
            OCIChar current = GetCurrentOCI();
            if (current == null)
            {
                Logger.LogMessage("No selected character.");
                return;
            }

            StopAllSessions();
            List<VertexBulgeSession> created = CreateSessionsForCharacter(current);
            if (created.Count == 0)
            {
                Logger.LogMessage("No available cloth pair found.");
                return;
            }

            _sessions.AddRange(created);
            _activeCharacter = current;
            Logger.LogMessage($"BakeClothVertex preview started. (pairs: {created.Count})");
        }

        private void AddPairOption(int outerClothIndex, int innerClothIndex)
        {
            if (outerClothIndex == innerClothIndex)
                return;

            for (int i = 0; i < _pairOptions.Count; i++)
            {
                if (_pairOptions[i].outerClothIndex == outerClothIndex && _pairOptions[i].innerClothIndex == innerClothIndex)
                    return;
            }

            _pairOptions.Add(new ClothPairOption
            {
                outerClothIndex = outerClothIndex,
                innerClothIndex = innerClothIndex
            });
        }

        private void EnsureDefaultPairs()
        {
            AddPairOption(TopClothIndex, BraClothIndex);
            AddPairOption(BottomClothIndex, UnderwearClothIndex);
        }

        private static string GetClothSlotLabel(int clothSlotIndex)
        {
            if (clothSlotIndex < 0 || clothSlotIndex >= ClothSlotLabels.Length)
                return $"Slot {clothSlotIndex}";
            return ClothSlotLabels[clothSlotIndex];
        }

        private static bool TryCreateSession(OCIChar ociChar, int outerClothIndex, int innerClothIndex, out VertexBulgeSession session)
        {
            session = null;
            if (ociChar == null || ociChar.charInfo == null || ociChar.charInfo.objClothes == null)
                return false;

            SkinnedMeshRenderer outerSmr = FindPrimaryClothRenderer(ociChar, outerClothIndex);
            SkinnedMeshRenderer innerSmr = FindPrimaryClothRenderer(ociChar, innerClothIndex);

            if (outerSmr == null)
                return false;

            if (innerSmr == null)
                return false;

            GameObject previewObject = new GameObject($"BCV_RuntimePreview_{outerClothIndex}_{innerClothIndex}");
            // 원본 SkinnedMeshRenderer는 숨기고, 런타임으로 갱신되는 preview mesh를 렌더링한다.
            previewObject.transform.SetParent(outerSmr.transform, false);
            previewObject.transform.localPosition = Vector3.zero;
            previewObject.transform.localRotation = Quaternion.identity;
            previewObject.transform.localScale = Vector3.one;

            MeshFilter mf = previewObject.AddComponent<MeshFilter>();
            MeshRenderer mr = previewObject.AddComponent<MeshRenderer>();
            mr.sharedMaterials = outerSmr.sharedMaterials;
            mr.shadowCastingMode = outerSmr.shadowCastingMode;
            mr.receiveShadows = outerSmr.receiveShadows;

            Mesh previewMesh = new Mesh();
            previewMesh.name = "BCV_RuntimeMesh";
            previewMesh.MarkDynamic();
            mf.sharedMesh = previewMesh;

            outerSmr.enabled = false;

            session = new VertexBulgeSession
            {
                ociChar = ociChar,
                outerClothIndex = outerClothIndex,
                innerClothIndex = innerClothIndex,
                outerSmr = outerSmr,
                innerSmr = innerSmr,
                previewObject = previewObject,
                previewFilter = mf,
                previewRenderer = mr,
                previewMesh = previewMesh
            };

            return true;
        }

        private void StopAllSessions()
        {
            for (int i = 0; i < _sessions.Count; i++)
                StopSession(_sessions[i]);
            _sessions.Clear();
            _activeCharacter = null;
        }

        private static void StopSession(VertexBulgeSession session)
        {
            if (session == null)
                return;

            if (session.outerSmr != null)
                session.outerSmr.enabled = true;

            if (session.previewObject != null)
                Destroy(session.previewObject);

            if (session.previewMesh != null)
                Destroy(session.previewMesh);

            if (session.bakedOuter != null)
                Destroy(session.bakedOuter);

            if (session.bakedInner != null)
                Destroy(session.bakedInner);
        }

        private static bool IsSessionValid(VertexBulgeSession session)
        {
            return session != null
                && session.ociChar != null
                && session.outerSmr != null
                && session.innerSmr != null
                && session.previewMesh != null
                && session.previewFilter != null;
        }

        private static SkinnedMeshRenderer FindPrimaryClothRenderer(OCIChar ociChar, int clothIndex)
        {
            if (ociChar == null || ociChar.charInfo == null || ociChar.charInfo.objClothes == null)
                return null;
            if (clothIndex < 0 || clothIndex >= ociChar.charInfo.objClothes.Length)
                return null;

            GameObject clothObj = ociChar.charInfo.objClothes[clothIndex];
            if (clothObj == null)
                return null;

            SkinnedMeshRenderer[] renderers = clothObj.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].sharedMesh != null)
                    return renderers[i];
            }

            return null;
        }

        private void UpdateBulgePreview(VertexBulgeSession session)
        {
            // MVP: 매 프레임 outer/inner를 BakeMesh하고, inner 근접 거리 기반으로 outer를 normal 방향으로 밀어낸다.
            float influenceDist = Mathf.Max(0.001f, ConfigInfluenceDistance.Value);
            float pushStrength = Mathf.Max(0.0001f, ConfigPushStrength.Value);
            float smoothing = Mathf.Clamp01(ConfigSmoothing.Value);
            bool backfaceOnly = ConfigBackfaceOnly.Value;

            session.outerSmr.BakeMesh(session.bakedOuter);
            session.innerSmr.BakeMesh(session.bakedInner);

            Vector3[] outerVerts = session.bakedOuter.vertices;
            Vector3[] outerNormals = session.bakedOuter.normals;
            int outerCount = outerVerts.Length;

            if (outerCount == 0 || outerNormals == null || outerNormals.Length != outerCount)
                return;

            Vector3[] innerWorldVerts = BuildWorldVertices(session.innerSmr.transform, session.bakedInner.vertices);
            Dictionary<Vector3Int, List<int>> innerGrid = BuildInnerVertexGrid(innerWorldVerts, influenceDist);

            Transform previewTransform = session.previewFilter.transform;
            Vector3[] previewLocalVerts = new Vector3[outerCount];
            Vector3[] previewLocalNormals = new Vector3[outerCount];

            if (session.smoothedWorldVertices == null || session.smoothedWorldVertices.Length != outerCount)
                session.smoothedWorldVertices = new Vector3[outerCount];

            for (int i = 0; i < outerCount; i++)
            {
                Vector3 worldPos = session.outerSmr.transform.TransformPoint(outerVerts[i]);
                Vector3 worldNormal = session.outerSmr.transform.TransformDirection(outerNormals[i]).normalized;

                if (backfaceOnly && worldNormal.z >= 0f)
                {
                    session.smoothedWorldVertices[i] = worldPos;
                    previewLocalVerts[i] = previewTransform.InverseTransformPoint(worldPos);
                    previewLocalNormals[i] = previewTransform.InverseTransformDirection(worldNormal);
                    continue;
                }

                if (TryFindNearestInner(worldPos, innerWorldVerts, innerGrid, influenceDist, out float nearestDist))
                {
                    float t = 1f - Mathf.Clamp01(nearestDist / influenceDist);
                    float push = pushStrength * t * t;
                    worldPos += worldNormal * push;
                }

                Vector3 prev = session.smoothedWorldVertices[i];
                if (prev == Vector3.zero)
                    prev = worldPos;

                Vector3 smoothed = Vector3.Lerp(prev, worldPos, smoothing);
                session.smoothedWorldVertices[i] = smoothed;

                previewLocalVerts[i] = previewTransform.InverseTransformPoint(smoothed);
                previewLocalNormals[i] = previewTransform.InverseTransformDirection(worldNormal).normalized;
            }

            if (session.cachedTriangles == null || session.cachedTriangles.Length != session.bakedOuter.triangles.Length)
                session.cachedTriangles = session.bakedOuter.triangles;

            if (session.cachedUv == null || session.cachedUv.Length != session.bakedOuter.uv.Length)
                session.cachedUv = session.bakedOuter.uv;

            Mesh m = session.previewMesh;
            m.Clear();
            m.vertices = previewLocalVerts;
            m.normals = previewLocalNormals;
            m.triangles = session.cachedTriangles;

            if (session.cachedUv != null && session.cachedUv.Length == previewLocalVerts.Length)
                m.uv = session.cachedUv;

            m.RecalculateBounds();
        }

        private static Vector3[] BuildWorldVertices(Transform tr, Vector3[] localVertices)
        {
            if (localVertices == null)
                return Array.Empty<Vector3>();

            Vector3[] world = new Vector3[localVertices.Length];
            for (int i = 0; i < localVertices.Length; i++)
                world[i] = tr.TransformPoint(localVertices[i]);
            return world;
        }

        private static Dictionary<Vector3Int, List<int>> BuildInnerVertexGrid(Vector3[] vertices, float cellSize)
        {
            var grid = new Dictionary<Vector3Int, List<int>>(vertices.Length);
            float inv = 1f / cellSize;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                Vector3Int key = new Vector3Int(
                    Mathf.FloorToInt(v.x * inv),
                    Mathf.FloorToInt(v.y * inv),
                    Mathf.FloorToInt(v.z * inv));

                if (!grid.TryGetValue(key, out List<int> list))
                {
                    list = new List<int>(8);
                    grid[key] = list;
                }
                list.Add(i);
            }

            return grid;
        }

        private static bool TryFindNearestInner(
            Vector3 worldPos,
            Vector3[] innerWorldVerts,
            Dictionary<Vector3Int, List<int>> innerGrid,
            float maxDistance,
            out float nearestDistance)
        {
            nearestDistance = float.MaxValue;
            float maxDistanceSq = maxDistance * maxDistance;
            float inv = 1f / maxDistance;

            Vector3Int baseKey = new Vector3Int(
                Mathf.FloorToInt(worldPos.x * inv),
                Mathf.FloorToInt(worldPos.y * inv),
                Mathf.FloorToInt(worldPos.z * inv));

            bool found = false;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Vector3Int key = new Vector3Int(baseKey.x + dx, baseKey.y + dy, baseKey.z + dz);
                        if (!innerGrid.TryGetValue(key, out List<int> candidates))
                            continue;

                        for (int i = 0; i < candidates.Count; i++)
                        {
                            Vector3 p = innerWorldVerts[candidates[i]];
                            float distSq = (p - worldPos).sqrMagnitude;
                            if (distSq > maxDistanceSq)
                                continue;

                            if (distSq < nearestDistance * nearestDistance)
                            {
                                nearestDistance = Mathf.Sqrt(distSq);
                                found = true;
                            }
                        }
                    }
                }
            }

            return found;
        }
        #endregion
    }
}
