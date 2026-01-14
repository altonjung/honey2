using Studio;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
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
using static Studio.GuideInput;
using static RootMotion.FinalIK.IKSolver;
using IllusionUtility.GetUtility;
using ADV.Commands.Object;
using static Illusion.Utils;
using static ADV.TextScenario;

#endif

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
        public const string Name = "ClothCollideVisualizerVisualizer";
        public const string Version = "0.9.0.2";
        public const string GUID = "com.alton.illusionplugins.ClothCollideVisualizer";
        internal const string _ownerId = "Alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "cloth_physics_visualizer";
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
        internal static ClothCollideVisualizer _self;

        internal static ConfigEntry<bool> ClothColliderVisual { get; private set; }

        private static string _assemblyLocation;
        private bool _loaded = false;

        private ObjectCtrlInfo _selectedOCI;

        private Dictionary<OCIChar, PhysicCollider> _ociCharMgmt = new Dictionary<OCIChar, PhysicCollider>();

        internal enum Cloth_Status
        {
            CHAR_CHANGE,
            IDLE
        }

        internal enum Update_Mode
        {
            SELECTION,
            CHANGE
        }        

        #endregion

        #region Accessors
        #endregion


        #region Unity Methods
        protected override void Awake()
        {

            base.Awake();

            ClothColliderVisual = Config.Bind("Option", "Active", false, new ConfigDescription("Enable/Disable"));

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

        // private static Transform GetPelvisBone(SkinnedMeshRenderer smr)
        // {
        //     return smr.rootBone;
        // }

        // CapsuleCollider Wireframe 디버그
        private static void CreateCapsuleWireframe(CapsuleCollider capsule, Transform bone, string name, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
        {
            Camera cam = Camera.main;

            // Root
            GameObject root = new GameObject(capsule.name + "_CapsuleWire");
            root.transform.SetParent(capsule.transform, false);
            root.transform.localPosition = capsule.center;
            root.transform.localRotation = Quaternion.identity;

            List<Renderer> renderers = new List<Renderer>();

            // Capsule 방향 결정
            Vector3 axis;
            Quaternion rot = Quaternion.identity;
            switch (capsule.direction)
            {
                case 0: axis = Vector3.right; rot = Quaternion.Euler(0f, 0f, 90f); break;   // X
                case 1: axis = Vector3.up; rot = Quaternion.identity; break;                 // Y
                case 2: axis = Vector3.forward; rot = Quaternion.Euler(90f, 0f, 0f); break; // Z
                default: axis = Vector3.up; break;
            }

            int segments = 48; // 원을 근사할 분할 수

            // Cylinder Body + Top/Bottom Hemisphere 선
            GameObject lineObj = new GameObject("CapsuleWireLines");
            lineObj.transform.SetParent(root.transform, false);
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = new Material(Shader.Find("Unlit/Color"));
            lr.material.color = Color.green;
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

            renderers.Add(lr); // LineRenderer 자체를 등록
            debugObjects.Add(root);
            debugObjects.Add(lineObj);

            debugCollideRenderers[capsule] = renderers;

            // 텍스트
            Vector3 textPos = axis * (halfHeight * 0.5f + 0.1f);
            CreateTextDebugLocal(root.transform, textPos, name, cam, debugObjects, debugCollideRenderers);
        }

        // SphereCollider Wireframe 디버그
        private static void CreateSphereWireframe(SphereCollider sphere, Transform bone, string name, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
        {
            Camera cam = Camera.main;

            GameObject root = new GameObject(sphere.name + "_SphereWire");
            root.transform.SetParent(sphere.transform, false);
            // root.transform.localPosition = sphere.center;
            root.transform.localRotation = Quaternion.identity;

            List<Renderer> renderers = new List<Renderer>();

            GameObject lineObj = new GameObject("SphereWireLines");
            lineObj.transform.SetParent(root.transform, false);
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = new Material(Shader.Find("Unlit/Color"));
            lr.material.color = Color.green;
            lr.widthMultiplier = 0.01f;

            List<Vector3> points = new List<Vector3>();
            int segments = 64; // 원 근사 분할 수
            float radius = sphere.radius;

            float angle_temp = 2 * Mathf.PI / segments;

            // XY 원
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                points.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
            // XZ 원
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                points.Add(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            // YZ 원
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

            // 텍스트
            Vector3 textPos = sphere.center + Vector3.up * (radius + 0.05f);
            CreateTextDebugLocal(root.transform, textPos, name, cam, debugObjects, debugCollideRenderers);
        }

        // Text 도 bone 기준으로 움직이도록 수정
        private static void CreateTextDebugLocal(Transform parent, Vector3 localPos, string text, Camera cam, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
        {
            GameObject textObj = new GameObject(text + "_" + "TextDebug");
            textObj.transform.SetParent(parent, false);
            textObj.transform.localPosition = localPos;

            TextMesh tm = textObj.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 35;
            tm.color = Color.yellow;
            tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;

            // 카메라를 바라보도록
            if (cam != null)
                textObj.transform.rotation = Quaternion.LookRotation(textObj.transform.position - cam.transform.position);

            debugObjects.Add(textObj);
        }

        // 선택된 Collider 빨간색 강조
        private static void HighlightSelectedCollider(Collider selected, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
        {
            foreach (var kvp in debugCollideRenderers)
            {
                foreach (var rend in kvp.Value)
                {
                    Color c = rend.material.color;
                    // 선택된 것만 빨강, 나머지는 원래 녹색 계열 유지
                    if (kvp.Key == selected)
                    {
                        c.r = 1f; c.g = 0f; c.b = 0f; // 빨강
                    }
                    else
                    {
                        // 원래 색 복원 (녹색 계열)
                        if (kvp.Key is CapsuleCollider)
                            c = new Color(0f, 1f, 0.5f, 0.5f);
                        else if (kvp.Key is SphereCollider)
                            c = new Color(0f, 1f, 0f, 0.5f);
                        else if (kvp.Key is BoxCollider)
                            c = new Color(0.5f, 1f, 0f, 0.5f);
                    }
                    rend.material.color = c;
                }
            }
        }

        private static OCIFolder CreateOCIFolder(OCIChar ociChar, string folderName, TreeNodeObject parentNode)
        {
            OIFolderInfo folderInfo = new OIFolderInfo(Studio.Studio.GetNewIndex());
            folderInfo.name = folderName;

            OCIFolder ociFolder = Studio.AddObjectFolder.Load(folderInfo, null, null, true, Studio.Studio.optionSystem.initialPosition);

            List<TreeNodeObject> newChild = new List<TreeNodeObject>();

            foreach (var child in parentNode.m_child)
            {
                if (child != null)
                {
                    newChild.Add(child);
                }
            }
            parentNode.m_child = newChild;

            ociFolder.treeNodeObject.SetParent(parentNode);

            ociFolder.treeNodeObject.enableDelete = false;
            ociFolder.treeNodeObject.enableCopy = false;
            ociFolder.treeNodeObject.enableChangeParent = false;

            return ociFolder;
        }

        private static OCICollider CreateOCICollider(OCIChar ociChar, Collider collider, Transform bone, string name, TreeNodeObject parentNode)
        {
            ChangeAmount changeAmount = new ChangeAmount(
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one
            );
            
            int idx = Studio.Studio.GetNewIndex();
            ColliderObjectInfo objectInfo = new ColliderObjectInfo(idx);
            objectInfo.changeAmount = changeAmount;

            OCICollider ociCollider = new OCICollider();
            ociCollider.ociChar = ociChar;
            ociCollider.objectInfo = objectInfo;
            ociCollider.collider = collider;

            GuideObject guideObject = Singleton<GuideObjectManager>.Instance.Add(bone.transform, idx);

            if (guideObject != null)
            {
                guideObject.enablePos = true;
                guideObject.enableScale = true;
                guideObject.enableMaluti = false;
                guideObject.calcScale = true;
                guideObject.scaleRate = 0.0f;
                guideObject.scaleRot = 0.0f;
                guideObject.scaleSelect = 0.0f;
                guideObject.SetVisibleCenter(true);
                guideObject.isActive = false;

                GuideObject guideObject2 = guideObject;
                guideObject2.isActiveFunc = (GuideObject.IsActiveFunc)Delegate.Combine(guideObject2.isActiveFunc, new GuideObject.IsActiveFunc(ociCollider.OnSelect));

                guideObject.parent = null;
                guideObject.nonconnect = false;
                guideObject.changeAmount = changeAmount;

                ociCollider.guideObject = guideObject;
                ociCollider.treeNodeObject = Studio.Studio.AddNode(name);
                ociCollider.treeNodeObject.SetParent(parentNode);
                ociCollider.treeNodeObject.enableChangeParent = false;
                ociCollider.treeNodeObject.enableAddChild = false;
                ociCollider.treeNodeObject.enableDelete = false;
                ociCollider.treeNodeObject.enableCopy = false;                

                Studio.Studio.AddCtrlInfo(ociCollider);
             
                return ociCollider;
            }

            return null;
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
#if FEATURE_GROUND_COLLIDER
            Transform groundTransform = value.ociChar.charInfo.objBodyBone.transform.FindLoop(GROUND_COLLIDER_NAME);
            if (groundTransform != null) { Destroy(groundTransform.gameObject); }
#endif            

            value.debugCapsuleCollideVisibleObjects.Clear();
            value.debugSphereCollideVisibleObjects.Clear();           
            value.debugCollideRenderers.Clear();                 
        }

        private static void ClearPhysicCollier(PhysicCollider value)
        {
            // UnityEngine.Debug.Log($">> ClearPhysicCollier start");

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
#if FEATURE_GROUND_COLLIDER
            Transform groundTransform = value.ociChar.charInfo.objBodyBone.transform.FindLoop(GROUND_COLLIDER_NAME);

            if (groundTransform != null) { Destroy(groundTransform.gameObject); }
#endif

            if (value.ociCFolder != null)
            {
                value.ociCFolder.treeNodeObject.enableDelete = true;
                value.ociSFolder.treeNodeObject.enableDelete = true;

                foreach (var obj in value.ociCFolderChild.Keys)
                {
                    if (obj == null) continue;

                    obj.enableDelete = true;
                    Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNode(obj);
                }

                foreach (var obj in value.ociSFolderChild.Keys)
                {
                    if (obj == null) continue;

                    obj.enableDelete = true;
                    Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNode(obj);
                }

                Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNode(value.ociSFolder.treeNodeObject);
                Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNode(value.ociCFolder.treeNodeObject);
            }

            value.ociCFolderChild.Clear();
            value.ociSFolderChild.Clear();
            value.debugCollideRenderers.Clear();

            value.ociCFolder = null;
            value.ociSFolder = null;

            // UnityEngine.Debug.Log($">> ClearPhysicCollier end");
        }

#if FEATURE_GROUND_COLLIDER
        private static CapsuleCollider AddCapsuleGroundCollider(GameObject colliderObject, Transform bone)
        {
            colliderObject.transform.SetParent(bone, false);

            var capsule = colliderObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0, -5.5f, 0);
            capsule.radius = 6f;
            capsule.height = 1.0f;
            capsule.direction = 1; // Y축 기준

            return capsule;
        }

        private static void CreateGroundClothCollider(ChaControl baseCharControl)
        {
            CapsuleCollider groundCollider = null;
            Transform groundTransform = baseCharControl.objBodyBone.transform.FindLoop(GROUND_COLLIDER_NAME);
            Transform root_bone = baseCharControl.objBodyBone.transform.FindLoop("cf_J_Root");
            // ground collider
            if (groundTransform == null)
            {
                GameObject groundObj = new GameObject(GROUND_COLLIDER_NAME);
                groundCollider = AddCapsuleGroundCollider(groundObj, root_bone);
            }
            else
            {
                groundCollider = groundTransform.GetComponent<CapsuleCollider>();

                if (groundCollider == null)
                {
                    groundCollider = AddCapsuleGroundCollider(groundTransform.gameObject, root_bone);
                }
            }

            List<Cloth> clothes = baseCharControl.transform.GetComponentsInChildren<Cloth>(true).ToList();

            foreach (Cloth cloth in clothes)
            {
                // 새 capsuleCollider 교체
                cloth.capsuleColliders = new CapsuleCollider[] { groundCollider }.ToArray();
            }
        }
#endif
        private static void AddVisualColliders(OCIChar ociChar, Update_Mode type)
        {
            if (ociChar != null && ClothColliderVisual.Value == true)
            {
                // UnityEngine.Debug.Log($">> AddVisualColliders");

                PhysicCollider physicCollider = null;

                if (_self._ociCharMgmt.TryGetValue(ociChar, out physicCollider))
                {
                    if (type == Update_Mode.SELECTION)
                        return;

                    ClearPhysicCollier(physicCollider);
                    _self._ociCharMgmt.Remove(ociChar);
                }
                else
                {
                    physicCollider = new PhysicCollider();
                }

                physicCollider.ociChar = ociChar;

                ChaControl baseCharControl = ociChar.charInfo;

                List<GameObject> physicsClothes = new List<GameObject>();

                int idx = 0;
                foreach (var cloth in baseCharControl.objClothes)
                {
                    if (cloth == null)
                    {
                        idx++;
                        continue;
                    }

                    physicCollider.clothInfos[idx].clothObj = cloth;

                    if (cloth.GetComponentsInChildren<Cloth>().Length > 0)
                    {
                        physicCollider.clothInfos[idx].hasCloth = true;
                        physicsClothes.Add(cloth);
                    }
                    else
                    {
                        physicCollider.clothInfos[idx].hasCloth = false;
                    }

                    idx++;
                }

                idx = 0;
                foreach (var accessory in baseCharControl.objAccessory)
                {
                    if (accessory == null)
                    {
                        idx++;
                        continue;
                    }

                    physicCollider.accessoryInfos[idx].clothObj = accessory;

                    if (accessory.GetComponentsInChildren<Cloth>().Length > 0)
                    {
                        physicCollider.accessoryInfos[idx].hasCloth = true;
                        physicsClothes.Add(accessory);
                    }
                    else
                    {
                        physicCollider.accessoryInfos[idx].hasCloth = false;
                    }

                    idx++;
                }

                if (physicsClothes.Count > 0)
                {
                    ociChar.treeNodeObject.enableAddChild = true;
                    physicCollider.ociCFolder = CreateOCIFolder(ociChar, GROUP_CAPSULE_COLLIDER, ociChar.treeNodeObject);
                    physicCollider.ociSFolder = CreateOCIFolder(ociChar, GROUP_SPHERE_COLLIDER, ociChar.treeNodeObject);

                    physicCollider.ociCFolder.treeNodeObject.enableAddChild = true;
                    physicCollider.ociSFolder.treeNodeObject.enableAddChild = true;
                }

#if FEATURE_GROUND_COLLIDER
                List<Cloth> clothes = baseCharControl.transform.GetComponentsInChildren<Cloth>(true).ToList();
                if (clothes.Count > 0)
                {
                    CreateGroundClothCollider(baseCharControl);
                }
#endif
                
                {
                    // sphere collider
                    if (physicCollider.ociSFolder != null && ociChar.charInfo.objBodyBone)
                    {
                        List<SphereCollider> spherecolliders = ociChar.charInfo.objBodyBone.transform.GetComponentsInChildren<SphereCollider>().OrderBy(col => col.gameObject.name).ToList();
                        List<CapsuleCollider> capsulecolliders = ociChar.charInfo.objBodyBone.transform.GetComponentsInChildren<CapsuleCollider>().OrderBy(col => col.gameObject.name).ToList();
        
                        foreach (var col in spherecolliders.OrderBy(col => col.gameObject.name).ToList())
                        {
                            if (col == null) continue; // Destroy 된 경우 스킵

                            if (col.gameObject.name.Contains("Cloth colliders"))
                            {
                                string trim_name = col.gameObject.name.Replace("Cloth colliders support_", "").Trim();
                                string collider_name;

                                idx = trim_name.IndexOf('-');
                                if (idx >= 0)
                                    collider_name = trim_name.Substring(0, idx);
                                else
                                    collider_name = trim_name;

                                OCICollider ociCollider = CreateOCICollider(ociChar, col, col.gameObject.transform, collider_name, physicCollider.ociSFolder.treeNodeObject);

                                if (ociCollider != null)
                                {

                                   physicCollider.ociSFolderChild.Add(ociCollider.treeNodeObject, col);
                                   CreateSphereWireframe(col,  col.gameObject.transform, collider_name, physicCollider.debugSphereCollideVisibleObjects, physicCollider.debugCollideRenderers);
                                }
                            }
                        }

                        foreach (var col in capsulecolliders.OrderBy(col => col.gameObject.name).ToList())
                        {
                            if (col == null) continue; // Destroy 된 경우 스킵

                            if (col.gameObject.name.Contains("Cloth colliders"))
                            {
                                string trim_name = col.gameObject.name.Replace("Cloth colliders support_", "").Trim();
                                string collider_name;
                                idx = trim_name.IndexOf('-');
                                if (idx >= 0)
                                    collider_name = trim_name.Substring(0, idx);
                                else
                                    collider_name = trim_name;

                                OCICollider ociCollider = CreateOCICollider(ociChar, col, col.gameObject.transform, collider_name, physicCollider.ociCFolder.treeNodeObject);

                                if (ociCollider != null)
                                {
                                   physicCollider.ociCFolderChild.Add(ociCollider.treeNodeObject, col);
                                   CreateCapsuleWireframe(col,  col.gameObject.transform, collider_name, physicCollider.debugCapsuleCollideVisibleObjects, physicCollider.debugCollideRenderers);
                                }
                            }
                        }
                    }

                    if (physicCollider.ociCFolder != null)
                    {
                        ociChar.treeNodeObject.enableAddChild = false;
                        physicCollider.ociCFolder.treeNodeObject.enableAddChild = false;
                        physicCollider.ociSFolder.treeNodeObject.enableAddChild = false;
                    }

                    if (_self._ociCharMgmt.TryGetValue(ociChar, out PhysicCollider currentValue))
                    {
                        _self._ociCharMgmt[ociChar] = currentValue;
                    }
                    else
                    {
                         _self._ociCharMgmt.Add(ociChar, physicCollider);
                    }

                    // UnityEngine.Debug.Log($">> renew ociChar collider {_self._ociCharMgmt.Count}");

                    // parent 구성 후 UI 업데이트                 
                    Singleton<Studio.Studio>.Instance.treeNodeCtrl.RefreshHierachy();
                }
            }
        }

        private static IEnumerator ExecuteAfterFrame(OCIChar ociChar, Update_Mode type)
        {
            int frameCount = 30;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            AddVisualColliders(ociChar, type);
        }

        private static void DeselectNode(OCIChar ociChar)
        {
            if (ociChar != null)
            {
                PhysicCollider value = null;
                if (_self._ociCharMgmt.TryGetValue(ociChar, out value))
                {
                    ClearPhysicCollier(value);
                    _self._ociCharMgmt.Remove(ociChar);
                }

                _self._selectedOCI = null;

            }
        }

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
                //  UnityEngine.Debug.Log($">> OnSelectSingle");               
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);         
                _self._selectedOCI = objectCtrlInfo;
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
                DeselectNode(_self._selectedOCI as OCIChar);

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

        [HarmonyPatch(typeof(TreeNodeObject), "SetVisibleLoop", new[] { typeof(TreeNodeObject), typeof(bool) })]
        private static class TreeNodeObject_SetVisibleLoop_Patches
        {
            public static bool Prefix(TreeNodeObject __instance, TreeNodeObject _source, bool _visible)
            {
                if (_source != null)
                {
                    if (_source.gameObject.activeSelf != _visible)
                    {
                        _source.gameObject.SetActive(_visible);
                    }
                    if (_visible && _source.treeState == TreeNodeObject.TreeState.Close)
                    {
                        _visible = false;
                    }
                    foreach (TreeNodeObject source in _source.child)
                    {
                        __instance.SetVisibleLoop(source, _visible);
                    }
                }
                
                return false;
            }
        }

        [HarmonyPatch(typeof(OCIFolder), "OnVisible", typeof(bool))]
        internal static class OCIFolder_OnVisible_Patches
        {
            public static void Postfix(OCIFolder __instance, bool _visible)
            {
                if (__instance.treeNodeObject == null || __instance.treeNodeObject.parent == null)
                    return;

                OCIChar ociChar = Studio.Studio.GetCtrlInfo(__instance.treeNodeObject.parent) as OCIChar;

                if (ociChar != null)
                {
                    if (_self._ociCharMgmt.TryGetValue(ociChar, out var physicCollider))
                    {
                        if (__instance.folderInfo.name == GROUP_CAPSULE_COLLIDER)
                        {
                            foreach (var visibleObject in physicCollider.debugCapsuleCollideVisibleObjects)
                            {
                                visibleObject.SetActive(_visible);
                            }
                        }
                        else if (__instance.folderInfo.name == GROUP_SPHERE_COLLIDER)
                        {
                            foreach (var visibleObject in physicCollider.debugSphereCollideVisibleObjects)
                            {
                                visibleObject.SetActive(_visible);
                            }
                        }
                    }
                }
            }
        }
        
        #endregion
    }


    public class ColliderObjectInfo : ObjectInfo
    {
        public override int kind => 1;
        public ColliderObjectInfo(int _key) : base(_key)
        {
        }

        // kinds는 ObjectInfo에서 virtual이라 override 가능
        public override int[] kinds
        {
            get
            {
                // 그냥 자기 kind 하나만 리턴 (dummy)
                return new int[] { kind };
            }
        }

        public override void Save(BinaryWriter _writer, Version _version)
        {
            base.Save(_writer, _version);
            // ColliderObjectInfo 전용 저장 데이터가 있다면 여기에 추가
            // 지금은 dummy라서 없음
        }

        // Load - 기본 부모 동작 호출 + dummy 처리
        public override void Load(BinaryReader _reader, Version _version, bool _import, bool _other = true)
        {
            base.Load(_reader, _version, _import, _other);
            // ColliderObjectInfo 전용 로드 데이터가 있다면 여기에 추가
            // 지금은 dummy라서 없음
        }

        // DeleteKey는 abstract라 반드시 구현해야 함
        public override void DeleteKey()
        {
            // ColliderObjectInfo 전용 키 삭제 로직이 있다면 여기에
            // 지금은 dummy 처리
        }
    }

    public class ClothInfo
    {
        public GameObject clothObj;
        public bool hasCloth;
    }

    public class PhysicCollider
    {
        public OCIChar ociChar;
        public ClothInfo[] clothInfos;

       public ClothInfo[] accessoryInfos;

        public List<GameObject> debugCapsuleCollideVisibleObjects = new List<GameObject>();

        public List<GameObject> debugSphereCollideVisibleObjects = new List<GameObject>();

        public Dictionary<Collider, List<Renderer>> debugCollideRenderers = new Dictionary<Collider, List<Renderer>>();

        public OCIFolder ociCFolder;
        public OCIFolder ociSFolder;

        public Dictionary<TreeNodeObject, Collider> ociCFolderChild = new Dictionary<TreeNodeObject, Collider>();

        public Dictionary<TreeNodeObject, Collider> ociSFolderChild = new Dictionary<TreeNodeObject, Collider>();

        public PhysicCollider()
        {
            clothInfos = new ClothInfo[8];
            for (int i = 0; i < clothInfos.Length; i++)
            {
                clothInfos[i] = new ClothInfo();
            }

            accessoryInfos = new ClothInfo[20];
            for (int i = 0; i < accessoryInfos.Length; i++)
            {
                accessoryInfos[i] = new ClothInfo();
            }            
        }        
    }

    public class OCICollider : ObjectCtrlInfo
    {
        public OCIChar ociChar;
        public Collider collider;
        public override void OnDelete() { }

        public override void OnAttach(TreeNodeObject _parent, ObjectCtrlInfo _child)
        {
        }

        public override void OnDetach()
        {
        }

        public override void OnDetachChild(ObjectCtrlInfo _child) { }

        public override void OnSelect(bool _select)
        {
        }

        public override void OnLoadAttach(TreeNodeObject _parent, ObjectCtrlInfo _child)
        {
        }

        public override void OnVisible(bool _visible)
        {
        }
    }
}