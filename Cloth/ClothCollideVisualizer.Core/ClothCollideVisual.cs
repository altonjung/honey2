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
    Agent task execution

    Purpose
    - Visualize cloth collider and bone information for the selected character.
    - Provide position/rotation/scale editing workflow in UI.

    Requirements
    - Support scene save/load for per-character collider transform data.
    - Show 'No Physics Cloth found' when no cloth component is available.

    Known issues
    - N/A
*/
namespace ClothCollideVisualizer
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public partial class ClothCollideVisualizer : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "ClothCollideVisualizer";
        public const string Version = "0.9.2.1";
        public const string GUID = "com.alton.illusionplugins.clothcollidevisualizer";
        internal const string _ownerId = "Alton";
        internal const string ReleaseType = "Pay";        
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "cloth_collide_visualizer";
#endif
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        private const string GROUP_CAPSULE_COLLIDER = "Capsule_Colliders";
        private const string GROUP_SPHERE_COLLIDER = "Sphere_Colliders";
        private const int SlotTop = 0;
        private const int SlotBottom = 1;
        private const string ColliderSupportPrefix = "Cloth colliders support_";

        #region Private Types
        #endregion

        #region Private Variables        

        internal static new ManualLogSource Logger;
        internal static ClothCollideVisualizer _self;
        private static string _assemblyLocation;
        private bool _loaded = false;
        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;
        private static bool _showDebugVisuals = true;

        private Vector2 _debugScroll;
        private string _colliderFilterText = string.Empty;
        private int _selectedDebugIndex = -1;
        private DebugColliderEntry _selectedDebugEntry;
        
        private const int _uniqueId = ('C' << 24) | ('C' << 16) | ('V' << 8) | 'S';
        private Rect _windowRect = new Rect(140, 10, 340, 10);
        private GUIStyle _richLabel;
        private static readonly Color ModifiedEntryColor = new Color(1f, 0f, 0f, 1f);
        private static readonly Color UnmodifiedEntryColor = new Color(0.75f, 0.95f, 0.75f, 1f);
        private const float TransformCompareEpsilon = 0.001f;
        private static readonly float[] SliderStepOptions = new float[] { 1f, 0.1f, 0.01f, 0.001f };
        private int _posStepIndex = 1;
        private int _rotStepIndex = 0;
        private int _scaleStepIndex = 2;

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
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

#if FEATURE_SCENE_SAVE
            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingImported += OnSceneImport;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingSaved += OnSceneSave;
#endif

            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open CollideVisualizer window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(ClothCollideVisualizer).Assembly).LoadTexture(),
                false, this, val =>
                {
                    _ShowUI = val;
                    var controller = GetCurrentControl();
                    if (val == false)
                    {
                        if (controller != null)
                        {
                            PhysicCollider data = controller.GetData();
                            SetDebugVisualsVisible(data, false);
                            ClearHighlight(data);
                        }
                    }
                    else
                    {
                        if (controller != null)
                        {
                            PhysicCollider data = controller.GetData();
                            SetDebugVisualsVisible(data, true);
                            if (_selectedDebugEntry != null && _selectedDebugEntry.source != null)
                                HighlightSelectedCollider(_selectedDebugEntry.source, data.debugCollideRenderers);
                        }
                    }
                });
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);   

            CharacterApi.RegisterExtraBehaviour<ClothCollideVisualizerController>(GUID);

            var harmony = HarmonyExtensions.CreateInstance(GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
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
        }

        protected override void OnGUI()
        {
            if (_loaded == false)
                return;

            if (StudioAPI.InsideStudio) {            
                if (_ShowUI == false)             
                    return;

                _windowRect = GUILayout.Window(_uniqueId + 1, _windowRect, WindowFunc, $"{Name}_{ReleaseType} " + Version);
            }                
        }


        // OCIChar
        private OCIChar GetCurrentOCI()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null)
                return null;

            TreeNodeObject node  = Studio.Studio.Instance.treeNodeCtrl.selectNodes
                .LastOrDefault();

            return  node != null ? Studio.Studio.GetCtrlInfo(node) as OCIChar : null;
        }

        internal ClothCollideVisualizerController GetCurrentControl()
        {
            OCIChar ociChar = GetCurrentOCI();
            if (ociChar != null)
            {
                return ociChar.GetChaControl().GetComponent<ClothCollideVisualizerController>();
            }
            return null;
        }
        internal ClothCollideVisualizerController GetControl(OCIChar ociChar)
        {
            if (ociChar != null)
            {
                return ociChar.GetChaControl().GetComponent<ClothCollideVisualizerController>();
            }
            return null;
        }

        internal PhysicCollider GetCurrentData()
        {
            OCIChar ociChar = GetCurrentOCI();
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            var controller = ociChar.GetChaControl().GetComponent<ClothCollideVisualizerController>();
            if (controller != null)
            {
                return controller.GetData();
            }

            return null;
        }

        private PhysicCollider GetDataAndCreate(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            var controller = ociChar.GetChaControl().GetComponent<ClothCollideVisualizerController>();
            if (controller == null)
                return null;

            return controller.GetData() ?? controller.CreateData(ociChar);            
        }


        // UI
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

            OCIChar ociChar = GetCurrentOCI();
            PhysicCollider physicCollider = GetCurrentData();

            if (physicCollider == null)
                physicCollider = GetDataAndCreate(ociChar);

            if (physicCollider != null)
            {
                ChaControl chaCtrl = ociChar != null ? ociChar.GetChaControl() : null;
                GameObject topObj = (chaCtrl != null && chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 0) ? chaCtrl.objClothes[0] : null;
                GameObject bottomObj = (chaCtrl != null && chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 1) ? chaCtrl.objClothes[1] : null;
                bool hasPhysicsCloth = HasPhysicsCloth(topObj) || HasPhysicsCloth(bottomObj);

                if (!physicCollider.visualColliderAdded && !physicCollider.requireForceRefresh)
                    AddVisualColliders(ociChar);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(_showDebugVisuals ? "Hide Wireframe/Text" : "Show Wireframe/Text"))
                {
                    _showDebugVisuals = !_showDebugVisuals;
                    SetDebugVisualsVisible(physicCollider, _showDebugVisuals);
                }
                if (GUILayout.Button("Force Refresh"))
                {
                    ForceRefreshVisualColliders(ociChar);
                    physicCollider = GetDataAndCreate(ociChar);
                }
                GUILayout.EndHorizontal();

                if (physicCollider != null && physicCollider.requireForceRefresh)
                {
                    GUILayout.Label("<color=red>click 'FORCE REFRESH'</color>", RichLabel);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Filter", GUILayout.Width(42));
                _colliderFilterText = GUILayout.TextField(_colliderFilterText ?? string.Empty, GUILayout.MinWidth(120));
                if (GUILayout.Button("Clear", GUILayout.Width(52)))
                    _colliderFilterText = string.Empty;
                GUILayout.EndHorizontal();

                string filter = (_colliderFilterText ?? string.Empty).Trim();
                bool hasFilter = !string.IsNullOrEmpty(filter);

                _debugScroll = GUILayout.BeginScrollView(_debugScroll, GUI.skin.box, GUILayout.Height(120));
                int shownCount = 0;
                for (int i = 0; i < physicCollider.debugEntries.Count; i++)
                {
                    var entry = physicCollider.debugEntries[i];
                    string label = entry != null ? NormalizeColliderDisplayName(entry.name) : "(null)";
                    if (hasFilter)
                    {
                        string haystack = label ?? string.Empty;
                        if (haystack.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }

                    shownCount++;
                    bool isModified = IsEntryModified(entry);
                    bool isAdjustableEntry = (entry != null && entry.source != null && IsAdjustableCollider(entry.source))
                        || (!string.IsNullOrEmpty(label) && label.IndexOf("Adjustable", StringComparison.OrdinalIgnoreCase) >= 0);
                    Color prevContentColor = GUI.contentColor;
                    // Modified Adjustable )
                    GUI.contentColor = isModified
                        ? ModifiedEntryColor
                        : (isAdjustableEntry ? new Color(1f, 0.55f, 0f, 1f) : UnmodifiedEntryColor);

                    bool isSelected = ReferenceEquals(_selectedDebugEntry, entry);
                    if (GUILayout.Toggle(isSelected, label, GUI.skin.button))
                    {
                        if (!isSelected)
                        {
                            _selectedDebugEntry = entry;
                            SyncDebugFromSource(entry);
                            if (entry != null && entry.source != null)
                                HighlightSelectedCollider(entry.source, physicCollider.debugCollideRenderers);
                        }
                    }
                    GUI.contentColor = prevContentColor;
                }
                GUILayout.EndScrollView();

                if (!hasPhysicsCloth)
                    GUILayout.Label("<color=yellow>No Physics Cloth found</color>", RichLabel);

                if (hasFilter)
                    GUILayout.Label($"Shown: {shownCount}/{physicCollider.debugEntries.Count}", RichLabel);

                if (_selectedDebugEntry != null && _selectedDebugEntry.debugTransform != null)
                {
                    Collider collider = _selectedDebugEntry.source;
                    string colliderType = collider is CapsuleCollider ? "Capsule" : (collider is SphereCollider ? "Sphere" : "Collider");
                    bool isSelectedModified = GetEntryModifiedFlags(_selectedDebugEntry, out bool centerModified, out bool rotModified, out bool scaleModified);
                    string statusText = isSelectedModified ? "<color=red>Modified</color>" : "<color=#7CFC00>Unmodified</color>";

                    // GUILayout.Label($"<color=orange>{colliderType}</color>: {NormalizeColliderDisplayName(collider.name)}", RichLabel);
                    // GUILayout.Label($"Status: {statusText}", RichLabel);
                    if (isSelectedModified)
                    {
                        string parts = string.Join(", ", new[]
                        {
                            centerModified ? "Center" : null,
                            rotModified ? "Rotation" : null,
                            scaleModified ? "Scale" : null
                        }.Where(v => !string.IsNullOrEmpty(v)).ToArray());
                        GUILayout.Label($"Modified: {parts}", RichLabel);
                    }
                    // GUILayout.Label($"Baseline Center: {_selectedDebugEntry.baselineCenter.x:0.###}, {_selectedDebugEntry.baselineCenter.y:0.###}, {_selectedDebugEntry.baselineCenter.z:0.###}", RichLabel);
                    // GUILayout.Label($"Baseline Rot: {_selectedDebugEntry.baselineLocalEuler.x:0.###}, {_selectedDebugEntry.baselineLocalEuler.y:0.###}, {_selectedDebugEntry.baselineLocalEuler.z:0.###}", RichLabel);
                    // GUILayout.Label($"Baseline Scale: {_selectedDebugEntry.baselineLocalScale.x:0.###}, {_selectedDebugEntry.baselineLocalScale.y:0.###}, {_selectedDebugEntry.baselineLocalScale.z:0.###}", RichLabel);
                    // draw_seperate();

                    Transform debugTr = _selectedDebugEntry.debugTransform;
                    Transform debugCenterTr = _selectedDebugEntry.debugCenterTransform;

                    GUILayout.Label("<color=orange>Position</color>", RichLabel);
                    DrawStepSelector(ref _posStepIndex, "Step");
                    Vector3 pos = debugCenterTr != null ? debugCenterTr.localPosition : Vector3.zero;
                    float posStep = SliderStepOptions[_posStepIndex];
                    pos.x = SliderRow("Pos X", pos.x, -3.0f, 3.0f, _selectedDebugEntry.baselineCenter.x, posStep);
                    pos.y = SliderRow("Pos Y", pos.y, -3.0f, 3.0f, _selectedDebugEntry.baselineCenter.y, posStep);
                    pos.z = SliderRow("Pos Z", pos.z, -3.0f, 3.0f, _selectedDebugEntry.baselineCenter.z, posStep);
                    if (debugCenterTr != null)
                        debugCenterTr.localPosition = pos;

                    GUILayout.Label("<color=orange>Rotation</color>", RichLabel);
                    DrawStepSelector(ref _rotStepIndex, "Step");
                    Vector3 rot = debugTr.localEulerAngles;
                    rot.x = NormalizeAngle(rot.x);
                    rot.y = NormalizeAngle(rot.y);
                    rot.z = NormalizeAngle(rot.z);
                    float rotStep = SliderStepOptions[_rotStepIndex];
                    rot.x = SliderRow("Rot X", rot.x, -180.0f, 180.0f, NormalizeAngle(_selectedDebugEntry.baselineLocalEuler.x), rotStep);
                    rot.y = SliderRow("Rot Y", rot.y, -180.0f, 180.0f, NormalizeAngle(_selectedDebugEntry.baselineLocalEuler.y), rotStep);
                    rot.z = SliderRow("Rot Z", rot.z, -180.0f, 180.0f, NormalizeAngle(_selectedDebugEntry.baselineLocalEuler.z), rotStep);
                    debugTr.localEulerAngles = rot;

                    GUILayout.Label("<color=orange>Scale</color>", RichLabel);
                    DrawStepSelector(ref _scaleStepIndex, "Step");
                    Vector3 scale = debugTr.localScale;
                    float scaleStep = SliderStepOptions[_scaleStepIndex];
                    scale.x = SliderRow("Scale X", scale.x, 0.1f, 2.0f, _selectedDebugEntry.baselineLocalScale.x, scaleStep);
                    scale.y = SliderRow("Scale Y", scale.y, 0.1f, 2.0f, _selectedDebugEntry.baselineLocalScale.y, scaleStep);
                    scale.z = SliderRow("Scale Z", scale.z, 0.1f, 2.0f, _selectedDebugEntry.baselineLocalScale.z, scaleStep);
                    debugTr.localScale = scale;

                    ApplyDebugToSource(_selectedDebugEntry);

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Reset All"))
                    {
                        ResetDebugToBaseline(_selectedDebugEntry);
                        ApplyDebugToSource(_selectedDebugEntry);
                    }
                    GUILayout.EndHorizontal();
                }                
            } else
            {
                GUILayout.Label("<color=white>Nothing to select</color>", RichLabel);   
            }

            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 150, 20), GUI.tooltip, GUI.skin.box);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Close"))
            {
                studio.cameraCtrl.noCtrlCondition = null;
                _ShowUI = false;
                SetDebugVisualsVisible(physicCollider, false);
                ClearHighlight(physicCollider);
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        private void draw_seperate()
        {
            GUILayout.Space(5);
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(0.3f));
            GUI.Box(rect, GUIContent.none);
            GUILayout.Space(10);
        }

        private void ForceRefreshVisualColliders(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.GetChaControl() == null)
                return;

            var controller = _self.GetControl(ociChar);
            if (controller == null)
                return;

            PhysicCollider existData = controller.GetData();
            if (existData != null)
                SaveCurrentTransformsForSlots(ociChar.GetChaControl(), existData);

            controller.RemovePhysicCollier();
            CleanupVisualsByName(ociChar);
            _selectedDebugEntry = null;
            _selectedDebugIndex = -1;

            // : Cloth Adjustable collider .
            PhysicCollider physicCollider = controller.GetData() ?? controller.CreateData(ociChar);
            if (physicCollider != null)
            {
                if (physicCollider.pendingResetTop)
                {
                    ClothCollideVisualUtils.RemoveAllocatedClothColliders(physicCollider, true);
                    ResetSlotCollidersToDefault(ociChar.GetChaControl(), physicCollider, SlotTop);
                }
                if (physicCollider.pendingResetBottom)
                {
                    ClothCollideVisualUtils.RemoveAllocatedClothColliders(physicCollider, false);
                    ResetSlotCollidersToDefault(ociChar.GetChaControl(), physicCollider, SlotBottom);
                }

                controller.SupportExtraClothCollider(ociChar.GetChaControl(), physicCollider);
                ApplySavedTransformsForSlots(ociChar.GetChaControl(), physicCollider);
                physicCollider.pendingResetTop = false;
                physicCollider.pendingResetBottom = false;
            }

            AddVisualColliders(ociChar);
            PhysicCollider refreshedData = controller.GetData();
            if (refreshedData != null)
                refreshedData.requireForceRefresh = false;
        }

        private void ForceRemoveVisualColliders(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.GetChaControl() == null)
                return;

            var controller = _self.GetControl(ociChar);
            if (controller == null)
                return;

            PhysicCollider existData = controller.GetData();
            if (existData != null)
                SaveCurrentTransformsForSlots(ociChar.GetChaControl(), existData);

            controller.RemovePhysicCollier();
            CleanupVisualsByName(ociChar);
            _selectedDebugEntry = null;
            _selectedDebugIndex = -1;

            PhysicCollider physicCollider = controller.GetData();
            if (physicCollider != null)
                physicCollider.requireForceRefresh = true;
        }

        private float SliderRow(string label, float value, float min, float max, float resetValue, float step)
        {
            float originalValue = value;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            if (GUILayout.Button("-", GUILayout.Width(22)))
                value -= step;
            value = GUILayout.HorizontalSlider(value, min, max);
            if (GUILayout.Button("+", GUILayout.Width(22)))
                value += step;

            // Avoid snapping just by drawing the UI (important when baseline is not aligned to the current step).
            if (Mathf.Abs(value - originalValue) > 1e-6f)
                value = Quantize(value, step, min, max);
            GUILayout.Label(FormatByStep(value, step), GUILayout.Width(50));
            if (GUILayout.Button("Reset", GUILayout.Width(52)))
                value = resetValue;
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
                string stepLabel = SliderStepOptions[i].ToString("0.###");
                if (GUILayout.Toggle(stepIndex == i, stepLabel, GUI.skin.button, GUILayout.Width(44)))
                    stepIndex = i;
            }
            GUILayout.EndHorizontal();
        }

        private float Quantize(float value, float step, float min, float max)
        {
            if (step <= 0f)
                return Mathf.Clamp(value, min, max);

            float quantized = Mathf.Round(value / step) * step;
            return Mathf.Clamp(quantized, min, max);
        }

        private string FormatByStep(float value, float step)
        {
            int decimals = 2;
            if (step > 0f)
            {
                decimals = Mathf.Clamp(Mathf.CeilToInt(-Mathf.Log10(step)), 0, 4);
            }
            string fmt = decimals == 0 ? "0" : ("0." + new string('0', decimals));
            return value.ToString(fmt, CultureInfo.InvariantCulture);
        }

        private float NormalizeAngle(float angle)
        {
            return Mathf.Repeat(angle + 180f, 360f) - 180f;
        }

        private void SyncDebugFromSource(DebugColliderEntry entry)
        {
            if (entry == null || entry.source == null || entry.debugTransform == null)
                return;

            Transform src = entry.source.transform;
            entry.debugTransform.localPosition = src.localPosition;
            entry.debugTransform.localEulerAngles = src.localEulerAngles;
            entry.debugTransform.localScale = src.localScale;

            if (entry.debugCenterTransform != null && TryGetColliderCenter(entry.source, out Vector3 center))
                entry.debugCenterTransform.localPosition = center;
        }

        private void ApplyDebugToSource(DebugColliderEntry entry)
        {
            if (entry == null || entry.source == null || entry.debugTransform == null)
                return;

            Transform src = entry.source.transform;
            src.localPosition = entry.debugTransform.localPosition;
            src.localEulerAngles = entry.debugTransform.localEulerAngles;
            src.localScale = entry.debugTransform.localScale;

            if (entry.debugCenterTransform != null)
                TrySetColliderCenter(entry.source, entry.debugCenterTransform.localPosition);

            PhysicCollider physicCollider = GetCurrentData();
            string colliderKey = GetColliderKey(entry.source);
            if (physicCollider != null && !string.IsNullOrEmpty(colliderKey))
            {
                // collider top/bottom ( )
                ChaControl chaCtrl = physicCollider.chaCtrl;
                if (chaCtrl != null && (physicCollider.colliderInstanceIdToSlot == null || physicCollider.colliderInstanceIdToSlot.Count == 0))
                    RebuildColliderSlotCache(chaCtrl, physicCollider);

                int slot = SlotTop;
                if (physicCollider.colliderInstanceIdToSlot != null
                    && physicCollider.colliderInstanceIdToSlot.TryGetValue(entry.source.GetInstanceID(), out int mappedSlot))
                    slot = mappedSlot;

                if (slot == SlotBottom)
                    physicCollider.bottomColliderTransformInfos[colliderKey] = new ColliderTransformInfo
                    {
                        localPosition = src.localPosition,
                        localEulerAngles = src.localEulerAngles,
                        localScale = src.localScale,
                        colliderCenter = entry.debugCenterTransform != null ? entry.debugCenterTransform.localPosition : GetColliderCenterOrDefault(entry.source),
                        hasLocalPosition = true,
                        hasLocalEulerAngles = true,
                        hasLocalScale = true,
                        hasColliderCenter = true
                    };
                else
                    physicCollider.topColliderTransformInfos[colliderKey] = new ColliderTransformInfo
                {
                    localPosition = src.localPosition,
                    localEulerAngles = src.localEulerAngles,
                    localScale = src.localScale,
                    colliderCenter = entry.debugCenterTransform != null ? entry.debugCenterTransform.localPosition : GetColliderCenterOrDefault(entry.source),
                    hasLocalPosition = true,
                    hasLocalEulerAngles = true,
                    hasLocalScale = true,
                    hasColliderCenter = true
                };
            }
        }

        private void ResetDebugToBaseline(DebugColliderEntry entry)
        {
            if (entry == null || entry.debugTransform == null)
                return;

            entry.debugTransform.localPosition = entry.baselineLocalPosition;
            entry.debugTransform.localEulerAngles = entry.baselineLocalEuler;
            entry.debugTransform.localScale = entry.baselineLocalScale;
            if (entry.debugCenterTransform != null)
                entry.debugCenterTransform.localPosition = entry.baselineCenter;
        }

        private bool IsEntryModified(DebugColliderEntry entry)
        {
            return GetEntryModifiedFlags(entry, out _, out _, out _);
        }

        private bool GetEntryModifiedFlags(DebugColliderEntry entry, out bool centerModified, out bool rotModified, out bool scaleModified)
        {
            if (entry == null || entry.debugTransform == null)
            {
                centerModified = false;
                rotModified = false;
                scaleModified = false;
                return false;
            }

            centerModified = entry.debugCenterTransform != null
                && !ApproximatelyVector(entry.debugCenterTransform.localPosition, entry.baselineCenter);

            // Position Collider.center(Offset) .
            rotModified = !ApproximatelyEuler(entry.debugTransform.localEulerAngles, entry.baselineLocalEuler);
            scaleModified = !ApproximatelyVector(entry.debugTransform.localScale, entry.baselineLocalScale);

            return centerModified || rotModified || scaleModified;
        }

        private static string NormalizeColliderDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            // onGUI prefix ( ).
            if (name.StartsWith(ColliderSupportPrefix, StringComparison.Ordinal))
                return name.Substring(ColliderSupportPrefix.Length);

            return name;
        }

        private bool ApproximatelyVector(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < TransformCompareEpsilon
                && Mathf.Abs(a.y - b.y) < TransformCompareEpsilon
                && Mathf.Abs(a.z - b.z) < TransformCompareEpsilon;
        }

        private bool ApproximatelyEuler(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(Mathf.DeltaAngle(a.x, b.x)) < TransformCompareEpsilon
                && Mathf.Abs(Mathf.DeltaAngle(a.y, b.y)) < TransformCompareEpsilon
                && Mathf.Abs(Mathf.DeltaAngle(a.z, b.z)) < TransformCompareEpsilon;
        }


        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            _loaded = true;
        }
        
        private void SceneInit()
        {
            Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
			_ShowUI = false;     
        }

        private static IEnumerator ExecuteAfterFrame(OCIChar ociChar, int frameCount)
        {
            for (int i = 0; i < frameCount; i++)
                yield return null;

            if (ociChar == null)
                yield break;

            var controller = _self.GetControl(ociChar);
            if (controller == null)
                yield break;

            PhysicCollider physicCollider = controller.GetData() ?? controller.CreateData(ociChar);
            if (physicCollider == null)
                yield break;

            ApplySavedTransformsForSlots(ociChar.GetChaControl(), physicCollider);

            if (_ShowUI)
                _self.AddVisualColliders(ociChar);
        }

        private void CleanupVisualsByName(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.charInfo == null || ociChar.charInfo.objBodyBone == null)
                return;

            List<GameObject> toDestroy = new List<GameObject>();

            Transform root = ociChar.charInfo.objBodyBone.transform;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string name = t.name ?? "";

                bool isDebugName =
                    name.EndsWith("_Debug") ||
                    name.Contains("_CapsuleWire") ||
                    name.Contains("_SphereWire") ||
                    name.Contains("_TextDebug");

                bool hasDebugComponent =
                    t.GetComponent<LineRenderer>() != null ||
                    t.GetComponent<TextMesh>() != null;

                if (isDebugName || hasDebugComponent)
                {
                    toDestroy.Add(t.gameObject);
                }
            }

            foreach (var go in toDestroy)
            {
                if (go != null)
                    GameObject.Destroy(go);
            }
        }
        #endregion
        // CapsuleCollider .
        private static void CreateCapsuleWireframe(CapsuleCollider capsule, Transform parent, string name, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
        {
            Camera cam = Camera.main;
            bool isAdjustable = IsAdjustableCollider(capsule);

            // Root
            GameObject root = new GameObject(capsule.name + "_CapsuleWire");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            List<Renderer> renderers = new List<Renderer>();

            // Capsule .
            Vector3 axis;
            Quaternion rot = Quaternion.identity;
            switch (capsule.direction)
            {
                case 0: axis = Vector3.right; rot = Quaternion.Euler(0f, 0f, 90f); break;   // X
                case 1: axis = Vector3.up; rot = Quaternion.identity; break;                 // Y
                case 2: axis = Vector3.forward; rot = Quaternion.Euler(90f, 0f, 0f); break; // Z
                default: axis = Vector3.up; break;
            }

            int segments = 48; // Number of segments used to draw capsule arcs.
            // Build one line strip for capsule rings and side edges.
            GameObject lineObj = new GameObject("CapsuleWireLines");
            lineObj.transform.SetParent(root.transform, false);
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = new Material(Shader.Find("Unlit/Color"));
            lr.material.color = GetBaseColliderColor(capsule, 0.75f);
            lr.widthMultiplier = 0.01f;

            List<Vector3> points = new List<Vector3>();

            float radius = capsule.radius;
            float halfHeight = capsule.height * 0.5f - radius;

            float angle_temp = 2 * Mathf.PI / segments;

            // Top Circle
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                points.Add(new Vector3(x, halfHeight, z));
            }

            // Bottom Circle
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                points.Add(new Vector3(x, -halfHeight, z));
            }

            // Cylinder side lines
            for (int i = 0; i <= segments; i++)
            {
                points.Add(new Vector3(Mathf.Cos(angle_temp * i) * radius, halfHeight, Mathf.Sin(angle_temp * i) * radius));
                points.Add(new Vector3(Mathf.Cos(angle_temp * i) * radius, -halfHeight, Mathf.Sin(angle_temp * i) * radius));
            }

            lr.positionCount = points.Count;
            lr.SetPositions(points.ToArray());

            renderers.Add(lr); // LineRenderer
            debugObjects.Add(root);
            debugObjects.Add(lineObj);
            debugCollideRenderers[capsule] = renderers;

            // Add a text label near the capsule center.
            Vector3 textPos = axis * (halfHeight * 0.5f + 0.1f);
            CreateTextDebugLocal(root.transform, textPos, name, cam, debugObjects, debugCollideRenderers, isAdjustable);

            if (_showDebugVisuals == false)
            {
                root.SetActive(false);
            }
        }
        // SphereCollider .
        private static void CreateSphereWireframe(SphereCollider sphere, Transform parent, string name, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
        {
            Camera cam = Camera.main;
            bool isAdjustable = IsAdjustableCollider(sphere);

            GameObject root = new GameObject(sphere.name + "_SphereWire");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            List<Renderer> renderers = new List<Renderer>();

            GameObject lineObj = new GameObject("SphereWireLines");
            lineObj.transform.SetParent(root.transform, false);
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = new Material(Shader.Find("Unlit/Color"));
            lr.material.color = GetBaseColliderColor(sphere, 0.75f);
            lr.widthMultiplier = 0.01f;

            List<Vector3> points = new List<Vector3>();
            int segments = 64; 
            float radius = sphere.radius;

            float angle_temp = 2 * Mathf.PI / segments;

            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                points.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                points.Add(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                points.Add(new Vector3(0f, Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
            }

            lr.positionCount = points.Count;
            lr.SetPositions(points.ToArray());

            renderers.Add(lr);
            debugObjects.Add(root);
            debugObjects.Add(lineObj);
            debugCollideRenderers[sphere] = renderers;

            // Add a text label above the sphere.
            Vector3 textPos = Vector3.up * (radius + 0.05f);
            CreateTextDebugLocal(root.transform, textPos, name, cam, debugObjects, debugCollideRenderers, isAdjustable);

            if (_showDebugVisuals == false)
            {
                root.SetActive(false);
            }
        }

        private static void CreateTextDebugLocal(Transform parent, Vector3 localPos, string text, Camera cam, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers, bool isAdjustable = false)
        {
            GameObject textObj = new GameObject(text + "_" + "TextDebug");
            textObj.transform.SetParent(parent, false);
            textObj.transform.localPosition = localPos;

            TextMesh tm = textObj.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 35;
            tm.color = isAdjustable ? new Color(1f, 0.55f, 0f, 1f) : Color.yellow;
            tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;

            // Keep the text facing the active camera.
            if (cam != null)
                textObj.transform.rotation = Quaternion.LookRotation(textObj.transform.position - cam.transform.position);

            debugObjects.Add(textObj);
        }

        private static void SetDebugVisualsVisible(PhysicCollider physicCollider, bool visible)
        {
            if (physicCollider == null)
                return;

            foreach (var obj in physicCollider.debugCapsuleCollideVisibleObjects)
            {
                if (obj != null)
                    obj.SetActive(visible);
            }
            foreach (var obj in physicCollider.debugSphereCollideVisibleObjects)
            {
                if (obj != null)
                    obj.SetActive(visible);
            }
        }

        private static void HighlightSelectedCollider(Collider selected, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
        {
            foreach (var kvp in debugCollideRenderers)
            {
                foreach (var rend in kvp.Value)
                {
                    if (rend == null)
                    {
                        Logger.LogMessage($"Reselect character to refresh colliders");
                        break;    
                    }

                    Color c = rend.material.color;
                    // Highlight selected collider and keep default colors for others.
                    if (kvp.Key == selected)
                    {
                        c = IsAdjustableCollider(kvp.Key)
                            ? new Color(1f, 0.55f, 0f, 1f)
                            : new Color(1f, 0f, 0f, 1f);
                    }
                    else
                    {
                        c = GetBaseColliderColor(kvp.Key, 0.5f);
                    }
                    rend.material.color = c;
                }
            }
        }

        private static void ClearHighlight(PhysicCollider physicCollider)
        {
            if (physicCollider == null || physicCollider.debugCollideRenderers == null)
                return;

            foreach (var kvp in physicCollider.debugCollideRenderers)
            {
                foreach (var rend in kvp.Value)
                {
                    if (rend == null)
                        continue;

                    rend.material.color = GetBaseColliderColor(kvp.Key, 0.5f);
                }
            }
        }

        private static bool IsAdjustableCollider(Collider collider)
        {
            if (collider == null)
                return false;

            if (!string.IsNullOrEmpty(collider.name) &&
                collider.name.IndexOf("Adjustable", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            Transform tr = collider.transform;
            while (tr != null)
            {
                if (!string.IsNullOrEmpty(tr.name) &&
                    tr.name.IndexOf("Adjustable", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                tr = tr.parent;
            }

            return false;
        }

        private static Color GetBaseColliderColor(Collider collider, float alpha)
        {
            if (IsAdjustableCollider(collider))
                return new Color(1f, 0.55f, 0f, alpha);

            if (collider is CapsuleCollider)
                return new Color(0f, 1f, 0.5f, alpha);
            if (collider is SphereCollider)
                return new Color(0f, 1f, 0f, alpha);
            if (collider is BoxCollider)
                return new Color(0.5f, 1f, 0f, alpha);

            return new Color(0f, 1f, 0f, alpha);
        }

        private static void InitCollider(PhysicCollider value)
        {
            foreach (var obj in value.debugCapsuleCollideVisibleObjects)
            {
                if (obj == null) continue;
                GameObject.Destroy(obj);
            }
            foreach (var obj in value.debugSphereCollideVisibleObjects)
            {
                if (obj == null) continue;
                GameObject.Destroy(obj);
            }           

            value.debugCapsuleCollideVisibleObjects.Clear();
            value.debugSphereCollideVisibleObjects.Clear();           
            value.debugCollideRenderers.Clear();
            if (value.debugEntries != null)
            {
                foreach (var entry in value.debugEntries)
                {
                    if (entry == null || entry.debugTransform == null)
                        continue;
                    GameObject.Destroy(entry.debugTransform.gameObject);
                }
                value.debugEntries.Clear();
            }
            if (value.debugEntryBySource != null)
                value.debugEntryBySource.Clear();                
        }

        // Cloth Collider .
        private void AddVisualColliders(OCIChar ociChar)
        {
            if (ociChar != null)
            {
                var controller = _self.GetControl(ociChar);
                if (controller != null)
                {
                    PhysicCollider physicCollider = controller.GetData();

                    if (physicCollider == null)
                        controller.CreateData(ociChar);

                    physicCollider = controller.GetData();
                    if (physicCollider == null)
                        return;

                    physicCollider.ociChar = ociChar;
                    ApplySavedTransformsForSlots(ociChar.GetChaControl(), physicCollider);

                    ChaControl baseCharControl = ociChar.charInfo;

                    List<GameObject> physicsClothes = new List<GameObject>();

                    int idx = 0;
                    // 8 Cloth .
                    foreach (var clothObj in baseCharControl.objClothes)
                    {
                        if (clothObj == null)
                        {
                            idx++;
                            continue;
                        }

                        physicCollider.clothInfos[idx].clothObj = clothObj;

                        if (clothObj.GetComponentsInChildren<Cloth>().Length > 0)
                        {
                            physicCollider.clothInfos[idx].hasCloth = true;
                            physicsClothes.Add(clothObj);
                        }
                        else
                        {
                            physicCollider.clothInfos[idx].hasCloth = false;
                        }

                        idx++;
                    }

                    // ( 20 //idx = 0;
                    //foreach (var accessory in baseCharControl.objAccessory)
                    //{
                    //    if (accessory == null)
                    //    {
                    //        idx++;
                    //        continue;
                    //    }

                    //    physicCollider.accessoryInfos[idx].clothObj = accessory;

                    //    if (accessory.GetComponentsInChildren<Cloth>().Length > 0)
                    //    {
                    //        physicCollider.accessoryInfos[idx].hasCloth = true;
                    //        physicsClothes.Add(accessory);
                    //    }
                    //    else
                    //    {
                    //        physicCollider.accessoryInfos[idx].hasCloth = false;
                    //    }

                    //    idx++;
                    //}
                    
                    // Cloth collider .
                    ChaControl chaCtrl = ociChar.GetChaControl();
                    GameObject topObj = (chaCtrl != null && chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 0) ? chaCtrl.objClothes[0] : null;
                    GameObject bottomObj = (chaCtrl != null && chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 1) ? chaCtrl.objClothes[1] : null;

                    List<Collider> usedColliders = GetCollidersUsedByCloth(topObj)
                        .Concat(GetCollidersUsedByCloth(bottomObj))
                        .Where(v => v != null)
                        .Distinct()
                        .OrderBy(v => v.name, StringComparer.Ordinal)
                        .ToList();

                    foreach (Collider col in usedColliders)
                    {
                        if (col == null)
                            continue;

                        EnsureDefaultColliderBaseline(col, physicCollider);
                        int colId = col.GetInstanceID();
                        ColliderTransformInfo baselineInfo = null;
                        if (physicCollider.colliderDefaultTransforms != null)
                            physicCollider.colliderDefaultTransforms.TryGetValue(colId, out baselineInfo);

                        string collider_name = GetColliderKey(col);
                        if (string.IsNullOrEmpty(collider_name))
                            collider_name = col.name ?? "(null)";

                        GameObject debugRoot = new GameObject(collider_name + "_Debug");
                        debugRoot.transform.SetParent(col.transform.parent, false);
                        debugRoot.transform.localPosition = col.transform.localPosition;
                        debugRoot.transform.localRotation = col.transform.localRotation;
                        debugRoot.transform.localScale = col.transform.localScale;

                        GameObject debugCenterRoot = new GameObject("Center");
                        debugCenterRoot.transform.SetParent(debugRoot.transform, false);
                        debugCenterRoot.transform.localPosition = GetColliderCenterOrDefault(col);
                        debugCenterRoot.transform.localRotation = Quaternion.identity;
                        debugCenterRoot.transform.localScale = Vector3.one;

                        var entry = new DebugColliderEntry
                        {
                            name = collider_name,
                            source = col,
                            debugTransform = debugRoot.transform,
                            debugCenterTransform = debugCenterRoot.transform,
                            baselineLocalPosition = baselineInfo != null ? baselineInfo.localPosition : debugRoot.transform.localPosition,
                            baselineLocalEuler = baselineInfo != null ? baselineInfo.localEulerAngles : debugRoot.transform.localEulerAngles,
                            baselineLocalScale = baselineInfo != null ? baselineInfo.localScale : debugRoot.transform.localScale,
                            baselineCenter = baselineInfo != null ? baselineInfo.colliderCenter : debugCenterRoot.transform.localPosition
                        };
                        physicCollider.debugEntries.Add(entry);
                        physicCollider.debugEntryBySource[col] = entry;

                        if (col is SphereCollider sphere)
                            CreateSphereWireframe(sphere, debugCenterRoot.transform, collider_name, physicCollider.debugSphereCollideVisibleObjects, physicCollider.debugCollideRenderers);
                        else if (col is CapsuleCollider capsule)
                            CreateCapsuleWireframe(capsule, debugCenterRoot.transform, collider_name, physicCollider.debugCapsuleCollideVisibleObjects, physicCollider.debugCollideRenderers);
                    }

                    SetDebugVisualsVisible(physicCollider, _showDebugVisuals);

                    physicCollider.visualColliderAdded = true;
                    physicCollider.requireForceRefresh = false;

                    // UnityEngine.Debug.Log($">> AddVisualColliders");     
                }        
            }
        }

        #region Patches         
        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.SceneInit();
                return true;
            }
        }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                OCIChar ociChar = __instance;

                if (ociChar != null)
                {
                    var controller = _self.GetControl(ociChar);
                    if (controller != null)
                    {
                        // Clear previous debug objects before rebuilding.
                        _self.ForceRemoveVisualColliders(ociChar);

                        // Character swap should reset both top and bottom cached transforms.
                        PhysicCollider data = controller.GetData();
                        if (data != null)
                        {
                            data.topColliderTransformInfos.Clear();
                            data.bottomColliderTransformInfos.Clear();
                            data.pendingResetTop = true;
                            data.pendingResetBottom = true;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        private static class ChaControl_ChangeClothes_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
            {
                OCIChar ociChar = __instance.GetOCIChar();

                if (ociChar != null)
                {
                    var controller = _self.GetControl(ociChar);
                    if (controller != null)
                    {
                        // Clear previous debug objects before rebuilding.
                        _self.ForceRemoveVisualColliders(ociChar);

                        // Reset only the affected slot when clothing changes.
                        PhysicCollider data = controller.GetData();
                        if (data != null && TryGetSlotFromChangeClothesKind(kind, out int slot))
                        {
                            if (slot == SlotBottom)
                            {
                                data.bottomColliderTransformInfos.Clear();
                                data.pendingResetBottom = true;
                            }
                            else
                            {
                                data.topColliderTransformInfos.Clear();
                                data.pendingResetTop = true;
                            }
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
                OCIChar ociChar = __instance.GetOCIChar();

                if (ociChar != null)
                {
                    var controller = _self.GetControl(ociChar);
                    if (controller != null)
                    {
                        // Accessory visibility can invalidate active collider references.
                        _self.ForceRemoveVisualColliders(ociChar);
                    }
                }
            }
        }

        #endregion
    }
}
