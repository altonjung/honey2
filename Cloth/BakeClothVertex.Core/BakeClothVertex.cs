using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.Threading.Tasks;
using System.Numerics;
using System.Xml;
using System.Globalization;

#if IPA
using Harmony;
using IllusionPlugin;
#elif BEPINEX
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
#endif
#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using ADV.Commands.Camera;
using IllusionUtility.GetUtility;
using ADV.Commands.Object;
#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
using static Illusion.Utils;
using System.Runtime.Remoting.Messaging;
#endif
using KKAPI.Studio;
using KKAPI.Studio.UI.Toolbars;
using KKAPI.Utilities;
using KKAPI.Chara;

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
        public const string Version = "0.9.0.0";
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
        private static readonly int[] AllowedOuterSlots = { TopClothIndex, BottomClothIndex };
        private static readonly int[] AllowedInnerSlots = { BraClothIndex, UnderwearClothIndex };
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

            public UnityEngine.Mesh originalOuterSharedMesh;
            public UnityEngine.Mesh runtimeOuterMesh;

            public UnityEngine.Mesh bakedOuter = new UnityEngine.Mesh();
            public UnityEngine.Mesh bakedInner = new UnityEngine.Mesh();

            public Vector3[] smoothedWorldVertices;
            public int[][] cachedSubMeshTriangles;
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
        private static bool _showUI;
        private static SimpleToolbarToggle _toolbarButton;
        private readonly List<VertexBulgeSession> _sessions = new List<VertexBulgeSession>();
        private OCIChar _activeCharacter;
        private readonly List<ClothPairOption> _pairOptions = new List<ClothPairOption>();
        private const int _uniqueId = ('B' << 24) | ('C' << 16) | ('V' << 8) | 'X';
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

            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open ClothVertex window",
                () => LoadToolbarIconSafe(),
                false, this, val =>
                {
                    _showUI = val;                    
                });
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);

            var harmony = HarmonyExtensions.CreateInstance(GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());            
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
            if (!_loaded || Studio.Studio.Instance == null || !_showUI)
                return;

            if (!StudioAPI.InsideStudio)
                return;

            _windowRect = GUILayout.Window(_uniqueId + 1, _windowRect, DrawWindow, "BakeClothVertex Pair UI");
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
            if (Studio.Studio.Instance != null && Studio.Studio.Instance.cameraCtrl != null)
                Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;

            StopAllSessions();
        }
        #endregion

        #region Private Methods
        private void Init()
        {
            _loaded = true;
            EnsureDefaultPairs();
        }

        private static Texture2D LoadToolbarIconSafe()
        {
            return ResourceUtils
                .GetEmbeddedResource("toolbar_icon.png", typeof(BakeClothVertex).Assembly)
                .LoadTexture();
        }

        private void ToggleBulgeForCurrentSelection()
        {
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
                {
                    sessions.Add(created);
                }
                else
                {
                    Logger.LogMessage($"Pair skipped: {GetClothSlotLabel(option.outerClothIndex)} -> {GetClothSlotLabel(option.innerClothIndex)} ({GetSessionFailureReason(ociChar, option.outerClothIndex, option.innerClothIndex)})");
                }
            }

            return sessions;
        }

        private void DrawWindow(int id)
        {
            var studio = Studio.Studio.Instance;

            bool guiUsingMouse = GUIUtility.hotControl != 0;
            bool mouseInWindow = _windowRect.Contains(Event.current.mousePosition);

            if (guiUsingMouse || mouseInWindow)
                studio.cameraCtrl.noCtrlCondition = () => true;
            else
                studio.cameraCtrl.noCtrlCondition = null;

            OCIChar current = GetCurrentOCI();
            bool hasCurrent = current != null;
            if (!AllowedOuterSlots.Contains(_pendingOuter))
                _pendingOuter = TopClothIndex;
            if (!AllowedInnerSlots.Contains(_pendingInner))
                _pendingInner = UnderwearClothIndex;

            bool canAddPairForCurrent = hasCurrent
                && _pendingOuter != _pendingInner
                && HasValidClothObjects(current, _pendingOuter, _pendingInner);

            GUILayout.Label("Pair Options (Outer -> Inner)");
            DrawPairSelectorRow("Outer", ref _pendingOuter, AllowedOuterSlots);
            DrawPairSelectorRow("Inner", ref _pendingInner, AllowedInnerSlots);

            GUILayout.BeginHorizontal();
            GUI.enabled = canAddPairForCurrent;
            if (GUILayout.Button("Add Pair"))
                AddPairOptionForCurrentCharacter(_pendingOuter, _pendingInner);
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
            GUI.enabled = hasCurrent;
            if (GUILayout.Button("Start/Restart Preview"))
                StartOrRestartPreview();
            if (GUILayout.Button("Stop Preview"))
                StopAllSessions();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (!hasCurrent)
                GUILayout.Label("Select a character to add pair bindings and start preview.");
            else if (!canAddPairForCurrent && _pendingOuter != _pendingInner)
                GUILayout.Label("Selected slots must both have valid cloth objects.");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Close"))
            {
                studio.cameraCtrl.noCtrlCondition = null;
                _showUI = false;
            }
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

        private static void DrawPairSelectorRow(string label, ref int slotIndex, int[] allowedSlots)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(50));
            for (int i = 0; i < allowedSlots.Length; i++)
            {
                int allowedSlot = allowedSlots[i];
                if (GUILayout.Toggle(slotIndex == allowedSlot, ClothSlotLabels[allowedSlot], GUI.skin.button, GUILayout.Width(58)))
                    slotIndex = allowedSlot;
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

        private void AddPairOptionForCurrentCharacter(int outerClothIndex, int innerClothIndex)
        {
            OCIChar current = GetCurrentOCI();
            if (current == null)
            {
                Logger.LogMessage("No selected character.");
                return;
            }

            if (!HasValidClothObjects(current, outerClothIndex, innerClothIndex))
            {
                Logger.LogMessage("Binding skipped: selected outer/inner cloth objects are not both valid.");
                return;
            }

            AddPairOption(outerClothIndex, innerClothIndex);
        }

        private void AddPairOption(int outerClothIndex, int innerClothIndex)
        {
            if (!AllowedOuterSlots.Contains(outerClothIndex) || !AllowedInnerSlots.Contains(innerClothIndex))
                return;

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
            if (ociChar == null || GetClothesArray(ociChar) == null)
                return false;
            if (!HasValidClothObjects(ociChar, outerClothIndex, innerClothIndex))
                return false;

            SkinnedMeshRenderer outerSmr = FindPrimaryClothRenderer(ociChar, outerClothIndex);
            SkinnedMeshRenderer innerSmr = FindPrimaryClothRenderer(ociChar, innerClothIndex);

            if (outerSmr == null)
                return false;

            if (innerSmr == null)
                return false;

            UnityEngine.Mesh originalShared = outerSmr.sharedMesh;
            if (originalShared == null)
                return false;

            UnityEngine.Mesh runtimeMesh = UnityEngine.Object.Instantiate(originalShared);
            runtimeMesh.name = $"{originalShared.name}_BCV_Runtime";
            runtimeMesh.MarkDynamic();
            outerSmr.sharedMesh = runtimeMesh;

            session = new VertexBulgeSession
            {
                ociChar = ociChar,
                outerClothIndex = outerClothIndex,
                innerClothIndex = innerClothIndex,
                outerSmr = outerSmr,
                innerSmr = innerSmr,
                originalOuterSharedMesh = originalShared,
                runtimeOuterMesh = runtimeMesh
            };

            return true;
        }

        private static bool HasValidClothObjects(OCIChar ociChar, int outerClothIndex, int innerClothIndex)
        {
            return TryGetClothObject(ociChar, outerClothIndex, out _)
                && TryGetClothObject(ociChar, innerClothIndex, out _);
        }

        private static bool TryGetClothObject(OCIChar ociChar, int clothIndex, out GameObject clothObj)
        {
            clothObj = null;
            GameObject[] clothes = GetClothesArray(ociChar);
            if (clothes == null)
                return false;
            if (clothIndex < 0 || clothIndex >= clothes.Length)
                return false;

            clothObj = clothes[clothIndex];
            return clothObj != null;
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

            if (session.outerSmr != null && session.originalOuterSharedMesh != null)
                session.outerSmr.sharedMesh = session.originalOuterSharedMesh;

            if (session.runtimeOuterMesh != null)
                Destroy(session.runtimeOuterMesh);

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
                && session.runtimeOuterMesh != null;
        }

        private static SkinnedMeshRenderer FindPrimaryClothRenderer(OCIChar ociChar, int clothIndex)
        {
            GameObject[] clothes = GetClothesArray(ociChar);
            if (clothes == null)
                return null;
            if (clothIndex < 0 || clothIndex >= clothes.Length)
                return null;

            GameObject clothObj = clothes[clothIndex];
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

        private static GameObject[] GetClothesArray(OCIChar ociChar)
        {
            if (ociChar == null)
                return null;

            ChaControl chaControl = ociChar.GetChaControl();
            if (chaControl != null && chaControl.objClothes != null)
                return chaControl.objClothes;

            if (ociChar.charInfo != null && ociChar.charInfo.objClothes != null)
                return ociChar.charInfo.objClothes;

            return null;
        }

        private static string GetSessionFailureReason(OCIChar ociChar, int outerClothIndex, int innerClothIndex)
        {
            if (ociChar == null)
                return "character is null";

            GameObject[] clothes = GetClothesArray(ociChar);
            if (clothes == null)
                return "clothes array is null";

            if (outerClothIndex < 0 || outerClothIndex >= clothes.Length)
                return $"outer slot out of range ({outerClothIndex})";
            if (innerClothIndex < 0 || innerClothIndex >= clothes.Length)
                return $"inner slot out of range ({innerClothIndex})";

            if (clothes[outerClothIndex] == null)
                return "outer cloth object is null";
            if (clothes[innerClothIndex] == null)
                return "inner cloth object is null";

            if (FindPrimaryClothRenderer(ociChar, outerClothIndex) == null)
                return "outer renderer not found";
            if (FindPrimaryClothRenderer(ociChar, innerClothIndex) == null)
                return "inner renderer not found";

            return "unknown";
        }

        private void UpdateBulgePreview(VertexBulgeSession session)
        {
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

            Transform previewTransform = session.outerSmr.transform;
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

            int subMeshCount = session.bakedOuter.subMeshCount;
            if (session.cachedSubMeshTriangles == null || session.cachedSubMeshTriangles.Length != subMeshCount)
            {
                session.cachedSubMeshTriangles = new int[subMeshCount][];
                for (int sub = 0; sub < subMeshCount; sub++)
                    session.cachedSubMeshTriangles[sub] = session.bakedOuter.GetTriangles(sub);
            }

            if (session.cachedUv == null || session.cachedUv.Length != session.bakedOuter.uv.Length)
                session.cachedUv = session.bakedOuter.uv;

            UnityEngine.Mesh m = session.runtimeOuterMesh;
            m.Clear();
            m.vertices = previewLocalVerts;
            m.normals = previewLocalNormals;
            m.subMeshCount = subMeshCount;
            for (int sub = 0; sub < subMeshCount; sub++)
                m.SetTriangles(session.cachedSubMeshTriangles[sub], sub, true);

            if (session.cachedUv != null && session.cachedUv.Length == previewLocalVerts.Length)
                m.uv = session.cachedUv;
            if (session.bakedOuter.tangents != null && session.bakedOuter.tangents.Length == previewLocalVerts.Length)
                m.tangents = session.bakedOuter.tangents;
            if (session.bakedOuter.colors32 != null && session.bakedOuter.colors32.Length == previewLocalVerts.Length)
                m.colors32 = session.bakedOuter.colors32;
            if (session.bakedOuter.uv2 != null && session.bakedOuter.uv2.Length == previewLocalVerts.Length)
                m.uv2 = session.bakedOuter.uv2;
            if (session.bakedOuter.uv3 != null && session.bakedOuter.uv3.Length == previewLocalVerts.Length)
                m.uv3 = session.bakedOuter.uv3;
            if (session.bakedOuter.uv4 != null && session.bakedOuter.uv4.Length == previewLocalVerts.Length)
                m.uv4 = session.bakedOuter.uv4;

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
