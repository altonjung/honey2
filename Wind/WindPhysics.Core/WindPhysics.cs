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
using BepInEx.Configuration;
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
        public const string Version = "0.9.8.0";
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

        private static string _assemblyLocation;
        internal bool _loaded = false;

        private static bool _ShowUI = false;

        private static SimpleToolbarToggle _toolbarButton;

        private const int _uniqueId = ('W' << 24) | ('P' << 16) | ('P' << 8) | 'X';

        private Rect _windowRect = new Rect(70, 10, 400, 10);

        // 위치에 따른 바람 강도
        private AnimationCurve _heightToForceCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.1f); // 위로 갈수록 약함

        // internal Dictionary<int, ChaControl> _selectChaMgmt = new Dictionary<int, ChaControl>();

        // private Coroutine _CheckWindMgmtCoroutine;

#if FEATURE_VISUAL_WINDDIRECTION
        // LineRenderer 객체
        private GameObject _windDirObj;
        private LineRenderer _windDirLine;

        // 표시 시간 제어
        private float _lastWindDirValue = float.NaN;
        private float _windDirVisibleUntil = 0f;
        private const float _windDirVisibleDuration = 2.0f;

        // 표시 위치/길이        
        private const float _windDirLength = 5f;        
#endif
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

        #region Accessors
        internal static ConfigEntry<float> Gravity { get; private set; }
        internal static ConfigEntry<float> WindDirection { get; private set; }
        internal static ConfigEntry<float> WindInterval { get; private set; }
        internal static ConfigEntry<float> WindKeepTime { get; private set; }
        internal static ConfigEntry<float> WindUpForce { get; private set; }
        internal static ConfigEntry<float> WindForce { get; private set; }
        internal static ConfigEntry<float> WindAmplitude { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            // Environment 
            Gravity = Config.Bind("All", "Gravity", -0.01f, new ConfigDescription("gravity", new AcceptableValueRange<float>(-0.1f, 0.1f)));

            WindDirection = Config.Bind("All", "Direction", 0.0f, new ConfigDescription("wind direction from 0 to 360 degree", new AcceptableValueRange<float>(0.0f, 359.0f)));

            WindUpForce = Config.Bind("All", "ForceUp", 0.0f, new ConfigDescription("wind up force", new AcceptableValueRange<float>(0.0f, 1.0f)));

            WindForce = Config.Bind("All", "Force", 0.35f, new ConfigDescription("wind force", new AcceptableValueRange<float>(0.0f, 1.0f)));

            WindInterval = Config.Bind("All", "Interval", 2.0f, new ConfigDescription("wind spawn interval(sec)", new AcceptableValueRange<float>(0.0f, 60.0f)));

            WindKeepTime = Config.Bind("All", "Keep", 1.0f, new ConfigDescription("wind keep time(sec)", new AcceptableValueRange<float>(0.1f, 60.0f)));

            WindAmplitude = Config.Bind("All", "Amplitude", 1.0f, new ConfigDescription("wind amplitude", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClampWindKeepTimeToInterval();

            _self = this;
            Logger = base.Logger; 

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmony = HarmonyExtensions.CreateInstance(GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());


            _lastWindDirValue = WindDirection.Value;

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

#if FEATURE_VISUAL_WINDDIRECTION
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
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("park"))
                    ApplyScenePreset("park");
                if (GUILayout.Button("hill"))
                    ApplyScenePreset("hill");
                if (GUILayout.Button("water"))
                    ApplyScenePreset("water");                    
                if (GUILayout.Button("space"))
                    ApplyScenePreset("space");
                // if (GUILayout.Button("rain"))
                //     ApplyScenePreset("rain");
                GUILayout.EndHorizontal();
    // Global
                GUILayout.Label("<color=orange>Global</color>", RichLabel);
                // Direction
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Direction", "Wind Direction"), GUILayout.Width(80));
                WindDirection.Value = GUILayout.HorizontalSlider(WindDirection.Value, 0.0f, 359.0f);
                GUILayout.Label(WindDirection.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                // Interval
                GUILayout.BeginHorizontal();            
                GUILayout.Label(new GUIContent("Interval", "Wind Interval"), GUILayout.Width(80));
                WindInterval.Value = GUILayout.HorizontalSlider(WindInterval.Value, 0.0f, 60.0f);
                GUILayout.Label(WindInterval.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();            

                // Keep
                ClampWindKeepTimeToInterval();
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Keep", "Wind Keep Time"), GUILayout.Width(80));
                float keepMin = 0.1f;
                if (WindInterval.Value <= keepMin)
                {
                    WindKeepTime.Value = keepMin;
                    GUI.enabled = false;
                    GUILayout.HorizontalSlider(WindKeepTime.Value, keepMin, keepMin + 0.001f);
                    GUI.enabled = true;
                }
                else
                {
                    WindKeepTime.Value = GUILayout.HorizontalSlider(WindKeepTime.Value, keepMin, WindInterval.Value);
                }
                GUILayout.Label(WindKeepTime.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                // Amplitude
                GUILayout.BeginHorizontal(); 
                GUILayout.Label(new GUIContent("Amplitude", "Wind Amplitude"),  GUILayout.Width(80));
                WindAmplitude.Value = GUILayout.HorizontalSlider(WindAmplitude.Value, 1.0f, 10.0f);
                GUILayout.Label(WindAmplitude.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                // Force
                GUILayout.BeginHorizontal();             
                GUILayout.Label(new GUIContent("Force", "Wind Force"), GUILayout.Width(80));
                WindForce.Value = GUILayout.HorizontalSlider(WindForce.Value, 0.0f, 1.0f);
                GUILayout.Label(WindForce.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                // Force up
                GUILayout.BeginHorizontal();              
                GUILayout.Label(new GUIContent("Force Up", "Wind ForceUp"),  GUILayout.Width(80));
                WindUpForce.Value = GUILayout.HorizontalSlider(WindUpForce.Value, 0.0f, 1.0f);
                GUILayout.Label(WindUpForce.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal(); 

                // Gravity
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Gravity", "Gravity"), GUILayout.Width(80));
                Gravity.Value = GUILayout.HorizontalSlider(Gravity.Value, -0.1f, 0.1f);
                GUILayout.Label(Gravity.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

    // Hair
                GUILayout.Label("<color=orange>Hair</color>", RichLabel);
                GUILayout.BeginHorizontal();            
                GUILayout.Label(new GUIContent("Elastic", "Elastic"), GUILayout.Width(80));
                data.HairElastic = GUILayout.HorizontalSlider(data.HairElastic, 0.0f, 1.0f);
                GUILayout.Label(data.HairElastic.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();        
    // Acc
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

                if (GUILayout.Button("Close")) {
                    Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
                    _ShowUI = false;
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

            // ⭐ 툴팁 직접 그리기
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 150, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }

#if FEATURE_VISUAL_WINDDIRECTION
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

            // 변화 감지
            float current = WindDirection.Value;
            if (!Mathf.Approximately(current, _lastWindDirValue))
            {
                _lastWindDirValue = current;
                _windDirVisibleUntil = Time.time + _windDirVisibleDuration;
            }

            // 표시 여부 판단
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

#if FEATURE_VISUAL_WINDDIRECTION
            EnsureWindDirectionLine();
#endif
        }

        private void InitConfig()
        {
            Gravity.Value = (float)Gravity.DefaultValue;
            WindDirection.Value = (float)WindDirection.DefaultValue;
            WindForce.Value = (float)WindForce.DefaultValue;
            WindUpForce.Value = (float)WindUpForce.DefaultValue;            
            WindInterval.Value = (float)WindInterval.DefaultValue;
            WindKeepTime.Value = (float)WindKeepTime.DefaultValue;
            WindAmplitude.Value = (float)WindAmplitude.DefaultValue;
            ClampWindKeepTimeToInterval();

            var controller = GetCurrentControl();
            if (controller != null)
            {
                controller.ResetWindData();
            }
        }

        private void ApplyScenePreset(string preset)
        {
            if (preset == "park")
            {
                WindForce.Value = 0.3f;
                Gravity.Value = -0.05f;
            }
            else if (preset == "hill")
            {
                WindForce.Value = 0.8f;
                Gravity.Value = -0.05f;
            }
            else if (preset == "space")
            {
                WindForce.Value = 0.01f;
                Gravity.Value = 0.0f;
            }
            else if (preset == "water")
            {
                WindForce.Value = 0.03f;
                Gravity.Value = 0.02f;
            }
            else if (preset == "rain")
            {
                WindForce.Value = 0.5f;
                Gravity.Value = -0.08f;
            }

            Gravity.Value = Mathf.Clamp(Gravity.Value, -0.1f, 0.1f);
            ClampWindKeepTimeToInterval();
        }

        private void SceneInit()
        {
            Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
			_ShowUI = false;
        }

        internal static void ClampWindKeepTimeToInterval()
        {
            if (WindInterval == null || WindKeepTime == null)
                return;

            float interval = Mathf.Clamp(WindInterval.Value, 0.0f, 60f);
            if (!Mathf.Approximately(interval, WindInterval.Value))
                WindInterval.Value = interval;

            float keepMax = Mathf.Max(0.1f, interval);
            float keep = Mathf.Clamp(WindKeepTime.Value, 0.1f, keepMax);
            if (!Mathf.Approximately(keep, WindKeepTime.Value))
                WindKeepTime.Value = keep;
        }

        #endregion

        #region Patches
        // (cltoh 할당때문에 반드시 delay 처리해야함)
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

        // 악세러리 변경
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

        // 헤어 변경
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
                        if (WindPhysics.WindDirection.Value != value)
                            WindPhysics.WindDirection.Value = value;
                    },
                    interpolateAfter: null,
                    isCompatibleWithTarget: (oci) => oci is OCIChar,
                    getValue: (oci, parameter) => WindPhysics.WindDirection.Value,
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
                        if (WindPhysics.WindInterval.Value != value)
                            WindPhysics.WindInterval.Value = value;
                        WindPhysics.ClampWindKeepTimeToInterval();
                    },
                    interpolateAfter: null,
                    isCompatibleWithTarget: (oci) => oci is OCIChar,
                    getValue: (oci, parameter) => WindPhysics.WindInterval.Value,
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
                        if (WindPhysics.WindForce.Value != value)
                            WindPhysics.WindForce.Value = value;
                    },
                    interpolateAfter: null,
                    isCompatibleWithTarget: (oci) => oci is OCIChar,
                    getValue: (oci, parameter) => WindPhysics.WindForce.Value,
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
