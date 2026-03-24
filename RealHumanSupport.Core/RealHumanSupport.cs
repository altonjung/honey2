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
using KKAPI.Studio;
using KKAPI.Studio.UI.Toolbars;
using KKAPI.Utilities;
using KKAPI.Chara;

/*
    기본로직

    1) 각 캐릭터에 RealGirlSupport 로직 자동 추가
    2) 각 pose 에 따른 얼굴 | 다리 | 몸  dynamic-bumpmap 지원
    3) belly inflation, teadrop 지원
    4) FEATURE_FACE_BLENDSHAPE_SUPPORT (wink) 당분간 off
    5) FEATURE_BODY_BLENDSHAPE_SUPPORT (목적 불분명) 당분간 off

        blend shape 기능 활용

        GP 7.7 혹은 그 이상 (최신 GP 계열 지원)
        >> blendShape GP.Basic Shape Legs Pull BothSide, 20 in body
        >> blendShape GP.Siri open Buttcheeks1, 221 in body
        >> blendShape GP.Siri open Buttcheeks2, 222 in body


    남은작업

    1) DAN bone 활용 지원
    2) FK 모드 및 animation 모드 지원
    3) FEATURE_BODY_BLENDSHAPE_SUPPORT 기능 활용
    4) 팔 영역 dynamic-bumpmap 지원
*/

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
        public const string Name = "RealGirlSupport";
        public const string Version = "0.9.1.2";
        public const string GUID = "com.alton.illusionplugins.RealGirl";
        internal const string _ownerId = "Alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "RealGirl_support";
#endif
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types

        enum WinkState { Idle, Playing }
        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static RealHumanSupport _self;

        private static string _assemblyLocation;
        internal bool _loaded = false;

        // internal ObjectCtrlInfo _selectedOCI;

        private AssetBundle _bundle;

        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;
		
        private const int _uniqueId = ('R' << 24) | ('E' << 16) | ('A' << 8) | 'G';

        private Rect _windowRect = new Rect(140, 10, 350, 10);            

        internal Texture2D _faceExpressionFemaleBumpMap2;

        internal Texture2D _bodyStrongFemaleBumpMap2;

#if FEATURE_TEARDROP_SUPPORT
        internal Texture2D _TearDropImg;
#endif
#if FEATURE_WINK_SUPPORT        
        private WinkState _winkState = WinkState.Idle;
        
        float _winkTime = 0f;
#endif
        internal ComputeShader _mergeComputeShader;

        internal Coroutine _CheckRotationRoutine;

        private bool mouseReleased = false;
#if FEATURE_WINK_SUPPORT 
        private bool winkReleased = false;
#endif
        private Coroutine _CheckMgmtCoroutine;    

        private float _prevTFScale = 1.0f;

        private GUIStyle _richLabel;

        private GUIStyle RichLabel
        {
            get
            {
                if (_richLabel == null)
                {
                    _richLabel = new GUIStyle(GUI.skin.label);
                    _richLabel.richText = true;
                }
                return _richLabel;
            }
        }

        private OCIChar _currentOCIChar = null;
        // Config
        #region Accessors
#if FEATURE_WINK_SUPPORT         
        internal static ConfigEntry<KeyboardShortcut> ConfigWinkShortcut { get; private set; }
#endif
        internal static ConfigEntry<bool> HairDownActive { get; private set; }

        internal static ConfigEntry<bool> TearDropActive { get; private set; }

        internal static ConfigEntry<bool> EyeShakeActive { get; private set; }

        internal static ConfigEntry<bool> BreathActive { get; private set; }

        internal static ConfigEntry<bool> BodyBlendingActive { get; private set; }

        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            string support_type = "Studio";

            HairDownActive = Config.Bind(support_type, "Hair Down", true, new ConfigDescription("Enable/Disable"));

            EyeShakeActive = Config.Bind(support_type, "Eye Shaking", true, new ConfigDescription("Enable/Disable"));

            BreathActive = Config.Bind(support_type, "Bumping Belly", true, new ConfigDescription("Enable/Disable"));
            
            TearDropActive = Config.Bind(support_type, "Tear Drop", true, new ConfigDescription("Enable/Disable"));            

            BodyBlendingActive = Config.Bind(support_type, "Dynamic Bump", true, new ConfigDescription("Enable/Disable"));

            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open RealGirl window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(RealHumanSupport).Assembly).LoadTexture(),
                false, this, val => _ShowUI = val);
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);        

            CharacterApi.RegisterExtraBehaviour<RealHumanSupportController>(GUID);

            _CheckRotationRoutine = StartCoroutine(CheckRotationRoutine());      

            Logger.LogMessage($"{Name} {Version}.. by unbreakable dreamer");      
        }

#if SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }
#endif

        private void InitConfig()
        {
            var controller = _currentOCIChar.GetChaControl().GetComponent<RealHumanSupportController>();
            if (controller)
            {
                controller.ResetRealHumanData();
            }               
        }

        private RealHumanData GetCurrentData()
        {
            if (_currentOCIChar == null)
                return null;

            var controller = _currentOCIChar.GetChaControl().GetComponent<RealHumanSupportController>();
            if (controller == null)
                return null;

            RealHumanData data = controller.GetData();

            return data;
        }        
        private RealHumanSupportController GetCurrentControl()
        {
            if (_currentOCIChar == null)
                return null;

            return _currentOCIChar.GetChaControl().GetComponent<RealHumanSupportController>();            
        }    


        protected override void Update()
        {
            if (_loaded == false)
                return;

            if (Input.GetMouseButtonUp(0))
            {
                mouseReleased = true;                
            }

#if FEATURE_WINK_SUPPORT 
            if (ConfigWinkShortcut.Value.IsDown())
            {
                if(winkReleased == false)
                    winkReleased = true;                
            }        
#endif            
        }

        // protected override void LateUpdate()
        // {
        //     if (_loaded == false)
        //         return;

        //     if (AnimActive.Value == false)
        //         return;

        //     if (Time.unscaledTime < _nextLateSampleTime)
        //         return;

        //     _nextLateSampleTime = Time.unscaledTime + 0.1f; // 0.1 mean 10fps

        //     if (Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes == null ||
        //         Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() == 0)
        //         return;

        //     TreeNodeObject _node = Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Last();
        //     if (_node == null)
        //         return;

        //     ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);
        //     OCIChar ociChar = objectCtrlInfo as OCIChar;
        //     if (ociChar == null)
        //         return;

        //     ChaControl chaControl = ociChar.GetChaControl();
        //     var controller = chaControl.GetComponent<RealHumanSupportController>();
        //     if (controller == null)
        //         return;

        //     RealHumanData realHumanData = controller.GetRealData();
        //     if (realHumanData == null || realHumanData.fk_head_bone == null)
        //         return;

        //     if (realHumanData.m_skin_body == null || realHumanData.m_skin_head == null)
        //         realHumanData = RealHumanSupportController.GetMaterials(ociChar.GetChaControl(), realHumanData);

        //     const float ROT_EPS_ANIM = 0.2f;

        //     if (
        //         RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_head_bone)._q, realHumanData.prev_fk_head_rot, ROT_EPS_ANIM) ||
        //         RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_left_foot_bone)._q, realHumanData.prev_fk_left_foot_rot, ROT_EPS_ANIM) ||
        //         RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_right_foot_bone)._q, realHumanData.prev_fk_right_foot_rot, ROT_EPS_ANIM) ||
        //         RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_left_knee_bone)._q, realHumanData.prev_fk_left_knee_rot, ROT_EPS_ANIM) ||
        //         RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_right_knee_bone)._q, realHumanData.prev_fk_right_knee_rot, ROT_EPS_ANIM) ||
        //         RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_left_thigh_bone)._q, realHumanData.prev_fk_left_thigh_rot, ROT_EPS_ANIM) ||
        //         RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_right_thigh_bone)._q, realHumanData.prev_fk_right_thigh_rot, ROT_EPS_ANIM) ||
        //         RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_spine01_bone)._q, realHumanData.prev_fk_spine01_rot, ROT_EPS_ANIM) ||
        //         RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_spine02_bone)._q, realHumanData.prev_fk_spine02_rot, ROT_EPS_ANIM) ||
        //         RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_left_shoulder_bone)._q, realHumanData.prev_fk_left_shoulder_rot, ROT_EPS_ANIM) ||
        //         RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_right_shoulder_bone)._q, realHumanData.prev_fk_right_shoulder_rot, ROT_EPS_ANIM)
        //     )
        //     {
        //         RealHumanSupportController.SupportBodyBumpEffect(ociChar.charInfo, realHumanData);
        //     }
        // }

       protected override void OnGUI()
        {
            if (_ShowUI == false)
                return;

            if (StudioAPI.InsideStudio)
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "RealHuman " + Version);
        }

       private void WindowFunc(int id)
        {
            var studio = Studio.Studio.Instance;
    		// 항상 기본값 복구
    		studio.cameraCtrl.noCtrlCondition = null;
	
            bool guiUsingMouse = GUIUtility.hotControl != 0;
            bool mouseInWindow = _windowRect.Contains(Event.current.mousePosition);

            if (guiUsingMouse || mouseInWindow)
            {
                studio.cameraCtrl.noCtrlCondition = () => true;
            }
            else
            {
                studio.cameraCtrl.noCtrlCondition = null;
            }

            RealHumanData data = GetCurrentData();
            if (data != null)
            {
                // ================= UI =================
    ///////////////////
                GUILayout.Label("<color=orange>Breath</color>", RichLabel);

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Strong", "Strong"), GUILayout.Width(60));
                data.BreathStrong = GUILayout.HorizontalSlider(data.BreathStrong, 0.1f, 1.0f);
                GUILayout.Label(data.BreathStrong.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Interval", "Interval"), GUILayout.Width(60));
                data.BreathInterval = GUILayout.HorizontalSlider(data.BreathInterval, 1.0f, 5.0f);
                GUILayout.Label(data.BreathInterval.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();

    ///////////////////
                GUILayout.Label("<color=orange>Tear</color>", RichLabel);

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Strong", "Strong"), GUILayout.Width(60));
                data.TearDropLevel = GUILayout.HorizontalSlider(data.TearDropLevel, 0.1f, 1.0f);
                GUILayout.Label(data.TearDropLevel.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal(); 

    ///////////////////            
    #if FEATURE_EXTRA_COLLIDER_SCALE
                GUILayout.Label("<color=red>Extra Collider</color>", RichLabel);

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Scale", "be careful"), GUILayout.Width(60));
                data.ExtraColliderScale = GUILayout.HorizontalSlider(data.ExtraColliderScale, 0.1f, 10.0f);
                GUILayout.Label(data.ExtraColliderScale.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal(); 
    #endif
                if (TearDropActive.Value) {
                    if (GUILayout.Button("Tear(D)"))
                    {
                        TearDropActive.Value = false;
                    }
                }
                else {
                    if (GUILayout.Button("Tear(A)"))
                    {
                        TearDropActive.Value = true;
                    }
                }

                if (GUILayout.Button("Force Hairdown"))
                {
                    RealHumanSupportController control = GetCurrentControl();
                    if (control != null)
                        control.SetHairDown(true);
                }

                if (GUILayout.Button("Default")) {
                    InitConfig();
                }
            }
            else
            {
                GUILayout.Label("<color=white>Nothing to select</color>", RichLabel);                
            }
//
            if (GUILayout.Button("Close")) {
                Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
				_ShowUI = false;
			}
            // ⭐ 툴팁 직접 그리기
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 150, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }
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

            _bodyStrongFemaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_F_BumpMap2");            
#if FEATURE_TEARDROP_SUPPORT
            _TearDropImg = _bundle.LoadAsset<Texture2D>("teardrop");
#endif            
            _faceExpressionFemaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Face_Expression_F_BumpMap2");

            _mergeComputeShader = _bundle.LoadAsset<ComputeShader>("MergeTextures.compute");
        }

        private void SceneInit()
        {
            Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
			_ShowUI = false;            
        }

        private static bool RotChanged(Quaternion current, Quaternion prev, float epsilonDeg)
        {
            return Quaternion.Angle(current, prev) > epsilonDeg;
        }

        IEnumerator CheckRotationRoutine()
        {
            bool isReleased = false;

            while (true) // 무한 반복
            {   
                if (!_loaded)
                    yield return new WaitForSeconds(0.5f); // 0.5초 대기

                if (Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes != null && Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() > 0)
                {
                    TreeNodeObject _node = Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Last();
                    
                    if (_node != null)
                    {
                        ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);
                        OCIChar ociChar = objectCtrlInfo as OCIChar;

                        if (ociChar != null && ociChar.oiCharInfo.enableFK) // FK 활성화 되었을때 동작
                        {
                            ChaControl chaControl = ociChar.GetChaControl();
                            var controller = chaControl.GetComponent<RealHumanSupportController>();
                            if (controller != null)
                            {
#if FEATURE_EXTRA_COLLIDER_SCALE                                
                                if (_prevTFScale != ExtraColliderScale.Value)
                                {
                                    _prevTFScale = ExtraColliderScale.Value;
                                    controller.ApplyScaleToExtraDynamicBoneColliders(chaControl.objBodyBone.transform, ExtraColliderScale.Value);                                                                                            
                                }
#endif                                                                
                                if (mouseReleased)
                                {
                                    mouseReleased = false;  // 한 번만 쓰고 초기화
                                    RealHumanData realHumanData = controller.GetData();

                                    if (realHumanData != null)
                                    {   
                                        if (realHumanData.fk_head_bone == null)
                                            continue;

                                        if (realHumanData.m_skin_body == null || realHumanData.m_skin_head == null)
                                            realHumanData = RealHumanSupportController.GetMaterials(ociChar.GetChaControl(), realHumanData);

                                        const float ROT_EPS = 0.1f;

                                        if (
                                            RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_head_bone)._q, realHumanData.prev_fk_head_rot, ROT_EPS) ||
                                            RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_left_foot_bone)._q, realHumanData.prev_fk_left_foot_rot, ROT_EPS) ||
                                            RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_right_foot_bone)._q, realHumanData.prev_fk_right_foot_rot, ROT_EPS) ||
                                            RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_left_knee_bone)._q, realHumanData.prev_fk_left_knee_rot, ROT_EPS) ||
                                            RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_right_knee_bone)._q, realHumanData.prev_fk_right_knee_rot, ROT_EPS) ||
                                            RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_left_thigh_bone)._q, realHumanData.prev_fk_left_thigh_rot, ROT_EPS) ||
                                            RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_right_thigh_bone)._q, realHumanData.prev_fk_right_thigh_rot, ROT_EPS) ||
                                            RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_spine01_bone)._q, realHumanData.prev_fk_spine01_rot, ROT_EPS) ||
                                            RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_spine02_bone)._q, realHumanData.prev_fk_spine02_rot, ROT_EPS) ||
                                            RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_left_shoulder_bone)._q, realHumanData.prev_fk_left_shoulder_rot, ROT_EPS) ||
                                            RotChanged(RealHumanSupportController.GetBoneRotationFromFK(realHumanData.fk_right_shoulder_bone)._q, realHumanData.prev_fk_right_shoulder_rot, ROT_EPS)
                                        )
                                        {
                                            RealHumanSupportController.SupportBodyBumpEffect(ociChar.charInfo, realHumanData);
                                        }
                                    }
                                }
                            } 
                        }                        
                    }
                }
                yield return new WaitForSeconds(0.5f); // 0.5초 대기
            }
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
                    _self._currentOCIChar = ociChar;
                    ChaControl chaControl = ociChar.GetChaControl();

                    var controller = chaControl.GetComponent<RealHumanSupportController>();
                    if (controller != null)
                    {
                        if (controller.GetData() == null)
                            controller.InitRealHumanData(chaControl);        
                    }
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
                    var controller = chaControl.GetComponent<RealHumanSupportController>();
                    if (controller != null)
                    {                  
                        controller.InitRealHumanData(chaControl);
                    }    
                }
            }
        }

        // 옷이 변경될때 마다, pregnancy 값 조정 필요
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {
                if (__instance != null)
                {
                    var controller = __instance.GetComponent<RealHumanSupportController>();
                    if (controller != null)
                    {
                        controller.SetPregnancyRoundness(0.0001f);
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
                if (__instance != null)
                {
                    var controller = __instance.GetComponent<RealHumanSupportController>();
                    if (controller != null)
                    {
#if FEATURE_TEARDROP_SUPPORT
                        controller.SetTearDropRate(value);
#endif
                     }
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
                    var controller = _char.GetChaControl().GetComponent<RealHumanSupportController>();
                    if (controller != null)
                    {                     
                        controller.SetHairDown();
                    }
                }
                return true;
            }
        }        

#if FEATURE_FACE_BLENDSHAPE_SUPPORT || FEATURE_WINK_SUPPORT
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

                    if (Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes == null || Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() == 0)
                        return;

                    if (_self.winkReleased) {
                        TreeNodeObject _node = Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Last();
                        ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);
                        OCIChar ociChar = objectCtrlInfo as OCIChar;
                        ChaControl chaControl = ociChar.GetChaControl();

                        if (chaControl != null)
                        {
                            var controller = chaControl.GetComponent<RealHumanSupportController>();
                            if (controller != null) {
                               RealHumanData realHumanData = controller.GetRealData();
                               if (realHumanData != null) {
                                    if (_self._winkState == WinkState.Idle)
                                    {
                                        realHumanData.originMouthType = chaControl.GetMouthPtn();
                                        chaControl.ChangeMouthPtn(1, true);   
                                        _self._winkState = WinkState.Playing;
                                        _self._winkTime = 0f;
                                    }

                                    if (_self._winkState == WinkState.Playing)
                                    {
                                        _self._winkTime += Time.deltaTime;

                                        const float CLOSE_TIME = 0.3f;
                                        const float HOLD_TIME  = 1.5f;
                                        const float TOTAL_TIME = CLOSE_TIME + HOLD_TIME;

                                        float weight = 80f;

                                        if (_self._winkTime < CLOSE_TIME)
                                        {
                                            // 천천히 감김
                                            float t = Mathf.Clamp01(_self._winkTime / CLOSE_TIME);
                                            weight = Mathf.SmoothStep(0f, 95f, t);
                                        }

                                        if (_self._winkTime >= TOTAL_TIME)
                                        {
                                            weight = 0f;
                                            _self._winkState = WinkState.Idle;
                                            _self._winkTime = 0f;

                                            chaControl.ChangeMouthPtn(realHumanData.originMouthType, true);
                                            _self.winkReleased = false;                            
                                        }                                    

                                        foreach (var fbsTarget in __instance.EyesCtrl.FBSTarget)
                                        {
                                            SkinnedMeshRenderer srender = fbsTarget.GetSkinnedMeshRenderer();
                                            var mesh = srender.sharedMesh;      
                                            if (mesh &&  mesh.blendShapeCount > 0)
                                            {
                                                string name = mesh.GetBlendShapeName(0);
                                                if (name.Contains("head"))
                                                {
                                                    srender
                                                        .SetBlendShapeWeight(realHumanData.eye_close_idx_in_head_of_eyectrl, 0f); // 눈감김 원천 봉쇄

                                                    srender
                                                        .SetBlendShapeWeight(realHumanData.eye_wink_idx_in_head_of_eyectrl, weight);                                        
                                                } else if (name.Contains("namida"))
                                                {
                                                    srender
                                                            .SetBlendShapeWeight(realHumanData.eye_close_idx_in_namida_of_eyectrl, 0f); // 눈감김 원천 봉쇄

                                                    srender
                                                        .SetBlendShapeWeight(realHumanData.eye_wink_idx_in_namida_of_eyectrl, weight);
                                                } else if (name.Contains("lash."))
                                                {
                                                    srender
                                                        .SetBlendShapeWeight(realHumanData.eye_close_idx_in_lash_of_eyectrl, 0f); // 눈감김 원천 봉쇄

                                                    srender
                                                        .SetBlendShapeWeight(realHumanData.eye_wink_idx_in_lash_of_eyectrl, weight);                                          
                                                }
                                            }
                                        }

                                        foreach (var fbsTarget in __instance.MouthCtrl.FBSTarget)
                                        {
                                            SkinnedMeshRenderer srender = fbsTarget.GetSkinnedMeshRenderer();
                                            var mesh = srender.sharedMesh;      
                                            if (mesh &&  mesh.blendShapeCount > 0)
                                            {
                                                string name = mesh.GetBlendShapeName(0);
                                                if (name.Contains("head"))
                                                {
                                                    srender
                                                        .SetBlendShapeWeight(realHumanData.eye_close_idx_in_head_of_mouthctrl, 0f); // 눈감김 원천 봉쇄

                                                    srender
                                                        .SetBlendShapeWeight(realHumanData.eye_wink_idx_in_head_of_mouthctrl, weight);   
                                                    //입모양
                                                    srender
                                                                    .SetBlendShapeWeight(38, 100);   

                                                } else if (name.Contains("namida"))
                                                {
                                                    srender
                                                        .SetBlendShapeWeight(realHumanData.eye_close_idx_in_namida_of_mouthctrl, 0f); // 눈감김 원천 봉쇄

                                                    srender
                                                        .SetBlendShapeWeight(realHumanData.eye_wink_idx_in_namida_of_mouthctrl, weight);                                      
                                                }
                                            }
                                        }                                             
                                    }        
                               }
                            }
                        }
                    }                   
                }
            }
        }      
#endif

#endregion
    }
#endregion
}
