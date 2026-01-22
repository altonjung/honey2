﻿using Studio;
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

using UILib.EventHandlers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using KK_PregnancyPlus;
using System.Threading.Tasks;
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
using IllusionUtility.GetUtility;
using ADV.Commands.Object;
#endif
using RootMotion.FinalIK;

#if AISHOUJO || HONEYSELECT2
using AIChara;
using static Illusion.Utils;
using System.Runtime.Remoting.Messaging;
#endif


namespace RealHumanSupport
{

#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
    [BepInProcess("HoneySelect2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class RealHumanSupport : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "RealHumanSupport";
        public const string Version = "0.9.0.5";
        public const string GUID = "com.alton.illusionplugins.RealHuman";
        internal const string _ownerId = "Alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "RealHuman_support";
#endif
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types

        enum WinkState { Idle, Playing }
        enum KissState { Idle, Playing }

        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static RealHumanSupport _self;

        private static string _assemblyLocation;
        internal bool _loaded = false;

        internal ObjectCtrlInfo _selectedOCI;

        private AssetBundle _bundle;


        private WinkState _winkState = WinkState.Idle;
        private KissState _kissState = KissState.Idle;

        float _winkTime = 0f;
        float _kissTime = 0f;
        float _mouthPhase = 0f;
        float _tearPhase = 0f;

        internal Texture2D _faceExpressionFemaleBumpMap2;

        internal Texture2D _faceExpressionMaleBumpMap2;

        internal Texture2D _bodyStrongFemale_A_BumpMap2;

        internal Texture2D _bodyStrongFemale_B_BumpMap2;

        internal Texture2D _bodyStrongMale_A_BumpMap2;

        internal Texture2D _bodyStrongMale_B_BumpMap2;

        internal ComputeShader _mergeComputeShader;

        internal int _mouth_type;
        internal int _eye_type;

        internal RenderTexture _head_rt;

        internal ComputeBuffer _head_areaBuffer;

        internal RenderTexture _body_rt;
        internal ComputeBuffer _body_areaBuffer;

        internal bool  extraColliderDebugObjAdded;

        internal float _expressionTimer = 0f;
        internal const float _ExpressionInterval = 0.1f;
        internal float _winkValue = 0f;
        internal float _kissValue = 0f;
        internal bool  _kissActive = false;

        internal SkinnedMeshRenderer _tang_render = null;



        internal Dictionary<int, RealHumanData> _ociCharMgmt = new Dictionary<int, RealHumanData>();
       
        internal Coroutine _CheckRotationRoutine;

        // Config


        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigWinkShortcut { get; private set; }

        internal static ConfigEntry<KeyboardShortcut> ConfigKissShortcut { get; private set; }

        internal static ConfigEntry<bool> EyeShakeActive { get; private set; }

        internal static ConfigEntry<bool> ExBoneColliderActive { get; private set; }

        internal static ConfigEntry<bool> BreathActive { get; private set; }

        internal static ConfigEntry<bool> TearDropActive { get; private set; }

        internal static ConfigEntry<bool> FaceBlendingActive { get; private set; }

        internal static ConfigEntry<bool> BodyBlendingActive { get; private set; }

        internal static ConfigEntry<float> BreathStrong { get; private set; }

        internal static ConfigEntry<float> BreathInterval { get; private set; }

        internal static ConfigEntry<float> ExtraColliderScale{ get; private set; }

        internal static ConfigEntry<bool> ExtraColliderDebug{ get; private set; }        
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            ConfigWinkShortcut = Config.Bind("Studio/InGame", "Toggle Wink", new KeyboardShortcut(KeyCode.W,  KeyCode.LeftControl));

            ConfigKissShortcut = Config.Bind("Studio/InGame", "Toggle Kiss", new KeyboardShortcut(KeyCode.K,  KeyCode.LeftControl));

            EyeShakeActive = Config.Bind("Studio/InGame", "Eye Shaking", true, new ConfigDescription("Enable/Disable"));

            ExBoneColliderActive = Config.Bind("Studio/InGame", "Extra Collider", true, new ConfigDescription("Enable/Disable"));

            BreathActive = Config.Bind("Studio/InGame", "Bumping Belly", true, new ConfigDescription("Enable/Disable"));
            
            TearDropActive = Config.Bind("Studio/InGame", "Tear Drop", true, new ConfigDescription("Enable/Disable"));
    
            FaceBlendingActive = Config.Bind("Studio", "Strong Face", true, new ConfigDescription("Enable/Disable"));

            BodyBlendingActive = Config.Bind("Studio", "Blending Body", true, new ConfigDescription("Enable/Disable"));

            BreathInterval = Config.Bind("Breath", "Cycle", 1.5f, new ConfigDescription("Breath Interval", new AcceptableValueRange<float>(1.0f,  5.0f)));;

            BreathStrong = Config.Bind("Breath", "Strong", 0.6f, new ConfigDescription("Breath Amplitude", new AcceptableValueRange<float>(0.1f, 1.0f)));

            ExtraColliderScale = Config.Bind("ExtraCollider", "Scale", 1.0f, new ConfigDescription("Extra collider Scale", new AcceptableValueRange<float>(0.1f, 10.0f)));

            ExtraColliderDebug = Config.Bind("ExtraCollider", "Show", false, new ConfigDescription("Debug Enable/Disable"));
  
            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            // UnityEngine.Debug.Log($">> start CheckRotationRoutine");

            _CheckRotationRoutine = StartCoroutine(CheckRotationRoutine());
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
        }
        // protected override void LateUpdate()
        // {
        //     ApplyBlendshape();
        // }
        #endregion

        #region Private Methods
        private void Init()
        {
            _loaded = true;
            
            // 배포용 번들 파일 경로
            string bundlePath = Application.dataPath + "/../abdata/realgirl/realgirlbundle.unity3d";

            _bundle = AssetBundle.LoadFromFile(bundlePath);
            if (_bundle == null)
            {                    
                Logger.LogMessage($"Please Install realgirl.zipmod!");
                return;
            }

            _bodyStrongFemale_A_BumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_F_BumpMap2");
            _bodyStrongFemale_B_BumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_FB_BumpMap2");
            _bodyStrongMale_A_BumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_M_BumpMap2");
            _bodyStrongMale_B_BumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_MB_BumpMap2");

#if FEATURE_FACEBUMP_SUPPORT               
            _faceExpressionFemaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Face_Expression_F_BumpMap2");
            _faceExpressionMaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Face_Expression_M_BumpMap2");
#endif            

            _mergeComputeShader = _bundle.LoadAsset<ComputeShader>("MergeTextures.compute");

#if FEATURE_FACEBUMP_SUPPORT            
            _self._head_areaBuffer = new ComputeBuffer(16, sizeof(float) * 6);            
#endif
            _self._body_areaBuffer = new ComputeBuffer(24, sizeof(float) * 6); 
        }

        private void SceneInit()
        {
            // UnityEngine.Debug.Log($">> SceneInit()");
            if (StudioAPI.InsideStudio)
            {
                foreach (var kvp in _ociCharMgmt)
                {
                    var key = kvp.Key;
                    RealHumanData value = kvp.Value;
                    value.c_m_eye.Clear();
                    if (value != null && value.charControl != null && value.coroutine != null)
                        value.charControl.StopCoroutine(value.coroutine);
                }

                _mouth_type = 0;
                _eye_type = 0;

                _ociCharMgmt.Clear();                
            }
        }

        IEnumerator CheckRotationRoutine()
        {
            while (true) // 무한 반복
            {
                OCIChar ociChar = _selectedOCI as OCIChar;
                if (ociChar != null)
                {
                    if (!ExtraColliderDebug.Value && extraColliderDebugObjAdded)
                    {   
                        if (ociChar.GetChaControl()) {
                            Logic.DeleteExtraDynamicBoneCollider(ociChar.GetChaControl().objAnim);
                        }
                    }

                    if (!Input.GetMouseButton(0)) {
                        if (_ociCharMgmt.TryGetValue(ociChar.GetChaControl().GetHashCode(), out var realHumanData))
                        {
                            if (realHumanData.m_skin_body == null || realHumanData.m_skin_head == null)
                                realHumanData = Logic.GetMaterials(ociChar.GetChaControl(), realHumanData);

                            if (
                                //(Logic.GetBoneRotationFromIK(realHumanData.lk_left_foot_bone)._q != realHumanData.prev_lk_left_foot_rot) ||
                                //(Logic.GetBoneRotationFromIK(realHumanData.lk_right_foot_bone)._q != realHumanData.prev_lk_right_foot_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_left_foot_bone)._q != realHumanData.prev_fk_left_foot_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_right_foot_bone)._q != realHumanData.prev_fk_right_foot_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_left_knee_bone)._q != realHumanData.prev_fk_left_knee_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_right_knee_bone)._q != realHumanData.prev_fk_right_knee_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_left_thigh_bone)._q != realHumanData.prev_fk_left_thigh_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_right_thigh_bone)._q != realHumanData.prev_fk_right_thigh_rot) ||                          
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_spine01_bone)._q != realHumanData.prev_fk_spine01_rot) || 
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_spine02_bone)._q != realHumanData.prev_fk_spine02_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_head_bone)._q != realHumanData.prev_fk_head_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_left_shoulder_bone)._q != realHumanData.prev_fk_left_shoulder_rot) ||
                                (Logic.GetBoneRotationFromFK(realHumanData.fk_right_shoulder_bone)._q != realHumanData.prev_fk_right_shoulder_rot)
                            )
                            {
                                Logic.SupportBodyBumpEffect(ociChar.charInfo, realHumanData);
                            }                          
                        }                        
                    }                  
                }

                yield return new WaitForSeconds(0.5f); // 0.5초 대기
            }
        }

        private IEnumerator RoutineForStudio(RealHumanData realHumanData)
        {
            float tearValue = 0;
            float refValue = 0;
            bool tearIncreasing = true;
            bool refIncreasing = true;

            PregnancyPlusCharaController pregnantChaController = realHumanData.charControl.GetComponent<KK_PregnancyPlus.PregnancyPlusCharaController>();
            float initBellySize = pregnantChaController.infConfig.inflationSize;
            
            while (true)
            {
                if (_loaded == true)
                {
                    float time = Time.time;
                    if (EyeShakeActive.Value)
                    {
                        foreach (Material mat in realHumanData.c_m_eye)
                        {
                            if (mat == null)
                                continue;
                            // sin 파형 (0 ~ 1로 정규화)
                            float easedBump = (Mathf.Sin(time * Mathf.PI * 3.5f * 2f) + 1f) * 0.5f;

                            float eyeScale = Mathf.Lerp(0.18f, 0.21f, easedBump);
                            mat.SetFloat("_Texture4Rotator", eyeScale);

                            eyeScale = Mathf.Lerp(0.1f, 0.2f, easedBump);
                            mat.SetFloat("_Parallax", eyeScale);
                        }
                    }
                    
                    if (BreathActive.Value)
                    {
                        if (pregnantChaController != null)
                        {
                            float sinValue = (Mathf.Sin(time * BreathInterval.Value) + 1f) * 0.5f;
                        
                            pregnantChaController.infConfig.SetSliders(BellyTemplate.GetTemplate(1));
                            pregnantChaController.infConfig.inflationSize = initBellySize + (1f - sinValue) * 10f * BreathStrong.Value;
                            pregnantChaController.MeshInflate(new MeshInflateFlags(pregnantChaController), "StudioSlider");
                        }
                    }

                    if (TearDropActive.Value)
                    {
                        float deltaTear = Time.deltaTime / 10f; // ← 여기만 수정 (10초)
  
                        if (tearIncreasing)
                        {
                            tearValue += deltaTear;
                            if (tearValue >= 1f)
                            {
                                tearValue = 1f;
                                tearIncreasing = false;
                            }
                        }
                        else
                        {
                            tearValue -= deltaTear;
                            if (tearValue <= 0.3f)
                            {
                                tearValue = 0.3f;
                                tearIncreasing = true;
                            }
                        }
                         float tearSin = Mathf.Sin(tearValue * Mathf.PI);

                        //  ---------------- 눈물 생성 ----------------
                        if (realHumanData.m_tear_eye != null) {
                            realHumanData.m_tear_eye.SetFloat("_NamidaScale", tearSin);
                            realHumanData.m_tear_eye.SetFloat("_RefractionScale", tearSin); 
                        }

                    } else
                    {
                        if (realHumanData.m_tear_eye != null) {
                            realHumanData.m_tear_eye.SetFloat("_NamidaScale", 0f);
                            realHumanData.m_tear_eye.SetFloat("_RefractionScale", 0f);
                        }
                    }

                    yield return null;
                }
                else
                {
                    yield return new WaitForSeconds(1);
                }
            }
        }    

        private IEnumerator RoutineForInGame(RealHumanData realHumanData)
        {

            float tearValue = 0;
            float refValue = 0;
            bool tearIncreasing = true;
            bool refIncreasing = true;

            PregnancyPlusCharaController pregnantChaController = realHumanData.charControl.GetComponent<KK_PregnancyPlus.PregnancyPlusCharaController>();
            float initBellySize = pregnantChaController.infConfig.inflationSize;

            while (true)
            {
                if (_loaded == true)
                {
                    float time = Time.time;

                    // eye shaking
                    {
                        foreach (Material mat in realHumanData.c_m_eye)
                        {
                            // sin 파형 (0 ~ 1로 정규화)
                            float easedBump = (Mathf.Sin(time * Mathf.PI * 3.5f * 2f) + 1f) * 0.5f;

                            float eyeScale = Mathf.Lerp(0.18f, 0.21f, easedBump);
                            mat.SetFloat("_Texture4Rotator", eyeScale);

                            eyeScale = Mathf.Lerp(0.1f, 0.2f, easedBump);
                            mat.SetFloat("_Parallax", eyeScale);
                        }
                    }

                    // belly bumping
                    if (BreathActive.Value)
                    {
                        if (pregnantChaController != null)
                        {
                            float sinValue = (Mathf.Sin(time * BreathInterval.Value) + 1f) * 0.5f;
                        
                            pregnantChaController.infConfig.SetSliders(BellyTemplate.GetTemplate(1));
                            pregnantChaController.infConfig.inflationSize = initBellySize + (1f - sinValue) * 10f * BreathStrong.Value;
                            pregnantChaController.MeshInflate(new MeshInflateFlags(pregnantChaController), "StudioSlider");
                        }
                    }

                    // tear drop
                    if (realHumanData.shouldTearing)
                    {
                       float deltaTear = Time.deltaTime / 10f; // ← 여기만 수정 (10초)
                        float deltaRef = Time.deltaTime / 10f; // ← 여기만 수정 (10초)

                        if (tearIncreasing)
                        {
                            tearValue += deltaTear;
                            if (tearValue >= 1f)
                            {
                                tearValue = 1f;
                                tearIncreasing = false;
                            }
                        }
                        else
                        {
                            tearValue -= deltaTear;
                            if (tearValue <= 0f)
                            {
                                tearValue = 0f;
                                tearIncreasing = true;
                            }
                        }
                        float tearSin = Mathf.Sin(tearValue * Mathf.PI);

                        if (refIncreasing)
                        {
                            refValue += deltaRef;
                            if (refValue >= 1f)
                            {
                                refValue = 1f;
                                refIncreasing = false;
                            }
                        }
                        else
                        {
                            refValue -= deltaRef;
                            if (refValue <= 0f)
                            {
                                refValue = 0f;
                                refIncreasing = true;
                            }
                        }

                        float refSin = Mathf.Sin(deltaRef * Mathf.PI);

                        //  ---------------- 눈물 생성 ----------------
                        realHumanData.m_tear_eye.SetFloat("_NamidaScale", tearSin);
                        realHumanData.m_tear_eye.SetFloat("_RefractionScale", refSin);                        

                        // ---------------- 눈 흔들림 ----------------

                        float amplitude = 0.001f; // HS2 기준, 필요하면 조절
                        float easedBump = (Mathf.Sin(time * Mathf.PI * 4.5f * 2f) + 1f) * 0.5f;
                        float yOffset = (easedBump - 0.5f) * 1.5f * amplitude; // -amp ~ +amp
                    }

                    yield return null;
                }
                else
                {
                    yield return new WaitForSeconds(1);
                }
            }
        }    

        private IEnumerator ExecuteAfterFrame(ChaControl chaControl, RealHumanData realHumanData)
        {
            int frameCount = 30;
            for (int i = 0; i < frameCount; i++)
                yield return null;
            
            Logic.SupportExtraDynamicBones(chaControl, realHumanData);
            Logic.SupportEyeFastBlinkEffect(chaControl, realHumanData);
            Logic.SupportBodyBumpEffect(chaControl, realHumanData);
#if FEATURE_FACEBUMP_SUPPORT            
            Logic.SupportFaceBumpEffect(chaControl, realHumanData);
#endif            
        }        

        #endregion

        #region Public Methods
        #endregion

        #region Patches



        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);   
                if (objectCtrlInfo == null)
                    return true;
                    
                OCIChar ociChar = objectCtrlInfo as OCIChar;

                if (ociChar != null)
                {
                    _self._selectedOCI = ociChar;

                    if (_self._ociCharMgmt.TryGetValue(ociChar.GetChaControl().GetHashCode(), out var realHumanData))
                    {                    
                        if (realHumanData.coroutine != null)
                        {
                            ociChar.GetChaControl().StopCoroutine(realHumanData.coroutine);
                            realHumanData.coroutine = ociChar.charInfo.StartCoroutine(_self.RoutineForStudio(realHumanData));
                            ociChar.GetChaControl().StartCoroutine(_self.ExecuteAfterFrame(ociChar.GetChaControl(), realHumanData));
                        }
                    }
                    else
                    {
                        RealHumanData realHumanData2 = new RealHumanData();
                        realHumanData2 = Logic.InitRealHumanData(ociChar.GetChaControl(), realHumanData2);

                        if (realHumanData2 != null)
                        {
                            realHumanData2.coroutine = ociChar.charInfo.StartCoroutine(_self.RoutineForStudio(realHumanData2));                    
                            _self._ociCharMgmt.Add(ociChar.GetChaControl().GetHashCode(), realHumanData2);
                            ociChar.GetChaControl().StartCoroutine(_self.ExecuteAfterFrame(ociChar.GetChaControl(), realHumanData2));
                        } else
                        {
                            Logger.LogMessage($"Body skin not has bumpmap2");
                        }                    
                    }    
                }

                return true;
            }
        }
    
        // [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        // internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        // {
        //     private static bool Prefix(object __instance, TreeNodeObject _node)
        //     {
        //         _self._selectedOCI = null;
        //         return true;
        //     }
        // }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeleteNode), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeleteNode_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {        
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);   
                _self._selectedOCI = null;

                if (objectCtrlInfo == null)
                    return true;
                
                OCIChar ociChar = objectCtrlInfo as OCIChar;

                if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar.GetChaControl().GetHashCode(), out var realHumanData))
                {
                    if (realHumanData.coroutine != null)
                    {
                        ociChar.GetChaControl().StopCoroutine(realHumanData.coroutine);
                    }
                    realHumanData.c_m_eye.Clear();

                    _self._ociCharMgmt.Remove(ociChar.GetChaControl().GetHashCode());
                }
                    
                return true;
            }
        }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                ChaControl chaControl = __instance.GetChaControl();

                if (chaControl != null)
                {                    
                    if (_self._ociCharMgmt.TryGetValue(__instance.GetChaControl().GetHashCode(), out var realHumanData))
                    {
                        _self._ociCharMgmt.Remove(__instance.GetChaControl().GetHashCode());
                        if (realHumanData.coroutine != null)
                        {
                            __instance.charInfo.StopCoroutine(realHumanData.coroutine);
                        }
                        realHumanData.c_m_eye.Clear();

                        RealHumanData realHumanData2 = new RealHumanData();
                        realHumanData2 = Logic.InitRealHumanData(__instance.GetChaControl(), realHumanData2);

                        if (realHumanData2 != null)
                        {
                            realHumanData2.coroutine = __instance.charInfo.StartCoroutine(_self.RoutineForStudio(realHumanData2));     
                            _self._ociCharMgmt.Add(__instance.GetChaControl().GetHashCode(), realHumanData2);
                            __instance.charInfo.StartCoroutine(_self.ExecuteAfterFrame(__instance.GetChaControl(), realHumanData2));
                        } else
                        {
                            Logger.LogMessage($"Body skin not has bumpmap2");
                        }                    

                        _self._selectedOCI = __instance;
                    }            
                }
            }
        }

    
        // 눈물 흘릴 시 마다
        [HarmonyPatch(typeof(AIChara.ChaControl), "ChangeTearsRate", typeof(float))]
        private static class ChaControl_ChangeTearsRate_Patches
        {
            private static bool Prefix(AIChara.ChaControl __instance, float value)
            {
                if (_self._ociCharMgmt.TryGetValue(__instance.GetHashCode(), out var realHumanData))
                {
                   realHumanData.shouldTearing = true;
                }
                return true;
            }
        }

//  포즈 변경 시 마다
        [HarmonyPatch(typeof(PauseCtrl.FileInfo), "Apply", typeof(OCIChar))]
        private static class PauseCtrl_Apply_Patches
        {
            private static bool Prefix(PauseCtrl.FileInfo __instance, OCIChar _char)
            {
                if (_char != null)
                {
                    if (_self._ociCharMgmt.TryGetValue(_char.GetChaControl().GetHashCode(), out var realHumanData))
                    {
                        Logic.SetHairDown(_char.GetChaControl(), realHumanData);
                    }
                }
                return true;
            }
        }        

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.SceneInit();
                return true;
            }
        }

        //// 표정 부분 변경
        //[HarmonyPatch(typeof(ChaControl), "ChangeEyesPtn", typeof(int), typeof(bool))]
        //private static class ChaControl_ChangeEyesPtn_Patches
        //{
        //    private static void Postfix(ChaControl __instance, int ptn, bool blend)
        //    {
        //        // UnityEngine.Debug.Log($">> ChangeEyesPtn {ptn}");

        //        OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
        //        if (ociChar != null)
        //        {
        //            if (_self._ociCharMgmt.TryGetValue(ociChar.GetChaControl().GetHashCode(), out var realHumanData))
        //            {
        //                _self._eye_type = ptn;
        //                Logic.SupportFaceBumpEffect(__instance, realHumanData);
        //            }
        //        }
        //    }
        //}     

        //[HarmonyPatch(typeof(ChaControl), "ChangeMouthPtn", typeof(int), typeof(bool))]
        //private static class ChaControl_ChangeMouthPtn_Patches
        //{
        //    private static void Postfix(ChaControl __instance, int ptn, bool blend)
        //    {
        //        // UnityEngine.Debug.Log($">> ChangeMouthPtn {ptn}");
        //        OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
        //        if (ociChar != null)
        //        {
        //            if (_self._ociCharMgmt.TryGetValue(ociChar.GetChaControl().GetHashCode(), out var realHumanData))
        //            {
        //                _self._mouth_type = ptn;
        //                //Logic.SupportFaceBumpEffect(__instance, realHumanData);
        //            }
        //        }
        //    }
        //}

        [HarmonyPatch(typeof(FaceBlendShape), "OnLateUpdate")]
        private class FaceBlendShape_Patches
        {
            [HarmonyAfter("com.joan6694.hsplugins.instrumentation")]
            public static void Postfix(FaceBlendShape __instance)
            {
                if (StudioAPI.InsideStudio)
                {
                    if (!_self._loaded)
                        return;

                    _self._expressionTimer += Time.deltaTime;

                    OCIChar ociChar = _self._selectedOCI as OCIChar;

                    if (ociChar != null)
                    {
                        var cha = ociChar.GetChaControl();

                        if (ConfigWinkShortcut.Value.IsDown() && _self._winkState == WinkState.Idle)
                        {
                            _self._winkState = WinkState.Playing;
                            _self._winkTime = 0f;
                            cha.ChangeMouthPtn(7, true);
                        }
                        if (ConfigKissShortcut.Value.IsDown())
                        {
                            if (_self._kissState == KissState.Idle)
                            {
                                _self._kissState = KissState.Playing;
                                _self._kissTime = 0f;
                                _self._mouthPhase = 0f;
                                SkinnedMeshRenderer[] renders = ociChar.GetChaControl().objHead.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                                foreach (SkinnedMeshRenderer smr in renders)
                                {

                                    if (smr.sharedMesh == null) continue;

                                    for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                                    {
                                        var name = smr.sharedMesh.GetBlendShapeName(i);
                                        if (smr.name.Equals("o_tang"))
                                        {
                                            _self._tang_render = smr;
                                            break;
                                        }
                                    }
                                }                                
                            }
                            else
                            {
                                _self._kissState = KissState.Idle;
                                cha.ChangeMouthPtn(0, true);
                                _self._tang_render = null;
                            }
                        }

                        if (_self._winkState == WinkState.Playing)
                        {
                            _self._winkTime += Time.deltaTime;

                            const float CLOSE_TIME = 0.3f;
                            const float HOLD_TIME  = 1.0f;
                            const float TOTAL_TIME = CLOSE_TIME + HOLD_TIME;

                            float weight;

                            if (_self._winkTime < CLOSE_TIME)
                            {
                                // 천천히 감김
                                float t = Mathf.Clamp01(_self._winkTime / CLOSE_TIME);
                                weight = Mathf.SmoothStep(0f, 100f, t);
                            }
                            else
                            {
                                // 완전히 감긴 상태 유지
                                weight = 100f;
                            }

                            foreach (var fbsTarget in __instance.EyesCtrl.FBSTarget)
                            {
                                fbsTarget.GetSkinnedMeshRenderer()
                                    .SetBlendShapeWeight(10, weight);
                            }

                            if (_self._winkTime >= TOTAL_TIME)
                            {
                                // 즉시 눈 뜸
                                foreach (var fbsTarget in __instance.EyesCtrl.FBSTarget)
                                {
                                    fbsTarget.GetSkinnedMeshRenderer()
                                        .SetBlendShapeWeight(10, 0f);
                                }

                                _self._winkState = WinkState.Idle;
                                _self._winkTime = 0f;

                                cha.ChangeMouthPtn(1, true);
                            }
                        }                      

                       if (_self._kissState == KissState.Playing)
                        {
                            _self._kissTime += Time.deltaTime;
                            _self._mouthPhase += Time.deltaTime * 0.55f;

                            float loop = Mathf.Repeat(_self._kissTime, 20f);

                            int mouthType;
                            int tongueType = 4;
                            float eyeClose;
                            float mouthStrong = 50f;

                            if (loop < 5f)
                            {
                                mouthType = 12;
                                eyeClose = 50f;
                                if (_self._ociCharMgmt.TryGetValue(ociChar.GetChaControl().GetHashCode(), out var realHumanData))
                                {
                                    realHumanData.mouth_down.localPosition = new Vector3(0.0f, 0.0f, 0.03f);
                                }                                    
                            }
                            else if (loop < 10f)
                            {
                                mouthType = 15;
                                tongueType = 4;
                                eyeClose = 70f;
                                if (_self._ociCharMgmt.TryGetValue(ociChar.GetChaControl().GetHashCode(), out var realHumanData))
                                {
                                    realHumanData.mouth_down.localPosition = new Vector3(0.0f, 0.0f, 0.01f);
                                }                                 
                            }
                            else if (loop < 15f)
                            {
                                mouthType = 16;
                                tongueType = 7;
                                eyeClose = 80f;
                                if (_self._ociCharMgmt.TryGetValue(ociChar.GetChaControl().GetHashCode(), out var realHumanData))
                                {
                                    realHumanData.mouth_down.localPosition = new Vector3(0.0f, 0.0f, 0.01f);
                                }                                 
                            }
                            else
                            {
                                mouthType = 15;
                                tongueType = 4;
                                eyeClose = 70f;
                            }

                            ociChar.GetChaControl().ChangeMouthPtn(mouthType, true);

                            float m = Mathf.Sin(_self._mouthPhase * Mathf.PI * 2f);
                            float mouthMove = (m * 0.5f + 0.5f) * mouthStrong;

                            if (mouthType != 12)
                            {
                                if (_self._ociCharMgmt.TryGetValue(ociChar.GetChaControl().GetHashCode(), out var realHumanData))
                                {
                                    realHumanData.mouth_down.localPosition = new Vector3(0.0f, -mouthMove * 0.002f, realHumanData.mouth_down.localPosition.z);
                                }                                          
                            }     
// 입 모양 처리
                            foreach (var fbsTarget in __instance.MouthCtrl.FBSTarget)
                            {
                                fbsTarget.GetSkinnedMeshRenderer()
                                    .SetBlendShapeWeight(44, mouthMove * 0.7f);
                                fbsTarget.GetSkinnedMeshRenderer()
                                    .SetBlendShapeWeight(45, mouthMove * 0.2f);
                            }
// 윗눈 반감김 처리
                            foreach (var fbsTarget in __instance.EyesCtrl.FBSTarget)
                            {
                                fbsTarget.GetSkinnedMeshRenderer()
                                    .SetBlendShapeWeight(1, eyeClose);
                            }
// 혀 처리
                            if (_self._tang_render != null)
                            {
                                _self._tang_render.SetBlendShapeWeight(tongueType, mouthMove / 2.5f);
                            }                           
                        }

                        if (_self._kissState == KissState.Idle && TearDropActive.Value)
                        {
                            _self._tearPhase += Time.deltaTime * 5f;
                            float tear = Mathf.PingPong(_self._tearPhase, 1f) * 5f;

                            foreach (var fbsTarget in __instance.EyesCtrl.FBSTarget)
                            {
                                fbsTarget.GetSkinnedMeshRenderer()
                                    .SetBlendShapeWeight(12, tear);
                            }
                        }
                    }
                }
            }
        }      


        // In Game Mode
#if FEATURE_INGAME_SUPPORT
        [HarmonyPatch(typeof(ADV.CharaData), "MotionPlay")]
        private static class CharaData_MotionPlay_Patches
        {
            private static bool Prefix(ADV.CharaData __instance, ADV.Commands.Base.Motion.Data motion, bool isCrossFade)
            {
                // UnityEngine.Debug.Log($">> MotionPlay {__instance.chaCtrl}");

                if (__instance.chaCtrl != null) {
                        
                    if (!_self._ociCharMgmt.TryGetValue(__instance.chaCtrl.GetHashCode(), out var realHumanData))
                    {
                        RealHumanData realHumanData2 = new RealHumanData();
                        realHumanData2 = Logic.InitRealHumanData(__instance.chaCtrl, realHumanData2);

                        if (realHumanData2 != null)
                        {
                            realHumanData2.coroutine = __instance.chaCtrl.StartCoroutine(_self.RoutineForInGame(realHumanData2));
                            _self._ociCharMgmt.Add(__instance.chaCtrl.GetHashCode(), realHumanData2);
                            __instance.chaCtrl.StartCoroutine(_self.ExecuteAfterFrame(__instance.chaCtrl, realHumanData2));                    
                        }

                        // UnityEngine.Debug.Log($">> realHumanData2  {__instance.chaCtrl.GetHashCode()}, {realHumanData2}");                          
                    }
                }

                return true;
            }
        }

        // 캐릭터 구성 시 마다
        [HarmonyPatch(typeof(Manager.HSceneManager), "SetFemaleState", typeof(ChaControl[]))]
        private static class HSceneManager_SetFemaleState_Patches
        {
            private static void Postfix(Manager.HSceneManager __instance, ChaControl[] female)
            {

                foreach (var kvp in _self._ociCharMgmt)
                {
                    var key = kvp.Key;
                    RealHumanData value = kvp.Value;
                    value.c_m_eye.Clear();
                    if (value != null && value.charControl != null && value.coroutine != null)
                        value.charControl.StopCoroutine(value.coroutine);
                }

                _self._ociCharMgmt.Clear(); 

                // player
                if (__instance.player != null) {
                    RealHumanData realHumanData2 = new RealHumanData();
                    realHumanData2 = Logic.InitRealHumanData(__instance.player, realHumanData2);

                    if (realHumanData2 != null)
                    {
                        realHumanData2.coroutine = __instance.player.StartCoroutine(_self.RoutineForInGame(realHumanData2));
                        _self._ociCharMgmt.Add(__instance.player.GetHashCode(), realHumanData2);
                        __instance.player.StartCoroutine(_self.ExecuteAfterFrame(__instance.player, realHumanData2));
                    }
                }

                // heroine
                foreach (ChaControl chaCtrl in female)
                {
                    if (chaCtrl != null) {
                        RealHumanData realHumanData2 = new RealHumanData();
                        realHumanData2 = Logic.InitRealHumanData(chaCtrl, realHumanData2);

                        if (realHumanData2 != null)
                        {
                            realHumanData2.coroutine = chaCtrl.StartCoroutine(_self.RoutineForInGame(realHumanData2));
                            _self._ociCharMgmt.Add(chaCtrl.GetHashCode(), realHumanData2);
                            chaCtrl.StartCoroutine(_self.ExecuteAfterFrame(chaCtrl, realHumanData2));
                        }
                    }
                }
            }
        }    

        [HarmonyPatch(typeof(AIChara.ChaControl), "OnDestroy")]
        private static class ChaControl_OnDestroy_Patches
        {
            private static void Postfix(AIChara.ChaControl __instance)
            {
                if (!StudioAPI.InsideStudio) {

                    List<int> deletedHashCode = new List<int>();
                    foreach (var kvp in _self._ociCharMgmt)
                    {
                        var key = kvp.Key;
                        RealHumanData value = kvp.Value;
                        value.c_m_eye.Clear();
                        if (value != null && value.charControl.GetHashCode() == __instance.GetHashCode())
                        {
                            if (value.charControl != null && value.coroutine != null) {
                                value.charControl.StopCoroutine(value.coroutine);                       
                                deletedHashCode.Add(__instance.GetHashCode());
                            }     
                        }
                    }                    

                    foreach (int hashCode in deletedHashCode)
                    {
                        _self._ociCharMgmt.Remove(hashCode);
                    }
                }
            }
        }
#endif

        #endregion
    }
    #endregion
}