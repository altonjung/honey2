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
#endif

namespace UndressSupport
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class UndressSupport : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "UndressSupport";
        public const string Version = "0.9.0.0";
        public const string GUID = "com.alton.illusionplugins.UndressSupport";
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
        internal static UndressSupport _self;
        private static string _assemblyLocation;

#if FEATURE_SPINE_COLLIDER
        internal const string SPINE_COLLIDER_NAME = "Cloth colliders support_spine";
#endif

        private bool _loaded = false;
        private Status _status = Status.IDLE;
        private ObjectCtrlInfo _selectedOCI;

        private Coroutine _UndressCoroutine;


        internal static ConfigEntry<KeyboardShortcut> ConfigKeyDoUndressShortcut { get; private set; }
                
        internal static ConfigEntry<int> ClothUndressSpeed { get; private set; }

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
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            ClothDamping = Config.Bind("Cloth", "Damping", 0.3f, new ConfigDescription("", new AcceptableValueRange<float>(0.0f, 1.0f)));

            ClothStiffness = Config.Bind("Cloth", "Stiffness", 2.0f, new ConfigDescription("", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothUndressSpeed = Config.Bind("Option", "Speed", 3, new ConfigDescription("multiple", new AcceptableValueRange<int>(1, 5)));

            // ClothMaxDistanceTop = Config.Bind("Option", "Top", 3.0f, new ConfigDescription("", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // ClothMaxDistanceMiddle = Config.Bind("Option", "Middle", 8.0f, new ConfigDescription("", new AcceptableValueRange<float>(5.0f, 15.0f)));

            // ClothMaxDistanceBottom = Config.Bind("Option", "Bottom", 10.0f, new ConfigDescription("", new AcceptableValueRange<float>(10.0f, 20.0f)));

            ClothUndressDuration = Config.Bind("Option", "Duration", 10.0f, new ConfigDescription("undress duration", new AcceptableValueRange<float>(0.0f, 90.0f)));

            ConfigKeyDoUndressShortcut = Config.Bind("ShortKey", "Undress key", new KeyboardShortcut(KeyCode.LeftControl, KeyCode.U));

            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;

            var harmony = HarmonyExtensions.CreateInstance(GUID);

            harmony.PatchAll(Assembly.GetExecutingAssembly());
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
            
                if (_UndressCoroutine == null) {
                    _UndressCoroutine = StartCoroutine(DoUnressCoroutine());
                } else
                {
                    Logger.LogMessage($"wait until undress finished"); 
                }
            }
        }

        private void OnSceneLoad(string path)
        {
            _status = Status.DESTORY;
            _selectedOCI = null;
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

        private IEnumerator UndressPart(
            Cloth cloth,
            float[] startDistances,
            float duration,
            int speed)
        {
            if (cloth != null)
            {
                var coeffs = cloth.coefficients;
                SkinnedMeshRenderer smr = cloth.GetComponent<SkinnedMeshRenderer>();
                Vector3[] vertices = smr.sharedMesh.vertices;

                cloth.useGravity = true;

                // 🔹 아래로 당기는 힘 설정
                float startPull = 0.0f;    // 시작은 거의 없음
                float endPull   = 1.0f;    // 끝날 때 강한 하강력 (튜닝 포인트)

                // y좌표 기반 정규화
                float minY = float.MaxValue;
                float maxY = float.MinValue;
                foreach (var v in vertices)
                {
                    float y = smr.transform.TransformPoint(v).y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
                float rangeY = maxY - minY;

                float[] normalizedYs = new float[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    float y = smr.transform.TransformPoint(vertices[i]).y;
                    normalizedYs[i] = (y - minY) / rangeY;
                }

                float timer = 0f;
                int topMaxDistance = 5 * speed;
                int midMaxDistance = 20 * speed;
                int bottomMaxDistance = 50 * speed;

                while (timer < duration)
                {
                    float t = timer / duration;
                    float tSmooth = Mathf.SmoothStep(0f, 1f, t);

                    // ⭐ 시간이 갈수록 아래로 당기는 힘 증가
                    float pullForce = Mathf.Lerp(startPull, endPull, tSmooth);
                    cloth.externalAcceleration = Vector3.down * pullForce;

                    float topScale = Mathf.Lerp(1f, 1.5f, tSmooth);
                    float midScale = Mathf.Lerp(1f, 1.5f, tSmooth);
                    float bottomScale = Mathf.Lerp(1f, 1.5f, tSmooth);

                    for (int i = 0; i < coeffs.Length; i++)
                    {
                        float targetMaxDistance;
                        if (normalizedYs[i] > 0.66f)
                            targetMaxDistance = Mathf.Lerp(startDistances[i], topMaxDistance * topScale, tSmooth);
                        else if (normalizedYs[i] > 0.33f)
                            targetMaxDistance = Mathf.Lerp(startDistances[i], midMaxDistance * midScale, tSmooth);
                        else
                            targetMaxDistance = Mathf.Lerp(startDistances[i], bottomMaxDistance * bottomScale, tSmooth);

                        coeffs[i].maxDistance = targetMaxDistance;
                    }

                    if (cloth == null)
                        break;

                    cloth.coefficients = coeffs;
                    timer += Time.deltaTime;
                    yield return null;
                }

                // ❗ 종료 시 외력은 RestoreMaxDistances에서 정리
            }
        }

        private IEnumerator UndressAll(UndressData undressData, float duration, int speed)
        {
            foreach (var cloth in undressData.clothes)
            {
                if (cloth == null) continue;

                // 물리 안정화
                cloth.useGravity = true;
                cloth.damping = ClothDamping.Value;
                cloth.stiffnessFrequency = ClothStiffness.Value;
                cloth.worldAccelerationScale = 1.0f;
                cloth.worldVelocityScale = 0.0f;

                var coeffs = cloth.coefficients;
                float[] startDistances = new float[coeffs.Length];
                for (int i = 0; i < coeffs.Length; i++)
                    startDistances[i] = coeffs[i].maxDistance;

                yield return StartCoroutine(UndressPart(cloth, startDistances, duration, speed));
            }
        }

        private IEnumerator DoUnressCoroutine()
        {
            UndressData undressData = Logic.GetCloth(_selectedOCI);
            if (undressData != null) {
                _status = Status.RUN;

                while (_status == Status.RUN)
                {
                    if (_loaded == true)
                    {
                        yield return StartCoroutine(UndressAll(undressData, ClothUndressDuration.Value, ClothUndressSpeed.Value));
                        _status = Status.DESTORY;
                    }

                    yield return null;
                }
                
                Logic.RestoreMaxDistances(undressData); 
                _UndressCoroutine = null;
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

                _self._selectedOCI = objectCtrlInfo;                
                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                _self._selectedOCI = null;
                return true;
            }
        }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                UndressData undressData = Logic.GetCloth(_self._selectedOCI);
                if (undressData != null)
                {
                    if (_self._UndressCoroutine != null) {
                        _self.StopCoroutine(_self._UndressCoroutine);
                        Logic.RestoreMaxDistances(undressData);
                        _self._UndressCoroutine = null;
                    }
                }
            }
        }

        // 개별 옷 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        private static class ChaControl_ChangeClothes_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
            {
                UndressData undressData = Logic.GetCloth(_self._selectedOCI);
                if (undressData != null)
                {
                    if (_self._UndressCoroutine != null)
                    {
                        _self.StopCoroutine(_self._UndressCoroutine);
                        Logic.RestoreMaxDistances(undressData);
                        _self._UndressCoroutine = null;
                    }
                }
            }
        }

        // 코디네이션 변경
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {
                UndressData undressData = Logic.GetCloth(_self._selectedOCI);
                if (undressData != null)
                {
                    if (_self._UndressCoroutine != null)
                    {
                        _self.StopCoroutine(_self._UndressCoroutine);
                        Logic.RestoreMaxDistances(undressData);
                        _self._UndressCoroutine = null;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                UndressData undressData = Logic.GetCloth(_self._selectedOCI);
                if (undressData != null)
                {
                    if (_self._UndressCoroutine != null)
                    {
                        _self.StopCoroutine(_self._UndressCoroutine);
                        Logic.RestoreMaxDistances(undressData);
                        _self._UndressCoroutine = null;
                    }
                }
                _self._selectedOCI = null;

                return true;
            }
        }

        #endregion
    }
}
