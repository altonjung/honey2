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

        private const string GROUP_CAPSULE_COLLIDER = "Group: (C)Colliders";
        private const string GROUP_SPHERE_COLLIDER = "Group: (S)Colliders";

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

                _windowRect = GUILayout.Window(_uniqueId + 1, _windowRect, WindowFunc, "ClothCollideVisualizer " + Version);
            }                
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
                if (controller != null) {
                    controller.CreateData(ociChar);
                }                

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

                writer.WriteStartElement("character");
                if (hasDicKey)
                    writer.WriteAttributeString("dicKey", dicKey.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("hash", ociChar.GetChaControl().GetHashCode().ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("name", ociChar.charInfo != null ? ociChar.charInfo.name : string.Empty);

                List<SphereCollider> scolliders = ociChar.charInfo.objBodyBone
                    .transform
                    .GetComponentsInChildren<SphereCollider>()
                    .OrderBy(col => col.gameObject.name) // 이름순 정렬
                    .ToList();

                List<Transform> targetCollider = new List<Transform>();

                foreach (var col in scolliders)
                {
                    if (col == null) continue;

                    if (col.gameObject.name.Contains("Cloth colliders"))
                    {
                        targetCollider.Add(col.gameObject.transform);
                    }
                }

                // capsule collider
                List<CapsuleCollider> ccolliders = ociChar.charInfo.objBodyBone
                    .transform
                    .GetComponentsInChildren<CapsuleCollider>(true)
                    .OrderBy(col => col.gameObject.name)
                    .ToList();

                foreach (var col in ccolliders)
                {

                    if (col == null) continue;

                    if (col.gameObject.name.Contains("Cloth colliders"))
                    {
                        targetCollider.Add(col.gameObject.transform);
                    }
                }

                string collider_name = "";
                foreach (Transform collider in targetCollider)
                {
                    int idx = collider.name.IndexOf('-');
                    if (idx >= 0)
                        collider_name = collider.name.Substring(0, idx);
                    else
                        collider_name = collider.name;

                    writer.WriteStartElement("bone");
                    writer.WriteAttributeString("name", collider_name);

                    // position
                    writer.WriteStartElement("position");
                    writer.WriteAttributeString("x", collider.localPosition.x.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("y", collider.localPosition.y.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("z", collider.localPosition.z.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();

                    // rotation
                    writer.WriteStartElement("rotation");
                    writer.WriteAttributeString("x", collider.localEulerAngles.x.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("y", collider.localEulerAngles.y.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("z", collider.localEulerAngles.z.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();

                    // scale
                    writer.WriteStartElement("scale");
                    writer.WriteAttributeString("x", collider.localScale.x.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("y", collider.localScale.y.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("z", collider.localScale.z.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();

                    writer.WriteEndElement(); // collider
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
                if (!physicCollider.visualColliderAdded)
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

                _debugScroll = GUILayout.BeginScrollView(_debugScroll, GUI.skin.box, GUILayout.Height(180));
                for (int i = 0; i < physicCollider.debugEntries.Count; i++)
                {
                    var entry = physicCollider.debugEntries[i];
                    string label = entry != null ? entry.name : "(null)";
                    bool isModified = IsEntryModified(entry);
                    bool isAdjustableEntry = (entry != null && entry.source != null && IsAdjustableCollider(entry.source))
                        || (!string.IsNullOrEmpty(label) && label.IndexOf("Adjustable", StringComparison.OrdinalIgnoreCase) >= 0);
                    Color prevContentColor = GUI.contentColor;
                    GUI.contentColor = isAdjustableEntry
                        ? new Color(1f, 0.55f, 0f, 1f)
                        : (isModified ? ModifiedEntryColor : UnmodifiedEntryColor);
                    if (GUILayout.Toggle(_selectedDebugIndex == i, label, GUI.skin.button))
                    {
                        if (_selectedDebugIndex != i)
                        {
                            _selectedDebugIndex = i;
                            _selectedDebugEntry = entry;
                            SyncDebugFromSource(entry);
                            if (entry != null && entry.source != null)
                                HighlightSelectedCollider(entry.source, physicCollider.debugCollideRenderers);
                        }
                    }
                    GUI.contentColor = prevContentColor;
                }
                GUILayout.EndScrollView();

                if (_selectedDebugEntry != null && _selectedDebugEntry.debugTransform != null)
                {
                    Collider collider = _selectedDebugEntry.source;
                    string colliderType = collider is CapsuleCollider ? "Capsule" : (collider is SphereCollider ? "Sphere" : "Collider");
                    bool isSelectedModified = IsEntryModified(_selectedDebugEntry);
                    string statusText = isSelectedModified ? "<color=red>Modified</color>" : "<color=#7CFC00>Unmodified</color>";

                    GUILayout.Label($"<color=orange>{colliderType}</color>: {collider.name}", RichLabel);
                    GUILayout.Label($"Status: {statusText}", RichLabel);
                    draw_seperate();

                    Transform debugTr = _selectedDebugEntry.debugTransform;

                    GUILayout.Label("<color=orange>Position</color>", RichLabel);
                    DrawStepSelector(ref _posStepIndex, "Step");
                    Vector3 pos = debugTr.localPosition;
                    float posStep = SliderStepOptions[_posStepIndex];
                    pos.x = SliderRow("Pos X", pos.x, -2.0f, 2.0f, 0.0f, posStep);
                    pos.y = SliderRow("Pos Y", pos.y, -2.0f, 2.0f, 0.0f, posStep);
                    pos.z = SliderRow("Pos Z", pos.z, -2.0f, 2.0f, 0.0f, posStep);
                    debugTr.localPosition = pos;

                    GUILayout.Label("<color=orange>Rotation</color>", RichLabel);
                    DrawStepSelector(ref _rotStepIndex, "Step");
                    Vector3 rot = debugTr.localEulerAngles;
                    rot.x = NormalizeAngle(rot.x);
                    rot.y = NormalizeAngle(rot.y);
                    rot.z = NormalizeAngle(rot.z);
                    float rotStep = SliderStepOptions[_rotStepIndex];
                    rot.x = SliderRow("Rot X", rot.x, -180.0f, 180.0f, 0.0f, rotStep);
                    rot.y = SliderRow("Rot Y", rot.y, -180.0f, 180.0f, 0.0f, rotStep);
                    rot.z = SliderRow("Rot Z", rot.z, -180.0f, 180.0f, 0.0f, rotStep);
                    debugTr.localEulerAngles = rot;

                    GUILayout.Label("<color=orange>Scale</color>", RichLabel);
                    DrawStepSelector(ref _scaleStepIndex, "Step");
                    Vector3 scale = debugTr.localScale;
                    float scaleStep = SliderStepOptions[_scaleStepIndex];
                    scale.x = SliderRow("Scale X", scale.x, 0.1f, 2.0f, 1.0f, scaleStep);
                    scale.y = SliderRow("Scale Y", scale.y, 0.1f, 2.0f, 1.0f, scaleStep);
                    scale.z = SliderRow("Scale Z", scale.z, 0.1f, 2.0f, 1.0f, scaleStep);
                    debugTr.localScale = scale;

                    ApplyDebugToSource(_selectedDebugEntry);

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Reset All"))
                    {
                        debugTr.localPosition = Vector3.zero;
                        debugTr.localEulerAngles = Vector3.zero;
                        debugTr.localScale = Vector3.one;
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

            controller.RemovePhysicCollier();
            CleanupVisualsByName(ociChar);
            _selectedDebugEntry = null;
            _selectedDebugIndex = -1;

            AddVisualColliders(ociChar);
        }

        private void ForceRemoveVisualColliders(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.GetChaControl() == null)
                return;

            var controller = _self.GetControl(ociChar);
            if (controller == null)
                return;

            controller.RemovePhysicCollier();
            CleanupVisualsByName(ociChar);
            _selectedDebugEntry = null;
            _selectedDebugIndex = -1;
            PhysicCollider physicCollider = controller.GetData();
            if (physicCollider != null)
                physicCollider.visualColliderAdded = false;

        }


        private float SliderRow(string label, float value, float min, float max, float resetValue, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            if (GUILayout.Button("-", GUILayout.Width(22)))
                value -= step;
            value = GUILayout.HorizontalSlider(value, min, max);
            if (GUILayout.Button("+", GUILayout.Width(22)))
                value += step;
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
        }

        private void ApplyDebugToSource(DebugColliderEntry entry)
        {
            if (entry == null || entry.source == null || entry.debugTransform == null)
                return;

            Transform src = entry.source.transform;
            src.localPosition = entry.debugTransform.localPosition;
            src.localEulerAngles = entry.debugTransform.localEulerAngles;
            src.localScale = entry.debugTransform.localScale;
        }

        private void ResetDebugToBaseline(DebugColliderEntry entry)
        {
            if (entry == null || entry.debugTransform == null)
                return;

            entry.debugTransform.localPosition = entry.baselineLocalPosition;
            entry.debugTransform.localEulerAngles = entry.baselineLocalEuler;
            entry.debugTransform.localScale = entry.baselineLocalScale;
        }

        private bool IsEntryModified(DebugColliderEntry entry)
        {
            if (entry == null || entry.debugTransform == null)
                return false;

            return !ApproximatelyVector(entry.debugTransform.localPosition, Vector3.zero)
                || !ApproximatelyEuler(entry.debugTransform.localEulerAngles, Vector3.zero)
                || !ApproximatelyVector(entry.debugTransform.localScale, Vector3.one);
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
                
            if (ociChar != null && _ShowUI)
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
            root.transform.localPosition = capsule.center;
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
            // root.transform.localPosition = sphere.center;
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
            Vector3 textPos = sphere.center + Vector3.up * (radius + 0.05f);
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
                    
                    if (ociChar.charInfo.objBodyBone)
                    {
                        List<SphereCollider> spherecolliders = ociChar.charInfo.objBodyBone.transform.GetComponentsInChildren<SphereCollider>().OrderBy(col => col.gameObject.name).ToList();
                        List<CapsuleCollider> capsulecolliders = ociChar.charInfo.objBodyBone.transform.GetComponentsInChildren<CapsuleCollider>().OrderBy(col => col.gameObject.name).ToList();
        
                        foreach (var col in spherecolliders.OrderBy(col => col.gameObject.name).ToList())
                        {
                            if (col == null) continue;

                            if (col.gameObject.name.Contains("Cloth colliders"))
                            {
                                string trim_name = col.gameObject.name.Replace("Cloth colliders support_", "").Trim();
                                string collider_name;

                                idx = trim_name.IndexOf('-');
                                if (idx >= 0)
                                    collider_name = trim_name.Substring(0, idx);
                                else
                                    collider_name = trim_name;

                                GameObject debugRoot = new GameObject(collider_name + "_Debug");
                                debugRoot.transform.SetParent(col.transform.parent, false);
                                debugRoot.transform.localPosition = col.transform.localPosition;
                                debugRoot.transform.localRotation = col.transform.localRotation;
                                debugRoot.transform.localScale = col.transform.localScale;

                                var entry = new DebugColliderEntry
                                {
                                    name = collider_name,
                                    source = col,
                                    debugTransform = debugRoot.transform,
                                    baselineLocalPosition = debugRoot.transform.localPosition,
                                    baselineLocalEuler = debugRoot.transform.localEulerAngles,
                                    baselineLocalScale = debugRoot.transform.localScale
                                };
                                physicCollider.debugEntries.Add(entry);
                                physicCollider.debugEntryBySource[col] = entry;

                                // SphereCollider 시각화 생성
                                CreateSphereWireframe(col, debugRoot.transform, collider_name, physicCollider.debugSphereCollideVisibleObjects, physicCollider.debugCollideRenderers);
                            }
                        }

                        foreach (var col in capsulecolliders.OrderBy(col => col.gameObject.name).ToList())
                        {
                            if (col == null) continue; // Destroy된 콜라이더는 건너뛴다.

                            if (col.gameObject.name.Contains("Cloth colliders"))
                            {
                                string trim_name = col.gameObject.name.Replace("Cloth colliders support_", "").Trim();
                                string collider_name;
                                idx = trim_name.IndexOf('-');
                                if (idx >= 0)
                                    collider_name = trim_name.Substring(0, idx);
                                else
                                    collider_name = trim_name;

                                GameObject debugRoot = new GameObject(collider_name + "_Debug");
                                debugRoot.transform.SetParent(col.transform.parent, false);
                                debugRoot.transform.localPosition = col.transform.localPosition;
                                debugRoot.transform.localRotation = col.transform.localRotation;
                                debugRoot.transform.localScale = col.transform.localScale;

                                var entry = new DebugColliderEntry
                                {
                                    name = collider_name,
                                    source = col,
                                    debugTransform = debugRoot.transform,
                                    baselineLocalPosition = debugRoot.transform.localPosition,
                                    baselineLocalEuler = debugRoot.transform.localEulerAngles,
                                    baselineLocalScale = debugRoot.transform.localScale
                                };
                                physicCollider.debugEntries.Add(entry);
                                physicCollider.debugEntryBySource[col] = entry;

                                // CapsuleCollider 시각화 생성
                                CreateCapsuleWireframe(col, debugRoot.transform, collider_name, physicCollider.debugCapsuleCollideVisibleObjects, physicCollider.debugCollideRenderers);
                            }
                        }
                    }

                    SetDebugVisualsVisible(physicCollider, _showDebugVisuals);

                    physicCollider.visualColliderAdded = true;

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
