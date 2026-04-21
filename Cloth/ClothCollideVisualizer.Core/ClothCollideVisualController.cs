using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using ADV.Commands.Camera;
using ADV.Commands.Object;
using IllusionUtility.GetUtility;
using KKAPI.Studio;
using KKAPI.Maker;
using KKAPI;
using KKAPI.Chara;
#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace ClothCollideVisualizer
{
    // Manages per-character collider visualization data.
    public class ClothCollideVisualizerController: CharaCustomFunctionController
    {
        // Cached runtime data for the current character.
        PhysicCollider physicCollider;
        protected override void OnCardBeingSaved(GameMode currentGameMode) { }

        // Returns current collider visualization data.
        internal PhysicCollider GetData()
        {
            return physicCollider;
        }

        // Creates and initializes collider visualization data.
        internal PhysicCollider CreateData(OCIChar _ociChar)
        {
            physicCollider = new PhysicCollider();
            physicCollider.ociChar = _ociChar;
            physicCollider.chaCtrl = _ociChar.GetChaControl();

            SupportExtraClothCollider(physicCollider.chaCtrl, physicCollider);

            return physicCollider;
        }

        internal void SupportExtraClothCollider(ChaControl chaCtrl, PhysicCollider physicCollider)
        {
            if (chaCtrl == null || physicCollider == null || chaCtrl.objClothes == null)
                return;

            var clothTop = chaCtrl.objClothes.Length > 0 ? chaCtrl.objClothes[0] : null;
            var clothBottom = chaCtrl.objClothes.Length > 1 ? chaCtrl.objClothes[1] : null;

            if (clothTop != null) {
                Cloth[] clothes = clothTop.GetComponentsInChildren<Cloth>(true);
                if (clothes.Length > 0) {
                    ClothCollideVisualUtils.AllocateClothColliders(physicCollider, ClothCollideVisualUtils.topManifestXml, "top", "Adjustable", clothes, true);
                }
            }
            
            if (clothBottom != null) {
                Cloth[] clothes = clothBottom.GetComponentsInChildren<Cloth>(true);

                if (clothes.Length > 0) {
                    ClothCollideVisualUtils.AllocateClothColliders(physicCollider, ClothCollideVisualUtils.bottomManifestXml, "bottom", "Adjustable", clothes, false);
                }
            }            
        }        

        // Removes generated debug objects and clears caches.
        internal void RemovePhysicCollier()
        {
            if (physicCollider == null)
                return;

            foreach (var obj in physicCollider.debugCapsuleCollideVisibleObjects)
            {
                if (obj == null) continue;
                GameObject.Destroy(obj);
            }
            foreach (var obj in physicCollider.debugSphereCollideVisibleObjects)
            {
                if (obj == null) continue;
                GameObject.Destroy(obj);
            }

            physicCollider.debugCapsuleCollideVisibleObjects.Clear();
            physicCollider.debugSphereCollideVisibleObjects.Clear();          

            if (physicCollider.debugCollideRenderers != null)
                physicCollider.debugCollideRenderers.Clear();

            if (physicCollider.debugEntries != null)
            {
                foreach (var entry in physicCollider.debugEntries)
                {
                    if (entry == null || entry.debugTransform == null)
                        continue;
                    GameObject.Destroy(entry.debugTransform.gameObject);
                }
                physicCollider.debugEntries.Clear();
            }
            if (physicCollider.debugEntryBySource != null)
                physicCollider.debugEntryBySource.Clear();

            physicCollider.visualColliderAdded = false;
        }
    }

    // Per-character runtime container for collider and debug state.
    class PhysicCollider
    {
        public OCIChar ociChar;
        public ChaControl chaCtrl;
        public bool visualColliderAdded;
        // Force Refresh
        public bool requireForceRefresh;
        // Force Refresh .
        public bool pendingResetTop;
        public bool pendingResetBottom;
        public ClothInfo[] clothInfos;

    //    public ClothInfo[] accessoryInfos;

        public List<GameObject> debugCapsuleCollideVisibleObjects = new List<GameObject>();

        public List<GameObject> debugSphereCollideVisibleObjects = new List<GameObject>();

        public Dictionary<Collider, List<Renderer>> debugCollideRenderers = new Dictionary<Collider, List<Renderer>>();

        public List<DebugColliderEntry> debugEntries = new List<DebugColliderEntry>();
        public Dictionary<Collider, DebugColliderEntry> debugEntryBySource = new Dictionary<Collider, DebugColliderEntry>();
        // ( / ) collider transform
        public Dictionary<string, ColliderTransformInfo> topColliderTransformInfos = new Dictionary<string, ColliderTransformInfo>(StringComparer.Ordinal);
        public Dictionary<string, ColliderTransformInfo> bottomColliderTransformInfos = new Dictionary<string, ColliderTransformInfo>(StringComparer.Ordinal);
        // collider (top/bottom)
        public Dictionary<int, int> colliderInstanceIdToSlot = new Dictionary<int, int>();
        // " local transform).
        public Dictionary<int, ColliderTransformInfo> colliderDefaultTransforms = new Dictionary<int, ColliderTransformInfo>();


        public List<SphereColliderPair> sphereColliders = new List < SphereColliderPair >();
        public List<CapsuleColliderData> capsuleColliders = new List<CapsuleColliderData>();

        public PhysicCollider()
        {
            clothInfos = new ClothInfo[8];
            for (int i = 0; i < clothInfos.Length; i++)
            {
                clothInfos[i] = new ClothInfo();
            }

            //accessoryInfos = new ClothInfo[20];
            //for (int i = 0; i < accessoryInfos.Length; i++)
            //{
            //    accessoryInfos[i] = new ClothInfo();
            //}            
        }        
    }

    class ClothInfo
    {
        // Original outfit slot object reference.
        public GameObject clothObj;
        // Cloth
        public bool hasCloth;
    }

    // Mapping between source collider and debug transform objects.
    class DebugColliderEntry
    {
        public string name;
        public Collider source;
        // Transform Debug Root
        public Transform debugTransform;
        // center(=Sphere/Capsule/Boxcenter) Offset Root
        public Transform debugCenterTransform;

        // baseline(
        public Vector3 baselineLocalPosition;
        public Vector3 baselineLocalEuler;
        public Vector3 baselineLocalScale;
        public Vector3 baselineCenter;
    }

    // transform
    class ColliderTransformInfo
    {
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale = Vector3.one;
        public Vector3 colliderCenter;

        public bool hasLocalPosition;
        public bool hasLocalEulerAngles;
        public bool hasLocalScale;
        public bool hasColliderCenter;
    }
}
