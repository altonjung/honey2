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
using static CharaUtils.Expression;

/*
    추가 개발
     - scene 저장

    활용 자료
     - charmRate 값 실시간 변경
     - orderRate 값 실시간 변경

        // category = 0  armup L
        // category = 1  armup R
        // category = 2  Knee L
        // category = 3  Knee R
        // category = 4  armLow L 
        // category = 5  armLow R 
        // category = 6  legup L, siri
        // category = 7  legup R, siri

*/

namespace JointCorrectionSlider
{

#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class JointCorrectionSlider : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "JointCorrectionSlider";
        public const string Version = "0.9.0.0";
        public const string GUID = "com.alton.illusionplugins.JointCorrectionSlider";
        internal const string _ownerId = "Alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "joint_correction_slider";
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
        internal static JointCorrectionSlider _self;

        private static string _assemblyLocation;
        internal bool _loaded = false;

        private AssetBundle _bundle;

        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;
		
        private const int _uniqueId = ('J' << 24) | ('T' << 16) | ('C' << 8) | 'S';

        private Rect _windowRect = new Rect(140, 10, 300, 10);
    
        private float _prevLeftShoulder = 0f;
        private float _prevRightShoulder = 0f; 
        private float _prevLeftKnee = 0f;
        private float _prevRightKnee = 0f;      
        private float _prevLeftKnee2 = 0f;
        private float _prevRightKnee2 = 0f;           
        private float _prevLeftLeg = 0f;
        private float _prevRightLeg = 0f;
        private float _prevLeftArmUp = 0f;
        private float _prevRightArmUp = 0f;
        private float _prevLeftArmDn = 0f;
        private float _prevRightArmDn = 0f;
        private float _prevLeftElbow = 0f;
        private float _prevRightElbow = 0f;

        private int   _creating_char_sex = 0;

        // shoulder
        private Transform _shoulder02_s_L;
        private Transform _shoulder02_s_R;

        private UnityEngine.Vector3 _shoulder02BasePosL;
        private UnityEngine.Vector3 _shoulder02BasePosR;
        private UnityEngine.Vector3 _shoulder02BaseScaleL;
        private UnityEngine.Vector3 _shoulder02BaseScaleR;
        private bool _shoulder02BaseSetL;
        private bool _shoulder02BaseSetR;
        
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
#if FEATURE_SHOULDER_CORRECTION
        internal static ConfigEntry<float> LeftShoulderConfig { get; private set; }
        internal static ConfigEntry<float> RightShoulderConfig { get; private set; }

        internal static ConfigEntry<float> LeftElbowConfig { get; private set; }
        internal static ConfigEntry<float> RightElbowConfig { get; private set; }
#endif

        internal static ConfigEntry<float> LeftArmUpperConfig { get; private set; }

        internal static ConfigEntry<float> RightArmUpperConfig { get; private set; }

        internal static ConfigEntry<float> LeftArmLowerConfig { get; private set; }

        internal static ConfigEntry<float> RightArmLowerConfig { get; private set; }

        internal static ConfigEntry<float> LeftKneeConfig { get; private set; }
        internal static ConfigEntry<float> RightKneeConfig { get; private set; }
#if FEATURE_KNEE_CORRECTION
        internal static ConfigEntry<float> LeftKnee2Config { get; private set; }
        internal static ConfigEntry<float> RightKnee2Config { get; private set; }
#endif
        internal static ConfigEntry<float> LeftLegConfig { get; private set; }

        internal static ConfigEntry<float> RightLegConfig { get; private set; }

#if FEATURE_DEBUG
        // internal static ConfigEntry<float> CharmRateConfig  { get; private set; }

        // internal static ConfigEntry<int> RoTOrderConfig  { get; private set; }

        internal static ConfigEntry<bool> RXConfig  { get; private set; }

        internal static ConfigEntry<bool> RYConfig  { get; private set; }

        internal static ConfigEntry<bool> RZConfig  { get; private set; }
#endif

        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            string support_type = "Joint";

            LeftArmUpperConfig = Config.Bind(support_type, "Left Arm Upper", 0.0f, new ConfigDescription("Left Arm Upper", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightArmUpperConfig = Config.Bind(support_type, "Right Arm Upper", 0.0f, new ConfigDescription("Right Arm Upper", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            LeftArmLowerConfig = Config.Bind(support_type, "Left Arm Lower", 0.0f, new ConfigDescription("Left Arm Lower", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightArmLowerConfig = Config.Bind(support_type, "Right Arm Lower", 0.0f, new ConfigDescription("Right Arm Lower", new AcceptableValueRange<float>(-1.0f, 1.0f)));

            LeftLegConfig = Config.Bind(support_type, "Left Leg", 0.0f, new ConfigDescription("Left Leg", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightLegConfig = Config.Bind(support_type, "Right Leg", 0.0f, new ConfigDescription("Right Leg", new AcceptableValueRange<float>(-1.0f, 1.0f)));

            LeftKneeConfig = Config.Bind(support_type, "Left Knee", 0.0f, new ConfigDescription("Left Knee", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightKneeConfig = Config.Bind(support_type, "Right Knee", 0.0f, new ConfigDescription("Right Knee", new AcceptableValueRange<float>(-1.0f, 1.0f)));

#if FEATURE_SHOULDER_CORRECTION
            LeftShoulderConfig = Config.Bind(support_type, "Left Shoulder", 0.0f, new ConfigDescription("Left Shoulder", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightShoulderConfig = Config.Bind(support_type, "Right Shoulder", 0.0f, new ConfigDescription("Right Shoulder", new AcceptableValueRange<float>(-1.0f, 1.0f)));
#endif
#if FEATURE_ELBOW_CORRECTION
            LeftElbowConfig = Config.Bind(support_type, "Left Elbow", 0.0f, new ConfigDescription("Left Elbow", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightElbowConfig = Config.Bind(support_type, "Right Elbow", 0.0f, new ConfigDescription("Right Elbow", new AcceptableValueRange<float>(-1.0f, 1.0f)));
#endif
#if FEATURE_KNEE_CORRECTION
            LeftKnee2Config = Config.Bind(support_type, "Left Knee2", 0.0f, new ConfigDescription("Left Knee", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightKnee2Config = Config.Bind(support_type, "Right Knee2", 0.0f, new ConfigDescription("Right Knee", new AcceptableValueRange<float>(-1.0f, 1.0f)));
#endif
#if FEATURE_DEBUG
            //CharmRateConfig = Config.Bind("Debug", "CharmRate", 0.0f, new ConfigDescription("CharmRate", new AcceptableValueRange<float>(0.0f, 1.0f)));
            //RoTOrderConfig = Config.Bind("Debug", "RoTOrder", 0, new ConfigDescription("RoTOrder", new AcceptableValueRange<int>(0, 5)));
            RXConfig = Config.Bind("Debug", "Toggle RXConfig", true, "");
            RYConfig = Config.Bind("Debug", "Toggle RYConfig", true, "");
            RZConfig = Config.Bind("Debug", "Toggle RZConfig", true, "");
#endif
            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open JointCorrection window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(JointCorrectionSlider).Assembly).LoadTexture(),
                false, this, val => _ShowUI = val);
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);        

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

        private void SetScriptInfo(OCIChar ociChar, int categoryId, float value)
        {
            foreach (Expression.ScriptInfo scriptInfo in ociChar.charInfo.expression.info)
            {   
                if (scriptInfo.categoryNo == categoryId) {
                    //scriptInfo.enableCorrect = true;
                    scriptInfo.enable = true;
#if FEATURE_DEBUG
                    scriptInfo.correct.useRX = RXConfig.Value;
                    scriptInfo.correct.useRY = RYConfig.Value;
                    scriptInfo.correct.useRZ = RZConfig.Value;
                    // scriptInfo.correct.charmRate = CharmRateConfig.Value;
                    // scriptInfo.correct.rotOrder = (Correct.RotationOrder)RoTOrderConfig.Value;
#endif

                    if(scriptInfo.correct.useRX)
                    {
                        scriptInfo.correct.valRXMin = value;
                        scriptInfo.correct.valRXMax = value;        
                    }

                    if(scriptInfo.correct.useRY)
                    {
                        scriptInfo.correct.valRYMin = value;
                        scriptInfo.correct.valRYMax = value;        
                    }

                    if (scriptInfo.correct.useRY)
                    {
                        scriptInfo.correct.valRZMin = value;
                        scriptInfo.correct.valRZMax = value;    
                    }
                }
            } 
        }

        protected override void Update()
        {
            if (_loaded == false)
                return;

            if (_currentOCIChar == null)
                return;             
         
// arm
            if (LeftArmUpperConfig.Value != _prevLeftArmUp)
            {
                _prevLeftArmUp = LeftArmUpperConfig.Value;
                SetScriptInfo(_currentOCIChar, 0, LeftArmUpperConfig.Value);
            }
            if (RightArmUpperConfig.Value != _prevRightArmUp)
            {
                _prevRightArmUp = RightArmUpperConfig.Value;
                SetScriptInfo(_currentOCIChar, 1, RightArmUpperConfig.Value);
            }
            if (LeftArmLowerConfig.Value != _prevLeftArmDn)
            {
                _prevLeftArmDn = LeftArmLowerConfig.Value;
                SetScriptInfo(_currentOCIChar, 4, LeftArmLowerConfig.Value);
            }
            if (RightArmLowerConfig.Value != _prevRightArmDn)
            {
                _prevRightArmDn = RightArmLowerConfig.Value;
                SetScriptInfo(_currentOCIChar, 5, RightArmLowerConfig.Value);
            }
// leg            
            if (LeftLegConfig.Value != _prevLeftLeg)
            {
                _prevLeftLeg = LeftLegConfig.Value;
                // SetScriptInfo(_currentOCIChar, 2, LeftLegConfig.Value);
                SetScriptInfo(_currentOCIChar, 6, LeftLegConfig.Value);
            }
            if (RightLegConfig.Value != _prevRightLeg)
            {
                _prevRightLeg = RightLegConfig.Value;
                // SetScriptInfo(_currentOCIChar, 3, RightLegConfig.Value);
                SetScriptInfo(_currentOCIChar, 7, RightLegConfig.Value);
            }

            if (LeftKneeConfig.Value != _prevLeftKnee)
            {
                _prevLeftKnee = LeftKneeConfig.Value;
                SetScriptInfo(_currentOCIChar, 2, LeftKneeConfig.Value);
            }
            if (RightKneeConfig.Value != _prevRightKnee)
            {
                _prevRightKnee = RightKneeConfig.Value;
                SetScriptInfo(_currentOCIChar, 3, RightKneeConfig.Value);
            }
#if FEATURE_KNEE_CORRECTION            
            if (LeftKnee2Config.Value != _prevLeftKnee2)
            {
                _prevLeftKnee2 = LeftKnee2Config.Value;
                SetScriptInfo(_currentOCIChar, 8, LeftKnee2Config.Value);
            }
            if (RightKnee2Config.Value != _prevRightKnee2)
            {
                _prevRightKnee2 = RightKnee2Config.Value;
                SetScriptInfo(_currentOCIChar, 9, RightKnee2Config.Value);
            }
#endif                
#if FEATURE_SHOULDER_CORRECTION
            if (LeftShoulderConfig.Value != _prevLeftShoulder)
            {
                _prevLeftShoulder = LeftShoulderConfig.Value;
                SetScriptInfo(_currentOCIChar, 10, LeftShoulderConfig.Value);
            }
            if (RightShoulderConfig.Value != _prevRightShoulder)
            {
                _prevRightShoulder = RightShoulderConfig.Value;
                SetScriptInfo(_currentOCIChar, 11, RightShoulderConfig.Value);
            }
#endif   
#if FEATURE_ELBOW_CORRECTION
            if (LeftElbowConfig.Value != _prevLeftElbow)
            {
                _prevLeftElbow = LeftElbowConfig.Value;
                SetScriptInfo(_currentOCIChar, 12, LeftElbowConfig.Value);
            }
            if (RightElbowConfig.Value != _prevRightElbow)
            {
                _prevRightElbow = RightElbowConfig.Value;
                SetScriptInfo(_currentOCIChar, 13, RightElbowConfig.Value);
            }
#endif
        }

        protected override void LateUpdate()
        {
#if FEATURE_SHOULDER_CORRECTION
            if (_shoulder02_s_L != null)
                ApplyBoneTransform(_shoulder02_s_L, LeftShoulderConfig.Value, ref _shoulder02BaseSetL, ref _shoulder02BasePosL, ref _shoulder02BaseScaleL, TargetDirection.X_POS);

            if (_shoulder02_s_R != null)
                ApplyBoneTransform(_shoulder02_s_R, RightShoulderConfig.Value, ref _shoulder02BaseSetR, ref _shoulder02BasePosR, ref _shoulder02BaseScaleR, TargetDirection.X_POS);
#endif
        }

        private void InitConfig()
        {
#if FEATURE_SHOULDER_CORRECTION
            LeftShoulderConfig.Value = (float)LeftShoulderConfig.DefaultValue;
            RightShoulderConfig.Value = (float)RightShoulderConfig.DefaultValue;
#endif
#if FEATURE_ELBOW_CORRECTION
            LeftElbowConfig.Value = (float)LeftElbowConfig.DefaultValue;
            RightElbowConfig.Value = (float)RightElbowConfig.DefaultValue;
#endif
#if FEATURE_KNEE_CORRECTION
            LeftKnee2Config.Value = (float)LeftKnee2Config.DefaultValue;
            RightKnee2Config.Value = (float)RightKnee2Config.DefaultValue;
#endif

            LeftKneeConfig.Value = (float)LeftKneeConfig.DefaultValue;
            RightKneeConfig.Value = (float)RightKneeConfig.DefaultValue;

            LeftLegConfig.Value = (float)LeftLegConfig.DefaultValue;
            RightLegConfig.Value = (float)RightLegConfig.DefaultValue;

            LeftArmUpperConfig.Value = (float)LeftArmUpperConfig.DefaultValue;            
            RightArmUpperConfig.Value = (float)RightArmUpperConfig.DefaultValue;
            LeftArmLowerConfig.Value = (float)LeftArmLowerConfig.DefaultValue;            
            RightArmLowerConfig.Value = (float)RightArmLowerConfig.DefaultValue;

#if FEATURE_DEBUG
            // CharmRateConfig.Value = (float)CharmRateConfig.DefaultValue;
            // RoTOrderConfig.Value = (int)RoTOrderConfig.DefaultValue;
            RXConfig.Value = (bool)RXConfig.DefaultValue;
            RYConfig.Value = (bool)RYConfig.DefaultValue;
            RZConfig.Value = (bool)RZConfig.DefaultValue;
#endif
        }

        protected override void OnGUI()
        {
            if (_ShowUI == false)
                return;

            if (StudioAPI.InsideStudio)
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "JointCorrection" + Version);
        }
        private void WindowFunc(int id)
        {
            var studio = Studio.Studio.Instance;

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

            // ================= UI =================
#if FEATURE_SHOULDER_CORRECTION
            GUILayout.Label("<color=orange>Shoulder</color>", RichLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Shdr(L)", "Left"), GUILayout.Width(60));
            LeftShoulderConfig.Value = GUILayout.HorizontalSlider(LeftShoulderConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftShoulderConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Shdr(R)", "Right"), GUILayout.Width(60));
            RightShoulderConfig.Value = GUILayout.HorizontalSlider(RightShoulderConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightShoulderConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
#endif            
            // Top
            GUILayout.Label("<color=orange>Arm_Up</color>", RichLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("ArmUp(L)", "Left"), GUILayout.Width(60));
            LeftArmUpperConfig.Value = GUILayout.HorizontalSlider(LeftArmUpperConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftArmUpperConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("ArmUp(R)", "Right"),GUILayout.Width(60));
            RightArmUpperConfig.Value = GUILayout.HorizontalSlider(RightArmUpperConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightArmUpperConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.Label("<color=orange>Arm_Dn</color>", RichLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("ArmDn(L)", "Left"), GUILayout.Width(60));
            LeftArmLowerConfig.Value = GUILayout.HorizontalSlider(LeftArmLowerConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftArmLowerConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("ArmDn(R)", "Right"), GUILayout.Width(60));
            RightArmLowerConfig.Value = GUILayout.HorizontalSlider(RightArmLowerConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightArmLowerConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
#if FEATURE_ELBOW_CORRECTION
            GUILayout.Label("<color=orange>Elbow</color>", RichLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Elbow(L)", "Left"), GUILayout.Width(60));
            LeftElbowConfig.Value = GUILayout.HorizontalSlider(LeftElbowConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftElbowConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Elbow(R)", "Right"), GUILayout.Width(60));
            RightElbowConfig.Value = GUILayout.HorizontalSlider(RightElbowConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightElbowConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
#endif
            // Bottom
            GUILayout.Label("<color=orange>Thigh</color>", RichLabel);            
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Thigh(L)", "Left"), GUILayout.Width(60));
            LeftLegConfig.Value = GUILayout.HorizontalSlider(LeftLegConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftLegConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Thigh(R)", "Right"), GUILayout.Width(60));
            RightLegConfig.Value = GUILayout.HorizontalSlider(RightLegConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightLegConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.Label("<color=orange>Knee</color>", RichLabel);            
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Knee(L)", "Back"), GUILayout.Width(60));
            LeftKneeConfig.Value = GUILayout.HorizontalSlider(LeftKneeConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftKneeConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Knee(R)", "Back"), GUILayout.Width(60));
            RightKneeConfig.Value = GUILayout.HorizontalSlider(RightKneeConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightKneeConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
#if FEATURE_KNEE_CORRECTION
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Knee(L)", "Front"), GUILayout.Width(60));
            LeftKnee2Config.Value = GUILayout.HorizontalSlider(LeftKnee2Config.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftKnee2Config.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Knee(R)", "Front"), GUILayout.Width(60));
            RightKnee2Config.Value = GUILayout.HorizontalSlider(RightKnee2Config.Value, -1.0f, 1.0f);
            GUILayout.Label(RightKnee2Config.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
#endif
            draw_seperate();  
            if (GUILayout.Button("Default"))
                InitConfig();

            if (GUILayout.Button("Close")) {
                Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
                _ShowUI = false;
            }

            // ⭐ Tooltip
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 150, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }
        private void draw_seperate()
        {
            GUILayout.Space(5);
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(0.3f));
            GUI.Box(rect, GUIContent.none);
            GUILayout.Space(10);
        }
        #endregion

        #region Private Methods
        private void Init()
        {
            _loaded = true;
        }

        private void SceneInit()
        {
        }
#endregion

#if FEATURE_SHOULDER_CORRECTION || FEATURE_KNEE_CORRECTION
        private const float Shoulder02PosXRange = 0.8f;
        private const float Shoulder02ScaleMin = 0.5f;
        private const float Shoulder02ScaleMax = 1.5f;

        private void ApplyBoneTransform(
            Transform tr,
            float value,
            ref bool baseSet,
            ref Vector3 basePos,
            ref Vector3 baseScale,
            params TargetDirection[] directions)
        {
            // -------------------------
            // 1. Base 값 캐싱 (최초 1회)
            // -------------------------
            if (!baseSet)
            {
                basePos = tr.localPosition;
                baseScale = tr.localScale;
                baseSet = true;
            }

            // -------------------------
            // 2. 입력 안정화
            // -------------------------
            value = Mathf.Clamp(value, -1f, 1f);

            // -------------------------
            // 3. Position 계산 (대칭 선형)
            // -------------------------
            float posOffset = value * Shoulder02PosXRange;

            Vector3 newPos = basePos;

            // directions가 null 또는 비어있으면 아무것도 안함
            if (directions != null)
            {
                for (int i = 0; i < directions.Length; i++)
                {
                    switch (directions[i])
                    {
                        case TargetDirection.X_POS:
                            newPos.x += posOffset;
                            break;

                        case TargetDirection.Y_POS:
                            newPos.y += posOffset;
                            break;

                        case TargetDirection.Z_POS:
                            newPos.z += posOffset;
                            break;
                    }
                }
            }

            // -------------------------
            // 4. Scale 계산 (0 기준 대칭)
            // -------------------------
            float scaleFactor = (value >= 0f)
                ? Mathf.Lerp(1f, Shoulder02ScaleMax, value)
                : Mathf.Lerp(1f, Shoulder02ScaleMin, -value);

            // -------------------------
            // 5. 적용
            // -------------------------
            tr.localPosition = newPos;
            tr.localScale = baseScale * scaleFactor;
        }

#endif

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

                    string bone_prefix_str = "cf_";
                    if (chaControl.sex == 0)
                        bone_prefix_str = "cm_";

#if FEATURE_SHOULDER_CORRECTION
                    _self._shoulder02_s_L = chaControl.objAnim.transform.FindLoop(bone_prefix_str + "J_Shoulder02_s_L");
                    _self._shoulder02_s_R = chaControl.objAnim.transform.FindLoop(bone_prefix_str + "J_Shoulder02_s_R");
                    _self._shoulder02BaseSetL = false;
                    _self._shoulder02BaseSetR = false;
#endif
                }

                return true;
            }
        }

#if FEATURE_SHOULDER_CORRECTION
       [HarmonyPatch(typeof(ChaControl), "InitializeExpression", typeof(int), typeof(bool))]
        private static class ChaControl_InitializeExpression_Patches
        {
            private static bool Prefix(ChaControl __instance, int sex, bool _enable, ref bool __result)
            {
                // UnityEngine.Debug.Log(Environment.StackTrace);

                _self._creating_char_sex = sex;
                string text = "list/expression.unity3d";
                string text2 = (sex == 0) ? "cm_expression" : "cf_expression";
                if (!global::AssetBundleCheck.IsFile(text, text2))
                {
                    __result = false;
                }
                __instance.expression = __instance.objRoot.AddComponent<Expression>();
                __instance.expression.LoadSetting(text, text2);
                int[] array = new int[]
                {
                    0,
                    0,
                    4,
                    0,
                    0,
                    0,
                    0,
                    1,
                    1,
                    5,
                    1,
                    1,
                    1,
                    1,
                    6,
                    6,
                    6,
                    2,
                    2,
                    6,
                    7,
                    7,
                    7,
                    3,
                    3,
                    7,
                    8,
                    9,
                    10,
                    11,
                    12,
                    13
                };
                for (int i = 0; i < __instance.expression.info.Length; i++)
                {
                    __instance.expression.info[i].categoryNo = array[i];
                }
                __instance.expression.SetCharaTransform(__instance.objRoot.transform);
                __instance.expression.Initialize();
                __instance.expression.enable = _enable;
                __result = true;

                return false;
            }
        }

        [HarmonyPatch(typeof(CharaUtils.Expression), nameof(Expression.LoadSettingSub), typeof(List<string>))]
        internal static class Expression_LoadSettingSub_Patches
        {
            private static bool Prefix(Expression __instance, List<string> slist, ref bool __result)
            {
                // UnityEngine.Debug.Log(Environment.StackTrace);                
                string prefix = _self._creating_char_sex == 0 ? "cm_" : "cf_";        
                var rawList = new List<string>
                {
                "32\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0",
                "0\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmUp01_dam_L\tcf_J_ArmUp00_L\tEuler\tYZX\t0.5\t○\t-0.66\t-0.66\t×\t0\t0\t×\t0\t0",
                "0\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmUp02_dam_L\tcf_J_ArmUp00_L\tEuler\tYZX\t0.5\t○\t-0.33\t-0.33\t×\t0\t0\t×\t0\t0",
                "0\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmLow02_dam_L\tcf_J_Hand_L\tEuler\tZYX\t0\t○\t0.5\t0.5\t×\t0\t0\t×\t0\t0",
                "0\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmElbo_dam_01_L\tcf_J_ArmLow01_L\tEuler\tXZY\t0\t×\t0\t0\t○\t0.6\t0.6\t×\t0\t0",
                "0\t○\tcf_J_ArmElboura_dam_L\tcf_J_ArmLow02_dam_L\tRevX\tcf_J_ArmElbo_dam_01_L\tY\tY\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "0\t○\tcf_J_Hand_Wrist_dam_L\tcf_J_ArmLow02_dam_L\tX\tcf_J_Hand_L\tZ\tZ\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "0\t○\tcf_J_Hand_dam_L\tcf_J_Hand_Middle01_L\tRevX\tcf_J_Hand_Wrist_dam_L\tY\tY\tZ\tZYX\t-180\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "1\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmUp01_dam_R\tcf_J_ArmUp00_R\tEuler\tYZX\t0.5\t○\t-0.66\t-0.66\t×\t0\t0\t×\t0\t0",
                "1\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmUp02_dam_R\tcf_J_ArmUp00_R\tEuler\tYZX\t0.5\t○\t-0.33\t-0.33\t×\t0\t0\t×\t0\t0",
                "1\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmLow02_dam_R\tcf_J_Hand_R\tEuler\tZYX\t0\t○\t0.5\t0.5\t×\t0\t0\t×\t0\t0",
                "1\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmElbo_dam_01_R\tcf_J_ArmLow01_R\tEuler\tXZY\t0\t×\t0\t0\t○\t0.6\t0.6\t×\t0\t0",
                "1\t○\tcf_J_ArmElboura_dam_R\tcf_J_ArmLow02_dam_R\tX\tcf_J_ArmElbo_dam_01_R\tZ\tZ\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "1\t○\tcf_J_Hand_Wrist_dam_R\tcf_J_ArmLow02_dam_R\tRevX\tcf_J_Hand_R\tZ\tZ\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "1\t○\tcf_J_Hand_dam_R\tcf_J_Hand_Middle01_R\tX\tcf_J_Hand_Wrist_dam_R\tZ\tZ\tZ\tZYX\t0\t180\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "2\t○\tcf_J_LegUpDam_L\tcf_J_LegLow01_L\tRevY\tcf_J_Kosi02\tX\tX\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "2\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegUp01_L\tcf_J_LegUp00_L\tEuler\tYZX\t0\t×\t0\t0\t○\t-0.85\t-0.85\t×\t0\t0",
                "2\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegUp02_L\tcf_J_LegUp00_L\tEuler\tYZX\t0\t×\t0\t0\t○\t-0.5\t-0.5\t×\t0\t0",
                "2\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegKnee_dam_L\tcf_J_LegLow01_L\tEuler\tZYX\t0\t○\t0.5\t0.5\t×\t0\t0\t×\t0\t0",
                "2\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegKnee_back_L\tcf_J_LegLow01_L\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "2\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_SiriDam_L\tcf_J_LegUp00_L\tEuler\tYZX\t0\t○\t0.5\t0.5\t○\t0.2\t0.2\t○\t0.25\t0.25",
                "3\t○\tcf_J_LegUpDam_R\tcf_J_LegLow01_R\tRevY\tcf_J_Kosi02\tX\tX\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegUp01_R\tcf_J_LegUp00_R\tEuler\tYZX\t0\t×\t0\t0\t○\t-0.85\t-0.85\t×\t0\t0",
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegUp02_R\tcf_J_LegUp00_R\tEuler\tYZX\t0\t×\t0\t0\t○\t-0.5\t-0.5\t×\t0\t0",
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegKnee_dam_R\tcf_J_LegLow01_R\tEuler\tZYX\t0\t○\t0.5\t0.5\t×\t0\t0\t×\t0\t0",
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegKnee_back_R\tcf_J_LegLow01_R\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_SiriDam_R\tcf_J_LegUp00_R\tEuler\tYZX\t0\t○\t0.5\t0.5\t○\t0.2\t0.2\t○\t0.25\t0.25",             
                "4\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegKnee_low_s_L\tcf_J_LegLow01_L\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "4\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegKnee_low_s_R\tcf_J_LegLow01_R\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "4\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_Shoulder02_s_L\tcf_J_Shoulder_L\tEuler\tXZY\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "4\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_Shoulder02_s_R\tcf_J_Shoulder_R\tEuler\tXZY\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "4\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmElbo_dam_01_L\tcf_J_ArmLow01_L\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "4\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmElbo_dam_01_R\tcf_J_ArmLow01_R\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",                 
                };

                var _slist = rawList
                    .Select(line => line.Replace("cf_", prefix))
                    .ToList();

                if (_slist.Count == 0)
                {
                    __result = false;
                }
                string[] array = _slist[0].Split(new char[]
                {
                    '\t'
                });
                int num = int.Parse(array[0]);
                if (num > _slist.Count - 1)
                {
                    __result = false;
                }
                __instance.info = new Expression.ScriptInfo[num];
                for (int i = 0; i < num; i++)
                {
                    array = _slist[i + 1].Split(new char[]
                    {
                        '\t'
                    });
                    __instance.info[i] = new Expression.ScriptInfo();
                    __instance.info[i].index = i;
                    int num2 = 0;
                    __instance.info[i].categoryNo = int.Parse(array[num2++]);
                    __instance.info[i].enableLookAt = (array[num2++] == "○");
                    if (__instance.info[i].enableLookAt)
                    {
                        __instance.info[i].lookAt.lookAtName = array[num2++];
                        if ("0" == __instance.info[i].lookAt.lookAtName)
                        {
                            __instance.info[i].lookAt.lookAtName = "";
                        }
                        else
                        {
                            __instance.info[i].elementName = __instance.info[i].lookAt.lookAtName;
                        }
                        __instance.info[i].lookAt.targetName = array[num2++];
                        if ("0" == __instance.info[i].lookAt.targetName)
                        {
                            __instance.info[i].lookAt.targetName = "";
                        }
                        __instance.info[i].lookAt.targetAxisType = (Expression.LookAt.AxisType)Enum.Parse(typeof(Expression.LookAt.AxisType), array[num2++]);
                        __instance.info[i].lookAt.upAxisName = array[num2++];
                        if ("0" == __instance.info[i].lookAt.upAxisName)
                        {
                            __instance.info[i].lookAt.upAxisName = "";
                        }
                        __instance.info[i].lookAt.upAxisType = (Expression.LookAt.AxisType)Enum.Parse(typeof(Expression.LookAt.AxisType), array[num2++]);
                        __instance.info[i].lookAt.sourceAxisType = (Expression.LookAt.AxisType)Enum.Parse(typeof(Expression.LookAt.AxisType), array[num2++]);
                        __instance.info[i].lookAt.limitAxisType = (Expression.LookAt.AxisType)Enum.Parse(typeof(Expression.LookAt.AxisType), array[num2++]);
                        __instance.info[i].lookAt.rotOrder = (Expression.LookAt.RotationOrder)Enum.Parse(typeof(Expression.LookAt.RotationOrder), array[num2++]);
                        __instance.info[i].lookAt.limitMin = float.Parse(array[num2++]);
                        __instance.info[i].lookAt.limitMax = float.Parse(array[num2++]);
                    }
                    else
                    {
                        num2 += 10;
                    }
                    __instance.info[i].enableCorrect = (array[num2++] == "○");
                    if (__instance.info[i].enableCorrect)
                    {
                        __instance.info[i].correct.correctName = array[num2++];
                        if ("0" == __instance.info[i].correct.correctName)
                        {
                            __instance.info[i].correct.correctName = "";
                        }
                        else
                        {
                            __instance.info[i].elementName = __instance.info[i].correct.correctName;
                        }
                        __instance.info[i].correct.referenceName = array[num2++];
                        if ("0" == __instance.info[i].correct.referenceName)
                        {
                            __instance.info[i].correct.referenceName = "";
                        }
                        __instance.info[i].correct.calcType = (Expression.Correct.CalcType)Enum.Parse(typeof(Expression.Correct.CalcType), array[num2++]);
                        __instance.info[i].correct.rotOrder = (Expression.Correct.RotationOrder)Enum.Parse(typeof(Expression.Correct.RotationOrder), array[num2++]);
                        __instance.info[i].correct.charmRate = float.Parse(array[num2++]);
                        __instance.info[i].correct.useRX = (array[num2++] == "○");
                        __instance.info[i].correct.valRXMin = float.Parse(array[num2++]);
                        __instance.info[i].correct.valRXMax = float.Parse(array[num2++]);
                        __instance.info[i].correct.useRY = (array[num2++] == "○");
                        __instance.info[i].correct.valRYMin = float.Parse(array[num2++]);
                        __instance.info[i].correct.valRYMax = float.Parse(array[num2++]);
                        __instance.info[i].correct.useRZ = (array[num2++] == "○");
                        __instance.info[i].correct.valRZMin = float.Parse(array[num2++]);
                        __instance.info[i].correct.valRZMax = float.Parse(array[num2++]);
                    }
                }

                __result = true;

                return false;
            }
        }
#endif
        #endregion
    }

    enum TargetDirection
    {
        X_POS,
        Y_POS,
        Z_POS
    }
#endregion
}
