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
                    <CapsuleCollider boneName='cf_J_Spine03' radius='1.00' center='0.00, 0.00, 0.00' height='2.60' direction='0' />
                    <CapsuleCollider boneName='cf_J_Spine02' radius='0.90' center='0.00, 0.65, 0.30' height='3.20' direction='0' />
                    <CapsuleCollider boneName='cf_J_Kosi02' radius='1.30' center='0.00, 0.00, 0.00' height='3.00' direction='1' />
                    <CapsuleCollider boneName='cf_J_Neck' radius='0.3' center='0.00, 0.00, 0.00' height='0.60' direction='1' />
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
                    
                    <CapsuleCollider boneName='cf_J_Spine01' radius='0.60' center='0.00, 0.00, 0.00' height='1.40' direction='1' />
                    <CapsuleCollider boneName='cf_N_height' radius='60.00' center='0.00, -60.00, 0.00' height='1.00' direction='1' />
                </cloth>
            </AI_ClothColliders>";

        internal static void AllocateClothColliders(ChaControl chaCtrl, string xml, string clothName, string uniqueId, Cloth[] clothes, bool isTop)
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null) return;

            foreach (var cloth in root.Elements("cloth"))
            {
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

            UpdateClothColliders(chaCtrl, clothes, isTop);
        }

        internal static void BackupClothColliders(Cloth cloth)
        {
            if (UndressPhysics._clothColliderBackup.ContainsKey(cloth))
                return;

            UndressPhysics._clothColliderBackup[cloth] = new ClothColliderBackup
            {
                Sphere = cloth.sphereColliders,
                Capsule = cloth.capsuleColliders
            };
        }
        internal static void RestoreClothColliders(Cloth cloth)
        {
            if (UndressPhysics._clothColliderBackup.TryGetValue(cloth, out var backup))
            {
                cloth.sphereColliders = backup.Sphere;
                cloth.capsuleColliders = backup.Capsule;
            }
        }

        internal static void ClearBackup()
        {
            UndressPhysics._clothColliderBackup.Clear();
        }

        internal static void UpdateClothColliders(ChaControl chaCtrl, Cloth[] targets, bool isTop)
        {
            foreach (var target in targets)
            {
                BackupClothColliders(target);

                var sphereResults = new List<ClothSphereColliderPair>();
                var capsuleResults = new List<CapsuleCollider>();

                // isTop이면 origin collider 먼저 추가
                if (isTop && UndressPhysics._clothColliderBackup.TryGetValue(target, out var backup))
                {
                    if (backup.Sphere != null)
                        sphereResults.AddRange(backup.Sphere);

                    if (backup.Capsule != null)
                        capsuleResults.AddRange(backup.Capsule);
                }

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

                target.sphereColliders = sphereResults.ToArray();
                target.capsuleColliders = capsuleResults.ToArray();
            }
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
            var bone = chaCtrl.objBodyBone.transform.FindLoop(boneName);
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

        private static CapsuleCollider AddCapsuleCollider(ChaControl chaCtrl, CapsuleColliderData capsuleColliderData)
        {
            if (capsuleColliderData == null)
                return null;
            string colliderName = $"{UndressPhysics.UNDRESS_COLLIDER_PREFIX}_{capsuleColliderData.BoneName}";
            if (!capsuleColliderData.ColliderNamePostfix.IsNullOrEmpty())
                colliderName += $"_{capsuleColliderData.ColliderNamePostfix}";
            return AddCapsuleCollider(chaCtrl, capsuleColliderData.BoneName, colliderName, capsuleColliderData.ColliderRadius, capsuleColliderData.CollierHeight, capsuleColliderData.ColliderCenter, capsuleColliderData.Direction);
        }

        private static CapsuleCollider AddCapsuleCollider(ChaControl chaCtrl, string rootBoneName, string colliderName, float colliderRadius = 0.5f, float collierHeight = 0f, Vector3 colliderCenter = new Vector3(), int colliderDirection = 0)
        {
            // todo find all bones and cache them for later finding to save time
            var bone = chaCtrl.objBodyBone.transform.FindLoop(rootBoneName);
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

        internal static UndressData CreateUndressData(Cloth cloth, ChaControl chaCtrl, bool isTop)
        {
            UndressData undressData = new UndressData();
            undressData.ociChar = chaCtrl.GetOCIChar();
            undressData.coroutine = null;
            undressData.cloth = cloth;
            undressData.IsTop = isTop;
            // UnityEngine.Debug.Log($">> isTop {undressData.IsTop} in {UndressPhysics.Name}");

            string colliderName = "";
            if (undressData.IsTop) {
                colliderName = $"{UndressPhysics.UNDRESS_COLLIDER_PREFIX}_cf_J_Neck_999999990";
            } else
            {
                colliderName = $"{UndressPhysics.UNDRESS_COLLIDER_PREFIX}_cf_J_Spine01_8888888880";
            }

            Transform target_bone = chaCtrl.objBodyBone.transform.FindLoop(colliderName);
            if (target_bone != null)
            {
                CapsuleCollider[] colliders = target_bone.gameObject.GetComponentsInChildren<CapsuleCollider>(true);
                // UnityEngine.Debug.Log($">> colliders {colliders.Length} in {UndressPhysics.Name}");

                if (colliders.Length > 0)
                {
                    undressData.collider = colliders[0];
                }
            }

            return undressData;
        }

        internal static void RestoreMaxDistances(UndressData undressData)
        {
            if (undressData.cloth != null)
            {
                var cloth = undressData.cloth;

                cloth.worldVelocityScale = 0f;
                cloth.worldAccelerationScale = 0f;
                cloth.useGravity = false;

                cloth.enabled = false;
                cloth.enabled = true;

                float[] originalMax = undressData.originalMaxDistances[cloth];

                if (originalMax != null && originalMax.Length > 0)
                {
                    var coeffs = cloth.coefficients;
                    int count = Mathf.Min(coeffs.Length, originalMax.Length);

                    for (int i = 0; i < count; i++)
                        coeffs[i].maxDistance = originalMax[i];

                    cloth.coefficients = coeffs;
                }

                cloth.ClearTransformMotion();   // ⭐ 중요 (velocity reset)
            }
        }
    }


    class UndressData {
        public OCIChar ociChar;
        public Cloth cloth;
        public Dictionary<Cloth, float[]> originalMaxDistances = new Dictionary<Cloth, float[]>();
        // public SkinnedMeshRenderer meshRenderer;
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

    internal class ClothColliderBackup
    {
        public ClothSphereColliderPair[] Sphere;
        public CapsuleCollider[] Capsule;
    }
}
