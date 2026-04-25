using Studio;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ToolBox;
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
#endif
using KKAPI.Chara;
using KKAPI.Studio;
using KKAPI.Studio.UI.Toolbars;
using KKAPI.Utilities;
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

        private enum CorrectionFieldId
        {
            Mouth,
            Eye,
            Eyeline,
            Expression
        }

        internal static new ManualLogSource Logger;
        internal static ExpressionSlider _self;

        private static string _assemblyLocation;
        internal bool _loaded;
        private static bool _showUi;
        private static SimpleToolbarToggle _toolbarButton;

        private const int _uniqueId = ('E' << 24) | ('X' << 16) | ('P' << 8) | 'R';
        private Rect _windowRect = new Rect(140, 10, 360, 10);
        private static readonly float[] SliderStepOptions = new[] { 1f, 0.1f, 0.01f };
        private int _correctionStepIndex = 2;
        private GUIStyle _richLabel;
        private static readonly int[] CorrectionCategoryIds = new[] { 0, 1, 2, 3 };

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
            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

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

#if SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }
#endif

        private void EnsureScriptInfoBase(OCIChar ociChar, ExpressionSliderData data)
        {
            if (ociChar == null || data == null || data.ScriptInfoBaseInitialized)
                return;

            foreach (int categoryId in CorrectionCategoryIds)
            {
                ScriptMinMax? baseInfo = GetScriptInfo(ociChar, categoryId);
                if (baseInfo.HasValue)
                    data.ScriptInfoBaseByCategory[categoryId] = baseInfo.Value;
            }

            data.ScriptInfoBaseInitialized = true;
        }

        private void SetScriptInfo(OCIChar ociChar, ExpressionSliderData data, int categoryId, float value)
        {
            if (ociChar == null || data == null || ociChar.charInfo == null || ociChar.charInfo.expression == null || ociChar.charInfo.expression.info == null)
                return;

            foreach (ScriptInfo scriptInfo in ociChar.charInfo.expression.info)
            {
                if (scriptInfo.categoryNo != categoryId)
                    continue;

                if (!data.ScriptInfoBaseByCategory.TryGetValue(categoryId, out ScriptMinMax baseInfo))
                    continue;

                scriptInfo.enable = true;

                if (scriptInfo.correct.useRX)
                {
                    scriptInfo.correct.valRXMin = value == 0f ? baseInfo.RXMin : value;
                    scriptInfo.correct.valRXMax = value == 0f ? baseInfo.RXMax : value;
                }
                if (scriptInfo.correct.useRY)
                {
                    scriptInfo.correct.valRYMin = value == 0f ? baseInfo.RYMin : value;
                    scriptInfo.correct.valRYMax = value == 0f ? baseInfo.RYMax : value;
                }
                if (scriptInfo.correct.useRZ)
                {
                    scriptInfo.correct.valRZMin = value == 0f ? baseInfo.RZMin : value;
                    scriptInfo.correct.valRZMax = value == 0f ? baseInfo.RZMax : value;
                }
            }
        }

        private ScriptMinMax? GetScriptInfo(OCIChar ociChar, int categoryId)
        {
            if (ociChar == null || ociChar.charInfo == null || ociChar.charInfo.expression == null || ociChar.charInfo.expression.info == null)
                return null;

            foreach (ScriptInfo scriptInfo in ociChar.charInfo.expression.info)
            {
                if (scriptInfo.categoryNo != categoryId)
                    continue;

                return new ScriptMinMax
                {
                    RXMin = scriptInfo.correct.valRXMin,
                    RXMax = scriptInfo.correct.valRXMax,
                    RYMin = scriptInfo.correct.valRYMin,
                    RYMax = scriptInfo.correct.valRYMax,
                    RZMin = scriptInfo.correct.valRZMin,
                    RZMax = scriptInfo.correct.valRZMax
                };
            }

            return null;
        }

        protected override void Update()
        {
            if (!_loaded)
                return;

            ExpressionSliderData data = GetCurrentData();
            OCIChar currentOCIChar = GetCurrentOCI();
            if (data == null || currentOCIChar == null)
                return;

            EnsureScriptInfoBase(currentOCIChar, data);

            if (!Mathf.Approximately(data.MouthValue, data._prevMouth))
            {
                data._prevMouth = data.MouthValue;
                SetScriptInfo(currentOCIChar, data, 0, data.MouthValue);
            }
            if (!Mathf.Approximately(data.EyeValue, data._prevEye))
            {
                data._prevEye = data.EyeValue;
                SetScriptInfo(currentOCIChar, data, 1, data.EyeValue);
            }
            if (!Mathf.Approximately(data.EyelineValue, data._prevEyeline))
            {
                data._prevEyeline = data.EyelineValue;
                SetScriptInfo(currentOCIChar, data, 2, data.EyelineValue);
            }
            if (!Mathf.Approximately(data.ExpressionValue, data._prevExpression))
            {
                data._prevExpression = data.ExpressionValue;
                SetScriptInfo(currentOCIChar, data, 3, data.ExpressionValue);
            }
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

            ExpressionSliderData data = controller.GetData() ?? controller.CreateData(ociChar);
            EnsureScriptInfoBase(ociChar, data);
            return data;
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

        private void WindowFunc(int id)
        {
            var studio = Studio.Studio.Instance;

            bool guiUsingMouse = GUIUtility.hotControl != 0;
            bool mouseInWindow = _windowRect.Contains(Event.current.mousePosition);
            studio.cameraCtrl.noCtrlCondition = (guiUsingMouse || mouseInWindow) ? (() => true) : null;

            ExpressionSliderData data = GetCurrentData() ?? GetDataAndCreate(GetCurrentOCI());
            if (data == null)
            {
                GUILayout.Label("<color=white>Nothing to select</color>", RichLabel);
                GUI.DragWindow();
                return;
            }

            DrawStepSelector(ref _correctionStepIndex, "Step");
            float step = SliderStepOptions[_correctionStepIndex];

            data.MouthValue = DrawFieldRow("Mouth", data.MouthValue, step);
            data.EyeValue = DrawFieldRow("Eye", data.EyeValue, step);
            data.EyelineValue = DrawFieldRow("Eyeline", data.EyelineValue, step);
            data.ExpressionValue = DrawFieldRow("Expression", data.ExpressionValue, step);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Default"))
                InitConfig();
            if (GUILayout.Button("Close"))
            {
                studio.cameraCtrl.noCtrlCondition = null;
                _showUi = false;
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 170, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }

        private float DrawFieldRow(string label, float value, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(80));
            if (GUILayout.Button("-", GUILayout.Width(22)))
                value -= step;
            value = GUILayout.HorizontalSlider(value, -1f, 1f);
            if (GUILayout.Button("+", GUILayout.Width(22)))
                value += step;
            value = Quantize(value, step, -1f, 1f);
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

        private void Init()
        {
            _loaded = true;
        }

        private void SceneInit()
        {
            Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
            _showUi = false;
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
                                    .SetBlendShapeWeight(realHumanData.eye_close_idx_in_head_of_eyectrl, 0f); // Comment normalized to English.

                                srender
                                    .SetBlendShapeWeight(realHumanData.eye_wink_idx_in_head_of_eyectrl, weight);                                        
                            } else if (name.Contains("namida"))
                            {
                                srender
                                        .SetBlendShapeWeight(realHumanData.eye_close_idx_in_namida_of_eyectrl, 0f); // Comment normalized to English.

                                srender
                                    .SetBlendShapeWeight(realHumanData.eye_wink_idx_in_namida_of_eyectrl, weight);
                            } else if (name.Contains("lash."))
                            {
                                srender
                                    .SetBlendShapeWeight(realHumanData.eye_close_idx_in_lash_of_eyectrl, 0f); // Comment normalized to English.

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
                                    .SetBlendShapeWeight(realHumanData.eye_close_idx_in_head_of_mouthctrl, 0f); // Comment normalized to English.

                                srender
                                    .SetBlendShapeWeight(realHumanData.eye_wink_idx_in_head_of_mouthctrl, weight);   
                                // Comment normalized to English.
                                srender
                                                .SetBlendShapeWeight(38, 100);   

                            } else if (name.Contains("namida"))
                            {
                                srender
                                    .SetBlendShapeWeight(realHumanData.eye_close_idx_in_namida_of_mouthctrl, 0f); // Comment normalized to English.

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
