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
        private const float FaceRotationMin = -45f;
        private const float FaceRotationMax = 45f;
        private const float FacePositionMin = -1f;
        private const float FacePositionMax = 1f;
        private const float EyeBallMarkerScale = 0.03f;
        private static readonly int[] MouthTypePatternMap = new[] { 0, 1, 2, 3, 5, 7, 8, 12, 22, 23 };
        private static readonly Color ModifiedEntryColor = new Color(1f, 0f, 0f, 1f);
        private static readonly Color UnmodifiedEntryColor = new Color(0.75f, 0.95f, 0.75f, 1f);
        private const float ValueCompareEpsilon = 0.001f;
        private GUIStyle _richLabel;
        private static readonly Color EyeBallMarkerColor = new Color(0f, 1f, 0f, 1f);
        private GameObject _eyeBallMarkerL;
        private GameObject _eyeBallMarkerR;
        private Material _eyeBallMarkerMaterial;
        private AssetBundle _bundle;
        internal Texture2D _tearDropImg;
        private Vector2 _categoryScroll;
        private Vector2 _targetScroll;
        private int _selectedCategoryIndex;
        private int _selectedTargetIndex;

        private enum ExpressionCategoryId
        {
            Eyebrow,
            Eye,
            EyeBall,
            EyeLid,
            Lip,
            Mouth,
            Nose,
            NoseWing,
            Tongue
        }

        private enum ExpressionTargetId
        {
            Both,
            Open,
            Tongue1,
            Tongue2,
            Smile,
            LipUp,
            LipDown,
            Left,
            Right,
            Shape,
            Tooth,
            Type
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

        private static readonly ExpressionCategoryUi[] BaseExpressionUiCategories = new[]
        {
            new ExpressionCategoryUi(ExpressionCategoryId.Eyebrow, "Eyebrow", new[]
            {
                new ExpressionTargetUi(ExpressionTargetId.Type, "Type", "Edit eyebrow pattern type")
            }),
            new ExpressionCategoryUi(ExpressionCategoryId.Eye, "Eye", new[]
            {
                new ExpressionTargetUi(ExpressionTargetId.Type, "Type", "Edit eye pattern type"),
                new ExpressionTargetUi(ExpressionTargetId.Smile, "Smile", "Edit in/out smile X rotation"),                
                new ExpressionTargetUi(ExpressionTargetId.Both, "Ball(Both)", "Edit both eyeballs"),
                new ExpressionTargetUi(ExpressionTargetId.Left, "Ball(L)", "Edit left eyeball"),
                new ExpressionTargetUi(ExpressionTargetId.Right, "Ball(R)", "Edit right eyeball")
            }),
            new ExpressionCategoryUi(ExpressionCategoryId.Nose, "Nose", new[]
            {
                new ExpressionTargetUi(ExpressionTargetId.Shape, "Bridge", "Edit nose rotation"),
                new ExpressionTargetUi(ExpressionTargetId.Both, "Wing(Both)", "Edit both nose wings"),
                new ExpressionTargetUi(ExpressionTargetId.Left, "Wing(L)", "Edit left nose wing rotation"),
                new ExpressionTargetUi(ExpressionTargetId.Right, "Wing(R)", "Edit right nose wing rotation")
            }),
            new ExpressionCategoryUi(ExpressionCategoryId.Mouth, "Mouth", new[]
            {
                new ExpressionTargetUi(ExpressionTargetId.Type, "Type", "Edit mouth pattern type"),                
                new ExpressionTargetUi(ExpressionTargetId.Smile, "Smile", "Edit mouth smile left/right X/Y position"),
                new ExpressionTargetUi(ExpressionTargetId.Tooth, "Tooth", "Edit mouth cavity Z position")
            }),
            new ExpressionCategoryUi(ExpressionCategoryId.Lip, "Lip", new[]
            {
                new ExpressionTargetUi(ExpressionTargetId.Both, "Both", "Edit mouth base rotation"),
                new ExpressionTargetUi(ExpressionTargetId.LipUp, "Up", "Edit upper lip rotation"),
                new ExpressionTargetUi(ExpressionTargetId.LipDown, "Down", "Edit lower lip rotation")
            }),                     
        };

        private static readonly ExpressionCategoryUi TongueExpressionCategory = new ExpressionCategoryUi(ExpressionCategoryId.Tongue, "Tongue", new[]
        {
            new ExpressionTargetUi(ExpressionTargetId.Tongue2, "Tip", "Edit tongue segment 2"),
            new ExpressionTargetUi(ExpressionTargetId.Tongue1, "Root", "Edit tongue segment 1")
        });

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
            if (_bundle != null)
            {
                _bundle.Unload(false);
                _bundle = null;
            }
        }

#if SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }
#endif

        private void LateUpdate()
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
            DrawTongueActivationButton(data);
            DrawTearDropActivationButton(data);

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

        private void DrawTongueActivationButton(FacialQuickTransformData data)
        {
            bool canActivateTongue = data != null && data.chaCtrl != null;
            bool prevEnabled = GUI.enabled;
            GUI.enabled = canActivateTongue;

            string label = data != null && data.TongueCategoryEnabled ? "Tongue Active: ON" : "Tongue Active: OFF";
            if (GUILayout.Button(new GUIContent(label, "Toggle tongue category and activate mouth pattern 10 on enable")))
            {
                bool enableTongue = !data.TongueCategoryEnabled;
                if (enableTongue)
                {
                    data.chaCtrl.ChangeTongueState(1);
                }
                else
                {
                    data.chaCtrl.ChangeTongueState(0);                
                }
                data.TongueCategoryEnabled = enableTongue;
            }

            GUI.enabled = prevEnabled;
        }

        private void DrawTearDropActivationButton(FacialQuickTransformData data)
        {
            bool canActivateTearDrop = data != null && data.chaCtrl != null;
            bool prevEnabled = GUI.enabled;
            GUI.enabled = canActivateTearDrop;

            string label = data != null && data.TearDropActive ? "Tear Active: ON" : "Tear Active: OFF";
            if (GUILayout.Button(new GUIContent(label, "Toggle tear drop effect coroutine")))
            {
                bool enableTearDrop = !data.TearDropActive;
                FacialQuickTransformController controller = GetCurrentControl();
                if (controller != null)
                    controller.SetTearDropActive(enableTearDrop);
                else
                    data.TearDropActive = enableTearDrop;
            }

            GUI.enabled = prevEnabled;
        }

        private void DrawExpressionControl(FacialQuickTransformData data, float step)
        {
            GUILayout.Space(6f);

            ExpressionCategoryUi[] categories = GetExpressionUiCategories(data);
            _selectedCategoryIndex = Mathf.Clamp(_selectedCategoryIndex, 0, categories.Length - 1);
            ExpressionCategoryUi selectedCategory = categories[_selectedCategoryIndex];
            _selectedTargetIndex = Mathf.Clamp(_selectedTargetIndex, 0, selectedCategory.Targets.Length - 1);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(160));
            GUILayout.Label("<color=orange>Category</color>", RichLabel);
            _categoryScroll = GUILayout.BeginScrollView(_categoryScroll, GUI.skin.box, GUILayout.Height(200));
            for (int i = 0; i < categories.Length; i++)
            {
                ExpressionCategoryUi category = categories[i];
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

            selectedCategory = categories[_selectedCategoryIndex];
            _selectedTargetIndex = Mathf.Clamp(_selectedTargetIndex, 0, selectedCategory.Targets.Length - 1);

            GUILayout.BeginVertical(GUILayout.Width(160));
            GUILayout.Label("<color=orange>Target</color>", RichLabel);
            _targetScroll = GUILayout.BeginScrollView(_targetScroll, GUI.skin.box, GUILayout.Height(100));
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
            if (categoryId == ExpressionCategoryId.Eyebrow)
            {
                if (targetId == ExpressionTargetId.Type)
                {
                    data.EyebrowTypeIndex = DrawIntFieldRow("Type", data.EyebrowTypeIndex, 0, 8);
                }
                return;
            }

            if (categoryId == ExpressionCategoryId.Eye)
            {
                if (targetId == ExpressionTargetId.Type)
                {
                    data.EyeTypeIndex = DrawIntFieldRow("Type", data.EyeTypeIndex, 0, 20);
                    data.EyeOpenMax = DrawClampedFieldRow("Open", data.EyeOpenMax, step, 0f, 1f, 1f);
                    return;
                }

                if (targetId == ExpressionTargetId.Both
                    || targetId == ExpressionTargetId.Left
                    || targetId == ExpressionTargetId.Right)
                {
                    float currentX = GetEyeBallTargetAxisValue(data, targetId, true);
                    float currentY = GetEyeBallTargetAxisValue(data, targetId, false);
                    float updatedX = DrawAngleFieldRow("X Rotate", currentX, step, EyeBallMinX, EyeBallMaxX);
                    float updatedY = DrawAngleFieldRow("Y Rotate", currentY, step, EyeBallMinY, EyeBallMaxY);
                    SetEyeBallTargetAxisValue(data, targetId, true, updatedX);
                    SetEyeBallTargetAxisValue(data, targetId, false, updatedY);
                    return;
                }

                if (targetId == ExpressionTargetId.Smile)
                {
                    float smileIn = DrawAngleFieldRow("in", data.EyeSmileInRotX, step, EyeRotationMinX, EyeRotationMaxX);
                    float smileOut = DrawAngleFieldRow("out", data.EyeSmileOutRotX, step, EyeRotationMinX, EyeRotationMaxX);
                    data.EyeSmileInRotX = smileIn;
                    data.EyeSmileOutRotX = smileOut;
                    return;
                }

                return;
            }

            if (categoryId == ExpressionCategoryId.Tongue)
            {
                bool isTongue1 = targetId == ExpressionTargetId.Tongue1;
                float posZ = isTongue1 ? data.Tongue1PosZ : data.Tongue2PosZ;

                float updatedPosZ = DrawAngleFieldRow("Z Pos", posZ, step, FacePositionMin, FacePositionMax);
                if (isTongue1)
                {
                    float updatedRotY = DrawAngleFieldRow("Y Rot", data.Tongue1RotY, step, FaceRotationMin, FaceRotationMax);
                    data.Tongue1PosZ = updatedPosZ;
                    data.Tongue1RotY = updatedRotY;
                }
                else
                {
                    float updatedRotX = DrawAngleFieldRow("X Rot", data.Tongue2RotX, step, FaceRotationMin, FaceRotationMax);
                    float updatedRotY = DrawAngleFieldRow("Y Rot", data.Tongue2RotY, step, FaceRotationMin, FaceRotationMax);
                    data.Tongue2PosZ = updatedPosZ;
                    data.Tongue2RotX = updatedRotX;
                    data.Tongue2RotY = updatedRotY;
                }
                return;
            }

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

            if (categoryId == ExpressionCategoryId.EyeLid)
            {
                if (targetId == ExpressionTargetId.Smile)
                {
                    float smileIn = DrawAngleFieldRow("in", data.EyeSmileInRotX, step, EyeRotationMinX, EyeRotationMaxX);
                    float smileOut = DrawAngleFieldRow("out", data.EyeSmileOutRotX, step, EyeRotationMinX, EyeRotationMaxX);
                    data.EyeSmileInRotX = smileIn;
                    data.EyeSmileOutRotX = smileOut;
                }
                else
                {
                    float winkLeft = DrawAngleFieldRow("left", data.EyeWinkLeftRotX, step, EyeRotationMinX, EyeRotationMaxX);
                    float winkRight = DrawAngleFieldRow("right", data.EyeWinkRightRotX, step, EyeRotationMinX, EyeRotationMaxX);
                    data.EyeWinkLeftRotX = winkLeft;
                    data.EyeWinkRightRotX = winkRight;
                }
                return;
            }

            if (categoryId == ExpressionCategoryId.Mouth)
            {

                if (targetId == ExpressionTargetId.Tooth)
                {
                    data.MouthCavityPosZ = DrawAngleFieldRow("Z Pos", data.MouthCavityPosZ, step, FacePositionMin, FacePositionMax);
                    return;
                }

                if (targetId == ExpressionTargetId.Type)
                {
                    data.MouthTypeIndex = DrawIntFieldRow("Type", data.MouthTypeIndex, 0, MouthTypePatternMap.Length - 1);
                    data.MouthOpenMax = DrawClampedFieldRow("Open", data.MouthOpenMax, step, 0f, 1f, 1f);
                    return;
                }

                if (targetId == ExpressionTargetId.Smile)
                {
                    float rightPair = GetSmilePairValue(data.MouthSmileRightPosX, data.MouthSmileRightPosY);

                    float updatedRightPair = DrawAngleFieldRow("Right", rightPair, step, FacePositionMin, FacePositionMax);

                    SetSmilePair(ref data.MouthSmileRightPosX, ref data.MouthSmileRightPosY, updatedRightPair, false, false);
                    SetSmilePair(ref data.MouthSmileLeftPosX, ref data.MouthSmileLeftPosY, updatedRightPair, true, false);
                    return;
                }

                return;
            }

            if (categoryId == ExpressionCategoryId.Lip)
            {
                if (targetId == ExpressionTargetId.Both)
                {
                    float mouthX = DrawAngleFieldRow("X Rot", data.MouthRotX, step, FaceRotationMin, FaceRotationMax);
                    float mouthY = DrawAngleFieldRow("Y Rot", data.MouthRotY, step, FaceRotationMin, FaceRotationMax);
                    float mouthZ = DrawAngleFieldRow("Z Rot", data.MouthRotZ, step, FaceRotationMin, FaceRotationMax);
                    data.MouthRotX = mouthX;
                    data.MouthRotY = mouthY;
                    data.MouthRotZ = mouthZ;
                    return;
                }

                bool isUp = targetId == ExpressionTargetId.LipUp;
                float currentX = isUp ? data.MouthLipUpRotX : data.MouthLipDnRotX;
                float currentY = isUp ? data.MouthLipUpRotY : data.MouthLipDnRotY;
                float currentZ = isUp ? data.MouthLipUpRotZ : data.MouthLipDnRotZ;
                float updatedX = DrawAngleFieldRow("X Rot", currentX, step, FaceRotationMin, FaceRotationMax);
                float updatedY = DrawAngleFieldRow("Y Rot", currentY, step, FaceRotationMin, FaceRotationMax);
                float updatedZ = DrawAngleFieldRow("Z Rot", currentZ, step, FaceRotationMin, FaceRotationMax);
                if (isUp)
                {
                    data.MouthLipUpRotX = updatedX;
                    data.MouthLipUpRotY = updatedY;
                    data.MouthLipUpRotZ = updatedZ;
                }
                else
                {
                    data.MouthLipDnRotX = updatedX;
                    data.MouthLipDnRotY = updatedY;
                    data.MouthLipDnRotZ = updatedZ;
                }
                return;
            }

            if (categoryId == ExpressionCategoryId.NoseWing)
            {
                if (targetId == ExpressionTargetId.Both)
                {
                    float pairX = GetNoseWingPairAxisValue(data, AxisId.X);
                    float pairY = GetNoseWingPairAxisValue(data, AxisId.Y);
                    float pairZ = GetNoseWingPairAxisValue(data, AxisId.Z);
                    float pairUpdatedX = DrawAngleFieldRow("X Rot", pairX, step, FaceRotationMin, FaceRotationMax);
                    float pairUpdatedY = DrawAngleFieldRow("Y Rot", pairY, step, FaceRotationMin, FaceRotationMax);
                    float pairUpdatedZ = DrawAngleFieldRow("Z Rot", pairZ, step, FaceRotationMin, FaceRotationMax);
                    SetNoseWingPairAxisValue(data, AxisId.X, pairUpdatedX);
                    SetNoseWingPairAxisValue(data, AxisId.Y, pairUpdatedY);
                    SetNoseWingPairAxisValue(data, AxisId.Z, pairUpdatedZ);
                    return;
                }

                bool isLeft = targetId == ExpressionTargetId.Left;
                float currentX = GetNoseWingSingleAxisValue(data, isLeft, AxisId.X);
                float currentY = GetNoseWingSingleAxisValue(data, isLeft, AxisId.Y);
                float currentZ = GetNoseWingSingleAxisValue(data, isLeft, AxisId.Z);
                float updatedX = DrawAngleFieldRow("X Rot", currentX, step, FaceRotationMin, FaceRotationMax);
                float updatedY = DrawAngleFieldRow("Y Rot", currentY, step, FaceRotationMin, FaceRotationMax);
                float updatedZ = DrawAngleFieldRow("Z Rot", currentZ, step, FaceRotationMin, FaceRotationMax);
                SetNoseWingSingleAxisValue(data, isLeft, AxisId.X, updatedX);
                SetNoseWingSingleAxisValue(data, isLeft, AxisId.Y, updatedY);
                SetNoseWingSingleAxisValue(data, isLeft, AxisId.Z, updatedZ);
                return;
            }

            if (categoryId == ExpressionCategoryId.Nose)
            {
                if (targetId == ExpressionTargetId.Shape)
                {
                    float currentXNose = GetFaceAxisValue(data, categoryId, AxisId.X);
                    float updatedXNose = DrawAngleFieldRow("X Rot", currentXNose, step, FaceRotationMin, FaceRotationMax);
                    SetFaceAxisValue(data, categoryId, AxisId.X, updatedXNose);
                    return;
                }

                if (targetId == ExpressionTargetId.Both)
                {
                    float pairX = GetNoseWingPairAxisValue(data, AxisId.X);
                    float pairY = GetNoseWingPairAxisValue(data, AxisId.Y);
                    float pairZ = GetNoseWingPairAxisValue(data, AxisId.Z);
                    float pairUpdatedX = DrawAngleFieldRow("X Rot", pairX, step, FaceRotationMin, FaceRotationMax);
                    float pairUpdatedY = DrawAngleFieldRow("Y Rot", pairY, step, FaceRotationMin, FaceRotationMax);
                    float pairUpdatedZ = DrawAngleFieldRow("Z Rot", pairZ, step, FaceRotationMin, FaceRotationMax);
                    SetNoseWingPairAxisValue(data, AxisId.X, pairUpdatedX);
                    SetNoseWingPairAxisValue(data, AxisId.Y, pairUpdatedY);
                    SetNoseWingPairAxisValue(data, AxisId.Z, pairUpdatedZ);
                    return;
                }

                bool isLeft = targetId == ExpressionTargetId.Left;
                float currentX = GetNoseWingSingleAxisValue(data, isLeft, AxisId.X);
                float currentY = GetNoseWingSingleAxisValue(data, isLeft, AxisId.Y);
                float currentZ = GetNoseWingSingleAxisValue(data, isLeft, AxisId.Z);
                float updatedX = DrawAngleFieldRow("X Rot", currentX, step, FaceRotationMin, FaceRotationMax);
                float updatedY = DrawAngleFieldRow("Y Rot", currentY, step, FaceRotationMin, FaceRotationMax);
                float updatedZ = DrawAngleFieldRow("Z Rot", currentZ, step, FaceRotationMin, FaceRotationMax);
                SetNoseWingSingleAxisValue(data, isLeft, AxisId.X, updatedX);
                SetNoseWingSingleAxisValue(data, isLeft, AxisId.Y, updatedY);
                SetNoseWingSingleAxisValue(data, isLeft, AxisId.Z, updatedZ);
                return;
            }
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
                return data.EyeTypeIndex != 0
                    || Mathf.Abs(data.EyeOpenMax - 1f) > ValueCompareEpsilon
                    || IsModifiedValue(data.EyeBallLeftX)
                    || IsModifiedValue(data.EyeBallLeftY)
                    || IsModifiedValue(data.EyeBallRightX)
                    || IsModifiedValue(data.EyeBallRightY)
                    || IsModifiedValue(data.EyeSmileInRotX)
                    || IsModifiedValue(data.EyeSmileOutRotX);
            }
            if (categoryId == ExpressionCategoryId.Eyebrow)
                return data.EyebrowTypeIndex != 0;

            if (categoryId == ExpressionCategoryId.EyeLid)
            {
                return IsModifiedValue(data.EyeSmileInRotX)
                    || IsModifiedValue(data.EyeSmileOutRotX)
                    || IsModifiedValue(data.EyeWinkLeftRotX)
                    || IsModifiedValue(data.EyeWinkRightRotX);
            }

            if (categoryId == ExpressionCategoryId.Mouth)
            {
                return IsModifiedValue(data.MouthCavityPosZ)
                    || data.MouthTypeIndex != 0
                    || Mathf.Abs(data.MouthOpenMax - 1f) > ValueCompareEpsilon
                    || IsModifiedValue(data.MouthSmileLeftPosX)
                    || IsModifiedValue(data.MouthSmileLeftPosY)
                    || IsModifiedValue(data.MouthSmileRightPosX)
                    || IsModifiedValue(data.MouthSmileRightPosY);
            }

            if (categoryId == ExpressionCategoryId.Lip)
            {
                return IsModifiedValue(data.MouthRotX)
                    || IsModifiedValue(data.MouthRotY)
                    || IsModifiedValue(data.MouthRotZ)
                    || IsModifiedValue(data.MouthLipUpRotX)
                    || IsModifiedValue(data.MouthLipUpRotY)
                    || IsModifiedValue(data.MouthLipUpRotZ)
                    || IsModifiedValue(data.MouthLipDnRotX)
                    || IsModifiedValue(data.MouthLipDnRotY)
                    || IsModifiedValue(data.MouthLipDnRotZ);
            }

            if (categoryId == ExpressionCategoryId.Nose)
            {
                return IsModifiedValue(data.NoseRotX)
                    || IsModifiedValue(data.NoseWingLeftRotX)
                    || IsModifiedValue(data.NoseWingLeftRotY)
                    || IsModifiedValue(data.NoseWingLeftRotZ)
                    || IsModifiedValue(data.NoseWingRightRotX)
                    || IsModifiedValue(data.NoseWingRightRotY)
                    || IsModifiedValue(data.NoseWingRightRotZ);
            }

            if (categoryId == ExpressionCategoryId.NoseWing)
            {
                return IsModifiedValue(data.NoseWingLeftRotX)
                    || IsModifiedValue(data.NoseWingLeftRotY)
                    || IsModifiedValue(data.NoseWingLeftRotZ)
                    || IsModifiedValue(data.NoseWingRightRotX)
                    || IsModifiedValue(data.NoseWingRightRotY)
                    || IsModifiedValue(data.NoseWingRightRotZ);
            }
            if (categoryId == ExpressionCategoryId.Tongue)
            {
                return IsModifiedValue(data.Tongue1PosZ)
                    || IsModifiedValue(data.Tongue1RotY)
                    || IsModifiedValue(data.Tongue2PosZ)
                    || IsModifiedValue(data.Tongue2RotX)
                    || IsModifiedValue(data.Tongue2RotY);
            }

            return false;
        }

        private bool IsTargetModified(FacialQuickTransformData data, ExpressionCategoryId categoryId, ExpressionTargetId targetId)
        {
            if (categoryId == ExpressionCategoryId.EyeBall)
            {
                return IsModifiedValue(GetEyeBallTargetAxisValue(data, targetId, true))
                    || IsModifiedValue(GetEyeBallTargetAxisValue(data, targetId, false));
            }

            if (categoryId == ExpressionCategoryId.Eye)
            {
                if (targetId == ExpressionTargetId.Type)
                    return data.EyeTypeIndex != 0
                        || Mathf.Abs(data.EyeOpenMax - 1f) > ValueCompareEpsilon;
                if (targetId == ExpressionTargetId.Both
                    || targetId == ExpressionTargetId.Left
                    || targetId == ExpressionTargetId.Right)
                {
                    return IsModifiedValue(GetEyeBallTargetAxisValue(data, targetId, true))
                        || IsModifiedValue(GetEyeBallTargetAxisValue(data, targetId, false));
                }
                if (targetId == ExpressionTargetId.Smile)
                    return IsModifiedValue(data.EyeSmileInRotX) || IsModifiedValue(data.EyeSmileOutRotX);
                return false;
            }
            if (categoryId == ExpressionCategoryId.Eyebrow)
            {
                return data.EyebrowTypeIndex != 0;
            }

            if (categoryId == ExpressionCategoryId.EyeLid)
            {
                if (targetId == ExpressionTargetId.Smile)
                    return IsModifiedValue(data.EyeSmileInRotX) || IsModifiedValue(data.EyeSmileOutRotX);
                return IsModifiedValue(data.EyeWinkLeftRotX) || IsModifiedValue(data.EyeWinkRightRotX);
            }

            if (categoryId == ExpressionCategoryId.Mouth)
            {
                if (targetId == ExpressionTargetId.Smile)
                {
                    return IsModifiedValue(data.MouthSmileLeftPosX)
                        || IsModifiedValue(data.MouthSmileLeftPosY)
                        || IsModifiedValue(data.MouthSmileRightPosX)
                        || IsModifiedValue(data.MouthSmileRightPosY);
                }

                if (targetId == ExpressionTargetId.Tooth)
                    return IsModifiedValue(data.MouthCavityPosZ);

                return data.MouthTypeIndex != 0
                    || Mathf.Abs(data.MouthOpenMax - 1f) > ValueCompareEpsilon;
            }

            if (categoryId == ExpressionCategoryId.Lip)
            {
                if (targetId == ExpressionTargetId.Both)
                {
                    return IsModifiedValue(data.MouthRotX)
                        || IsModifiedValue(data.MouthRotY)
                        || IsModifiedValue(data.MouthRotZ);
                }

                if (targetId == ExpressionTargetId.LipUp)
                {
                    return IsModifiedValue(data.MouthLipUpRotX)
                        || IsModifiedValue(data.MouthLipUpRotY)
                        || IsModifiedValue(data.MouthLipUpRotZ);
                }

                return IsModifiedValue(data.MouthLipDnRotX)
                    || IsModifiedValue(data.MouthLipDnRotY)
                    || IsModifiedValue(data.MouthLipDnRotZ);
            }


            if (categoryId == ExpressionCategoryId.NoseWing)
            {
                if (targetId == ExpressionTargetId.Both)
                {
                    return IsModifiedValue(GetNoseWingPairAxisValue(data, AxisId.X))
                        || IsModifiedValue(GetNoseWingPairAxisValue(data, AxisId.Y))
                        || IsModifiedValue(GetNoseWingPairAxisValue(data, AxisId.Z));
                }

                if (targetId == ExpressionTargetId.Left)
                {
                    return IsModifiedValue(data.NoseWingLeftRotX)
                        || IsModifiedValue(data.NoseWingLeftRotY)
                        || IsModifiedValue(data.NoseWingLeftRotZ);
                }

                return IsModifiedValue(data.NoseWingRightRotX)
                    || IsModifiedValue(data.NoseWingRightRotY)
                    || IsModifiedValue(data.NoseWingRightRotZ);
            }

            if (categoryId == ExpressionCategoryId.Nose)
            {
                if (targetId == ExpressionTargetId.Shape)
                    return IsModifiedValue(data.NoseRotX);
                if (targetId == ExpressionTargetId.Both)
                {
                    return IsModifiedValue(GetNoseWingPairAxisValue(data, AxisId.X))
                        || IsModifiedValue(GetNoseWingPairAxisValue(data, AxisId.Y))
                        || IsModifiedValue(GetNoseWingPairAxisValue(data, AxisId.Z));
                }
                if (targetId == ExpressionTargetId.Left)
                {
                    return IsModifiedValue(data.NoseWingLeftRotX)
                        || IsModifiedValue(data.NoseWingLeftRotY)
                        || IsModifiedValue(data.NoseWingLeftRotZ);
                }
                return IsModifiedValue(data.NoseWingRightRotX)
                    || IsModifiedValue(data.NoseWingRightRotY)
                    || IsModifiedValue(data.NoseWingRightRotZ);
            }

            if (categoryId == ExpressionCategoryId.Tongue)
            {
                if (targetId == ExpressionTargetId.Tongue1)
                {
                    return IsModifiedValue(data.Tongue1PosZ)
                        || IsModifiedValue(data.Tongue1RotY);
                }
                return IsModifiedValue(data.Tongue2PosZ)
                    || IsModifiedValue(data.Tongue2RotX)
                    || IsModifiedValue(data.Tongue2RotY);
            }

            return false;
        }

        private ExpressionCategoryUi[] GetExpressionUiCategories(FacialQuickTransformData data)
        {
            if (data != null && data.TongueCategoryEnabled)
            {
                ExpressionCategoryUi[] categories = new ExpressionCategoryUi[BaseExpressionUiCategories.Length + 1];
                Array.Copy(BaseExpressionUiCategories, categories, BaseExpressionUiCategories.Length);
                categories[categories.Length - 1] = TongueExpressionCategory;
                return categories;
            }

            return BaseExpressionUiCategories;
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

            bool isLeft = targetId == ExpressionTargetId.Left;
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

            bool isLeft = targetId == ExpressionTargetId.Left;
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

        private float GetFaceAxisValue(FacialQuickTransformData data, ExpressionCategoryId categoryId, AxisId axisId)
        {
            switch (categoryId)
            {
                case ExpressionCategoryId.Nose:
                    if (axisId == AxisId.X) return data.NoseRotX;
                    if (axisId == AxisId.Y) return data.NoseRotY;
                    return data.NoseRotZ;
                default:
                    return 0f;
            }
        }

        private void SetFaceAxisValue(FacialQuickTransformData data, ExpressionCategoryId categoryId, AxisId axisId, float value)
        {
            switch (categoryId)
            {
                case ExpressionCategoryId.Nose:
                    if (axisId == AxisId.X) data.NoseRotX = value;
                    else if (axisId == AxisId.Y) data.NoseRotY = value;
                    else data.NoseRotZ = value;
                    break;
                default:
                    break;
            }
        }

        private float GetNoseWingPairAxisValue(FacialQuickTransformData data, AxisId axisId)
        {
            float left = GetNoseWingSingleAxisValue(data, true, axisId);
            float right = GetNoseWingSingleAxisValue(data, false, axisId);
            return (left + right) * 0.5f;
        }

        private void SetNoseWingPairAxisValue(FacialQuickTransformData data, AxisId axisId, float value)
        {
            SetNoseWingSingleAxisValue(data, true, axisId, value);
            SetNoseWingSingleAxisValue(data, false, axisId, value);
        }

        private float GetNoseWingSingleAxisValue(FacialQuickTransformData data, bool isLeft, AxisId axisId)
        {
            if (isLeft)
            {
                if (axisId == AxisId.X) return data.NoseWingLeftRotX;
                if (axisId == AxisId.Y) return data.NoseWingLeftRotY;
                return data.NoseWingLeftRotZ;
            }

            if (axisId == AxisId.X) return data.NoseWingRightRotX;
            if (axisId == AxisId.Y) return data.NoseWingRightRotY;
            return data.NoseWingRightRotZ;
        }

        private void SetNoseWingSingleAxisValue(FacialQuickTransformData data, bool isLeft, AxisId axisId, float value)
        {
            if (isLeft)
            {
                if (axisId == AxisId.X) data.NoseWingLeftRotX = value;
                else if (axisId == AxisId.Y) data.NoseWingLeftRotY = value;
                else data.NoseWingLeftRotZ = value;
                return;
            }

            if (axisId == AxisId.X) data.NoseWingRightRotX = value;
            else if (axisId == AxisId.Y) data.NoseWingRightRotY = value;
            else data.NoseWingRightRotZ = value;
        }

        private float DrawAngleFieldRow(string label, float value, float step, float min, float max)
        {
            return DrawClampedFieldRow(label, value, step, min, max, 0f);
        }

        private float DrawClampedFieldRow(string label, float value, float step, float min, float max, float resetValue)
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
                value = Mathf.Clamp(resetValue, min, max);
            GUILayout.EndHorizontal();
            return value;
        }

        private int DrawIntFieldRow(string label, int value, int min, int max)
        {
            value = Mathf.Clamp(value, min, max);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(80));
            if (GUILayout.Button("-", GUILayout.Width(22)))
                value -= 1;
            value = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max));
            if (GUILayout.Button("+", GUILayout.Width(22)))
                value += 1;
            value = Mathf.Clamp(value, min, max);
            GUILayout.Label(value.ToString(CultureInfo.InvariantCulture), GUILayout.Width(44));
            if (GUILayout.Button("Reset", GUILayout.Width(52)))
                value = min;
            GUILayout.EndHorizontal();
            return value;
        }

        private float GetSmilePairValue(float x, float y)
        {
            return (x + y) * 0.5f;
        }

        private void SetSmilePair(ref float x, ref float y, float value, bool invertX, bool invertY)
        {
            x = invertX ? -value : value;
            y = invertY ? -value : value;
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

            ApplyEyebrowType(data);
            ApplyEyeType(data);
            ApplyEyeOpenMax(data);

            if (!data._eye_base_rot_ready)
                data.CaptureEyeBaseTransformRotations();

            float smileIn = data.EyeSmileInRotX;
            float smileOut = data.EyeSmileOutRotX;

            // smile_in: Eye01, smile_out: Eye03
            ApplyEyePairRotationX(data._eye_01_L, data._eye_01_base_rot_L, smileIn);
            ApplyEyePairRotationX(data._eye_02_L, data._eye_02_base_rot_L, 0f);
            ApplyEyePairRotationX(data._eye_03_L, data._eye_03_base_rot_L, smileOut);
            ApplyEyePairRotationX(data._eye_04_L, data._eye_04_base_rot_L, 0f);

            ApplyEyePairRotationX(data._eye_01_R, data._eye_01_base_rot_R, smileIn);
            ApplyEyePairRotationX(data._eye_02_R, data._eye_02_base_rot_R, 0f);
            ApplyEyePairRotationX(data._eye_03_R, data._eye_03_base_rot_R, smileOut);
            ApplyEyePairRotationX(data._eye_04_R, data._eye_04_base_rot_R, 0f);
        }

        private void ApplyEyebrowType(FacialQuickTransformData data)
        {
            if (data == null || data.chaCtrl == null)
                return;

            int typeIndex = Mathf.Clamp(data.EyebrowTypeIndex, 0, 8);
            if (data.EyebrowTypeIndex != typeIndex)
                data.EyebrowTypeIndex = typeIndex;

            if (data.EyebrowTypeLastAppliedIndex == typeIndex)
                return;

            data.chaCtrl.ChangeEyebrowPtn(typeIndex, true);
            data.EyebrowTypeLastAppliedIndex = typeIndex;
        }

        private void ApplyEyeType(FacialQuickTransformData data)
        {
            if (data == null || data.chaCtrl == null)
                return;

            int typeIndex = Mathf.Clamp(data.EyeTypeIndex, 0, 20);
            if (data.EyeTypeIndex != typeIndex)
                data.EyeTypeIndex = typeIndex;

            if (data.EyeTypeLastAppliedIndex == typeIndex)
                return;

            data.chaCtrl.ChangeEyesPtn(typeIndex, true);
            data.EyeTypeLastAppliedIndex = typeIndex;
        }

        private void ApplyEyeOpenMax(FacialQuickTransformData data)
        {
            if (data == null || data.chaCtrl == null)
                return;

            float maxValue = Mathf.Clamp(data.EyeOpenMax, 0f, 1f);
            if (!Mathf.Approximately(data.EyeOpenMax, maxValue))
                data.EyeOpenMax = maxValue;

            if (Mathf.Abs(data.EyeOpenMaxLastApplied - maxValue) <= ValueCompareEpsilon)
                return;

            data.chaCtrl.ChangeEyesOpenMax(maxValue);
            data.EyeOpenMaxLastApplied = maxValue;
        }

        private void ApplyFaceTransforms(FacialQuickTransformData data)
        {
            if (data == null)
                return;

            if (!data._mouth_base_ready
                || !data._nose_base_ready
                || !data._tongue_base_ready
                || (data._mouth_cavity != null && !data._mouth_cavity_base_pos_ready)
                || (data._mouth_smile_l != null && !data._mouth_smile_base_pos_ready_l)
                || (data._mouth_smile_r != null && !data._mouth_smile_base_pos_ready_r)
                || (data._tongue_s1 != null && !data._tongue_s1_base_pos_ready)
                || (data._tongue_s2 != null && !data._tongue_s2_base_pos_ready))
                data.CaptureFaceBaseTransforms();

            ApplyFaceTransform(
                data._mouth,
                data._mouth_base_rot,
                data.MouthRotX,
                data.MouthRotY,
                data.MouthRotZ);
            ApplyFaceTransform(
                data._mouth_lip_up,
                data._mouth_lip_up_base_rot,
                data.MouthRotX + data.MouthLipUpRotX,
                data.MouthRotY + data.MouthLipUpRotY,
                data.MouthRotZ + data.MouthLipUpRotZ);
            ApplyFaceTransform(
                data._mouth_lip_dn,
                data._mouth_lip_dn_base_rot,
                data.MouthRotX + data.MouthLipDnRotX,
                data.MouthRotY + data.MouthLipDnRotY,
                data.MouthRotZ + data.MouthLipDnRotZ);
            ApplyFacePositionZ(
                data._mouth_cavity,
                data._mouth_cavity_base_pos,
                data.MouthCavityPosZ);
            ApplyMouthOpenMax(data);
            ApplyMouthType(data);
            ApplyFacePositionXY(
                data._mouth_smile_l,
                data._mouth_smile_base_pos_l,
                data.MouthSmileLeftPosX,
                data.MouthSmileLeftPosY);
            ApplyFacePositionXY(
                data._mouth_smile_r,
                data._mouth_smile_base_pos_r,
                data.MouthSmileRightPosX,
                data.MouthSmileRightPosY);

            ApplyFaceTransform(
                data._nose,
                data._nose_base_rot,
                data.NoseRotX,
                data.NoseRotY,
                data.NoseRotZ);
            ApplyFaceTransform(
                data._nose_wing_l,
                data._nose_wing_base_rot_l,
                data.NoseRotX + data.NoseWingLeftRotX,
                data.NoseRotY + data.NoseWingLeftRotY,
                data.NoseRotZ + data.NoseWingLeftRotZ);
            ApplyFaceTransform(
                data._nose_wing_r,
                data._nose_wing_base_rot_r,
                data.NoseRotX + data.NoseWingRightRotX,
                data.NoseRotY + data.NoseWingRightRotY,
                data.NoseRotZ + data.NoseWingRightRotZ);

            ApplyFaceTransform(
                data._tongue_s1,
                data._tongue_s1_base_rot,
                0f,
                data.Tongue1RotY,
                0f);
            ApplyFacePositionZ(
                data._tongue_s1,
                data._tongue_s1_base_pos,
                data.Tongue1PosZ);

            ApplyFaceTransform(
                data._tongue_s2,
                data._tongue_s2_base_rot,
                data.Tongue2RotX,
                data.Tongue2RotY,
                0f);
            ApplyFacePositionZ(
                data._tongue_s2,
                data._tongue_s2_base_pos,
                data.Tongue2PosZ);
        }

        private void ApplyMouthType(FacialQuickTransformData data)
        {
            if (data == null || data.chaCtrl == null)
                return;

            int typeIndex = Mathf.Clamp(data.MouthTypeIndex, 0, MouthTypePatternMap.Length - 1);
            if (data.MouthTypeIndex != typeIndex)
                data.MouthTypeIndex = typeIndex;

            if (data.MouthTypeLastAppliedIndex == typeIndex)
                return;

            int patternValue = MouthTypePatternMap[typeIndex];
            data.chaCtrl.ChangeMouthPtn(patternValue, true);
            data.MouthTypeLastAppliedIndex = typeIndex;
        }

        private void ApplyMouthOpenMax(FacialQuickTransformData data)
        {
            if (data == null || data.chaCtrl == null)
                return;

            float maxValue = Mathf.Clamp(data.MouthOpenMax, 0f, 1f);
            if (!Mathf.Approximately(data.MouthOpenMax, maxValue))
                data.MouthOpenMax = maxValue;

            if (Mathf.Abs(data.MouthOpenMaxLastApplied - maxValue) <= ValueCompareEpsilon)
                return;

            data.chaCtrl.ChangeMouthOpenMax(maxValue);
            data.MouthOpenMaxLastApplied = maxValue;
        }

        private void ApplyFaceTransform(Transform target, Quaternion baseRot, float rx, float ry, float rz)
        {
            if (target == null)
                return;

            target.localRotation = baseRot * Quaternion.Euler(rx, ry, rz);
        }

        private void ApplyFacePositionZ(Transform target, Vector3 basePos, float zOffset)
        {
            if (target == null)
                return;

            target.localPosition = new Vector3(basePos.x, basePos.y, basePos.z + zOffset);
        }

        private void ApplyFacePositionXY(Transform target, Vector3 basePos, float xOffset, float yOffset)
        {
            if (target == null)
                return;

            target.localPosition = new Vector3(basePos.x + xOffset, basePos.y + yOffset, basePos.z);
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
            // Visualization disabled: do not render eye markers.
            SetEyeBallMarkerVisible(false, false);
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
            string bundlePath = Application.dataPath + "/../abdata/facialQuickTransform/facialquickbundle.unity3d";

            _bundle = AssetBundle.LoadFromFile(bundlePath);
            if (_bundle == null)
            {
                Logger.LogMessage("TearDrop resource bundle not found: facialQuickTransform.unity3d");
                return;
            }

            _tearDropImg = _bundle.LoadAsset<Texture2D>("teardrop");
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

    //    [HarmonyPatch(typeof(FaceBlendShape), "OnLateUpdate")]
    //     private class FaceBlendShape_Patches
    //     {
    //         [HarmonyAfter("com.joan6694.hsplugins.instrumentation")]
    //         public static void Postfix(FaceBlendShape __instance)
    //         {
    //             if (StudioAPI.InsideStudio)
    //             {
    //                 if (!_self._loaded)
    //                     return;

    //                 if (Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes == null || Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() == 0)
    //                     return;
                    
    //                 TreeNodeObject _node = Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Last();
    //                 ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);
    //                 OCIChar ociChar = objectCtrlInfo as OCIChar;
    //                 ChaControl chaControl = ociChar.GetChaControl();

    //                 var controller = chaControl.GetComponent<FacialQuickTransformController>();
    //                 if (controller != null)
    //                 {
    //                     FacialQuickTransformData expressionData = controller.GetData();

    //                     foreach (var fbsTarget in __instance.EyesCtrl.FBSTarget)
    //                     {
    //                         SkinnedMeshRenderer srender = fbsTarget.GetSkinnedMeshRenderer();
    //                         var mesh = srender.sharedMesh;
    //                         if (mesh && mesh.blendShapeCount > 0)
    //                         {
    //                             string name = mesh.GetBlendShapeName(0);
    //                             if (name.Contains("head"))
    //                             {
    //                                 srender
    //                                     .SetBlendShapeWeight(expressionData.eye_close_idx_in_head_of_eyectrl, 0f);

    //                                 srender
    //                                     .SetBlendShapeWeight(expressionData.eye_wink_idx_in_head_of_eyectrl, 0);
    //                             }
    //                             else if (name.Contains("namida"))
    //                             {
    //                                 srender
    //                                         .SetBlendShapeWeight(expressionData.eye_close_idx_in_namida_of_eyectrl, 0f);

    //                                 srender
    //                                     .SetBlendShapeWeight(expressionData.eye_wink_idx_in_namida_of_eyectrl, 0);
    //                             }
    //                             else if (name.Contains("lash."))
    //                             {
    //                                 srender
    //                                     .SetBlendShapeWeight(expressionData.eye_close_idx_in_lash_of_eyectrl, 0f);

    //                                 srender
    //                                     .SetBlendShapeWeight(expressionData.eye_wink_idx_in_lash_of_eyectrl, 0);
    //                             }
    //                         }
    //                     }

    //                     foreach (var fbsTarget in __instance.MouthCtrl.FBSTarget)
    //                     {
    //                         SkinnedMeshRenderer srender = fbsTarget.GetSkinnedMeshRenderer();
    //                         var mesh = srender.sharedMesh;
    //                         if (mesh && mesh.blendShapeCount > 0)
    //                         {
    //                             string name = mesh.GetBlendShapeName(0);
    //                             if (name.Contains("head"))
    //                             {
    //                                 srender
    //                                     .SetBlendShapeWeight(expressionData.eye_close_idx_in_head_of_mouthctrl, 0f);

    //                                 srender
    //                                     .SetBlendShapeWeight(expressionData.eye_wink_idx_in_head_of_mouthctrl, 0);
    //                                 srender
    //                                                 .SetBlendShapeWeight(38, 100);

    //                             }
    //                             else if (name.Contains("namida"))
    //                             {
    //                                 srender
    //                                     .SetBlendShapeWeight(expressionData.eye_close_idx_in_namida_of_mouthctrl, 0f);

    //                                 srender
    //                                     .SetBlendShapeWeight(expressionData.eye_wink_idx_in_namida_of_mouthctrl, 0);
    //                             }
    //                         }
    //                     }
    //                 }
                                        
    //             }
    //         }
    //     }         
    }
}
