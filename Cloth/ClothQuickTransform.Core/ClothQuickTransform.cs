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

    목적:
    - 의상 스키닝 본을 빠르게 매핑하고 위치/스케일을 조정하는 플러그인

    용어:
    - OCIChar: 캐릭터 
        > GetCurrentOCI 함수를 통해 현재 씬내 활성화된 캐리터를 획득

    최소 요구 기능:
        1) onGUI 내에 아래 UI를 구성해야 한다.
            1.1) 각 옷 파트 정보 버튼으로 구성
            1.2) 선택된 옷 파트에 대한 collider 리스트 조회 제공
            1.3) collider 선택 시 시각적으로  collider 제공(녹색 실선)
            1.4) collider 에 대한 postioon, scale editing UI 제공
        2) sceneWrite, sceneRead가 가능한데, 현재 씬을 저장 후 다시 복원하는 기능이다.
            >  캐릭터 별 ClothQuickTransformMapData 는 GetCurrentData() 를 통해, 현재 활성화된 캐릭터 데이터를 획득할 수 있다.
            2.1) sceneWrite 함수는 씬내 각 캐릭터의 각 ClothQuickTransformMapData 가 보유한 bone 이름과 bone 의 속성(position, scale) 정보를 xml에 저장한다.
            2.2) sceneRead는 함수는 sceneWrite 에서 저장한 xml 정보를 다시 ClothQuickTransformMapData 로 업데이트 해야 한다.

    추가 요구 기능:
        N/A

    현 버전 문제점:
        N/A
*/
namespace ClothQuickTransform
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
    public class ClothQuickTransform : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "ClothQuickTransform";
        public const string Version = "0.9.0.0";
        public const string GUID = "com.alton.illusionplugins.ClothQuickTransform";
        internal const string _ownerId = "Alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "cloth_transform_slider";
#endif
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types

        enum WinkState { Idle, Playing }
        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static ClothQuickTransform _self;

        private static string _assemblyLocation;
        internal bool _loaded = false;

        private AssetBundle _bundle;

        internal bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;
		
        private const int _uniqueId = ('C' << 24) | ('T' << 16) | ('F' << 8) | 'S';

        private Rect _windowRect = new Rect(140, 10, 340, 10);

        internal OCIChar _currentOCIChar = null;
        private ClothQuickTransformMapData _currentMapData = null;
        private GameObject _selectedBoneMarker;
        private Material _selectedBoneMaterial;
        private const float _selectedBoneMarkerScale = 1.0f;
        private const int _selectedBoneMarkerSegments = 24;
        private const float _selectedBoneMarkerLineWidth = 0.02f;
        private static readonly Color ModifiedEntryColor = new Color(1f, 0f, 0f, 1f);
        private static readonly Color UnmodifiedEntryColor = new Color(0.75f, 0.95f, 0.75f, 1f);
        private const float TransformCompareEpsilon = 0.001f;
        private const float RotationCompareEpsilonDegrees = 0.1f;
        private static readonly float[] SliderStepOptions = new float[] { 1f, 0.1f, 0.01f, 0.001f };
        private int _posStepIndex = 1;
        private int _rotStepIndex = 1;
        private int _scaleStepIndex = 2;
        private int _slotIndex = 0;
        private string _boneFilterText = string.Empty;
        private static readonly string[] ClothSlotLabels = new[]
        {
            "Top", "Bottom", "Bra", "Pants", "Gloves", "Stockings", "Shoes"
        };
    
        private GUIStyle _richLabel;

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
        // Config

        #region Accessors
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // Harmony 패치 적용 및 씬 저장/로드 이벤트 연결
            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            
#if FEATURE_SCENE_SAVE
            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingImported += OnSceneImport;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingSaved += OnSceneSave;
#endif
            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open ClothTransform window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(ClothQuickTransform).Assembly).LoadTexture(),
                false, this, val =>
                {
                    _ShowUI = val;
                    if (val && _currentOCIChar != null)
                        RefreshMappingsForSelection(_currentOCIChar);
                });
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);        
            CharacterApi.RegisterExtraBehaviour<ClothQuickTransformController>(GUID);

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
        protected override void Update()
        {
        }

        protected override void LateUpdate()
        {
        }

        protected override void OnGUI()
        {
            if (_loaded == false)
                return;

            if (StudioAPI.InsideStudio) {            
                if (_ShowUI == false) {                    
                    ClearSelectedBoneHighlight();
                    return;
                }

                OCIChar current = GetCurrentOCI();
                if (current != _currentOCIChar)
                {
                    _currentOCIChar = current;
                    _currentMapData = current != null ? GetDataAndCreate(current) : null;
                    if (_currentOCIChar != null)
                        RefreshMappingsForSelection(_currentOCIChar);
                    else
                        ClearSelectedBoneHighlight();
                }

                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "ClothQuickTransform " + Version);
            }
        }
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
            _boneFilterText = string.Empty;
            _currentOCIChar = null;
            _currentMapData = null;
            ClearSelectedBoneHighlight();
        }        

#if FEATURE_SCENE_SAVE
        // 씬 저장시 현재 조정값을 기록한다.
        private void OnSceneSave(string path)
        {
            using (StringWriter stringWriter = new StringWriter())
            using (XmlTextWriter xmlWriter = new XmlTextWriter(stringWriter))
            {
                xmlWriter.WriteStartElement("root");
                SceneWrite(path, xmlWriter);
                xmlWriter.WriteEndElement();

                PluginData data = new PluginData();
                data.version = _saveVersion;
                data.data.Add("sceneInfo", stringWriter.ToString());
                // UnityEngine.Debug.Log($">> CTS SceneSave xml:\n{stringWriter}");
                ExtendedSave.SetSceneExtendedDataById(_extSaveKey, data);
            }
        }

        // 씬 로드시 저장된 조정값을 복원한다.
        private void OnSceneLoad(string path)
        {
            PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
            if (data == null)
                return;
            //if (data.data != null && data.data.ContainsKey("sceneInfo"))
            //     UnityEngine.Debug.Log($">> CTS SceneLoad xml:\n{data.data["sceneInfo"]}");

            XmlDocument doc = new XmlDocument();
            doc.LoadXml((string)data.data["sceneInfo"]);
            XmlNode node = doc.FirstChild;
            if (node == null)
                return;
            SceneLoad(path, node);
        }

        // 씬 임포트시 저장된 조정값을 복원한다.
        private void OnSceneImport(string path)
        {
            Logger.LogMessage($"Import not support");
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

        // 씬 데이터에서 캐릭터별 조정값을 읽어 적용한다.
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

            int restoredChars = 0;
            int restoredTransfers = 0;

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
                    string name = charNode.Attributes["name"]?.Value;
                    if (!string.IsNullOrEmpty(name))
                        ociCharByName.TryGetValue(name, out ociChar);
                }

                if (ociChar == null)
                    continue;

                var savedBySlot = new Dictionary<int, Dictionary<string, SavedAdjustment>>();
                foreach (XmlNode transferNode in charNode.SelectNodes("transfer"))
                {
                    string boneName = NormalizeBoneName(transferNode.Attributes["name"]?.Value);
                    if (string.IsNullOrEmpty(boneName))
                        continue;

                    int slotIndex = 0;
                    var slotAttr = transferNode.Attributes["slot"];
                    if (slotAttr != null && int.TryParse(slotAttr.Value, out int slotValue))
                        slotIndex = slotValue;

                    var adj = new SavedAdjustment
                    {
                        position = new Vector3(
                            ReadFloat(transferNode, "posX"),
                            ReadFloat(transferNode, "posY"),
                            ReadFloat(transferNode, "posZ")),
                        rotation = new Vector3(
                            ReadFloat(transferNode, "rotX"),
                            ReadFloat(transferNode, "rotY"),
                            ReadFloat(transferNode, "rotZ")),
                        scale = new Vector3(
                            ReadFloat(transferNode, "scaleX", 1f),
                            ReadFloat(transferNode, "scaleY", 1f),
                            ReadFloat(transferNode, "scaleZ", 1f))
                    };

                    if (!savedBySlot.TryGetValue(slotIndex, out var slotMap))
                    {
                        slotMap = new Dictionary<string, SavedAdjustment>();
                        savedBySlot[slotIndex] = slotMap;
                    }
                    slotMap[boneName] = adj;
                }

                int dicKey;
                bool hasDicKey = TryGetDicKey(ociChar.GetChaControl(), out dicKey);
                int savedCount = savedBySlot.Values.Sum(v => v != null ? v.Count : 0);
                UnityEngine.Debug.Log($">> SceneRead char dicKey={(hasDicKey ? dicKey.ToString() : "not-found")} savedTransfers={savedCount}");

                var mapData = GetDataAndCreate(ociChar);
                if (mapData != null)
                {
                    mapData.savedAdjustmentsBySlot = savedBySlot;
                    mapData.transferEntriesBySlot = new Dictionary<int, List<TransferEntry>>();
                    mapData.selectedTransferIndexBySlot = new Dictionary<int, int>();

                    // SceneRead 직후 실제 본 매핑/조정값 적용까지 수행한다.
                    // 슬롯별로 저장된 값이 있는 경우에만 리맵 시도.
                    var chaCtrl = ociChar.GetChaControl();
                    if (chaCtrl != null && savedBySlot != null)
                    {
                        foreach (var kv in savedBySlot)
                        {
                            int slotIndex = kv.Key;
                            var slotMap = kv.Value;
                            if (slotMap == null || slotMap.Count == 0)
                                continue;

                            if (TryMarkPendingAutoRemap(chaCtrl, slotIndex))
                                StartCoroutine(AutoMapDelayedForSlot(chaCtrl, slotIndex, false));
                        }
                    }
                }

                restoredChars++;
                restoredTransfers += savedCount;
            }

            if (_currentOCIChar != null)
                RefreshMappingsForSelection(_currentOCIChar);
        }

        // 현재 캐릭터의 조정값을 씬 데이터로 기록한다.
        private void SceneWrite(string path, XmlTextWriter writer)
        {
            var dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
            var ociCharByDicKey = dic
                .Where(kv => kv.Value is OCIChar)
                .ToDictionary(kv => kv.Key, kv => kv.Value as OCIChar);
            int activeChars = ociCharByDicKey.Count;
            int adjustmentChars = 0;

            foreach (var kv in ociCharByDicKey)
            {
                int dicKey = kv.Key;
                OCIChar ociChar = kv.Value;
                if (ociChar == null)
                    continue;

                var mapData = GetDataAndCreate(ociChar);
                if (mapData == null)
                    continue;

                if (mapData.savedAdjustmentsBySlot == null)
                    mapData.savedAdjustmentsBySlot = new Dictionary<int, Dictionary<string, SavedAdjustment>>();

                if (mapData.transferEntriesBySlot != null)
                {
                    foreach (var kvp in mapData.transferEntriesBySlot)
                    {
                        var list = kvp.Value;
                        if (list == null || list.Count == 0)
                            continue;
                        mapData.savedAdjustmentsBySlot[kvp.Key] = CaptureAdjustments(list);
                    }
                }

                var mapBySlot = mapData.savedAdjustmentsBySlot;
                if (mapBySlot == null || mapBySlot.Count == 0)
                    continue;

                adjustmentChars++;
                // UnityEngine.Debug.Log($">> SceneWrite char dicKey={dicKey} transfers={map.Count}");

                writer.WriteStartElement("character");
                writer.WriteAttributeString("dicKey", dicKey.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("hash", ociChar.GetChaControl().GetHashCode().ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("name", ociChar.charInfo != null ? ociChar.charInfo.name : string.Empty);

                foreach (var slotEntry in mapBySlot)
                {
                    int slotIndex = slotEntry.Key;
                    var map = slotEntry.Value;
                    if (map == null || map.Count == 0)
                        continue;

                    foreach (var entry in map)
                    {
                        writer.WriteStartElement("transfer");
                        writer.WriteAttributeString("slot", slotIndex.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("name", entry.Key);
                        writer.WriteAttributeString("posX", entry.Value.position.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("posY", entry.Value.position.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("posZ", entry.Value.position.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("rotX", entry.Value.rotation.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("rotY", entry.Value.rotation.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("rotZ", entry.Value.rotation.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("scaleX", entry.Value.scale.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("scaleY", entry.Value.scale.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("scaleZ", entry.Value.scale.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }
                }

                writer.WriteEndElement();
            }
            // UnityEngine.Debug.Log($">> SceneWrite adjustments chars={adjustmentChars} activeChars={activeChars}");
        }
#endif

        // 현재 씬에서 선택된 OCIChar를 가져온다.
        private OCIChar GetCurrentOCI()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null)
                return null;

            TreeNodeObject node  = Studio.Studio.Instance.treeNodeCtrl.selectNodes
                .LastOrDefault();

            return  node != null ? Studio.Studio.GetCtrlInfo(node) as OCIChar : null;
        }

        internal ClothQuickTransformController GetCurrentControl()
        {
            OCIChar ociChar = GetCurrentOCI();
            if (ociChar != null)
            {
                return ociChar.GetChaControl().GetComponent<ClothQuickTransformController>();
            }
            return null;
        }        

        // 현재 선택된 OCIChar 에서 MapData 를 가져온다.
        private ClothQuickTransformMapData GetDataAndCreate(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            var controller = ociChar.GetChaControl().GetComponent<ClothQuickTransformController>();
            if (controller == null)
                return null;

            return controller.GetData() ?? controller.CreateData(ociChar);
        }

        private bool TryGetMapData(ChaControl chaCtrl, out ClothQuickTransformMapData mapData, out OCIChar ociChar)
        {
            mapData = null;
            ociChar = null;
            if (chaCtrl == null)
                return false;

            ociChar = chaCtrl.GetOCIChar();
            if (ociChar == null)
                return false;

            mapData = GetDataAndCreate(ociChar);
            return mapData != null;
        }

        private ClothQuickTransformMapData EnsureCurrentMapData()
        {
            if (_currentOCIChar == null)
                return null;
            if (_currentMapData == null || _currentMapData.ociChar != _currentOCIChar)
                _currentMapData = GetDataAndCreate(_currentOCIChar);
            return _currentMapData;
        }

        // 메인 UI 렌더링 및 선택 처리
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

            var chaCtrl = GetCurrentChaControl();
            var mapData = EnsureCurrentMapData();

            if (chaCtrl == null)
            {
                GUILayout.Label("<color=white>Nothing to select</color>", RichLabel);
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Auto Map", GUILayout.Width(120)))
                {
                    AutoMap(chaCtrl);
                }
                if (GUILayout.Button("Clear", GUILayout.Width(120)))
                {
                    if (mapData != null)
                    {
                        var entries = GetOrCreateTransferEntriesFor(chaCtrl, _slotIndex);
                        ClearMappings(entries, true, mapData, _slotIndex);
                    }
                }
                GUILayout.EndHorizontal();

                draw_seperate();
                int prevSlotIndex = _slotIndex;
                DrawClothSlotFilter();
                if (prevSlotIndex != _slotIndex && mapData != null)
                {
                    var entries = GetOrCreateTransferEntriesFor(chaCtrl, _slotIndex);
                    int selectedIndex = GetSelectedTransferIndex(mapData, _slotIndex);
                    if (selectedIndex < 0 || selectedIndex >= entries.Count)
                        SetSelectedTransferIndex(mapData, _slotIndex, entries.Count > 0 ? 0 : -1);
                    if (entries.Count == 0 && CanAutoMap(chaCtrl))
                        AutoMap(chaCtrl);
                    UpdateSelectedBoneHighlight();
                }
                draw_seperate();

                if (mapData == null)
                {
                    GUILayout.Label("<color=white>No mapper data</color>", RichLabel);
                }
                else
                {
                    int selectedIndex = 0;
                    var entries = GetOrCreateTransferEntriesFor(chaCtrl, _slotIndex);
                    Vector2 scroll = GetTransferScroll(mapData, _slotIndex);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Filter", GUILayout.Width(42));
                    _boneFilterText = GUILayout.TextField(_boneFilterText ?? string.Empty, GUILayout.MinWidth(120));
                    if (GUILayout.Button("Clear", GUILayout.Width(52)))
                        _boneFilterText = string.Empty;
                    GUILayout.EndHorizontal();

                    string filter = (_boneFilterText ?? string.Empty).Trim();
                    bool hasFilter = !string.IsNullOrEmpty(filter);

                    scroll = GUILayout.BeginScrollView(scroll, GUI.skin.box, GUILayout.Height(180));
                    int prevSelectedIndex = GetSelectedTransferIndex(mapData, _slotIndex);
                    var activeBoneNames = GetActiveClothBoneNames(chaCtrl, _slotIndex);
                    var visibleIndices = entries
                        .Select((entry, index) => new { entry, index })
                        .Where(x => x.entry != null && !string.IsNullOrEmpty(x.entry.boneName))
                        .Where(x => activeBoneNames != null && activeBoneNames.Contains(NormalizeBoneName(x.entry.boneName)))
                        .Where(x => IsAdjustableBoneName(NormalizeBoneName(x.entry.boneName)))
                        .Where(x => !hasFilter
                            || x.entry.boneName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                            || NormalizeBoneName(x.entry.boneName).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderBy(x => x.entry.boneName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (visibleIndices.Count == 0)
                    {
                        GUILayout.Label("<color=white>No mapped bones</color>", RichLabel);
                        SetSelectedTransferIndex(mapData, _slotIndex, -1);
                        ClearSelectedBoneHighlight();
                    }
                    else
                    {
                        selectedIndex = GetSelectedTransferIndex(mapData, _slotIndex);
                        if (!visibleIndices.Any(v => v.index == selectedIndex))
                            SetSelectedTransferIndex(mapData, _slotIndex, visibleIndices[0].index);

                        foreach (var item in visibleIndices)
                        {
                            var entry = item.entry;
                            string label = entry.boneName;
                            bool isModified = IsEntryModified(entry);
                            Color prevContentColor = GUI.contentColor;
                            GUI.contentColor = isModified ? ModifiedEntryColor : UnmodifiedEntryColor;
                            if (GUILayout.Toggle(GetSelectedTransferIndex(mapData, _slotIndex) == item.index, label, GUI.skin.button))
                            {
                                SetSelectedTransferIndex(mapData, _slotIndex, item.index);
                            }
                            GUI.contentColor = prevContentColor;
                        }
                    }
                    GUILayout.EndScrollView();
                    SetTransferScroll(mapData, _slotIndex, scroll);
                    if (hasFilter)
                        GUILayout.Label($"Shown: {visibleIndices.Count}/{entries.Count}", RichLabel);
                    if (prevSelectedIndex != GetSelectedTransferIndex(mapData, _slotIndex))
                    {
                        UpdateSelectedBoneHighlight();
                    }

                    selectedIndex = GetSelectedTransferIndex(mapData, _slotIndex);
                    if (selectedIndex >= 0 && selectedIndex < entries.Count)
                    {
                        var entry = entries[selectedIndex];
                        if (entry != null && entry.transfer != null)
                        {
                            bool isSelectedModified = IsEntryModified(entry);
                            string statusText = isSelectedModified ? "<color=red>Modified</color>" : "<color=#7CFC00>Unmodified</color>";
                            GUILayout.Label($"Status: {statusText}", RichLabel);

                            GUILayout.Label("<color=orange>Position</color>", RichLabel);
                            DrawStepSelector(ref _posStepIndex, "Step");
                            Vector3 pos = entry.transfer.localPosition;
                            float posStep = SliderStepOptions[_posStepIndex];
                            pos.x = SliderRow("Pos X", pos.x, -2.0f, 2.0f, 0.0f, posStep);
                            pos.y = SliderRow("Pos Y", pos.y, -2.0f, 2.0f, 0.0f, posStep);
                            pos.z = SliderRow("Pos Z", pos.z, -2.0f, 2.0f, 0.0f, posStep);
                            entry.transfer.localPosition = pos;

                            GUILayout.Label("<color=orange>Rotation</color>", RichLabel);
                            DrawStepSelector(ref _rotStepIndex, "Step");
                            Vector3 rot = NormalizeEuler(entry.transfer.localEulerAngles);
                            float rotStep = SliderStepOptions[_rotStepIndex];
                            rot.x = SliderRow("Rot X", rot.x, -180.0f, 180.0f, 0.0f, rotStep);
                            rot.y = SliderRow("Rot Y", rot.y, -180.0f, 180.0f, 0.0f, rotStep);
                            rot.z = SliderRow("Rot Z", rot.z, -180.0f, 180.0f, 0.0f, rotStep);
                            entry.transfer.localRotation = Quaternion.Euler(rot);

                            GUILayout.Label("<color=orange>Scale</color>", RichLabel);
                            DrawStepSelector(ref _scaleStepIndex, "Step");
                            Vector3 scale = entry.transfer.localScale;
                            float scaleStep = SliderStepOptions[_scaleStepIndex];
                            scale.x = SliderRow("Scale X", scale.x, 0.1f, 3.0f, 1.0f, scaleStep);
                            scale.y = SliderRow("Scale Y", scale.y, 0.1f, 3.0f, 1.0f, scaleStep);
                            scale.z = SliderRow("Scale Z", scale.z, 0.1f, 3.0f, 1.0f, scaleStep);
                            entry.transfer.localScale = scale;

                            StoreAdjustmentsFor(chaCtrl, CaptureAdjustments(entries), _slotIndex);

                            if (GUILayout.Button("Reset All"))
                            {
                                entry.transfer.localPosition = Vector3.zero;
                                entry.transfer.localRotation = Quaternion.identity;
                                entry.transfer.localScale = Vector3.one;
                                StoreAdjustmentsFor(chaCtrl, CaptureAdjustments(entries), _slotIndex);
                            }
                        }
                    }
                }
            }

            if (GUILayout.Button("Close")) {
                Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
                _ShowUI = false;
            }

            // 툴팁 표시
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 150, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }
        private void draw_seperate()
        {
            GUILayout.Space(5);
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(0.3f));
            GUI.Box(rect, GUIContent.none);
            GUILayout.Space(10);
        }

        internal float SliderRow(string label, float value, float min, float max, float resetValue, float step)
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

        private bool IsEntryModified(TransferEntry entry)
        {
            if (entry == null || entry.transfer == null)
                return false;

            Vector3 pos = entry.transfer.localPosition;
            Quaternion rot = entry.transfer.localRotation;
            Vector3 scale = entry.transfer.localScale;

            return !ApproximatelyVector(pos, Vector3.zero)
                || !ApproximatelyQuaternion(rot, Quaternion.identity)
                || !ApproximatelyVector(scale, Vector3.one);
        }

        private bool ApproximatelyVector(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < TransformCompareEpsilon
                && Mathf.Abs(a.y - b.y) < TransformCompareEpsilon
                && Mathf.Abs(a.z - b.z) < TransformCompareEpsilon;
        }

        private bool ApproximatelyQuaternion(Quaternion a, Quaternion b)
        {
            return Quaternion.Angle(a, b) < RotationCompareEpsilonDegrees;
        }

        private Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(
                NormalizeAngle(euler.x),
                NormalizeAngle(euler.y),
                NormalizeAngle(euler.z));
        }

        private float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            else if (angle < -180f)
                angle += 360f;
            return angle;
        }

        private bool IsAdjustableBoneName(string normalizedBoneName)
        {
            if (string.IsNullOrEmpty(normalizedBoneName))
                return false;

            bool hasPrefix = normalizedBoneName.StartsWith("cf_J_", StringComparison.Ordinal)
                || normalizedBoneName.StartsWith("cm_J_", StringComparison.Ordinal);
            if (!hasPrefix)
                return false;

            return normalizedBoneName.IndexOf("_s_", StringComparison.Ordinal) >= 0
                || normalizedBoneName.EndsWith("_s", StringComparison.Ordinal);
        }

        internal IEnumerator AutoMapDelayed(ChaControl chaCtrl)
        {
            int slotIndex = _slotIndex;
            yield return AutoMapDelayedForSlot(chaCtrl, slotIndex, true);
        }

        internal IEnumerator AutoMapDelayedForSlot(ChaControl chaCtrl, int slotIndex, bool updateSelection)
        {
            int frameCount = 15;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            try
            {
                // 의상이 비동기로 로딩되는 구간이 있어 추가 대기
                int maxWaitFrames = 120;
                for (int i = 0; i < maxWaitFrames; i++)
                {
                    if (CanAutoMap(chaCtrl, slotIndex) && GetPotentialBoneCount(chaCtrl, slotIndex) > 0)
                        break;
                    yield return null;
                }

                if (!CanAutoMap(chaCtrl, slotIndex))
                    yield break;

                var entries = GetOrCreateTransferEntriesFor(chaCtrl, slotIndex);
                var saved = GetSavedFor(chaCtrl, slotIndex) ?? CaptureAdjustments(entries);
                var isCurrent = IsCurrentChar(chaCtrl);
                var mapData = TryGetMapData(chaCtrl, out var md, out _) ? md : null;

                bool updateSelectionForSlot = updateSelection && isCurrent && _slotIndex == slotIndex;
                ClearMappings(entries, updateSelectionForSlot, mapData, slotIndex);
                AutoMapWithSaved(chaCtrl, slotIndex, saved, entries, updateSelectionForSlot);
            }
            finally
            {
                UnmarkPendingAutoRemap(chaCtrl, slotIndex);
            }
        }

        internal ChaControl GetCurrentChaControl()
        {
            if (_currentOCIChar == null)
                return null;
            return _currentOCIChar.GetChaControl();
        }

        private bool IsCurrentChar(ChaControl chaCtrl)
        {
            if (chaCtrl == null || _currentOCIChar == null)
                return false;
            return _currentOCIChar.GetChaControl() == chaCtrl;
        }

        private List<TransferEntry> GetOrCreateTransferEntriesFor(ChaControl chaCtrl, int slotIndex)
        {
            if (chaCtrl == null)
                return new List<TransferEntry>();

            if (!TryGetMapData(chaCtrl, out var mapData, out _))
                return new List<TransferEntry>();

            if (mapData.transferEntriesBySlot == null)
                mapData.transferEntriesBySlot = new Dictionary<int, List<TransferEntry>>();

            if (!mapData.transferEntriesBySlot.TryGetValue(slotIndex, out var list) || list == null)
            {
                list = new List<TransferEntry>();
                mapData.transferEntriesBySlot[slotIndex] = list;
            }

            return list;
        }

        private int GetSelectedTransferIndex(ClothQuickTransformMapData mapData, int slotIndex)
        {
            if (mapData == null || mapData.selectedTransferIndexBySlot == null)
                return -1;
            if (mapData.selectedTransferIndexBySlot.TryGetValue(slotIndex, out int idx))
                return idx;
            return -1;
        }

        private void SetSelectedTransferIndex(ClothQuickTransformMapData mapData, int slotIndex, int index)
        {
            if (mapData == null)
                return;
            if (mapData.selectedTransferIndexBySlot == null)
                mapData.selectedTransferIndexBySlot = new Dictionary<int, int>();
            mapData.selectedTransferIndexBySlot[slotIndex] = index;
        }

        private Vector2 GetTransferScroll(ClothQuickTransformMapData mapData, int slotIndex)
        {
            if (mapData == null || mapData.transferScrollBySlot == null)
                return Vector2.zero;
            if (mapData.transferScrollBySlot.TryGetValue(slotIndex, out var scroll))
                return scroll;
            return Vector2.zero;
        }

        private void SetTransferScroll(ClothQuickTransformMapData mapData, int slotIndex, Vector2 scroll)
        {
            if (mapData == null)
                return;
            if (mapData.transferScrollBySlot == null)
                mapData.transferScrollBySlot = new Dictionary<int, Vector2>();
            mapData.transferScrollBySlot[slotIndex] = scroll;
        }

        private void SwitchToCharForUI(ChaControl chaCtrl)
        {
            if (chaCtrl == null)
                return;

            if (!TryGetMapData(chaCtrl, out var mapData, out var ociChar))
                return;

            _currentOCIChar = ociChar;
            _currentMapData = mapData;

            var entries = GetOrCreateTransferEntriesFor(chaCtrl, _slotIndex);
            int selectedIndex = GetSelectedTransferIndex(_currentMapData, _slotIndex);
            if (selectedIndex < 0 || selectedIndex >= entries.Count)
                SetSelectedTransferIndex(_currentMapData, _slotIndex, entries.Count > 0 ? 0 : -1);

            ClearSelectedBoneHighlight();
            UpdateSelectedBoneHighlight();
        }

        // 선택된 캐릭터 기준으로 매핑을 다시 구성한다.
        internal void RefreshMappingsForSelection(OCIChar ociChar)
        {
            if (ociChar == null)
                return;

            var chaCtrl = ociChar.GetChaControl();
            if (chaCtrl == null)
                return;

            SwitchToCharForUI(chaCtrl);

            if (!CanAutoMap(chaCtrl))
                return;

            if (_currentMapData == null)
                return;

            var entries = GetOrCreateTransferEntriesFor(chaCtrl, _slotIndex);
            if (entries.Count > 0)
                return;

            var saved = GetSavedFor(chaCtrl, _slotIndex);

            ClearMappings(entries, true, _currentMapData, _slotIndex);
            AutoMapWithSaved(chaCtrl, _slotIndex, saved, entries, true);
        }

        // 외부 호출용 자동 매핑 진입점
        internal bool TryMarkPendingAutoRemap(ChaControl chaCtrl)
        {
            return TryMarkPendingAutoRemap(chaCtrl, _slotIndex);
        }

        // 슬롯별 자동 리맵 중복 수행 방지
        internal bool TryMarkPendingAutoRemap(ChaControl chaCtrl, int slotIndex)
        {
            if (!TryGetMapData(chaCtrl, out ClothQuickTransformMapData mapData, out _))
                return false;

            if (mapData.pendingAutoRemapSlots == null)
                mapData.pendingAutoRemapSlots = new HashSet<int>();

            if (mapData.pendingAutoRemapSlots.Contains(slotIndex))
                return false;

            mapData.pendingAutoRemapSlots.Add(slotIndex);
            return true;
        }

        private void UnmarkPendingAutoRemap(ChaControl chaCtrl, int slotIndex)
        {
            if (TryGetMapData(chaCtrl, out ClothQuickTransformMapData mapData, out _))
            {
                if (mapData.pendingAutoRemapSlots != null)
                    mapData.pendingAutoRemapSlots.Remove(slotIndex);
            }
        }

        internal void AutoMap(ChaControl chaCtrl)
        {
            if (!CanAutoMap(chaCtrl, _slotIndex))
            {
                if (TryMarkPendingAutoRemap(chaCtrl, _slotIndex))
                    StartCoroutine(AutoMapDelayedForSlot(chaCtrl, _slotIndex, true));
                return;
            }

            int potential = GetPotentialBoneCount(chaCtrl, _slotIndex);
            if (potential == 0)
            {
                return;
            }

            var entries = GetOrCreateTransferEntriesFor(chaCtrl, _slotIndex);
            var saved = GetSavedFor(chaCtrl, _slotIndex) ?? CaptureAdjustments(entries);
            var isCurrent = IsCurrentChar(chaCtrl);
            var mapData = TryGetMapData(chaCtrl, out var md, out _) ? md : null;
            ClearMappings(entries, isCurrent, mapData, _slotIndex);
            AutoMapWithSaved(chaCtrl, _slotIndex, saved, entries, isCurrent);
        }

        // 저장된 조정값을 반영하면서 본을 매핑한다.
        // ChangeClothesAsync(kind) -> objClothes 슬롯 인덱스로 해석한다.
        // HS2/AI 기준으로 kind가 0~6(상의/하의/장갑/신발/악세류 등) 범위로 들어오는 케이스가 많다.
        private bool TryResolveClothSlotIndex(int kind, out int slotIndex)
        {
            slotIndex = -1;
            if (kind < 0)
                return false;
            // UI의 슬롯 필터(0~6)와 동일하게 사용
            if (kind >= 0 && kind < ClothSlotLabels.Length)
            {
                slotIndex = kind;
                return true;
            }
            return false;
        }

        // 의상 슬롯이 새로 로딩되면 해당 슬롯의 조정값/매핑은 "무조건 초기화"한다.
        private void ResetSlotForClothesChange(ChaControl chaCtrl, int slotIndex)
        {
            if (chaCtrl == null)
                return;
            if (!TryGetMapData(chaCtrl, out var mapData, out _))
                return;

            // 1) 저장된 조정값 제거 (A -> B -> A 로 돌아와도 복원하지 않음)
            if (mapData.savedAdjustmentsBySlot != null)
                mapData.savedAdjustmentsBySlot.Remove(slotIndex);

            // 2) UI 상태 초기화(선택/스크롤)
            if (mapData.selectedTransferIndexBySlot != null)
                mapData.selectedTransferIndexBySlot.Remove(slotIndex);
            if (mapData.transferScrollBySlot != null)
                mapData.transferScrollBySlot.Remove(slotIndex);

            // 3) 기존 transfer 오브젝트/렌더러 본 매핑 제거
            if (mapData.transferEntriesBySlot != null
                && mapData.transferEntriesBySlot.TryGetValue(slotIndex, out var entries)
                && entries != null)
            {
                bool clearSelection = IsCurrentChar(chaCtrl) && _slotIndex == slotIndex;
                ClearMappings(entries, clearSelection, mapData, slotIndex);
            }
        }

        // Clothing/accessory state can invalidate all mapped collider refs.
        // Reset entire map data so the next automap reads from current outfit state.
        private void ResetAllMapDataForClothesChange(ChaControl chaCtrl)
        {
            if (chaCtrl == null)
                return;
            if (!TryGetMapData(chaCtrl, out var mapData, out _))
                return;

            if (mapData.transferEntriesBySlot != null)
            {
                var slots = mapData.transferEntriesBySlot.Keys.ToList();
                foreach (int slotIndex in slots)
                {
                    if (!mapData.transferEntriesBySlot.TryGetValue(slotIndex, out var entries) || entries == null)
                        continue;

                    bool clearSelection = IsCurrentChar(chaCtrl) && _slotIndex == slotIndex;
                    ClearMappings(entries, clearSelection, mapData, slotIndex);
                }
            }

            mapData.transferEntriesBySlot = new Dictionary<int, List<TransferEntry>>();
            mapData.selectedTransferIndexBySlot = new Dictionary<int, int>();
            mapData.savedAdjustmentsBySlot = new Dictionary<int, Dictionary<string, SavedAdjustment>>();
            mapData.pendingAutoRemapSlots = new HashSet<int>();
            mapData.transferScrollBySlot = new Dictionary<int, Vector2>();
        }

        private void AutoMapWithSaved(ChaControl chaCtrl, int slotIndex, Dictionary<string, SavedAdjustment> saved, List<TransferEntry> entries, bool updateSelection)
        {
            if (chaCtrl == null)
                return;
            if (entries == null)
                return;

            SkinnedMeshRenderer bodyRenderer = GetBodyRenderer(chaCtrl);
            if (bodyRenderer == null)
            {
                // UnityEngine.Debug.Log($">> AutoMapWithSaved: bodyRenderer null");
                return;
            }

            var bodyBonesByName = new Dictionary<string, Transform>();
            // Prefer full body hierarchy to avoid missing bones when renderer bones list is limited.
            if (chaCtrl.objBody != null)
            {
                var all = chaCtrl.objBody.GetComponentsInChildren<Transform>(true);
                if (all != null)
                {
                    foreach (var bone in all)
                    {
                        if (bone == null)
                            continue;
                        string boneName = NormalizeBoneName(bone.name);
                        if (string.IsNullOrEmpty(boneName))
                            continue;
                        if (!bodyBonesByName.ContainsKey(boneName))
                            bodyBonesByName.Add(boneName, bone);
                    }
                }
            }
            // UnityEngine.Debug.Log($">> AutoMapWithSaved: bodyBones={bodyBonesByName.Count}");

            var clothRenderers = GetActiveClothRenderers(chaCtrl, slotIndex);
            if (clothRenderers.Count == 0)
            {
                // UnityEngine.Debug.Log($">> AutoMapWithSaved: no cloth renderers");
                return;
            }

            int savedCount = saved != null ? saved.Count : 0;
            // UnityEngine.Debug.Log($">> AutoMapWithSaved: clothRenderers={clothRenderers.Count} savedTransfers={savedCount}");

            if (saved != null && savedCount > 0)
            {
                string[] sampleSaved = saved.Keys.Take(5).ToArray();
                // UnityEngine.Debug.Log($">> AutoMapWithSaved: saved sample={string.Join(",", sampleSaved)}");
            }

            var transferByName = new Dictionary<string, TransferEntry>();
            int applied = 0;

            foreach (var renderer in clothRenderers)
            {
                var bones = renderer.bones;
                bool changed = false;

                for (int i = 0; i < bones.Length; i++)
                {
                    Transform clothBone = bones[i];
                    if (clothBone == null)
                        continue;

                    string clothBoneName = NormalizeBoneName(clothBone.name);
                    if (string.IsNullOrEmpty(clothBoneName))
                        continue;
                    if (!IsAdjustableBoneName(clothBoneName))
                        continue;

                    Transform bodyBone;
                    if (!bodyBonesByName.TryGetValue(clothBoneName, out bodyBone))
                        bodyBone = clothBone;

                    TransferEntry entry;
                    if (!transferByName.TryGetValue(clothBoneName, out entry))
                    {
                        var transferGO = new GameObject(clothBoneName + "_CTS");
                        var transferTr = transferGO.transform;
                        transferTr.SetParent(bodyBone, false);
                        transferTr.localPosition = Vector3.zero;
                        transferTr.localRotation = Quaternion.identity;
                        transferTr.localScale = Vector3.one;

                        entry = new TransferEntry
                        {
                            boneName = clothBoneName,
                            bodyBone = bodyBone,
                            transfer = transferTr
                        };
                        transferByName.Add(clothBoneName, entry);
                        entries.Add(entry);
                    }

                    if (saved != null && saved.ContainsKey(entry.boneName))
                        applied++;
                    ApplySavedAdjustment(entry, saved);

                    entry.refs.Add(new RendererBoneRef
                    {
                        renderer = renderer,
                        boneIndex = i,
                        originalBone = clothBone
                    });

                    bones[i] = entry.transfer;
                    changed = true;
                }

                if (changed)
                {
                    renderer.bones = bones;
                }
            }

            StoreAdjustmentsFor(chaCtrl, CaptureAdjustments(entries), slotIndex);
            if (updateSelection)
            {
                if (TryGetMapData(chaCtrl, out var mapData, out _))
                {
                    SetSelectedTransferIndex(mapData, slotIndex, entries.Count > 0 ? 0 : -1);
                }
                UpdateSelectedBoneHighlight();
            }
        }

        private Dictionary<string, SavedAdjustment> CaptureAdjustments(List<TransferEntry> entries)
        {
            var map = new Dictionary<string, SavedAdjustment>();
            if (entries == null)
                return map;
            foreach (var entry in entries)
            {
                if (entry == null || entry.transfer == null || string.IsNullOrEmpty(entry.boneName))
                    continue;
                string name = NormalizeBoneName(entry.boneName);
                if (string.IsNullOrEmpty(name))
                    continue;
                map[name] = new SavedAdjustment
                {
                    position = entry.transfer.localPosition,
                    rotation = NormalizeEuler(entry.transfer.localEulerAngles),
                    scale = entry.transfer.localScale
                };
            }
            return map;
        }
        private void ApplySavedAdjustment(TransferEntry entry, Dictionary<string, SavedAdjustment> saved)
        {
            if (entry == null || entry.transfer == null || saved == null)
                return;
            if (saved.TryGetValue(entry.boneName, out var adj))
            {
                entry.transfer.localPosition = adj.position;
                entry.transfer.localRotation = Quaternion.Euler(adj.rotation);
                entry.transfer.localScale = adj.scale;
            }
        }

        // 기존 렌더러 본 연결을 원래대로 되돌리고 생성 오브젝트를 정리한다.
        private void ClearMappings(List<TransferEntry> entries, bool clearSelection, ClothQuickTransformMapData mapData, int slotIndex)
        {
            if (entries == null)
                return;
            foreach (var entry in entries)
            {
                if (entry == null)
                    continue;

                foreach (var r in entry.refs)
                {
                    if (r.renderer == null || r.originalBone == null)
                        continue;

                    var bones = r.renderer.bones;
                    if (r.boneIndex >= 0 && r.boneIndex < bones.Length)
                    {
                        bones[r.boneIndex] = r.originalBone;
                        r.renderer.bones = bones;
                    }
                }

                if (entry.transfer != null)
                {
                    entry.transfer.SetParent(null);
                    GameObject.Destroy(entry.transfer.gameObject);
                }
            }

            entries.Clear();
            if (clearSelection)
            {
                if (mapData != null)
                    SetSelectedTransferIndex(mapData, slotIndex, -1);
                ClearSelectedBoneHighlight();
            }
        }

        // dicKey가 있으면 dicKey로, 없으면 해시로 저장한다.
        private void StoreAdjustmentsFor(ChaControl chaCtrl, Dictionary<string, SavedAdjustment> map, int slotIndex)
        {
            if (chaCtrl == null || map == null)
                return;
            if (TryGetMapData(chaCtrl, out var mapData, out _))
            {
                if (mapData.savedAdjustmentsBySlot == null)
                    mapData.savedAdjustmentsBySlot = new Dictionary<int, Dictionary<string, SavedAdjustment>>();
                mapData.savedAdjustmentsBySlot[slotIndex] = map;
            }
        }

        private Dictionary<string, SavedAdjustment> GetSavedFor(ChaControl chaCtrl, int slotIndex)
        {
            if (chaCtrl == null)
                return null;

            if (TryGetMapData(chaCtrl, out var mapData, out _))
            {
                if (mapData.savedAdjustmentsBySlot != null
                    && mapData.savedAdjustmentsBySlot.TryGetValue(slotIndex, out var map))
                    return map;
            }

            return null;
        }

        private bool CanAutoMap(ChaControl chaCtrl)
        {
            return CanAutoMap(chaCtrl, _slotIndex);
        }

        private bool CanAutoMap(ChaControl chaCtrl, int slotIndex)
        {
            if (chaCtrl == null)
                return false;

            SkinnedMeshRenderer bodyRenderer = GetBodyRenderer(chaCtrl);
            if (bodyRenderer == null)
                return false;

            var clothRenderers = GetActiveClothRenderers(chaCtrl, slotIndex);
            if (clothRenderers.Count == 0)
                return false;

            bool hasBones = clothRenderers.Any(r => r != null && r.bones != null && r.bones.Length > 0);
            return hasBones;
        }

        private int GetPotentialBoneCount(ChaControl chaCtrl)
        {
            return GetPotentialBoneCount(chaCtrl, _slotIndex);
        }

        private int GetPotentialBoneCount(ChaControl chaCtrl, int slotIndex)
        {
            var clothRenderers = GetActiveClothRenderers(chaCtrl, slotIndex);
            int count = 0;
            foreach (var renderer in clothRenderers)
            {
                if (renderer == null || renderer.bones == null)
                    continue;
                foreach (var bone in renderer.bones)
                {
                    if (bone == null)
                        continue;
                    string name = NormalizeBoneName(bone.name);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (!IsAdjustableBoneName(name))
                        continue;
                    count++;
                    if (count > 0)
                        return count;
                }
            }
            return count;
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
        
        private SkinnedMeshRenderer GetBodyRenderer(ChaControl chaCtrl)
        {
            if (chaCtrl == null || chaCtrl.objBody == null)
                return null;

            var renderers = chaCtrl.objBody.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers == null || renderers.Length == 0)
                return null;

            SkinnedMeshRenderer best = null;
            int bestCount = -1;
            foreach (var r in renderers)
            {
                if (r == null || r.bones == null)
                    continue;
                if (r.bones.Length > bestCount)
                {
                    best = r;
                    bestCount = r.bones.Length;
                }
            }
            return best;
        }

        private List<SkinnedMeshRenderer> GetActiveClothRenderers(ChaControl chaCtrl, int slotIndex)
        {
            var results = new List<SkinnedMeshRenderer>();
            var clothes = GetClothesObjects(chaCtrl);
            if (clothes == null)
                return results;

            int clothesIndex = slotIndex;
            if (clothesIndex < 0 || clothesIndex >= clothes.Length)
                return results;

            for (int i = 0; i < clothes.Length; i++)
            {
                var go = clothes[i];
                if (i != clothesIndex)
                    continue;
                if (go == null)
                    continue;

                var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers != null && renderers.Length > 0)
                    results.AddRange(renderers);
            }

            return results;
        }

        private List<SkinnedMeshRenderer> GetActiveClothRenderers(ChaControl chaCtrl)
        {
            return GetActiveClothRenderers(chaCtrl, _slotIndex);
        }

        

        private HashSet<string> GetActiveClothBoneNames(ChaControl chaCtrl, int slotIndex)
        {
            if (chaCtrl == null)
                return null;

            var renderers = GetActiveClothRenderers(chaCtrl, slotIndex);
            if (renderers == null || renderers.Count == 0)
                return new HashSet<string>();

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in renderers)
            {
                if (r == null || r.bones == null)
                    continue;
                foreach (var bone in r.bones)
                {
                    if (bone == null)
                        continue;
                    string name = NormalizeBoneName(bone.name);
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }
            return names;
        }

        private void DrawClothSlotFilter()
        {
            GUILayout.Label("<color=orange>Cloth Slots</color>", RichLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_slotIndex == 0, ClothSlotLabels[0], GUI.skin.button, GUILayout.Width(70))) _slotIndex = 0;
            if (GUILayout.Toggle(_slotIndex == 1, ClothSlotLabels[1], GUI.skin.button, GUILayout.Width(70))) _slotIndex = 1;
            if (GUILayout.Toggle(_slotIndex == 2, ClothSlotLabels[2], GUI.skin.button, GUILayout.Width(60))) _slotIndex = 2;
            if (GUILayout.Toggle(_slotIndex == 3, ClothSlotLabels[3], GUI.skin.button, GUILayout.Width(70))) _slotIndex = 3;
            if (GUILayout.Toggle(_slotIndex == 4, ClothSlotLabels[4], GUI.skin.button, GUILayout.Width(70))) _slotIndex = 4;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_slotIndex == 5, ClothSlotLabels[5], GUI.skin.button, GUILayout.Width(150))) _slotIndex = 5;
            if (GUILayout.Toggle(_slotIndex == 6, ClothSlotLabels[6], GUI.skin.button, GUILayout.Width(70))) _slotIndex = 6;
            GUILayout.EndHorizontal();
        }

        private GameObject[] GetClothesObjects(ChaControl chaCtrl)
        {
            if (chaCtrl == null)
                return null;

            var field = typeof(ChaControl).GetField("objClothes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(chaCtrl) as GameObject[];

            var prop = typeof(ChaControl).GetProperty("objClothes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
                return prop.GetValue(chaCtrl, null) as GameObject[];

            return null;
        }

        private float ReadFloat(XmlNode node, string attrName, float fallback = 0f)
        {
            if (node == null || node.Attributes == null)
                return fallback;
            var attr = node.Attributes[attrName];
            if (attr == null)
                return fallback;
            if (float.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
            return fallback;
        }

        // 저장/비교를 위해 본 이름을 정규화한다.
        private string NormalizeBoneName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            // 제어문자 제거 및 트림
            // Remove control characters and trim whitespace.
            char[] buffer = new char[name.Length];
            int idx = 0;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c <= 0x1F || c == 0x7F)
                    continue;
                buffer[idx++] = c;
            }
            string cleaned = new string(buffer, 0, idx).Trim();
            if (string.IsNullOrEmpty(cleaned))
                return cleaned;
            // Strip transfer/clone suffixes so saved names match live bones.
            const string transferSuffix = "_CTS";
            const string cloneToken = "_CTClone_";
            if (cleaned.EndsWith(transferSuffix, StringComparison.Ordinal))
                cleaned = cleaned.Substring(0, cleaned.Length - transferSuffix.Length);
            int cloneIndex = cleaned.IndexOf(cloneToken, StringComparison.Ordinal);
            if (cloneIndex >= 0)
                cleaned = cleaned.Substring(0, cloneIndex);
            return cleaned;
        }

        // 선택된 본 위치에 디버그 마커를 배치한다.
        private void UpdateSelectedBoneHighlight()
        {
            var mapData = EnsureCurrentMapData();
            if (mapData == null)
            {
                ClearSelectedBoneHighlight();
                return;
            }

            var entries = GetOrCreateTransferEntriesFor(mapData.ociChar != null ? mapData.ociChar.GetChaControl() : null, _slotIndex);
            int selectedIndex = GetSelectedTransferIndex(mapData, _slotIndex);
            if (selectedIndex < 0 || selectedIndex >= entries.Count)
            {
                ClearSelectedBoneHighlight();
                return;
            }

            var entry = entries[selectedIndex];
            if (entry == null || entry.transfer == null)
            {
                ClearSelectedBoneHighlight();
                return;
            }

            EnsureSelectedBoneMarker();
            _selectedBoneMarker.transform.SetParent(entry.transfer, false);
            _selectedBoneMarker.transform.localPosition = Vector3.zero;
            _selectedBoneMarker.transform.localRotation = Quaternion.identity;
            _selectedBoneMarker.transform.localScale = Vector3.one * _selectedBoneMarkerScale;
            _selectedBoneMarker.SetActive(true);
        }

        private void EnsureSelectedBoneMarker()
        {
            if (_selectedBoneMarker != null)
                return;

            var marker = new GameObject("CTS_SelectedBoneMarker");
            marker.hideFlags = HideFlags.DontSave;
            if (_selectedBoneMaterial == null)
                _selectedBoneMaterial = CreateSelectedBoneMaterial();
            CreateWireSphere(marker.transform);

            _selectedBoneMarker = marker;
        }

        private Material CreateSelectedBoneMaterial()
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                return null;
            var mat = new Material(shader);
            mat.color = Color.green;
            return mat;
        }

        // 축별 원을 조합해 구형 와이어프레임을 만든다.
        private void CreateWireSphere(Transform parent)
        {
            CreateWireCircle(parent, "Wire_XY", Vector3.forward);
            CreateWireCircle(parent, "Wire_XZ", Vector3.up);
            CreateWireCircle(parent, "Wire_YZ", Vector3.right);
        }

        // LineRenderer로 원을 그린다.
        private void CreateWireCircle(Transform parent, string name, Vector3 normal)
        {
            var go = new GameObject(name);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = _selectedBoneMarkerSegments;
            lr.startWidth = _selectedBoneMarkerLineWidth;
            lr.endWidth = _selectedBoneMarkerLineWidth;
            if (_selectedBoneMaterial != null)
                lr.sharedMaterial = _selectedBoneMaterial;

            Quaternion rot = Quaternion.FromToRotation(Vector3.forward, normal);
            for (int i = 0; i < _selectedBoneMarkerSegments; i++)
            {
                float t = (float)i / _selectedBoneMarkerSegments * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(t), Mathf.Sin(t), 0f);
                lr.SetPosition(i, rot * p);
            }
        }

        private void ClearSelectedBoneHighlight()
        {
            if (_selectedBoneMarker != null)
            {
                _selectedBoneMarker.SetActive(false);
                _selectedBoneMarker.transform.SetParent(null, false);
            }
        }

        internal class RendererBoneRef
        {
            public SkinnedMeshRenderer renderer;
            public int boneIndex;
            public Transform originalBone;
        }

        internal class TransferEntry
        {
            public string boneName;
            public Transform bodyBone;
            public Transform transfer;
            public List<RendererBoneRef> refs = new List<RendererBoneRef>();
        }

        internal struct SavedAdjustment
        {
            public Vector3 position;
            public Vector3 rotation;
            public Vector3 scale;
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

        [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        private static class ChaControl_ChangeClothes_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
            {
                OCIChar ociChar = __instance.GetOCIChar();

                if (ociChar != null)
                {
                    if (_self == null || !_self._loaded)
                        return;

                    if (!_self.TryResolveClothSlotIndex(kind, out int slotIndex))
                        return;

                    // Clothing changed: reset all map data and rebuild from new outfit.
                    _self.ResetAllMapDataForClothesChange(__instance);

                    if (_self._ShowUI && _self.TryMarkPendingAutoRemap(__instance, slotIndex))
                        _self.StartCoroutine(_self.AutoMapDelayedForSlot(__instance, slotIndex, true));
                }
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
                    if (_self == null || !_self._loaded)
                        return;

                    ChaControl chaCtrl = ociChar.GetChaControl();
                    if (chaCtrl == null)
                        return;

                    // Character swapped: clear all map data for this character.
                    _self.ResetAllMapDataForClothesChange(chaCtrl);

                    if (_self._ShowUI && _self._currentOCIChar == ociChar)
                        _self.RefreshMappingsForSelection(ociChar);
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
                    if (_self == null || !_self._loaded)
                        return;

                    // Accessory visibility can change active renderers/bones; reset all cached map data.
                    _self.ResetAllMapDataForClothesChange(__instance);

                    if (_self._ShowUI && _self.IsCurrentChar(__instance))
                    {
                        _self.RefreshMappingsForSelection(ociChar);
                        int slotIndex = _self._slotIndex;
                        var entries = _self.GetOrCreateTransferEntriesFor(__instance, slotIndex);
                        if ((entries == null || entries.Count == 0) && _self.TryMarkPendingAutoRemap(__instance, slotIndex))
                            _self.StartCoroutine(_self.AutoMapDelayedForSlot(__instance, slotIndex, true));
                    }
                }
            }
        }        

        #endregion        
    }
#endregion
}
