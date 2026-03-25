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
        public const string Version = "0.9.7.1";
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

#if FEATURE_PUBLIC

#else
        private static SimpleToolbarToggle _toolbarButton;
#endif		
        private const int _uniqueId = ('W' << 24) | ('P' << 16) | ('P' << 8) | 'X';

        private Rect _windowRect = new Rect(70, 10, 550, 10);

        private bool _lastConfigKeyEnableWind;
        private float _lastInterval;

        // 위치에 따른 바람 강도
        private AnimationCurve _heightToForceCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.1f); // 위로 갈수록 약함

        internal Dictionary<int, ChaControl> _selectChaMgmt = new Dictionary<int, ChaControl>();

        private Coroutine _CheckWindMgmtCoroutine;

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
        private OCIChar _currentOCIChar = null;

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
        internal static ConfigEntry<bool> ConfigKeyEnableWind { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyEnableWindShortcut { get; private set; }

        internal static ConfigEntry<float> Gravity { get; private set; }
        internal static ConfigEntry<float> WindDirection { get; private set; }
        internal static ConfigEntry<float> WindInterval { get; private set; }
        internal static ConfigEntry<float> WindUpForce { get; private set; }
        internal static ConfigEntry<float> WindForce { get; private set; }
        internal static ConfigEntry<float> WindAmplitude { get; private set; }

        // internal static ConfigEntry<float> AccesoriesForce { get; private set; }
        // internal static ConfigEntry<float> AccesoriesElastic { get; private set; }

        // internal static ConfigEntry<float> HairForce { get; private set; }
        // internal static ConfigEntry<float> HairElastic { get; private set; }

        // internal static ConfigEntry<float> ClotheForce { get; private set; }
        // internal static ConfigEntry<float> ClothDamping { get; private set; }
        // internal static ConfigEntry<float> ClothStiffness { get; private set; }    
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            // Environment 
            Gravity = Config.Bind("All", "Gravity", -0.03f, new ConfigDescription("Gravity", new AcceptableValueRange<float>(-0.1f, 0.1f)));

            WindDirection = Config.Bind("All", "Direction", 0f, new ConfigDescription("wind direction from 0 to 360 degree", new AcceptableValueRange<float>(0.0f, 359.0f)));

            WindUpForce = Config.Bind("All", "ForceUp", 0.0f, new ConfigDescription("wind up force", new AcceptableValueRange<float>(0.0f, 0.5f)));

            WindForce = Config.Bind("All", "Force", 0.1f, new ConfigDescription("wind force", new AcceptableValueRange<float>(0.0f, 1.0f)));

            WindInterval = Config.Bind("All", "Interval", 2f, new ConfigDescription("wind spawn interval(sec)", new AcceptableValueRange<float>(0.0f, 60.0f)));

            WindAmplitude = Config.Bind("All", "Amplitude", 1.0f, new ConfigDescription("wind amplitude", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // // clothes
            // ClotheForce = Config.Bind("Cloth", "Force", 1.0f, new ConfigDescription("cloth force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            // ClothDamping = Config.Bind("Cloth", "Damping", 0.5f, new ConfigDescription("cloth damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            // ClothStiffness = Config.Bind("Cloth", "Stiffness", 7.0f, new ConfigDescription("wind stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // // hair
            // HairForce = Config.Bind("Hair", "Force", 1.0f, new ConfigDescription("hair force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            // HairElastic = Config.Bind("Hair", "Elastic", 0.15f, new ConfigDescription("hair elastic", new AcceptableValueRange<float>(0.0f, 1.0f)));

            // // accesories
            // AccesoriesForce = Config.Bind("Misc", "Force", 1.0f, new ConfigDescription("accesories force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            // AccesoriesElastic = Config.Bind("Misc", "Elastic", 0.7f, new ConfigDescription("accesories elastic", new AcceptableValueRange<float>(0.0f, 1.0f)));


            // option 
            ConfigKeyEnableWind = Config.Bind("Options", "Toggle effect", false, "Wind enabled/disabled");

            ConfigKeyEnableWindShortcut = Config.Bind("ShortKey", "Toggle effect key", new KeyboardShortcut(KeyCode.W));

            _lastConfigKeyEnableWind = ConfigKeyEnableWind.Value;
            _lastInterval = WindInterval.Value;

            _self = this;
            Logger = base.Logger; 

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmony = HarmonyExtensions.CreateInstance(GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
#if FEATURE_PUBLIC

#else
            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open WindPhysics window",
                () => ResourceUtils.GetEmbeddedResource("wp_toolbar_icon.png", typeof(WindPhysics).Assembly).LoadTexture(),
                false, this, val => _ShowUI = val);
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);
#endif
            CharacterApi.RegisterExtraBehaviour<WindPhysicsController>(GUID);

            _CheckWindMgmtCoroutine = StartCoroutine(CheckWindMgmtRoutine());

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

            if (ConfigKeyEnableWindShortcut.Value.IsDown())
            {
                ConfigKeyEnableWind.Value = !ConfigKeyEnableWind.Value;
            }

#if FEATURE_VISUAL_WINDDIRECTION
            UpdateWindDirectionLine();
#endif
        }
#if FEATURE_PUBLIC

#else
        private WindData GetCurrentData()
        {
            if (_currentOCIChar == null)
                return null;

            var controller = _currentOCIChar.GetChaControl().GetComponent<WindPhysicsController>();
            if (controller == null)
                return null;

            WindData data = controller.GetWindData();

            return data;
        }    

        private WindPhysicsController GetCurrentControl()
        {
            if (_currentOCIChar == null)
                return null;

            return _currentOCIChar.GetChaControl().GetComponent<WindPhysicsController>();            
        }   

        protected override void OnGUI()
        {
            if (_ShowUI == false)
                return;
            
            if (StudioAPI.InsideStudio)
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "Wind Physics " + Version);
        }       

        private void draw_seperate()
        {
            GUILayout.Space(5);            
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(0.2f));
            GUI.Box(rect, GUIContent.none);
            GUILayout.Space(10);
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

            WinData data = GetCurrentData();

            if (data != null)
            {

                // ================= UI =================
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

                // Amplitude
                GUILayout.BeginHorizontal(); 
                GUILayout.Label(new GUIContent("Amplitude", "Wind Amplitude"),  GUILayout.Width(80));
                WindAmplitude.Value = GUILayout.HorizontalSlider(WindAmplitude.Value, 1.0f, 10.0f);
                GUILayout.Label(WindAmplitude.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                // Force
                GUILayout.BeginHorizontal();             
                GUILayout.Label(new GUIContent("Force", "Wind Force"), GUILayout.Width(80));
                WindForce.Value = GUILayout.HorizontalSlider(WindForce.Value, 0.1f, 1.0f);
                GUILayout.Label(WindForce.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                // Force up
                GUILayout.BeginHorizontal();              
                GUILayout.Label(new GUIContent("Force Up", "Wind ForceUp"),  GUILayout.Width(80));
                WindUpForce.Value = GUILayout.HorizontalSlider(WindUpForce.Value, 0.0f, 0.5f);
                GUILayout.Label(WindUpForce.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal(); 

                // Gravity            
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Gravity", "Gravity"), GUILayout.Width(80));
                Gravity.Value = GUILayout.HorizontalSlider(Gravity.Value, -0.1f, 0.1f);
                GUILayout.Label(Gravity.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();
            
                draw_seperate();
    // Hair
                GUILayout.Label("<color=orange>Hair</color>", RichLabel);
                GUILayout.BeginHorizontal();            
                GUILayout.Label(new GUIContent("E", "Elastic"), GUILayout.Width(20));
                data.HairElastic = GUILayout.HorizontalSlider(data.HairElastic, 0.0f, 1.0f);
                GUILayout.Label(data.HairElastic.ToString("0.00"), GUILayout.Width(40));

                GUILayout.Label(new GUIContent("F", "Force"), GUILayout.Width(20));
                HairForce.Value = GUILayout.HorizontalSlider(HairForce.Value, 0.1f, 1.0f);
                GUILayout.Label(HairForce.Value.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();
                
                draw_seperate();            
    // Acc
                GUILayout.Label("<color=orange>Accessory</color>", RichLabel);;
                GUILayout.BeginHorizontal();
            
                GUILayout.Label(new GUIContent("D", "Elastic"), GUILayout.Width(20));
                data.AccesoriesElastic = GUILayout.HorizontalSlider(data.AccesoriesElastic, 0.0f, 1.0f);
                GUILayout.Label(data.AccesoriesElastic.ToString("0.00"), GUILayout.Width(40));
                
                GUILayout.Label(new GUIContent("F", "Force"), GUILayout.Width(20));
                data.AccesoriesForce = GUILayout.HorizontalSlider(data.AccesoriesForce, 0.1f, 1.0f);
                GUILayout.Label(data.AccesoriesForce.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                draw_seperate();
    // Cloth
                GUILayout.Label("<color=orange>Cloth</color>", RichLabel);
                GUILayout.BeginHorizontal();        
                GUILayout.Label(new GUIContent("D", "Damping"), GUILayout.Width(20));
                data.ClothDamping = GUILayout.HorizontalSlider(data.ClothDamping, 0.0f, 1.0f);
                GUILayout.Label(data.ClothDamping.ToString("0.00"), GUILayout.Width(40));

                GUILayout.Label(new GUIContent("S", "Stiffness"), GUILayout.Width(20));
                data.ClothStiffness = GUILayout.HorizontalSlider(data.ClothStiffness, 0.0f, 10.0f);
                GUILayout.Label(data.ClothStiffness.ToString("0.00"), GUILayout.Width(40));

                GUILayout.Label(new GUIContent("F", "Force"), GUILayout.Width(20));
                data.ClotheForce = GUILayout.HorizontalSlider(data.ClotheForce, 0.1f, 1.0f);
                GUILayout.Label(data.ClotheForce.ToString("0.00"), GUILayout.Width(40));
                GUILayout.EndHorizontal();

                GUILayout.Space(10);  

                GUILayout.BeginHorizontal();
                if (ConfigKeyEnableWind.Value == true)
                {
                    if (GUILayout.Button("Deactive")) {
                        _lastConfigKeyEnableWind = ConfigKeyEnableWind.Value;   
                        ConfigKeyEnableWind.Value = false;
                    }    
                } else
                {
                    if (GUILayout.Button("Active")) {
                        _lastConfigKeyEnableWind = ConfigKeyEnableWind.Value; 
                        ConfigKeyEnableWind.Value = true;
                    }   
                }

                if(GUILayout.Button("Default"))
                {
                    InitConfig();   
                }   
            }

            if (GUILayout.Button("Close")) {
                Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
                _ShowUI = false;
            }
            
            GUILayout.EndHorizontal();

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

            WindData data = GetCurrentData();

            if (data != null) {
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

        }        
#endif

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
            WindAmplitude.Value = (float)WindAmplitude.DefaultValue;

            var controller = GetCurrentControl();
            if (controller != null)
            {
                controller.ResetWindData();
            }
        }

        private void MgmtInit()
        {
            foreach (var kvp in _selectChaMgmt)
            {
               var key = kvp.Key;
               ChaControl chaCtrl = kvp.Value;

                var controller = chaCtrl.GetComponent<WindPhysicsController>();
                if (controller != null)
                {    
                    controller.StopWindEffect();
                }
            }

            _selectChaMgmt.Clear();
            _ShowUI = false;
        }

        IEnumerator CheckWindMgmtRoutine()
        {
            while (true)
            {
                if(_loaded && (_lastConfigKeyEnableWind != ConfigKeyEnableWind.Value || _lastInterval != WindInterval.Value))
                {
                    // 선택된 대상에 대해서만, 재처리
                    foreach (var kvp in _selectChaMgmt)
                    {
                        var key = kvp.Key;
                        ChaControl chaCtrl = kvp.Value;

                        var controller = chaCtrl.GetComponent<WindPhysicsController>();
                        if (controller != null)
                        {    
                            if (ConfigKeyEnableWind.Value)
                            {
                                WindData windData = controller.GetWindData();
                                if (windData != null && windData.wind_status != Status.RUN)
                                {
                                    controller.ExecuteWindEffect(chaCtrl);
                                }
                            } else
                            {
                                controller.StopWindEffect();
                                controller.SetHairDown();
                            }
                        }
                    }
                
                    _lastInterval = WindInterval.Value;
                    _lastConfigKeyEnableWind = ConfigKeyEnableWind.Value;
                }

                yield return new WaitForSeconds(0.3f); // 0.3초 대기
            }
        }

        #endregion

        #region Patches

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {

                ObjectCtrlInfo selectedCtrlInfo = Studio.Studio.GetCtrlInfo(_node);

                if (selectedCtrlInfo == null)
                   return true;

                OCIChar ociChar =  selectedCtrlInfo as OCIChar;

                if (ociChar != null)
                {
                    _self._currentOCIChar = ociChar;
                    ChaControl chaCtrl = ociChar.GetChaControl();

                    if (_self._selectChaMgmt.TryGetValue(chaCtrl.GetHashCode(), out var chaCtrl1))
                    {
                        var controller = chaCtrl1.GetComponent<WindPhysicsController>();

                        if (controller != null)
                        {
                            WindData windData = controller.GetWindData();
                            if (windData.wind_status != Status.RUN)
                            {
                                controller.ExecuteWindEffect(chaCtrl1);
                            }
                        }
                    } 
                    else
                    {
                        _self._selectChaMgmt.Add(chaCtrl.GetHashCode(), chaCtrl);
                        var controller = chaCtrl.GetComponent<WindPhysicsController>();
                     
                        if (controller != null)
                        {                  
                            controller.ExecuteWindEffect(chaCtrl);
                        }
                    }                 
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectMultiple))]
        private static class WorkspaceCtrl_OnSelectMultiple_Patches
        {
            private static bool Prefix(object __instance)
            {

                foreach (TreeNodeObject node in Studio.Studio.instance.treeNodeCtrl.selectNodes)
                {
                    ObjectCtrlInfo selectedCtrlInfo = Studio.Studio.GetCtrlInfo(node);

                    if (selectedCtrlInfo == null)
                        return true;

                    OCIChar ociChar =  selectedCtrlInfo as OCIChar;

                    if (ociChar != null)
                    {
                        ChaControl chaCtrl = ociChar.GetChaControl();

                        if (_self._selectChaMgmt.TryGetValue(chaCtrl.GetHashCode(), out var chaCtrl1))
                        {
                            var controller = chaCtrl1.GetComponent<WindPhysicsController>();

                            if (controller != null)
                            {
                                WindData windData = controller.GetWindData();
                                if (windData != null && windData.wind_status != Status.RUN)
                                {
                                    controller.ExecuteWindEffect(chaCtrl1);
                                }
                            }
                        } 
                        else
                        {
                            _self._selectChaMgmt.Add(chaCtrl.GetHashCode(), chaCtrl);
                            var controller = chaCtrl.GetComponent<WindPhysicsController>();
                        
                            if (controller != null)
                            {                  
                                controller.ExecuteWindEffect(chaCtrl);
                            }
                        }                 
                    }                    
                }            

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                // 선택된 대상 모두 Stop 
                foreach (var kvp in _self._selectChaMgmt)
                {
                    var key = kvp.Key;
                    ChaControl chaCtrl = kvp.Value;

                    if (chaCtrl != null)
                    {                 
                        var controller = chaCtrl.GetComponent<WindPhysicsController>();
                        if (controller != null)
                        {                  
                            controller.StopWindEffect();
                        }    
                    }
                }

                _self._selectChaMgmt.Clear();

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

                _self._selectChaMgmt.Remove(unselectedCtrlInfo.GetHashCode());

                return true;
            }
        }

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
                        WindData windData = controller.GetWindData();
                        if (windData != null && windData.wind_status == Status.RUN)
                            controller.ExecuteWindEffect(chaControl);
                    }    
                }
            }
        }

        // 개별 옷 변경 (cloth 할당때문에 반드시 delay 처리해야함)
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
                        WindData windData = controller.GetWindData();
                        if (windData != null && windData.wind_status == Status.RUN)
                            controller.ExecuteWindEffect(chaControl);
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
                        WindData windData = controller.GetWindData();
                        if (windData != null && windData.wind_status == Status.RUN)
                            controller.ExecuteWindEffect(chaControl);
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
                        WindData windData = controller.GetWindData();
                        if (windData != null && windData.wind_status == Status.RUN)
                            controller.ExecuteWindEffect(chaControl);
                    }    
                }
            }
        }        

        // 코디네이션 변경 (cloth 할당때문에 반드시 delay 처리해야함)
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
                        WindData windData = controller.GetWindData();
                        if (windData != null && windData.wind_status == Status.RUN)
                            controller.ExecuteWindEffect(chaControl);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.MgmtInit();
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
                        if (WindPhysics.ConfigKeyEnableWind.Value != value) {
                            WindPhysics.ConfigKeyEnableWind.Value = value;

                            if (value) {
                                OCIChar ociChar =  oci as OCIChar;

                                if (ociChar != null)
                                {
                                    var controller = ociChar.GetChaControl().GetComponent<WindPhysicsController>();
                            
                                    if (controller != null)
                                    {                  
                                        controller.ExecuteWindEffect(ociChar.GetChaControl());
                                    }
                                }
                            }
                        }
                    },
                    interpolateAfter: null,
                    isCompatibleWithTarget: (oci) => oci is OCIChar,
                    getValue: (oci, parameter) => WindPhysics.ConfigKeyEnableWind.Value,
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
