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

namespace ClothQuickTransform
{
    public partial class ClothQuickTransform
    {
#if FEATURE_SCENE_SAVE
        // Writes current adjustment data into scene extended data.
        private void OnSceneSave(string path)
        {
            using (StringWriter stringWriter = new StringWriter())
            using (XmlTextWriter xmlWriter = new XmlTextWriter(stringWriter))
            {
                xmlWriter.WriteStartElement("root");
                SceneWrite(path, xmlWriter);
                xmlWriter.WriteEndElement();

                PluginData data = new PluginData();
                data.version = _saveVersion;
                data.data.Add("sceneInfo", stringWriter.ToString());
                // UnityEngine.Debug.Log($">> CTS SceneSave xml:\n{stringWriter}");
                ExtendedSave.SetSceneExtendedDataById(_extSaveKey, data);
            }
        }

        // Restores saved adjustment data when a scene is loaded.
        private void OnSceneLoad(string path)
        {
            PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
            if (data == null)
                return;
            //if (data.data != null && data.data.ContainsKey("sceneInfo"))
            //     UnityEngine.Debug.Log($">> CTS SceneLoad xml:\n{data.data["sceneInfo"]}");

            XmlDocument doc = new XmlDocument();
            doc.LoadXml((string)data.data["sceneInfo"]);
            XmlNode node = doc.FirstChild;
            if (node == null)
                return;
            SceneLoad(path, node);
        }

        // Imports saved adjustment data from scene extended data.
        private void OnSceneImport(string path)
        {
            Logger.LogMessage($"Import not support");
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
            }, 20);
        }

        // Parses scene XML and applies saved data to matching characters.
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
                string name = oci.charInfo.name;
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!ociCharByName.ContainsKey(name))
                    ociCharByName.Add(name, oci);
            }

            int restoredChars = 0;
            int restoredTransfers = 0;

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
                    string name = charNode.Attributes["name"]?.Value;
                    if (!string.IsNullOrEmpty(name))
                        ociCharByName.TryGetValue(name, out ociChar);
                }

                if (ociChar == null)
                    continue;

                var savedBySlot = new Dictionary<int, Dictionary<string, SavedAdjustment>>();
                foreach (XmlNode transferNode in charNode.SelectNodes("transfer"))
                {
                    string boneName = NormalizeBoneName(transferNode.Attributes["name"]?.Value);
                    if (string.IsNullOrEmpty(boneName))
                        continue;

                    int slotIndex = 0;
                    var slotAttr = transferNode.Attributes["slot"];
                    if (slotAttr != null && int.TryParse(slotAttr.Value, out int slotValue))
                        slotIndex = slotValue;

                    var adj = new SavedAdjustment
                    {
                        position = new Vector3(
                            ReadFloat(transferNode, "posX"),
                            ReadFloat(transferNode, "posY"),
                            ReadFloat(transferNode, "posZ")),
                        rotation = new Vector3(
                            ReadFloat(transferNode, "rotX"),
                            ReadFloat(transferNode, "rotY"),
                            ReadFloat(transferNode, "rotZ")),
                        scale = new Vector3(
                            ReadFloat(transferNode, "scaleX", 1f),
                            ReadFloat(transferNode, "scaleY", 1f),
                            ReadFloat(transferNode, "scaleZ", 1f))
                    };

                    if (!savedBySlot.TryGetValue(slotIndex, out var slotMap))
                    {
                        slotMap = new Dictionary<string, SavedAdjustment>();
                        savedBySlot[slotIndex] = slotMap;
                    }
                    slotMap[boneName] = adj;
                }

                int dicKey;
                bool hasDicKey = TryGetDicKey(ociChar.GetChaControl(), out dicKey);
                int savedCount = savedBySlot.Values.Sum(v => v != null ? v.Count : 0);
                // UnityEngine.Debug.Log($">> SceneRead char dicKey={(hasDicKey dicKey.ToString() : "not-found")} savedTransfers={savedCount}");

                var mapData = GetDataAndCreate(ociChar);
                if (mapData != null)
                {
                    mapData.savedAdjustmentsBySlot = savedBySlot;
                    mapData.transferEntriesBySlot = new Dictionary<int, List<TransferEntry>>();
                    mapData.selectedTransferIndexBySlot = new Dictionary<int, int>();

                    // SceneRead / .
                    // Apply saved transforms after slot remap has been prepared.
                    var chaCtrl = ociChar.GetChaControl();
                    if (chaCtrl != null && savedBySlot != null)
                    {
                        foreach (var kv in savedBySlot)
                        {
                            int slotIndex = kv.Key;
                            var slotMap = kv.Value;
                            if (slotMap == null || slotMap.Count == 0)
                                continue;

                            if (TryMarkPendingAutoRemap(chaCtrl, slotIndex))
                                StartCoroutine(AutoMapDelayedForSlot(chaCtrl, slotIndex, false));
                        }
                    }
                }

                restoredChars++;
                restoredTransfers += savedCount;
            }

            if (_currentOCIChar != null)
                RefreshMappingsForSelection(_currentOCIChar);
        }

        // Serializes per-character adjustment data into scene XML.
        private void SceneWrite(string path, XmlTextWriter writer)
        {
            var dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
            var ociCharByDicKey = dic
                .Where(kv => kv.Value is OCIChar)
                .ToDictionary(kv => kv.Key, kv => kv.Value as OCIChar);
            int activeChars = ociCharByDicKey.Count;
            int adjustmentChars = 0;

            foreach (var kv in ociCharByDicKey)
            {
                int dicKey = kv.Key;
                OCIChar ociChar = kv.Value;
                if (ociChar == null)
                    continue;

                var mapData = GetDataAndCreate(ociChar);
                if (mapData == null)
                    continue;

                if (mapData.savedAdjustmentsBySlot == null)
                    mapData.savedAdjustmentsBySlot = new Dictionary<int, Dictionary<string, SavedAdjustment>>();

                if (mapData.transferEntriesBySlot != null)
                {
                    foreach (var kvp in mapData.transferEntriesBySlot)
                    {
                        var list = kvp.Value;
                        if (list == null || list.Count == 0)
                            continue;
                        mapData.savedAdjustmentsBySlot[kvp.Key] = CaptureAdjustments(list);
                    }
                }

                var mapBySlot = mapData.savedAdjustmentsBySlot;
                if (mapBySlot == null || mapBySlot.Count == 0)
                    continue;

                adjustmentChars++;
                // UnityEngine.Debug.Log($">> SceneWrite char dicKey={dicKey} transfers={map.Count}");

                writer.WriteStartElement("character");
                writer.WriteAttributeString("dicKey", dicKey.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("hash", ociChar.GetChaControl().GetHashCode().ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("name", ociChar.charInfo != null ? ociChar.charInfo.name : string.Empty);

                foreach (var slotEntry in mapBySlot)
                {
                    int slotIndex = slotEntry.Key;
                    var map = slotEntry.Value;
                    if (map == null || map.Count == 0)
                        continue;

                    foreach (var entry in map)
                    {
                        writer.WriteStartElement("transfer");
                        writer.WriteAttributeString("slot", slotIndex.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("name", entry.Key);
                        writer.WriteAttributeString("posX", entry.Value.position.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("posY", entry.Value.position.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("posZ", entry.Value.position.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("rotX", entry.Value.rotation.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("rotY", entry.Value.rotation.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("rotZ", entry.Value.rotation.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("scaleX", entry.Value.scale.x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("scaleY", entry.Value.scale.y.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("scaleZ", entry.Value.scale.z.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }
                }

                writer.WriteEndElement();
            }
            // UnityEngine.Debug.Log($">> SceneWrite adjustments chars={adjustmentChars} activeChars={activeChars}");
        }
#endif
    }
}
