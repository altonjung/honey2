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

            physicCollider.visualColliderAdded = false;
        }
    }

    // 캐릭터 단위의 물리 콜라이더/디버그 시각화 데이터 묶음
    class PhysicCollider
    {
        public OCIChar ociChar;
        public ChaControl chaCtrl;
        public bool visualColliderAdded;
        // 의상 변경 등으로 인해 수동 Force Refresh가 필요한 상태
        public bool requireForceRefresh;
        // 옷이 바뀐 슬롯은 Force Refresh 시 기본값으로 초기화해야 한다.
        public bool pendingResetTop;
        public bool pendingResetBottom;
        public ClothInfo[] clothInfos;

    //    public ClothInfo[] accessoryInfos;

        public List<GameObject> debugCapsuleCollideVisibleObjects = new List<GameObject>();

        public List<GameObject> debugSphereCollideVisibleObjects = new List<GameObject>();

        public Dictionary<Collider, List<Renderer>> debugCollideRenderers = new Dictionary<Collider, List<Renderer>>();

        public List<DebugColliderEntry> debugEntries = new List<DebugColliderEntry>();
        public Dictionary<Collider, DebugColliderEntry> debugEntryBySource = new Dictionary<Collider, DebugColliderEntry>();
        // 슬롯(상의/하의)별 collider transform 저장소
        public Dictionary<string, ColliderTransformInfo> topColliderTransformInfos = new Dictionary<string, ColliderTransformInfo>(StringComparer.Ordinal);
        public Dictionary<string, ColliderTransformInfo> bottomColliderTransformInfos = new Dictionary<string, ColliderTransformInfo>(StringComparer.Ordinal);
        // 현재 옷 기준으로 collider가 어느 슬롯(top/bottom)에 속하는지 빠르게 찾기 위한 캐시
        public Dictionary<int, int> colliderInstanceIdToSlot = new Dictionary<int, int>();
        // 콜라이더 "생성 직후 기본값"을 저장한다(최초 관측 시점의 local transform).
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
        // 콜라이더의 Transform을 그대로 복제해 보여주기 위한 Debug Root
        public Transform debugTransform;
        // 콜라이더 center(=Sphere/Capsule/Box의 center)를 편집하기 위한 Offset Root
        public Transform debugCenterTransform;

        // baseline(최초 관측 시점의 값)
        public Vector3 baselineLocalPosition;
        public Vector3 baselineLocalEuler;
        public Vector3 baselineLocalScale;
        public Vector3 baselineCenter;
    }

    // 콜라이더 이름 기준으로 저장되는 로컬 transform 정보
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
