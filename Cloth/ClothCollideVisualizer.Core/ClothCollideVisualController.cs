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
    // 캐릭터별 물리 콜라이더 시각화 데이터의 생성/정리를 담당한다.
    public class ClothCollideVisualizerController: CharaCustomFunctionController
    {
        // 현재 캐릭터의 콜라이더 시각화 상태 저장소
        PhysicCollider physicCollider;
        protected override void OnCardBeingSaved(GameMode currentGameMode) { }

        // 현재 물리 콜라이더 데이터를 반환한다.
        internal PhysicCollider GetData()
        {
            return physicCollider;
        }

        // 캐릭터 기준으로 물리 콜라이더 데이터 컨테이너를 초기화한다.
        internal void InitPhysicCollider(OCIChar _ociChar)
        {
            physicCollider = new PhysicCollider();
            physicCollider.ociChar = _ociChar;
        }

        // 생성했던 디버그 오브젝트와 캐시를 모두 정리한다.
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

    // 캐릭터 단위의 물리 콜라이더/디버그 시각화 데이터 묶음
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
        // 의상 슬롯 원본 오브젝트
        public GameObject clothObj;
        // Cloth 컴포넌트 보유 여부
        public bool hasCloth;
    }

    // 원본 콜라이더와 디버그 트랜스폼의 매핑 정보
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
