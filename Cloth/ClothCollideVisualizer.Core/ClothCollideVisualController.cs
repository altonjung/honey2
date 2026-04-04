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
    public class ClothCollideVisualizerController: CharaCustomFunctionController
    {
        PhysicCollider physicCollider;
        protected override void OnCardBeingSaved(GameMode currentGameMode) { }

        internal PhysicCollider GetData()
        {
            return physicCollider;
        }

        internal void InitPhysicCollider(OCIChar _ociChar)
        {
            physicCollider = new PhysicCollider();
            physicCollider.ociChar = _ociChar;
        }

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

            physicCollider = null;
        }
    }

    class PhysicCollider
    {
        public OCIChar ociChar;
        public ClothInfo[] clothInfos;

    //    public ClothInfo[] accessoryInfos;

        public List<GameObject> debugCapsuleCollideVisibleObjects = new List<GameObject>();

        public List<GameObject> debugSphereCollideVisibleObjects = new List<GameObject>();

        public Dictionary<Collider, List<Renderer>> debugCollideRenderers = new Dictionary<Collider, List<Renderer>>();

        public List<DebugColliderEntry> debugEntries = new List<DebugColliderEntry>();
        public Dictionary<Collider, DebugColliderEntry> debugEntryBySource = new Dictionary<Collider, DebugColliderEntry>();

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
        public GameObject clothObj;
        public bool hasCloth;
    }

    class DebugColliderEntry
    {
        public string name;
        public Collider source;
        public Transform debugTransform;
        public Vector3 baselineLocalPosition;
        public Vector3 baselineLocalEuler;
        public Vector3 baselineLocalScale;
    }
}
