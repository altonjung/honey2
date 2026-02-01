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

#if FEATURE_SPINE_COLLIDER
        private static CapsuleCollider AddCapsuleSpineCollider(GameObject colliderObject, Transform bone)
        {
            colliderObject.transform.SetParent(bone, false);

            var capsule = colliderObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0, 0, 0);
            capsule.radius = 0.9f;
            capsule.height = 3.0f;
            capsule.direction = 1; // Y축 기준

            return capsule;
        }

        private static void CreateSpineClothCollider(ChaControl charControl, List<Cloth> clothes)
        {
            // UnityEngine.Debug.Log($">> CreateSpineClothCollider()");

            CapsuleCollider groundCollider = null;
            Transform groundTransform = charControl.objBodyBone.transform.FindLoop(UndressSupport.SPINE_COLLIDER_NAME);
            Transform root_bone = charControl.objBodyBone.transform.FindLoop("cf_J_Kosi02");
            // ground collider
            if (groundTransform == null)
            {
                GameObject groundObj = new GameObject(UndressSupport.SPINE_COLLIDER_NAME);
                groundCollider = AddCapsuleSpineCollider(groundObj, root_bone);
            }
            else
            {
                groundCollider = groundTransform.GetComponent<CapsuleCollider>();

                if (groundCollider == null)
                {
                    groundCollider = AddCapsuleSpineCollider(groundTransform.gameObject, root_bone);
                }
            }

            foreach (Cloth cloth in clothes)
            {
                // 새 capsuleCollider 교체
                cloth.capsuleColliders = new CapsuleCollider[] { groundCollider }.ToArray();
            }
        }
#endif

        internal static UndressData GetCloth(ObjectCtrlInfo objCtrlInfo)
        {
            UndressData undressData = null;

            if (objCtrlInfo == null)
                return null;

            OCIChar ociChar = objCtrlInfo as OCIChar;
            if (ociChar == null)
                return null;

            undressData = new UndressData();

            // Body renderer (참고용)
            undressData.meshRenderer =
                GetBodyRenderer(ociChar.guideObject.transformTarget);

            // 모든 Cloth 수집
            undressData.clothes =
                ociChar.GetChaControl()
                    .transform
                    .GetComponentsInChildren<Cloth>(true)
                    .ToList();

#if FEATURE_SPINE_COLLIDER
            if (undressData.clothes.Count > 0)
            {
                CreateSpineClothCollider(ociChar.GetChaControl(), undressData.clothes);
            }
#endif

            foreach (var cloth in undressData.clothes)
            {
                if (cloth == null)
                    continue;

                // 🔹 Cloth 기준 coefficients 저장
                ClothSkinningCoefficient[] coeffs = cloth.coefficients;
                float[] maxDistances = new float[coeffs.Length];

                for (int i = 0; i < coeffs.Length; i++)
                {
                    maxDistances[i] = coeffs[i].maxDistance;
                }

                undressData.originalMaxDistances[cloth] = maxDistances;
                // 🔹 물리 설정 복원
            }

            return undressData;
        }  

        internal static void RestoreMaxDistances(UndressData undressData)
        {
            foreach (var cloth in undressData.clothes)
            {
                if (cloth == null) continue;

                // 2️⃣ solver 리셋 (이때 떨어지지 않음)
                cloth.enabled = false;
                cloth.enabled = true;
                
                float[] originalMax = undressData.originalMaxDistances[cloth];

                if (originalMax != null && originalMax.Length > 0)
                {
                    ClothSkinningCoefficient[] coeffs = cloth.coefficients;
                    int count = Mathf.Min(coeffs.Length, originalMax.Length);

                    for (int i = 0; i < count; i++)
                        coeffs[i].maxDistance = originalMax[i];

                    cloth.coefficients = coeffs;
                }

                // 3️⃣ 정상 물리 복원
                cloth.worldVelocityScale = 1f;
                cloth.worldAccelerationScale = 1f;
                cloth.useGravity = true;
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
    }


    class UndressData {
        public List<Cloth> clothes = new List<Cloth>();
        public Dictionary<Cloth, float[]> originalMaxDistances = new Dictionary<Cloth, float[]>();
        public SkinnedMeshRenderer meshRenderer;
    }
}
