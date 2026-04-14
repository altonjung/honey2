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

    1) 각 캐릭터에 RealGirlController 로직 자동 추가
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

    1) DAN bone 활용 + FEATURE_BODY_BLENDSHAPE_SUPPORT 연동
*/

namespace RealHumanSupport
{

#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
    // [BepInProcess("HoneySelect2")]
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
        public const string Version = "0.9.2.1";
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
#if FEATURE_WINK_SUPPORT  
        enum WinkState { Idle, Playing }
#endif        
        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static RealHumanSupport _self;

        private static string _assemblyLocation;
        internal bool _loaded = false;

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
        private Vector2 _extraBodyColliderScroll;
        private int _selectedExtraBodyColliderIndex = -1;
        private DynamicBoneCollider _selectedExtraBodyCollider;
        private static readonly float[] _extraColliderStepOptions = new float[] { 1f, 0.1f, 0.01f, 0.001f };
        private int _extraColliderStepIndex = 2;
        private DynamicBoneCollider _visualizedExtraBodyCollider;
        private GameObject _extraBodyColliderVisualRoot;
        private readonly List<LineRenderer> _extraBodyColliderVisualLines = new List<LineRenderer>();

        private GUIStyle _richLabel;
        private GUIStyle _richButton;

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

        private GUIStyle RichButton
        {
            get
            {
                if (_richButton == null)
                {
                    _richButton = new GUIStyle(GUI.skin.button);
                    _richButton.richText = true;
                    _richButton.alignment = TextAnchor.MiddleLeft;
                }
                return _richButton;
            }
        }

        private OCIChar _currentOCIChar = null;
        // Config
        #region Accessors
#if FEATURE_WINK_SUPPORT         
        internal static ConfigEntry<KeyboardShortcut> ConfigWinkShortcut { get; private set; }
#endif
        internal static ConfigEntry<bool> TearDropActive { get; private set; }

        internal static ConfigEntry<bool> BreathActive { get; private set; }        

        internal static ConfigEntry<bool> EyeShakeActive { get; private set; }

        internal static ConfigEntry<bool> BodyBlendingActive { get; private set; }

        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            string support_type = "Studio";

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
            if (_currentOCIChar != null && _currentOCIChar.GetChaControl() != null) {
                var controller = _currentOCIChar.GetChaControl().GetComponent<RealHumanSupportController>();
                if (controller == null)
                    return null;

                RealHumanData data = controller.GetRealHumanData();
                return data;
            } 

            return null;
        }

        private RealHumanSupportController GetCurrentControl()
        {
            if (_currentOCIChar != null && _currentOCIChar.GetChaControl() != null) {
                return _currentOCIChar.GetChaControl().GetComponent<RealHumanSupportController>();         
            }
             
            return null;      
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

       protected override void OnGUI()
        {
            if (_ShowUI == false)
            {
                ClearSelectedExtraBodyColliderVisual();
                return;
            }

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

                GUILayout.Label("<color=orange>Tear</color>", RichLabel);

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Strong", "Strong"), GUILayout.Width(60));
                data.TearDropLevel = GUILayout.HorizontalSlider(data.TearDropLevel, 0.1f, 1.0f);
                GUILayout.Label(data.TearDropLevel.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal(); 

                DrawExtraBodyColliderEditor(data);

                if (GUILayout.Button("Force Refresh"))
                {
                    RealHumanSupportController controller = GetCurrentControl();
                    if (controller != null) {
                        controller.ExecuteRealHumanEffect(data.chaCtrl);
                    }
                }

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

                if (BreathActive.Value) {
                    if (GUILayout.Button("Belly(D)"))
                    {
                        BreathActive.Value = false;
                    }
                }
                else {
                    if (GUILayout.Button("Belly(A)"))
                    {
                        BreathActive.Value = true;
                    }
                }

                if (EyeShakeActive.Value) {
                    if (GUILayout.Button("Eye(D)"))
                    {
                        EyeShakeActive.Value = false;
                    }
                }
                else {
                    if (GUILayout.Button("Eye(A)"))
                    {
                        EyeShakeActive.Value = true;
                    }
                }

                if (GUILayout.Button("Default")) {
                    InitConfig();
                }
            }
            else
            {
                ClearSelectedExtraBodyColliderVisual();
                GUILayout.Label("<color=white>Nothing to select</color>", RichLabel);                
            }
//
            if (GUILayout.Button("Close")) {
                Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
                ClearSelectedExtraBodyColliderVisual();
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

        private void DrawExtraBodyColliderEditor(RealHumanData data)
        {
            if (data == null || data.chaCtrl == null || data.chaCtrl.objBodyBone == null)
                return;

            RealHumanSupportController controller = data.chaCtrl.GetComponent<RealHumanSupportController>();
            List<DynamicBoneCollider> colliders = GetExtraBodyColliders(data);
            GUILayout.Space(6f);
            GUILayout.Label("<color=orange>Extra Body Collider</color>", RichLabel);

            if (colliders.Count == 0)
            {
                GUILayout.Label("<color=white>No dynamic bone collider</color>", RichLabel);
                _selectedExtraBodyCollider = null;
                _selectedExtraBodyColliderIndex = -1;
                ClearSelectedExtraBodyColliderVisual();
                return;
            }

            if (_selectedExtraBodyCollider != null)
            {
                int idx = colliders.IndexOf(_selectedExtraBodyCollider);
                _selectedExtraBodyColliderIndex = idx >= 0 ? idx : _selectedExtraBodyColliderIndex;
            }

            if (_selectedExtraBodyColliderIndex < 0 || _selectedExtraBodyColliderIndex >= colliders.Count)
                _selectedExtraBodyColliderIndex = 0;

            _extraBodyColliderScroll = GUILayout.BeginScrollView(_extraBodyColliderScroll, GUI.skin.box, GUILayout.Height(90));
            for (int i = 0; i < colliders.Count; i++)
            {
                DynamicBoneCollider collider = colliders[i];
                if (collider == null)
                    continue;

                bool isModified = controller != null && controller.IsExtraColliderModified(collider);
                string color = isModified ? "red" : "white";
                string label = $"<color={color}>{collider.name} ({collider.m_Direction})</color>";
                if (GUILayout.Toggle(_selectedExtraBodyColliderIndex == i, label, RichButton))
                {
                    _selectedExtraBodyColliderIndex = i;
                    _selectedExtraBodyCollider = collider;
                }
            }
            GUILayout.EndScrollView();

            if (_selectedExtraBodyCollider == null || !_selectedExtraBodyCollider)
                _selectedExtraBodyCollider = colliders[_selectedExtraBodyColliderIndex];

            Transform tr = _selectedExtraBodyCollider != null ? _selectedExtraBodyCollider.transform : null;
            if (tr == null)
            {
                ClearSelectedExtraBodyColliderVisual();
                return;
            }

            EnsureSelectedExtraBodyColliderVisual(_selectedExtraBodyCollider);

            GUILayout.Label($"Target: {tr.name}");
            _extraColliderStepIndex = DrawStepRow("Step", _extraColliderStepIndex);
            float step = _extraColliderStepOptions[_extraColliderStepIndex];

            Vector3 posBefore = tr.localPosition;
            Vector3 scaleBefore = tr.localScale;

            Vector3 pos = posBefore;
            pos.x = SliderRow("Pos X", pos.x, -2.0f, 2.0f, step);
            pos.y = SliderRow("Pos Y", pos.y, -2.0f, 2.0f, step);
            pos.z = SliderRow("Pos Z", pos.z, -2.0f, 2.0f, step);
            tr.localPosition = pos;

            Vector3 scale = scaleBefore;
            scale.x = SliderRow("Scl X", scale.x, 0.05f, 3.0f, step);
            scale.y = SliderRow("Scl Y", scale.y, 0.05f, 3.0f, step);
            scale.z = SliderRow("Scl Z", scale.z, 0.05f, 3.0f, step);
            tr.localScale = scale;

            if (controller != null && (!NearlyEqual(posBefore, pos) || !NearlyEqual(scaleBefore, scale)))
                controller.TrackExtraColliderCurrentSnapshot(_selectedExtraBodyCollider);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Collide Reset All"))
            {
                if (controller != null)
                    controller.ResetAllExtraColliderTransformsToOriginal(colliders);
                else
                    ResetAllExtraBodyColliderTransforms(colliders);
            }
            GUILayout.EndHorizontal();
        }

        private static List<DynamicBoneCollider> GetExtraBodyColliders(RealHumanData data)
        {
            if (data == null || data.chaCtrl == null || data.chaCtrl.objBodyBone == null)
                return new List<DynamicBoneCollider>();

            var colliders = (data.extraBodyColliders != null && data.extraBodyColliders.Count > 0)
                ? data.extraBodyColliders
                : data.chaCtrl.objBodyBone
                    .GetComponentsInChildren<DynamicBoneCollider>(true)
                    .ToList();

            return colliders
                .Where(v => v != null && !string.IsNullOrEmpty(v.name) &&
                            v.name.IndexOf("_ExtDBoneCollider") >= 0)
                .Distinct()
                .OrderBy(v => v.name)
                .ToList();
        }

        private int DrawStepRow(string label, int stepIndex)
        {
            int clampedIndex = Mathf.Clamp(stepIndex, 0, _extraColliderStepOptions.Length - 1);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            for (int i = 0; i < _extraColliderStepOptions.Length; i++)
            {
                string optionLabel = _extraColliderStepOptions[i].ToString("0.###");
                if (GUILayout.Toggle(clampedIndex == i, optionLabel, GUI.skin.button))
                    clampedIndex = i;
            }
            GUILayout.EndHorizontal();
            return clampedIndex;
        }

        private static float SliderRow(string label, float value, float min, float max, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            if (GUILayout.Button("-", GUILayout.Width(24)))
                value -= step;
            value = GUILayout.HorizontalSlider(value, min, max);
            if (GUILayout.Button("+", GUILayout.Width(24)))
                value += step;
            value = Mathf.Clamp(value, min, max);
            GUILayout.Label(value.ToString("0.000"), GUILayout.Width(45));
            GUILayout.EndHorizontal();
            return value;
        }

        private static bool NearlyEqual(Vector3 a, Vector3 b, float epsilon = 0.0001f)
        {
            return (a - b).sqrMagnitude <= epsilon * epsilon;
        }

        private static void ResetExtraBodyColliderTransform(Transform tr)
        {
            if (tr == null)
                return;

            tr.localPosition = Vector3.zero;
            tr.localScale = Vector3.one;
        }

        private static void ResetAllExtraBodyColliderTransforms(List<DynamicBoneCollider> colliders)
        {
            if (colliders == null || colliders.Count == 0)
                return;

            for (int i = 0; i < colliders.Count; i++)
            {
                DynamicBoneCollider collider = colliders[i];
                if (collider == null)
                    continue;

                ResetExtraBodyColliderTransform(collider.transform);
            }
        }

        private void EnsureSelectedExtraBodyColliderVisual(DynamicBoneCollider collider)
        {
            if (collider == null)
            {
                ClearSelectedExtraBodyColliderVisual();
                return;
            }

            if (_visualizedExtraBodyCollider == collider && _extraBodyColliderVisualRoot != null)
                return;

            ClearSelectedExtraBodyColliderVisual();

            _visualizedExtraBodyCollider = collider;
            _extraBodyColliderVisualRoot = new GameObject("RealHumanSupport_selectedExtraBodyColliderVisual");
            _extraBodyColliderVisualRoot.transform.SetParent(collider.transform, false);
            _extraBodyColliderVisualRoot.transform.localPosition = Vector3.zero;
            _extraBodyColliderVisualRoot.transform.localRotation = Quaternion.identity;
            _extraBodyColliderVisualRoot.transform.localScale = Vector3.one;

            const int segmentCount = 40;
            float radius = Mathf.Max(0.001f, collider.m_Radius);
            float height = Mathf.Max(0f, collider.m_Height);
            Vector3 center = collider.m_Center;
            float halfLine = Mathf.Max(0f, (height * 0.5f) - radius);
            bool isSphereLike = height <= 0.0001f || halfLine <= 0.0001f;

            if (isSphereLike)
            {
                CreateCircleWire(center, Vector3.right, Vector3.up, radius, segmentCount, "Sphere_XY");
                CreateCircleWire(center, Vector3.right, Vector3.forward, radius, segmentCount, "Sphere_XZ");
                CreateCircleWire(center, Vector3.up, Vector3.forward, radius, segmentCount, "Sphere_YZ");
            }
            else
            {
                Vector3 axis;
                Vector3 tangentA;
                Vector3 tangentB;
                switch (collider.m_Direction)
                {
                    case DynamicBoneColliderBase.Direction.X:
                        axis = Vector3.right;
                        tangentA = Vector3.up;
                        tangentB = Vector3.forward;
                        break;
                    case DynamicBoneColliderBase.Direction.Y:
                        axis = Vector3.up;
                        tangentA = Vector3.right;
                        tangentB = Vector3.forward;
                        break;
                    default:
                        axis = Vector3.forward;
                        tangentA = Vector3.right;
                        tangentB = Vector3.up;
                        break;
                }

                Vector3 top = center + axis * halfLine;
                Vector3 bottom = center - axis * halfLine;

                CreateCircleWire(top, tangentA, tangentB, radius, segmentCount, "Capsule_Top");
                CreateCircleWire(bottom, tangentA, tangentB, radius, segmentCount, "Capsule_Bottom");
                CreateLineWire(top + tangentA * radius, bottom + tangentA * radius, "Capsule_SideA");
                CreateLineWire(top - tangentA * radius, bottom - tangentA * radius, "Capsule_SideB");
                CreateLineWire(top + tangentB * radius, bottom + tangentB * radius, "Capsule_SideC");
                CreateLineWire(top - tangentB * radius, bottom - tangentB * radius, "Capsule_SideD");
            }
        }

        private void CreateCircleWire(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, int segmentCount, string name)
        {
            LineRenderer lr = CreateWireLineRenderer(name);
            if (lr == null)
                return;

            lr.positionCount = segmentCount + 1;
            for (int i = 0; i <= segmentCount; i++)
            {
                float t = (float)i / segmentCount * Mathf.PI * 2f;
                Vector3 p = center + axisA * (Mathf.Cos(t) * radius) + axisB * (Mathf.Sin(t) * radius);
                lr.SetPosition(i, p);
            }
        }

        private void CreateLineWire(Vector3 from, Vector3 to, string name)
        {
            LineRenderer lr = CreateWireLineRenderer(name);
            if (lr == null)
                return;

            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
        }

        private LineRenderer CreateWireLineRenderer(string name)
        {
            if (_extraBodyColliderVisualRoot == null)
                return null;

            GameObject go = new GameObject(name);
            go.transform.SetParent(_extraBodyColliderVisualRoot.transform, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.008f;
            lr.endWidth = 0.008f;
            lr.alignment = LineAlignment.View;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            Color wireColor = new Color(0.1f, 1.0f, 0.2f, 0.95f);
            lr.startColor = wireColor;
            lr.endColor = wireColor;

            _extraBodyColliderVisualLines.Add(lr);
            return lr;
        }

        private void ClearSelectedExtraBodyColliderVisual()
        {
            _visualizedExtraBodyCollider = null;

            for (int i = 0; i < _extraBodyColliderVisualLines.Count; i++)
            {
                LineRenderer lr = _extraBodyColliderVisualLines[i];
                if (lr != null)
                {
                    if (lr.material != null)
                        UnityEngine.Object.Destroy(lr.material);
                    UnityEngine.Object.Destroy(lr.gameObject);
                }
            }

            _extraBodyColliderVisualLines.Clear();

            if (_extraBodyColliderVisualRoot != null)
            {
                UnityEngine.Object.Destroy(_extraBodyColliderVisualRoot);
                _extraBodyColliderVisualRoot = null;
            }
        }

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
            ClearSelectedExtraBodyColliderVisual();
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

                if (Singleton<Studio.Studio>.Instance != null && Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes != null && Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() > 0)
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
                                if (mouseReleased)
                                {
                                    mouseReleased = false;  // 한 번만 쓰고 초기화
                                    RealHumanData realHumanData = controller.GetRealHumanData();

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
                        if (controller.GetRealHumanData() == null)
                            controller.ExecuteRealHumanEffect(chaControl);        
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
                        controller.ExecuteRealHumanEffect(chaControl);
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

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.SceneInit();
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
