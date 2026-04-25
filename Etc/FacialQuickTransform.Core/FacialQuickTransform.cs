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

namespace FacialQuickTransform
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
    public partial class FacialQuickTransform : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        public const string Name = "FacialQuickTransform";
        public const string Version = "0.9.0.0";
        public const string GUID = "com.alton.illusionplugins.Expression";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "expression_slider";
#endif

        internal static new ManualLogSource Logger;
        internal static FacialQuickTransform _self;

        internal bool _loaded;
        private static bool _showUi;
        private static SimpleToolbarToggle _toolbarButton;

        private const int _uniqueId = ('E' << 24) | ('X' << 16) | ('P' << 8) | 'R';
        private Rect _windowRect = new Rect(140, 10, 360, 10);
        private static readonly float[] SliderStepOptions = new[] { 1f, 0.1f, 0.01f };
        private int _correctionStepIndex = 2;
        private const float EyeBallMinX = -50f;
        private const float EyeBallMaxX = 50f;
        private const float EyeBallMinY = -50f;
        private const float EyeBallMaxY = 50f;
        private const float EyeRotationMinX = -45f;
        private const float EyeRotationMaxX = 45f;
        private const float FacePositionMin = -1f;
        private const float FacePositionMax = 1f;
        private const float FaceRotationMin = -45f;
        private const float FaceRotationMax = 45f;
        private const float EyeBallMarkerScale = 0.03f;
        private static readonly Color ModifiedEntryColor = new Color(1f, 0f, 0f, 1f);
        private static readonly Color UnmodifiedEntryColor = new Color(0.75f, 0.95f, 0.75f, 1f);
        private const float ValueCompareEpsilon = 0.001f;
        private GUIStyle _richLabel;
        private static readonly Color EyeBallMarkerColor = new Color(0f, 1f, 0f, 1f);
        private GameObject _eyeBallMarkerL;
        private GameObject _eyeBallMarkerR;
        private Material _eyeBallMarkerMaterial;
        private Vector2 _categoryScroll;
        private Vector2 _targetScroll;
        private int _selectedCategoryIndex;
        private int _selectedTargetIndex;

        private enum ExpressionCategoryId
        {
            EyeBall,
            Eye,
            Mouth,
            Nose
        }

        private enum ExpressionTargetId
        {
            LeftEye,
            RightEye,
            Both,
            LidUp,
            LidDn,
            SmileIn,
            SmileOut,
            Position,
            Rotation
        }

        private enum AxisId
        {
            X,
            Y,
            Z
        }

        private class ExpressionTargetUi
        {
            public readonly ExpressionTargetId Id;
            public readonly string Label;
            public readonly string Tooltip;

            public ExpressionTargetUi(ExpressionTargetId id, string label, string tooltip)
            {
                Id = id;
                Label = label;
                Tooltip = tooltip;
            }
        }

        private class ExpressionCategoryUi
        {
            public readonly ExpressionCategoryId Id;
            public readonly string Name;
            public readonly ExpressionTargetUi[] Targets;

            public ExpressionCategoryUi(ExpressionCategoryId id, string name, ExpressionTargetUi[] targets)
            {
                Id = id;
                Name = name;
                Targets = targets;
            }
        }

        private static readonly ExpressionCategoryUi[] ExpressionUiCategories = new[]
        {
            new ExpressionCategoryUi(ExpressionCategoryId.EyeBall, "EyeBall", new[]
            {
                new ExpressionTargetUi(ExpressionTargetId.Both, "Both", "Edit both eyeballs"),
                new ExpressionTargetUi(ExpressionTargetId.LeftEye, "Left Eye", "Edit left eyeball"),
                new ExpressionTargetUi(ExpressionTargetId.RightEye, "Right Eye", "Edit right eyeball")
            }),
            new ExpressionCategoryUi(ExpressionCategoryId.Eye, "Eye", new[]
            {
                new ExpressionTargetUi(ExpressionTargetId.LidUp, "lid_up", "Rotate Eye01/Eye02 pairs on X"),
                new ExpressionTargetUi(ExpressionTargetId.LidDn, "lid_dn", "Rotate Eye03/Eye04 pairs on X"),
                new ExpressionTargetUi(ExpressionTargetId.SmileIn, "smile_in", "Rotate Eye01 pair on X"),
                new ExpressionTargetUi(ExpressionTargetId.SmileOut, "smile_out", "Rotate Eye03 pair on X")
            }),
            new ExpressionCategoryUi(ExpressionCategoryId.Mouth, "Mouth", new[]
            {
                new ExpressionTargetUi(ExpressionTargetId.Rotation, "Rotation", "Edit mouth rotation"),
                new ExpressionTargetUi(ExpressionTargetId.Position, "Position", "Edit mouth position")
            }),
            new ExpressionCategoryUi(ExpressionCategoryId.Nose, "Nose", new[]
            {
                new ExpressionTargetUi(ExpressionTargetId.Rotation, "Rotation", "Edit nose rotation"),
                new ExpressionTargetUi(ExpressionTargetId.Position, "Position", "Edit nose position")
            })
        };

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
                "Open FacialQuickTransform window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(FacialQuickTransform).Assembly).LoadTexture(),
                false,
                this,
                val => _showUi = val);
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);

            CharacterApi.RegisterExtraBehaviour<FacialQuickTransformController>(GUID);
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

            FacialQuickTransformData data = GetCurrentData();
            OCIChar currentOCIChar = GetCurrentOCI();
            if (data == null || currentOCIChar == null)
            {
                SetEyeBallMarkerVisible(false, false);
                return;
            }

            ApplyEyeBallRotation(data);
            ApplyEyeTransforms(data);
            ApplyFaceTransforms(data);
            UpdateEyeBallMarker(data);
        }

        private OCIChar GetCurrentOCI()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null)
                return null;

            TreeNodeObject node = Studio.Studio.Instance.treeNodeCtrl.selectNodes.LastOrDefault();
            return node != null ? Studio.Studio.GetCtrlInfo(node) as OCIChar : null;
        }

        internal FacialQuickTransformController GetCurrentControl()
        {
            OCIChar ociChar = GetCurrentOCI();
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            return ociChar.GetChaControl().GetComponent<FacialQuickTransformController>();
        }

        private FacialQuickTransformData GetCurrentData()
        {
            OCIChar ociChar = GetCurrentOCI();
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            var controller = ociChar.GetChaControl().GetComponent<FacialQuickTransformController>();
            return controller != null ? controller.GetData() : null;
        }

        private FacialQuickTransformData GetDataAndCreate(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            var controller = ociChar.GetChaControl().GetComponent<FacialQuickTransformController>();
            if (controller == null)
                return null;

            return controller.GetData() ?? controller.CreateData(ociChar);
        }

        private void InitConfig()
        {
            var controller = GetCurrentControl();
            if (controller != null)
                controller.ResetFacialQuickTransformData();
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

            FacialQuickTransformData data = GetCurrentData() ?? GetDataAndCreate(GetCurrentOCI());
            if (data == null)
            {
                GUILayout.Label("<color=white>Nothing to select</color>", RichLabel);
                GUI.DragWindow();
                return;
            }

            DrawStepSelector(ref _correctionStepIndex, "Step");
            float step = SliderStepOptions[_correctionStepIndex];

            DrawExpressionControl(data, step);

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

        private void DrawExpressionControl(FacialQuickTransformData data, float step)
        {
            GUILayout.Space(6f);

            _selectedCategoryIndex = Mathf.Clamp(_selectedCategoryIndex, 0, ExpressionUiCategories.Length - 1);
            ExpressionCategoryUi selectedCategory = ExpressionUiCategories[_selectedCategoryIndex];
            _selectedTargetIndex = Mathf.Clamp(_selectedTargetIndex, 0, selectedCategory.Targets.Length - 1);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(160));
            GUILayout.Label("<color=orange>Category</color>", RichLabel);
            _categoryScroll = GUILayout.BeginScrollView(_categoryScroll, GUI.skin.box, GUILayout.Height(120));
            for (int i = 0; i < ExpressionUiCategories.Length; i++)
            {
                ExpressionCategoryUi category = ExpressionUiCategories[i];
                bool isSelected = i == _selectedCategoryIndex;
                bool isModified = IsCategoryModified(data, category.Id);
                Color prevColor = GUI.contentColor;
                GUI.contentColor = isModified ? ModifiedEntryColor : UnmodifiedEntryColor;
                if (GUILayout.Toggle(isSelected, category.Name, GUI.skin.button) && !isSelected)
                {
                    _selectedCategoryIndex = i;
                    _selectedTargetIndex = 0;
                }
                GUI.contentColor = prevColor;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            selectedCategory = ExpressionUiCategories[_selectedCategoryIndex];
            _selectedTargetIndex = Mathf.Clamp(_selectedTargetIndex, 0, selectedCategory.Targets.Length - 1);

            GUILayout.BeginVertical(GUILayout.Width(160));
            GUILayout.Label("<color=orange>Target</color>", RichLabel);
            _targetScroll = GUILayout.BeginScrollView(_targetScroll, GUI.skin.box, GUILayout.Height(120));
            for (int i = 0; i < selectedCategory.Targets.Length; i++)
            {
                ExpressionTargetUi target = selectedCategory.Targets[i];
                bool isSelected = i == _selectedTargetIndex;
                bool isModified = IsTargetModified(data, selectedCategory.Id, target.Id);
                Color prevColor = GUI.contentColor;
                GUI.contentColor = isModified ? ModifiedEntryColor : UnmodifiedEntryColor;
                if (GUILayout.Toggle(isSelected, new GUIContent(target.Label, target.Tooltip), GUI.skin.button))
                    _selectedTargetIndex = i;
                GUI.contentColor = prevColor;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            ExpressionTargetUi selectedTarget = selectedCategory.Targets[_selectedTargetIndex];
            DrawTargetSliders(data, selectedCategory.Id, selectedTarget.Id, step);
        }

        private void DrawTargetSliders(FacialQuickTransformData data, ExpressionCategoryId categoryId, ExpressionTargetId targetId, float step)
        {
            if (categoryId == ExpressionCategoryId.EyeBall)
            {
                float currentX = GetEyeBallTargetAxisValue(data, targetId, true);
                float currentY = GetEyeBallTargetAxisValue(data, targetId, false);
                float updatedX = DrawAngleFieldRow("X Rotate", currentX, step, EyeBallMinX, EyeBallMaxX);
                float updatedY = DrawAngleFieldRow("Y Rotate", currentY, step, EyeBallMinY, EyeBallMaxY);
                SetEyeBallTargetAxisValue(data, targetId, true, updatedX);
                SetEyeBallTargetAxisValue(data, targetId, false, updatedY);
                return;
            }

            if (categoryId == ExpressionCategoryId.Eye)
            {
                float currentX = GetEyeTargetValue(data, targetId);
                float updatedX = DrawAngleFieldRow("X Rotate", currentX, step, EyeRotationMinX, EyeRotationMaxX);
                SetEyeTargetValue(data, targetId, updatedX);
                return;
            }

            bool isPosition = targetId == ExpressionTargetId.Position;
            float min = isPosition ? FacePositionMin : FaceRotationMin;
            float max = isPosition ? FacePositionMax : FaceRotationMax;
            string suffix = isPosition ? "Pos" : "Rot";

            float currentXFace = GetFaceAxisValue(data, categoryId, isPosition, AxisId.X);
            float currentYFace = GetFaceAxisValue(data, categoryId, isPosition, AxisId.Y);
            float currentZFace = GetFaceAxisValue(data, categoryId, isPosition, AxisId.Z);
            float updatedXFace = DrawAngleFieldRow($"X {suffix}", currentXFace, step, min, max);
            float updatedYFace = DrawAngleFieldRow($"Y {suffix}", currentYFace, step, min, max);
            float updatedZFace = DrawAngleFieldRow($"Z {suffix}", currentZFace, step, min, max);
            SetFaceAxisValue(data, categoryId, isPosition, AxisId.X, updatedXFace);
            SetFaceAxisValue(data, categoryId, isPosition, AxisId.Y, updatedYFace);
            SetFaceAxisValue(data, categoryId, isPosition, AxisId.Z, updatedZFace);
        }

        private bool IsCategoryModified(FacialQuickTransformData data, ExpressionCategoryId categoryId)
        {
            if (categoryId == ExpressionCategoryId.EyeBall)
            {
                return IsModifiedValue(data.EyeBallLeftX)
                    || IsModifiedValue(data.EyeBallLeftY)
                    || IsModifiedValue(data.EyeBallRightX)
                    || IsModifiedValue(data.EyeBallRightY);
            }

            if (categoryId == ExpressionCategoryId.Eye)
            {
                return IsModifiedValue(data.EyeLidUpRotX)
                    || IsModifiedValue(data.EyeLidDnRotX)
                    || IsModifiedValue(data.EyeSmileInRotX)
                    || IsModifiedValue(data.EyeSmileOutRotX);
            }

            return IsModifiedValue(GetFaceAxisValue(data, categoryId, true, AxisId.X))
                || IsModifiedValue(GetFaceAxisValue(data, categoryId, true, AxisId.Y))
                || IsModifiedValue(GetFaceAxisValue(data, categoryId, true, AxisId.Z))
                || IsModifiedValue(GetFaceAxisValue(data, categoryId, false, AxisId.X))
                || IsModifiedValue(GetFaceAxisValue(data, categoryId, false, AxisId.Y))
                || IsModifiedValue(GetFaceAxisValue(data, categoryId, false, AxisId.Z));
        }

        private bool IsTargetModified(FacialQuickTransformData data, ExpressionCategoryId categoryId, ExpressionTargetId targetId)
        {
            if (categoryId == ExpressionCategoryId.EyeBall)
            {
                return IsModifiedValue(GetEyeBallTargetAxisValue(data, targetId, true))
                    || IsModifiedValue(GetEyeBallTargetAxisValue(data, targetId, false));
            }

            if (categoryId == ExpressionCategoryId.Eye)
                return IsModifiedValue(GetEyeTargetValue(data, targetId));

            bool isPosition = targetId == ExpressionTargetId.Position;
            return IsModifiedValue(GetFaceAxisValue(data, categoryId, isPosition, AxisId.X))
                || IsModifiedValue(GetFaceAxisValue(data, categoryId, isPosition, AxisId.Y))
                || IsModifiedValue(GetFaceAxisValue(data, categoryId, isPosition, AxisId.Z));
        }

        private bool IsModifiedValue(float value)
        {
            return Mathf.Abs(value) > ValueCompareEpsilon;
        }

        private float GetEyeBallTargetAxisValue(FacialQuickTransformData data, ExpressionTargetId targetId, bool isX)
        {
            if (targetId == ExpressionTargetId.Both)
            {
                float left = GetEyeBallSingleAxisValue(data, true, isX);
                float right = GetEyeBallSingleAxisValue(data, false, isX);
                return (left + right) * 0.5f;
            }

            bool isLeft = targetId == ExpressionTargetId.LeftEye;
            return GetEyeBallSingleAxisValue(data, isLeft, isX);
        }

        private void SetEyeBallTargetAxisValue(FacialQuickTransformData data, ExpressionTargetId targetId, bool isX, float value)
        {
            if (targetId == ExpressionTargetId.Both)
            {
                SetEyeBallSingleAxisValue(data, true, isX, value);
                SetEyeBallSingleAxisValue(data, false, isX, value);
                return;
            }

            bool isLeft = targetId == ExpressionTargetId.LeftEye;
            SetEyeBallSingleAxisValue(data, isLeft, isX, value);
        }

        private float GetEyeBallSingleAxisValue(FacialQuickTransformData data, bool isLeft, bool isX)
        {
            if (isLeft)
                return isX ? data.EyeBallLeftX : data.EyeBallLeftY;
            return isX ? data.EyeBallRightX : data.EyeBallRightY;
        }

        private void SetEyeBallSingleAxisValue(FacialQuickTransformData data, bool isLeft, bool isX, float value)
        {
            if (isLeft)
            {
                if (isX)
                    data.EyeBallLeftX = value;
                else
                    data.EyeBallLeftY = value;
            }
            else
            {
                if (isX)
                    data.EyeBallRightX = value;
                else
                    data.EyeBallRightY = value;
            }
        }

        private float GetEyeTargetValue(FacialQuickTransformData data, ExpressionTargetId targetId)
        {
            if (targetId == ExpressionTargetId.LidUp)
                return data.EyeLidUpRotX;
            if (targetId == ExpressionTargetId.LidDn)
                return data.EyeLidDnRotX;
            if (targetId == ExpressionTargetId.SmileIn)
                return data.EyeSmileInRotX;
            return data.EyeSmileOutRotX;
        }

        private void SetEyeTargetValue(FacialQuickTransformData data, ExpressionTargetId targetId, float value)
        {
            if (targetId == ExpressionTargetId.LidUp)
            {
                data.EyeLidUpRotX = value;
                return;
            }

            if (targetId == ExpressionTargetId.LidDn)
            {
                data.EyeLidDnRotX = value;
                return;
            }
            if (targetId == ExpressionTargetId.SmileIn)
            {
                data.EyeSmileInRotX = value;
                return;
            }
            data.EyeSmileOutRotX = value;
        }

        private float GetFaceAxisValue(FacialQuickTransformData data, ExpressionCategoryId categoryId, bool isPosition, AxisId axisId)
        {
            switch (categoryId)
            {
                case ExpressionCategoryId.Mouth:
                    if (isPosition)
                    {
                        if (axisId == AxisId.X) return data.MouthPosX;
                        if (axisId == AxisId.Y) return data.MouthPosY;
                        return data.MouthPosZ;
                    }
                    if (axisId == AxisId.X) return data.MouthRotX;
                    if (axisId == AxisId.Y) return data.MouthRotY;
                    return data.MouthRotZ;
                default:
                    if (isPosition)
                    {
                        if (axisId == AxisId.X) return data.NosePosX;
                        if (axisId == AxisId.Y) return data.NosePosY;
                        return data.NosePosZ;
                    }
                    if (axisId == AxisId.X) return data.NoseRotX;
                    if (axisId == AxisId.Y) return data.NoseRotY;
                    return data.NoseRotZ;
            }
        }

        private void SetFaceAxisValue(FacialQuickTransformData data, ExpressionCategoryId categoryId, bool isPosition, AxisId axisId, float value)
        {
            switch (categoryId)
            {
                case ExpressionCategoryId.Mouth:
                    if (isPosition)
                    {
                        if (axisId == AxisId.X) data.MouthPosX = value;
                        else if (axisId == AxisId.Y) data.MouthPosY = value;
                        else data.MouthPosZ = value;
                    }
                    else
                    {
                        if (axisId == AxisId.X) data.MouthRotX = value;
                        else if (axisId == AxisId.Y) data.MouthRotY = value;
                        else data.MouthRotZ = value;
                    }
                    break;
                default:
                    if (isPosition)
                    {
                        if (axisId == AxisId.X) data.NosePosX = value;
                        else if (axisId == AxisId.Y) data.NosePosY = value;
                        else data.NosePosZ = value;
                    }
                    else
                    {
                        if (axisId == AxisId.X) data.NoseRotX = value;
                        else if (axisId == AxisId.Y) data.NoseRotY = value;
                        else data.NoseRotZ = value;
                    }
                    break;
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

        private void ApplyEyeBallRotation(FacialQuickTransformData data)
        {
            if (data == null)
                return;

            if (!data._eye_ball_base_rot_ready)
                data.CaptureEyeBaseRotations();

            ApplyEyeBallRotationToTransform(data._eye_ball_L, data._eye_ball_base_rot_L, data.EyeBallLeftX, data.EyeBallLeftY);
            ApplyEyeBallRotationToTransform(data._eye_ball_R, data._eye_ball_base_rot_R, data.EyeBallRightX, data.EyeBallRightY);
        }

        private void ApplyEyeTransforms(FacialQuickTransformData data)
        {
            if (data == null)
                return;

            if (!data._eye_base_rot_ready)
                data.CaptureEyeBaseTransformRotations();

            float lidUp = data.EyeLidUpRotX;
            float lidDn = data.EyeLidDnRotX;
            float smileIn = data.EyeSmileInRotX;
            float smileOut = data.EyeSmileOutRotX;

            // lid_up: Eye01 + Eye02, lid_dn: Eye03 + Eye04, smile_in: Eye01, smile_out: Eye03
            ApplyEyePairRotationX(data._eye_01_L, data._eye_01_base_rot_L, lidUp + smileIn);
            ApplyEyePairRotationX(data._eye_02_L, data._eye_02_base_rot_L, lidUp);
            ApplyEyePairRotationX(data._eye_03_L, data._eye_03_base_rot_L, lidDn + smileOut);
            ApplyEyePairRotationX(data._eye_04_L, data._eye_04_base_rot_L, lidDn);

            ApplyEyePairRotationX(data._eye_01_R, data._eye_01_base_rot_R, lidUp + smileIn);
            ApplyEyePairRotationX(data._eye_02_R, data._eye_02_base_rot_R, lidUp);
            ApplyEyePairRotationX(data._eye_03_R, data._eye_03_base_rot_R, lidDn + smileOut);
            ApplyEyePairRotationX(data._eye_04_R, data._eye_04_base_rot_R, lidDn);
        }

        private void ApplyFaceTransforms(FacialQuickTransformData data)
        {
            if (data == null)
                return;

            if (!data._mouth_base_ready || !data._nose_base_ready)
                data.CaptureFaceBaseTransforms();

            ApplyFaceTransform(
                data._mouth,
                data._mouth_base_pos,
                data._mouth_base_rot,
                data.MouthPosX,
                data.MouthPosY,
                data.MouthPosZ,
                data.MouthRotX,
                data.MouthRotY,
                data.MouthRotZ);

            ApplyFaceTransform(
                data._nose,
                data._nose_base_pos,
                data._nose_base_rot,
                data.NosePosX,
                data.NosePosY,
                data.NosePosZ,
                data.NoseRotX,
                data.NoseRotY,
                data.NoseRotZ);
        }

        private void ApplyFaceTransform(Transform target, Vector3 basePos, Quaternion baseRot, float px, float py, float pz, float rx, float ry, float rz)
        {
            if (target == null)
                return;

            target.localPosition = basePos + new Vector3(px, py, pz);
            target.localRotation = baseRot * Quaternion.Euler(rx, ry, rz);
        }

        private void ApplyEyePairRotationX(Transform target, Quaternion baseRotation, float xRotation)
        {
            if (target == null)
                return;

            target.localRotation = baseRotation * Quaternion.Euler(xRotation, 0f, 0f);
        }

        private void ApplyEyeBallRotationToTransform(Transform eye, Quaternion baseRotation, float x, float y)
        {
            if (eye == null)
                return;

            Quaternion offset = Quaternion.Euler(x, y, 0f);
            eye.localRotation = baseRotation * offset;
        }

        private void UpdateEyeBallMarker(FacialQuickTransformData data)
        {
            if (!_showUi || data == null)
            {
                SetEyeBallMarkerVisible(false, false);
                return;
            }

            ExpressionCategoryId selectedCategory = ExpressionUiCategories[Mathf.Clamp(_selectedCategoryIndex, 0, ExpressionUiCategories.Length - 1)].Id;
            bool eyeBallCategorySelected = selectedCategory == ExpressionCategoryId.EyeBall;
            ExpressionCategoryUi selectedCategoryUi = ExpressionUiCategories[Mathf.Clamp(_selectedCategoryIndex, 0, ExpressionUiCategories.Length - 1)];
            ExpressionTargetId selectedTarget = selectedCategoryUi.Targets[Mathf.Clamp(_selectedTargetIndex, 0, selectedCategoryUi.Targets.Length - 1)].Id;
            bool showLeft = eyeBallCategorySelected && (selectedTarget == ExpressionTargetId.LeftEye || selectedTarget == ExpressionTargetId.Both);
            bool showRight = eyeBallCategorySelected && (selectedTarget == ExpressionTargetId.RightEye || selectedTarget == ExpressionTargetId.Both);
            UpdateEyeBallMarkerObject(ref _eyeBallMarkerL, "FacialQuickTransform_EyeMarker_L", data._eye_ball_L, showLeft);
            UpdateEyeBallMarkerObject(ref _eyeBallMarkerR, "FacialQuickTransform_EyeMarker_R", data._eye_ball_R, showRight);
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

                    var controller = chaControl.GetComponent<FacialQuickTransformController>();
                    if (controller != null)
                    {
                        FacialQuickTransformData expressionData = controller.GetData();

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
