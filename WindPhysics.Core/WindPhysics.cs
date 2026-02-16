using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
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
        public const string Version = "0.9.6.0";
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

        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;
		
        private const int _uniqueId = ('W' << 24) | ('P' << 16) | ('P' << 8) | 'X';

        private Rect _windowRect = new Rect(70, 10, 600, 10);

        private float _minY = float.MaxValue;
        private float _maxY = float.MinValue;

        private bool _previousConfigKeyEnableWind;
        private float _previousInterval;

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
        internal static ConfigEntry<float> WindAmplitude { get; private set; }

        internal static ConfigEntry<float> AccesoriesForce { get; private set; }
        internal static ConfigEntry<float> AccesoriesDamping { get; private set; }
        internal static ConfigEntry<float> AccesoriesStiffness { get; private set; }

        internal static ConfigEntry<float> HairForce { get; private set; }
        internal static ConfigEntry<float> HairDamping { get; private set; }
        internal static ConfigEntry<float> HairStiffness { get; private set; }

        internal static ConfigEntry<float> ClotheForce { get; private set; }
        internal static ConfigEntry<float> ClothDamping { get; private set; }
        internal static ConfigEntry<float> ClothStiffness { get; private set; }    
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
#if FEATURE_PUBLIC
            WindInterval = Config.Bind("All", "Interval", 2f, new ConfigDescription("wind spawn interval(sec)", new AcceptableValueRange<float>(1.0f, 10.0f)));
#else
            WindInterval = Config.Bind("All", "Interval", 2f, new ConfigDescription("wind spawn interval(sec)", new AcceptableValueRange<float>(0.0f, 60.0f)));
#endif
            WindAmplitude = Config.Bind("All", "Amplitude", 1.0f, new ConfigDescription("wind amplitude", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // clothes
            ClotheForce = Config.Bind("Cloth", "Force", 1.0f, new ConfigDescription("cloth force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            ClothDamping = Config.Bind("Cloth", "Damping", 0.3f, new ConfigDescription("cloth damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            ClothStiffness = Config.Bind("Cloth", "Stiffness", 2.0f, new ConfigDescription("wind stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // hair
            HairForce = Config.Bind("Hair", "Force", 1.0f, new ConfigDescription("hair force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            HairDamping = Config.Bind("Hair", "Damping", 0.15f, new ConfigDescription("hair damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            HairStiffness = Config.Bind("Hair", "Stiffness", 0.3f, new ConfigDescription("hair stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // accesories
            AccesoriesForce = Config.Bind("Misc", "Force", 1.0f, new ConfigDescription("accesories force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            AccesoriesDamping = Config.Bind("Misc", "Damping", 0.7f, new ConfigDescription("accesories damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            AccesoriesStiffness = Config.Bind("Misc", "Stiffness", 1.0f, new ConfigDescription("accesories stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // option 
            ConfigKeyEnableWind = Config.Bind("Options", "Toggle effect", false, "Wind enabled/disabled");

            ConfigKeyEnableWindShortcut = Config.Bind("ShortKey", "Toggle effect key", new KeyboardShortcut(KeyCode.W));

            _previousConfigKeyEnableWind = ConfigKeyEnableWind.Value;
            _previousInterval = WindInterval.Value;


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

            _CheckWindMgmtRoutine = StartCoroutine(CheckWindMgmtRoutine());
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

        protected override void OnGUI()
        {
            if (_ShowUI == false)
                return;
            
            if (StudioAPI.InsideStudio)
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "Wind Physics" + Version);
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
// Global
            GUILayout.Label("Global");
            GUILayout.BeginHorizontal();
            // Gravity
            GUILayout.Label(new GUIContent("G", "Gravity"), GUILayout.Width(20));
            Gravity.Value = GUILayout.HorizontalSlider(Gravity.Value, -0.1f, 0.1f);
            GUILayout.Label(Gravity.Value.ToString("0.00"), GUILayout.Width(40));

            // direction
            GUILayout.Label(new GUIContent("D", "Wind Direction"), GUILayout.Width(20));
            WindDirection.Value = GUILayout.HorizontalSlider(WindDirection.Value, 0.0f, 359.0f);
            GUILayout.Label(WindDirection.Value.ToString("0.00"), GUILayout.Width(40));
            // force
            GUILayout.Label(new GUIContent("F", "Wind Force"), GUILayout.Width(20));
            WindForce.Value = GUILayout.HorizontalSlider(WindForce.Value, 0.1f, 1.0f);
            GUILayout.Label(WindForce.Value.ToString("0.00"), GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            // force up
            GUILayout.Label(new GUIContent("FU", "Wind ForceUp"),  GUILayout.Width(20));
            WindUpForce.Value = GUILayout.HorizontalSlider(WindUpForce.Value, 0.0f, 0.5f);
            GUILayout.Label(WindUpForce.Value.ToString("0.00"), GUILayout.Width(40));

            // amplitude
            GUILayout.Label(new GUIContent("A", "Wind Amplitude"),  GUILayout.Width(20));
            WindAmplitude.Value = GUILayout.HorizontalSlider(WindAmplitude.Value, 1.0f, 10.0f);
            GUILayout.Label(WindAmplitude.Value.ToString("0.00"), GUILayout.Width(40));

            // interval
            GUILayout.Label(new GUIContent("I", "Interval"), GUILayout.Width(20));
            WindInterval.Value = GUILayout.HorizontalSlider(WindInterval.Value, 0.0f, 60.0f);
            GUILayout.Label(WindInterval.Value.ToString("0.00"), GUILayout.Width(40));
            GUILayout.EndHorizontal();

// Hair
            GUILayout.Label("Hair");
            GUILayout.BeginHorizontal();
            
            GUILayout.Label(new GUIContent("D", "Damping"), GUILayout.Width(20));
            HairDamping.Value = GUILayout.HorizontalSlider(HairDamping.Value, 0.0f, 1.0f);
            GUILayout.Label(HairDamping.Value.ToString("0.00"), GUILayout.Width(40));

            GUILayout.Label(new GUIContent("S", "Stiffness"), GUILayout.Width(20));
            HairStiffness.Value = GUILayout.HorizontalSlider(HairStiffness.Value, 0.0f, 10.0f);
            GUILayout.Label(HairStiffness.Value.ToString("0.00"), GUILayout.Width(40));

            GUILayout.Label(new GUIContent("F", "Force"), GUILayout.Width(20));
            HairForce.Value = GUILayout.HorizontalSlider(HairForce.Value, 0.1f, 1.0f);
            GUILayout.Label(HairForce.Value.ToString("0.00"), GUILayout.Width(40));


            GUILayout.EndHorizontal();

// Cloth
            GUILayout.Label("Cloth");
            GUILayout.BeginHorizontal();
            
            GUILayout.Label(new GUIContent("D", "Damping"), GUILayout.Width(20));
            ClothDamping.Value = GUILayout.HorizontalSlider(ClothDamping.Value, 0.0f, 1.0f);
            GUILayout.Label(ClothDamping.Value.ToString("0.00"), GUILayout.Width(40));

            GUILayout.Label(new GUIContent("S", "Stiffness"), GUILayout.Width(20));
            ClothStiffness.Value = GUILayout.HorizontalSlider(ClothStiffness.Value, 0.0f, 10.0f);
            GUILayout.Label(ClothStiffness.Value.ToString("0.00"), GUILayout.Width(40));

            GUILayout.Label(new GUIContent("F", "Force"), GUILayout.Width(20));
            ClotheForce.Value = GUILayout.HorizontalSlider(ClotheForce.Value, 0.1f, 1.0f);
            GUILayout.Label(ClotheForce.Value.ToString("0.00"), GUILayout.Width(40));

            GUILayout.EndHorizontal();
// Acc
            GUILayout.Label("Acc");
            GUILayout.BeginHorizontal();
            
            GUILayout.Label(new GUIContent("D", "Damping"), GUILayout.Width(20));
            AccesoriesDamping.Value = GUILayout.HorizontalSlider(AccesoriesDamping.Value, 0.0f, 1.0f);
            GUILayout.Label(AccesoriesDamping.Value.ToString("0.00"), GUILayout.Width(40));

            GUILayout.Label(new GUIContent("S", "Stiffness"), GUILayout.Width(20));
            AccesoriesStiffness.Value = GUILayout.HorizontalSlider(AccesoriesStiffness.Value, 0.0f, 10.0f);
            GUILayout.Label(AccesoriesStiffness.Value.ToString("0.00"), GUILayout.Width(40));

            GUILayout.Label(new GUIContent("F", "DamForceping"), GUILayout.Width(20));
            AccesoriesForce.Value = GUILayout.HorizontalSlider(AccesoriesForce.Value, 0.1f, 1.0f);
            GUILayout.Label(AccesoriesForce.Value.ToString("0.00"), GUILayout.Width(40));

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (ConfigKeyEnableWind.Value == true)
            {
                if (GUILayout.Button("Deactive")) {
                    _previousConfigKeyEnableWind = ConfigKeyEnableWind.Value;   
                    ConfigKeyEnableWind.Value = false;
                }    
            } else
            {
                if (GUILayout.Button("Active")) {
                    _previousConfigKeyEnableWind = ConfigKeyEnableWind.Value; 
                    ConfigKeyEnableWind.Value = true;
                }   
            }

            if (GUILayout.Button("Close"))
                _ShowUI = false;
            
            GUILayout.EndHorizontal();

            // ⭐ 툴팁 직접 그리기
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 150, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }

        
        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            _loaded = true;
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
        }

        // n 개 대상 아이템에 대해 active/inactive 동시 적용 처리 
        IEnumerator CheckWindMgmtRoutine()
        {
            while (true)
            {
                if(_previousConfigKeyEnableWind != ConfigKeyEnableWind.Value || _previousInterval != WindInterval.Value)
                {
                    List<ObjectCtrlInfo>  selectedObjCtrlInfos = Logic.GetSelectedObjects();
                    foreach (ObjectCtrlInfo ctrlInfo in selectedObjCtrlInfos)
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
                                        windData1.coroutine = ociItem.guideObject.StartCoroutine(WindRoutine(windData1));
                                    }
                                }
                            } else
                            {
                                windData1.wind_status = Status.STOP;
                            }                            
                        }
#endif
                    }                        

                    _previousInterval = WindInterval.Value;
                    _previousConfigKeyEnableWind = ConfigKeyEnableWind.Value;          
                }

                yield return new WaitForSeconds(0.5f); // 1.0초 대기
            }
        }

        void ApplyWind(Vector3 windEffect, float factor, WindData windData)
        {
            float time = Time.time;

            // Scale by current wind energy once to avoid repeated multiplies in loops.
            windEffect *= factor;

            // PERF: cache frequently used settings once per call.
            bool gravityUp = Gravity.Value >= 0f;
            float gravity = Gravity.Value;
            float windForce = WindForce.Value;
            float windUpForce = WindUpForce.Value;

            float hairDamping = HairDamping.Value;
            float hairStiffness = HairStiffness.Value;
            float hairForce = HairForce.Value;

            float accessoriesDamping = AccesoriesDamping.Value;
            float accessoriesStiffness = AccesoriesStiffness.Value;
            float accessoriesForce = AccesoriesForce.Value;

            float clothDamping = ClothDamping.Value;
            float clothStiffness = ClothStiffness.Value;
            float clothForce = ClotheForce.Value;

            // PERF: evaluate waves once per update instead of per element.
            float windWave = Mathf.Max(Mathf.Sin(time * WindAmplitude.Value), 0f);
            float upWave = Mathf.SmoothStep(0f, 1f, Mathf.Max(windWave, 0f));
            float downWave = Mathf.SmoothStep(0f, 1f, Mathf.Max(-windWave, 0f));

            Vector3 hairFinalWind = windEffect * windForce * hairForce;
            hairFinalWind.y += windWave * windUpForce * factor;

            Vector3 accessoriesFinalWind = windEffect * windForce * accessoriesForce;
            accessoriesFinalWind.y += windWave * windUpForce * factor;

            Vector3 baseWind = windEffect.sqrMagnitude > 0f ? windEffect.normalized : Vector3.zero;
            Vector3 externalWind = baseWind * windForce * clothForce;
            float noise = (Mathf.PerlinNoise(time * 0.8f, 0f) - 0.5f) * 2f;

            // Tuned multipliers for stronger lift and reduced downward pull.
            const float upBoost = 5.0f;
            const float downReduce = 0.15f;

            Vector3 randomWind =
                baseWind * noise * windForce * clothForce +
                Vector3.up * (upWave * windUpForce * upBoost - downWave * windUpForce * downReduce);

            Vector3 clothExternalUp = Vector3.up * gravity;
            Vector3 clothExternalDown = externalWind * 600f * factor;
            Vector3 clothRandomUp = randomWind * 400f * factor;
            Vector3 clothRandomDown = randomWind * 1600f * factor;

            // Hair
            var hairBones = windData.hairDynamicBones;
            for (int i = 0; i < hairBones.Count; i++)
            {
                var hairBone = hairBones[i];
                if (hairBone == null)
                    continue;

                // Add subtle left/right spread based on head position.
                Transform hairBoneRoot = hairBone.m_Root != null ? hairBone.m_Root : hairBone.transform;
                Transform headTr = windData.head_bone;                
                float sideSign = 0f;
                if (headTr != null)
                {
                    Vector3 toHairBoneRoot = hairBoneRoot.position - headTr.position;
                    float side = Vector3.Dot(toHairBoneRoot, headTr.right); // head 기준 hairRoot 가 왼쪽인지/오른쪽인지 내적으로 판단
                    sideSign = Mathf.Sign(side);
                }

                float sideAmount = hairFinalWind.magnitude * UnityEngine.Random.Range(0.001f, 0.5f);
                Vector3 sideWind = headTr != null ? headTr.right * sideSign * sideAmount : Vector3.zero;

                hairBone.m_Damping = hairDamping + UnityEngine.Random.Range(-0.2f, 0.2f);
                hairBone.m_Stiffness = hairStiffness;
                hairBone.m_Force = hairFinalWind + sideWind;
                hairBone.m_Gravity = new Vector3(0, gravityUp
                ? UnityEngine.Random.Range(gravity, gravity + 0.02f)
                : UnityEngine.Random.Range(gravity, gravity - 0.01f), 0f);
            }

            // Accessories
            var accessoryBones = windData.accesoriesDynamicBones;
            for (int i = 0; i < accessoryBones.Count; i++)
            {
                var bone = accessoryBones[i];
                if (bone == null)
                    continue;

                bone.m_Damping = accessoriesDamping + UnityEngine.Random.Range(-0.2f, 0.2f);
                bone.m_Stiffness = accessoriesStiffness;
                bone.m_Force = accessoriesFinalWind;
                bone.m_Gravity = new Vector3(0f, gravityUp
                ? UnityEngine.Random.Range(gravity, gravity + 0.03f)
                : UnityEngine.Random.Range(gravity, gravity - 0.03f), 0f);
            }

            // Clothes
            var clothes = windData.clothes;
            for (int i = 0; i < clothes.Count; i++)
            {
                var cloth = clothes[i];
                if (cloth == null)
                    continue;

                cloth.worldAccelerationScale = 1.0f;
                cloth.worldVelocityScale = 0.0f;

                if (gravityUp)
                {
                    cloth.useGravity = false;
                    cloth.externalAcceleration = clothExternalUp;
                    cloth.randomAcceleration = clothRandomUp;
                }
                else
                {
                    cloth.useGravity = true;
                    cloth.externalAcceleration = clothExternalDown;
                    cloth.randomAcceleration = clothRandomDown;
                }

                // PERF: use cached values and avoid repeated property access.
                cloth.damping = clothDamping;
                cloth.stiffnessFrequency = clothStiffness;
            }
        }

        private IEnumerator FadeoutWindEffect_Cloth(
            List<Cloth> clothes,
            int settleFrames = 15,
            float settleForce = 0.2f)
        {
            if (clothes == null || clothes.Count == 0)
                yield break;

            // 1. Remove wind immediately and apply a small grounding force.
            foreach (var cloth in clothes)
            {
                if (cloth == null) continue;

                cloth.randomAcceleration = Vector3.zero;
                cloth.externalAcceleration = Vector3.down * settleForce;
            }

            // 2. Wait several frames so the cloth can settle.
            for (int i = 0; i < settleFrames; i++)
                yield return null; // Ensure at least one LateUpdate pass.

            // 3. Restore normal external acceleration.
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

            // 1. Remove wind force and apply temporary downward gravity.
            foreach (var bone in dynamicBones)
            {
                if (bone == null) continue;

                bone.m_Force = Vector3.zero;
                bone.m_Gravity = Vector3.down * settleGravity;
            }

            // 2. Wait several frames so bones stabilize.
            for (int i = 0; i < settleFrames; i++)
                yield return null; // Ensure at least one LateUpdate pass.

            // 3. Restore default gravity state.
            foreach (var bone in dynamicBones)
            {
                if (bone == null) continue;

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
                if (!_loaded)
                {
                    yield return null;
                    continue;
                }

                if (windData.wind_status == Status.RUN)
                {
                    // Gather Y-range data once per cycle.
                    foreach (var bone in windData.hairDynamicBones)
                    {
                        if (bone == null)
                            continue;

                        float y = bone.m_Root.position.y;
                        _minY = Mathf.Min(_minY, y);
                        _maxY = Mathf.Max(_maxY, y);
                    }

                    Quaternion globalRotation = Quaternion.Euler(0f, WindDirection.Value, 0f);

                    // Add small directional variation for less repetitive motion.
                    float angleY = UnityEngine.Random.Range(-15, 15); // Front/back offset.
                    float angleX = UnityEngine.Random.Range(-7, 7);   // Left/right offset.
                    Quaternion localRotation = Quaternion.Euler(angleX, angleY, 0f);

                    Quaternion rotation = globalRotation * localRotation;
                    Vector3 direction = rotation * Vector3.back;

                    // Slightly randomize base wind strength.
                    Vector3 windEffect = direction.normalized * UnityEngine.Random.Range(0.1f, 0.15f);

                    ApplyWind(windEffect, 1.0f, windData);
                    yield return new WaitForSeconds(0.2f);

                    // Fade out naturally over half of the configured interval.
                    float windInterval = WindInterval.Value;
                    float keepWindTime = windInterval * 0.5f;
                    float fadeTime = keepWindTime;

                    float t = 0f;
                    while (t < fadeTime)
                    {
                        t += Time.deltaTime;
                        float fadeFactor = Mathf.SmoothStep(1f, 0f, t / fadeTime); // Smoothly decrease.
                        ApplyWind(windEffect, fadeFactor, windData);
                        yield return null;
                    }

                    if (keepWindTime <= 0.3f)
                        yield return null;
                    else
                        yield return new WaitForSeconds(windInterval - keepWindTime);
                } 
                else
                {
                    yield return StartCoroutine(FadeoutWindEffect_Cloth(windData.clothes));
                    yield return StartCoroutine(FadeoutWindEffect_DynamicBone(windData.hairDynamicBones));
                    yield return StartCoroutine(FadeoutWindEffect_DynamicBone(windData.accesoriesDynamicBones));
                    windData.coroutine = null;

                    if (windData.wind_status == Status.REMOVE) {
                        _ociObjectMgmt.Remove(windData.objectCtrlInfo.GetHashCode());
                    }

                    yield break;
                }
            }
        }


        #endregion

        #region Patches

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                // UnityEngine.Debug.Log($">> OnSelectSingle");

                ObjectCtrlInfo selectedCtrlInfo = Studio.Studio.GetCtrlInfo(_node);

                if (selectedCtrlInfo == null)
                    return true;

                // 기존 다중 대상 중 선택되지 못한 대상은 모두 stop
                foreach (var kvp in _self._ociObjectMgmt)
                {
                    var key = kvp.Key;
                    WindData value = kvp.Value;

                    if (value.objectCtrlInfo != selectedCtrlInfo)
                    {
                        value.wind_status = Status.STOP;
                        // UnityEngine.Debug.Log($">> STOP {value.objectCtrlInfo}");
                    }
                }
                
                if (_self._ociObjectMgmt.TryGetValue(selectedCtrlInfo.GetHashCode(), out var windData))
                {
                    if (windData.coroutine == null)
                    {
                        OCIChar ociChar = selectedCtrlInfo as OCIChar;
                        if (ociChar != null)
                        {
                            windData.wind_status = Status.RUN;
                            windData.coroutine = ociChar.charInfo.StartCoroutine(_self.WindRoutine(windData));
                        }

#if FEATURE_ITEM_SUPPORT
                        OCIItem ociItem = value.objectCtrlInfo as OCIItem;
                        if (ociItem != null)
                        {
                            windData.wind_status = Status.RUN;
                            windData.coroutine = ociItem.guideObject.StartCoroutine(_self.WindRoutine(windData));
                        }
#endif
                    }
                } else
                {
                    // 선택 대상 신규 등록
                    Logic.TryAllocateObject(selectedCtrlInfo); 
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectMultiple))]
        private static class WorkspaceCtrl_OnSelectMultiple_Patches
        {
            private static bool Prefix(object __instance)
            {
                // UnityEngine.Debug.Log($">> OnSelectMultiple");

                List<ObjectCtrlInfo>  selectedObjCtrlInfos = Logic.GetSelectedObjects();

                // 기존 다중 대상 중 선택되지 못한 대상은 모두 stop
                foreach (var kvp in _self._ociObjectMgmt)
                {
                    var key = kvp.Key;
                    WindData value = kvp.Value;
                    bool isSelected = false;
                    foreach (ObjectCtrlInfo objCtrlInfo in selectedObjCtrlInfos)
                    {
                        if (objCtrlInfo == value.objectCtrlInfo)
                        {
                            isSelected = true;
                            break;
                        }
                    }

                    if (!isSelected)
                    {
                        value.wind_status = Status.STOP;
                        // UnityEngine.Debug.Log($">> STOP {value.objectCtrlInfo}");
                    }
                }

                // 선택된 대상에 대해 재활성화 혹은 신규 등록
                foreach (ObjectCtrlInfo objCtrlInfo in selectedObjCtrlInfos)
                {
                    if (_self._ociObjectMgmt.TryGetValue(objCtrlInfo.GetHashCode(), out var windData))
                    {
                        if (windData.coroutine == null)
                        {
                            OCIChar ociChar = objCtrlInfo as OCIChar;
                            if (ociChar != null)
                            {
                                windData.wind_status = Status.RUN;
                                windData.coroutine = ociChar.charInfo.StartCoroutine(_self.WindRoutine(windData));
                            }

#if FEATURE_ITEM_SUPPORT
                        OCIItem ociItem = value.objectCtrlInfo as OCIItem;
                        if (ociItem != null)
                        {
                            windData.wind_status = Status.RUN;
                            windData.coroutine = ociItem.guideObject.StartCoroutine(_self.WindRoutine(windData));
                        }
#endif
                        }
                    }
                    else
                    {
                        Logic.TryAllocateObject(objCtrlInfo); 
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
                // 기존 다중 대상 모두 Stop 
                foreach (var kvp in _self._ociObjectMgmt)
                {
                    var key = kvp.Key;
                    WindData value = kvp.Value;
                    value.wind_status = Status.STOP;
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

                if (_self._ociObjectMgmt.TryGetValue(unselectedCtrlInfo.GetHashCode(), out var windData))
                {
                    windData.wind_status = Status.REMOVE;
                }

                return true;
            }
        }

        // (cltoh 할당때문에 반드시 delay 처리해야함)
        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                Logic.TryAllocateObject(__instance);
            }
        }

        // 개별 옷 변경 (cltoh 할당때문에 반드시 delay 처리해야함)
        [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        private static class ChaControl_ChangeClothes_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;

                if (ociChar != null)
                {
                    Logic.TryAllocateObject(ociChar);
                }
            }
        }

        // 코디네이션 변경 (cltoh 할당때문에 반드시 delay 처리해야함)
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;

                if (ociChar != null)
                {
                    Logic.TryAllocateObject(ociChar);
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
                    Logic.TryAllocateObject(ociChar);
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
