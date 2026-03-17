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

        // internal ObjectCtrlInfo _selectedOCI;

        private AssetBundle _bundle;

        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;
		
        private const int _uniqueId = ('J' << 24) | ('T' << 16) | ('C' << 8) | 'S';

        private Rect _windowRect = new Rect(140, 10, 400, 10);
    
        private float _prevLeftLeg = 0f;
        private float _prevRightLeg = 0f;
        private float _prevBothLeg = 0f;
        private float _prevLeftArm = 0f;
        private float _prevRightArm = 0f;
        private float _prevBothArm = 0f;
        private float _prevLeftAnkle = 0f;
        private float _prevRightAnkle = 0f;
        private float _prevCrouch = 0f;
        private OCIChar _currentOCIChar = null;

        // Config


        #region Accessors
        internal static ConfigEntry<float> LeftLegConfig { get; private set; }

        internal static ConfigEntry<float> RightLegConfig { get; private set; }

        internal static ConfigEntry<float> BothLegConfig { get; private set; }

        internal static ConfigEntry<float> LeftArmConfig { get; private set; }

        internal static ConfigEntry<float> RightArmConfig { get; private set; }

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


            LeftLegConfig = Config.Bind(support_type, "Left Leg", 0.0f, new ConfigDescription("Left Leg", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightLegConfig = Config.Bind(support_type, "Right Leg", 0.0f, new ConfigDescription("Right Leg", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            BothLegConfig = Config.Bind(support_type, "Both Leg", 0.0f, new ConfigDescription("Both Leg", new AcceptableValueRange<float>(-1.0f, 1.0f)));

            LeftArmConfig = Config.Bind(support_type, "Left Arm", 0.0f, new ConfigDescription("Left Arm", new AcceptableValueRange<float>(-1.0f, 1.0f)));
            RightArmConfig = Config.Bind(support_type, "Right Arm", 0.0f, new ConfigDescription("Right Arm", new AcceptableValueRange<float>(-1.0f, 1.0f)));
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
                    scriptInfo.correct.charmRate = value;
                    scriptInfo.correct.valRXMin = value;
                    scriptInfo.correct.valRXMax = value;
                    scriptInfo.correct.valRYMin = value;
                    scriptInfo.correct.valRYMax = value;
                    scriptInfo.correct.valRZMin = value;
                    scriptInfo.correct.valRZMax = value;
                }
            } 
        }

        protected override void LateUpdate()
        {
            if (_currentOCIChar == null)
                return;

            // if (LeftLegConfig.Value != _prevLeftLeg)
            // {
            //     _prevLeftLeg = LeftLegConfig.Value;
            //     if (_currentOCIChar != null)
            //     {
            //         foreach (Expression.ScriptInfo scriptInfo in _currentOCIChar.charInfo.expression.info)
            //         {
            //             float strength = LeftLegConfig.Value;

            //             scriptInfo.correct.charmRate = strength;
            //             scriptInfo.correct.valRXMin = strength;
            //             scriptInfo.correct.valRXMax = strength;
            //             scriptInfo.correct.valRYMin = strength;
            //             scriptInfo.correct.valRYMax = strength;
            //             scriptInfo.correct.valRZMin = strength;
            //             scriptInfo.correct.valRZMax = strength;

            //             // scriptInfo.correct.Update();
            //             scriptInfo.enable = true; 
            //             UnityEngine.Debug.Log($">> scriptInfo correctName  {scriptInfo.categoryNo}, {scriptInfo.index}, {scriptInfo.correct.correctName}, {scriptInfo.enableCorrect}");
            //             // UnityEngine.Debug.Log($">> scriptInfo {scriptInfo.correct.charmRate}");
            //         }

                    
            //     }
            // }
            if (LeftArmConfig.Value != _prevLeftArm)
            {
                _prevLeftArm = LeftArmConfig.Value;
                SetScriptInfo(_currentOCIChar, 0, LeftArmConfig.Value);
            }
            if (RightArmConfig.Value != _prevRightArm)
            {
                _prevRightArm = RightArmConfig.Value;
                SetScriptInfo(_currentOCIChar, 1, RightArmConfig.Value);
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
            // category = 0  armup L
            // category = 1  armup R
            // category = 2  Knee L
            // category = 3  Knee R
            // category = 4  armLow L 
            // category = 5  armLow R 
            // category = 6  legup L, siri
            // category = 7  legup R, siri
        }

        private void InitConfig()
        {
            LeftLegConfig.Value = (float)LeftLegConfig.DefaultValue;
            RightLegConfig.Value = (float)RightLegConfig.DefaultValue;
            BothLegConfig.Value = (float)BothLegConfig.DefaultValue;

            LeftArmConfig.Value = (float)LeftArmConfig.DefaultValue;            
            RightArmConfig.Value = (float)RightArmConfig.DefaultValue;
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
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "RealHuman" + Version);
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
            // Top
            GUILayout.Label("Top");
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("LeftArm", "LeftArm"), GUILayout.Width(80));
            LeftArmConfig.Value = GUILayout.HorizontalSlider(LeftArmConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(LeftArmConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("RightArm", "RightArm"), GUILayout.Width(80));
            RightArmConfig.Value = GUILayout.HorizontalSlider(RightArmConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(RightArmConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("BothArm", "BothArm"), GUILayout.Width(80));
            BothArmConfig.Value = GUILayout.HorizontalSlider(BothArmConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(BothArmConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            draw_seperate();  
            // Bottom
            GUILayout.Label("Bottom");
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
            GUILayout.Label(new GUIContent("BothLeg", "BothLeg"), GUILayout.Width(80));
            BothLegConfig.Value = GUILayout.HorizontalSlider(BothLegConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(BothLegConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();

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
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Crouch", "Crouch"), GUILayout.Width(80));
            CrouchConfig.Value = GUILayout.HorizontalSlider(CrouchConfig.Value, -1.0f, 1.0f);
            GUILayout.Label(CrouchConfig.Value.ToString("0.00"), GUILayout.Width(30));
            GUILayout.EndHorizontal();


            if (GUILayout.Button("Default"))
                InitConfig();

            if (GUILayout.Button("Close"))
                _ShowUI = false;


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
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(0.5f));
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

                }

                return true;
            }
        }

//  포즈 변경 시 마다
        // [HarmonyPatch(typeof(PauseCtrl.FileInfo), "Apply", typeof(OCIChar))]
        // private static class PauseCtrl_Apply_Patches
        // {
        //     private static bool Prefix(PauseCtrl.FileInfo __instance, OCIChar _char)
        //     {
        //         if (_char != null)
        //         {
        //             var controller = _char.GetChaControl().GetComponent<JointCorrectionSliderController>();
        //             if (controller != null)
        //             {                     
        //                 controller.SetHairDown();
        //             }
        //         }
        //         return true;
        //     }
        // }
        #endregion
    }
#endregion
}
