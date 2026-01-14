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

#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using ADV.Commands.Camera;
using KKAPI.Studio;
using System;
#endif

#endif

namespace ClothColliderVisualizer
{
    public class Logic
    {
        #region Private Methods
        // CapsuleCollider Wireframe 디버그
        internal static void CreateCapsuleWireframe(CapsuleCollider capsule, Transform bone, string name, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
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
        internal static void CreateSphereWireframe(SphereCollider sphere, Transform bone, string name, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
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
        internal static void CreateTextDebugLocal(Transform parent, Vector3 localPos, string text, Camera cam, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
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
        internal static void HighlightSelectedCollider(Collider selected, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
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

        // internal static OCIFolder CreateOCIFolder(OCIChar ociChar, string folderName, TreeNodeObject parentNode)
        // {
        //     OIFolderInfo folderInfo = new OIFolderInfo(Studio.Studio.GetNewIndex());
        //     folderInfo.name = folderName;

        //     OCIFolder ociFolder = Studio.AddObjectFolder.Load(folderInfo, null, null, true, Studio.Studio.optionSystem.initialPosition);

        //     List<TreeNodeObject> newChild = new List<TreeNodeObject>();

        //     foreach (var child in parentNode.m_child)
        //     {
        //         if (child != null)
        //         {
        //             newChild.Add(child);
        //         }
        //     }
        //     parentNode.m_child = newChild;

        //     ociFolder.treeNodeObject.SetParent(parentNode);

        //     ociFolder.treeNodeObject.enableDelete = false;
        //     ociFolder.treeNodeObject.enableCopy = false;
        //     ociFolder.treeNodeObject.enableChangeParent = false;

        //     return ociFolder;
        // }

        internal static OCICollider CreateOCICollider(OCIChar ociChar, Collider collider, Transform bone, string name, TreeNodeObject parentNode)
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

        internal static void InitCollider(PhysicCollider value)
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

        internal static void ClearPhysicCollier(PhysicCollider value)
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

            // if (value.ociCFolder != null)
            // {
            //     value.ociCFolder.treeNodeObject.enableDelete = true;
            //     value.ociSFolder.treeNodeObject.enableDelete = true;

            //     // foreach (var obj in value.ociCFolderChild.Keys)
            //     // {
            //     //     if (obj == null) continue;

            //     //     obj.enableDelete = true;
            //     //     Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNode(obj);
            //     // }

            //     foreach (var obj in value.ociSFolderChild.Keys)
            //     {
            //         if (obj == null) continue;

            //         obj.enableDelete = true;
            //         Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNode(obj);
            //     }

            //     Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNode(value.ociSFolder.treeNodeObject);
            //     Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNode(value.ociCFolder.treeNodeObject);
            // }

            // value.ociCFolderChild.Clear();
            // value.ociSFolderChild.Clear();
            value.debugCollideRenderers.Clear();

            // value.ociCFolder = null;
            // value.ociSFolder = null;

            // UnityEngine.Debug.Log($">> ClearPhysicCollier end");
        }

#if FEATURE_GROUND_COLLIDER
        internal static CapsuleCollider AddCapsuleGroundCollider(GameObject colliderObject, Transform bone)
        {
            colliderObject.transform.SetParent(bone, false);

            var capsule = colliderObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0, -5.5f, 0);
            capsule.radius = 6f;
            capsule.height = 1.0f;
            capsule.direction = 1; // Y축 기준

            return capsule;
        }

        internal static void CreateGroundClothCollider(ChaControl baseCharControl)
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
        internal static void AddVisualColliders(OCIChar ociChar, Update_Mode type)
        {
            if (ociChar != null && ClothColliderEnable.Value == true)
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
                                //    physicCollider.ociCFolderChild.Add(ociCollider.treeNodeObject, col);
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

        internal static IEnumerator ExecuteAfterFrame(OCIChar ociChar, Update_Mode type)
        {
            int frameCount = 30;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            AddVisualColliders(ociChar, type);
        }

        internal static void DeselectNode(OCIChar ociChar)
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

        // public OCIFolder ociCFolder;
        // public OCIFolder ociSFolder;

        // public Dictionary<TreeNodeObject, Collider> ociCFolderChild = new Dictionary<TreeNodeObject, Collider>();

        // public Dictionary<TreeNodeObject, Collider> ociSFolderChild = new Dictionary<TreeNodeObject, Collider>();

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

    public enum Cloth_Status
    {
        CHAR_CHANGE,
        IDLE
    }

    public enum Update_Mode
    {
        SELECTION,
        CHANGE
    }        

}