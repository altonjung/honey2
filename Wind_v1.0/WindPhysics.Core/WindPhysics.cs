using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using TimelineCompatibility = ToolBox.TimelineCompatibility;
using ToolBox;
using ToolBox.Extensions;
using UnityEngine;
using UnityEngine.SceneManagement;

#if IPA
using Harmony;
using IllusionPlugin;
#elif BEPINEX
using BepInEx;
using HarmonyLib;
#endif
#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using IllusionUtility.GetUtility;
using KKAPI.Studio;
using KKAPI.Studio.UI.Toolbars;
using KKAPI.Utilities;
using static Illusion.Utils;
#endif
using KKAPI.Studio;
using KKAPI.Studio.UI.Toolbars;
using KKAPI.Utilities;
using KKAPI.Chara;
using System.Linq;

/*
    Agent implementation notes

    Purpose:
    - Provide wind effects to cloth components (tops/bottoms) and dynamic-bone targets such as hair
      for the currently active character.

    Terms:
    - OCIChar: character
      > The currently active character in the scene is obtained through GetCurrentOCI().

    Minimum requirements:
    1) Build the following UI in OnGUI:
       1.1) Editing controls for wind attributes (gravity, direction, force, etc.)
       1.2) Editing controls for cloth attributes
       1.3) Editing controls for hair (dynamic bone) attributes
       1.4) Editing controls for accessories (dynamic bone) attributes
    2) Character-specific values must be stored and managed in WindData (WindPhysicsController.cs).

    Wind attributes:
    - gravity: -0.1 to 0.0
    - direction: 0 to 360 degrees
    - force/force up: increases Z-axis intensity; force up increases Y-axis intensity

    Additional requirements:
    - N/A

    Current issues:
    - N/A
*/
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
        public const string Version = "1.0.0";
        public const string GUID = "com.alton.illusionplugins.windphysics";
        internal const string _ownerId = "Alton";
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

        private static string _assemblyLocation;
        internal bool _loaded = false;

#if !FEATURE_PUBLIC
        // LineRenderer object
        private GameObject _windDirObj;
        private LineRenderer _windDirLine;

        // Visible-time control
        private float _lastWindDirValue = float.NaN;
        private float _windDirVisibleUntil = 0f;
        private const float _windDirVisibleDuration = 2.0f;

        // Display position/length
        private const float _windDirLength = 5f;        
#endif

        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;

        private const int _uniqueId = ('W' << 24) | ('P' << 16) | ('P' << 8) | 'X';

        private Rect _windowRect = new Rect(70, 10, 400, 10);

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

        #endregion

        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            _self = this;
            Logger = base.Logger; 

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmony = HarmonyExtensions.CreateInstance(GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());


            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open WindPhysics window",
                () => ResourceUtils.GetEmbeddedResource("wp_toolbar_icon.png", typeof(WindPhysics).Assembly).LoadTexture(),
                false, this, val => _ShowUI = val);
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);

            CharacterApi.RegisterExtraBehaviour<WindPhysicsController>(GUID);

            // _CheckWindMgmtCoroutine = StartCoroutine(CheckWindMgmtRoutine());

#if FEATURE_TIMELINE_SUPPORT
            this.ExecuteDelayed2(() =>
            {
                TimelineCompatibility.Init();
                TimelineCompatibilityWindPhysics.Populate();
            }, 3);
#endif

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

#if !FEATURE_PUBLIC
            UpdateWindDirectionLine();
#endif
        }

        protected override void OnGUI()
        {
            if (_loaded == false)
                return;

            if (StudioAPI.InsideStudio) {            
                if (_ShowUI == false)             
                    return;

                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "Wind Physics " + Version);
            }
        }       

        private OCIChar GetCurrentOCI()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null)
                return null;

            TreeNodeObject node = Studio.Studio.Instance.treeNodeCtrl.selectNodes
                .LastOrDefault();            
                
            return  node != null ? Studio.Studio.GetCtrlInfo(node) as OCIChar : null;
        }        

        private WindData GetCurrentData()
        {
            OCIChar curOciChar = GetCurrentOCI();
            if (curOciChar != null && curOciChar.GetChaControl() != null) {
                var controller = curOciChar.GetChaControl().GetComponent<WindPhysicsController>();
                if (controller == null)
                    return null;

                WindData data = controller.GetData();
                return data;
            }

            return null;
        }    

        private WindPhysicsController GetCurrentControl()
        {
            OCIChar curOciChar = GetCurrentOCI();
            if (curOciChar != null && curOciChar.GetChaControl() != null) {
                return curOciChar.GetChaControl().GetComponent<WindPhysicsController>();         
            }
             
            return null;   
        }   

        private void WindowFunc(int id)
        {
            var studio = Studio.Studio.Instance;
            // Always restore default camera control behavior first.
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

            var controller = GetCurrentControl();
            WindData data = null;
            if (controller != null)
            {
                data = controller.GetData();
                if (data == null)
                {
                    OCIChar curOciChar = GetCurrentOCI();
                    if (curOciChar != null && curOciChar.GetChaControl() != null)
                        data = controller.CreateWindData(curOciChar.GetChaControl());
                }
            }

            if (data != null)
            {
                // ================= UI =================
#if !FEATURE_PUBLIC                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("park"))
                    ApplyScenePreset(data, "park");
                if (GUILayout.Button("hill"))
                    ApplyScenePreset(data, "hill");
                if (GUILayout.Button("inWater"))
                    ApplyScenePreset(data, "inWater");                    
                if (GUILayout.Button("inSpace"))
                    ApplyScenePreset(data, "inSpace");
                GUILayout.EndHorizontal();
#endif

    // Wind
                GUILayout.Label("<color=orange>Wind</color>", RichLabel);
                // Direction
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Direction", "Wind Direction"), GUILayout.Width(80));
                data.WindDirection = GUILayout.HorizontalSlider(data.WindDirection, 0.0f, 359.0f);
                GUILayout.Label(data.WindDirection.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                // Interval
                GUILayout.BeginHorizontal();            
                GUILayout.Label(new GUIContent("Interval", "Wind Interval"), GUILayout.Width(80));
                data.WindInterval = GUILayout.HorizontalSlider(data.WindInterval, 0.0f, 60.0f);
                GUILayout.Label(data.WindInterval.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();            

                // Keep
                ClampWindKeepTimeToInterval(data);
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Keep", "Wind Keep Time"), GUILayout.Width(80));
                float keepMin = 0.1f;
                if (data.WindInterval <= keepMin)
                {
                    data.WindKeepTime = keepMin;
                    GUI.enabled = false;
                    GUILayout.HorizontalSlider(data.WindKeepTime, keepMin, keepMin + 0.001f);
                    GUI.enabled = true;
                }
                else
                {
                    data.WindKeepTime = GUILayout.HorizontalSlider(data.WindKeepTime, keepMin, data.WindInterval);
                }
                GUILayout.Label(data.WindKeepTime.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                // Amplitude
                GUILayout.BeginHorizontal(); 
                GUILayout.Label(new GUIContent("Amplitude", "Wind Amplitude"),  GUILayout.Width(80));
                data.WindAmplitude = GUILayout.HorizontalSlider(data.WindAmplitude, 0.0f, 10.0f);
                GUILayout.Label(data.WindAmplitude.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                // Force
                GUILayout.BeginHorizontal();             
                GUILayout.Label(new GUIContent("Force", "Wind Force"), GUILayout.Width(80));
                data.WindForce = GUILayout.HorizontalSlider(data.WindForce, 0.0f, 1.0f);
                GUILayout.Label(data.WindForce.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                // Force up
                GUILayout.BeginHorizontal();              
                GUILayout.Label(new GUIContent("Force Up", "Wind ForceUp"),  GUILayout.Width(80));
                data.WindUpForce = GUILayout.HorizontalSlider(data.WindUpForce, 0.0f, 1.0f);
                GUILayout.Label(data.WindUpForce.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal(); 

                // Gravity
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Gravity", "Gravity"), GUILayout.Width(80));
                data.Gravity = GUILayout.HorizontalSlider(data.Gravity, -0.1f, 0.05f);
                GUILayout.Label(data.Gravity.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

    // Hair
                GUILayout.Label("<color=orange>Hair</color>", RichLabel);
                GUILayout.BeginHorizontal();            
                GUILayout.Label(new GUIContent("Elastic", "Elastic"), GUILayout.Width(80));
                data.HairElastic = GUILayout.HorizontalSlider(data.HairElastic, 0.0f, 1.0f);
                GUILayout.Label(data.HairElastic.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();        
    // Accessory
                GUILayout.Label("<color=orange>Accessory</color>", RichLabel);;
                GUILayout.BeginHorizontal();
            
                GUILayout.Label(new GUIContent("Elastic", "Elastic"), GUILayout.Width(80));
                data.AccesoriesElastic = GUILayout.HorizontalSlider(data.AccesoriesElastic, 0.0f, 1.0f);
                GUILayout.Label(data.AccesoriesElastic.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();
    // Cloth
                GUILayout.Label("<color=orange>Cloth</color>", RichLabel);
                GUILayout.BeginHorizontal();        
                GUILayout.Label(new GUIContent("Damping", "Damping"), GUILayout.Width(80));
                data.ClothDamping = GUILayout.HorizontalSlider(data.ClothDamping, 0.0f, 1.0f);
                GUILayout.Label(data.ClothDamping.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();        
                GUILayout.Label(new GUIContent("Stiffness", "Stiffness"), GUILayout.Width(80));
                data.ClothStiffness = GUILayout.HorizontalSlider(data.ClothStiffness, 0.0f, 10.0f);
                GUILayout.Label(data.ClothStiffness.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                GUILayout.Space(10);                

                GUILayout.BeginHorizontal();
                if (data.enabled == true && data.coroutine != null)
                {
                    if (GUILayout.Button("Deactive")) {
                        data.wind_status = Status.STOP;
                        data.enabled = false;
                    }    
                } 
                else
                {
                    if (GUILayout.Button("Active")) {
                        OCIChar curOciChar = GetCurrentOCI();
                        if (controller != null && curOciChar != null && curOciChar.GetChaControl() != null)
                        {
                            controller.ExecuteWindEffect(curOciChar.GetChaControl());
                            data.enabled = true;
                        }
                    }
                }

                if(GUILayout.Button("Default"))
                {
                    InitConfig();   
                }
                
                GUILayout.EndHorizontal();                
            } 
            else
            {
                GUILayout.Label("<color=white>Nothing to select</color>", RichLabel);
            }         

            if (GUILayout.Button("Close")) {
                Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
                _ShowUI = false;
            }

            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 150, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }

#if !FEATURE_PUBLIC
        private void EnsureWindDirectionLine()
        {
            if (_windDirObj != null)
                return;

            _windDirObj = new GameObject("WindPhysics_WindDirection");
            _windDirLine = _windDirObj.AddComponent<LineRenderer>();
            _windDirLine.positionCount = 2;
            _windDirLine.startWidth = 0.3f;
            _windDirLine.endWidth = 0.1f;
            _windDirLine.useWorldSpace = true;
            _windDirLine.material = new Material(Shader.Find("Sprites/Default"));
            _windDirLine.startColor = new Color(0.2f, 0.8f, 1.0f, 0.9f);
            _windDirLine.endColor = new Color(0.2f, 0.8f, 1.0f, 0.9f);
        }

        private void UpdateWindDirectionLine()
        {
            if (!_loaded)
                return;

            if (_windDirLine == null)
                return;

            // Only show the direction helper while plugin UI is open.
            if (!_ShowUI || !StudioAPI.InsideStudio)
            {
                _windDirLine.enabled = false;
                return;
            }

            WindData data = GetCurrentData();
            if (data == null)
            {
                _windDirLine.enabled = false;
                return;
            }

            // Detect direction changes.
            float current = data.WindDirection;
            if (!Mathf.Approximately(current, _lastWindDirValue))
            {
                _lastWindDirValue = current;
                _windDirVisibleUntil = Time.time + _windDirVisibleDuration;
            }

            // Decide whether the helper should be visible.
            bool visible = Time.time <= _windDirVisibleUntil;
            _windDirLine.enabled = visible;
            if (!visible)
                return;

            Vector3 origin = data.head_bone.position
                + Vector3.up * 0.2f
                - data.head_bone.forward * 0.5f;

            float rad = (current + 180f) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

            _windDirLine.SetPosition(0, origin);
            _windDirLine.SetPosition(1, origin + dir * _windDirLength);

        }
#endif
        
#endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            _loaded = true;

#if !FEATURE_PUBLIC
            EnsureWindDirectionLine();
#endif
        }

        private void InitConfig()
        {
            var controller = GetCurrentControl();
            if (controller != null)
            {
                controller.ResetWindData();
            }
        }

        private void ApplyScenePreset(WindData data, string preset)
        {
            if (data == null)
                return;

            if (preset == "park")
            {
                data.WindForce = 0.3f;
                data.WindUpForce = 0.0f;
                data.WindAmplitude = 1.0f;
                data.Gravity = -0.01f;
            }
            else if (preset == "hill")
            {
                data.WindForce = 0.8f;
                data.WindUpForce = 0.01f;
                data.WindAmplitude = 3.0f;
                data.Gravity = -0.01f;
            }
            else if (preset == "inWater")
            {
                data.WindForce = 0.3f;
                data.WindUpForce = 0.02f;
                data.WindAmplitude = 2.0f;
                data.Gravity = 0.01f;
            }
            else if (preset == "inSpace")
            {
                data.WindForce = 0.3f;
                data.WindUpForce = 0.02f;
                data.WindAmplitude = 0.0f;
                data.Gravity = 0.025f;
            }

            data.Gravity = Mathf.Clamp(data.Gravity, -0.1f, 0.05f);
            ClampWindKeepTimeToInterval(data);
        }

        private void SceneInit()
        {
            Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
			_ShowUI = false;
        }

        internal static void ClampWindKeepTimeToInterval(WindData data)
        {
            if (data == null)
                return;

            float interval = Mathf.Clamp(data.WindInterval, 0.0f, 60f);
            if (!Mathf.Approximately(interval, data.WindInterval))
                data.WindInterval = interval;

            float keepMax = Mathf.Max(0.1f, interval);
            float keep = Mathf.Clamp(data.WindKeepTime, 0.1f, keepMax);
            if (!Mathf.Approximately(keep, data.WindKeepTime))
                data.WindKeepTime = keep;
        }

        #endregion

        #region Patches
        // Delay is required because cloth assignment is not immediate.
        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                ChaControl chaControl = __instance.GetChaControl();

                if (chaControl != null)
                {                 
                    var controller = chaControl.GetComponent<WindPhysicsController>();
                    if (controller != null)
                    {                  
                        WindData windData = controller.GetData();                        
                        if (windData != null) {
                            windData.wind_status = Status.STOP;
                        }
                    }    
                }
            }
        }

        [HarmonyPatch(typeof(ChaControl), "ChangeClothesAsync", typeof(int), typeof(int), typeof(bool), typeof(bool))]
        private static class ChaControl_ChangeClothesAsync_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange = false, bool asyncFlags = true)
            {
                ChaControl chaControl = __instance as ChaControl;
                if (chaControl != null)
                {                 
                    var controller = chaControl.GetComponent<WindPhysicsController>();
                    if (controller != null)
                    {                  
                        WindData windData = controller.GetData();                        
                        if (windData != null) {
                            windData.wind_status = Status.STOP;
                        }
                    }    
                }
            }
        }

        // Accessory change
        [HarmonyPatch(typeof(ChaControl), "ChangeAccessoryAsync", typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool), typeof(bool))]
        private static class ChaControl_ChangeAccessoryAsync_Patches
        {
            private static void Postfix(ChaControl __instance, int slotNo, int type, int id, string parentKey, bool forceChange, bool asyncFlags = true)
            {
                ChaControl chaControl = __instance as ChaControl;
                if (chaControl != null)
                {                 
                    var controller = chaControl.GetComponent<WindPhysicsController>();
                    if (controller != null)
                    {                  
                        WindData windData = controller.GetData();
                        if (windData != null) {
                            windData.wind_status = Status.STOP;
                        }
                    }    
                }
            }
        }

        // Hair change
        [HarmonyPatch(typeof(ChaControl), "ChangeHairAsync", typeof(int), typeof(int), typeof(bool), typeof(bool))]
        private static class ChaControl_ChangeHairAsync_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange = false, bool asyncFlags = true)
            {
                ChaControl chaControl = __instance as ChaControl;
                if (chaControl != null)
                {                 
                    var controller = chaControl.GetComponent<WindPhysicsController>();
                    if (controller != null)
                    {                  
                        WindData windData = controller.GetData();                        
                        if (windData != null) {
                            windData.wind_status = Status.STOP;
                        }
                    }    
                }
            }
        }        
        
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {
                ChaControl chaControl = __instance as ChaControl;
                if (chaControl != null)
                {                 
                    var controller = chaControl.GetComponent<WindPhysicsController>();
                    if (controller != null)
                    {                  
                        WindData windData = controller.GetData();                        
                        if (windData != null) {
                            windData.wind_status = Status.STOP;
                        }
                    }
                }
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

        #endregion

#if FEATURE_TIMELINE_SUPPORT
        #region Timeline Compatibility
        internal static class TimelineCompatibilityWindPhysics
        {
            public static void Populate()
            {
                ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
                    owner: WindPhysics.Name,
                    id: "windEnable",
                    name: "Enabled",
                    interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
                    {
                        bool value = (bool)leftValue;

                        OCIChar ociChar =  oci as OCIChar;

                        if (ociChar != null)
                        {
                            var controller = ociChar.GetChaControl().GetComponent<WindPhysicsController>();
                    
                            if (controller != null)
                            {                 
                                WindData winData = controller.GetData(); 

                                if(winData != null && winData.enabled != value) {
                                    winData.enabled = value;
                                    controller.ExecuteWindEffect(ociChar.GetChaControl());   
                                }
                            }
                        }
                    },
                    interpolateAfter: null,
                    isCompatibleWithTarget: (oci) => oci is OCIChar,
                    getValue: (oci, parameter) => {
                        OCIChar ociChar =  oci as OCIChar;

                        if (ociChar != null)
                        {
                            var controller = ociChar.GetChaControl().GetComponent<WindPhysicsController>();
                    
                            if (controller != null)
                            {                 
                                WindData winData = controller.GetData(); 
                                if (winData != null)
                                    return winData.enabled;
                            }
                        }

                        return false;                        
                    },
                    readValueFromXml: (parameter, node) => node.ReadBool("value"),
                    writeValueToXml: (parameter, writer, o) => writer.WriteValue("value", (bool)o),
                    getParameter: GetParameter,
                    readParameterFromXml: null,
                    writeParameterToXml: null,
                    checkIntegrity: CheckIntegrity
                );
                ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
                    owner: WindPhysics.Name,
                    id: "windFlow",
                    name: "Flow",
                    interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
                    {
                        float value = (float)leftValue;
                        OCIChar ociChar = oci as OCIChar;
                        if (ociChar == null || ociChar.GetChaControl() == null)
                            return;

                        var controller = ociChar.GetChaControl().GetComponent<WindPhysicsController>();
                        if (controller == null)
                            return;

                        WindData windData = controller.GetData() ?? controller.CreateWindData(ociChar.GetChaControl());
                        if (windData != null)
                            windData.WindDirection = value;
                    },
                    interpolateAfter: null,
                    isCompatibleWithTarget: (oci) => oci is OCIChar,
                    getValue: (oci, parameter) =>
                    {
                        OCIChar ociChar = oci as OCIChar;
                        if (ociChar == null || ociChar.GetChaControl() == null)
                            return 0f;

                        var controller = ociChar.GetChaControl().GetComponent<WindPhysicsController>();
                        if (controller == null)
                            return 0f;

                        WindData windData = controller.GetData();
                        return windData != null ? windData.WindDirection : 0f;
                    },
                    readValueFromXml: (parameter, node) => node.ReadFloat("value"),
                    writeValueToXml: (parameter, writer, o) => writer.WriteValue("value", (float)o),
                    getParameter: GetParameter,
                    readParameterFromXml: null,
                    writeParameterToXml: null,
                    checkIntegrity: CheckIntegrity
                );
                ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
                    owner: WindPhysics.Name,
                    id: "windInterval",
                    name: "Interval",
                    interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
                    {
                        float value = (float)leftValue;
                        OCIChar ociChar = oci as OCIChar;
                        if (ociChar == null || ociChar.GetChaControl() == null)
                            return;

                        var controller = ociChar.GetChaControl().GetComponent<WindPhysicsController>();
                        if (controller == null)
                            return;

                        WindData windData = controller.GetData() ?? controller.CreateWindData(ociChar.GetChaControl());
                        if (windData != null)
                        {
                            windData.WindInterval = value;
                            WindPhysics.ClampWindKeepTimeToInterval(windData);
                        }
                    },
                    interpolateAfter: null,
                    isCompatibleWithTarget: (oci) => oci is OCIChar,
                    getValue: (oci, parameter) =>
                    {
                        OCIChar ociChar = oci as OCIChar;
                        if (ociChar == null || ociChar.GetChaControl() == null)
                            return 2f;

                        var controller = ociChar.GetChaControl().GetComponent<WindPhysicsController>();
                        if (controller == null)
                            return 2f;

                        WindData windData = controller.GetData();
                        return windData != null ? windData.WindInterval : 2f;
                    },
                    readValueFromXml: (parameter, node) => node.ReadFloat("value"),
                    writeValueToXml: (parameter, writer, o) => writer.WriteValue("value", (float)o),
                    getParameter: GetParameter,
                    readParameterFromXml: null,
                    writeParameterToXml: null,
                    checkIntegrity: CheckIntegrity
                );
                ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
                    owner: WindPhysics.Name,
                    id: "windForce",
                    name: "Force",
                    interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
                    {
                        float value = (float)leftValue;
                        OCIChar ociChar = oci as OCIChar;
                        if (ociChar == null || ociChar.GetChaControl() == null)
                            return;

                        var controller = ociChar.GetChaControl().GetComponent<WindPhysicsController>();
                        if (controller == null)
                            return;

                        WindData windData = controller.GetData() ?? controller.CreateWindData(ociChar.GetChaControl());
                        if (windData != null)
                            windData.WindForce = value;
                    },
                    interpolateAfter: null,
                    isCompatibleWithTarget: (oci) => oci is OCIChar,
                    getValue: (oci, parameter) =>
                    {
                        OCIChar ociChar = oci as OCIChar;
                        if (ociChar == null || ociChar.GetChaControl() == null)
                            return 0.35f;

                        var controller = ociChar.GetChaControl().GetComponent<WindPhysicsController>();
                        if (controller == null)
                            return 0.35f;

                        WindData windData = controller.GetData();
                        return windData != null ? windData.WindForce : 0.35f;
                    },
                    readValueFromXml: (parameter, node) => node.ReadFloat("value"),
                    writeValueToXml: (parameter, writer, o) => writer.WriteValue("value", (float)o),
                    getParameter: GetParameter,
                    readParameterFromXml: null,
                    writeParameterToXml: null,
                    checkIntegrity: CheckIntegrity
                );
            }
        }

        private static bool CheckIntegrity(ObjectCtrlInfo oci, object parameter, object leftValue, object rightValue)
        {
            return parameter != null;
        }

        private static object GetParameter(ObjectCtrlInfo oci)
        {
            return oci;
        }
        #endregion
#endif
    }           
}
