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

namespace UndressSupport
{
    public class Logic
    {     
        internal static  UndressData GetCloth(ObjectCtrlInfo objCtrlInfo)
        {
            UndressData undressData = null;

            if (objCtrlInfo != null)
            {
                OCIChar ociChar = objCtrlInfo as OCIChar;
                if (ociChar != null) {

                    undressData = new UndressData();

                    undressData.meshRenderer = GetBodyRenderer(ociChar.guideObject.transformTarget);

                    undressData.clothes = ociChar.GetChaControl().transform.GetComponentsInChildren<Cloth>(true).ToList();

                    foreach (var cloth in undressData.clothes)
                    {
                        if (cloth == null || cloth.transform == null)
                            continue;    

                        // Max Distance 처리
                        ClothSkinningCoefficient[] coeffs = cloth.coefficients;
                        float[] maxDistances = new float[coeffs.Length];

                        for (int i = 0; i < coeffs.Length; i++)
                            maxDistances[i] = coeffs[i].maxDistance;

                        undressData.originalMaxDistances.Add(cloth, maxDistances);
                    }
                }
            }

            return undressData;
        }             

        internal static void RestoreMaxDistances(UndressData undressData)
        {
            foreach (var cloth in undressData.clothes)
            {
                if (cloth == null) continue;

                float[] originalMax = undressData.originalMaxDistances[cloth];

                if (originalMax.Length > 0)
                {
                    Vector3[] vertices = undressData.meshRenderer.sharedMesh.vertices;
                    ClothSkinningCoefficient[] coeffs = cloth.coefficients;

                    int vertexCount = Mathf.Min(vertices.Length, coeffs.Length);

                    for (int i = 0; i < vertexCount; i++)
                    {
                        coeffs[i].maxDistance = originalMax[i];
                    }
                    cloth.coefficients = coeffs;
                }
                cloth.useGravity = true;
                cloth.externalAcceleration = Vector3.zero;
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
