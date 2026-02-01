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
using UILib;
using UnityEngine;
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
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using KKAPI.Studio;
using IllusionUtility.GetUtility;
using System.Dynamic;
#endif

// 추가 작업 예정
// - direction 자동 360도 회전

namespace WindPhysics
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class WindPhysics : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "WindPhysics";
        public const string Version = "0.9.5.3";
        public const string GUID = "com.alton.illusionplugins.windphysics";
        internal const string _ownerId = "alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "wind_physics";
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
        internal static WindPhysics _self;

        internal static Dictionary<string, string> boneDict = new Dictionary<string, string>();

        private static string _assemblyLocation;
        private bool _loaded = false;

        internal List<ObjectCtrlInfo> _selectedOCIs = new List<ObjectCtrlInfo>();

        private float _minY = float.MaxValue;
        private float _maxY = float.MinValue;

        private bool _previousConfigKeyEnableWind;

        // 위치에 따른 바람 강도
        private AnimationCurve _heightToForceCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.1f); // 위로 갈수록 약함

        internal Dictionary<int, WindData> _ociObjectMgmt = new Dictionary<int, WindData>();

        private Coroutine _CheckWindMgmtRoutine;    

        #endregion

        #region Accessors
        internal static ConfigEntry<bool> ConfigKeyEnableWind { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyEnableWindShortcut { get; private set; }

        internal static ConfigEntry<float> Gravity { get; private set; }
        internal static ConfigEntry<float> WindDirection { get; private set; }
        internal static ConfigEntry<float> WindInterval { get; private set; }
        internal static ConfigEntry<float> WindUpForce { get; private set; }
        internal static ConfigEntry<float> WindForce { get; private set; }

        internal static ConfigEntry<float> AccesoriesForce { get; private set; }
        internal static ConfigEntry<float> AccesoriesAmplitude { get; private set; }
        internal static ConfigEntry<float> AccesoriesDamping { get; private set; }
        internal static ConfigEntry<float> AccesoriesStiffness { get; private set; }

        internal static ConfigEntry<float> HairForce { get; private set; }
        internal static ConfigEntry<float> HairAmplitude { get; private set; }
        internal static ConfigEntry<float> HairDamping { get; private set; }
        internal static ConfigEntry<float> HairStiffness { get; private set; }

        internal static ConfigEntry<float> ClotheForce { get; private set; }
        internal static ConfigEntry<float> ClotheAmplitude { get; private set; }
        internal static ConfigEntry<float> ClothDamping { get; private set; }
        internal static ConfigEntry<float> ClothStiffness { get; private set; }    
        // internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            // Environment 
            Gravity = Config.Bind("All", "Gravity", -0.03f, new ConfigDescription("Gravity", new AcceptableValueRange<float>(-0.1f, 0.1f)));

            WindDirection = Config.Bind("All", "Direction", 0f, new ConfigDescription("wind direction from 0 to 360 degree", new AcceptableValueRange<float>(0.0f, 359.0f)));

            WindUpForce = Config.Bind("All", "ForceUp", 0.0f, new ConfigDescription("wind up force", new AcceptableValueRange<float>(0.0f, 0.5f)));

            WindForce = Config.Bind("All", "Force", 0.1f, new ConfigDescription("wind force", new AcceptableValueRange<float>(0.1f, 1.0f)));
#if FEATURE_PUBLIC
            WindInterval = Config.Bind("All", "Interval", 2f, new ConfigDescription("wind spawn interval(sec)", new AcceptableValueRange<float>(1.0f, 10.0f)));
#else
            WindInterval = Config.Bind("All", "Interval", 2f, new ConfigDescription("wind spawn interval(sec)", new AcceptableValueRange<float>(0.0f, 30.0f)));
#endif
            // clothes
            ClotheForce = Config.Bind("Cloth", "Force", 1.0f, new ConfigDescription("cloth force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            ClotheAmplitude = Config.Bind("Cloth", "Amplitude", 0.5f, new ConfigDescription("cloth amplitude", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothDamping = Config.Bind("Cloth", "Damping", 0.3f, new ConfigDescription("cloth damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            ClothStiffness = Config.Bind("Cloth", "Stiffness", 2.0f, new ConfigDescription("wind stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // hair
            HairForce = Config.Bind("Hair", "Force", 1.0f, new ConfigDescription("hair force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            HairAmplitude = Config.Bind("Hair", "Amplitude", 0.5f, new ConfigDescription("hair amplitude", new AcceptableValueRange<float>(0.0f, 10.0f)));

            HairDamping = Config.Bind("Hair", "Damping", 0.15f, new ConfigDescription("hair damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            HairStiffness = Config.Bind("Hair", "Stiffness", 0.3f, new ConfigDescription("hair stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // accesories
            AccesoriesForce = Config.Bind("Misc", "Force", 1.0f, new ConfigDescription("accesories force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            AccesoriesAmplitude = Config.Bind("Misc", "Amplitude", 0.3f, new ConfigDescription("accesories amplitude", new AcceptableValueRange<float>(0.0f, 10.0f)));

            AccesoriesDamping = Config.Bind("Misc", "Damping", 0.7f, new ConfigDescription("accesories damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            AccesoriesStiffness = Config.Bind("Misc", "Stiffness", 1.0f, new ConfigDescription("accesories stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // option 
            ConfigKeyEnableWind = Config.Bind("Options", "Toggle effect", false, "Wind enabled/disabled");

            ConfigKeyEnableWindShortcut = Config.Bind("ShortKey", "Toggle effect key", new KeyboardShortcut(KeyCode.W));

            _previousConfigKeyEnableWind = ConfigKeyEnableWind.Value;


            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

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

            if (ConfigKeyEnableWindShortcut.Value.IsDown())
            {
                ConfigKeyEnableWind.Value = !ConfigKeyEnableWind.Value;
            }
        }

        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            // UIUtility.Init();
            _loaded = true;


            _CheckWindMgmtRoutine = StartCoroutine(CheckWindMgmtRoutine());
        }

        private void MgmtInit()
        {
            foreach (var kvp in _ociObjectMgmt)
            {
                var key = kvp.Key;
                WindData value = kvp.Value;

                value.clothes.Clear();
                value.hairDynamicBones.Clear();
                OCIChar ociChar = value.objectCtrlInfo as OCIChar;

                if (ociChar != null)
                {
                    StopWindEffect(value);
                }
#if FEATURE_ITEM_SUPPORT
                OCIItem ociItem = value.objectCtrlInfo as OCIItem;
                if (ociItem != null)
                {
                    StopWindEffect(value);
                }
#endif
            }
            _ociObjectMgmt.Clear();
            _selectedOCIs.Clear();
        }

        // n 개 대상 아이템에 대해 active/inactive 동시 적용 처리 
        IEnumerator CheckWindMgmtRoutine()
        {
            while (true)
            {
                if(_previousConfigKeyEnableWind != ConfigKeyEnableWind.Value)
                {

                    foreach (ObjectCtrlInfo ctrlInfo in _selectedOCIs)
                    {
                        OCIChar ociChar = ctrlInfo as OCIChar;
                        if (ociChar != null && _self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData))
                        {                          
                            if (ConfigKeyEnableWind.Value)
                            {
                                OCIChar ociChar1 = windData.objectCtrlInfo as OCIChar;

                                if (windData.coroutine == null)
                                {                      
                                    windData.wind_status = Status.RUN;
                                    windData.coroutine = ociChar1.charInfo.StartCoroutine(WindRoutine(windData));
                                }
                            } 
                            else
                            {
                                windData.wind_status = Status.STOP;
                            }                            
                        }
#if FEATURE_ITEM_SUPPORT
                        OCIItem ociItem = ctrlInfo as OCIItem;
                        if (ociItem != null && _self._ociObjectMgmt.TryGetValue(ociItem.GetHashCode(), out var windData1))
                        {                          
                            if (ConfigKeyEnableWind.Value)
                            {
                                OCIItem ociItem1 = windData1.objectCtrlInfo as OCIItem;

                                if (windData1.wind_status == Status.RUN)
                                {
                                    if (windData1.coroutine == null)
                                    {                      
                                        windData1.coroutine = ociChar.charInfo.StartCoroutine(WindRoutine(windData1));
                                    }
                                }
                            } else
                            {
                                windData1.wind_status = Status.STOP;
                            }                            
                        }
#endif
                    }                        

                    _previousConfigKeyEnableWind = ConfigKeyEnableWind.Value;          
                }

                yield return new WaitForSeconds(1.0f); // 1.0초 대기
            }
        }

        void ApplyWind(Vector3 windEffect, float factor, WindData windData)
        {
            float time = Time.time;

            // factor 자체가 바람 에너지
            windEffect *= factor;

            // =========================
            // Hair
            // =========================
            foreach (var bone in windData.hairDynamicBones)
            {
                if (bone == null)
                    continue;

                float wave = Mathf.Sin(time * HairAmplitude.Value);
                if (wave < 0f) wave = 0f; // 위로만

                Vector3 finalWind = windEffect * WindForce.Value * HairForce.Value;
                finalWind.y += wave * WindUpForce.Value * factor;

                bone.m_Damping = HairDamping.Value;
                bone.m_Stiffness = HairStiffness.Value;
                bone.m_Force = finalWind;

                if (Gravity.Value >= 0)
                {
                    bone.m_Gravity = new Vector3(
                    0,
                    UnityEngine.Random.Range(Gravity.Value, Gravity.Value + 0.02f),
                    0
                    );   
                } else
                {
                    bone.m_Gravity = new Vector3(
                    0,
                    UnityEngine.Random.Range(Gravity.Value, -0.005f),
                    0
                    );  
                }
            }

            // =========================
            // Accessories
            // =========================
            foreach (var bone in windData.accesoriesDynamicBones)
            {
                if (bone == null)
                    continue;

                float wave = Mathf.Sin(time * AccesoriesAmplitude.Value);
                if (wave < 0f) wave = 0f;

                Vector3 finalWind = windEffect * WindForce.Value * AccesoriesForce.Value;;
                finalWind.y += wave * WindUpForce.Value * factor;

                bone.m_Damping = AccesoriesDamping.Value;
                bone.m_Stiffness = AccesoriesStiffness.Value;
                bone.m_Force = finalWind;

                if (Gravity.Value >= 0)
                {
                    bone.m_Gravity = new Vector3(
                    0,
                    UnityEngine.Random.Range(Gravity.Value, Gravity.Value + 0.03f),
                    0
                    );   
                } else
                {
                    bone.m_Gravity = new Vector3(
                    0,
                    UnityEngine.Random.Range(Gravity.Value - 0.02f, -0.01f),
                    0
                    );  
                }
            }

            // =========================
            // Clothes
            // =========================
            foreach (var cloth in windData.clothes)
            {
                if (cloth == null)
                    continue;

                float rawWave = Mathf.Sin(time * ClotheAmplitude.Value);

                float upWave   = Mathf.Max(rawWave, 0f);   // 올라갈 때
                float downWave = Mathf.Max(-rawWave, 0f);  // 내려올 때
                
                upWave = Mathf.SmoothStep(0f, 1f, upWave);
                downWave = Mathf.SmoothStep(0f, 1f, downWave);

                Vector3 baseWind = windEffect.normalized;

                // =========================
                // Directional (XZ)
                // =========================
                Vector3 externalWind =
                    baseWind * WindForce.Value * ClotheForce.Value;

                // =========================
                // Random + Upward
                // =========================
                float noise =
                    (Mathf.PerlinNoise(time * 0.8f, 0f) - 0.5f) * 2f;

                // 🔥 upward는 강하게
                float upBoost = 5.0f;

                // 🔻 downward는 거의 힘 주지 않음
                float downReduce = 0.15f;   // 0.1 ~ 0.3 권장

                Vector3 randomWind =
                    baseWind * noise * WindForce.Value * ClotheForce.Value +
                    Vector3.up *
                    (
                        upWave   * WindUpForce.Value * upBoost -
                        downWave * WindUpForce.Value * downReduce
                    );

                // =========================
                // Cloth physics
                // =========================
                cloth.worldAccelerationScale = 1.0f;
                cloth.worldVelocityScale = 0.0f;

                if (Gravity.Value >= 0) {
                    cloth.useGravity = false;
                    // 위로 작용하는 가짜 중력
                    cloth.externalAcceleration = Vector3.up * Gravity.Value;

                    cloth.randomAcceleration =
                        randomWind * 20f * factor * 20;
                } else
                {
                    cloth.useGravity = true;

                    cloth.externalAcceleration =
                        externalWind * 30f * factor * 20;    
                
                    cloth.randomAcceleration =
                        randomWind * 80f * factor * 20;
                }
                
                // 🔧 하강 시 damping 증가 → elastic 제거 핵심
                float downDampingBoost = 2.0f;
                cloth.damping = ClothDamping.Value;

                cloth.stiffnessFrequency = ClothStiffness.Value;
            }
        }

        private IEnumerator FadeoutWindEffect_Cloth(
            List<Cloth> clothes,
            int settleFrames = 15,
            float settleForce = 0.2f)
        {
            if (clothes == null || clothes.Count == 0)
                yield break;

            // 1. Wind 즉시 제거 + grounding force 적용
            foreach (var cloth in clothes)
            {
                if (cloth == null) continue;

                cloth.randomAcceleration   = Vector3.zero;
                cloth.externalAcceleration = Vector3.down * settleForce;
            }

            // 2. 프레임 단위로 안정화 대기
            for (int i = 0; i < settleFrames; i++)
                yield return null; // LateUpdate 여러 번 보장

            // 3. 정상 상태 복귀
            foreach (var cloth in clothes)
            {
                if (cloth == null) continue;

                cloth.externalAcceleration = Vector3.zero;
            }
        }

        private IEnumerator FadeoutWindEffect_DynamicBone(
            List<DynamicBone> dynamicBones,
            int settleFrames = 15,
            float settleGravity = 0.2f)
        {
            if (dynamicBones == null || dynamicBones.Count == 0)
                yield break;

            // 1. Wind 즉시 제거 + grounding gravity 적용
            foreach (var bone in dynamicBones)
            {
                if (bone == null) continue;

                // 바람 계열 제거
                bone.m_Force = Vector3.zero;

                // 착지 유도 중력
                bone.m_Gravity = Vector3.down * settleGravity;
            }

            // 2. 프레임 기반 안정화 대기
            for (int i = 0; i < settleFrames; i++)
                yield return null; // LateUpdate 여러 번 보장

            // 3. 기본 상태 복귀
            foreach (var bone in dynamicBones)
            {
                if (bone == null) continue;

                // 프로젝트 기본값에 맞게 조정 가능
                bone.m_Gravity = Vector3.zero;
            }
        }

        internal void StopWindEffect(WindData windData)
        {
            if (windData.coroutine != null)
            {
                windData.wind_status = Status.STOP;
            }
        }

        internal IEnumerator WindRoutine(WindData windData)
        {
            while (true)
            {
                if (_loaded == true)
                {
                    if (windData.wind_status == Status.RUN)
                    {
                        // y 위치 기반 바람세기 처리를 위한 위치 정보 획득
                        foreach (var bone in windData.hairDynamicBones)
                        {
                            if (bone == null)
                                continue;

                            float y = bone.m_Root.position.y;
                            _minY = Mathf.Min(_minY, y);
                            _maxY = Mathf.Max(_maxY, y);
                        }

                        Quaternion globalRotation = Quaternion.Euler(0f, WindDirection.Value, 0f);

                        // 방향에 랜덤성 부여 (약한 변화만 허용)
                        float angleY = UnityEngine.Random.Range(-15, 15); // 위/아래 유지 (음수면 아래 방향, 양수면 위 방향)
                        float angleX = UnityEngine.Random.Range(-7, 7);    // 좌우 유지
                        Quaternion localRotation = Quaternion.Euler(angleX, angleY, 0f);

                        Quaternion rotation = globalRotation * localRotation;

                        Vector3 direction = rotation * Vector3.back;

                        // 기본 바람 강도는 낮게 유지
                        Vector3 windEffect = direction.normalized * UnityEngine.Random.Range(0.1f, 0.15f);

                        // 적용
                        ApplyWind(windEffect, 1.0f, windData);
                        yield return new WaitForSeconds(0.2f);

                        // 자연스럽게 사라짐
                        float keepwindTime = WindInterval.Value/2;

                        float fadeTime = Mathf.Lerp(keepwindTime, keepwindTime, WindForce.Value);
                        float t = 0f;
                        while (t < fadeTime)
                        {
                            t += Time.deltaTime;
                            float factor = Mathf.SmoothStep(1f, 0f, t / fadeTime); // 부드러운 감소                                
                            ApplyWind(windEffect, factor, windData);
                            yield return null;
                        }
                        
                        if (keepwindTime <= 0.3)
                            yield return null;
                        else    
                            yield return new WaitForSeconds(WindInterval.Value - keepwindTime);

                    }
                    else if (windData.wind_status == Status.STOP)
                    {
                        yield return StartCoroutine(FadeoutWindEffect_Cloth(windData.clothes));
                        yield return StartCoroutine(FadeoutWindEffect_DynamicBone(windData.hairDynamicBones));
                        yield return StartCoroutine(FadeoutWindEffect_DynamicBone(windData.accesoriesDynamicBones));
                        
                        yield break;
                    }
                }

                yield return null;
            }

            windData.clothes.Clear();
            windData.hairDynamicBones.Clear();
            windData.accesoriesDynamicBones.Clear();
            windData.coroutine = null;
        }

        #endregion

        #region Patches

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo selectedCtrlInfo = Studio.Studio.GetCtrlInfo(_node);
                // UnityEngine.Debug.Log($">> OnSelectSingle {selectedCtrlInfo}");

                if (selectedCtrlInfo == null)
                    return true;

                List<ObjectCtrlInfo> deleteObjInfos = new List<ObjectCtrlInfo>();

                // 삭제 대상 선별 
                foreach(ObjectCtrlInfo objCtrlInfo in _self._selectedOCIs)
                {
                    if (objCtrlInfo != null && objCtrlInfo != selectedCtrlInfo)
                    {
                        deleteObjInfos.Add(objCtrlInfo);
                    }
                }

                // 삭제 대상 stop 처리
                foreach(ObjectCtrlInfo objCtrlInfo in deleteObjInfos)
                {
                    if (_self._ociObjectMgmt.TryGetValue(objCtrlInfo.GetHashCode(), out var windData))
                    {
                        windData.wind_status = Status.STOP;
                    }
                }

                // 기존 클릭 대상이 아니면, 기존 대상은 STOP 처리
                List<ObjectCtrlInfo> objCtrlInfos = new List<ObjectCtrlInfo>(); 
                objCtrlInfos.Add(selectedCtrlInfo);

                Logic.TryAllocateObject(objCtrlInfos);
             
                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectMultiple))]
        private static class WorkspaceCtrl_OnSelectMultiple_Patches
        {
            private static bool Prefix(object __instance)
            {
                List<ObjectCtrlInfo> selectedObjCtrlInfos = new List<ObjectCtrlInfo>(); 
                foreach (TreeNodeObject node in Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes)
                {
                    ObjectCtrlInfo ctrlInfo = Studio.Studio.GetCtrlInfo(node);
                    if (ctrlInfo == null)
                        continue;

                    selectedObjCtrlInfos.Add(ctrlInfo);                  
                }

                List<ObjectCtrlInfo> deleteObjInfos = new List<ObjectCtrlInfo>();
                // 삭제 대상 선별 
                foreach(ObjectCtrlInfo objCtrlInf1 in _self._selectedOCIs)
                {
                    foreach(ObjectCtrlInfo objCtrlInfo2 in selectedObjCtrlInfos)
                    if (objCtrlInf1 != null && objCtrlInf1 != objCtrlInfo2)
                    {
                        deleteObjInfos.Add(objCtrlInf1);
                    }
                }

                // 삭제 대상 stop 처리
                foreach(ObjectCtrlInfo objCtrlInfo in deleteObjInfos)
                {
                    if (_self._ociObjectMgmt.TryGetValue(objCtrlInfo.GetHashCode(), out var windData))
                    {
                        windData.wind_status = Status.STOP;
                    }
                }

                Logic.TryAllocateObject(selectedObjCtrlInfos); 

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo unselectedCtrlInfo = Studio.Studio.GetCtrlInfo(_node);

                if (unselectedCtrlInfo == null)
                    return true;

                if (_self._ociObjectMgmt.TryGetValue(unselectedCtrlInfo.GetHashCode(), out var windData))
                {
                    windData.wind_status = Status.STOP;
                    _self._ociObjectMgmt.Remove(unselectedCtrlInfo.GetHashCode());
                }

                foreach (ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
                {
                    if (ctrlInfo == unselectedCtrlInfo)
                    {
                        _self._selectedOCIs.Remove(unselectedCtrlInfo);
                        break;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeleteNode), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeleteNode_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo unselectedCtrlInfo = Studio.Studio.GetCtrlInfo(_node);

                if (unselectedCtrlInfo == null)
                    return true;

                _self.MgmtInit();
                return true;
            }
        }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                List<ObjectCtrlInfo> objCtrlInfos = new List<ObjectCtrlInfo>();
                objCtrlInfos.Add(__instance);

                Logic.TryAllocateObject(objCtrlInfos);
            }
        }

        // 개별 옷 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        private static class ChaControl_ChangeClothes_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;

                if (ociChar != null)
                {
                    List<ObjectCtrlInfo> objCtrlInfos = new List<ObjectCtrlInfo>();
                    objCtrlInfos.Add(ociChar);

                    Logic.TryAllocateObject(objCtrlInfos);
                }
            }
        }

        // 코디네이션 변경
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;

                if (ociChar != null)
                {
                    List<ObjectCtrlInfo> objCtrlInfos = new List<ObjectCtrlInfo>();
                    objCtrlInfos.Add(ociChar);

                    Logic.TryAllocateObject(objCtrlInfos);
                }
            }
        }

        // 악세러리 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeAccessory", typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool))]
        private static class ChaControl_ChangeAccessory_Patches
        {
            private static void Postfix(ChaControl __instance, int slotNo, int type, int id, string parentKey, bool forceChange)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;

                if (ociChar != null)
                {
                    List<ObjectCtrlInfo> objCtrlInfos = new List<ObjectCtrlInfo>();
                    objCtrlInfos.Add(ociChar);

                    Logic.TryAllocateObject(objCtrlInfos);
                }
            }
        }

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                // UnityEngine.Debug.Log($">> InitScene");
                _self.MgmtInit();
                return true;
            }
        }

        #endregion
    }       
}
