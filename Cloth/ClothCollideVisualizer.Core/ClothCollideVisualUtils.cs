using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;
#if AISHOUJO || HONEYSELECT2
using AIChara;
using IllusionUtility.GetUtility;
#endif

namespace ClothCollideVisualizer
{
    public class ClothCollideVisualUtils
    {
        private const string ColliderPrefix = "Cloth colliders support_";
        private const string ClothTopName = "top";
        private const string ClothBottomName = "bottom";

        internal const string topManifestXml = @"
            <AI_ClothColliders>
                <cloth>
                    <CapsuleCollider boneName='cf_J_Spine03' radius='0.90' center='0.00, 0.00, 0.00' height='2.60' direction='0' />
                </cloth>
            </AI_ClothColliders>";

        internal const string bottomManifestXml = @"
            <AI_ClothColliders>
                <cloth>
                    <SphereColliderPair>
                        <first boneName='cf_J_Kosi02' radius='1.2' center='0.00, 0.00, 0.00' />
                        <second boneName='cf_J_LegUp00_L' radius='0.9' center='0.04, 0.02, 0.00' />
                    </SphereColliderPair>
                        <SphereColliderPair>
                            <first boneName='cf_J_Kosi02' radius='1.2' center='0.00, 0.00, 0.00' />
                            <second boneName='cf_J_LegUp00_R' radius='0.9' center='-0.04, 0.02, 0.00' />
                        </SphereColliderPair>
                </cloth>
            </AI_ClothColliders>";

        internal static void AllocateClothColliders(PhysicCollider physicCollider, string xml, string clothName, string uniqueId, Cloth[] clothes, bool isTop)
        {
            if (physicCollider == null || physicCollider.chaCtrl == null || string.IsNullOrEmpty(xml) || clothes == null || clothes.Length == 0)
                return;

            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RealHumanSupport] Failed to parse cloth collider xml: {ex.Message}");
                return;
            }

            XElement root = doc.Root;
            if (root == null)
                return;

            foreach (XElement cloth in root.Elements("cloth"))
            {
                foreach (XElement element in cloth.Elements())
                {
                    if (element.Name == "CapsuleCollider")
                    {
                        CapsuleColliderData capsule = GetCapsuleColliderData(element, uniqueId, clothName);
                        if (capsule != null && !ContainsCapsuleData(physicCollider.capsuleColliders, capsule))
                            physicCollider.capsuleColliders.Add(capsule);
                    }
                    else if (element.Name == "SphereColliderPair")
                    {
                        SphereColliderData first = GetSphereColliderData(element.Element("first"), uniqueId);
                        SphereColliderData second = GetSphereColliderData(element.Element("second"), uniqueId);
                        if (first == null)
                            continue;

                        SphereColliderPair pair = new SphereColliderPair(first, second, clothName);
                        if (!ContainsSpherePairData(physicCollider.sphereColliders, pair))
                            physicCollider.sphereColliders.Add(pair);
                    }
                }
            }

            UpdateClothColliders(physicCollider, clothes, isTop);
        }

        internal static void UpdateClothColliders(PhysicCollider physicCollider, Cloth[] targets, bool isTop)
        {
            if (physicCollider == null || physicCollider.chaCtrl == null || targets == null || targets.Length == 0)
                return;

            string targetClothName = isTop ? ClothTopName : ClothBottomName;
            ChaControl chaCtrl = physicCollider.chaCtrl;

            List<SphereColliderPair> sphereData = physicCollider.sphereColliders
                .Where(v => v != null && string.Equals(v.ClothName, targetClothName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            List<CapsuleColliderData> capsuleData = physicCollider.capsuleColliders
                .Where(v => v != null && string.Equals(v.ClothName, targetClothName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (Cloth target in targets)
            {
                if (target == null)
                    continue;

                List<ClothSphereColliderPair> sphereResults = (target.sphereColliders ?? Array.Empty<ClothSphereColliderPair>())
                    .Where(v => v.first != null || v.second != null)
                    .ToList();

                List<CapsuleCollider> capsuleResults = (target.capsuleColliders ?? Array.Empty<CapsuleCollider>())
                    .Where(v => v != null)
                    .ToList();

                foreach (SphereColliderPair pair in sphereData)
                {
                    SphereCollider c1 = AddSphereCollider(chaCtrl, pair.first);
                    SphereCollider c2 = AddSphereCollider(chaCtrl, pair.second);
                    if (c1 == null && c2 == null)
                        continue;

                    bool exists = sphereResults.Any(v => v.first == c1 && v.second == c2);
                    if (!exists)
                    {
                        ClothSphereColliderPair newPair = new ClothSphereColliderPair();
                        newPair.first = c1;
                        newPair.second = c2;
                        sphereResults.Add(newPair);
                    }
                }

                foreach (CapsuleColliderData capsule in capsuleData)
                {
                    CapsuleCollider collider = AddCapsuleCollider(chaCtrl, capsule);
                    if (collider != null && !capsuleResults.Contains(collider))
                        capsuleResults.Add(collider);
                }

                target.sphereColliders = sphereResults.ToArray();
                target.capsuleColliders = capsuleResults.ToArray();
            }
        }

        private static SphereColliderData GetSphereColliderData(XElement element, string uniqueId)
        {
            if (element == null)
                return null;

            return new SphereColliderData(
                element.Attribute("boneName")?.Value ?? throw new FormatException("Missing boneName attribute"),
                float.Parse(element.Attribute("radius")?.Value ?? throw new FormatException("Missing radius attribute"), CultureInfo.InvariantCulture),
                ParseVector3(element.Attribute("center")?.Value ?? throw new FormatException("Missing center attribute")),
                uniqueId);
        }

        private static CapsuleColliderData GetCapsuleColliderData(XElement element, string uniqueId, string clothName)
        {
            if (element == null)
                return null;

            return new CapsuleColliderData(
                element.Attribute("boneName")?.Value ?? throw new FormatException("Missing boneName attribute"),
                float.Parse(element.Attribute("radius")?.Value ?? throw new FormatException("Missing radius attribute"), CultureInfo.InvariantCulture),
                float.Parse(element.Attribute("height")?.Value ?? throw new FormatException("Missing height attribute"), CultureInfo.InvariantCulture),
                ParseVector3(element.Attribute("center")?.Value ?? throw new FormatException("Missing center attribute")),
                int.Parse(element.Attribute("direction")?.Value ?? throw new FormatException("Missing direction attribute"), CultureInfo.InvariantCulture),
                uniqueId,
                clothName);
        }

        private static Vector3 ParseVector3(string value)
        {
            string[] parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                throw new FormatException("Could not parse Vector3 from " + value);

            return new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));
        }

        private static SphereCollider AddSphereCollider(ChaControl chaCtrl, SphereColliderData sphereColliderData)
        {
            if (chaCtrl == null || sphereColliderData == null)
                return null;

            string colliderName = $"{ColliderPrefix}{sphereColliderData.BoneName}";
            if (!string.IsNullOrEmpty(sphereColliderData.UniqueId))
                colliderName += $"_{sphereColliderData.UniqueId}";

            return AddSphereCollider(chaCtrl, sphereColliderData.BoneName, colliderName, sphereColliderData.ColliderRadius, sphereColliderData.ColliderCenter);
        }

        private static SphereCollider AddSphereCollider(ChaControl chaCtrl, string boneName, string colliderName, float colliderRadius = 0.5f, Vector3 colliderCenter = new Vector3())
        {
            Transform bone = chaCtrl.objBodyBone.transform.FindLoop(boneName);
            if (bone == null)
                return null;

            Transform colliderObject = bone.transform.Find(colliderName);
            if (colliderObject == null)
            {
                colliderObject = new GameObject(colliderName).transform;
                colliderObject.SetParent(bone.transform, false);
                colliderObject.localScale = Vector3.one;
                colliderObject.localPosition = Vector3.zero;
            }

            SphereCollider collider = colliderObject.GetComponent<SphereCollider>();
            if (collider == null)
                collider = colliderObject.gameObject.AddComponent<SphereCollider>();

            collider.radius = colliderRadius;
            collider.center = colliderCenter;
            return collider;
        }

        private static CapsuleCollider AddCapsuleCollider(ChaControl chaCtrl, CapsuleColliderData capsuleColliderData)
        {
            if (chaCtrl == null || capsuleColliderData == null)
                return null;

            string colliderName = $"{ColliderPrefix}{capsuleColliderData.BoneName}";
            if (!string.IsNullOrEmpty(capsuleColliderData.ColliderNamePostfix))
                colliderName += $"_{capsuleColliderData.ColliderNamePostfix}";

            return AddCapsuleCollider(
                chaCtrl,
                capsuleColliderData.BoneName,
                colliderName,
                capsuleColliderData.ColliderRadius,
                capsuleColliderData.CollierHeight,
                capsuleColliderData.ColliderCenter,
                capsuleColliderData.Direction);
        }

        internal static void RemoveAllocatedClothColliders(PhysicCollider physicCollider, bool isTop)
        {
            if (physicCollider == null || physicCollider.chaCtrl == null)
                return;

            string targetClothName = isTop ? ClothTopName : ClothBottomName;
            ChaControl chaCtrl = physicCollider.chaCtrl;

            foreach (CapsuleColliderData capsule in physicCollider.capsuleColliders
                         .Where(v => v != null && string.Equals(v.ClothName, targetClothName, StringComparison.OrdinalIgnoreCase)))
            {
                RemoveCapsuleColliderObject(chaCtrl, capsule);
            }

            foreach (SphereColliderPair pair in physicCollider.sphereColliders
                         .Where(v => v != null && string.Equals(v.ClothName, targetClothName, StringComparison.OrdinalIgnoreCase)))
            {
                RemoveSphereColliderObject(chaCtrl, pair.first);
                RemoveSphereColliderObject(chaCtrl, pair.second);
            }
        }

        private static void RemoveSphereColliderObject(ChaControl chaCtrl, SphereColliderData sphereColliderData)
        {
            if (chaCtrl == null || sphereColliderData == null)
                return;

            Transform bone = chaCtrl.objBodyBone.transform.FindLoop(sphereColliderData.BoneName);
            if (bone == null)
                return;

            string colliderName = $"{ColliderPrefix}{sphereColliderData.BoneName}";
            if (!string.IsNullOrEmpty(sphereColliderData.UniqueId))
                colliderName += $"_{sphereColliderData.UniqueId}";

            Transform colliderObject = bone.transform.Find(colliderName);
            if (colliderObject != null)
                UnityEngine.Object.Destroy(colliderObject.gameObject);
        }

        private static void RemoveCapsuleColliderObject(ChaControl chaCtrl, CapsuleColliderData capsuleColliderData)
        {
            if (chaCtrl == null || capsuleColliderData == null)
                return;

            Transform bone = chaCtrl.objBodyBone.transform.FindLoop(capsuleColliderData.BoneName);
            if (bone == null)
                return;

            string colliderName = $"{ColliderPrefix}{capsuleColliderData.BoneName}";
            if (!string.IsNullOrEmpty(capsuleColliderData.ColliderNamePostfix))
                colliderName += $"_{capsuleColliderData.ColliderNamePostfix}";

            Transform colliderObject = bone.transform.Find(colliderName);
            if (colliderObject != null)
                UnityEngine.Object.Destroy(colliderObject.gameObject);
        }

        private static CapsuleCollider AddCapsuleCollider(ChaControl chaCtrl, string rootBoneName, string colliderName, float colliderRadius = 0.5f, float collierHeight = 0f, Vector3 colliderCenter = new Vector3(), int colliderDirection = 0)
        {
            Transform bone = chaCtrl.objBodyBone.transform.FindLoop(rootBoneName);
            if (bone == null)
                return null;

            Transform colliderObject = bone.transform.Find(colliderName);
            if (colliderObject == null)
            {
                colliderObject = new GameObject(colliderName).transform;
                colliderObject.SetParent(bone.transform, false);
                colliderObject.localScale = Vector3.one;
                colliderObject.localPosition = Vector3.zero;
            }

            CapsuleCollider collider = colliderObject.GetComponent<CapsuleCollider>();
            if (collider == null)
                collider = colliderObject.gameObject.AddComponent<CapsuleCollider>();

            collider.radius = colliderRadius;
            collider.center = colliderCenter;
            collider.height = collierHeight;
            collider.direction = colliderDirection;
            return collider;
        }

        private static bool ContainsCapsuleData(List<CapsuleColliderData> list, CapsuleColliderData candidate)
        {
            if (list == null || candidate == null)
                return false;

            return list.Any(v =>
                v != null &&
                string.Equals(v.ClothName, candidate.ClothName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(v.BoneName, candidate.BoneName, StringComparison.Ordinal) &&
                string.Equals(v.ColliderNamePostfix, candidate.ColliderNamePostfix, StringComparison.Ordinal) &&
                Mathf.Approximately(v.ColliderRadius, candidate.ColliderRadius) &&
                Mathf.Approximately(v.CollierHeight, candidate.CollierHeight) &&
                ApproximatelyVector(v.ColliderCenter, candidate.ColliderCenter) &&
                v.Direction == candidate.Direction);
        }

        private static bool ContainsSpherePairData(List<SphereColliderPair> list, SphereColliderPair candidate)
        {
            if (list == null || candidate == null)
                return false;

            return list.Any(v =>
                v != null &&
                string.Equals(v.ClothName, candidate.ClothName, StringComparison.OrdinalIgnoreCase) &&
                SphereDataEquals(v.first, candidate.first) &&
                SphereDataEquals(v.second, candidate.second));
        }

        private static bool SphereDataEquals(SphereColliderData a, SphereColliderData b)
        {
            if (a == null && b == null)
                return true;
            if (a == null || b == null)
                return false;

            return string.Equals(a.BoneName, b.BoneName, StringComparison.Ordinal)
                && string.Equals(a.UniqueId, b.UniqueId, StringComparison.Ordinal)
                && Mathf.Approximately(a.ColliderRadius, b.ColliderRadius)
                && ApproximatelyVector(a.ColliderCenter, b.ColliderCenter);
        }

        private static bool ApproximatelyVector(Vector3 a, Vector3 b, float epsilon = 0.0001f)
        {
            return Mathf.Abs(a.x - b.x) <= epsilon
                && Mathf.Abs(a.y - b.y) <= epsilon
                && Mathf.Abs(a.z - b.z) <= epsilon;
        }
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

        public CapsuleColliderData(string boneName, float colliderRadius, float collierHeight, Vector3 colliderCenter, int direction, string colliderNamePostfix, string clothName)
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
