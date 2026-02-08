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
using static Studio.GuideInput;
using static RootMotion.FinalIK.IKSolver;
using IllusionUtility.GetUtility;
using ADV.Commands.Object;
using static Illusion.Utils;
using static ADV.TextScenario;
#endif

namespace UndressSupport
{
    public class Logic
    {     
        private static CapsuleCollider AddCapsuleSpineCollider(GameObject colliderObject, Transform bone, Vector3 position, float radius=1.2f, float height=2.0f, int direction=1)
        {
            colliderObject.transform.SetParent(bone, false);

            var capsule = colliderObject.AddComponent<CapsuleCollider>();
            capsule.center = position;
            capsule.radius = radius;
            capsule.height = height;
            capsule.direction = direction;
// 0 = X 축
// 1 = Y 축
// 2 = Z 축
            return capsule;
        }

        static void UpdateCapsuleCollider(Cloth cloth, CapsuleCollider col)
        {
            CapsuleCollider[] old = cloth.capsuleColliders;

            if (old == null)
            {
                cloth.capsuleColliders = new CapsuleCollider[] { col };
                return;
            }

            List<CapsuleCollider> list = new List<CapsuleCollider>(old);

            // 기존 동일 collider 제거
            list.RemoveAll(c => c == col);

            // 다시 추가
            list.Add(col);

            cloth.capsuleColliders = list.ToArray();
        }

        private static CapsuleCollider CreateClothCollider(ChaControl charControl, Cloth cloth, string name, float radius, float height, Vector3? position = null)
        {
            Vector3 pos = position ?? Vector3.zero;

            CapsuleCollider spineCollider = null;

            Transform root_bone = charControl.objBodyBone.transform.FindLoop(name);

            if (root_bone != null) {
                Transform colliderTr = root_bone.Find(UndressSupport.CLOTH_COLLIDER_PREFIX + "_Undress_" + name);

                if (colliderTr != null) {
                      UnityEngine.Object.Destroy(colliderTr.gameObject);
                }

                // spine collider
                GameObject boneObj = new GameObject(UndressSupport.CLOTH_COLLIDER_PREFIX + "_Undress_" + name);
                spineCollider = AddCapsuleSpineCollider(boneObj, root_bone, pos, radius, height);
                UpdateCapsuleCollider(cloth, spineCollider);
            }

            return spineCollider;
        }

        internal static UndressData GetUndressData(Cloth cloth, OCIChar ociChar)
        {
            // UnityEngine.Debug.Log($">> GetUndressData");

            UndressData undressData = new UndressData();
            undressData.ociChar = ociChar;
            undressData.coroutine = null;
            undressData.cloth = cloth;

            // Body renderer (참고용)
            undressData.meshRenderer =
                GetBodyRenderer(ociChar.guideObject.transformTarget);

            Collider[] allColliders = ociChar.guideObject.transformTarget.GetComponentsInChildren<Collider>();
            
            undressData.IsTop = false;

            foreach (var col in allColliders)
            {
                if (col.name.Contains("Cloth colliders"))
                {
                    if (col.name.Contains("_Mune"))
                    {
                        undressData.IsTop = true;
                        break;
                    }
                }
            }

            // UnityEngine.Debug.Log($">> isTop {undressData.IsTop}");
            
            // top, down 확인 필요
            // ground
            if (undressData.IsTop) {
                undressData.collider = CreateClothCollider(ociChar.GetChaControl(), undressData.cloth, "cf_J_Neck", 0.3f, 2.0f);
            } else {
                undressData.collider = CreateClothCollider(ociChar.GetChaControl(), undressData.cloth, "cf_J_Spine01", 0.6f, 2.0f);
                CreateClothCollider(ociChar.GetChaControl(), undressData.cloth, "cf_J_Kosi02", 0.8f, 3.0f);
            }
            
            // 🔹 Cloth 기준 coefficients 저장
            ClothSkinningCoefficient[] coeffs = cloth.coefficients;
            float[] maxDistances = new float[coeffs.Length];

            for (int i = 0; i < coeffs.Length; i++)
            {
                maxDistances[i] = coeffs[i].maxDistance;
            }

            undressData.originalMaxDistances[cloth] = maxDistances;
            // 🔹 물리 설정 복원

            return undressData;
        }  

        internal static void RestoreMaxDistances(UndressData undressData)
        {
            // 2️⃣ solver 리셋 (이때 떨어지지 않음)
            if (undressData.cloth != null) {
                undressData.cloth.enabled = false;
                undressData.cloth.enabled = true;
                
                float[] originalMax = undressData.originalMaxDistances[undressData.cloth];

                if (originalMax != null && originalMax.Length > 0)
                {
                    ClothSkinningCoefficient[] coeffs = undressData.cloth.coefficients;
                    int count = Mathf.Min(coeffs.Length, originalMax.Length);

                    for (int i = 0; i < count; i++)
                        coeffs[i].maxDistance = originalMax[i];

                    undressData.cloth.coefficients = coeffs;
                }

                // 3️⃣ 정상 물리 복원
                undressData.cloth.worldVelocityScale = 0f;
                undressData.cloth.worldAccelerationScale = 1f;
                undressData.cloth.useGravity = true;
            }
        }

        internal static SkinnedMeshRenderer GetBodyRenderer(Transform targetTransform)
        {
            SkinnedMeshRenderer bodyRenderer = null;
#if AISHOUJO || HONEYSELECT2
            List<Transform> transformStack = new List<Transform>();

            transformStack.Add(targetTransform);

            while (transformStack.Count != 0)
            {
                Transform currTransform = transformStack[transformStack.Count - 1];
                transformStack.RemoveAt(transformStack.Count - 1);

                if (currTransform.Find("p_cf_body_00"))
                {
                    Transform bodyTransform = currTransform.Find("p_cf_body_00");
                    AIChara.CmpBody bodyCmp = bodyTransform.GetComponent<AIChara.CmpBody>();

                    if (bodyCmp != null)
                    {
                        if (bodyCmp.targetCustom != null && bodyCmp.targetCustom.rendBody != null)
                        {
                            bodyRenderer = bodyCmp.targetCustom.rendBody.transform.GetComponent<SkinnedMeshRenderer>();
                        }
                        else
                        {
                            if (bodyCmp.targetEtc != null && bodyCmp.targetEtc.objBody != null)
                            {
                                bodyRenderer = bodyCmp.targetEtc.objBody.GetComponent<SkinnedMeshRenderer>();
                            }
                        }
                    }

                    break;
                }
                else if (currTransform.Find("p_cm_body_00"))
                {
                    Transform bodyTransform = currTransform.Find("p_cm_body_00");
                    AIChara.CmpBody bodyCmp = bodyTransform.GetComponent<AIChara.CmpBody>();

                    if (bodyCmp != null)
                    {
                        if (bodyCmp.targetCustom != null && bodyCmp.targetCustom.rendBody != null)
                        {
                            bodyRenderer = bodyCmp.targetCustom.rendBody.transform.GetComponent<SkinnedMeshRenderer>();
                        }
                        else
                        {
                            if (bodyCmp.targetEtc != null && bodyCmp.targetEtc.objBody != null)
                            {
                                bodyRenderer = bodyCmp.targetEtc.objBody.GetComponent<SkinnedMeshRenderer>();
                            }
                        }
                    }

                    break;
                }

                for (int i = 0; i < currTransform.childCount; i++)
                {
                    transformStack.Add(currTransform.GetChild(i));
                }
            }
#endif
            return bodyRenderer;
        }

        //internal static void TryAllocateObject(UndressSupport instance, OCIChar ociChar) {
        //    ociChar.GetChaControl().StartCoroutine(ExecuteAfterFrame(instance, ociChar));
        //}

    //    internal static IEnumerator ExecuteAfterFrame(UndressSupport instance, OCIChar ociChar)
    //     {
    //         int frameCount = 20;
    //         for (int i = 0; i < frameCount; i++)
    //             yield return null;

    //         ReallocateUndressDataList(instance, ociChar);
    //     }

        internal static void ReallocateUndressDataList(UndressSupport instance, OCIChar ociChar)
        {   
            foreach(UndressData undressData in instance._undressDataList)
            {
                if (undressData.coroutine != null) {
                    instance.StopCoroutine(undressData.coroutine);
                    RestoreMaxDistances(undressData);
                }
            }

            instance._undressDataList.Clear();

            if (ociChar != null)
            {
                var clothTop = ociChar.GetChaControl().objClothes[0];
                var clothBottom = ociChar.GetChaControl().objClothes[1];
                List<Cloth> clothes = new List<Cloth>();

                if (clothTop != null)
                    clothes.AddRange(clothTop.GetComponentsInChildren<Cloth>(true));

                if (clothBottom != null)
                    clothes.AddRange(clothBottom.GetComponentsInChildren<Cloth>(true));

                foreach(Cloth cloth in clothes)
                {
                    UndressData undressData = Logic.GetUndressData(cloth, ociChar);
                    instance._undressDataList.Add(undressData);
                }
            }
        }

        internal static void RemoveUndressDataList(UndressSupport instance)
        {   
            foreach(UndressData undressData in instance._undressDataList)
            {
                if (undressData.coroutine != null) {
                    instance.StopCoroutine(undressData.coroutine);
                }
            }

            instance._undressDataList.Clear();
        }
    }


    class UndressData {
        public OCIChar ociChar;
        public Cloth cloth;
        public Dictionary<Cloth, float[]> originalMaxDistances = new Dictionary<Cloth, float[]>();
        public SkinnedMeshRenderer meshRenderer;
        public CapsuleCollider collider;
        public Coroutine coroutine;
        public bool IsTop; // top, bottom
    }
}
