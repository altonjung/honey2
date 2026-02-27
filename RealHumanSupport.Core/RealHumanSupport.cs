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
        public const string Version = "0.9.1.0";
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
        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static RealHumanSupport _self;

        private static string _assemblyLocation;
        internal bool _loaded = false;

        internal ObjectCtrlInfo _selectedOCI;

        private AssetBundle _bundle;

        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;
		
        private const int _uniqueId = ('R' << 24) | ('E' << 16) | ('A' << 8) | 'G';

        private Rect _windowRect = new Rect(70, 10, 400, 10);
        
        private WinkState _winkState = WinkState.Idle;
        
        float _winkTime = 0f;

        internal Texture2D _faceExpressionFemaleBumpMap2;

        internal Texture2D _faceExpressionMaleBumpMap2;

        internal Texture2D _bodyStrongFemale_A_BumpMap2;

        internal Texture2D _bodyStrongMale_A_BumpMap2;
#if FEATURE_TEARDROP
        internal Texture2D _TearDropImg;
#endif
        internal ComputeShader _mergeComputeShader;

        internal Dictionary<int, RealHumanData> _ociCharMgmt = new Dictionary<int, RealHumanData>();
       
        internal Coroutine _CheckRotationRoutine;

        // Config


        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigWinkShortcut { get; private set; }

        internal static ConfigEntry<bool> TearDropActive { get; private set; }

        internal static ConfigEntry<float> TearDropLevel { get; private set; }

        internal static ConfigEntry<bool> EyeShakeActive { get; private set; }

        internal static ConfigEntry<bool> ExBoneColliderActive { get; private set; }

        internal static ConfigEntry<bool> BreathActive { get; private set; }
#if FEATURE_FACE_BUMP_SUPPORT
        internal static ConfigEntry<bool> FaceBlendingActive { get; private set; }
#endif        

        internal static ConfigEntry<bool> BodyBlendingActive { get; private set; }

        internal static ConfigEntry<float> BreathStrong { get; private set; }

        internal static ConfigEntry<float> BreathInterval { get; private set; }

        internal static ConfigEntry<float> ExtraColliderScale{ get; private set; }

#if FEATURE_EXTRA_COLLIDER_DEBUG
        internal static ConfigEntry<bool> ExtraColliderDebug{ get; private set; }        
#endif        
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            string support_type = "Studio";
#if FEATURE_INGAME_SUPPORT            
            support_type = "Studio/InGame";
#endif
            EyeShakeActive = Config.Bind(support_type, "Eye Shaking", true, new ConfigDescription("Enable/Disable"));

            ExBoneColliderActive = Config.Bind(support_type, "Extra Collider", true, new ConfigDescription("Enable/Disable"));

            BreathActive = Config.Bind(support_type, "Bumping Belly", true, new ConfigDescription("Enable/Disable"));
            
            TearDropActive = Config.Bind(support_type, "Tear Drop", true, new ConfigDescription("Enable/Disable"));

            TearDropLevel = Config.Bind(support_type, "Tear Drop Level", 0.3f, new ConfigDescription("Tear Drop Level", new AcceptableValueRange<float>(0.1f, 1.0f)));

#if FEATURE_FACE_BUMP_SUPPORT    
            FaceBlendingActive = Config.Bind(support_type, "Strong Face", true, new ConfigDescription("Enable/Disable"));
#endif
            BodyBlendingActive = Config.Bind(support_type, "Blending Body", true, new ConfigDescription("Enable/Disable"));

            BreathInterval = Config.Bind("Breath", "Cycle", 1.5f, new ConfigDescription("Breath Interval", new AcceptableValueRange<float>(1.0f,  5.0f)));;

            BreathStrong = Config.Bind("Breath", "Strong", 0.45f, new ConfigDescription("Breath Amplitude", new AcceptableValueRange<float>(0.1f, 1.0f)));

            ExtraColliderScale = Config.Bind("ExtraCollider", "Scale", 1.0f, new ConfigDescription("Extra collider Scale", new AcceptableValueRange<float>(0.1f, 10.0f)));
#if FEATURE_EXTRA_COLLIDER_DEBUG
            ExtraColliderDebug = Config.Bind("ExtraCollider", "Show", false, new ConfigDescription("Debug Enable/Disable"));
#endif            
            ConfigWinkShortcut = Config.Bind("Expresison", "Toggle Wink", new KeyboardShortcut(KeyCode.W,  KeyCode.LeftControl));
  
            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            // UnityEngine.Debug.Log($">> start CheckRotationRoutine");
            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open RealGirl window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(RealHumanSupport).Assembly).LoadTexture(),
                false, this, val => _ShowUI = val);
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);
            
            _CheckRotationRoutine = StartCoroutine(CheckRotationRoutine());

            CharacterApi.RegisterExtraBehaviour<Controller>(GUID);
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
#if FEATURE_DYNAMIC_POSITION_CHANGE_SUPPORT   
            if (Input.GetMouseButtonDown(0)) {
                CheckNeckClick();
            }
#endif
        }
        
#if FEATURE_DYNAMIC_POSITION_CHANGE_SUPPORT

        void CheckNeckClick()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            if (!Physics.Raycast(ray, out RaycastHit hit, 10f))
                return;

            if (IsNeckClicked(hit))
            {
                UnityEngine.Debug.Log("Neck clicked");
                isNeckPressed = true;
                // 여기서 neck 선택 처리                
            } else {
                isNeckPressed = false;
            }
        }

        bool IsNeckClicked(RaycastHit hit)
        {
            // Raycast에 맞은 Collider가
            // neck 본의 자식인가?
            return hit.collider.transform.IsChildOf(neckTransform);
        }

        protected override void LateUpdate() {
            if (_loaded == false)
                return;

            // 마우스가 놓였을때.. 처리
            if (isNeckPressed) {
                if (_ociCharMgmt.Count > 0) {                
                    foreach (var kvp in _self._ociCharMgmt) {
                        CharControl charCtrl = kvp.Key;
                        RealHumanData realHumanData = kvp.Value;

                        if (charCtrl != null && realHumanData != null) {
                            Controller.ReflectIKToAnimation(realHumanData);
                        }
                    }
                }
            }
        }
#endif      

       protected override void OnGUI()
        {
            if (_ShowUI == false)
                return;

            if (StudioAPI.InsideStudio)
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "RealHuman" + Version);
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
            GUILayout.Label("Acc");
            GUILayout.BeginHorizontal();
            
            GUILayout.EndHorizontal();

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
            _bodyStrongMale_A_BumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_M_BumpMap2");
#if FEATURE_TEARDROP
            _TearDropImg = _bundle.LoadAsset<Texture2D>("teardrop");
#endif            
#if FEATURE_FACE_BUMP_SUPPORT               
            _faceExpressionFemaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Face_Expression_F_BumpMap2");
            _faceExpressionMaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Face_Expression_M_BumpMap2");
#endif            

            _mergeComputeShader = _bundle.LoadAsset<ComputeShader>("MergeTextures.compute");
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
                    if (!Input.GetMouseButton(0)) {
                        if (_ociCharMgmt.TryGetValue(ociChar.GetChaControl().GetHashCode(), out var realHumanData))
                        {
                            if (realHumanData.m_skin_body == null || realHumanData.m_skin_head == null)
                                realHumanData = Controller.GetMaterials(ociChar.GetChaControl(), realHumanData);

                            if (
                                (Controller.GetBoneRotationFromFK(realHumanData.fk_left_foot_bone)._q != realHumanData.prev_fk_left_foot_rot) ||
                                (Controller.GetBoneRotationFromFK(realHumanData.fk_right_foot_bone)._q != realHumanData.prev_fk_right_foot_rot) ||
                                (Controller.GetBoneRotationFromFK(realHumanData.fk_left_knee_bone)._q != realHumanData.prev_fk_left_knee_rot) ||
                                (Controller.GetBoneRotationFromFK(realHumanData.fk_right_knee_bone)._q != realHumanData.prev_fk_right_knee_rot) ||
                                (Controller.GetBoneRotationFromFK(realHumanData.fk_left_thigh_bone)._q != realHumanData.prev_fk_left_thigh_rot) ||
                                (Controller.GetBoneRotationFromFK(realHumanData.fk_right_thigh_bone)._q != realHumanData.prev_fk_right_thigh_rot) ||                          
                                (Controller.GetBoneRotationFromFK(realHumanData.fk_spine01_bone)._q != realHumanData.prev_fk_spine01_rot) || 
                                (Controller.GetBoneRotationFromFK(realHumanData.fk_spine02_bone)._q != realHumanData.prev_fk_spine02_rot) ||
                                (Controller.GetBoneRotationFromFK(realHumanData.fk_head_bone)._q != realHumanData.prev_fk_head_rot) ||
                                (Controller.GetBoneRotationFromFK(realHumanData.fk_left_shoulder_bone)._q != realHumanData.prev_fk_left_shoulder_rot) ||
                                (Controller.GetBoneRotationFromFK(realHumanData.fk_right_shoulder_bone)._q != realHumanData.prev_fk_right_shoulder_rot)
                            )
                            {
                                Controller.SupportBodyBumpEffect(ociChar.charInfo, realHumanData);
                            }                          
                        }                        
                    }                  
                }

                yield return new WaitForSeconds(0.5f); // 0.5초 대기
            }
        }

        private IEnumerator RoutineForBody(RealHumanData realHumanData)
        {
            float tearValue = 0;
            float noseValue = 0;
            float refValue = 0;
            bool tearIncreasing = true;
            bool noseIncreasing = true;
            bool refIncreasing = true;

            float previosBellySize = 0.0f;
            float initBellySize = realHumanData.pregnancyController.infConfig.inflationSize;

            while (true)
            {
                if (_loaded == true)
                {
                    if (realHumanData.coroutine_pause)
                    {
                        yield return null;
                    }

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
                        if (realHumanData.pregnancyController != null)
                        {   
                            if(previosBellySize != realHumanData.pregnancyController.infConfig.inflationSize)
                            {
                                initBellySize = previosBellySize = realHumanData.pregnancyController.infConfig.inflationSize;

                                if (initBellySize > 30.0f)
                                {
                                    initBellySize = 30.0f;
                                }
                            } else
                            {
                                float sinValue = (Mathf.Sin(time * BreathInterval.Value) + 1f) * 0.5f;
                     
                                realHumanData.pregnancyController.infConfig.inflationSize = initBellySize + (1f - sinValue) * 10f * BreathStrong.Value;
                                realHumanData.pregnancyController.MeshInflate(new MeshInflateFlags(realHumanData.pregnancyController), "StudioSlider");
                                previosBellySize = realHumanData.pregnancyController.infConfig.inflationSize;
                            }
                        }
                    }

#if FEATURE_TEARDROP
                    if (TearDropActive.Value)
                    {
                        float deltaTear = Time.deltaTime / 10f; // ← (10초)

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
                            realHumanData.m_tear_eye.SetFloat("_NamidaScale", TearDropLevel.Value);
                            realHumanData.m_tear_eye.SetFloat("_RefractionScale", tearSin); 
                        }

                        float deltaNose = Time.deltaTime / 3f; // ← (3초)

                        if (noseIncreasing)
                        {
                            noseValue += deltaNose;
                            if (noseValue >= 1f)
                            {
                                noseValue = 1f;
                                noseIncreasing = false;
                            }
                        }
                        else
                        {
                            noseValue -= deltaNose;
                            if (noseValue <= 0.1f)
                            {
                                noseValue = 0.1f;
                                noseIncreasing = true;
                            }
                        }
                        
                        float noseSin = Mathf.Sin(noseValue * Mathf.PI);
                        // ----------------- 코평수 처리 -----------------
                        if (realHumanData.nose_wing_l_tr != null) {

                            float noseScaleFactor = 1f + (noseSin * 0.2f);
                            Vector3 scalel = realHumanData.noseBaseScale;
                            Vector3 scaler = realHumanData.noseBaseScale;

                            scalel.x = realHumanData.noseBaseScale.x * noseScaleFactor;
                            scaler.x = realHumanData.noseBaseScale.x * noseScaleFactor;

                            realHumanData.nose_wing_l_tr.localScale = scalel;
                            realHumanData.nose_wing_r_tr.localScale = scaler;
                        }
                    } else
                    {
                        if (realHumanData.m_tear_eye != null) {
                            realHumanData.m_tear_eye.SetFloat("_NamidaScale", 0f);
                            realHumanData.m_tear_eye.SetFloat("_RefractionScale", 0f);
                        }

                        if (realHumanData.noseScaleInitialized)
                        {
                            if (realHumanData.nose_wing_l_tr != null)
                                realHumanData.nose_wing_l_tr.localScale = realHumanData.noseBaseScale;

                            if (realHumanData.nose_wing_r_tr != null)
                                realHumanData.nose_wing_r_tr.localScale = realHumanData.noseBaseScale;
                        }
                    }
#endif
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
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return null;

#if FEATURE_DYNAMIC_POSITION_CHANGE_SUPPORT
            Controller.SupportIKOnScene(chaControl, realHumanData);
#endif              
            Controller.SupportExtraDynamicBones(chaControl, realHumanData);
            Controller.SupportEyeFastBlinkEffect(chaControl, realHumanData);
            Controller.SupportBodyBumpEffect(chaControl, realHumanData);
#if FEATURE_FACE_BUMP_SUPPORT            
            Controller.SupportFaceBumpEffect(chaControl, realHumanData);
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
                            ociChar.GetChaControl().StartCoroutine(_self.ExecuteAfterFrame(ociChar.GetChaControl(), realHumanData));
                        }
                    }
                    else
                    {
                        RealHumanData realHumanData2 = new RealHumanData();
                        realHumanData2 = Controller.InitRealHumanData(ociChar.GetChaControl(), realHumanData2);

                        if (realHumanData2 != null)
                        {
                            realHumanData2.coroutine = ociChar.charInfo.StartCoroutine(_self.RoutineForBody(realHumanData2));                    
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
                        realHumanData.coroutine_pause = true;
                        realHumanData.c_m_eye.Clear();
                        // 재 추가 
                        realHumanData = Controller.InitRealHumanData(__instance.GetChaControl(), realHumanData);

                        if (realHumanData != null)
                        {
                            realHumanData.coroutine_pause = false;
                            __instance.charInfo.StartCoroutine(_self.ExecuteAfterFrame(__instance.GetChaControl(), realHumanData));
                        } else
                        {
                            Logger.LogMessage($"Body skin not has bumpmap2");
                        }                    

                        _self._selectedOCI = __instance;
                    }            
                }
            }
        }

        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {

                if (__instance != null)
                {                    
                    if (_self._ociCharMgmt.TryGetValue(__instance.GetHashCode(), out var realHumanData))
                    {
                        if (realHumanData.pregnancyController != null)
                            realHumanData.pregnancyController.infConfig.inflationRoundness += 0.0001f;
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
                   TearDropLevel.Value = value;
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
                        Controller.SetHairDown(_char.GetChaControl(), realHumanData);
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

#if FEATURE_FACE_EXPRESSION_SUPPORT
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

                    if (ConfigWinkShortcut.Value.IsDown()) {
                        OCIChar ociChar = _self._selectedOCI as OCIChar;
                        if (ociChar != null)
                        {
                            var cha = ociChar.GetChaControl();

                            if (_self._ociCharMgmt.TryGetValue(cha.GetHashCode(), out var realHumanData))
                            {
                                if (_self._winkState == WinkState.Idle)
                                {
                                    _self._winkState = WinkState.Playing;
                                    _self._winkTime = 0f;
                                }

                                if (_self._winkState == WinkState.Playing)
                                {
                                    _self._winkTime += Time.deltaTime;

                                    const float CLOSE_TIME = 0.3f;
                                    const float HOLD_TIME  = 1.0f;
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

                                        cha.ChangeMouthPtn(1, true);                                
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
                                                fbsTarget.GetSkinnedMeshRenderer()
                                                        .SetBlendShapeWeight(realHumanData.eye_close_idx_in_head_of_eyectrl, 0f); // 눈감김 원천 봉쇄

                                                fbsTarget.GetSkinnedMeshRenderer()
                                                    .SetBlendShapeWeight(realHumanData.eye_wink_idx_in_head_of_eyectrl, weight);                                        
                                            } else if (name.Contains("namida"))
                                            {
                                                fbsTarget.GetSkinnedMeshRenderer()
                                                        .SetBlendShapeWeight(realHumanData.eye_close_idx_in_namida_of_eyectrl, 0f); // 눈감김 원천 봉쇄

                                                fbsTarget.GetSkinnedMeshRenderer()
                                                    .SetBlendShapeWeight(realHumanData.eye_wink_idx_in_namida_of_eyectrl, weight);
                                            } else if (name.Contains("lash."))
                                            {
                                                fbsTarget.GetSkinnedMeshRenderer()
                                                        .SetBlendShapeWeight(realHumanData.eye_close_idx_in_lash_of_eyectrl, 0f); // 눈감김 원천 봉쇄

                                                fbsTarget.GetSkinnedMeshRenderer()
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
                                                fbsTarget.GetSkinnedMeshRenderer()
                                                        .SetBlendShapeWeight(realHumanData.eye_close_idx_in_head_of_mouthctrl, 0f); // 눈감김 원천 봉쇄

                                                fbsTarget.GetSkinnedMeshRenderer()
                                                    .SetBlendShapeWeight(realHumanData.eye_wink_idx_in_head_of_mouthctrl, weight);   
        //입모양
                                                fbsTarget.GetSkinnedMeshRenderer()
                                                                .SetBlendShapeWeight(38, 100);   

                                            } else if (name.Contains("namida"))
                                            {
                                                fbsTarget.GetSkinnedMeshRenderer()
                                                        .SetBlendShapeWeight(realHumanData.eye_close_idx_in_namida_of_mouthctrl, 0f); // 눈감김 원천 봉쇄

                                                fbsTarget.GetSkinnedMeshRenderer()
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
#endif

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
                        realHumanData2 = Controller.InitRealHumanData(__instance.chaCtrl, realHumanData2);

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
                    realHumanData2 = Controller.InitRealHumanData(__instance.player, realHumanData2);

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
                        realHumanData2 = Controller.InitRealHumanData(chaCtrl, realHumanData2);

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
