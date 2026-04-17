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
    - 활성화된 캐릭터가 착용한 Cloth 컴포넌트의 각 bone 정보를 시각화하고 position, scale 처리를 제공

    용어
    - OCIChar: 캐릭터 
        > GetCurrentOCI 함수를 통해 현재 씬내 활성화된 캐리터를 획득

    요구 기능
        1) onGUI 내에 아래 UI를 구성해야 한다.
            1.1) 현재 씬내 현재 캐릭터 대상 cloth 컴포넌트 에 매핑된 collide 정보 조회
            1.2) 조회된 collide 클릭 시  정보 시각화 (녹색실선)
            1.3) 시각화된 collide 는 position, scale 정보를 onGUI 에서 editing 되도록 제공
            1.4) 시각화된 collide의 position, scale 정보는 각 캐릭터 정보마다 보유해야 하며, ClothCollideVisualController.cs 내 PhysicCollider class 에서 관리되어야 한다.        
        2) sceneWrite, sceneRead가 가능한데, 현재 씬을 저장 후 다시 복원하는 기능이다.
            >  캐릭터 별 PhysicCollider 는 GetCurrentData() 를 통해, 현재 활성화된 캐릭터 데이터를 획득할 수 있다.
            2.1) sceneWrite 시 씬내 각 캐릭터의 각 PhysicCollider 가 보유한 collide 이름과 collide 의 속성(position, scale) 정보를 xml에 저장한다.
            2.2) sceneRead는 시 씬내 각 캐릭터의 각 PhysicCollider 에 1.5.1에서 저장한 xml 정보를 다시 PhysicCollider 로 업데이트 해야 한다.

    추가 요구 기능:
        - 해당 옷에 cloth 컴포넌트가 없으면 'No Physics Cloth found' 라고 출력되어야 함

    현 버전 문제점:
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
    public class ClothCollideVisualizer : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "ClothCollideVisualizer";
        public const string Version = "0.9.2.0";
        public const string GUID = "com.alton.illusionplugins.clothcollidevisualizer";
        internal const string _ownerId = "alton";
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
                data.version = ClothCollideVisualizer._saveVersion;
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

                // delete collider treeNodeObjects
                List<TreeNodeObject> deleteTargets = new List<TreeNodeObject>();
                foreach (TreeNodeObject treeNode in Singleton<Studio.Studio>.Instance.treeNodeCtrl.m_TreeNodeObject)
                {
                    if (treeNode.m_TextName.text == GROUP_CAPSULE_COLLIDER || treeNode.m_TextName.text == GROUP_SPHERE_COLLIDER)
                    {
                        treeNode.enableDelete = true;
                        deleteTargets.Add(treeNode);
                    }
                }

                foreach (TreeNodeObject target  in deleteTargets)
                {
                    Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNode(target);
                }
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

                string charName = oci.charInfo.name;
                if (string.IsNullOrEmpty(charName))
                    continue;

                if (!ociCharByName.ContainsKey(charName))
                    ociCharByName.Add(charName, oci);
            }

            foreach (XmlNode charNode in node.SelectNodes("character"))
            {
                OCIChar ociChar = null;

                string dicKeyText = charNode.Attributes["dicKey"]?.Value;
                if (!string.IsNullOrEmpty(dicKeyText) && int.TryParse(dicKeyText, out int dicKeyValue))
                    ociChar = FindOciCharByDicKey(ociCharByDicKey, dicKeyValue);

                if (ociChar == null)
                {
                    string hash = charNode.Attributes["hash"]?.Value;
                    if (!string.IsNullOrEmpty(hash) && int.TryParse(hash, out int hashValue))
                        ociCharByHash.TryGetValue(hashValue, out ociChar);
                }

                if (ociChar == null)
                {
                    string charName = charNode.Attributes["name"]?.Value;
                    if (!string.IsNullOrEmpty(charName))
                        ociCharByName.TryGetValue(charName, out ociChar);
                }

                if (ociChar == null)
                {
                    continue;
                }
    
                ClothCollideVisualizerController controller = GetControl(ociChar);
                if (controller == null)
                    continue;

                PhysicCollider physicCollider = controller.GetData() ?? controller.CreateData(ociChar);
                if (physicCollider == null)
                    continue;

                foreach (XmlNode boneNode in charNode.SelectNodes("bone"))
                {
                    string colliderName = boneNode.Attributes["name"]?.Value;
                    if (string.IsNullOrEmpty(colliderName)) continue;

                    ColliderTransformInfo transformInfo = new ColliderTransformInfo();
                    string slotText = boneNode.Attributes["slot"]?.Value;
                    string legacyClothKey = boneNode.Attributes["clothKey"]?.Value;
                    int slot = SlotTop;
                    bool hasSlot = !string.IsNullOrEmpty(slotText) && int.TryParse(slotText, out slot);

                    // position
                    XmlNode posNode = boneNode.SelectSingleNode("position");
                    if (posNode != null)
                    {
                        // legacy: position은 Transform.localPosition
                        transformInfo.localPosition = new Vector3(
                            ParseFloat(posNode.Attributes["x"]?.Value),
                            ParseFloat(posNode.Attributes["y"]?.Value),
                            ParseFloat(posNode.Attributes["z"]?.Value)
                        );
                        transformInfo.hasLocalPosition = true;
                    }

                    // center (Collider.center)
                    XmlNode centerNode = boneNode.SelectSingleNode("center");
                    if (centerNode != null)
                    {
                        transformInfo.colliderCenter = new Vector3(
                            ParseFloat(centerNode.Attributes["x"]?.Value),
                            ParseFloat(centerNode.Attributes["y"]?.Value),
                            ParseFloat(centerNode.Attributes["z"]?.Value)
                        );
                        transformInfo.hasColliderCenter = true;
                    }

                    // rotation
                    XmlNode rotNode = boneNode.SelectSingleNode("rotation");
                    if (rotNode != null)
                    {
                        transformInfo.localEulerAngles = new Vector3(
                            ParseFloat(rotNode.Attributes["x"]?.Value),
                            ParseFloat(rotNode.Attributes["y"]?.Value),
                            ParseFloat(rotNode.Attributes["z"]?.Value)
                        );
                        transformInfo.hasLocalEulerAngles = true;
                    }

                    // scale
                    XmlNode scaleNode = boneNode.SelectSingleNode("scale");
                    if (scaleNode != null)
                    {
                        transformInfo.localScale = new Vector3(
                            ParseFloat(scaleNode.Attributes["x"]?.Value),
                            ParseFloat(scaleNode.Attributes["y"]?.Value),
                            ParseFloat(scaleNode.Attributes["z"]?.Value)
                        );
                        transformInfo.hasLocalScale = true;
                    }

                    // slot 우선, 없으면 legacy clothKey("top:...","bottom:...")를 보고 판단한다.
                    if (!hasSlot && !string.IsNullOrEmpty(legacyClothKey))
                    {
                        if (legacyClothKey.StartsWith("bottom:", StringComparison.OrdinalIgnoreCase))
                            slot = SlotBottom;
                        else if (legacyClothKey.StartsWith("top:", StringComparison.OrdinalIgnoreCase))
                            slot = SlotTop;
                        else
                            slot = SlotTop; // legacy global은 top에만 넣고, 아래에서 bottom에 복사한다.
                        hasSlot = true;
                    }

                    if (!hasSlot && string.IsNullOrEmpty(legacyClothKey))
                    {
                        // 아주 오래된 데이터(구분자 없음)는 top/bottom 둘 다 적용한다.
                        physicCollider.topColliderTransformInfos[colliderName] = transformInfo;
                        physicCollider.bottomColliderTransformInfos[colliderName] = transformInfo;
                    }
                    else if (slot == SlotBottom)
                    {
                        physicCollider.bottomColliderTransformInfos[colliderName] = transformInfo;
                    }
                    else
                    {
                        physicCollider.topColliderTransformInfos[colliderName] = transformInfo;
                        if (!string.IsNullOrEmpty(legacyClothKey) && legacyClothKey.Equals("global", StringComparison.OrdinalIgnoreCase))
                            physicCollider.bottomColliderTransformInfos[colliderName] = transformInfo;
                    }
                }

                ApplySavedTransformsForSlots(ociChar.GetChaControl(), physicCollider);

                if (physicCollider.visualColliderAdded)
                {
                    ForceRefreshVisualColliders(ociChar);
                }
            }
        }

        private static string GetColliderKey(Collider collider)
        {
            if (collider == null)
                return string.Empty;

            string sourceName = collider.name ?? string.Empty;
            int idx = sourceName.IndexOf('-');
            return idx >= 0 ? sourceName.Substring(0, idx) : sourceName;
        }

        private static IEnumerable<Collider> GetCollidersUsedByCloth(GameObject clothObj)
        {
            if (clothObj == null)
                return Enumerable.Empty<Collider>();

            Cloth[] clothes = clothObj.GetComponentsInChildren<Cloth>(true);
            if (clothes == null || clothes.Length == 0)
                return Enumerable.Empty<Collider>();

            HashSet<Collider> result = new HashSet<Collider>();
            foreach (Cloth cloth in clothes)
            {
                if (cloth == null)
                    continue;

                CapsuleCollider[] capsules = cloth.capsuleColliders ?? Array.Empty<CapsuleCollider>();
                foreach (CapsuleCollider c in capsules)
                {
                    if (c != null)
                        result.Add(c);
                }

                ClothSphereColliderPair[] spherePairs = cloth.sphereColliders ?? Array.Empty<ClothSphereColliderPair>();
                foreach (ClothSphereColliderPair pair in spherePairs)
                {
                    if (pair.first != null)
                        result.Add(pair.first);
                    if (pair.second != null)
                        result.Add(pair.second);
                }
            }

            return result;
        }

        private static bool HasPhysicsCloth(GameObject clothObj)
        {
            return clothObj != null && clothObj.GetComponentsInChildren<Cloth>(true).Length > 0;
        }

        private static bool TryGetColliderCenter(Collider collider, out Vector3 center)
        {
            center = Vector3.zero;
            if (collider == null)
                return false;

            if (collider is SphereCollider sphere)
            {
                center = sphere.center;
                return true;
            }
            if (collider is CapsuleCollider capsule)
            {
                center = capsule.center;
                return true;
            }
            if (collider is BoxCollider box)
            {
                center = box.center;
                return true;
            }

            return false;
        }

        private static Vector3 GetColliderCenterOrDefault(Collider collider)
        {
            return TryGetColliderCenter(collider, out Vector3 center) ? center : Vector3.zero;
        }

        private static bool TrySetColliderCenter(Collider collider, Vector3 center)
        {
            if (collider == null)
                return false;

            if (collider is SphereCollider sphere)
            {
                sphere.center = center;
                return true;
            }
            if (collider is CapsuleCollider capsule)
            {
                capsule.center = center;
                return true;
            }
            if (collider is BoxCollider box)
            {
                box.center = center;
                return true;
            }

            return false;
        }

        private static void EnsureDefaultColliderBaseline(Collider collider, PhysicCollider physicCollider)
        {
            if (collider == null || physicCollider == null)
                return;

            if (physicCollider.colliderDefaultTransforms == null)
                physicCollider.colliderDefaultTransforms = new Dictionary<int, ColliderTransformInfo>();

            int id = collider.GetInstanceID();
            if (physicCollider.colliderDefaultTransforms.ContainsKey(id))
                return;

            Transform tr = collider.transform;
            physicCollider.colliderDefaultTransforms[id] = new ColliderTransformInfo
            {
                localPosition = tr.localPosition,
                localEulerAngles = tr.localEulerAngles,
                localScale = tr.localScale,
                colliderCenter = GetColliderCenterOrDefault(collider),
                hasLocalPosition = true,
                hasLocalEulerAngles = true,
                hasLocalScale = true,
                hasColliderCenter = true
            };
        }

        private static void ResetSlotCollidersToDefault(ChaControl chaCtrl, PhysicCollider physicCollider, int slot)
        {
            if (chaCtrl == null || physicCollider == null)
                return;

            GameObject clothObj = (chaCtrl.objClothes != null && slot >= 0 && slot < chaCtrl.objClothes.Length) ? chaCtrl.objClothes[slot] : null;
            foreach (Collider collider in GetCollidersUsedByCloth(clothObj))
            {
                if (collider == null)
                    continue;

                EnsureDefaultColliderBaseline(collider, physicCollider);

                int id = collider.GetInstanceID();
                if (physicCollider.colliderDefaultTransforms != null
                    && physicCollider.colliderDefaultTransforms.TryGetValue(id, out ColliderTransformInfo baseline)
                    && baseline != null)
                {
                    Transform tr = collider.transform;
                    if (baseline.hasLocalPosition) tr.localPosition = baseline.localPosition;
                    if (baseline.hasLocalEulerAngles) tr.localEulerAngles = baseline.localEulerAngles;
                    if (baseline.hasLocalScale) tr.localScale = baseline.localScale;
                    if (baseline.hasColliderCenter) TrySetColliderCenter(collider, baseline.colliderCenter);
                }
            }
        }

        private static void RebuildColliderSlotCache(ChaControl chaCtrl, PhysicCollider physicCollider)
        {
            if (chaCtrl == null || physicCollider == null)
                return;

            if (physicCollider.colliderInstanceIdToSlot == null)
                physicCollider.colliderInstanceIdToSlot = new Dictionary<int, int>();
            physicCollider.colliderInstanceIdToSlot.Clear();

            GameObject topObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 0) ? chaCtrl.objClothes[0] : null;
            GameObject bottomObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 1) ? chaCtrl.objClothes[1] : null;

            foreach (Collider collider in GetCollidersUsedByCloth(topObj))
            {
                if (collider == null)
                    continue;
                int id = collider.GetInstanceID();
                if (!physicCollider.colliderInstanceIdToSlot.ContainsKey(id))
                    physicCollider.colliderInstanceIdToSlot[id] = SlotTop;
            }

            foreach (Collider collider in GetCollidersUsedByCloth(bottomObj))
            {
                if (collider == null)
                    continue;
                int id = collider.GetInstanceID();
                if (!physicCollider.colliderInstanceIdToSlot.ContainsKey(id))
                    physicCollider.colliderInstanceIdToSlot[id] = SlotBottom;
            }
        }

        private static void ApplySavedTransformsForSlots(ChaControl chaCtrl, PhysicCollider physicCollider)
        {
            if (chaCtrl == null || physicCollider == null)
                return;

            GameObject topObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 0) ? chaCtrl.objClothes[0] : null;
            GameObject bottomObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 1) ? chaCtrl.objClothes[1] : null;

            // 저장값이 있는 경우에만 적용한다. 없으면 생성 직후 기본값을 유지한다.
            foreach (Collider collider in GetCollidersUsedByCloth(topObj))
            {
                if (collider == null)
                    continue;

                EnsureDefaultColliderBaseline(collider, physicCollider);

                string colliderKey = GetColliderKey(collider);
                if (string.IsNullOrEmpty(colliderKey))
                    continue;

                if (physicCollider.topColliderTransformInfos != null
                    && physicCollider.topColliderTransformInfos.TryGetValue(colliderKey, out ColliderTransformInfo info)
                    && info != null)
                {
                    Transform tr = collider.transform;
                    if (info.hasLocalPosition) tr.localPosition = info.localPosition;
                    if (info.hasLocalEulerAngles) tr.localEulerAngles = info.localEulerAngles;
                    if (info.hasLocalScale) tr.localScale = info.localScale;
                    if (info.hasColliderCenter) TrySetColliderCenter(collider, info.colliderCenter);
                }
            }

            foreach (Collider collider in GetCollidersUsedByCloth(bottomObj))
            {
                if (collider == null)
                    continue;

                EnsureDefaultColliderBaseline(collider, physicCollider);

                string colliderKey = GetColliderKey(collider);
                if (string.IsNullOrEmpty(colliderKey))
                    continue;

                if (physicCollider.bottomColliderTransformInfos != null
                    && physicCollider.bottomColliderTransformInfos.TryGetValue(colliderKey, out ColliderTransformInfo info)
                    && info != null)
                {
                    Transform tr = collider.transform;
                    if (info.hasLocalPosition) tr.localPosition = info.localPosition;
                    if (info.hasLocalEulerAngles) tr.localEulerAngles = info.localEulerAngles;
                    if (info.hasLocalScale) tr.localScale = info.localScale;
                    if (info.hasColliderCenter) TrySetColliderCenter(collider, info.colliderCenter);
                }
            }
        }

        private static void SaveCurrentTransformsForSlots(ChaControl chaCtrl, PhysicCollider physicCollider)
        {
            if (chaCtrl == null || physicCollider == null)
                return;

            GameObject topObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 0) ? chaCtrl.objClothes[0] : null;
            GameObject bottomObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 1) ? chaCtrl.objClothes[1] : null;

            foreach (Collider collider in GetCollidersUsedByCloth(topObj))
            {
                if (collider == null)
                    continue;

                EnsureDefaultColliderBaseline(collider, physicCollider);

                string colliderKey = GetColliderKey(collider);
                if (string.IsNullOrEmpty(colliderKey))
                    continue;

                Transform tr = collider.transform;
                physicCollider.topColliderTransformInfos[colliderKey] = new ColliderTransformInfo
                {
                    localPosition = tr.localPosition,
                    localEulerAngles = tr.localEulerAngles,
                    localScale = tr.localScale,
                    colliderCenter = GetColliderCenterOrDefault(collider),
                    hasLocalPosition = true,
                    hasLocalEulerAngles = true,
                    hasLocalScale = true,
                    hasColliderCenter = true
                };
            }

            foreach (Collider collider in GetCollidersUsedByCloth(bottomObj))
            {
                if (collider == null)
                    continue;

                EnsureDefaultColliderBaseline(collider, physicCollider);

                string colliderKey = GetColliderKey(collider);
                if (string.IsNullOrEmpty(colliderKey))
                    continue;

                Transform tr = collider.transform;
                physicCollider.bottomColliderTransformInfos[colliderKey] = new ColliderTransformInfo
                {
                    localPosition = tr.localPosition,
                    localEulerAngles = tr.localEulerAngles,
                    localScale = tr.localScale,
                    colliderCenter = GetColliderCenterOrDefault(collider),
                    hasLocalPosition = true,
                    hasLocalEulerAngles = true,
                    hasLocalScale = true,
                    hasColliderCenter = true
                };
            }
        }

        private static bool TryGetSlotFromChangeClothesKind(int kind, out int slot)
        {
            // chaCtrl.objClothes 인덱스(0=top, 1=bottom) 기준으로만 처리한다.
            // 다른 의상 종류(브라/팬티 등)는 이 플러그인(상의/하의) 범위를 벗어나므로 무시한다.
            slot = SlotTop;
            if (kind == SlotTop)
            {
                slot = SlotTop;
                return true;
            }
            if (kind == SlotBottom)
            {
                slot = SlotBottom;
                return true;
            }
            return false;
        }

        // 유틸: 문자열을 float 값으로 파싱한다.
        private float ParseFloat(string value)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
            return 0f;
        }

        private void SceneWrite(string path, XmlTextWriter writer)
        {
            List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();

            List<OCIChar> ociChars = dic
                .Select(kv => kv.Value as OCIChar)   // ObjectCtrlInfo를 OCIChar로 캐스팅
                .Where(c => c != null)               // null 항목 제거
                .ToList();

            foreach (OCIChar ociChar in ociChars)
            {
                int dicKey;
                bool hasDicKey = TryGetDicKey(ociChar.GetChaControl(), out dicKey);
                ClothCollideVisualizerController controller = GetControl(ociChar);
                PhysicCollider physicCollider = controller != null ? (controller.GetData() ?? controller.CreateData(ociChar)) : null;
                if (physicCollider != null)
                    SaveCurrentTransformsForSlots(ociChar.GetChaControl(), physicCollider);

                writer.WriteStartElement("character");
                if (hasDicKey)
                    writer.WriteAttributeString("dicKey", dicKey.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("hash", ociChar.GetChaControl().GetHashCode().ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("name", ociChar.charInfo != null ? ociChar.charInfo.name : string.Empty);

                foreach (var kv in (physicCollider != null ? physicCollider.topColliderTransformInfos : null)
                             ?? new Dictionary<string, ColliderTransformInfo>())
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
                        continue;

                    writer.WriteStartElement("bone");
                    writer.WriteAttributeString("name", kv.Key);
                    writer.WriteAttributeString("slot", SlotTop.ToString(CultureInfo.InvariantCulture));

                    if (kv.Value.hasLocalPosition)
                    {
                        writer.WriteStartElement("position");
                        writer.WriteAttributeString("x", kv.Value.localPosition.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localPosition.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localPosition.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasColliderCenter)
                    {
                        writer.WriteStartElement("center");
                        writer.WriteAttributeString("x", kv.Value.colliderCenter.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.colliderCenter.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.colliderCenter.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasLocalEulerAngles)
                    {
                        writer.WriteStartElement("rotation");
                        writer.WriteAttributeString("x", kv.Value.localEulerAngles.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localEulerAngles.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localEulerAngles.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasLocalScale)
                    {
                        writer.WriteStartElement("scale");
                        writer.WriteAttributeString("x", kv.Value.localScale.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localScale.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localScale.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }

                foreach (var kv in (physicCollider != null ? physicCollider.bottomColliderTransformInfos : null)
                             ?? new Dictionary<string, ColliderTransformInfo>())
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
                        continue;

                    writer.WriteStartElement("bone");
                    writer.WriteAttributeString("name", kv.Key);
                    writer.WriteAttributeString("slot", SlotBottom.ToString(CultureInfo.InvariantCulture));

                    if (kv.Value.hasLocalPosition)
                    {
                        writer.WriteStartElement("position");
                        writer.WriteAttributeString("x", kv.Value.localPosition.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localPosition.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localPosition.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasColliderCenter)
                    {
                        writer.WriteStartElement("center");
                        writer.WriteAttributeString("x", kv.Value.colliderCenter.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.colliderCenter.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.colliderCenter.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasLocalEulerAngles)
                    {
                        writer.WriteStartElement("rotation");
                        writer.WriteAttributeString("x", kv.Value.localEulerAngles.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localEulerAngles.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localEulerAngles.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasLocalScale)
                    {
                        writer.WriteStartElement("scale");
                        writer.WriteAttributeString("x", kv.Value.localScale.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localScale.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localScale.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement(); // character
            }             
        }

        private bool TryGetDicKey(ChaControl chaCtrl, out int dicKey)
        {
            dicKey = 0;
            if (chaCtrl == null)
                return false;

            foreach (var kvp in Studio.Studio.Instance.dicObjectCtrl)
            {
                var oci = kvp.Value as OCIChar;
                if (oci == null)
                    continue;

                if (oci.GetChaControl() == chaCtrl)
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

                _windowRect = GUILayout.Window(_uniqueId + 1, _windowRect, WindowFunc, "ClothCollideVisualizer " + Version);
            }                
        }


        // 현재 스튜디오에서 선택된 OCIChar를 가져온다.
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


        // 플러그인 메인 윈도우 UI를 그린다.
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
                    // Modified는 예외 없이 항상 빨간색(Adjustable 포함)
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

            // 핵심: 새로 갈아입은 의상의 Cloth 배열에 Adjustable collider를 다시 매핑한다.
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
                // 현재 collider가 top/bottom 중 어디에 속하는지(현재 옷 기준) 판단해서 해당 옷 키에 저장한다.
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

            // Position은 Collider.center(Offset) 기준으로만 판단한다.
            rotModified = !ApproximatelyEuler(entry.debugTransform.localEulerAngles, entry.baselineLocalEuler);
            scaleModified = !ApproximatelyVector(entry.debugTransform.localScale, entry.baselineLocalScale);

            return centerModified || rotModified || scaleModified;
        }

        private static string NormalizeColliderDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            // onGUI 표시에서만 prefix를 제거한다(내부 키/매핑에는 영향 없음).
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
        // CapsuleCollider 디버그 와이어프레임을 생성한다.
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

            // Capsule 방향에 따라 기준 축과 회전을 결정한다.
            Vector3 axis;
            Quaternion rot = Quaternion.identity;
            switch (capsule.direction)
            {
                case 0: axis = Vector3.right; rot = Quaternion.Euler(0f, 0f, 90f); break;   // X
                case 1: axis = Vector3.up; rot = Quaternion.identity; break;                 // Y
                case 2: axis = Vector3.forward; rot = Quaternion.Euler(90f, 0f, 0f); break; // Z
                default: axis = Vector3.up; break;
            }

            int segments = 48; // 원호를 구성할 세그먼트 수

            // 원통 몸통과 상/하단 반구를 하나의 라인으로 생성
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

            renderers.Add(lr); // LineRenderer를 목록에 추가
            debugObjects.Add(root);
            debugObjects.Add(lineObj);
            debugCollideRenderers[capsule] = renderers;

            // 콜라이더 이름 텍스트 생성
            Vector3 textPos = axis * (halfHeight * 0.5f + 0.1f);
            CreateTextDebugLocal(root.transform, textPos, name, cam, debugObjects, debugCollideRenderers, isAdjustable);

            if (_showDebugVisuals == false)
            {
                root.SetActive(false);
            }
        }
        // SphereCollider 디버그 와이어프레임을 생성한다.
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

            // XY 평면 원
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                points.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
            // XZ 평면 원
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                points.Add(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            // YZ 평면 원
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

            // 콜라이더 이름 텍스트 생성
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

            // 카메라를 바라보도록 텍스트 방향 정렬
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
                    // 선택된 콜라이더는 빨간색으로 강조하고 나머지는 타입별 기본색을 유지한다.
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

        // 선택된 캐릭터의 Cloth Collider를 탐색해 디버그 시각화를 생성한다.
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
                    // 캐릭터의 8개 의상 슬롯에서 Cloth 컴포넌트를 찾아 기록한다.
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

                    // 필요 시 액세서리 슬롯(최대 20개)도 같은 방식으로 확장 가능
                    //idx = 0;
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
                    
                    // 현재 상/하의 Cloth가 실제로 참조하는 collider만 시각화 대상으로 한다.
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
                        // 교체 전 데이터/디버그 오브젝트 정리
                        _self.ForceRemoveVisualColliders(ociChar);

                        // 캐릭터가 바뀌면 상/하의 모두 "새로 로딩"된 것으로 보고 초기화한다.
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
                        // 변경 전 데이터/디버그 오브젝트 정리
                        _self.ForceRemoveVisualColliders(ociChar);

                        // 상의/하의가 새로 로딩되면 해당 슬롯은 무조건 초기화(이력 미보관)
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
                        // 변경 전 데이터/디버그 오브젝트 정리
                        _self.ForceRemoveVisualColliders(ociChar);
                    }
                }
            }
        }

        #endregion
    }
}
