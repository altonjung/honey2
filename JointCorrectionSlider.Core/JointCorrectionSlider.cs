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

/*
    jointCorrection 관련 코드 조사
        - OCIChar.ChaInfo.Expression 내 8개 사용되는듯
        - Expression 내 아래 LateUpdate() 함수를 통해 각 Expression 항목 업데이트
		private void LateUpdate()
		{
			if (__instance.info == null)
			{
				return;
			}
			if (this.enable)
			{
				Expression.ScriptInfo[] array = __instance.info;
				for (int i = 0; i < array.Length; i++)
				{
					array[i].Update();
				}
			}
		}        
        - 결국 __instance.info 항목 갯수를 늘려 활용 필요
    
        // cf_J_Shoulder_L/cf_J_ArmUp00_L
        // cf_J_Shoulder_R/cf_J_ArmUp00_R

        // category = 0  armup L
        // category = 1  armup R
        // category = 2  Knee L
        // category = 3  Knee R
        // category = 4  armLow L 
        // category = 5  armLow R 
        // category = 6  legup L, siri
        // category = 7  legup R, siri    
    
        확인해야 할 내용
        - Expression 내 public void SetCharaTransform(Transform trf) 호출 부분 확인 필요
        - Expression 내 public void Initialize() 호출 부분 확인 필요
        - Expression 내 public bool LoadSettingSub(List<string> slist) 호출 부분 확인 필요  <- 여기에 shoulder 부분 추가 되면 될듯..
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

        private Rect _windowRect = new Rect(140, 10, 400, 10);
    
        private float _prevLeftShoulder = 0f;
        private float _prevRightShoulder = 0f; 
        private float _prevLeftLeg = 0f;
        private float _prevRightLeg = 0f;
        private float _prevBothLeg = 0f;
        private float _prevLeftArmUp = 0f;
        private float _prevRightArmUp = 0f;
        private float _prevLeftArmDn = 0f;
        private float _prevRightArmDn = 0f;
        private float _prevBothArm = 0f;
        private float _prevLeftAnkle = 0f;
        private float _prevRightAnkle = 0f;
        private float _prevCrouch = 0f;

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
#endif
        internal static ConfigEntry<float> LeftLegConfig { get; private set; }

        internal static ConfigEntry<float> RightLegConfig { get; private set; }

        internal static ConfigEntry<float> BothLegConfig { get; private set; }

        internal static ConfigEntry<float> LeftArmUpperConfig { get; private set; }

        internal static ConfigEntry<float> RightArmUpperConfig { get; private set; }

        internal static ConfigEntry<float> LeftArmLowerConfig { get; private set; }

        internal static ConfigEntry<float> RightArmLowerConfig { get; private set; }

        internal static ConfigEntry<float> BothArmConfig { get; private set; }

        internal static ConfigEntry<float> CrouchConfig { get; private set; }

        internal static ConfigEntry<float> LeftAnkleConfig { get; private set; }

        internal static ConfigEntry<float> RightAnkleConfig { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            string support_type = "Studio";
#if FEATURE_SHOULDER_CORRECTION
            LeftShoulderConfig = Config.Bind(support_type, "Left Shoulder", 0.0f, new ConfigDescription("Left Shoulder", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightShoulderConfig = Config.Bind(support_type, "Right Shoulder", 0.0f, new ConfigDescription("Right Shoulder", new AcceptableValueRange<float>(-1.0f, 1.0f)));
#endif
            LeftLegConfig = Config.Bind(support_type, "Left Leg", 0.0f, new ConfigDescription("Left Leg", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightLegConfig = Config.Bind(support_type, "Right Leg", 0.0f, new ConfigDescription("Right Leg", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            BothLegConfig = Config.Bind(support_type, "Both Leg", 0.0f, new ConfigDescription("Both Leg", new AcceptableValueRange<float>(-1.0f, 1.0f)));

            LeftArmUpperConfig = Config.Bind(support_type, "Left Arm Upper", 0.0f, new ConfigDescription("Left Arm Upper", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightArmUpperConfig = Config.Bind(support_type, "Right Arm Upper", 0.0f, new ConfigDescription("Right Arm Upper", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            LeftArmLowerConfig = Config.Bind(support_type, "Left Arm Lower", 0.0f, new ConfigDescription("Left Arm Lower", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightArmLowerConfig = Config.Bind(support_type, "Right Arm Lower", 0.0f, new ConfigDescription("Right Arm Lower", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            BothArmConfig = Config.Bind(support_type, "Both Arm", 0.0f, new ConfigDescription("Both Arm", new AcceptableValueRange<float>(-1.0f, 1.0f)));

            CrouchConfig = Config.Bind(support_type, "Crouch", 0.0f, new ConfigDescription("Crouch", new AcceptableValueRange<float>(-1.0f, 1.0f)));

            LeftAnkleConfig = Config.Bind(support_type, "Left Ankle", 0.0f, new ConfigDescription("Left Ankle", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightAnkleConfig = Config.Bind(support_type, "Right Ankle", 0.0f, new ConfigDescription("Right Ankle", new AcceptableValueRange<float>(-1.0f, 1.0f)));

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

        protected override void Update()
        {
            if (_loaded == false)
                return;
        }

        private void SetScriptInfo(OCIChar ociChar, int categoryId, float value)
        {
            foreach (Expression.ScriptInfo scriptInfo in ociChar.charInfo.expression.info)
            {   
                if (scriptInfo.categoryNo == categoryId) {
                    // scriptInfo.correct.charmRate = value;
                    
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

        protected override void LateUpdate()
        {
            if (_currentOCIChar == null)
                return;
#if FEATURE_SHOULDER_CORRECTION
            if (LeftShoulderConfig.Value != _prevLeftShoulder)
            {
                _prevLeftShoulder = LeftShoulderConfig.Value;
                SetScriptInfo(_currentOCIChar, 8, LeftShoulderConfig.Value);
            }
            if (RightShoulderConfig.Value != _prevRightShoulder)
            {
                _prevRightShoulder = RightShoulderConfig.Value;
                SetScriptInfo(_currentOCIChar, 9, RightShoulderConfig.Value);
            } 
#endif
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
            if (BothArmConfig.Value != _prevBothArm)
            {
                _prevBothArm = BothArmConfig.Value;
                SetScriptInfo(_currentOCIChar, 0, BothArmConfig.Value);
                SetScriptInfo(_currentOCIChar, 4, BothArmConfig.Value);
                SetScriptInfo(_currentOCIChar, 1, BothArmConfig.Value);
                SetScriptInfo(_currentOCIChar, 5, BothArmConfig.Value);
            }
            if (LeftLegConfig.Value != _prevLeftLeg)
            {
                _prevLeftLeg = LeftLegConfig.Value;
                SetScriptInfo(_currentOCIChar, 2, LeftLegConfig.Value);
                SetScriptInfo(_currentOCIChar, 6, LeftLegConfig.Value);
            }
            if (RightLegConfig.Value != _prevRightLeg)
            {
                _prevRightLeg = RightLegConfig.Value;
                SetScriptInfo(_currentOCIChar, 3, RightLegConfig.Value);
                SetScriptInfo(_currentOCIChar, 7, RightLegConfig.Value);
            }
            if (BothLegConfig.Value != _prevBothLeg)
            {
                _prevBothLeg = BothLegConfig.Value;
                SetScriptInfo(_currentOCIChar, 2, BothLegConfig.Value);
                SetScriptInfo(_currentOCIChar, 6, BothLegConfig.Value);
                SetScriptInfo(_currentOCIChar, 3, BothLegConfig.Value);
                SetScriptInfo(_currentOCIChar, 7, BothLegConfig.Value);
            }
            if (CrouchConfig.Value != _prevCrouch)
            {
                _prevCrouch = CrouchConfig.Value;
                SetScriptInfo(_currentOCIChar, 6, CrouchConfig.Value);
                SetScriptInfo(_currentOCIChar, 7, CrouchConfig.Value);
            }
        }

        private void InitConfig()
        {
#if FEATURE_SHOULDER_CORRECTION
            LeftShoulderConfig.Value = (float)LeftShoulderConfig.DefaultValue;
            RightShoulderConfig.Value = (float)RightShoulderConfig.DefaultValue;
#endif
            LeftLegConfig.Value = (float)LeftLegConfig.DefaultValue;
            RightLegConfig.Value = (float)RightLegConfig.DefaultValue;
            BothLegConfig.Value = (float)BothLegConfig.DefaultValue;

            LeftArmUpperConfig.Value = (float)LeftArmUpperConfig.DefaultValue;            
            RightArmUpperConfig.Value = (float)RightArmUpperConfig.DefaultValue;
            LeftArmLowerConfig.Value = (float)LeftArmLowerConfig.DefaultValue;            
            RightArmLowerConfig.Value = (float)RightArmLowerConfig.DefaultValue;
            BothArmConfig.Value = (float)BothArmConfig.DefaultValue;

            LeftAnkleConfig.Value = (float)LeftAnkleConfig.DefaultValue;
            RightAnkleConfig.Value = (float)RightAnkleConfig.DefaultValue;

            CrouchConfig.Value = (float)CrouchConfig.DefaultValue;
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
            GUILayout.Label("<color=yellow>Shoulder</color>", RichLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("LeftShoulder", "LeftArm"), GUILayout.Width(80));
            LeftShoulderConfig.Value = GUILayout.HorizontalSlider(LeftShoulderConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftShoulderConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("LeftShoulder", "LeftArm"), GUILayout.Width(80));
            RightShoulderConfig.Value = GUILayout.HorizontalSlider(RightShoulderConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightShoulderConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
#endif
            // Top
            GUILayout.Label("<color=yellow>Arm-Upper</color>", RichLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("LeftArm(Up)", "LeftArm"), GUILayout.Width(80));
            LeftArmUpperConfig.Value = GUILayout.HorizontalSlider(LeftArmUpperConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftArmUpperConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("RightArm(Up)", "RightArm"), GUILayout.Width(80));
            RightArmUpperConfig.Value = GUILayout.HorizontalSlider(RightArmUpperConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightArmUpperConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.Label("<color=yellow>Arm-Lower</color>", RichLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("LeftArm(Dn)", "LeftArm"), GUILayout.Width(80));
            LeftArmLowerConfig.Value = GUILayout.HorizontalSlider(LeftArmLowerConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftArmLowerConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("RightArm(Dn)", "RightArm"), GUILayout.Width(80));
            RightArmLowerConfig.Value = GUILayout.HorizontalSlider(RightArmLowerConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightArmLowerConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("<color=green>BothArm</color>", "BothArm"), _richLabel, GUILayout.Width(80));
            BothArmConfig.Value = GUILayout.HorizontalSlider(BothArmConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(BothArmConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            // Bottom
            GUILayout.Label("<color=yellow>Bottom</color>", RichLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("LeftLeg", "LeftLeg"), GUILayout.Width(80));
            LeftLegConfig.Value = GUILayout.HorizontalSlider(LeftLegConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftLegConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("RightLeg", "RightLeg"), GUILayout.Width(80));
            RightLegConfig.Value = GUILayout.HorizontalSlider(RightLegConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightLegConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();            
            GUILayout.Label(new GUIContent("<color=green>BothLeg</color>", "BothLeg"), _richLabel, GUILayout.Width(80));
            BothLegConfig.Value = GUILayout.HorizontalSlider(BothLegConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(BothLegConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("<color=blue>Crouch</color>", "Crouch"), _richLabel, GUILayout.Width(80));
            CrouchConfig.Value = GUILayout.HorizontalSlider(CrouchConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(CrouchConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            // Ankle
            GUILayout.Label("<color=yellow>Ankle</color>", RichLabel);            
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("LeftAnkle", "LeftAnkle"), GUILayout.Width(80));
            LeftAnkleConfig.Value = GUILayout.HorizontalSlider(LeftAnkleConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftAnkleConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("RightAnkle", "RightAnkle"), GUILayout.Width(80));
            RightAnkleConfig.Value = GUILayout.HorizontalSlider(RightAnkleConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightAnkleConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

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

                    foreach (Expression.ScriptInfo scriptInfo in ociChar.charInfo.expression.info)
                    {
                        UnityEngine.Debug.Log($"scriptInfo: {scriptInfo.categoryNo}");
                    }
                }

                return true;
            }
        }

#if FEATURE_SHOULDER_CORRECTION
        [HarmonyPatch(typeof(CharaUtils.Expression), nameof(Expression.LoadSettingSub), typeof(List<string>))]
        internal static class Expression_LoadSettingSub_Patches
        {
            private static bool Prefix(Expression __instance, List<string> slist, ref bool __result)
            {
                // UnityEngine.Debug.Log(Environment.StackTrace);

                // 아래는 총 28개
                var _slist = new List<string>
                {
                "28\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0",
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
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_Shoulder02_s_L\tcf_J_Shoulder02_s_L\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t×\t0\t0",
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_Shoulder02_s_R\tcf_J_Shoulder02_s_R\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t×\t0\t0",
                };

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

                // UnityEngine.Debug.Log(Environment.StackTrace);
                // 필요하면 결과 변경 가능
                __result = true;

                return false;
            }
        }

        [HarmonyPatch(typeof(ChaControl), "InitializeExpression", typeof(int), typeof(bool))]
        private static class ChaControl_InitializeExpression_Patches
        {
            private static bool Prefix(ChaControl __instance, int sex, bool _enable, ref bool __result)
            {
                // UnityEngine.Debug.Log(Environment.StackTrace);
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
                    9
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
#endif
        #endregion
    }
#endregion
}
