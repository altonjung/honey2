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

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.Threading.Tasks;
using System.Numerics;
using System.Xml;
using System.Globalization;

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

namespace ExpressionSlider
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
    public partial class ExpressionSlider : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        public const string Name = "ExpressionSlider";
        public const string Version = "0.9.0.0";
        public const string GUID = "com.alton.illusionplugins.Expression";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "expression_slider";
#endif

        internal static new ManualLogSource Logger;
        internal static ExpressionSlider _self;

        internal bool _loaded;
        private static bool _showUi;
        private static SimpleToolbarToggle _toolbarButton;

        private const int _uniqueId = ('E' << 24) | ('X' << 16) | ('P' << 8) | 'R';
        private Rect _windowRect = new Rect(140, 10, 360, 10);
        private static readonly float[] SliderStepOptions = new[] { 1f, 0.1f, 0.01f };
        private int _correctionStepIndex = 2;
        private const float EyeBallMinX = -20f;
        private const float EyeBallMaxX = 20f;
        private const float EyeBallMinY = -30f;
        private const float EyeBallMaxY = 30f;
        private const float EyeBallMarkerScale = 0.03f;
        private GUIStyle _richLabel;
        private static readonly Color EyeBallMarkerColor = new Color(0f, 1f, 0f, 1f);
        private GameObject _eyeBallMarkerL;
        private GameObject _eyeBallMarkerR;
        private Material _eyeBallMarkerMaterial;

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

        protected override void Awake()
        {
            base.Awake();

            _self = this;
            Logger = base.Logger;

#if FEATURE_SCENE_SAVE
#if IPA
            HSExtSave.HSExtSave.RegisterHandler("rendererEditor", null, null, this.OnSceneLoad, this.OnSceneImport, this.OnSceneSave, null, null);
#elif BEPINEX
            ExtendedSave.SceneBeingLoaded += this.OnSceneLoad;
            ExtendedSave.SceneBeingImported += this.OnSceneImport;
            ExtendedSave.SceneBeingSaved += this.OnSceneSave;
#endif
#endif

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open ExpressionSlider window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(ExpressionSlider).Assembly).LoadTexture(),
                false,
                this,
                val => _showUi = val);
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);

            CharacterApi.RegisterExtraBehaviour<ExpressionSliderController>(GUID);
            Logger.LogMessage($"{Name} {Version} loaded");
        }

        private void OnDestroy()
        {
            DestroyEyeBallMarkers();
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
            if (!_loaded)
                return;

            ExpressionSliderData data = GetCurrentData();
            OCIChar currentOCIChar = GetCurrentOCI();
            if (data == null || currentOCIChar == null)
            {
                SetEyeBallMarkerVisible(false, false);
                return;
            }

            ApplyEyeBallRotation(data);
            UpdateEyeBallMarker(data);
        }

        private OCIChar GetCurrentOCI()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null)
                return null;

            TreeNodeObject node = Studio.Studio.Instance.treeNodeCtrl.selectNodes.LastOrDefault();
            return node != null ? Studio.Studio.GetCtrlInfo(node) as OCIChar : null;
        }

        internal ExpressionSliderController GetCurrentControl()
        {
            OCIChar ociChar = GetCurrentOCI();
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            return ociChar.GetChaControl().GetComponent<ExpressionSliderController>();
        }

        private ExpressionSliderData GetCurrentData()
        {
            OCIChar ociChar = GetCurrentOCI();
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            var controller = ociChar.GetChaControl().GetComponent<ExpressionSliderController>();
            return controller != null ? controller.GetData() : null;
        }

        private ExpressionSliderData GetDataAndCreate(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            var controller = ociChar.GetChaControl().GetComponent<ExpressionSliderController>();
            if (controller == null)
                return null;

            return controller.GetData() ?? controller.CreateData(ociChar);
        }

        private void InitConfig()
        {
            var controller = GetCurrentControl();
            if (controller != null)
                controller.ResetExpressionSliderData();
        }

        protected override void OnGUI()
        {
            if (!_loaded || !StudioAPI.InsideStudio || !_showUi)
                return;

            _windowRect = GUILayout.Window(_uniqueId + 1, _windowRect, WindowFunc, $"{Name} {Version}");
        }
        private static bool AlwaysTrue()
        {
            return true;
        }

        private void WindowFunc(int id)
        {
            var studio = Studio.Studio.Instance;

            bool guiUsingMouse = GUIUtility.hotControl != 0;
            bool mouseInWindow = _windowRect.Contains(Event.current.mousePosition);

            if (guiUsingMouse || mouseInWindow)
            {
                studio.cameraCtrl.noCtrlCondition = AlwaysTrue;
            }
            else
            {
                studio.cameraCtrl.noCtrlCondition = null;
            }

            ExpressionSliderData data = GetCurrentData() ?? GetDataAndCreate(GetCurrentOCI());
            if (data == null)
            {
                GUILayout.Label("<color=white>Nothing to select</color>", RichLabel);
                GUI.DragWindow();
                return;
            }

            DrawStepSelector(ref _correctionStepIndex, "Step");
            float step = SliderStepOptions[_correctionStepIndex];

            // add category/target
            DrawEyeBallControl(data, step);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Default"))
                InitConfig();
            if (GUILayout.Button("Close"))
            {
                studio.cameraCtrl.noCtrlCondition = null;
                _showUi = false;
                SetEyeBallMarkerVisible(false, false);
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 170, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }

        private void DrawEyeBallControl(ExpressionSliderData data, float step)
        {
            GUILayout.Space(6f);
            GUILayout.Label("EyeBall Rotate", RichLabel);

            DrawEyeCategorySelector(ref data.EyeBallCategory);
            DrawEyeTargetSelector(ref data.EyeBallEditTarget);

            bool categoryIsX = data.EyeBallCategory == 0;
            float min = categoryIsX ? EyeBallMinX : EyeBallMinY;
            float max = categoryIsX ? EyeBallMaxX : EyeBallMaxY;
            string label = categoryIsX ? "Eye X" : "Eye Y";
            float current = GetEyeBallCategoryValue(data, data.EyeBallCategory, data.EyeBallEditTarget);
            float updated = DrawAngleFieldRow(label, current, step, min, max);
            SetEyeBallCategoryValue(data, data.EyeBallCategory, data.EyeBallEditTarget, updated);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Eye Reset"))
            {
                data.EyeBallLeftX = 0f;
                data.EyeBallLeftY = 0f;
                data.EyeBallRightX = 0f;
                data.EyeBallRightY = 0f;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawEyeCategorySelector(ref int category)
        {
            if (category < 0 || category > 1)
                category = 0;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Category", GUILayout.Width(80));
            if (GUILayout.Toggle(category == 0, "Eye X", GUI.skin.button, GUILayout.Width(72)))
                category = 0;
            if (GUILayout.Toggle(category == 1, "Eye Y", GUI.skin.button, GUILayout.Width(72)))
                category = 1;
            GUILayout.EndHorizontal();
        }

        private void DrawEyeTargetSelector(ref int target)
        {
            if (target < 0 || target > 2)
                target = 0;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Target", GUILayout.Width(80));
            if (GUILayout.Toggle(target == 0, "Both", GUI.skin.button, GUILayout.Width(56)))
                target = 0;
            if (GUILayout.Toggle(target == 1, "Left", GUI.skin.button, GUILayout.Width(56)))
                target = 1;
            if (GUILayout.Toggle(target == 2, "Right", GUI.skin.button, GUILayout.Width(56)))
                target = 2;
            GUILayout.EndHorizontal();
        }

        private float GetEyeBallCategoryValue(ExpressionSliderData data, int category, int target)
        {
            bool categoryIsX = category == 0;
            switch (target)
            {
                case 1:
                    return categoryIsX ? data.EyeBallLeftX : data.EyeBallLeftY;
                case 2:
                    return categoryIsX ? data.EyeBallRightX : data.EyeBallRightY;
                default:
                    float left = categoryIsX ? data.EyeBallLeftX : data.EyeBallLeftY;
                    float right = categoryIsX ? data.EyeBallRightX : data.EyeBallRightY;
                    return (left + right) * 0.5f;
            }
        }

        private void SetEyeBallCategoryValue(ExpressionSliderData data, int category, int target, float value)
        {
            bool categoryIsX = category == 0;
            if (target == 0 || target == 1)
            {
                if (categoryIsX)
                    data.EyeBallLeftX = value;
                else
                    data.EyeBallLeftY = value;
            }
            if (target == 0 || target == 2)
            {
                if (categoryIsX)
                    data.EyeBallRightX = value;
                else
                    data.EyeBallRightY = value;
            }
        }

        private float DrawAngleFieldRow(string label, float value, float step, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(80));
            if (GUILayout.Button("-", GUILayout.Width(22)))
                value -= step;
            value = GUILayout.HorizontalSlider(value, min, max);
            if (GUILayout.Button("+", GUILayout.Width(22)))
                value += step;
            value = Quantize(value, step, min, max);
            GUILayout.Label(FormatByStep(value, step), GUILayout.Width(44));
            if (GUILayout.Button("Reset", GUILayout.Width(52)))
                value = 0f;
            GUILayout.EndHorizontal();
            return value;
        }

        private void DrawStepSelector(ref int stepIndex, string label)
        {
            if (stepIndex < 0 || stepIndex >= SliderStepOptions.Length)
                stepIndex = 0;

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            for (int i = 0; i < SliderStepOptions.Length; i++)
            {
                string stepLabel = SliderStepOptions[i].ToString("0.###", CultureInfo.InvariantCulture);
                if (GUILayout.Toggle(stepIndex == i, stepLabel, GUI.skin.button, GUILayout.Width(50)))
                    stepIndex = i;
            }
            GUILayout.EndHorizontal();
        }

        private float Quantize(float value, float step, float min, float max)
        {
            if (step <= 0f)
                return Mathf.Clamp(value, min, max);

            return Mathf.Clamp(Mathf.Round(value / step) * step, min, max);
        }

        private string FormatByStep(float value, float step)
        {
            int decimals = 2;
            if (step > 0f)
                decimals = Mathf.Clamp(Mathf.CeilToInt(-Mathf.Log10(step)), 0, 4);

            string fmt = decimals == 0 ? "0" : ("0." + new string('0', decimals));
            return value.ToString(fmt, CultureInfo.InvariantCulture);
        }

        private void ApplyEyeBallRotation(ExpressionSliderData data)
        {
            if (data == null)
                return;

            if (!data._eye_ball_base_rot_ready)
                data.CaptureEyeBaseRotations();

            ApplyEyeBallRotationToTransform(data._eye_ball_L, data._eye_ball_base_rot_L, data.EyeBallLeftX, data.EyeBallLeftY);
            ApplyEyeBallRotationToTransform(data._eye_ball_R, data._eye_ball_base_rot_R, data.EyeBallRightX, data.EyeBallRightY);
        }

        private void ApplyEyeBallRotationToTransform(Transform eye, Quaternion baseRotation, float x, float y)
        {
            if (eye == null)
                return;

            Quaternion offset = Quaternion.Euler(x, y, 0f);
            eye.localRotation = baseRotation * offset;
        }

        private void UpdateEyeBallMarker(ExpressionSliderData data)
        {
            if (!_showUi || data == null)
            {
                SetEyeBallMarkerVisible(false, false);
                return;
            }

            bool showLeft = data.EyeBallEditTarget == 0 || data.EyeBallEditTarget == 1;
            bool showRight = data.EyeBallEditTarget == 0 || data.EyeBallEditTarget == 2;
            UpdateEyeBallMarkerObject(ref _eyeBallMarkerL, "ExpressionSlider_EyeMarker_L", data._eye_ball_L, showLeft);
            UpdateEyeBallMarkerObject(ref _eyeBallMarkerR, "ExpressionSlider_EyeMarker_R", data._eye_ball_R, showRight);
        }

        private void UpdateEyeBallMarkerObject(ref GameObject marker, string markerName, Transform target, bool visible)
        {
            if (!visible || target == null)
            {
                if (marker != null)
                    marker.SetActive(false);
                return;
            }

            if (marker == null)
                marker = CreateEyeBallMarker(markerName);

            marker.SetActive(true);
            marker.transform.position = target.position;
            marker.transform.localScale = Vector3.one * EyeBallMarkerScale;
        }

        private GameObject CreateEyeBallMarker(string name)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;
            marker.hideFlags = HideFlags.HideAndDontSave;

            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null)
                Destroy(markerCollider);

            MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetEyeBallMarkerMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return marker;
        }

        private Material GetEyeBallMarkerMaterial()
        {
            if (_eyeBallMarkerMaterial != null)
                return _eyeBallMarkerMaterial;

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");

            _eyeBallMarkerMaterial = new Material(shader);
            _eyeBallMarkerMaterial.hideFlags = HideFlags.HideAndDontSave;
            _eyeBallMarkerMaterial.color = EyeBallMarkerColor;
            return _eyeBallMarkerMaterial;
        }

        private void SetEyeBallMarkerVisible(bool showLeft, bool showRight)
        {
            if (_eyeBallMarkerL != null)
                _eyeBallMarkerL.SetActive(showLeft);
            if (_eyeBallMarkerR != null)
                _eyeBallMarkerR.SetActive(showRight);
        }

        private void DestroyEyeBallMarkers()
        {
            if (_eyeBallMarkerL != null)
                Destroy(_eyeBallMarkerL);
            _eyeBallMarkerL = null;

            if (_eyeBallMarkerR != null)
                Destroy(_eyeBallMarkerR);
            _eyeBallMarkerR = null;

            if (_eyeBallMarkerMaterial != null)
                Destroy(_eyeBallMarkerMaterial);
            _eyeBallMarkerMaterial = null;
        }

        private void Init()
        {
            _loaded = true;
        }

        private void SceneInit()
        {
            Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
            _showUi = false;
            DestroyEyeBallMarkers();
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
                    
                    TreeNodeObject _node = Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Last();
                    ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);
                    OCIChar ociChar = objectCtrlInfo as OCIChar;
                    ChaControl chaControl = ociChar.GetChaControl();

                    var controller = chaControl.GetComponent<ExpressionSliderController>();
                    if (controller != null)
                    {
                        ExpressionSliderData expressionData = controller.GetData();

                        foreach (var fbsTarget in __instance.EyesCtrl.FBSTarget)
                        {
                            SkinnedMeshRenderer srender = fbsTarget.GetSkinnedMeshRenderer();
                            var mesh = srender.sharedMesh;
                            if (mesh && mesh.blendShapeCount > 0)
                            {
                                string name = mesh.GetBlendShapeName(0);
                                if (name.Contains("head"))
                                {
                                    srender
                                        .SetBlendShapeWeight(expressionData.eye_close_idx_in_head_of_eyectrl, 0f);

                                    srender
                                        .SetBlendShapeWeight(expressionData.eye_wink_idx_in_head_of_eyectrl, 0);
                                }
                                else if (name.Contains("namida"))
                                {
                                    srender
                                            .SetBlendShapeWeight(expressionData.eye_close_idx_in_namida_of_eyectrl, 0f);

                                    srender
                                        .SetBlendShapeWeight(expressionData.eye_wink_idx_in_namida_of_eyectrl, 0);
                                }
                                else if (name.Contains("lash."))
                                {
                                    srender
                                        .SetBlendShapeWeight(expressionData.eye_close_idx_in_lash_of_eyectrl, 0f);

                                    srender
                                        .SetBlendShapeWeight(expressionData.eye_wink_idx_in_lash_of_eyectrl, 0);
                                }
                            }
                        }

                        foreach (var fbsTarget in __instance.MouthCtrl.FBSTarget)
                        {
                            SkinnedMeshRenderer srender = fbsTarget.GetSkinnedMeshRenderer();
                            var mesh = srender.sharedMesh;
                            if (mesh && mesh.blendShapeCount > 0)
                            {
                                string name = mesh.GetBlendShapeName(0);
                                if (name.Contains("head"))
                                {
                                    srender
                                        .SetBlendShapeWeight(expressionData.eye_close_idx_in_head_of_mouthctrl, 0f);

                                    srender
                                        .SetBlendShapeWeight(expressionData.eye_wink_idx_in_head_of_mouthctrl, 0);
                                    srender
                                                    .SetBlendShapeWeight(38, 100);

                                }
                                else if (name.Contains("namida"))
                                {
                                    srender
                                        .SetBlendShapeWeight(expressionData.eye_close_idx_in_namida_of_mouthctrl, 0f);

                                    srender
                                        .SetBlendShapeWeight(expressionData.eye_wink_idx_in_namida_of_mouthctrl, 0);
                                }
                            }
                        }
                    }
                                        
                }
            }
        }         
    }
}
