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

    확인사항:
        - 이게 성능 이슈가 좀 있는거 같아.. collider 생성 시점 많이 느려지는 느낌이 존재해..원인을 한번 확인해봐..
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
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types
        private enum ClothBindingTarget
        {
            Top,
            Bottom
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
        }

        private sealed class ClothInfo
        {
            public GameObject clothObj;
            public bool hasCloth;
        }

        private sealed class BinderState
        {
            public OCIChar ociChar;
            public ClothInfo[] clothInfos = Enumerable.Range(0, 8).Select(_ => new ClothInfo()).ToArray();
            public List<ExternalItemColliderBinding> externalItemColliderBindings = new List<ExternalItemColliderBinding>();
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

        private const int _uniqueId = ('C' << 24) | ('C' << 16) | ('V' << 8) | 'S';
        
        private Rect _windowRect = new Rect(140, 10, 430, 10);
        private GUIStyle _richLabel;

        private const string EXTERNAL_COLLIDER_OBJECT_PREFIX = "CC_ExternalCollider_";

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
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(ClothCollideBinder).Assembly).LoadTexture(),
                false, this, val =>
                {
                    _ShowUI = val;
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
        }

        private OCIChar GetCurrentOCI()
        {
            OCIChar ociChar = GetLastOCICharFromStudio();

            return ociChar;
        }
        
        private Cloth GetClothTop()
        {
            OCIChar ociChar = GetCurrentOCI();
            if (ociChar != null)
            {
                if (ociChar.GetChaControl().objClothes[0] != null)
                {
                    Cloth[] clothes = ociChar.GetChaControl().objClothes[0].transform.GetComponentsInChildren<Cloth>(true);

                    if (clothes.Length > 0)
                    {
                        return clothes[0];
                    }
                }
            }

            return null;        
        }

        private Cloth GetClothBottom()
        {
            OCIChar ociChar = GetCurrentOCI();
            if (ociChar != null)
            {
                if (ociChar.GetChaControl().objClothes[1] != null)
                {
                    Cloth[] clothes = ociChar.GetChaControl().objClothes[1].transform.GetComponentsInChildren<Cloth>(true);

                    if (clothes.Length > 0)
                    {
                        return clothes[0];
                    }
                }
            }

            return null;             
        }

        protected override void OnGUI()
        {
            if (_ShowUI == false)
                return;

            if (StudioAPI.InsideStudio)
                _windowRect = GUILayout.Window(_uniqueId + 1, _windowRect, WindowFunc, "ClothCollideBinder " + Version);
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

        private void DrawExternalItemUI(BinderState physicCollider)
        {
            GUILayout.Label("<color=orange>External OCIItem Binder</color>", RichLabel);
            if (physicCollider != null && physicCollider.ociChar != null && physicCollider.ociChar.charInfo != null)
                GUILayout.Label($"Character: {physicCollider.ociChar.charInfo.name}");

            Cloth clothTop = GetClothTop();
            Cloth clothBottom = GetClothBottom();
            bool hasTop = clothTop != null;
            bool hasBottom = clothBottom != null;

            if (!hasTop && !hasBottom)
            {
                GUILayout.Label("<color=white>No cloth found in Top/Bottom slot</color>", RichLabel);
                return;
            }

            if (_selectedClothTarget == ClothBindingTarget.Top && !hasTop)
                _selectedClothTarget = ClothBindingTarget.Bottom;
            if (_selectedClothTarget == ClothBindingTarget.Bottom && !hasBottom)
                _selectedClothTarget = ClothBindingTarget.Top;

            GUILayout.Label("<color=orange>1) Cloth Target</color>", RichLabel);
            GUILayout.BeginHorizontal();
            bool prevEnabled = GUI.enabled;

            GUI.enabled = hasTop;
            if (GUILayout.Toggle(_selectedClothTarget == ClothBindingTarget.Top, "Top", GUI.skin.button))
                _selectedClothTarget = ClothBindingTarget.Top;

            GUI.enabled = hasBottom;
            if (GUILayout.Toggle(_selectedClothTarget == ClothBindingTarget.Bottom, "Bottom", GUI.skin.button))
                _selectedClothTarget = ClothBindingTarget.Bottom;

            GUI.enabled = prevEnabled;
            GUILayout.EndHorizontal();

            GUILayout.Label($"Selected: {_selectedClothTarget}");

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
                return;
            }

            if (_selectedItemIndex < 0 || _selectedItemIndex >= _cachedOciItems.Count)
                _selectedItemIndex = 0;

            _itemScroll = GUILayout.BeginScrollView(_itemScroll, GUI.skin.box, GUILayout.Height(150));
            for (int i = 0; i < _cachedOciItems.Count; i++)
            {
                OCIItem item = _cachedOciItems[i];
                if (item == null)
                    continue;

                string label = ResolveItemDisplayName(item);
                if (GUILayout.Toggle(_selectedItemIndex == i, label, GUI.skin.button))
                {
                    if (_selectedItemIndex != i)
                        _selectedItemIndex = i;
                }
            }
            GUILayout.EndScrollView();

            OCIItem selectedItem = GetSelectedExternalItem();
            if (selectedItem == null)
                return;

            ExternalItemColliderBinding binding = FindExternalItemColliderBinding(physicCollider, selectedItem, _selectedClothTarget);
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

            GUILayout.Space(4f);
            GUILayout.Label("<color=orange>3) Collider Type</color>", RichLabel);
            GUILayout.BeginHorizontal();
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
            _pendingColliderCenter.x = SliderRow("Center X", _pendingColliderCenter.x, -2.0f, 2.0f);
            _pendingColliderCenter.y = SliderRow("Center Y", _pendingColliderCenter.y, -2.0f, 2.0f);
            _pendingColliderCenter.z = SliderRow("Center Z", _pendingColliderCenter.z, -2.0f, 2.0f);
            _pendingColliderRadius = SliderRow("Radius", _pendingColliderRadius, 0.05f, 1.5f);
            if (_pendingColliderType == ExternalColliderType.Capsule)
                _pendingColliderHeight = SliderRow("Height", _pendingColliderHeight, 0.1f, 3.0f);

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
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

            if (binding != null && GUILayout.Button("Remove"))
            {
                RemoveExternalColliderBinding(physicCollider, selectedItem, _selectedClothTarget);
            }
            GUILayout.EndHorizontal();
        }

        private OCIItem GetSelectedExternalItem()
        {
            if (_selectedItemIndex < 0 || _selectedItemIndex >= _cachedOciItems.Count)
                return null;

            return _cachedOciItems[_selectedItemIndex];
        }

        private static ExternalItemColliderBinding FindExternalItemColliderBinding(BinderState physicCollider, OCIItem ociItem, ClothBindingTarget clothTarget)
        {
            if (physicCollider == null || ociItem == null)
                return null;

            return physicCollider.externalItemColliderBindings
                .FirstOrDefault(v => v != null && v.ociItem == ociItem && v.clothTarget == clothTarget);
        }

        private void RefreshExternalItemList(BinderState physicCollider)
        {
            _cachedOciItems.Clear();
            _cachedOciItems.AddRange(GetAllOCIItemFromStudio() ?? new List<OCIItem>());

            if (_selectedItemIndex >= _cachedOciItems.Count)
                _selectedItemIndex = _cachedOciItems.Count > 0 ? 0 : -1;

            if (physicCollider == null)
                return;

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
            }

            RebuildCharacterClothColliderBindings(physicCollider);
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

            ExternalItemColliderBinding existing = FindExternalItemColliderBinding(physicCollider, ociItem, clothTarget);
            if (existing != null)
            {
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
            RebuildCharacterClothColliderBindings(physicCollider);
        }

        private void RemoveExternalColliderBinding(BinderState physicCollider, OCIItem ociItem, ClothBindingTarget clothTarget)
        {
            if (physicCollider == null || ociItem == null)
                return;

            ExternalItemColliderBinding existing = FindExternalItemColliderBinding(physicCollider, ociItem, clothTarget);
            if (existing == null)
                return;

            if (existing.collider != null)
                GameObject.Destroy(existing.collider);
            if (existing.colliderObject != null)
                GameObject.Destroy(existing.colliderObject);
            if (existing.visualObject != null)
                GameObject.Destroy(existing.visualObject);

            physicCollider.externalItemColliderBindings.Remove(existing);
            RebuildCharacterClothColliderBindings(physicCollider);
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
            return visual;
        }

        private static void UpdateExternalColliderVisual(ExternalItemColliderBinding binding)
        {
            if (binding == null || binding.visualObject == null || binding.collider == null)
                return;

            const float lineWidth = 0.008f;
            Color wireColor = Color.green;
            ClearExternalWireLines(binding.visualObject.transform);

            if (binding.colliderType == ExternalColliderType.Sphere)
            {
                SphereCollider sphere = binding.collider as SphereCollider;
                if (sphere == null)
                    return;

                AddWireCircle(binding.visualObject.transform, sphere.center, Quaternion.identity, sphere.radius, 40, lineWidth, wireColor, "Sphere_XZ");
                AddWireCircle(binding.visualObject.transform, sphere.center, Quaternion.Euler(90f, 0f, 0f), sphere.radius, 40, lineWidth, wireColor, "Sphere_XY");
                AddWireCircle(binding.visualObject.transform, sphere.center, Quaternion.Euler(0f, 0f, 90f), sphere.radius, 40, lineWidth, wireColor, "Sphere_YZ");
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

                AddWireCircle(binding.visualObject.transform, topCenter, Quaternion.identity, radius, 32, lineWidth, wireColor, "Capsule_Top");
                AddWireCircle(binding.visualObject.transform, bottomCenter, Quaternion.identity, radius, 32, lineWidth, wireColor, "Capsule_Bottom");
                AddCapsuleProfile(binding.visualObject.transform, center, radius, cylHalf, Vector3.right, 20, lineWidth, wireColor, "Capsule_ProfileX");
                AddCapsuleProfile(binding.visualObject.transform, center, radius, cylHalf, Vector3.forward, 20, lineWidth, wireColor, "Capsule_ProfileZ");
            }
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
            if (shader != null)
                lr.material = new Material(shader);
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

            List<Cloth> topCloths = GetClothsByTarget(physicCollider, ClothBindingTarget.Top);
            List<Cloth> bottomCloths = GetClothsByTarget(physicCollider, ClothBindingTarget.Bottom);
            var topSet = new HashSet<Cloth>(topCloths);
            var bottomSet = new HashSet<Cloth>(bottomCloths);

            List<CapsuleCollider> topCapsules = physicCollider.externalItemColliderBindings
                .Where(v => v != null && v.collider != null && v.clothTarget == ClothBindingTarget.Top && v.colliderType == ExternalColliderType.Capsule)
                .Select(v => v.collider as CapsuleCollider)
                .Where(v => v != null)
                .ToList();

            List<CapsuleCollider> bottomCapsules = physicCollider.externalItemColliderBindings
                .Where(v => v != null && v.collider != null && v.clothTarget == ClothBindingTarget.Bottom && v.colliderType == ExternalColliderType.Capsule)
                .Select(v => v.collider as CapsuleCollider)
                .Where(v => v != null)
                .ToList();

            List<SphereCollider> topSpheres = physicCollider.externalItemColliderBindings
                .Where(v => v != null && v.collider != null && v.clothTarget == ClothBindingTarget.Top && v.colliderType == ExternalColliderType.Sphere)
                .Select(v => v.collider as SphereCollider)
                .Where(v => v != null)
                .ToList();

            List<SphereCollider> bottomSpheres = physicCollider.externalItemColliderBindings
                .Where(v => v != null && v.collider != null && v.clothTarget == ClothBindingTarget.Bottom && v.colliderType == ExternalColliderType.Sphere)
                .Select(v => v.collider as SphereCollider)
                .Where(v => v != null)
                .ToList();

            foreach (Cloth cloth in allCloths)
            {
                if (cloth == null)
                    continue;

                List<CapsuleCollider> capsules = (cloth.capsuleColliders ?? Array.Empty<CapsuleCollider>())
                    .Where(v => v != null && !v.name.StartsWith(EXTERNAL_COLLIDER_OBJECT_PREFIX))
                    .ToList();

                IEnumerable<CapsuleCollider> addCapsules = Enumerable.Empty<CapsuleCollider>();
                if (topSet.Contains(cloth))
                    addCapsules = addCapsules.Concat(topCapsules);
                if (bottomSet.Contains(cloth))
                    addCapsules = addCapsules.Concat(bottomCapsules);

                capsules.AddRange(addCapsules.Where(v => v != null && !capsules.Contains(v)));
                cloth.capsuleColliders = capsules.ToArray();

                List<ClothSphereColliderPair> spherePairs = (cloth.sphereColliders ?? Array.Empty<ClothSphereColliderPair>())
                    .Where(v =>
                        (v.first == null || !v.first.name.StartsWith(EXTERNAL_COLLIDER_OBJECT_PREFIX)) &&
                        (v.second == null || !v.second.name.StartsWith(EXTERNAL_COLLIDER_OBJECT_PREFIX)))
                    .ToList();

                IEnumerable<SphereCollider> addSpheres = Enumerable.Empty<SphereCollider>();
                if (topSet.Contains(cloth))
                    addSpheres = addSpheres.Concat(topSpheres);
                if (bottomSet.Contains(cloth))
                    addSpheres = addSpheres.Concat(bottomSpheres);

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

                cloth.sphereColliders = spherePairs.ToArray();
            }
        }

        private static List<Cloth> GetClothsByTarget(BinderState physicCollider, ClothBindingTarget clothTarget)
        {
            if (physicCollider == null || physicCollider.clothInfos == null)
                return new List<Cloth>();

            int clothInfoIndex = clothTarget == ClothBindingTarget.Top ? 0 : 1;
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

            return ociItem.itemInfo.objectInfo.name;// ociItem.treeNodeObject.textName;
        }

        private void SceneInit()
        {
        }

#if FEATURE_SCENE_SAVE
        private void OnSceneLoad(string path)
        {
            SceneInit();
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
            PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
            if (data == null)
                return;
            XmlDocument doc = new XmlDocument();
            doc.LoadXml((string)data.data["sceneInfo"]);
            XmlNode node = doc.FirstChild;
            if (node == null)
                return;
            SceneImport(path, node);
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
            //     if (node == null)
            //         return;
            //     this.ExecuteDelayed2(() =>
            //     {
            //         List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();

            //         List<OCIChar> ociChars = dic
            //             .Select(kv => kv.Value as OCIChar)   // ObjectCtrlInfo를 OCIChar로 캐스팅
            //             .Where(c => c != null)               // null 제거 (OCIChar가 아닌 경우 스킵)
            //             .ToList();

            //         SceneRead(node, ociChars);

            //         // delete collider treeNodeObjects
            //         List<TreeNodeObject> deleteTargets = new List<TreeNodeObject>();
            //         foreach (TreeNodeObject treeNode in Singleton<Studio.Studio>.Instance.treeNodeCtrl.m_TreeNodeObject)
            //         {
            //             if (treeNode.m_TextName.text == GROUP_CAPSULE_COLLIDER || treeNode.m_TextName.text == GROUP_SPHERE_COLLIDER)
            //             {
            //                 treeNode.enableDelete = true;
            //                 deleteTargets.Add(treeNode);
            //             }
            //         }

            //         foreach (TreeNodeObject target  in deleteTargets)
            //         {
            //             Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNode(target);
            //         }
            //     }, 20);
        }

        private void SceneImport(string path, XmlNode node)
        {
            Dictionary<int, ObjectCtrlInfo> toIgnore = new Dictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
            this.ExecuteDelayed2(() =>
            {
                List<KeyValuePair<int, ObjectCtrlInfo>> dic = Studio.Studio.Instance.dicObjectCtrl.Where(e => toIgnore.ContainsKey(e.Key) == false).OrderBy(e => SceneInfo_Import_Patches._newToOldKeys[e.Key]).ToList();

                List<OCIChar> ociChars = dic
                    .Select(kv => kv.Value as OCIChar)   // ObjectCtrlInfo를 OCIChar로 캐스팅
                    .Where(c => c != null)               // null 제거 (OCIChar가 아닌 경우 스킵)
                    .ToList();

                SceneRead(node, ociChars);
            }, 20);
        }

        private void SceneRead(XmlNode node, List<OCIChar> ociChars)
        {            
            foreach (XmlNode charNode in node.SelectNodes("character"))
            {
                string charName = charNode.Attributes["name"]?.Value;
                if (string.IsNullOrEmpty(charName)) continue;

                // 이름으로 ociChar 찾기
                OCIChar ociChar = ociChars.FirstOrDefault(c => c.charInfo.name == charName);
                if (ociChar == null)
                {
                    // UnityEngine.Debug.LogWarning($">> SceneRead: Character '{charName}' not found!");
                    continue;
                }


                // bone 목록 순회
                foreach (XmlNode boneNode in charNode.SelectNodes("bone"))
                {
                    string colliderName = boneNode.Attributes["name"]?.Value;
                    if (string.IsNullOrEmpty(colliderName)) continue;

                    Transform bone = FindColliderByName(ociChar, colliderName);
                    if (bone == null)
                    {
                        // UnityEngine.Debug.LogWarning($">> SceneRead: collider '{colliderName}' not found in {charName}");
                        continue;
                    }

                    // position
                    XmlNode posNode = boneNode.SelectSingleNode("position");
                    if (posNode != null)
                    {
                        bone.localPosition = new Vector3(
                            ParseFloat(posNode.Attributes["x"]?.Value),
                            ParseFloat(posNode.Attributes["y"]?.Value),
                            ParseFloat(posNode.Attributes["z"]?.Value)
                        );
                    }

                    // rotation
                    XmlNode rotNode = boneNode.SelectSingleNode("rotation");
                    if (rotNode != null)
                    {
                        bone.localEulerAngles = new Vector3(
                            ParseFloat(rotNode.Attributes["x"]?.Value),
                            ParseFloat(rotNode.Attributes["y"]?.Value),
                            ParseFloat(rotNode.Attributes["z"]?.Value)
                        );
                    }

                    // scale
                    XmlNode scaleNode = boneNode.SelectSingleNode("scale");
                    if (scaleNode != null)
                    {
                        bone.localScale = new Vector3(
                            ParseFloat(scaleNode.Attributes["x"]?.Value),
                            ParseFloat(scaleNode.Attributes["y"]?.Value),
                            ParseFloat(scaleNode.Attributes["z"]?.Value)
                        );
                    }
                }
            }
        }

        private Transform FindColliderByName(OCIChar ociChar, string colliderName)
        {
            var capColliders = ociChar.charInfo.objBodyBone
                .transform
                .GetComponentsInChildren<CapsuleCollider>();

            foreach (CapsuleCollider capCollider in capColliders) {
                if (capCollider != null) {
                    string collider_name = "";
                    int idx = capCollider.name.IndexOf('-');
                    if (idx >= 0)
                        collider_name = capCollider.name.Substring(0, idx);
                    else
                        collider_name = capCollider.name;

                    if (colliderName == collider_name)
                        return capCollider.transform;
                }
            }

            var sphereColliders = ociChar.charInfo.objBodyBone
                .transform
                .GetComponentsInChildren<SphereCollider>();            

            foreach (SphereCollider sphereCollider in sphereColliders) {
                if (sphereCollider != null) {
                    string collider_name = "";
                    int idx = sphereCollider.name.IndexOf('-');
                    if (idx >= 0)
                        collider_name = sphereCollider.name.Substring(0, idx);
                    else
                        collider_name = sphereCollider.name;

                    if (colliderName == collider_name)
                        return sphereCollider.transform;
                }
            }

            return null;
        }

        // 유틸: 문자열 float 파싱
        private float ParseFloat(string value)
        {
            if (float.TryParse(value, out float result))
                return result;
            return 0f;
        }

        private void SceneWrite(string path, XmlTextWriter writer)
        {
            // List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();

            // List<OCIChar> ociChars = dic
            //     .Select(kv => kv.Value as OCIChar)   // ObjectCtrlInfo를 OCIChar로 캐스팅
            //     .Where(c => c != null)               // null 제거 (OCIChar가 아닌 경우 스킵)
            //     .ToList();

            // foreach (OCIChar ociChar in ociChars)
            // {
            //     writer.WriteStartElement("character");
            //     writer.WriteAttributeString("name", "" + ociChar.charInfo.name);

            //     List<SphereCollider> scolliders = ociChar.charInfo.objBodyBone
            //         .transform
            //         .GetComponentsInChildren<SphereCollider>()
            //         .OrderBy(col => col.gameObject.name) // 이름 기준 정렬
            //         .ToList();

            //     List<Transform> targetCollider = new List<Transform>();

            //     foreach (var col in scolliders)
            //     {
            //         if (col == null) continue; // Destroy된 경우 스킵

            //         if (col.gameObject.name.Contains("Cloth colliders"))
            //         {
            //             targetCollider.Add(col.gameObject.transform);
            //         }
            //     }

            //     // capsule collider
            //     List<CapsuleCollider> ccolliders = ociChar.charInfo.objBodyBone
            //         .transform
            //         .GetComponentsInChildren<CapsuleCollider>(true)
            //         .OrderBy(col => col.gameObject.name) // 이름 기준 정렬
            //         .ToList();

            //     foreach (var col in ccolliders)
            //     {

            //         if (col == null) continue; // Destroy된 경우 스킵

            //         if (col.gameObject.name.Contains("Cloth colliders"))
            //         {
            //             targetCollider.Add(col.gameObject.transform);
            //         }
            //     }

            //     string collider_name = "";
            //     foreach (Transform collider in targetCollider)
            //     {
            //         int idx = collider.name.IndexOf('-');
            //         if (idx >= 0)
            //             collider_name = collider.name.Substring(0, idx);
            //         else
            //             collider_name = collider.name;

            //         writer.WriteStartElement("bone");
            //         writer.WriteAttributeString("name", collider_name);

            //         // position
            //         writer.WriteStartElement("position");
            //         writer.WriteAttributeString("x", collider.localPosition.x.ToString());
            //         writer.WriteAttributeString("y", collider.localPosition.y.ToString());
            //         writer.WriteAttributeString("z", collider.localPosition.z.ToString());
            //         writer.WriteEndElement();

            //         // rotation
            //         writer.WriteStartElement("rotation");
            //         writer.WriteAttributeString("x", collider.localEulerAngles.x.ToString());
            //         writer.WriteAttributeString("y", collider.localEulerAngles.y.ToString());
            //         writer.WriteAttributeString("z", collider.localEulerAngles.z.ToString());
            //         writer.WriteEndElement();

            //         // scale
            //         writer.WriteStartElement("scale");
            //         writer.WriteAttributeString("x", collider.localScale.x.ToString());
            //         writer.WriteAttributeString("y", collider.localScale.y.ToString());
            //         writer.WriteAttributeString("z", collider.localScale.z.ToString());
            //         writer.WriteEndElement();

            //         writer.WriteEndElement(); // collider
            //     }

            //     writer.WriteEndElement(); // character
            // }             
        }
#endif
        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            UIUtility.Init();
            _loaded = true;
        }

        private static OCIChar GetLastOCICharFromStudio()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null)
                return null;

            TreeNodeObject node  = Studio.Studio.Instance.treeNodeCtrl.selectNodes
                .LastOrDefault();

            return  node != null ? Studio.Studio.GetCtrlInfo(node) as OCIChar : null;
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

        #endregion

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
            for (int i = 0; i < state.clothInfos.Length; i++)
            {
                GameObject clothObj = (clothes != null && i < clothes.Length) ? clothes[i] : null;
                state.clothInfos[i].clothObj = clothObj;
                state.clothInfos[i].hasCloth = clothObj != null && clothObj.GetComponentsInChildren<Cloth>(true).Length > 0;
            }

            RebuildCharacterClothColliderBindings(state);
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

        #region Patches
        [HarmonyPatch(typeof(SceneInfo), "Import", new[] { typeof(BinaryReader), typeof(global::System.Version) })]
        private static class SceneInfo_Import_Patches //This is here because I fucked up the save format making it impossible to import scenes correctly
        {
            internal static readonly Dictionary<int, int> _newToOldKeys = new Dictionary<int, int>();

            private static void Prefix()
            {
                _newToOldKeys.Clear();
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









