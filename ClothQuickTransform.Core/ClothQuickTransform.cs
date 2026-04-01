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
using static CharaUtils.Expression;

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

        private Rect _windowRect = new Rect(140, 10, 300, 10);

        internal OCIChar _currentOCIChar = null;
        private int _mappedCharKey = int.MinValue;
        private Vector2 _transferScroll;
        private int _selectedTransferIndex = -1;
        private readonly List<TransferEntry> _transferEntries = new List<TransferEntry>();
        internal readonly HashSet<int> _pendingAutoRemap = new HashSet<int>();
        private readonly Dictionary<int, Dictionary<string, SavedAdjustment>> _perCharAdjustments =
            new Dictionary<int, Dictionary<string, SavedAdjustment>>();
        private GameObject _selectedBoneMarker;
        private Material _selectedBoneMaterial;
        private const float _selectedBoneMarkerScale = 1.0f;
        private const int _selectedBoneMarkerSegments = 24;
        private const float _selectedBoneMarkerLineWidth = 0.02f;
    
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

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
#if FEATURE_SCENE_SAVE
#if HONEYSELECT
            HSExtSave.HSExtSave.RegisterHandler("timeline", null, null, this.SceneLoad, this.SceneImport, this.SceneWrite, null, null);
#else
            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingImported += OnSceneImport;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingSaved += OnSceneSave;
#endif
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

#if FEATURE_SCENE_SAVE
        private void OnSceneLoad(string path)
        {
            PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
            if (data == null)
                return;
            if (data.data != null && data.data.ContainsKey("sceneInfo"))
                UnityEngine.Debug.Log($">> CTS SceneLoad xml:\n{data.data["sceneInfo"]}");
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
            if (data.data != null && data.data.ContainsKey("sceneInfo"))
                UnityEngine.Debug.Log($">> CTS SceneImport xml:\n{data.data["sceneInfo"]}");
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
                data.version = _saveVersion;
                data.data.Add("sceneInfo", stringWriter.ToString());

                UnityEngine.Debug.Log($">> CTS SceneSave xml:\n{stringWriter}");

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

        private void SceneImport(string path, XmlNode node)
        {
            Dictionary<int, ObjectCtrlInfo> toIgnore = new Dictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
            this.ExecuteDelayed2(() =>
            {
                List<KeyValuePair<int, ObjectCtrlInfo>> dic = Studio.Studio.Instance.dicObjectCtrl
                    .Where(e => toIgnore.ContainsKey(e.Key) == false)
                    .OrderBy(e => SceneInfo_Import_Patches._newToOldKeys[e.Key])
                    .ToList();

                List<OCIChar> ociChars = dic
                    .Select(kv => kv.Value as OCIChar)
                    .Where(c => c != null)
                    .ToList();

                SceneRead(node, ociChars);
            }, 20);
        }

        private void SceneRead(XmlNode node, List<OCIChar> ociChars)
        {
            UnityEngine.Debug.Log($">> SceneRead in ClothQuickTransform");

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
                    continue;

                var saved = new Dictionary<string, SavedAdjustment>();
                foreach (XmlNode transferNode in charNode.SelectNodes("transfer"))
                {
                    string boneName = NormalizeBoneName(transferNode.Attributes["name"]?.Value);
                    if (string.IsNullOrEmpty(boneName))
                        continue;

                    var adj = new SavedAdjustment
                    {
                        position = new Vector3(
                            ReadFloat(transferNode, "posX"),
                            ReadFloat(transferNode, "posY"),
                            ReadFloat(transferNode, "posZ")),
                        scale = new Vector3(
                            ReadFloat(transferNode, "scaleX", 1f),
                            ReadFloat(transferNode, "scaleY", 1f),
                            ReadFloat(transferNode, "scaleZ", 1f))
                    };

                    saved[boneName] = adj;
                }

                int dicKey;
                bool hasDicKey = TryGetDicKey(ociChar.GetChaControl(), out dicKey);
                UnityEngine.Debug.Log($">> SceneRead char dicKey={(hasDicKey ? dicKey.ToString() : "not-found")} savedTransfers={saved.Count}");

                StoreAdjustmentsFor(ociChar.GetChaControl(), saved);

                restoredChars++;
                restoredTransfers += saved.Count;
            }

            UnityEngine.Debug.Log($">> SceneRead summary chars={restoredChars} transfers={restoredTransfers}");
        }

        private void SceneWrite(string path, XmlTextWriter writer)
        {
            UnityEngine.Debug.Log($">> SceneWrite in ClothQuickTransform");

            var dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
            var ociCharByDicKey = dic
                .Where(kv => kv.Value is OCIChar)
                .ToDictionary(kv => kv.Key, kv => kv.Value as OCIChar);
            var activeDicKeys = new HashSet<int>(ociCharByDicKey.Keys);

            UnityEngine.Debug.Log($">> SceneWrite adjustments chars={_perCharAdjustments.Count} activeChars={activeDicKeys.Count}");

            foreach (var kvp in _perCharAdjustments)
            {
                OCIChar ociChar = null;
                int dicKey = kvp.Key;

                if (!activeDicKeys.Contains(dicKey))
                {
                    // Fallback for legacy keys: try to match by ChaControl hash
                    ociChar = ociCharByDicKey.Values.FirstOrDefault(c => c != null && c.GetChaControl().GetHashCode() == kvp.Key);
                    if (ociChar == null)
                        continue;
                    dicKey = ociCharByDicKey.First(kv => kv.Value == ociChar).Key;
                }
                else
                {
                    ociChar = ociCharByDicKey[dicKey];
                }

                var map = kvp.Value;
                if (map == null || map.Count == 0)
                    continue;

                UnityEngine.Debug.Log($">> SceneWrite char dicKey={dicKey} transfers={map.Count}");

                writer.WriteStartElement("character");
                writer.WriteAttributeString("dicKey", dicKey.ToString(CultureInfo.InvariantCulture));
                if (ociChar != null)
                    writer.WriteAttributeString("hash", ociChar.GetChaControl().GetHashCode().ToString(CultureInfo.InvariantCulture));

                foreach (var entry in map)
                {
                    writer.WriteStartElement("transfer");
                    writer.WriteAttributeString("name", entry.Key);
                    writer.WriteAttributeString("posX", entry.Value.position.x.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("posY", entry.Value.position.y.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("posZ", entry.Value.position.z.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("scaleX", entry.Value.scale.x.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("scaleY", entry.Value.scale.y.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("scaleZ", entry.Value.scale.z.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }
        }
#endif

        protected override void Update()
        {
            if (_loaded == false)
                return;
        }

        protected override void LateUpdate()
        {
        }

        protected override void OnGUI()
        {
            if (_ShowUI == false)
            {
                ClearSelectedBoneHighlight();
                return;
            }

            if (StudioAPI.InsideStudio)
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "ClothQuickTransform " + Version);
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

            var chaCtrl = GetCurrentChaControl();
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
                    ClearMappings();
                }
                GUILayout.EndHorizontal();

                draw_seperate();

                _transferScroll = GUILayout.BeginScrollView(_transferScroll, GUI.skin.box, GUILayout.Height(180));
                int prevSelectedIndex = _selectedTransferIndex;
                for (int i = 0; i < _transferEntries.Count; i++)
                {
                    var entry = _transferEntries[i];
                    string label = entry != null ? entry.boneName : "(null)";
                    if (GUILayout.Toggle(_selectedTransferIndex == i, label, GUI.skin.button))
                    {
                        _selectedTransferIndex = i;
                    }
                }
                GUILayout.EndScrollView();
                if (prevSelectedIndex != _selectedTransferIndex)
                    UpdateSelectedBoneHighlight();

                if (_selectedTransferIndex >= 0 && _selectedTransferIndex < _transferEntries.Count)
                {
                    var entry = _transferEntries[_selectedTransferIndex];
                    if (entry != null && entry.transfer != null)
                    {
                        GUILayout.Label("<color=orange>Position</color>", RichLabel);
                        Vector3 pos = entry.transfer.localPosition;
                        pos.x = SliderRow("Pos X", pos.x, -1.0f, 1.0f);
                        pos.y = SliderRow("Pos Y", pos.y, -1.0f, 1.0f);
                        pos.z = SliderRow("Pos Z", pos.z, -1.0f, 1.0f);
                        entry.transfer.localPosition = pos;

                        GUILayout.Label("<color=orange>Scale</color>", RichLabel);
                        Vector3 scale = entry.transfer.localScale;
                        scale.x = SliderRow("Scale X", scale.x, 0.2f, 2.0f);
                        scale.y = SliderRow("Scale Y", scale.y, 0.2f, 2.0f);
                        scale.z = SliderRow("Scale Z", scale.z, 0.2f, 2.0f);
                        entry.transfer.localScale = scale;

                        StoreAdjustmentsFor(chaCtrl, CaptureCurrentAdjustments());

                        if (GUILayout.Button("Reset"))
                        {
                            entry.transfer.localPosition = Vector3.zero;
                            entry.transfer.localScale = Vector3.one;
                            StoreAdjustmentsFor(chaCtrl, CaptureCurrentAdjustments());
                        }
                    }
                }
            }

            if (GUILayout.Button("Close")) {
                Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
                _ShowUI = false;
            }

            // ⭐ Tooltip
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

        private void Init()
        {
            _loaded = true;
        }

        internal float SliderRow(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(value.ToString("0.00"), GUILayout.Width(40));
            GUILayout.EndHorizontal();
            return value;
        }

        internal IEnumerator AutoMapDelayed(ChaControl chaCtrl, int key)
        {
            int frameCount = 15;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            try
            {
                AutoMap(chaCtrl);
            }
            finally
            {
                _pendingAutoRemap.Remove(key);
            }
        }

        internal ChaControl GetCurrentChaControl()
        {
            if (_currentOCIChar == null)
                return null;
            return _currentOCIChar.GetChaControl();
        }

        internal void RefreshMappingsForSelection(OCIChar ociChar)
        {
            if (ociChar == null)
                return;

            var chaCtrl = ociChar.GetChaControl();
            if (chaCtrl == null)
                return;

            if (!CanAutoMap(chaCtrl))
                return;

            Dictionary<string, SavedAdjustment> saved = null;
            if (TryGetDicKey(chaCtrl, out int dicKey) && _perCharAdjustments.TryGetValue(dicKey, out var map))
                saved = map;
            else if (_perCharAdjustments.TryGetValue(chaCtrl.GetHashCode(), out var map2))
                saved = map2;

            ClearMappings();
            AutoMapWithSaved(chaCtrl, saved);
            _mappedCharKey = GetCharKey(chaCtrl);
        }

        internal void AutoMap(ChaControl chaCtrl)
        {
            if (!CanAutoMap(chaCtrl))
            {
                int key = GetCharKey(chaCtrl);
                if (_pendingAutoRemap.Add(key))
                    StartCoroutine(AutoMapDelayed(chaCtrl, key));
                return;
            }

            int potential = GetPotentialBoneCount(chaCtrl);
            if (potential == 0)
            {
                return;
            }

            var saved = GetSavedFor(chaCtrl) ?? CaptureCurrentAdjustments();
            ClearMappings();
            AutoMapWithSaved(chaCtrl, saved);
        }

        private void AutoMapWithSaved(ChaControl chaCtrl, Dictionary<string, SavedAdjustment> saved)
        {
            if (chaCtrl == null)
                return;

            SkinnedMeshRenderer bodyRenderer = GetBodyRenderer(chaCtrl);
            if (bodyRenderer == null)
            {
                UnityEngine.Debug.Log($">> AutoMapWithSaved: bodyRenderer null");
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
            UnityEngine.Debug.Log($">> AutoMapWithSaved: bodyBones={bodyBonesByName.Count}");

            var clothRenderers = GetActiveClothRenderers(chaCtrl);
            if (clothRenderers.Count == 0)
            {
                UnityEngine.Debug.Log($">> AutoMapWithSaved: no cloth renderers");
                return;
            }

            int savedCount = saved != null ? saved.Count : 0;
            UnityEngine.Debug.Log($">> AutoMapWithSaved: clothRenderers={clothRenderers.Count} savedTransfers={savedCount}");

            if (saved != null && savedCount > 0)
            {
                string[] sampleSaved = saved.Keys.Take(5).ToArray();
                UnityEngine.Debug.Log($">> AutoMapWithSaved: saved sample={string.Join(",", sampleSaved)}");
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
                        _transferEntries.Add(entry);
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

            if (transferByName.Count > 0)
            {
                string[] sampleTransfer = transferByName.Keys.Take(5).ToArray();
                UnityEngine.Debug.Log($">> AutoMapWithSaved: transfer sample={string.Join(",", sampleTransfer)}");
            }

            int matched = 0;
            if (saved != null && savedCount > 0)
            {
                var savedSet = new HashSet<string>(saved.Keys);
                matched = transferByName.Keys.Count(k => savedSet.Contains(k));
            }
            UnityEngine.Debug.Log($">> AutoMapWithSaved: appliedTransfers={applied} matchedByName={matched} totalTransfers={_transferEntries.Count}");

            if (saved != null && savedCount > 0)
            {
                string sampleSaved = saved.Keys.FirstOrDefault();
                UnityEngine.Debug.Log($">> AutoMapWithSaved: saved name detail={DescribeName(sampleSaved)}");
            }
            if (transferByName.Count > 0)
            {
                string sampleTransfer = transferByName.Keys.FirstOrDefault();
                UnityEngine.Debug.Log($">> AutoMapWithSaved: transfer name detail={DescribeName(sampleTransfer)}");
            }

            UnityEngine.Debug.Log($">> saved: {saved}: savedCount: {savedCount}, transferByName.Count: {transferByName.Count}");

            if (saved != null && savedCount > 0 && transferByName.Count > 0)
            {
                string sampleSaved = saved.Keys.FirstOrDefault();
                string sampleTransfer = transferByName.Keys.FirstOrDefault();
                bool eq = string.Equals(sampleSaved, sampleTransfer, StringComparison.Ordinal);
                bool savedHasTransfer = saved.ContainsKey(sampleTransfer);
                bool transferHasSaved = transferByName.ContainsKey(sampleSaved);
                UnityEngine.Debug.Log($">> AutoMapWithSaved: compare eq={eq} savedHasTransfer={savedHasTransfer} transferHasSaved={transferHasSaved}");
            }

            _selectedTransferIndex = _transferEntries.Count > 0 ? 0 : -1;
            StoreAdjustmentsFor(chaCtrl, CaptureCurrentAdjustments());
            UpdateSelectedBoneHighlight();
        }

        private Dictionary<string, SavedAdjustment> CaptureCurrentAdjustments()
        {
            var map = new Dictionary<string, SavedAdjustment>();
            foreach (var entry in _transferEntries)
            {
                if (entry == null || entry.transfer == null || string.IsNullOrEmpty(entry.boneName))
                    continue;
                string name = NormalizeBoneName(entry.boneName);
                if (string.IsNullOrEmpty(name))
                    continue;
                map[name] = new SavedAdjustment
                {
                    position = entry.transfer.localPosition,
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
                entry.transfer.localScale = adj.scale;
            }
        }

        private void ClearMappings()
        {
            foreach (var entry in _transferEntries)
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

            _transferEntries.Clear();
            _selectedTransferIndex = -1;
            ClearSelectedBoneHighlight();
        }

        private void StoreAdjustmentsFor(ChaControl chaCtrl, Dictionary<string, SavedAdjustment> map)
        {
            if (chaCtrl == null || map == null)
                return;
            if (TryGetDicKey(chaCtrl, out int dicKey))
                _perCharAdjustments[dicKey] = map;
            else
                _perCharAdjustments[chaCtrl.GetHashCode()] = map;
        }

        private int GetCharKey(ChaControl chaCtrl)
        {
            if (chaCtrl == null)
                return int.MinValue;

            if (TryGetDicKey(chaCtrl, out int dicKey))
                return dicKey;

            return chaCtrl.GetHashCode();
        }

        private Dictionary<string, SavedAdjustment> GetSavedFor(ChaControl chaCtrl)
        {
            if (chaCtrl == null)
                return null;

            if (TryGetDicKey(chaCtrl, out int dicKey) && _perCharAdjustments.TryGetValue(dicKey, out var map))
                return map;

            if (_perCharAdjustments.TryGetValue(chaCtrl.GetHashCode(), out var map2))
                return map2;

            return null;
        }

        private bool CanAutoMap(ChaControl chaCtrl)
        {
            if (chaCtrl == null)
                return false;

            SkinnedMeshRenderer bodyRenderer = GetBodyRenderer(chaCtrl);
            if (bodyRenderer == null)
            {
                return false;
            }

            var clothRenderers = GetActiveClothRenderers(chaCtrl);
            if (clothRenderers.Count == 0)
            {
                return false;
            }

            bool hasBones = clothRenderers.Any(r => r != null && r.bones != null && r.bones.Length > 0);
            if (!hasBones)
            {
                return false;
            }

            return true;
        }

        private int GetPotentialBoneCount(ChaControl chaCtrl)
        {
            var clothRenderers = GetActiveClothRenderers(chaCtrl);
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

            // Handle scene import where dicKeys are remapped (new -> old).
            foreach (var pair in SceneInfo_Import_Patches._newToOldKeys)
            {
                if (pair.Value == savedDicKey && map.TryGetValue(pair.Key, out oci))
                    return oci;
            }

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

        private List<SkinnedMeshRenderer> GetActiveClothRenderers(ChaControl chaCtrl)
        {
            var results = new List<SkinnedMeshRenderer>();
            var clothes = GetClothesObjects(chaCtrl);
            if (clothes == null)
                return results;

            foreach (var go in clothes)
            {
                if (go == null || !go.activeInHierarchy)
                    continue;

                var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers != null && renderers.Length > 0)
                    results.AddRange(renderers);
            }

            return results;
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

        private string NormalizeBoneName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
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

        private string DescribeName(string name)
        {
            if (name == null)
                return "null";
            var codes = new List<string>(name.Length);
            for (int i = 0; i < name.Length; i++)
                codes.Add(((int)name[i]).ToString(CultureInfo.InvariantCulture));
            return $"len={name.Length} text='{name}' codes=[{string.Join(",", codes)}]";
        }

        private void UpdateSelectedBoneHighlight()
        {
            if (_selectedTransferIndex < 0 || _selectedTransferIndex >= _transferEntries.Count)
            {
                ClearSelectedBoneHighlight();
                return;
            }

            var entry = _transferEntries[_selectedTransferIndex];
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

        private void CreateWireSphere(Transform parent)
        {
            CreateWireCircle(parent, "Wire_XY", Vector3.forward);
            CreateWireCircle(parent, "Wire_XZ", Vector3.up);
            CreateWireCircle(parent, "Wire_YZ", Vector3.right);
        }

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

        private class RendererBoneRef
        {
            public SkinnedMeshRenderer renderer;
            public int boneIndex;
            public Transform originalBone;
        }

        private class TransferEntry
        {
            public string boneName;
            public Transform bodyBone;
            public Transform transfer;
            public List<RendererBoneRef> refs = new List<RendererBoneRef>();
        }

        private struct SavedAdjustment
        {
            public Vector3 position;
            public Vector3 scale;
        }

        #endregion
    }

#region Patches
    [HarmonyPatch(typeof(SceneInfo), "Import", new[] { typeof(BinaryReader), typeof(Version) })]
    internal static class SceneInfo_Import_Patches //This is here because I fucked up the save format making it impossible to import scenes correctly
    {
        internal static readonly Dictionary<int, int> _newToOldKeys = new Dictionary<int, int>();

        private static void Prefix()
        {
            _newToOldKeys.Clear();
        }
    }
    
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
                ClothQuickTransform._self._currentOCIChar = ociChar;
                if (ClothQuickTransform._self._ShowUI)
                    ClothQuickTransform._self.RefreshMappingsForSelection(ociChar);
            }
            return true;
        }
    }
#if AISHOUJO || HONEYSELECT2
    [HarmonyPatch(typeof(ChaControl), "ChangeClothesAsync", typeof(int), typeof(int), typeof(bool), typeof(bool))]
    internal static class ChaControl_ChangeClothesAsync_Patches
    {
        private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange = false, bool asyncFlags = true)
        {
            var self = ClothQuickTransform._self;
            if (self == null || __instance == null)
                return;

            int key = __instance.GetHashCode();
            if (!self._pendingAutoRemap.Add(key))
                return;

            self.StartCoroutine(self.AutoMapDelayed(__instance, key));
        }
    }
#endif
#endregion

#endregion
}
