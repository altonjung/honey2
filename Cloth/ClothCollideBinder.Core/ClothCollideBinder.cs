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

/*
    Agent 코드 수행

    목적
    - 활성화된 캐릭터가 착용한 Cloth 컴포넌트와 외부 Item에 임의로 부여한 Collider에 대해 상호작용 효과를 제공한다.

    용어
    - OCIChar: 캐릭터 
        > GetCurrentOCI 함수를 통해 현재 씬내 활성화된 캐리터를 획득
    - OCIItem: 아이템(공, 테이블, 의자 등)
        > GetAllOCIItemFromStudio 함수를 통해 현재 씬내 전체 아이템 목록 획득


    요구 기능
    1) UI 생성
    1.1) 현재 씬내 현재 캐릭터내 cloth 컴포넌트를 조회 하여 UI 제공
        - 상의/하의 Cloth 선택 UI 가 필요하고 각 정보는 GetClothTop()/GetClothBottom() 통해서 획득
    1.2) 현재 씬내 조회된 OCIItem 목록 선택 UI 제공
    1.3) OCIItem 목록에서 사용자가 선택한 Item에 대해 Capsule/Sphere Collider 생성 UI 제공        
        - capsule/sphere 중 선택한 대상에 대한 생성 버튼 제공
        - Center(X, Y, Z), Radius, Height 조정 가능 UI 제공 필요((녹색 실선으로 collide 속성값 변경 부분 실시간 확인 제공)
    1.4) Binding 버튼 클릭 시, 선택한 Cloth의 충돌 매핑에 Item Collider를 추가
    1.5) 아래는 고도화 기능 (나중에 해달라고 할때 하면 됨..)
        - collide가 생성된 대상 OCIItem 가 재 선택시 생성되었던 collide가 다시 시각화 되어야 함..
        - 이처리를 위해선 collide 이름에 특별한 명칭을 부여하여, OCItem 선택시 collide 찾기가 용이해야함
        - collide 삭제 버튼을 제공하여 삭제될 수 있어야 함
*/
namespace ClothCollideBinder
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class ClothCollideBinder : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "ClothCollideBinder";
        public const string Version = "0.9.0.0";
        public const string GUID = "com.alton.illusionplugins.ClothCollideBinder";
        internal const string _ownerId = "alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "cloth_collide_binder";
#endif
        private const int ClothSlotCount = 7;
        private static readonly string[] ClothSlotLabels = new[]
        {
            "Top", "Bottom", "Bra", "Pants", "Gloves", "Stockings", "Shoes"
        };
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types
        private enum ClothBindingTarget
        {
            Top = 0,
            Bottom = 1,
            Bra = 2,
            Pants = 3,
            Gloves = 4,
            Stockings = 5,
            Shoes = 6
        }

        private enum ExternalColliderType
        {
            None,
            Capsule,
            Sphere
        }

        private sealed class ExternalItemColliderBinding
        {
            public OCIItem ociItem;
            public ClothBindingTarget clothTarget;
            public GameObject targetObject;
            public GameObject colliderObject;
            public GameObject visualObject;
            public Collider collider;
            public ExternalColliderType colliderType;
            public Vector3 center;
            public float radius;
            public float height;
            public ExternalColliderType visualType;
            public Dictionary<string, LineRenderer> visualLines;
        }

        private sealed class ClothInfo
        {
            public GameObject clothObj;
            public bool hasCloth;
        }

        private sealed class BinderState
        {
            public OCIChar ociChar;
            public ClothInfo[] clothInfos = Enumerable.Range(0, ClothSlotCount).Select(_ => new ClothInfo()).ToArray();
            public List<ExternalItemColliderBinding> externalItemColliderBindings = new List<ExternalItemColliderBinding>();
            public bool bindingsDirty;
        }
        #endregion

        #region Private Variables        

        internal static new ManualLogSource Logger;
        internal static ClothCollideBinder _self;
        private static string _assemblyLocation;
        private bool _loaded = false;
        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;

        private Vector2 _itemScroll;
        private readonly List<OCIItem> _cachedOciItems = new List<OCIItem>();
        private readonly Dictionary<int, BinderState> _binderStateByChar = new Dictionary<int, BinderState>();
        private ClothBindingTarget _selectedClothTarget = ClothBindingTarget.Top;
        private int _selectedItemIndex = -1;
        private OCIItem _lastUiItem;
        private ClothBindingTarget _lastUiClothTarget = ClothBindingTarget.Top;
        private ExternalColliderType _pendingColliderType = ExternalColliderType.Capsule;
        private Vector3 _pendingColliderCenter = Vector3.zero;
        private float _pendingColliderRadius = 0.2f;
        private float _pendingColliderHeight = 0.6f;
        private GameObject _selectedItemMarkerObject;
        private OCIItem _selectedMarkerItem;

        private const int _uniqueId = ('C' << 24) | ('C' << 16) | ('V' << 8) | 'S';
        
        private Rect _windowRect = new Rect(140, 10, 430, 10);
        private GUIStyle _richLabel;

        private const string EXTERNAL_COLLIDER_OBJECT_PREFIX = "CC_ExternalCollider_";
        private const string SELECTED_ITEM_MARKER_OBJECT_NAME = "CC_SelectedOCIItemMarker";
        private static readonly Color BOUND_ITEM_COLOR = new Color(1f, 0.35f, 0.35f, 1f);
        private static readonly Color DEFAULT_ITEM_COLOR = Color.white;

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
                "Open CollideBinder window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(ClothCollideBinder).Assembly).LoadTexture(),
                false, this, val =>
                {
                    _ShowUI = val;
                    if (!val)
                        ClearSelectedOciItemMarker();
                });
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);

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
            if (_loaded == false)
                return;

            ProcessDirtyRebuilds();
        }

        #endregion
        
        #region Private Methods
        private OCIChar GetCurrentOCI()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null)
                return null;

            TreeNodeObject node  = Studio.Studio.Instance.treeNodeCtrl.selectNodes
                .LastOrDefault();

            return  node != null ? Studio.Studio.GetCtrlInfo(node) as OCIChar : null;
        }
        
        private Cloth GetCloth(int clothIndex)
        {
            return GetCloth(GetCurrentOCI(), clothIndex);
        }

        private static Cloth GetCloth(OCIChar ociChar, int clothIndex)
        {
            if (clothIndex < 0 || clothIndex >= ClothSlotCount)
                return null;

            ChaControl chaControl = ociChar != null ? ociChar.GetChaControl() : null;
            if (chaControl == null || chaControl.objClothes == null || clothIndex >= chaControl.objClothes.Length)
                return null;

            GameObject clothObj = chaControl.objClothes[clothIndex];
            if (clothObj == null)
                return null;

            Cloth[] clothes = clothObj.transform.GetComponentsInChildren<Cloth>(true);
            if (clothes == null || clothes.Length == 0)
                return null;

            return clothes[0];
        }

        protected override void OnGUI()
        {
            if (_loaded == false)
                return;

            if (StudioAPI.InsideStudio) {            
                if (_ShowUI == false)
                {
                    ClearSelectedOciItemMarker();
                    return;
                }

                _windowRect = GUILayout.Window(_uniqueId + 1, _windowRect, WindowFunc, "ClothCollideBinder " + Version);
            }
            else
            {
                ClearSelectedOciItemMarker();
            }
        }

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
            BinderState binderState = EnsureBinderState(ociChar);

            // UnityEngine.Debug.Log($">> physicCollider {physicCollider}");  

            if (binderState != null)
            {
                if (_cachedOciItems.Count == 0)
                    RefreshExternalItemList(binderState);
                DrawExternalItemUI(binderState);
            } else
            {
                GUILayout.Label("<color=white>Nothing to select</color>", RichLabel);   
                ClearSelectedOciItemMarker();
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
                ClearSelectedOciItemMarker();
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }


        private float SliderRow(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(value.ToString("0.00"), GUILayout.Width(40));
            GUILayout.EndHorizontal();
            return value;
        }

        private static bool Approximately(float a, float b, float epsilon = 0.0001f)
        {
            return Mathf.Abs(a - b) <= epsilon;
        }

        private static bool ApproximatelyVector(Vector3 a, Vector3 b, float epsilon = 0.0001f)
        {
            return Mathf.Abs(a.x - b.x) <= epsilon
                && Mathf.Abs(a.y - b.y) <= epsilon
                && Mathf.Abs(a.z - b.z) <= epsilon;
        }

        private void UpdateExternalColliderBindingParams(ExternalItemColliderBinding binding, Vector3 center, float radius, float height)
        {
            if (binding == null || binding.collider == null || binding.colliderObject == null)
                return;

            binding.center = center;
            binding.radius = Mathf.Max(0.05f, radius);
            binding.height = Mathf.Max(0.1f, height);

            if (binding.visualObject == null)
                binding.visualObject = CreateExternalColliderVisual(binding);

            ApplyExternalColliderSize(binding);
        }

        private void UpdateExternalColliderPreview(ExternalItemColliderBinding binding, ExternalColliderType previewType, Vector3 center, float radius, float height)
        {
            if (binding == null || binding.colliderObject == null || previewType == ExternalColliderType.None)
                return;

            if (binding.visualObject == null)
                binding.visualObject = CreateExternalColliderVisual(binding);

            if (binding.visualLines == null)
                binding.visualLines = new Dictionary<string, LineRenderer>();

            if (binding.visualType != previewType)
            {
                ResetExternalWireLines(binding);
                binding.visualType = previewType;
            }

            const float lineWidth = 0.008f;
            Color wireColor = Color.green;

            float safeRadius = Mathf.Max(0.05f, radius);
            float safeHeight = Mathf.Max(safeRadius * 2f, height);

            if (previewType == ExternalColliderType.Sphere)
            {
                UpdateWireCircle(
                    GetOrCreateWireLine(binding, "Sphere_XZ", lineWidth, wireColor),
                    center, Quaternion.identity, safeRadius, 40);
                UpdateWireCircle(
                    GetOrCreateWireLine(binding, "Sphere_XY", lineWidth, wireColor),
                    center, Quaternion.Euler(90f, 0f, 0f), safeRadius, 40);
                UpdateWireCircle(
                    GetOrCreateWireLine(binding, "Sphere_YZ", lineWidth, wireColor),
                    center, Quaternion.Euler(0f, 0f, 90f), safeRadius, 40);
            }
            else if (previewType == ExternalColliderType.Capsule)
            {
                float halfHeight = safeHeight * 0.5f;
                float cylHalf = Mathf.Max(0f, halfHeight - safeRadius);
                Vector3 topCenter = center + Vector3.up * cylHalf;
                Vector3 bottomCenter = center - Vector3.up * cylHalf;

                UpdateWireCircle(
                    GetOrCreateWireLine(binding, "Capsule_Top", lineWidth, wireColor),
                    topCenter, Quaternion.identity, safeRadius, 32);
                UpdateWireCircle(
                    GetOrCreateWireLine(binding, "Capsule_Bottom", lineWidth, wireColor),
                    bottomCenter, Quaternion.identity, safeRadius, 32);
                UpdateCapsuleProfile(
                    GetOrCreateWireLine(binding, "Capsule_ProfileX", lineWidth, wireColor),
                    center, safeRadius, cylHalf, Vector3.right, 20);
                UpdateCapsuleProfile(
                    GetOrCreateWireLine(binding, "Capsule_ProfileZ", lineWidth, wireColor),
                    center, safeRadius, cylHalf, Vector3.forward, 20);
            }
        }

        private void DrawExternalItemUI(BinderState physicCollider)
        {
            GUILayout.Label("<color=orange>External OCIItem Binder</color>", RichLabel);
            if (physicCollider != null && physicCollider.ociChar != null && physicCollider.ociChar.charInfo != null)
                GUILayout.Label($"Character: {physicCollider.ociChar.charInfo.name}");

            bool[] hasClothByIndex = new bool[ClothSlotCount];
            bool hasSelectableTarget = false;
            OCIChar currentOci = physicCollider != null ? physicCollider.ociChar : GetCurrentOCI();
            for (int i = 0; i < ClothSlotCount; i++)
            {
                hasClothByIndex[i] = GetCloth(currentOci, i) != null;
                if (IsClothTargetButtonEnabled(i, hasClothByIndex[i]))
                    hasSelectableTarget = true;
            }

            if (!hasSelectableTarget)
            {
                GUILayout.Label("<color=white>No selectable cloth target found</color>", RichLabel);
                UpdateSelectedOciItemMarker(null);
                return;
            }

            int selectedClothIndex = (int)_selectedClothTarget;
            if (selectedClothIndex < 0 || selectedClothIndex >= ClothSlotCount || !IsClothTargetButtonEnabled(selectedClothIndex, hasClothByIndex[selectedClothIndex]))
                _selectedClothTarget = FindFirstSelectableClothTarget(hasClothByIndex);

            GUILayout.Label("<color=orange>1) Cloth Target</color>", RichLabel);
            bool prevEnabled = GUI.enabled;
            for (int rowStart = 0; rowStart < ClothSlotCount; rowStart += 4)
            {
                GUILayout.BeginHorizontal();
                int rowEnd = Mathf.Min(rowStart + 4, ClothSlotCount);
                for (int i = rowStart; i < rowEnd; i++)
                {
                    bool enabled = IsClothTargetButtonEnabled(i, hasClothByIndex[i]);
                    GUI.enabled = enabled;
                    ClothBindingTarget clothTarget = (ClothBindingTarget)i;
                    if (GUILayout.Toggle(_selectedClothTarget == clothTarget, ClothSlotLabels[i], GUI.skin.button))
                        _selectedClothTarget = clothTarget;
                }
                GUILayout.EndHorizontal();
            }
            GUI.enabled = prevEnabled;

            GUILayout.Label($"Selected: {GetClothTargetLabel(_selectedClothTarget)}");

            GUILayout.Space(4f);
            GUILayout.Label("<color=orange>2) OCIItem List</color>", RichLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh OCIItem"))
            {
                RefreshExternalItemList(physicCollider);
            }
            GUILayout.EndHorizontal();

            if (_cachedOciItems.Count == 0)
            {
                GUILayout.Label("<color=white>No OCIItem in scene</color>", RichLabel);
                UpdateSelectedOciItemMarker(null);
                return;
            }

            if (_selectedItemIndex < 0 || _selectedItemIndex >= _cachedOciItems.Count)
                _selectedItemIndex = 0;

            Color previousContentColor = GUI.contentColor;
            _itemScroll = GUILayout.BeginScrollView(_itemScroll, GUI.skin.box, GUILayout.Height(150));
            for (int i = 0; i < _cachedOciItems.Count; i++)
            {
                OCIItem item = _cachedOciItems[i];
                if (item == null)
                    continue;

                bool hasBinding = HasExternalItemColliderBinding(physicCollider, item);
                string label = ResolveItemDisplayName(item);
                GUI.contentColor = hasBinding ? BOUND_ITEM_COLOR : DEFAULT_ITEM_COLOR;
                if (GUILayout.Toggle(_selectedItemIndex == i, label, GUI.skin.button))
                {
                    if (_selectedItemIndex != i)
                        _selectedItemIndex = i;
                }
                GUI.contentColor = previousContentColor;
            }
            GUILayout.EndScrollView();
            GUI.contentColor = previousContentColor;

            OCIItem selectedItem = GetSelectedExternalItem();
            if (selectedItem == null)
            {
                UpdateSelectedOciItemMarker(null);
                return;
            }

            UpdateSelectedOciItemMarker(selectedItem);

            ExternalItemColliderBinding binding = FindExternalItemColliderBinding(physicCollider, selectedItem);
            bool selectionChanged = _lastUiItem != selectedItem || _lastUiClothTarget != _selectedClothTarget;
            if (selectionChanged)
            {
                if (binding != null)
                {
                    _pendingColliderType = binding.colliderType;
                    _pendingColliderCenter = binding.center;
                    _pendingColliderRadius = binding.radius;
                    _pendingColliderHeight = binding.height;
                }
                else
                {
                    _pendingColliderCenter = Vector3.zero;
                    _pendingColliderRadius = _pendingColliderType == ExternalColliderType.Capsule ? 0.2f : 0.22f;
                    _pendingColliderHeight = 0.6f;
                }

                _lastUiItem = selectedItem;
                _lastUiClothTarget = _selectedClothTarget;
            }

            if (binding != null)
            {
                if (binding.clothTarget != _selectedClothTarget)
                    GUILayout.Label($"<color=yellow>Bound To: {GetClothTargetLabel(binding.clothTarget)} (Remove to change)</color>", RichLabel);
                if (binding.colliderType != _pendingColliderType)
                    GUILayout.Label($"<color=yellow>Collider Type Locked: {binding.colliderType} (Remove to change)</color>", RichLabel);
            }

            GUILayout.Space(4f);
            GUILayout.Label("<color=orange>3) Collider Type</color>", RichLabel);
            GUILayout.BeginHorizontal();
            ExternalColliderType prevType = _pendingColliderType;
            if (GUILayout.Toggle(_pendingColliderType == ExternalColliderType.Capsule, "Capsule", GUI.skin.button))
            {
                if (_pendingColliderType != ExternalColliderType.Capsule)
                {
                    _pendingColliderType = ExternalColliderType.Capsule;
                    _pendingColliderHeight = Mathf.Max(_pendingColliderHeight, 0.6f);
                    _pendingColliderRadius = Mathf.Max(_pendingColliderRadius, 0.2f);
                }
            }
            if (GUILayout.Toggle(_pendingColliderType == ExternalColliderType.Sphere, "Sphere", GUI.skin.button))
            {
                if (_pendingColliderType != ExternalColliderType.Sphere)
                {
                    _pendingColliderType = ExternalColliderType.Sphere;
                    _pendingColliderRadius = Mathf.Max(_pendingColliderRadius, 0.22f);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("<color=orange>4) Collider Params</color>", RichLabel);
            Vector3 prevCenter = _pendingColliderCenter;
            float prevRadius = _pendingColliderRadius;
            float prevHeight = _pendingColliderHeight;
            _pendingColliderCenter.x = SliderRow("Center X", _pendingColliderCenter.x, -2.0f, 2.0f);
            _pendingColliderCenter.y = SliderRow("Center Y", _pendingColliderCenter.y, -2.0f, 2.0f);
            _pendingColliderCenter.z = SliderRow("Center Z", _pendingColliderCenter.z, -2.0f, 2.0f);
            _pendingColliderRadius = SliderRow("Radius", _pendingColliderRadius, 0.05f, 1.5f);
            if (_pendingColliderType == ExternalColliderType.Capsule)
                _pendingColliderHeight = SliderRow("Height", _pendingColliderHeight, 0.1f, 3.0f);

            if (binding != null && binding.colliderType == _pendingColliderType)
            {
                bool changed =
                    !ApproximatelyVector(prevCenter, _pendingColliderCenter) ||
                    !Approximately(prevRadius, _pendingColliderRadius) ||
                    !Approximately(prevHeight, _pendingColliderHeight);
                if (changed || prevType != _pendingColliderType)
                    UpdateExternalColliderBindingParams(binding, _pendingColliderCenter, _pendingColliderRadius, _pendingColliderHeight);
            }

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            bool canBind = binding == null || (binding.colliderType == _pendingColliderType && binding.clothTarget == _selectedClothTarget);
            GUI.enabled = canBind;
            if (GUILayout.Button("Bind"))
            {
                AddOrUpdateExternalColliderBinding(
                    physicCollider,
                    selectedItem,
                    _selectedClothTarget,
                    _pendingColliderType,
                    _pendingColliderCenter,
                    _pendingColliderRadius,
                    _pendingColliderHeight);
            }
            GUI.enabled = true;

            if (binding != null && GUILayout.Button("Remove"))
            {
                RemoveExternalColliderBinding(physicCollider, selectedItem);
            }
            GUILayout.EndHorizontal();
        }

        private static bool IsClothTargetButtonEnabled(int clothIndex, bool hasCloth)
        {
            if (!hasCloth)
                return false;

            if (clothIndex == (int)ClothBindingTarget.Bra || clothIndex == (int)ClothBindingTarget.Pants)
            {
#if FEATURE_SUPPORT_INNER_CLOTH
                return true;
#else
                return false;
#endif
            }

            return true;
        }

        private static ClothBindingTarget FindFirstSelectableClothTarget(bool[] hasClothByIndex)
        {
            if (hasClothByIndex == null)
                return ClothBindingTarget.Top;

            int limit = Mathf.Min(hasClothByIndex.Length, ClothSlotCount);
            for (int i = 0; i < limit; i++)
            {
                if (IsClothTargetButtonEnabled(i, hasClothByIndex[i]))
                    return (ClothBindingTarget)i;
            }
            return ClothBindingTarget.Top;
        }

        private static string GetClothTargetLabel(ClothBindingTarget clothTarget)
        {
            int index = (int)clothTarget;
            if (index < 0 || index >= ClothSlotLabels.Length)
                return clothTarget.ToString();
            return ClothSlotLabels[index];
        }

        private OCIItem GetSelectedExternalItem()
        {
            if (_selectedItemIndex < 0 || _selectedItemIndex >= _cachedOciItems.Count)
                return null;

            return _cachedOciItems[_selectedItemIndex];
        }

        private void UpdateSelectedOciItemMarker(OCIItem selectedItem)
        {
            if (selectedItem == null)
            {
                ClearSelectedOciItemMarker();
                return;
            }

            GameObject target = ResolveOciItemTarget(selectedItem);
            if (target == null)
            {
                ClearSelectedOciItemMarker();
                return;
            }

            bool needsRecreate =
                _selectedItemMarkerObject == null ||
                _selectedMarkerItem != selectedItem ||
                _selectedItemMarkerObject.transform.parent != target.transform;

            if (needsRecreate)
            {
                ClearSelectedOciItemMarker();
                _selectedItemMarkerObject = CreateSelectedOciItemMarker(target);
                _selectedMarkerItem = selectedItem;
            }

            if (_selectedItemMarkerObject == null)
                return;

            float yOffset = CalculateSelectedMarkerYOffset(target);
            _selectedItemMarkerObject.transform.localPosition = new Vector3(0f, yOffset, 0f);
            _selectedItemMarkerObject.transform.localRotation = Quaternion.identity;
            _selectedItemMarkerObject.transform.localScale = Vector3.one;
        }

        private void ClearSelectedOciItemMarker()
        {
            if (_selectedItemMarkerObject != null)
                GameObject.Destroy(_selectedItemMarkerObject);

            _selectedItemMarkerObject = null;
            _selectedMarkerItem = null;
        }

        private static GameObject CreateSelectedOciItemMarker(GameObject target)
        {
            if (target == null)
                return null;

            GameObject marker = new GameObject(SELECTED_ITEM_MARKER_OBJECT_NAME);
            marker.transform.SetParent(target.transform, false);
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;

            CreateSelectedMarkerLine(
                marker.transform,
                "Shaft",
                new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0.30f, 0f) });
            CreateSelectedMarkerLine(
                marker.transform,
                "Head_Left",
                new[] { new Vector3(0f, 0.30f, 0f), new Vector3(-0.09f, 0.21f, 0f) });
            CreateSelectedMarkerLine(
                marker.transform,
                "Head_Right",
                new[] { new Vector3(0f, 0.30f, 0f), new Vector3(0.09f, 0.21f, 0f) });

            return marker;
        }

        private static void CreateSelectedMarkerLine(Transform parent, string lineName, Vector3[] points)
        {
            if (parent == null || points == null || points.Length < 2)
                return;

            GameObject lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(parent, false);

            LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
            ConfigureWireLineRenderer(lineRenderer, 0.012f, Color.green);
            lineRenderer.positionCount = points.Length;
            lineRenderer.SetPositions(points);
        }

        private static float CalculateSelectedMarkerYOffset(GameObject target)
        {
            if (target == null)
                return 0.6f;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return 0.6f;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return Mathf.Max(0.3f, bounds.extents.y + 0.2f);
        }

        private static ExternalItemColliderBinding FindExternalItemColliderBinding(BinderState physicCollider, OCIItem ociItem)
        {
            if (physicCollider == null || ociItem == null)
                return null;

            return physicCollider.externalItemColliderBindings
                .FirstOrDefault(v => v != null && v.ociItem == ociItem);
        }

        private static bool HasExternalItemColliderBinding(BinderState physicCollider, OCIItem ociItem)
        {
            return FindExternalItemColliderBinding(physicCollider, ociItem) != null;
        }

        private void RefreshExternalItemList(BinderState physicCollider)
        {
            _cachedOciItems.Clear();
            _cachedOciItems.AddRange(GetAllOCIItemFromStudio() ?? new List<OCIItem>());

            if (_selectedItemIndex >= _cachedOciItems.Count)
                _selectedItemIndex = _cachedOciItems.Count > 0 ? 0 : -1;

            if (physicCollider == null)
                return;

            bool removedInvalidBinding = false;
            List<ExternalItemColliderBinding> invalidBindings = physicCollider.externalItemColliderBindings
                .Where(v => v == null || v.ociItem == null || !_cachedOciItems.Contains(v.ociItem))
                .ToList();

            foreach (ExternalItemColliderBinding invalid in invalidBindings)
            {
                if (invalid == null)
                    continue;

                if (invalid.collider != null)
                    GameObject.Destroy(invalid.collider);

                if (invalid.colliderObject != null)
                    GameObject.Destroy(invalid.colliderObject);

                if (invalid.visualObject != null)
                    GameObject.Destroy(invalid.visualObject);

                physicCollider.externalItemColliderBindings.Remove(invalid);
                removedInvalidBinding = true;
            }

            if (_selectedMarkerItem != null && !_cachedOciItems.Contains(_selectedMarkerItem))
                ClearSelectedOciItemMarker();

            bool removedDuplicate = CleanupDuplicateBindings(physicCollider);
            if (removedInvalidBinding || removedDuplicate)
                MarkBindingsDirty(physicCollider);
        }

        private void AddOrUpdateExternalColliderBinding(
            BinderState physicCollider,
            OCIItem ociItem,
            ClothBindingTarget clothTarget,
            ExternalColliderType colliderType,
            Vector3 center,
            float radius,
            float height)
        {
            if (physicCollider == null || ociItem == null || colliderType == ExternalColliderType.None)
                return;

            GameObject target = ResolveOciItemTarget(ociItem);
            if (target == null)
            {
                Logger.LogWarning($"[ClothCollideBinder] Cannot resolve target transform for OCIItem: {ResolveItemDisplayName(ociItem)}");
                return;
            }

            ExternalItemColliderBinding existing = FindExternalItemColliderBinding(physicCollider, ociItem);
            if (existing != null)
            {
                if (existing.colliderType != colliderType || existing.clothTarget != clothTarget)
                {
                    Logger.LogWarning("[ClothCollideBinder] An OCIItem can have only one collider. Remove first to change type/target.");
                    return;
                }

                if (existing.colliderType == colliderType && existing.collider != null && existing.colliderObject != null)
                {
                    existing.center = center;
                    existing.radius = Mathf.Max(0.05f, radius);
                    existing.height = Mathf.Max(0.1f, height);
                    if (existing.visualObject == null)
                        existing.visualObject = CreateExternalColliderVisual(existing);
                    ApplyExternalColliderSize(existing);
                    return;
                }

                if (existing.collider != null)
                    GameObject.Destroy(existing.collider);
                if (existing.colliderObject != null)
                    GameObject.Destroy(existing.colliderObject);
                if (existing.visualObject != null)
                    GameObject.Destroy(existing.visualObject);
                physicCollider.externalItemColliderBindings.Remove(existing);
            }

            GameObject colliderObject = new GameObject($"{EXTERNAL_COLLIDER_OBJECT_PREFIX}{clothTarget}_{colliderType}");
            colliderObject.transform.SetParent(target.transform, false);

            Collider collider = null;
            if (colliderType == ExternalColliderType.Capsule)
            {
                CapsuleCollider capsule = colliderObject.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                collider = capsule;
            }
            else if (colliderType == ExternalColliderType.Sphere)
            {
                SphereCollider sphere = colliderObject.AddComponent<SphereCollider>();
                collider = sphere;
            }

            if (collider == null)
            {
                GameObject.Destroy(colliderObject);
                return;
            }

            ExternalItemColliderBinding binding = new ExternalItemColliderBinding
            {
                ociItem = ociItem,
                clothTarget = clothTarget,
                targetObject = target,
                colliderObject = colliderObject,
                collider = collider,
                colliderType = colliderType,
                center = center,
                radius = Mathf.Max(0.05f, radius),
                height = Mathf.Max(0.1f, height)
            };

            binding.visualObject = CreateExternalColliderVisual(binding);
            ApplyExternalColliderSize(binding);

            physicCollider.externalItemColliderBindings.Add(binding);
            MarkBindingsDirty(physicCollider);
        }

        private void RemoveExternalColliderBinding(BinderState physicCollider, OCIItem ociItem)
        {
            if (physicCollider == null || ociItem == null)
                return;

            ExternalItemColliderBinding existing = FindExternalItemColliderBinding(physicCollider, ociItem);
            if (existing == null)
                return;

            if (existing.collider != null)
                GameObject.Destroy(existing.collider);
            if (existing.colliderObject != null)
                GameObject.Destroy(existing.colliderObject);
            if (existing.visualObject != null)
                GameObject.Destroy(existing.visualObject);

            physicCollider.externalItemColliderBindings.Remove(existing);
            MarkBindingsDirty(physicCollider);
        }

        private static void MarkBindingsDirty(BinderState state)
        {
            if (state != null)
                state.bindingsDirty = true;
        }

        private bool CleanupDuplicateBindings(BinderState state)
        {
            if (state == null || state.externalItemColliderBindings == null || state.externalItemColliderBindings.Count == 0)
                return false;

            var seen = new HashSet<OCIItem>();
            var duplicates = new List<ExternalItemColliderBinding>();
            foreach (var binding in state.externalItemColliderBindings)
            {
                if (binding == null || binding.ociItem == null)
                    continue;
                if (!seen.Add(binding.ociItem))
                    duplicates.Add(binding);
            }

            if (duplicates.Count == 0)
                return false;

            foreach (var dup in duplicates)
            {
                if (dup.collider != null)
                    GameObject.Destroy(dup.collider);
                if (dup.colliderObject != null)
                    GameObject.Destroy(dup.colliderObject);
                if (dup.visualObject != null)
                    GameObject.Destroy(dup.visualObject);
                state.externalItemColliderBindings.Remove(dup);
            }

            return true;
        }

        private void ProcessDirtyRebuilds()
        {
            if (_binderStateByChar.Count == 0)
                return;

            foreach (var kvp in _binderStateByChar)
            {
                BinderState state = kvp.Value;
                if (state == null || !state.bindingsDirty)
                    continue;

                state.bindingsDirty = false;
                RebuildCharacterClothColliderBindings(state);
            }
        }

        private static void ApplyExternalColliderSize(ExternalItemColliderBinding binding)
        {
            if (binding == null || binding.collider == null)
                return;

            if (binding.colliderType == ExternalColliderType.Capsule)
            {
                CapsuleCollider capsule = binding.collider as CapsuleCollider;
                if (capsule == null)
                    return;

                capsule.center = binding.center;
                capsule.radius = binding.radius;
                capsule.height = Mathf.Max(binding.radius * 2f, binding.height);
            }
            else if (binding.colliderType == ExternalColliderType.Sphere)
            {
                SphereCollider sphere = binding.collider as SphereCollider;
                if (sphere == null)
                    return;

                sphere.center = binding.center;
                sphere.radius = binding.radius;
            }

            UpdateExternalColliderVisual(binding);
        }

        private static GameObject CreateExternalColliderVisual(ExternalItemColliderBinding binding)
        {
            if (binding == null || binding.colliderObject == null)
                return null;

            GameObject visual = new GameObject("CC_ExternalColliderVisual");
            visual.transform.SetParent(binding.colliderObject.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            binding.visualLines = binding.visualLines ?? new Dictionary<string, LineRenderer>();
            binding.visualType = binding.colliderType;
            return visual;
        }

        private static void UpdateExternalColliderVisual(ExternalItemColliderBinding binding)
        {
            if (binding == null || binding.visualObject == null || binding.collider == null)
                return;

            const float lineWidth = 0.008f;
            Color wireColor = Color.green;
            if (binding.visualLines == null)
                binding.visualLines = new Dictionary<string, LineRenderer>();
            if (binding.visualType != binding.colliderType)
            {
                ResetExternalWireLines(binding);
                binding.visualType = binding.colliderType;
            }

            if (binding.colliderType == ExternalColliderType.Sphere)
            {
                SphereCollider sphere = binding.collider as SphereCollider;
                if (sphere == null)
                    return;

                UpdateWireCircle(
                    GetOrCreateWireLine(binding, "Sphere_XZ", lineWidth, wireColor),
                    sphere.center, Quaternion.identity, sphere.radius, 40);
                UpdateWireCircle(
                    GetOrCreateWireLine(binding, "Sphere_XY", lineWidth, wireColor),
                    sphere.center, Quaternion.Euler(90f, 0f, 0f), sphere.radius, 40);
                UpdateWireCircle(
                    GetOrCreateWireLine(binding, "Sphere_YZ", lineWidth, wireColor),
                    sphere.center, Quaternion.Euler(0f, 0f, 90f), sphere.radius, 40);
            }
            else if (binding.colliderType == ExternalColliderType.Capsule)
            {
                CapsuleCollider capsule = binding.collider as CapsuleCollider;
                if (capsule == null)
                    return;

                float radius = capsule.radius;
                float halfHeight = capsule.height * 0.5f;
                float cylHalf = Mathf.Max(0f, halfHeight - radius);
                Vector3 center = capsule.center;

                Vector3 topCenter = center + Vector3.up * cylHalf;
                Vector3 bottomCenter = center - Vector3.up * cylHalf;

                UpdateWireCircle(
                    GetOrCreateWireLine(binding, "Capsule_Top", lineWidth, wireColor),
                    topCenter, Quaternion.identity, radius, 32);
                UpdateWireCircle(
                    GetOrCreateWireLine(binding, "Capsule_Bottom", lineWidth, wireColor),
                    bottomCenter, Quaternion.identity, radius, 32);
                UpdateCapsuleProfile(
                    GetOrCreateWireLine(binding, "Capsule_ProfileX", lineWidth, wireColor),
                    center, radius, cylHalf, Vector3.right, 20);
                UpdateCapsuleProfile(
                    GetOrCreateWireLine(binding, "Capsule_ProfileZ", lineWidth, wireColor),
                    center, radius, cylHalf, Vector3.forward, 20);
            }
        }

        private static void ResetExternalWireLines(ExternalItemColliderBinding binding)
        {
            if (binding == null || binding.visualObject == null)
                return;
            ClearExternalWireLines(binding.visualObject.transform);
            if (binding.visualLines != null)
                binding.visualLines.Clear();
        }

        private static void ClearExternalWireLines(Transform root)
        {
            if (root == null)
                return;

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child != null)
                    GameObject.Destroy(child.gameObject);
            }
        }

        private static LineRenderer GetOrCreateWireLine(ExternalItemColliderBinding binding, string name, float width, Color color)
        {
            if (binding == null || binding.visualObject == null)
                return null;

            if (binding.visualLines != null && binding.visualLines.TryGetValue(name, out var cached) && cached != null)
            {
                ConfigureWireLineRenderer(cached, width, color);
                return cached;
            }

            LineRenderer lr = null;
            Transform existing = binding.visualObject.transform.Find(name);
            if (existing != null)
                lr = existing.GetComponent<LineRenderer>();

            if (lr == null)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(binding.visualObject.transform, false);
                lr = go.AddComponent<LineRenderer>();
            }

            ConfigureWireLineRenderer(lr, width, color);
            if (binding.visualLines != null)
                binding.visualLines[name] = lr;
            return lr;
        }

        private static void UpdateWireCircle(LineRenderer lr, Vector3 center, Quaternion rotation, float radius, int segments)
        {
            if (lr == null)
                return;

            lr.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = t * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                lr.SetPosition(i, center + (rotation * p));
            }
        }

        private static void UpdateCapsuleProfile(LineRenderer lr, Vector3 center, float radius, float cylHalf, Vector3 axis, int segments)
        {
            if (lr == null)
                return;

            int total = (segments + 1) * 2 + 1;
            Vector3[] points = new Vector3[total];
            int idx = 0;

            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.Lerp(0f, Mathf.PI, (float)i / segments);
                float u = Mathf.Cos(t) * radius;
                float y = -cylHalf + Mathf.Sin(t) * radius;
                points[idx++] = center + axis * u + Vector3.up * y;
            }

            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.Lerp(Mathf.PI, Mathf.PI * 2f, (float)i / segments);
                float u = Mathf.Cos(t) * radius;
                float y = cylHalf + Mathf.Sin(t) * radius;
                points[idx++] = center + axis * u + Vector3.up * y;
            }

            points[idx] = points[0];
            lr.positionCount = points.Length;
            lr.SetPositions(points);
        }

        private static void AddWireCircle(Transform parent, Vector3 center, Quaternion rotation, float radius, int segments, float width, Color color, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            ConfigureWireLineRenderer(lr, width, color);

            lr.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float angle = t * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                lr.SetPosition(i, center + (rotation * p));
            }
        }

        private static void AddCapsuleProfile(Transform parent, Vector3 center, float radius, float cylHalf, Vector3 axis, int segments, float width, Color color, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            ConfigureWireLineRenderer(lr, width, color);

            List<Vector3> points = new List<Vector3>();

            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.Lerp(0f, Mathf.PI, (float)i / segments);
                float u = Mathf.Cos(t) * radius;
                float y = -cylHalf + Mathf.Sin(t) * radius;
                points.Add(center + axis * u + Vector3.up * y);
            }

            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.Lerp(Mathf.PI, Mathf.PI * 2f, (float)i / segments);
                float u = Mathf.Cos(t) * radius;
                float y = cylHalf + Mathf.Sin(t) * radius;
                points.Add(center + axis * u + Vector3.up * y);
            }

            points.Add(points[0]);
            lr.positionCount = points.Count;
            lr.SetPositions(points.ToArray());
        }

        private static void ConfigureWireLineRenderer(LineRenderer lr, float width, Color color)
        {
            if (lr == null)
                return;

            lr.useWorldSpace = false;
            lr.loop = false;
            lr.widthMultiplier = width;
            lr.startColor = color;
            lr.endColor = color;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader != null && (lr.sharedMaterial == null || lr.sharedMaterial.shader != shader))
                lr.sharedMaterial = new Material(shader);
        }

        private void RebuildCharacterClothColliderBindings(BinderState physicCollider)
        {
            if (physicCollider == null || physicCollider.ociChar == null || physicCollider.ociChar.charInfo == null)
                return;

            List<Cloth> allCloths = new List<Cloth>();
            foreach (ClothInfo info in physicCollider.clothInfos)
            {
                if (info == null || !info.hasCloth || info.clothObj == null)
                    continue;
                allCloths.AddRange(info.clothObj.GetComponentsInChildren<Cloth>(true));
            }

            if (allCloths.Count == 0)
                return;

            var clothSetsByTarget = new HashSet<Cloth>[ClothSlotCount];
            for (int i = 0; i < ClothSlotCount; i++)
                clothSetsByTarget[i] = new HashSet<Cloth>(GetClothsByTarget(physicCollider, (ClothBindingTarget)i));

            var capsuleCollidersByTarget = new List<CapsuleCollider>[ClothSlotCount];
            var sphereCollidersByTarget = new List<SphereCollider>[ClothSlotCount];
            for (int i = 0; i < ClothSlotCount; i++)
            {
                capsuleCollidersByTarget[i] = new List<CapsuleCollider>();
                sphereCollidersByTarget[i] = new List<SphereCollider>();
            }

            foreach (ExternalItemColliderBinding binding in physicCollider.externalItemColliderBindings)
            {
                if (binding == null || binding.collider == null)
                    continue;

                int targetIndex = (int)binding.clothTarget;
                if (targetIndex < 0 || targetIndex >= ClothSlotCount)
                    continue;

                if (binding.colliderType == ExternalColliderType.Capsule)
                {
                    CapsuleCollider capsule = binding.collider as CapsuleCollider;
                    if (capsule != null)
                        capsuleCollidersByTarget[targetIndex].Add(capsule);
                }
                else if (binding.colliderType == ExternalColliderType.Sphere)
                {
                    SphereCollider sphere = binding.collider as SphereCollider;
                    if (sphere != null)
                        sphereCollidersByTarget[targetIndex].Add(sphere);
                }
            }

            foreach (Cloth cloth in allCloths)
            {
                if (cloth == null)
                    continue;

                List<CapsuleCollider> capsules = (cloth.capsuleColliders ?? Array.Empty<CapsuleCollider>())
                    .Where(v => v != null && !v.name.StartsWith(EXTERNAL_COLLIDER_OBJECT_PREFIX))
                    .ToList();

                for (int targetIndex = 0; targetIndex < ClothSlotCount; targetIndex++)
                {
                    if (!clothSetsByTarget[targetIndex].Contains(cloth))
                        continue;

                    List<CapsuleCollider> addCapsules = capsuleCollidersByTarget[targetIndex];
                    capsules.AddRange(addCapsules.Where(v => v != null && !capsules.Contains(v)));
                }
                cloth.capsuleColliders = capsules.ToArray();

                List<ClothSphereColliderPair> spherePairs = (cloth.sphereColliders ?? Array.Empty<ClothSphereColliderPair>())
                    .Where(v =>
                        (v.first == null || !v.first.name.StartsWith(EXTERNAL_COLLIDER_OBJECT_PREFIX)) &&
                        (v.second == null || !v.second.name.StartsWith(EXTERNAL_COLLIDER_OBJECT_PREFIX)))
                    .ToList();

                for (int targetIndex = 0; targetIndex < ClothSlotCount; targetIndex++)
                {
                    if (!clothSetsByTarget[targetIndex].Contains(cloth))
                        continue;

                    List<SphereCollider> addSpheres = sphereCollidersByTarget[targetIndex];
                    foreach (SphereCollider sphere in addSpheres)
                    {
                        bool exists = spherePairs.Any(v => v.first == sphere || v.second == sphere);
                        if (!exists)
                        {
                            ClothSphereColliderPair pair = new ClothSphereColliderPair();
                            pair.first = sphere;
                            pair.second = null;
                            spherePairs.Add(pair);
                        }
                    }
                }

                cloth.sphereColliders = spherePairs.ToArray();
            }
        }

        private static List<Cloth> GetClothsByTarget(BinderState physicCollider, ClothBindingTarget clothTarget)
        {
            if (physicCollider == null || physicCollider.clothInfos == null)
                return new List<Cloth>();

            int clothInfoIndex = (int)clothTarget;
            if (clothInfoIndex < 0 || clothInfoIndex >= physicCollider.clothInfos.Length)
                return new List<Cloth>();

            ClothInfo info = physicCollider.clothInfos[clothInfoIndex];
            if (info == null || !info.hasCloth || info.clothObj == null)
                return new List<Cloth>();

            return info.clothObj.GetComponentsInChildren<Cloth>(true).Where(v => v != null).ToList();
        }

        private static GameObject ResolveOciItemTarget(OCIItem ociItem)
        {
            if (ociItem == null)
                return null;

            return ociItem.guideObject.gameObject;
        }

        private static string ResolveItemDisplayName(OCIItem ociItem)
        {
            if (ociItem == null)
                return "(null)";

            return ociItem.treeNodeObject.textName; // ociItem.itemInfo.GetObjectCtrlInfo().Getname;// ociItem.treeNodeObject.textName;
        }

#if FEATURE_SCENE_SAVE
        private void OnSceneLoad(string path)
        {
            PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
            if (data == null)
                return;
            XmlDocument doc = new XmlDocument();
            doc.LoadXml((string)data.data["sceneInfo"]);
            XmlNode node = doc.FirstChild;
            if (node == null)
                return;
            SceneLoad(path, node);
        }

        private void OnSceneImport(string path)
        {
            Logger.LogMessage($"Import not support");
        }

        private void OnSceneSave(string path)
        {
            using (StringWriter stringWriter = new StringWriter())
            using (XmlTextWriter xmlWriter = new XmlTextWriter(stringWriter))
            {
                xmlWriter.WriteStartElement("root");
                SceneWrite(path, xmlWriter);
                xmlWriter.WriteEndElement();

                PluginData data = new PluginData();
                data.version = ClothCollideBinder._saveVersion;
                data.data.Add("sceneInfo", stringWriter.ToString());

                // UnityEngine.Debug.Log($">> visualizer sceneInfo {stringWriter.ToString()}");

                ExtendedSave.SetSceneExtendedDataById(_extSaveKey, data);
            }
        }

        private void SceneLoad(string path, XmlNode node)
        {
            if (node == null)
                return;
            this.ExecuteDelayed2(() =>
            {
                List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();

                List<OCIChar> ociChars = dic
                    .Select(kv => kv.Value as OCIChar)
                    .Where(c => c != null)
                    .ToList();

                SceneRead(node, ociChars);
            }, 20);
        }

        private void SceneRead(XmlNode node, List<OCIChar> ociChars)
        {
            var ociCharByDicKey = new Dictionary<int, OCIChar>();
            foreach (var kvp in Studio.Studio.Instance.dicObjectCtrl)
            {
                var oci = kvp.Value as OCIChar;
                if (oci != null)
                    ociCharByDicKey[kvp.Key] = oci;
            }

            var ociCharByHash = new Dictionary<int, OCIChar>();
            foreach (var oci in ociChars)
            {
                if (oci == null)
                    continue;
                int hash = oci.GetChaControl().GetHashCode();
                if (!ociCharByHash.ContainsKey(hash))
                    ociCharByHash.Add(hash, oci);
            }

            var ociCharByName = new Dictionary<string, OCIChar>(StringComparer.Ordinal);
            foreach (var oci in ociChars)
            {
                if (oci == null || oci.charInfo == null)
                    continue;
                string name = oci.charInfo.name;
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!ociCharByName.ContainsKey(name))
                    ociCharByName.Add(name, oci);
            }

            var ociItemByDicKey = new Dictionary<int, OCIItem>();
            foreach (var kvp in Studio.Studio.Instance.dicObjectCtrl)
            {
                var ociItem = kvp.Value as OCIItem;
                if (ociItem != null)
                    ociItemByDicKey[kvp.Key] = ociItem;
            }

            var ociItemByHash = new Dictionary<int, OCIItem>();
            foreach (var ociItem in ociItemByDicKey.Values)
            {
                if (ociItem == null)
                    continue;
                int hash = ociItem.GetHashCode();
                if (!ociItemByHash.ContainsKey(hash))
                    ociItemByHash.Add(hash, ociItem);
            }

            var ociItemByName = new Dictionary<string, OCIItem>(StringComparer.Ordinal);
            foreach (var ociItem in ociItemByDicKey.Values)
            {
                if (ociItem == null)
                    continue;
                string name = ResolveItemDisplayName(ociItem);
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!ociItemByName.ContainsKey(name))
                    ociItemByName.Add(name, ociItem);
            }

            foreach (XmlNode charNode in node.SelectNodes("character"))
            {
                OCIChar ociChar = null;

                string dicKeyText = charNode.Attributes["dicKey"]?.Value;
                if (!string.IsNullOrEmpty(dicKeyText) && int.TryParse(dicKeyText, out int dicKeyValue))
                    ociChar = FindOciCharByDicKey(ociCharByDicKey, dicKeyValue);

                if (ociChar == null)
                {
                    string hashText = charNode.Attributes["hash"]?.Value;
                    if (!string.IsNullOrEmpty(hashText) && int.TryParse(hashText, out int hashValue))
                        ociCharByHash.TryGetValue(hashValue, out ociChar);
                }

                if (ociChar == null)
                {
                    string name = charNode.Attributes["name"]?.Value;
                    if (!string.IsNullOrEmpty(name))
                        ociCharByName.TryGetValue(name, out ociChar);
                }

                if (ociChar == null)
                    continue;

                RemoveBinderState(ociChar);
                BinderState state = EnsureBinderState(ociChar);
                if (state == null)
                    continue;

                foreach (XmlNode bindingNode in charNode.SelectNodes("binding"))
                {
                    OCIItem ociItem = null;

                    string itemDicKeyText = bindingNode.Attributes["itemDicKey"]?.Value;
                    if (!string.IsNullOrEmpty(itemDicKeyText) && int.TryParse(itemDicKeyText, out int itemDicKey))
                        ociItem = FindOciItemByDicKey(ociItemByDicKey, itemDicKey);

                    if (ociItem == null)
                    {
                        string itemHashText = bindingNode.Attributes["itemHash"]?.Value;
                        if (!string.IsNullOrEmpty(itemHashText) && int.TryParse(itemHashText, out int itemHash))
                            ociItemByHash.TryGetValue(itemHash, out ociItem);
                    }

                    if (ociItem == null)
                    {
                        string itemName = bindingNode.Attributes["itemName"]?.Value;
                        if (!string.IsNullOrEmpty(itemName))
                            ociItemByName.TryGetValue(itemName, out ociItem);
                    }

                    if (ociItem == null)
                        continue;

                    if (!Enum.TryParse(bindingNode.Attributes["clothTarget"]?.Value, true, out ClothBindingTarget clothTarget))
                        clothTarget = ClothBindingTarget.Top;

                    if (!Enum.TryParse(bindingNode.Attributes["colliderType"]?.Value, true, out ExternalColliderType colliderType))
                        continue;
                    if (colliderType == ExternalColliderType.None)
                        continue;

                    Vector3 center = new Vector3(
                        ParseFloat(bindingNode.Attributes["centerX"]?.Value),
                        ParseFloat(bindingNode.Attributes["centerY"]?.Value),
                        ParseFloat(bindingNode.Attributes["centerZ"]?.Value));
                    float radius = ParseFloat(bindingNode.Attributes["radius"]?.Value);
                    float height = ParseFloat(bindingNode.Attributes["height"]?.Value);

                    AddOrUpdateExternalColliderBinding(
                        state,
                        ociItem,
                        clothTarget,
                        colliderType,
                        center,
                        radius > 0f ? radius : 0.2f,
                        height > 0f ? height : 0.6f);
                }
            }
        }

        // 유틸: 문자열 float 파싱
        private float ParseFloat(string value)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
            return 0f;
        }

        private void SceneWrite(string path, XmlTextWriter writer)
        {
            var dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
            var ociCharByDicKey = dic
                .Where(kv => kv.Value is OCIChar)
                .ToDictionary(kv => kv.Key, kv => kv.Value as OCIChar);

            foreach (var kv in ociCharByDicKey)
            {
                int charDicKey = kv.Key;
                OCIChar ociChar = kv.Value;
                if (ociChar == null)
                    continue;

                BinderState state = GetCurrentData(ociChar);
                if (state == null || state.externalItemColliderBindings == null || state.externalItemColliderBindings.Count == 0)
                    continue;

                writer.WriteStartElement("character");
                writer.WriteAttributeString("dicKey", charDicKey.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("hash", ociChar.GetChaControl().GetHashCode().ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("name", ociChar.charInfo != null ? ociChar.charInfo.name : string.Empty);

                foreach (var binding in state.externalItemColliderBindings)
                {
                    if (binding == null || binding.ociItem == null)
                        continue;

                    int itemDicKey;
                    bool hasItemDicKey = TryGetItemDicKey(binding.ociItem, out itemDicKey);

                    writer.WriteStartElement("binding");
                    if (hasItemDicKey)
                        writer.WriteAttributeString("itemDicKey", itemDicKey.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("itemHash", binding.ociItem.GetHashCode().ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("itemName", ResolveItemDisplayName(binding.ociItem));
                    writer.WriteAttributeString("clothTarget", binding.clothTarget.ToString());
                    writer.WriteAttributeString("colliderType", binding.colliderType.ToString());
                    writer.WriteAttributeString("centerX", binding.center.x.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("centerY", binding.center.y.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("centerZ", binding.center.z.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("radius", binding.radius.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("height", binding.height.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }
        }

        private bool TryGetItemDicKey(OCIItem ociItem, out int dicKey)
        {
            dicKey = 0;
            if (ociItem == null)
                return false;

            foreach (var kvp in Studio.Studio.Instance.dicObjectCtrl)
            {
                var sceneItem = kvp.Value as OCIItem;
                if (sceneItem != null && sceneItem == ociItem)
                {
                    dicKey = kvp.Key;
                    return true;
                }
            }
            return false;
        }

        private OCIChar FindOciCharByDicKey(Dictionary<int, OCIChar> map, int savedDicKey)
        {
            if (map.TryGetValue(savedDicKey, out var oci))
                return oci;
            return null;
        }

        private OCIItem FindOciItemByDicKey(Dictionary<int, OCIItem> map, int savedDicKey)
        {
            if (map.TryGetValue(savedDicKey, out var ociItem))
                return ociItem;
            return null;
        }
#endif
        
        private void Init()
        {
            _loaded = true;
        }

        private void SceneInit()
        {
            Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
			_ShowUI = false;
            ClearSelectedOciItemMarker();
        }    

        private static List<OCIItem> GetAllOCIItemFromStudio()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.dicObjectCtrl == null)
                return new List<OCIItem>();

            return new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl)
                .Select(kv => kv.Value as OCIItem)
                .Where(v => v != null)
                .OrderBy(v => ResolveItemDisplayName(v))
                .ToList();
        }

        private static IEnumerator ExecuteAfterFrame(OCIChar ociChar, int frameCount)
        {
            for (int i = 0; i < frameCount; i++)
                yield return null;
                

            if (ociChar != null && _ShowUI)
                _self.EnsureBinderState(ociChar);
        }

        private BinderState GetCurrentData(OCIChar ociChar)
        {
            if (ociChar == null)
                return null;

            BinderState state;
            return _binderStateByChar.TryGetValue(ociChar.GetHashCode(), out state) ? state : null;
        }

        private BinderState EnsureBinderState(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.charInfo == null)
                return null;

            int key = ociChar.GetHashCode();
            BinderState state;
            if (!_binderStateByChar.TryGetValue(key, out state) || state == null)
            {
                state = new BinderState();
                _binderStateByChar[key] = state;
            }

            state.ociChar = ociChar;

            var clothes = ociChar.GetChaControl() != null ? ociChar.GetChaControl().objClothes : null;
            bool clothInfoChanged = false;
            for (int i = 0; i < state.clothInfos.Length; i++)
            {
                GameObject clothObj = (clothes != null && i < clothes.Length) ? clothes[i] : null;
                bool hasCloth = clothObj != null && clothObj.GetComponentsInChildren<Cloth>(true).Length > 0;
                if (state.clothInfos[i].clothObj != clothObj || state.clothInfos[i].hasCloth != hasCloth)
                    clothInfoChanged = true;
                state.clothInfos[i].clothObj = clothObj;
                state.clothInfos[i].hasCloth = hasCloth;
            }

            bool removedDuplicate = CleanupDuplicateBindings(state);
            if (clothInfoChanged || removedDuplicate)
                MarkBindingsDirty(state);
            return state;
        }

        private void RemoveBinderState(OCIChar ociChar)
        {
            if (ociChar == null)
                return;

            int key = ociChar.GetHashCode();
            BinderState state;
            if (!_binderStateByChar.TryGetValue(key, out state) || state == null)
                return;

            foreach (var binding in state.externalItemColliderBindings)
            {
                if (binding == null)
                    continue;
                if (binding.collider != null)
                    GameObject.Destroy(binding.collider);
                if (binding.colliderObject != null)
                    GameObject.Destroy(binding.colliderObject);
                if (binding.visualObject != null)
                    GameObject.Destroy(binding.visualObject);
            }

            state.externalItemColliderBindings.Clear();
            _binderStateByChar.Remove(key);
        }
        #endregion

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
                    _self.RemoveBinderState(ociChar);
                    __instance.GetChaControl().StartCoroutine(ExecuteAfterFrame(ociChar, 10));
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
                    _self.RemoveBinderState(ociChar);
                    __instance.StartCoroutine(ExecuteAfterFrame(ociChar, 10));
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
                    _self.RemoveBinderState(ociChar);
                    __instance.StartCoroutine(ExecuteAfterFrame(ociChar, 10));
                }
            }
        }


        #endregion
    }
}
