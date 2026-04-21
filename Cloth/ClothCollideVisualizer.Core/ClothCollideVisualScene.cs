using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.Threading.Tasks;
using System.Numerics;
using System.Xml;
using System.Globalization;

#if IPA
using Harmony;
using IllusionPlugin;
#elif BEPINEX
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
#endif
#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using ADV.Commands.Camera;
using KKAPI.Studio;
using IllusionUtility.GetUtility;
using ADV.Commands.Object;
#endif
using RootMotion.FinalIK;

#if AISHOUJO || HONEYSELECT2
using AIChara;
using static Illusion.Utils;
using System.Runtime.Remoting.Messaging;
#endif
using KKAPI.Studio;
using KKAPI.Studio.UI.Toolbars;
using KKAPI.Utilities;
using KKAPI.Chara;

namespace ClothCollideVisualizer
{
    public partial class ClothCollideVisualizer
    {
#if FEATURE_SCENE_SAVE
        private void OnSceneLoad(string path)
        {            
            PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
            if (data == null)
                return;
            XmlDocument doc = new XmlDocument();
            doc.LoadXml((string)data.data["sceneInfo"]);
            XmlNode node = doc.FirstChild;
            if (node == null)
                return;
            SceneLoad(path, node);
        }

        private void OnSceneImport(string path)
        {
            Logger.LogMessage($"Import not support");
        }

        private void OnSceneSave(string path)
        {
            using (StringWriter stringWriter = new StringWriter())
            using (XmlTextWriter xmlWriter = new XmlTextWriter(stringWriter))
            {
                xmlWriter.WriteStartElement("root");
                SceneWrite(path, xmlWriter);
                xmlWriter.WriteEndElement();

                PluginData data = new PluginData();
                data.version = ClothCollideVisualizer._saveVersion;
                data.data.Add("sceneInfo", stringWriter.ToString());

                // UnityEngine.Debug.Log($">> visualizer sceneInfo {stringWriter.ToString()}");

                ExtendedSave.SetSceneExtendedDataById(_extSaveKey, data);
            }
        }

        private void SceneLoad(string path, XmlNode node)
        {
            if (node == null)
                return;
            this.ExecuteDelayed2(() =>
            {
                List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();

                List<OCIChar> ociChars = dic
                    .Select(kv => kv.Value as OCIChar)
                    .Where(c => c != null)       
                    .ToList();

                SceneRead(node, ociChars);

                // delete collider treeNodeObjects
                List<TreeNodeObject> deleteTargets = new List<TreeNodeObject>();
                foreach (TreeNodeObject treeNode in Singleton<Studio.Studio>.Instance.treeNodeCtrl.m_TreeNodeObject)
                {
                    if (treeNode.m_TextName.text == GROUP_CAPSULE_COLLIDER || treeNode.m_TextName.text == GROUP_SPHERE_COLLIDER)
                    {
                        treeNode.enableDelete = true;
                        deleteTargets.Add(treeNode);
                    }
                }

                foreach (TreeNodeObject target  in deleteTargets)
                {
                    Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNode(target);
                }
            }, 20);
        }

        private void SceneRead(XmlNode node, List<OCIChar> ociChars)
        {
            var ociCharByDicKey = new Dictionary<int, OCIChar>();
            foreach (var kvp in Studio.Studio.Instance.dicObjectCtrl)
            {
                var oci = kvp.Value as OCIChar;
                if (oci != null)
                    ociCharByDicKey[kvp.Key] = oci;
            }

            var ociCharByHash = new Dictionary<int, OCIChar>();
            foreach (var oci in ociChars)
            {
                if (oci == null)
                    continue;

                int hash = oci.GetChaControl().GetHashCode();
                if (!ociCharByHash.ContainsKey(hash))
                    ociCharByHash.Add(hash, oci);
            }

            var ociCharByName = new Dictionary<string, OCIChar>(StringComparer.Ordinal);
            foreach (var oci in ociChars)
            {
                if (oci == null || oci.charInfo == null)
                    continue;

                string charName = oci.charInfo.name;
                if (string.IsNullOrEmpty(charName))
                    continue;

                if (!ociCharByName.ContainsKey(charName))
                    ociCharByName.Add(charName, oci);
            }

            foreach (XmlNode charNode in node.SelectNodes("character"))
            {
                OCIChar ociChar = null;

                string dicKeyText = charNode.Attributes["dicKey"]?.Value;
                if (!string.IsNullOrEmpty(dicKeyText) && int.TryParse(dicKeyText, out int dicKeyValue))
                    ociChar = FindOciCharByDicKey(ociCharByDicKey, dicKeyValue);

                if (ociChar == null)
                {
                    string hash = charNode.Attributes["hash"]?.Value;
                    if (!string.IsNullOrEmpty(hash) && int.TryParse(hash, out int hashValue))
                        ociCharByHash.TryGetValue(hashValue, out ociChar);
                }

                if (ociChar == null)
                {
                    string charName = charNode.Attributes["name"]?.Value;
                    if (!string.IsNullOrEmpty(charName))
                        ociCharByName.TryGetValue(charName, out ociChar);
                }

                if (ociChar == null)
                {
                    continue;
                }
    
                ClothCollideVisualizerController controller = GetControl(ociChar);
                if (controller == null)
                    continue;

                PhysicCollider physicCollider = controller.GetData() ?? controller.CreateData(ociChar);
                if (physicCollider == null)
                    continue;

                foreach (XmlNode boneNode in charNode.SelectNodes("bone"))
                {
                    string colliderName = boneNode.Attributes["name"]?.Value;
                    if (string.IsNullOrEmpty(colliderName)) continue;

                    ColliderTransformInfo transformInfo = new ColliderTransformInfo();
                    string slotText = boneNode.Attributes["slot"]?.Value;
                    string legacyClothKey = boneNode.Attributes["clothKey"]?.Value;
                    int slot = SlotTop;
                    bool hasSlot = !string.IsNullOrEmpty(slotText) && int.TryParse(slotText, out slot);

                    // position
                    XmlNode posNode = boneNode.SelectSingleNode("position");
                    if (posNode != null)
                    {
                        // legacy: position Transform.localPosition
                        transformInfo.localPosition = new Vector3(
                            ParseFloat(posNode.Attributes["x"]?.Value),
                            ParseFloat(posNode.Attributes["y"]?.Value),
                            ParseFloat(posNode.Attributes["z"]?.Value)
                        );
                        transformInfo.hasLocalPosition = true;
                    }

                    // center (Collider.center)
                    XmlNode centerNode = boneNode.SelectSingleNode("center");
                    if (centerNode != null)
                    {
                        transformInfo.colliderCenter = new Vector3(
                            ParseFloat(centerNode.Attributes["x"]?.Value),
                            ParseFloat(centerNode.Attributes["y"]?.Value),
                            ParseFloat(centerNode.Attributes["z"]?.Value)
                        );
                        transformInfo.hasColliderCenter = true;
                    }

                    // rotation
                    XmlNode rotNode = boneNode.SelectSingleNode("rotation");
                    if (rotNode != null)
                    {
                        transformInfo.localEulerAngles = new Vector3(
                            ParseFloat(rotNode.Attributes["x"]?.Value),
                            ParseFloat(rotNode.Attributes["y"]?.Value),
                            ParseFloat(rotNode.Attributes["z"]?.Value)
                        );
                        transformInfo.hasLocalEulerAngles = true;
                    }

                    // scale
                    XmlNode scaleNode = boneNode.SelectSingleNode("scale");
                    if (scaleNode != null)
                    {
                        transformInfo.localScale = new Vector3(
                            ParseFloat(scaleNode.Attributes["x"]?.Value),
                            ParseFloat(scaleNode.Attributes["y"]?.Value),
                            ParseFloat(scaleNode.Attributes["z"]?.Value)
                        );
                        transformInfo.hasLocalScale = true;
                    }

                    // slot , legacy clothKey("top:...","bottom:...") .
                    if (!hasSlot && !string.IsNullOrEmpty(legacyClothKey))
                    {
                        if (legacyClothKey.StartsWith("bottom:", StringComparison.OrdinalIgnoreCase))
                            slot = SlotBottom;
                        else if (legacyClothKey.StartsWith("top:", StringComparison.OrdinalIgnoreCase))
                            slot = SlotTop;
                        else
                            slot = SlotTop; // legacy global top , bottom .
                        hasSlot = true;
                    }

                    if (!hasSlot && string.IsNullOrEmpty(legacyClothKey))
                    {
                        // )top/bottom .
                        physicCollider.topColliderTransformInfos[colliderName] = transformInfo;
                        physicCollider.bottomColliderTransformInfos[colliderName] = transformInfo;
                    }
                    else if (slot == SlotBottom)
                    {
                        physicCollider.bottomColliderTransformInfos[colliderName] = transformInfo;
                    }
                    else
                    {
                        physicCollider.topColliderTransformInfos[colliderName] = transformInfo;
                        if (!string.IsNullOrEmpty(legacyClothKey) && legacyClothKey.Equals("global", StringComparison.OrdinalIgnoreCase))
                            physicCollider.bottomColliderTransformInfos[colliderName] = transformInfo;
                    }
                }

                ApplySavedTransformsForSlots(ociChar.GetChaControl(), physicCollider);

                if (physicCollider.visualColliderAdded)
                {
                    ForceRefreshVisualColliders(ociChar);
                }
            }
        }

        private static string GetColliderKey(Collider collider)
        {
            if (collider == null)
                return string.Empty;

            string sourceName = collider.name ?? string.Empty;
            int idx = sourceName.IndexOf('-');
            return idx >= 0 ? sourceName.Substring(0, idx) : sourceName;
        }

        private static IEnumerable<Collider> GetCollidersUsedByCloth(GameObject clothObj)
        {
            if (clothObj == null)
                return Enumerable.Empty<Collider>();

            Cloth[] clothes = clothObj.GetComponentsInChildren<Cloth>(true);
            if (clothes == null || clothes.Length == 0)
                return Enumerable.Empty<Collider>();

            HashSet<Collider> result = new HashSet<Collider>();
            foreach (Cloth cloth in clothes)
            {
                if (cloth == null)
                    continue;

                CapsuleCollider[] capsules = cloth.capsuleColliders ?? Array.Empty<CapsuleCollider>();
                foreach (CapsuleCollider c in capsules)
                {
                    if (c != null)
                        result.Add(c);
                }

                ClothSphereColliderPair[] spherePairs = cloth.sphereColliders ?? Array.Empty<ClothSphereColliderPair>();
                foreach (ClothSphereColliderPair pair in spherePairs)
                {
                    if (pair.first != null)
                        result.Add(pair.first);
                    if (pair.second != null)
                        result.Add(pair.second);
                }
            }

            return result;
        }

        private static bool HasPhysicsCloth(GameObject clothObj)
        {
            return clothObj != null && clothObj.GetComponentsInChildren<Cloth>(true).Length > 0;
        }

        private static bool TryGetColliderCenter(Collider collider, out Vector3 center)
        {
            center = Vector3.zero;
            if (collider == null)
                return false;

            if (collider is SphereCollider sphere)
            {
                center = sphere.center;
                return true;
            }
            if (collider is CapsuleCollider capsule)
            {
                center = capsule.center;
                return true;
            }
            if (collider is BoxCollider box)
            {
                center = box.center;
                return true;
            }

            return false;
        }

        private static Vector3 GetColliderCenterOrDefault(Collider collider)
        {
            return TryGetColliderCenter(collider, out Vector3 center) ? center : Vector3.zero;
        }

        private static bool TrySetColliderCenter(Collider collider, Vector3 center)
        {
            if (collider == null)
                return false;

            if (collider is SphereCollider sphere)
            {
                sphere.center = center;
                return true;
            }
            if (collider is CapsuleCollider capsule)
            {
                capsule.center = center;
                return true;
            }
            if (collider is BoxCollider box)
            {
                box.center = center;
                return true;
            }

            return false;
        }

        private static void EnsureDefaultColliderBaseline(Collider collider, PhysicCollider physicCollider)
        {
            if (collider == null || physicCollider == null)
                return;

            if (physicCollider.colliderDefaultTransforms == null)
                physicCollider.colliderDefaultTransforms = new Dictionary<int, ColliderTransformInfo>();

            int id = collider.GetInstanceID();
            if (physicCollider.colliderDefaultTransforms.ContainsKey(id))
                return;

            Transform tr = collider.transform;
            physicCollider.colliderDefaultTransforms[id] = new ColliderTransformInfo
            {
                localPosition = tr.localPosition,
                localEulerAngles = tr.localEulerAngles,
                localScale = tr.localScale,
                colliderCenter = GetColliderCenterOrDefault(collider),
                hasLocalPosition = true,
                hasLocalEulerAngles = true,
                hasLocalScale = true,
                hasColliderCenter = true
            };
        }

        private static void ResetSlotCollidersToDefault(ChaControl chaCtrl, PhysicCollider physicCollider, int slot)
        {
            if (chaCtrl == null || physicCollider == null)
                return;

            GameObject clothObj = (chaCtrl.objClothes != null && slot >= 0 && slot < chaCtrl.objClothes.Length) ? chaCtrl.objClothes[slot] : null;
            foreach (Collider collider in GetCollidersUsedByCloth(clothObj))
            {
                if (collider == null)
                    continue;

                EnsureDefaultColliderBaseline(collider, physicCollider);

                int id = collider.GetInstanceID();
                if (physicCollider.colliderDefaultTransforms != null
                    && physicCollider.colliderDefaultTransforms.TryGetValue(id, out ColliderTransformInfo baseline)
                    && baseline != null)
                {
                    Transform tr = collider.transform;
                    if (baseline.hasLocalPosition) tr.localPosition = baseline.localPosition;
                    if (baseline.hasLocalEulerAngles) tr.localEulerAngles = baseline.localEulerAngles;
                    if (baseline.hasLocalScale) tr.localScale = baseline.localScale;
                    if (baseline.hasColliderCenter) TrySetColliderCenter(collider, baseline.colliderCenter);
                }
            }
        }

        private static void RebuildColliderSlotCache(ChaControl chaCtrl, PhysicCollider physicCollider)
        {
            if (chaCtrl == null || physicCollider == null)
                return;

            if (physicCollider.colliderInstanceIdToSlot == null)
                physicCollider.colliderInstanceIdToSlot = new Dictionary<int, int>();
            physicCollider.colliderInstanceIdToSlot.Clear();

            GameObject topObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 0) ? chaCtrl.objClothes[0] : null;
            GameObject bottomObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 1) ? chaCtrl.objClothes[1] : null;

            foreach (Collider collider in GetCollidersUsedByCloth(topObj))
            {
                if (collider == null)
                    continue;
                int id = collider.GetInstanceID();
                if (!physicCollider.colliderInstanceIdToSlot.ContainsKey(id))
                    physicCollider.colliderInstanceIdToSlot[id] = SlotTop;
            }

            foreach (Collider collider in GetCollidersUsedByCloth(bottomObj))
            {
                if (collider == null)
                    continue;
                int id = collider.GetInstanceID();
                if (!physicCollider.colliderInstanceIdToSlot.ContainsKey(id))
                    physicCollider.colliderInstanceIdToSlot[id] = SlotBottom;
            }
        }

        private static void ApplySavedTransformsForSlots(ChaControl chaCtrl, PhysicCollider physicCollider)
        {
            if (chaCtrl == null || physicCollider == null)
                return;

            GameObject topObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 0) ? chaCtrl.objClothes[0] : null;
            GameObject bottomObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 1) ? chaCtrl.objClothes[1] : null;

            // Build a fast lookup for currently used colliders by slot.
            foreach (Collider collider in GetCollidersUsedByCloth(topObj))
            {
                if (collider == null)
                    continue;

                EnsureDefaultColliderBaseline(collider, physicCollider);

                string colliderKey = GetColliderKey(collider);
                if (string.IsNullOrEmpty(colliderKey))
                    continue;

                if (physicCollider.topColliderTransformInfos != null
                    && physicCollider.topColliderTransformInfos.TryGetValue(colliderKey, out ColliderTransformInfo info)
                    && info != null)
                {
                    Transform tr = collider.transform;
                    if (info.hasLocalPosition) tr.localPosition = info.localPosition;
                    if (info.hasLocalEulerAngles) tr.localEulerAngles = info.localEulerAngles;
                    if (info.hasLocalScale) tr.localScale = info.localScale;
                    if (info.hasColliderCenter) TrySetColliderCenter(collider, info.colliderCenter);
                }
            }

            foreach (Collider collider in GetCollidersUsedByCloth(bottomObj))
            {
                if (collider == null)
                    continue;

                EnsureDefaultColliderBaseline(collider, physicCollider);

                string colliderKey = GetColliderKey(collider);
                if (string.IsNullOrEmpty(colliderKey))
                    continue;

                if (physicCollider.bottomColliderTransformInfos != null
                    && physicCollider.bottomColliderTransformInfos.TryGetValue(colliderKey, out ColliderTransformInfo info)
                    && info != null)
                {
                    Transform tr = collider.transform;
                    if (info.hasLocalPosition) tr.localPosition = info.localPosition;
                    if (info.hasLocalEulerAngles) tr.localEulerAngles = info.localEulerAngles;
                    if (info.hasLocalScale) tr.localScale = info.localScale;
                    if (info.hasColliderCenter) TrySetColliderCenter(collider, info.colliderCenter);
                }
            }
        }

        private static void SaveCurrentTransformsForSlots(ChaControl chaCtrl, PhysicCollider physicCollider)
        {
            if (chaCtrl == null || physicCollider == null)
                return;

            GameObject topObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 0) ? chaCtrl.objClothes[0] : null;
            GameObject bottomObj = (chaCtrl.objClothes != null && chaCtrl.objClothes.Length > 1) ? chaCtrl.objClothes[1] : null;

            foreach (Collider collider in GetCollidersUsedByCloth(topObj))
            {
                if (collider == null)
                    continue;

                EnsureDefaultColliderBaseline(collider, physicCollider);

                string colliderKey = GetColliderKey(collider);
                if (string.IsNullOrEmpty(colliderKey))
                    continue;

                Transform tr = collider.transform;
                physicCollider.topColliderTransformInfos[colliderKey] = new ColliderTransformInfo
                {
                    localPosition = tr.localPosition,
                    localEulerAngles = tr.localEulerAngles,
                    localScale = tr.localScale,
                    colliderCenter = GetColliderCenterOrDefault(collider),
                    hasLocalPosition = true,
                    hasLocalEulerAngles = true,
                    hasLocalScale = true,
                    hasColliderCenter = true
                };
            }

            foreach (Collider collider in GetCollidersUsedByCloth(bottomObj))
            {
                if (collider == null)
                    continue;

                EnsureDefaultColliderBaseline(collider, physicCollider);

                string colliderKey = GetColliderKey(collider);
                if (string.IsNullOrEmpty(colliderKey))
                    continue;

                Transform tr = collider.transform;
                physicCollider.bottomColliderTransformInfos[colliderKey] = new ColliderTransformInfo
                {
                    localPosition = tr.localPosition,
                    localEulerAngles = tr.localEulerAngles,
                    localScale = tr.localScale,
                    colliderCenter = GetColliderCenterOrDefault(collider),
                    hasLocalPosition = true,
                    hasLocalEulerAngles = true,
                    hasLocalScale = true,
                    hasColliderCenter = true
                };
            }
        }

        private static bool TryGetSlotFromChangeClothesKind(int kind, out int slot)
        {
            // chaCtrl.objClothes 0=top, 1=bottom) .
            // Ignore unsupported clothing kinds and fall back to top.
            slot = SlotTop;
            if (kind == SlotTop)
            {
                slot = SlotTop;
                return true;
            }
            if (kind == SlotBottom)
            {
                slot = SlotBottom;
                return true;
            }
            return false;
        }

        // : float .
        private float ParseFloat(string value)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
            return 0f;
        }

        private void SceneWrite(string path, XmlTextWriter writer)
        {
            List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();

            List<OCIChar> ociChars = dic
                .Select(kv => kv.Value as OCIChar)   // ObjectCtrlInfo OCIChar .Where(c => c != null) // null
                .ToList();

            foreach (OCIChar ociChar in ociChars)
            {
                int dicKey;
                bool hasDicKey = TryGetDicKey(ociChar.GetChaControl(), out dicKey);
                ClothCollideVisualizerController controller = GetControl(ociChar);
                PhysicCollider physicCollider = controller != null ? (controller.GetData() ?? controller.CreateData(ociChar)) : null;
                if (physicCollider != null)
                    SaveCurrentTransformsForSlots(ociChar.GetChaControl(), physicCollider);

                writer.WriteStartElement("character");
                if (hasDicKey)
                    writer.WriteAttributeString("dicKey", dicKey.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("hash", ociChar.GetChaControl().GetHashCode().ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("name", ociChar.charInfo != null ? ociChar.charInfo.name : string.Empty);

                foreach (var kv in (physicCollider != null ? physicCollider.topColliderTransformInfos : null)
                             ?? new Dictionary<string, ColliderTransformInfo>())
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
                        continue;

                    writer.WriteStartElement("bone");
                    writer.WriteAttributeString("name", kv.Key);
                    writer.WriteAttributeString("slot", SlotTop.ToString(CultureInfo.InvariantCulture));

                    if (kv.Value.hasLocalPosition)
                    {
                        writer.WriteStartElement("position");
                        writer.WriteAttributeString("x", kv.Value.localPosition.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localPosition.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localPosition.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasColliderCenter)
                    {
                        writer.WriteStartElement("center");
                        writer.WriteAttributeString("x", kv.Value.colliderCenter.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.colliderCenter.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.colliderCenter.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasLocalEulerAngles)
                    {
                        writer.WriteStartElement("rotation");
                        writer.WriteAttributeString("x", kv.Value.localEulerAngles.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localEulerAngles.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localEulerAngles.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasLocalScale)
                    {
                        writer.WriteStartElement("scale");
                        writer.WriteAttributeString("x", kv.Value.localScale.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localScale.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localScale.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }

                foreach (var kv in (physicCollider != null ? physicCollider.bottomColliderTransformInfos : null)
                             ?? new Dictionary<string, ColliderTransformInfo>())
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
                        continue;

                    writer.WriteStartElement("bone");
                    writer.WriteAttributeString("name", kv.Key);
                    writer.WriteAttributeString("slot", SlotBottom.ToString(CultureInfo.InvariantCulture));

                    if (kv.Value.hasLocalPosition)
                    {
                        writer.WriteStartElement("position");
                        writer.WriteAttributeString("x", kv.Value.localPosition.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localPosition.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localPosition.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasColliderCenter)
                    {
                        writer.WriteStartElement("center");
                        writer.WriteAttributeString("x", kv.Value.colliderCenter.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.colliderCenter.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.colliderCenter.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasLocalEulerAngles)
                    {
                        writer.WriteStartElement("rotation");
                        writer.WriteAttributeString("x", kv.Value.localEulerAngles.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localEulerAngles.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localEulerAngles.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (kv.Value.hasLocalScale)
                    {
                        writer.WriteStartElement("scale");
                        writer.WriteAttributeString("x", kv.Value.localScale.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", kv.Value.localScale.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("z", kv.Value.localScale.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement(); // character
            }             
        }

        private bool TryGetDicKey(ChaControl chaCtrl, out int dicKey)
        {
            dicKey = 0;
            if (chaCtrl == null)
                return false;

            foreach (var kvp in Studio.Studio.Instance.dicObjectCtrl)
            {
                var oci = kvp.Value as OCIChar;
                if (oci == null)
                    continue;

                if (oci.GetChaControl() == chaCtrl)
                {
                    dicKey = kvp.Key;
                    return true;
                }
            }

            return false;
        }

        private OCIChar FindOciCharByDicKey(Dictionary<int, OCIChar> map, int savedDicKey)
        {
            if (map.TryGetValue(savedDicKey, out var oci))
                return oci;

            return null;
        }
#endif
    }
}
