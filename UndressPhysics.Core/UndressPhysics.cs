using Studio;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Numerics;

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
using KKAPI.Studio;
using System;
using KKAPI.Studio;
using KKAPI.Studio.UI.Toolbars;
using KKAPI.Utilities;
#endif

namespace UndressPhysics
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class UndressPhysics : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "UndressPhysics";
        public const string Version = "0.9.0.1";
        public const string GUID = "com.alton.illusionplugins.UndressPhysics";
        internal const string _ownerId = "alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "undress_support";
#endif
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types
        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static UndressPhysics _self;
        private static string _assemblyLocation;

        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;
		
        private const int _uniqueId = ('U' << 24) | ('D' << 16) | ('S' << 8) | 'S';

        private Rect _windowRect = new Rect(70, 10, 300, 10);

        internal const string CLOTH_COLLIDER_PREFIX = "Cloth colliders";

        private bool _loaded = false;
        private Status _status = Status.IDLE;
        internal List<UndressData> _undressDataList = new List<UndressData>();

        private AnimationCurve PullCurve;

        private AnimationCurve UndressCurve;

        internal static ConfigEntry<KeyboardShortcut> ConfigKeyDoUndressShortcut { get; private set; }
        internal static ConfigEntry<float> ClothUndressForce { get; private set; }
        internal static ConfigEntry<float> ClothDamping { get; private set; }
        internal static ConfigEntry<float> ClothStiffness { get; private set; }
        internal static ConfigEntry<float> ClothUndressDuration { get; private set; }        

        internal enum Status
        {
            RUN,
            DESTORY,
            IDLE
        }

        #endregion

        #region Accessors
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            ClothDamping = Config.Bind("Cloth", "Damping", 0.5f, new ConfigDescription("", new AcceptableValueRange<float>(0.4f, 0.7f)));

            ClothStiffness = Config.Bind("Cloth", "Stiffness", 5.0f, new ConfigDescription("", new AcceptableValueRange<float>(0.1f, 9.0f)));

            ClothUndressForce = Config.Bind("Option", "Force", 3.0f, new ConfigDescription("multiple", new AcceptableValueRange<float>(1f, 5f)));

            ClothUndressDuration = Config.Bind("Option", "Duration", 10.0f, new ConfigDescription("undress duration", new AcceptableValueRange<float>(0.0f, 90.0f)));

            ConfigKeyDoUndressShortcut = Config.Bind("ShortKey", "Undress key", new KeyboardShortcut(KeyCode.LeftControl, KeyCode.U));

            PullCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

            UndressCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.4f, 0.1f),
                new Keyframe(1f, 1f)
            );

            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;

            var harmony = HarmonyExtensions.CreateInstance(GUID);

            harmony.PatchAll(Assembly.GetExecutingAssembly());

            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open Undress window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(UndressPhysics).Assembly).LoadTexture(),
                false, this, val => _ShowUI = val);
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);
        }

#if SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }
#endif

        protected override void Update()
        {
            if (_loaded == false)
                return;

            if (ConfigKeyDoUndressShortcut.Value.IsDown())
            {   
                DoUndress();
            }
        }

        protected override void OnGUI()
        {
            if (_ShowUI == false)
                return;
            
            if (StudioAPI.InsideStudio) 
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "Undress Physics" + Version);
        }

        private void WindowFunc(int id)
        {

            var studio = Studio.Studio.Instance;

            // ⭐ UI 조작 중이면 Studio 입력 막기
            if (Event.current.type == EventType.MouseDown ||
                Event.current.type == EventType.MouseDrag)
            {
                studio.cameraCtrl.noCtrlCondition = () => true;
            }

            // ⭐ 마우스 떼면 해제
            if (Event.current.type == EventType.MouseUp)
            {
                studio.cameraCtrl.noCtrlCondition = null;
            }

            // ================= UI =================
// Global
            GUILayout.Label("Option");
            GUILayout.BeginHorizontal();
            // Speed
            GUILayout.Label(new GUIContent("F", "Force"), GUILayout.Width(20));
            ClothUndressForce.Value = GUILayout.HorizontalSlider(ClothUndressForce.Value, 1f, 5f);
            GUILayout.Label(ClothUndressForce.Value.ToString("0.00"), GUILayout.Width(40));

            // Duration
            GUILayout.Label(new GUIContent("D", "Duration"), GUILayout.Width(20));
            ClothUndressDuration.Value = GUILayout.HorizontalSlider(ClothUndressDuration.Value, 0.0f, 359.0f);
            GUILayout.Label(ClothUndressDuration.Value.ToString("0.00"), GUILayout.Width(40));

            GUILayout.EndHorizontal();

// Cloth
            GUILayout.Label("Cloth");
            GUILayout.BeginHorizontal();
            
            GUILayout.Label(new GUIContent("D", "Damping"), GUILayout.Width(20));
            ClothDamping.Value = GUILayout.HorizontalSlider(ClothDamping.Value, 0.4f, 0.7f);
            GUILayout.Label(ClothDamping.Value.ToString("0.00"), GUILayout.Width(40));

            GUILayout.Label(new GUIContent("S", "Stiffness"), GUILayout.Width(20));
            ClothStiffness.Value = GUILayout.HorizontalSlider(ClothStiffness.Value, 0.0f, 10.0f);
            GUILayout.Label(ClothStiffness.Value.ToString("0.00"), GUILayout.Width(40));

            GUILayout.EndHorizontal();

            if (GUILayout.Button("Do Undress"))
                DoUndress();

            if (GUILayout.Button("Close"))
                _ShowUI = false;

            // ⭐ 툴팁 직접 그리기
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 150, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }

        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            UIUtility.Init();
            _loaded = true;
        }        

         private IEnumerator Undress(
            UndressData undressData,
            Cloth cloth,
            float duration)
        {
            if (cloth == null)
                yield break;

            cloth.useGravity = true;
            cloth.damping = ClothDamping.Value;
            cloth.stiffnessFrequency = ClothStiffness.Value;
            cloth.worldAccelerationScale = 1.0f;
            cloth.worldVelocityScale = 0.0f;

            var coeffs = cloth.coefficients;
            int vertCount = coeffs.Length;

            float[] startDistances = new float[vertCount];
            for (int i = 0; i < vertCount; i++)
                startDistances[i] = coeffs[i].maxDistance;

            SkinnedMeshRenderer smr = cloth.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
                yield break;

            Vector3[] vertices = smr.sharedMesh.vertices;

            float[] worldYs = new float[vertCount];

            float minY = float.MaxValue;
            float maxY = float.MinValue;

            Matrix4x4 localToWorld = smr.transform.localToWorldMatrix;

            for (int i = 0; i < vertCount; i++)
            {
                float y = localToWorld.MultiplyPoint3x4(vertices[i]).y;
                worldYs[i] = y;

                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            float invRange = (maxY - minY) > 0f ? 1f / (maxY - minY) : 0f;

            float[] normalizedY = new float[vertCount];
            for (int i = 0; i < vertCount; i++)
                normalizedY[i] = (worldYs[i] - minY) * invRange;

            float endPull = 5.0f;

            int topMaxDistance = Int(3f * ClothUndressForce.Value);
            int midMaxDistance = Int(5f * ClothUndressForce.Value);
            int bottomMaxDistance = Int(3f * ClothUndressForce.Value);

            float startRadius = undressData.IsTop ? 0.5f : 0.9f;
            var collider = undressData.collider;

            float timer = 0f;

            while (timer < duration)
            {
                if (cloth == null)
                    yield break;

                float t = timer / duration;

                if (collider != null)
                    collider.radius = Mathf.Lerp(startRadius, 1.5f, t);

                // 🔥 Pull도 곡선 적용
                float pull = PullCurve.Evaluate(t) * endPull;
                cloth.externalAcceleration = Vector3.down * pull;

                bool changed = false;

                for (int i = 0; i < vertCount; i++)
                {
                    // 🔥 height 기반 지연
                    float delay = normalizedY[i] * 0.35f; // 위쪽일수록 늦게
                    float localT = Mathf.Clamp01((t - delay) / (1f - delay));

                    // 🔥 AnimationCurve 적용
                    float curveT = UndressCurve.Evaluate(localT);

                    float targetMaxDistance;

                    if (normalizedY[i] > 0.80f)
                    {
                        // 🔥 위쪽은 초반 유지
                        if (t < 0.2f)
                            continue;

                        targetMaxDistance = Mathf.Lerp(
                            startDistances[i],
                            topMaxDistance,
                            curveT);
                    }
                    else if (normalizedY[i] > 0.40f)
                    {
                        targetMaxDistance = Mathf.Lerp(
                            startDistances[i],
                            midMaxDistance,
                            curveT);
                    }
                    else
                    {
                        targetMaxDistance = Mathf.Lerp(
                            startDistances[i],
                            bottomMaxDistance,
                            curveT);
                    }

                    if (Mathf.Abs(coeffs[i].maxDistance - targetMaxDistance) > 0.0001f)
                    {
                        coeffs[i].maxDistance = targetMaxDistance;
                        changed = true;
                    }
                }

                if (changed)
                    cloth.coefficients = coeffs;

                timer += Time.deltaTime;
                yield return null;
            }
        }

        private int Int(float v)
        {
            throw new NotImplementedException();
        }

        private IEnumerator DoUnressCoroutine(UndressData undressData, Cloth cloth)
        {
            // UnityEngine.Debug.Log($">> DoUnressCoroutine {cloth}");

            if (cloth != null)
            {
                while (!_loaded)
                    yield return null;

                yield return StartCoroutine(Undress(undressData, cloth, ClothUndressDuration.Value));         
            }

            // 기본 spine collider
            if (undressData.collider)            
                if (undressData.IsTop)                
                    undressData.collider.radius = 0.3f;
                else 
                    undressData.collider.radius = 0.5f;
            Logic.RestoreMaxDistances(undressData);
            undressData.coroutine = null;
        }

        private void DoUndress(){
            OCIChar ociChar = null;
            foreach (UndressData undressData in _undressDataList)
            {
                ociChar = undressData.ociChar;
            }

            if (ociChar != null)
            {
                Logic.ReallocateUndressDataList(_self, ociChar);
                foreach (UndressData undressData in _undressDataList)
                {
                    if (undressData != null)
                    {
                        if (undressData.coroutine == null) {
                            undressData.coroutine = StartCoroutine(DoUnressCoroutine(undressData, undressData.cloth));
                        }
                    }
                }
            }            
        }

        #endregion

        #region Patches        

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);
                OCIChar ociChar = objectCtrlInfo as OCIChar;

                // 새로 할당
                Logic.ReallocateUndressDataList(_self, ociChar);
                
                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                Logic.RemoveUndressDataList(_self);
                return true;
            }
        }

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                Logic.RemoveUndressDataList(_self);
                return true;
            }
        }

        #endregion
    }
}
