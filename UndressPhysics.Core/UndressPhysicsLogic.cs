using Studio;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Reflection;
using System.Globalization;
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

namespace UndressPhysics
{
    public class Logic
    {    
        internal const string topManifestXml = @"
            <AI_ClothColliders>
                <cloth>
                    <CapsuleCollider boneName='cf_J_Spine02' radius='0.91' center='0.00, -0.10, 0.00' height='3.90' direction='1' />
                    <CapsuleCollider boneName='cf_J_Spine03' radius='0.60' center='0.00, 0.40, 0.00' height='2.60' direction='0' />
                    <CapsuleCollider boneName='cf_J_Kosi01' radius='1.05' center='0.00, -0.15, -0.10' height='3.00' direction='1' />
                    <CapsuleCollider boneName='cf_J_Kosi02' radius='1.15' center='0.00, 0.00, -0.13' height='3.00' direction='1' />
                </cloth>
            </AI_ClothColliders>";

        internal const string bottomManifestXml = @"
            <AI_ClothColliders>
                <cloth>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegUp01_s_L' radius='0.88' center='0.09, -0.04, 0.08' />
                        <second boneName='cf_J_LegKnee_low_s_L' radius='0.8' center='0.06, -0.30, -0.35' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegUp01_s_L' radius='0.88' center='0.09, -0.04, 0.08' />
                        <second boneName='cf_J_LegUp02_s_L' radius='0.85' center='-0.05, 0.76, 0.1' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegUp01_s_L' radius='0.88' center='0.09, -0.04, 0.08' />
                        <second boneName='cf_J_LegUp03_s_L' radius='0.60' center='-0.24, 0.00, 0.13' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegUp03_s_L' radius='0.60' center='-0.24, 0.00, 0.13' />
                        <second boneName='cf_J_LegKnee_low_s_L' radius='0.8' center='0.06, -0.30, -0.35' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegUp02_s_L' radius='0.85' center='-0.05, 0.76, 0.1' />
                        <second boneName='cf_J_LegUp03_s_L' radius='0.60' center='-0.24, 0.00, 0.13' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegUp01_s_R' radius='0.88' center='-0.09, -0.04, 0.08' />
                        <second boneName='cf_J_LegKnee_low_s_R' radius='0.8' center='-0.06, -0.30, -0.35' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegUp01_s_R' radius='0.88' center='-0.09, -0.04, 0.08' />
                        <second boneName='cf_J_LegUp02_s_R' radius='0.85' center='0.05, 0.76, 0.1' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegUp01_s_R' radius='0.88' center='-0.09, -0.04, 0.08' />
                        <second boneName='cf_J_LegUp03_s_R' radius='0.60' center='0.16, 0.00, 0.13' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegUp03_s_R' radius='0.60' center='0.16, 0.00, 0.13' />
                        <second boneName='cf_J_LegKnee_low_s_R' radius='0.8' center='-0.06, -0.30, -0.35' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegUp02_s_R' radius='0.85' center='0.05, 0.76, 0.1' />
                        <second boneName='cf_J_LegUp03_s_R' radius='0.60' center='0.16, 0.00, 0.13' />
                    </SphereColliderPair>

                    <SphereColliderPair>
                        <first boneName='cf_J_LegKnee_low_s_L' radius='0.8' center='0.06, -0.30, -0.35' />
                        <second boneName='cf_J_LegLow01_s_L' radius='0.65' center='-0.07, -1.41, -0.25' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegLow01_s_L' radius='0.65' center='-0.07, -1.41, -0.25' />
                        <second boneName='cf_J_LegLow02_s_L' radius='0.50' center='-0.06, 0.00, -0.20' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegLow02_s_L' radius='0.50' center='-0.06, 0.00, -0.20' />
                        <second boneName='cf_J_LegLow03_s_L' radius='0.38' center='0.07, -1.07, -0.10' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegKnee_low_s_L' radius='0.8' center='0.06, -0.30, -0.31' />
                        <second boneName='cf_J_LegLow02_s_L' radius='0.50' center='-0.06, 0.00, -0.20' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegLow03_s_L' radius='0.38' center='0.07, -1.07, -0.10' />
                        <second boneName='cf_J_Foot02_L' radius='0.38' center='0.00, -0.32, 1.30' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegKnee_low_s_R' radius='0.8' center='-0.06, -0.30, -0.35' />
                        <second boneName='cf_J_LegLow01_s_R' radius='0.65' center='0.07, -1.41, -0.25' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegLow01_s_R' radius='0.65' center='0.07, -1.41, -0.25' />
                        <second boneName='cf_J_LegLow02_s_R' radius='0.50' center='0.06, 0.00, -0.20' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegLow02_s_R' radius='0.50' center='0.06, 0.00, -0.20' />
                        <second boneName='cf_J_LegLow03_s_R' radius='0.38' center='-0.07, -1.07, -0.10' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegKnee_low_s_R' radius='0.8' center='-0.06, -0.30, -0.35' />
                        <second boneName='cf_J_LegLow02_s_R' radius='0.50' center='0.06, 0.00, -0.20' />
                    </SphereColliderPair>
                    <SphereColliderPair>
                        <first boneName='cf_J_LegLow03_s_R' radius='0.38' center='-0.07, -1.07, -0.10' />
                        <second boneName='cf_J_Foot02_R' radius='0.38' center='0.00, -0.32, 1.30' />
                    </SphereColliderPair>

                    <CapsuleCollider boneName='cf_N_height' radius='60.00' center='0.00, -60.00, 0.00' height='1.00' direction='1' />
                </cloth>
            </AI_ClothColliders>";

        internal static void AllocateClothColliders(ChaControl chaCtrl, string xml, string clothName, string uniqueId, Cloth[] clothes)
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null) return;

            var existing = GetExistingColliderNames(chaCtrl.transform);

            foreach (var cloth in root.Elements("cloth"))
            {
                // 1️⃣ XML에서 이미 존재하는 collider 제거
                FilterExistingColliders(cloth, existing);

                // 2️⃣ 남은 collider만 생성
                foreach (var element in cloth.Elements())
                {
                    if (element.Name == "CapsuleCollider")
                    {
                        var collider = GetCapsuleColliderData(element, uniqueId, clothName);
                        UndressPhysics._capsuleColliders.Add(collider);
                    }
                    else if (element.Name == "SphereColliderPair")
                    {
                        var first = GetSphereColliderData(element.Element("first"), uniqueId);
                        var second = GetSphereColliderData(element.Element("second"), uniqueId);

                        UndressPhysics._sphereColliders.Add(new SphereColliderPair(first, second, clothName));
                    }
                }
            }

            UpdateClothColliders(chaCtrl, clothes);
        }

        internal static HashSet<string> GetExistingColliderNames(Transform clothRoot)
        {
            var set = new HashSet<string>();

            var colliders = clothRoot.GetComponentsInChildren<Collider>(true);

            foreach (var col in colliders)
            {
                if (col.name.StartsWith(UndressPhysics.CLOTH_COLLIDER_PREFIX))
                    set.Add(col.name);
            }

            return set;
        }

        internal static void FilterExistingColliders(XElement clothElement, HashSet<string> existing)
        {

            var nodes = clothElement.Elements().ToList();

            foreach (var node in nodes)
            {
                if (node.Name == "CapsuleCollider")
                {
                    var boneName = node.Attribute("boneName")?.Value;
                    var colliderName = UndressPhysics.CLOTH_COLLIDER_PREFIX + "_" + boneName;

                    if (existing.Contains(colliderName))
                        node.Remove();
                }
                else if (node.Name == "SphereColliderPair")
                {
                    var first = node.Element("first");
                    var second = node.Element("second");

                    var bone1 = first?.Attribute("boneName")?.Value;
                    var bone2 = second?.Attribute("boneName")?.Value;

                    var name1 = UndressPhysics.CLOTH_COLLIDER_PREFIX + "_" + bone1;
                    var name2 = UndressPhysics.CLOTH_COLLIDER_PREFIX + "_" + bone2;

                    if (existing.Contains(name1) || existing.Contains(name2))
                        node.Remove();
                }
            }
        }

        internal static long GetDictKey(int clothPartId, int itemId)
        {
            return ((long)clothPartId << sizeof(int) * 8) | (uint)itemId;
        }

        private static SphereColliderData GetSphereColliderData(XElement element, string uniqueId)
        {
            if (element == null) return null;

            var colliderData = new SphereColliderData(
                element.Attribute("boneName")?.Value ?? throw new FormatException("Missing boneName attribute"),
                float.Parse(element.Attribute("radius")?.Value ?? throw new FormatException("Missing radius attribute"), CultureInfo.InvariantCulture),
                ParseVector3(element.Attribute("center")?.Value ?? throw new FormatException("Missing center attribute")),
                uniqueId);
            // UnityEngine.Debug.Log($">> Added SphereCollider: boneName={colliderData.BoneName} radius={colliderData.ColliderRadius} center={colliderData.ColliderCenter}");
            return colliderData;
        }

        private static CapsuleColliderData GetCapsuleColliderData(XElement element, string uniqueId, string clothName)
        {
            if (element == null) return null;

            var colliderData = new CapsuleColliderData(
                element.Attribute("boneName")?.Value ?? throw new FormatException("Missing boneName attribute"),
                float.Parse(element.Attribute("radius")?.Value ?? throw new FormatException("Missing radius attribute"), CultureInfo.InvariantCulture),
                float.Parse(element.Attribute("height")?.Value ?? throw new FormatException("Missing height attribute"), CultureInfo.InvariantCulture),
                ParseVector3(element.Attribute("center")?.Value ?? throw new FormatException("Missing center attribute")),
                int.Parse(element.Attribute("direction")?.Value ?? throw new FormatException("Missing direction attribute"), CultureInfo.InvariantCulture),
                uniqueId, clothName);
            // UnityEngine.Debug.Log($">> Added CapsuleCollider: boneName={colliderData.BoneName} radius={colliderData.ColliderRadius} height={colliderData.CollierHeight} center={colliderData.ColliderCenter} direction={colliderData.Direction}");
            return colliderData;
        }

        private static Vector3 ParseVector3(string value)
        {
            var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new FormatException("Could not parse Vector3 from " + value);
            return new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));
        }

        private static void UpdateClothColliders(ChaControl chaCtrl, Cloth[] targets)
        {
            foreach (var target in targets)
            {
                // 기존 collider 복사
                var sphereResults = new List<ClothSphereColliderPair>();
                var capsuleResults = new List<CapsuleCollider>();

                if (target.sphereColliders != null)
                    sphereResults.AddRange(target.sphereColliders);

                if (target.capsuleColliders != null)
                    capsuleResults.AddRange(target.capsuleColliders);

                // Sphere 추가
                foreach (var pair in UndressPhysics._sphereColliders)
                {
                    var c1 = AddSphereCollider(chaCtrl, pair.first);
                    var c2 = AddSphereCollider(chaCtrl, pair.second);

                    sphereResults.Add(new ClothSphereColliderPair(c1, c2));
                }

                // Capsule 추가
                foreach (var capsule in UndressPhysics._capsuleColliders)
                {
                    var collider = AddCapsuleCollider(chaCtrl, capsule);
                    capsuleResults.Add(collider);
                }

                // 다시 적용
                target.sphereColliders = sphereResults.ToArray();
                target.capsuleColliders = capsuleResults.ToArray();
            }
        }

        private static SphereCollider AddSphereCollider(ChaControl chaCtrl, SphereColliderData sphereColliderData)
        {
            if (sphereColliderData == null)
                return null;
            string colliderName = $"{UndressPhysics.UNDRESS_COLLIDER_PREFIX}_{sphereColliderData.BoneName}";
            if (!sphereColliderData.UniqueId.IsNullOrEmpty())
                colliderName += $"_{sphereColliderData.UniqueId}";
            return AddSphereCollider(chaCtrl, sphereColliderData.BoneName, colliderName, sphereColliderData.ColliderRadius, sphereColliderData.ColliderCenter);
        }

        private static SphereCollider AddSphereCollider(ChaControl chaCtrl, string boneName, string colliderName, float colliderRadius = 0.5f, Vector3 colliderCenter = new Vector3())
        {
            // todo find all bones and cache them for later finding to save time
            var bone = chaCtrl.transform.FindLoop(boneName);
            if (bone == null)
                return null;

            var colliderObject = bone.transform.Find(colliderName);
            if (colliderObject == null)
            {
                colliderObject = new GameObject(colliderName).transform;
                colliderObject.transform.SetParent(bone.transform, false);
            }

            var collider = colliderObject.GetComponent<SphereCollider>();
            if (collider == null)
                collider = colliderObject.gameObject.AddComponent<SphereCollider>();

            collider.radius = colliderRadius;
            collider.center = colliderCenter;

            return collider;
        }

        private static CapsuleCollider AddCapsuleCollider(ChaControl chaCtrl, CapsuleColliderData sphereColliderData)
        {
            if (sphereColliderData == null)
                return null;
            string colliderName = $"{UndressPhysics.UNDRESS_COLLIDER_PREFIX}_{sphereColliderData.BoneName}";
            if (!sphereColliderData.ColliderNamePostfix.IsNullOrEmpty())
                colliderName += $"_{sphereColliderData.ColliderNamePostfix}";
            return AddCapsuleCollider(chaCtrl, sphereColliderData.BoneName, colliderName, sphereColliderData.ColliderRadius, sphereColliderData.CollierHeight, sphereColliderData.ColliderCenter, sphereColliderData.Direction);
        }

        private static CapsuleCollider AddCapsuleCollider(ChaControl chaCtrl, string boneName, string colliderName, float colliderRadius = 0.5f, float collierHeight = 0f, Vector3 colliderCenter = new Vector3(), int colliderDirection = 0)
        {
            // todo find all bones and cache them for later finding to save time
            var bone = chaCtrl.transform.FindLoop(boneName);
            if (bone == null)
                return null;

            var colliderObject = bone.transform.Find(colliderName);
            if (colliderObject == null)
            {
                colliderObject = new GameObject(colliderName).transform;
                colliderObject.transform.SetParent(bone.transform, false);
                colliderObject.transform.localScale = Vector3.one;
                colliderObject.transform.localPosition = Vector3.zero;
            }

            var collider = colliderObject.GetComponent<CapsuleCollider>();
            if (collider == null)
                collider = colliderObject.gameObject.AddComponent<CapsuleCollider>();

            collider.radius = colliderRadius;
            collider.center = colliderCenter;
            collider.height = collierHeight;
            collider.direction = colliderDirection;

            return collider;
        }
    
        private static CapsuleCollider AddExtraCapsuleCollider(GameObject colliderObject, Transform bone, Vector3 position, float radius=1.2f, float height=2.0f, int direction=1)
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

        private static SphereCollider AddExtraSphereCollider(GameObject colliderObject, Transform bone, Vector3 position, float radius=1.2f)
        {
            colliderObject.transform.SetParent(bone, false);

            var sphere = colliderObject.AddComponent<SphereCollider>();
            sphere.center = position;
            sphere.radius = radius;
            return sphere;
        }
        

        internal static void RemoveUndressPhysicsColliders(GameObject bodyRoot)
        {
            var colliders = bodyRoot.GetComponentsInChildren<Collider>(true);

            foreach (var col in colliders)
            {
                if (col.gameObject.name.StartsWith(UndressPhysics.UNDRESS_COLLIDER_PREFIX))
                {
                    GameObject.Destroy(col); // Collider 컴포넌트만 제거
                }
            }
        }

        static void UpdateExtraCapsuleCollider(Cloth cloth, CapsuleCollider col)
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

        private static CapsuleCollider CreateExtraClothCollider(ChaControl charControl, Cloth cloth, string name, float radius, float height, int direction = 1)
        {
            CapsuleCollider extraCollider = null;

            Transform root_bone = charControl.objBodyBone.transform.FindLoop(name);

            if (root_bone != null) {
                Transform colliderTr = root_bone.Find(UndressPhysics.UNDRESS_COLLIDER_PREFIX + name);

                if (colliderTr != null) {
                      UnityEngine.Object.Destroy(colliderTr.gameObject);
                }

                // spine collider
                GameObject boneObj = new GameObject(UndressPhysics.UNDRESS_COLLIDER_PREFIX + name);
                extraCollider = AddExtraCapsuleCollider(boneObj, root_bone, Vector3.zero, radius, height, direction);
                UpdateExtraCapsuleCollider(cloth, extraCollider);
            }

            return extraCollider;
        }

        internal static UndressData GetUndressData(Cloth cloth, ChaControl chaCtrl, bool isTop)
        {
            // UnityEngine.Debug.Log($">> GetUndressData");

            UndressData undressData = new UndressData();
            undressData.ociChar = chaCtrl.GetOCIChar();
            undressData.coroutine = null;
            undressData.cloth = cloth;

            // Body renderer (참고용)
            undressData.meshRenderer =
                GetBodyRenderer(undressData.ociChar.guideObject.transformTarget);

            Collider[] allColliders = undressData.ociChar.guideObject.transformTarget.GetComponentsInChildren<Collider>();
            
            undressData.IsTop = isTop;

            // UnityEngine.Debug.Log($">> isTop {undressData.IsTop}");
            // top, down 확인 필요
            // ground
            if (undressData.IsTop) {
                var newCollider1 = CreateExtraClothCollider(chaCtrl, undressData.cloth, "cf_J_Neck", 0.3f, 0.6f, 1);
                // Cloth에 적용
                undressData.collider = newCollider1;
            } else {
                var newCollider1 = CreateExtraClothCollider(chaCtrl, undressData.cloth, "cf_J_Spine01", 0.6f, 1.2f);
                var newCollider2 = CreateExtraClothCollider(chaCtrl, undressData.cloth, "cf_J_Kosi02", 0.8f, 3.0f);
                undressData.collider = newCollider1;
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

        //internal static void TryAllocateObject(UndressPhysics instance, OCIChar ociChar) {
        //    ociChar.GetChaControl().StartCoroutine(ExecuteAfterFrame(instance, ociChar));
        //}

    //    internal static IEnumerator ExecuteAfterFrame(UndressPhysics instance, OCIChar ociChar)
    //     {
    //         int frameCount = 20;
    //         for (int i = 0; i < frameCount; i++)
    //             yield return null;

    //         ReallocateUndressDataList(instance, ociChar);
    //     }

        // internal static void RemoveUndressDataList()
        // {   
        //     foreach(UndressData undressData in UndressPhysics._undressDataList)
        //     {
        //         if (undressData.coroutine != null) {
        //             instance.StopCoroutine(undressData.coroutine);
        //         }
        //     }

        //     UndressPhysics._undressDataList.Clear();
        // }
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

    class CapsuleColliderData
    {
        public string ClothName { get; }
        public string BoneName;
        public float ColliderRadius;
        public float CollierHeight;
        public Vector3 ColliderCenter;
        public int Direction;
        public string ColliderNamePostfix;

        public CapsuleColliderData(string boneName, float colliderRadius, float collierHeight, Vector3 colliderCenter,
            int direction, string colliderNamePostfix, string clothName)
        {
            ClothName = clothName;
            BoneName = boneName;
            ColliderRadius = colliderRadius;
            CollierHeight = collierHeight;
            ColliderCenter = colliderCenter;
            Direction = direction;
            ColliderNamePostfix = colliderNamePostfix;
        }
    }
    class SphereColliderData
    {
        public string BoneName;
        public float ColliderRadius;
        public Vector3 ColliderCenter;
        public string UniqueId;

        public SphereColliderData(string boneName, float colliderRadius, Vector3 colliderCenter, string uniqueId)
        {
            BoneName = boneName;
            ColliderRadius = colliderRadius;
            ColliderCenter = colliderCenter;
            UniqueId = uniqueId;
        }
    }

    class SphereColliderPair
    {
        public string ClothName { get; }
        public SphereColliderData first;
        public SphereColliderData second;

        public SphereColliderPair(SphereColliderData first, SphereColliderData second, string clothName)
        {
            ClothName = clothName;
            this.first = first;
            this.second = second;
        }
    }
}
