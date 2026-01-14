using Studio;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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
using System;
#endif

namespace ClothColliderVisualizer
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class ClothColliderVisualizer : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "ClothColliderVisualizer";
        public const string Version = "0.9.0.2";
        public const string GUID = "com.alton.illusionplugins.clothColliderVisualizer";
        internal const string _ownerId = "alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "cloth_collider_visualizer";
#endif
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        private const string GROUP_CAPSULE_COLLIDER = "Group: (C)Colliders";
        private const string GROUP_SPHERE_COLLIDER = "Group: (S)Colliders";

#if FEATURE_GROUND_COLLIDER        
        private const string GROUND_COLLIDER_NAME = "Cloth colliders support_flat_ground";
#endif
        #region Private Types
        #endregion

        #region Private Variables        

        internal static new ManualLogSource Logger;

        private static string _assemblyLocation;
        private bool _loaded = false;

        private ObjectCtrlInfo _selectedOCI;

        private Dictionary<OCIChar, PhysicCollider> _ociCharMgmt = new Dictionary<OCIChar, PhysicCollider>();

        internal static ClothColliderVisualizer _self;        

        internal static ConfigEntry<bool> ClothColliderEnable { get; private set; }

        internal static ConfigEntry<bool> ClothColliderShowCollider { get; private set; }

        internal static ConfigEntry<bool> ClothColliderShowColliderText { get; private set; }

        #endregion

        #region Accessors
        #endregion


        #region Unity Methods
        protected override void Awake()
        {

            base.Awake();

            ClothColliderEnable = Config.Bind("Option", "Plugin Active", false, new ConfigDescription("Enable/Disable"));

            ClothColliderShowCollider = Config.Bind("Option", "Collider Show", false, new ConfigDescription("Show/Hide"));

            ClothColliderShowColliderText = Config.Bind("Option", "Collider Text Show", false, new ConfigDescription("Show/Hide"));

            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

#if FEATURE_SCENE_SAVE
            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingImported += OnSceneImport;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingSaved += OnSceneSave;
#endif
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

        private void SceneInit()
        {
            foreach (var kvp in _ociCharMgmt)
            {
                var key = kvp.Key;
                PhysicCollider value = kvp.Value;
                InitCollider(value);
            }

            _ociCharMgmt.Clear();
            _selectedOCI = null;            
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
                data.version = ClothColliderVisualizer._saveVersion;
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
                    .Select(kv => kv.Value as OCIChar)   // ObjectCtrlInfo → OCIChar
                    .Where(c => c != null)               // null 제거 (OCIChar가 아닌 경우 스킵)
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

        private void SceneImport(string path, XmlNode node)
        {
            Dictionary<int, ObjectCtrlInfo> toIgnore = new Dictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
            this.ExecuteDelayed2(() =>
            {
                List<KeyValuePair<int, ObjectCtrlInfo>> dic = Studio.Studio.Instance.dicObjectCtrl.Where(e => toIgnore.ContainsKey(e.Key) == false).OrderBy(e => SceneInfo_Import_Patches._newToOldKeys[e.Key]).ToList();

                List<OCIChar> ociChars = dic
                    .Select(kv => kv.Value as OCIChar)   // ObjectCtrlInfo → OCIChar
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

#if FEATURE_GROUND_COLLIDER
                // ground collider
                CreateGroundClothCollider(ociChar.charInfo);
#endif                

                // bone 노드 순회
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

        // 유틸: 안전한 float 파싱
        private float ParseFloat(string value)
        {
            if (float.TryParse(value, out float result))
                return result;
            return 0f;
        }

        private void SceneWrite(string path, XmlTextWriter writer)
        {
            foreach (OCIChar ociChar in _ociCharMgmt.Keys)
            {
                writer.WriteStartElement("character");
                writer.WriteAttributeString("name", "" + ociChar.charInfo.name);

                List<SphereCollider> scolliders = ociChar.charInfo.objBodyBone
                    .transform
                    .GetComponentsInChildren<SphereCollider>()
                    .OrderBy(col => col.gameObject.name) // 이름 기준 정렬
                    .ToList();

                List<Transform> targetCollider = new List<Transform>();

                foreach (var col in scolliders)
                {
                    if (col == null) continue; // Destroy 된 경우 스킵

                    if (col.gameObject.name.Contains("Cloth colliders"))
                    {
                        targetCollider.Add(col.gameObject.transform);
                    }
                }

                // capsule collider
                List<CapsuleCollider> ccolliders = ociChar.charInfo.objBodyBone
                    .transform
                    .GetComponentsInChildren<CapsuleCollider>(true)
                    .OrderBy(col => col.gameObject.name) // 이름 기준 정렬
                    .ToList();

                foreach (var col in ccolliders)
                {

                    if (col == null) continue; // Destroy 된 경우 스킵

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
                    writer.WriteAttributeString("x", collider.localPosition.x.ToString());
                    writer.WriteAttributeString("y", collider.localPosition.y.ToString());
                    writer.WriteAttributeString("z", collider.localPosition.z.ToString());
                    writer.WriteEndElement();

                    // rotation
                    writer.WriteStartElement("rotation");
                    writer.WriteAttributeString("x", collider.localEulerAngles.x.ToString());
                    writer.WriteAttributeString("y", collider.localEulerAngles.y.ToString());
                    writer.WriteAttributeString("z", collider.localEulerAngles.z.ToString());
                    writer.WriteEndElement();

                    // scale
                    writer.WriteStartElement("scale");
                    writer.WriteAttributeString("x", collider.localScale.x.ToString());
                    writer.WriteAttributeString("y", collider.localScale.y.ToString());
                    writer.WriteAttributeString("z", collider.localScale.z.ToString());
                    writer.WriteEndElement();

                    writer.WriteEndElement(); // collider
                }

                writer.WriteEndElement(); // character
            }             
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
        #endregion

        #region Patches
        [HarmonyPatch(typeof(SceneInfo), "Import", new[] { typeof(BinaryReader), typeof(Version) })]
        private static class SceneInfo_Import_Patches //This is here because I fucked up the save format making it impossible to import scenes correctly
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
                _self._selectedOCI = objectCtrlInfo;
                
                if (objectCtrlInfo == null)
                    return true;

                OCIChar ociChar = objectCtrlInfo as OCIChar;

                if (ociChar != null)
                {
                    ociChar.GetChaControl().StartCoroutine(ExecuteAfterFrame(ociChar, Update_Mode.SELECTION));
                }

                OCICollider ociCollider = Studio.Studio.GetCtrlInfo(_node) as OCICollider;
                if (ociCollider != null)
                {

                    if (_node.parent != null)
                    {
                        if (_self._ociCharMgmt.TryGetValue(ociCollider.ociChar, out var physicCollider))
                        {
                            Collider collider = null;
                            if (physicCollider.ociCFolderChild.TryGetValue(_node, out collider))
                            {
                                HighlightSelectedCollider(collider, physicCollider.debugCollideRenderers);
                            }

                            if (physicCollider.ociSFolderChild.TryGetValue(_node, out collider))
                            {
                                HighlightSelectedCollider(collider, physicCollider.debugCollideRenderers);
                            }
                        }
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeleteNode), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeleteNode_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);
                _self._selectedOCI = null;
                
                if (objectCtrlInfo == null)
                    return true;

                OCIChar ociChar = objectCtrlInfo as OCIChar;

                if (ociChar != null) {

                    DeselectNode(ociChar);
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {

                ChaControl chaControl = __instance.GetChaControl();

                if (chaControl != null)
                {                    
                    chaControl.StartCoroutine(ExecuteAfterFrame(__instance as OCIChar, Update_Mode.CHANGE));
                }
            }
        }

        // 악세러리 부분 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeAccessory", typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool))]
        private static class ChaControl_ChangeAccessory_Patches
        {
            private static void Postfix(ChaControl __instance, int slotNo, int type, int id, string parentKey, bool forceChange)
            {                 
                bool shouldReallocation = true;
                PhysicCollider physicCollider = null;
                if (_self._ociCharMgmt.TryGetValue(__instance.GetOCIChar(), out physicCollider))
                {
                    if (physicCollider.accessoryInfos[slotNo] != null)
                    {                    
                        GameObject newClothObj = __instance.objAccessory[slotNo];

                        if (newClothObj != null)
                        {
                            if (physicCollider.accessoryInfos[slotNo].hasCloth == false && newClothObj.GetComponentsInChildren<Cloth>().Length == 0 && newClothObj.GetComponentsInChildren<DynamicBone>().Length == 0)
                            {
                                shouldReallocation = false;
                            }
                        }                                        
                    }

                    if (shouldReallocation)
                        __instance.StartCoroutine(ExecuteAfterFrame(__instance.GetOCIChar(), Update_Mode.CHANGE));
                    else
                    {
                        // 옷 부분 변경 시 물리 옷이 없는 옷의 경우에도 ground collider 대상으로 상태 update 는 해줘야 함
#if FEATURE_GROUND_COLLIDER
                        CreateGroundClothCollider(__instance.GetOCIChar().charInfo);
#endif                        
                    }
                }                 
            } 
        }
        
        // 옷 부분 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        private static class ChaControl_ChangeClothes_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
            {
                // UnityEngine.Debug.Log($">> ChangeClothes");
                bool shouldReallocation = true;
                PhysicCollider physicCollider = null;
                if (_self._ociCharMgmt.TryGetValue(__instance.GetOCIChar(), out physicCollider))
                {
                    if (physicCollider.clothInfos[kind] != null)
                    {                    
                        GameObject newClothObj = __instance.objClothes[kind];

                        if (newClothObj != null)
                        {
                            if (physicCollider.clothInfos[kind].hasCloth == false && newClothObj.GetComponentsInChildren<Cloth>().Length == 0)
                            {
                                shouldReallocation = false;
                            }
                        }                                        
                    }

                    if (shouldReallocation)
                        __instance.StartCoroutine(ExecuteAfterFrame(__instance.GetOCIChar(), Update_Mode.CHANGE));
                    else
                    {
                        // 옷 부분 변경 시 물리 옷이 없는 옷의 경우에도 ground collider 대상으로 상태 update 는 해줘야 함
#if FEATURE_GROUND_COLLIDER
                        CreateGroundClothCollider(__instance.GetOCIChar().charInfo);
#endif                        
                    }
                }                    
            }
        }

        // 옷 전체 변경
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {
                if (__instance != null)
                {
                    // UnityEngine.Debug.Log($">> SetAccessoryStateAll");
                    __instance.StartCoroutine(ExecuteAfterFrame(__instance.GetOCIChar(), Update_Mode.CHANGE));  
                }
            }
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

        // [HarmonyPatch(typeof(TreeNodeObject), "SetVisibleLoop", new[] { typeof(TreeNodeObject), typeof(bool) })]
        // private static class TreeNodeObject_SetVisibleLoop_Patches
        // {
        //     public static bool Prefix(TreeNodeObject __instance, TreeNodeObject _source, bool _visible)
        //     {
        //         if (_source != null)
        //         {
        //             if (_source.gameObject.activeSelf != _visible)
        //             {
        //                 _source.gameObject.SetActive(_visible);
        //             }
        //             if (_visible && _source.treeState == TreeNodeObject.TreeState.Close)
        //             {
        //                 _visible = false;
        //             }
        //             foreach (TreeNodeObject source in _source.child)
        //             {
        //                 __instance.SetVisibleLoop(source, _visible);
        //             }
        //         }
                
        //         return false;
        //     }
        // }

        // [HarmonyPatch(typeof(OCIFolder), "OnVisible", typeof(bool))]
        // internal static class OCIFolder_OnVisible_Patches
        // {
        //     public static void Postfix(OCIFolder __instance, bool _visible)
        //     {
        //         if (__instance.treeNodeObject == null || __instance.treeNodeObject.parent == null)
        //             return;

        //         OCIChar ociChar = Studio.Studio.GetCtrlInfo(__instance.treeNodeObject.parent) as OCIChar;

        //         if (ociChar != null)
        //         {
        //             if (_self._ociCharMgmt.TryGetValue(ociChar, out var physicCollider))
        //             {
        //                 if (__instance.folderInfo.name == GROUP_CAPSULE_COLLIDER)
        //                 {
        //                     foreach (var visibleObject in physicCollider.debugCapsuleCollideVisibleObjects)
        //                     {
        //                         visibleObject.SetActive(_visible);
        //                     }
        //                 }
        //                 else if (__instance.folderInfo.name == GROUP_SPHERE_COLLIDER)
        //                 {
        //                     foreach (var visibleObject in physicCollider.debugSphereCollideVisibleObjects)
        //                     {
        //                         visibleObject.SetActive(_visible);
        //                     }
        //                 }
        //             }
        //         }
        //     }
        // }
        
        #endregion
    }
}