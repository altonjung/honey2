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
using static CharaUtils.Expression;

namespace FacialQuickTransform
{
    public partial class FacialQuickTransform
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
                data.version = _saveVersion;
                data.data.Add("sceneInfo", stringWriter.ToString());

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

                string name = oci.charInfo.name;
                if (string.IsNullOrEmpty(name))
                    continue;

                if (!ociCharByName.ContainsKey(name))
                    ociCharByName.Add(name, oci);
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
                    string name = charNode.Attributes["name"]?.Value;
                    if (!string.IsNullOrEmpty(name))
                        ociCharByName.TryGetValue(name, out ociChar);
                }

                if (ociChar == null)
                {
                    continue;
                }

                FacialQuickTransformData data = GetDataAndCreate(ociChar);

                foreach (XmlNode boneNode in charNode.SelectNodes("config"))
                {
                    string configName = boneNode.Attributes["name"]?.Value;
                    if (string.IsNullOrEmpty(configName)) continue;
                    if (!string.Equals(configName, "expression", StringComparison.OrdinalIgnoreCase))
                        continue;
    
                    data.EyeBallCategory = Mathf.Clamp(ReadInt(boneNode, "eyeBallCategory", data.EyeBallCategory), 0, 1);
                    data.EyeBallEditTarget = Mathf.Clamp(ReadInt(boneNode, "eyeBallEditTarget", data.EyeBallEditTarget), 0, 2);
                    data.EyebrowTypeIndex = Mathf.Clamp(ReadInt(boneNode, "eyebrowTypeIndex", data.EyebrowTypeIndex), 0, 8);
                    data.EyeTypeIndex = Mathf.Clamp(ReadInt(boneNode, "eyeTypeIndex", data.EyeTypeIndex), 0, 20);
                    data.EyeOpenMax = Mathf.Clamp(ReadFloat(boneNode, "eyeOpenMax", data.EyeOpenMax), 0f, 1f);
                    bool hasLeftX = HasNode(boneNode, "eyeBallLeftX");
                    bool hasLeftY = HasNode(boneNode, "eyeBallLeftY");
                    bool hasRightX = HasNode(boneNode, "eyeBallRightX");
                    bool hasRightY = HasNode(boneNode, "eyeBallRightY");

                    if (hasLeftX)
                        data.EyeBallLeftX = ReadFloat(boneNode, "eyeBallLeftX", data.EyeBallLeftX);
                    if (hasLeftY)
                        data.EyeBallLeftY = ReadFloat(boneNode, "eyeBallLeftY", data.EyeBallLeftY);
                    if (hasRightX)
                        data.EyeBallRightX = ReadFloat(boneNode, "eyeBallRightX", data.EyeBallRightX);
                    if (hasRightY)
                        data.EyeBallRightY = ReadFloat(boneNode, "eyeBallRightY", data.EyeBallRightY);
                    data.EyeSmileInRotX = ReadFloat(boneNode, "eyeSmileInRotX", data.EyeSmileInRotX);
                    data.EyeSmileOutRotX = ReadFloat(boneNode, "eyeSmileOutRotX", data.EyeSmileOutRotX);
                    data.EyeWinkLeftRotX = ReadFloat(boneNode, "eyeWinkLeftRotX", data.EyeWinkLeftRotX);
                    data.EyeWinkRightRotX = ReadFloat(boneNode, "eyeWinkRightRotX", data.EyeWinkRightRotX);
                    data.MouthRotX = ReadFloat(boneNode, "mouthRotX", data.MouthRotX);
                    data.MouthRotY = ReadFloat(boneNode, "mouthRotY", data.MouthRotY);
                    data.MouthRotZ = ReadFloat(boneNode, "mouthRotZ", data.MouthRotZ);
                    data.MouthLipUpRotX = ReadFloat(boneNode, "mouthLipUpRotX", data.MouthLipUpRotX);
                    data.MouthLipUpRotY = ReadFloat(boneNode, "mouthLipUpRotY", data.MouthLipUpRotY);
                    data.MouthLipUpRotZ = ReadFloat(boneNode, "mouthLipUpRotZ", data.MouthLipUpRotZ);
                    data.MouthLipDnRotX = ReadFloat(boneNode, "mouthLipDnRotX", data.MouthLipDnRotX);
                    data.MouthLipDnRotY = ReadFloat(boneNode, "mouthLipDnRotY", data.MouthLipDnRotY);
                    data.MouthLipDnRotZ = ReadFloat(boneNode, "mouthLipDnRotZ", data.MouthLipDnRotZ);
                    data.MouthCavityPosZ = ReadFloat(boneNode, "mouthCavityPosZ", data.MouthCavityPosZ);
                    data.MouthTypeIndex = ReadInt(boneNode, "mouthTypeIndex", data.MouthTypeIndex);
                    data.MouthOpenMax = Mathf.Clamp(ReadFloat(boneNode, "mouthOpenMax", data.MouthOpenMax), 0f, 1f);
                    data.MouthSmileLeftPosX = ReadFloat(boneNode, "mouthSmileLeftPosX", data.MouthSmileLeftPosX);
                    data.MouthSmileLeftPosY = ReadFloat(boneNode, "mouthSmileLeftPosY", data.MouthSmileLeftPosY);
                    data.MouthSmileRightPosX = ReadFloat(boneNode, "mouthSmileRightPosX", data.MouthSmileRightPosX);
                    data.MouthSmileRightPosY = ReadFloat(boneNode, "mouthSmileRightPosY", data.MouthSmileRightPosY);
                    data.NoseRotX = ReadFloat(boneNode, "noseRotX", data.NoseRotX);
                    data.NoseRotY = ReadFloat(boneNode, "noseRotY", data.NoseRotY);
                    data.NoseRotZ = ReadFloat(boneNode, "noseRotZ", data.NoseRotZ);
                    data.NoseWingLeftRotX = ReadFloat(boneNode, "noseWingLeftRotX", data.NoseWingLeftRotX);
                    data.NoseWingLeftRotY = ReadFloat(boneNode, "noseWingLeftRotY", data.NoseWingLeftRotY);
                    data.NoseWingLeftRotZ = ReadFloat(boneNode, "noseWingLeftRotZ", data.NoseWingLeftRotZ);
                    data.NoseWingRightRotX = ReadFloat(boneNode, "noseWingRightRotX", data.NoseWingRightRotX);
                    data.NoseWingRightRotY = ReadFloat(boneNode, "noseWingRightRotY", data.NoseWingRightRotY);
                    data.NoseWingRightRotZ = ReadFloat(boneNode, "noseWingRightRotZ", data.NoseWingRightRotZ);
                    data.TongueCategoryEnabled = ReadInt(boneNode, "tongueCategoryEnabled", data.TongueCategoryEnabled ? 1 : 0) != 0;
                    data.Tongue1PosZ = ReadFloat(boneNode, "tongue1PosZ", data.Tongue1PosZ);
                    data.Tongue1RotY = ReadFloat(boneNode, "tongue1RotY", data.Tongue1RotY);
                    data.Tongue2PosZ = ReadFloat(boneNode, "tongue2PosZ", data.Tongue2PosZ);
                    data.Tongue2RotX = ReadFloat(boneNode, "tongue2RotX", data.Tongue2RotX);
                    data.Tongue2RotY = ReadFloat(boneNode, "tongue2RotY", data.Tongue2RotY);
                    data.TearDropActive = ReadInt(boneNode, "tearDropActive", data.TearDropActive ? 1 : 0) != 0;
                    data.TearDropLevel = Mathf.Clamp(ReadFloat(boneNode, "tearDropLevel", data.TearDropLevel), 0f, 1f);

                    // Backward compatibility for old sync format.
                    if (!hasLeftX || !hasRightX)
                    {
                        float syncX = ReadFloat(boneNode, "eyeBallSyncX", 0f);
                        if (!hasLeftX)
                            data.EyeBallLeftX = syncX;
                        if (!hasRightX)
                            data.EyeBallRightX = syncX;
                    }
                    if (!hasLeftY || !hasRightY)
                    {
                        float syncY = ReadFloat(boneNode, "eyeBallSyncY", 0f);
                        if (!hasLeftY)
                            data.EyeBallLeftY = syncY;
                        if (!hasRightY)
                            data.EyeBallRightY = syncY;
                    }

                    // Backward compatibility:
                    // Old v1 had mouthX/mouthY and noseX/noseY.
                    data.MouthRotX = ReadFloat(boneNode, "mouthX", data.MouthRotX);
                    data.MouthRotY = ReadFloat(boneNode, "mouthY", data.MouthRotY);
                    data.NoseRotX = ReadFloat(boneNode, "noseX", data.NoseRotX);
                    data.NoseRotY = ReadFloat(boneNode, "noseY", data.NoseRotY);

                    // Old v2 had left/right split values.
                    float mouthLeftX = ReadFloat(boneNode, "mouthLeftX", data.MouthRotX);
                    float mouthLeftY = ReadFloat(boneNode, "mouthLeftY", data.MouthRotY);
                    float mouthRightX = ReadFloat(boneNode, "mouthRightX", data.MouthRotX);
                    float mouthRightY = ReadFloat(boneNode, "mouthRightY", data.MouthRotY);
                    float noseLeftX = ReadFloat(boneNode, "noseLeftX", data.NoseRotX);
                    float noseLeftY = ReadFloat(boneNode, "noseLeftY", data.NoseRotY);
                    float noseRightX = ReadFloat(boneNode, "noseRightX", data.NoseRotX);
                    float noseRightY = ReadFloat(boneNode, "noseRightY", data.NoseRotY);

                    data.MouthRotX = (mouthLeftX + mouthRightX) * 0.5f;
                    data.MouthRotY = (mouthLeftY + mouthRightY) * 0.5f;
                    data.NoseRotX = (noseLeftX + noseRightX) * 0.5f;
                    data.NoseRotY = (noseLeftY + noseRightY) * 0.5f;
                }
            }
        }

        private void SceneWrite(string path, XmlTextWriter writer)
        {
            foreach (TreeNodeObject treeNode in Singleton<Studio.Studio>.Instance.treeNodeCtrl.m_TreeNodeObject)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(treeNode);
                OCIChar ociChar = objectCtrlInfo as OCIChar;

                FacialQuickTransformData data = GetDataAndCreate(ociChar);

                if (ociChar != null && data != null) {
                    int dicKey;
                    bool hasDicKey = TryGetDicKey(ociChar.GetChaControl(), out dicKey);

                    writer.WriteStartElement("character");
                    if (hasDicKey)
                        writer.WriteAttributeString("dicKey", dicKey.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("hash", ociChar.GetChaControl().GetHashCode().ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("name", ociChar.charInfo != null ? ociChar.charInfo.name : string.Empty);

                    writer.WriteStartElement("config");
                    writer.WriteAttributeString("name", "expression");

                    WriteIntNode(writer, "eyeBallCategory", Mathf.Clamp(data.EyeBallCategory, 0, 1));
                    WriteIntNode(writer, "eyeBallEditTarget", Mathf.Clamp(data.EyeBallEditTarget, 0, 2));
                    WriteIntNode(writer, "eyebrowTypeIndex", Mathf.Clamp(data.EyebrowTypeIndex, 0, 8));
                    WriteIntNode(writer, "eyeTypeIndex", Mathf.Clamp(data.EyeTypeIndex, 0, 20));
                    WriteValueNode(writer, "eyeOpenMax", Mathf.Clamp(data.EyeOpenMax, 0f, 1f));
                    WriteValueNode(writer, "eyeBallLeftX", data.EyeBallLeftX);
                    WriteValueNode(writer, "eyeBallLeftY", data.EyeBallLeftY);
                    WriteValueNode(writer, "eyeBallRightX", data.EyeBallRightX);
                    WriteValueNode(writer, "eyeBallRightY", data.EyeBallRightY);
                    WriteValueNode(writer, "eyeSmileInRotX", data.EyeSmileInRotX);
                    WriteValueNode(writer, "eyeSmileOutRotX", data.EyeSmileOutRotX);
                    WriteValueNode(writer, "eyeWinkLeftRotX", data.EyeWinkLeftRotX);
                    WriteValueNode(writer, "eyeWinkRightRotX", data.EyeWinkRightRotX);
                    WriteValueNode(writer, "mouthRotX", data.MouthRotX);
                    WriteValueNode(writer, "mouthRotY", data.MouthRotY);
                    WriteValueNode(writer, "mouthRotZ", data.MouthRotZ);
                    WriteValueNode(writer, "mouthLipUpRotX", data.MouthLipUpRotX);
                    WriteValueNode(writer, "mouthLipUpRotY", data.MouthLipUpRotY);
                    WriteValueNode(writer, "mouthLipUpRotZ", data.MouthLipUpRotZ);
                    WriteValueNode(writer, "mouthLipDnRotX", data.MouthLipDnRotX);
                    WriteValueNode(writer, "mouthLipDnRotY", data.MouthLipDnRotY);
                    WriteValueNode(writer, "mouthLipDnRotZ", data.MouthLipDnRotZ);
                    WriteValueNode(writer, "mouthCavityPosZ", data.MouthCavityPosZ);
                    WriteIntNode(writer, "mouthTypeIndex", data.MouthTypeIndex);
                    WriteValueNode(writer, "mouthOpenMax", Mathf.Clamp(data.MouthOpenMax, 0f, 1f));
                    WriteValueNode(writer, "mouthSmileLeftPosX", data.MouthSmileLeftPosX);
                    WriteValueNode(writer, "mouthSmileLeftPosY", data.MouthSmileLeftPosY);
                    WriteValueNode(writer, "mouthSmileRightPosX", data.MouthSmileRightPosX);
                    WriteValueNode(writer, "mouthSmileRightPosY", data.MouthSmileRightPosY);
                    WriteValueNode(writer, "noseRotX", data.NoseRotX);
                    WriteValueNode(writer, "noseRotY", data.NoseRotY);
                    WriteValueNode(writer, "noseRotZ", data.NoseRotZ);
                    WriteValueNode(writer, "noseWingLeftRotX", data.NoseWingLeftRotX);
                    WriteValueNode(writer, "noseWingLeftRotY", data.NoseWingLeftRotY);
                    WriteValueNode(writer, "noseWingLeftRotZ", data.NoseWingLeftRotZ);
                    WriteValueNode(writer, "noseWingRightRotX", data.NoseWingRightRotX);
                    WriteValueNode(writer, "noseWingRightRotY", data.NoseWingRightRotY);
                    WriteValueNode(writer, "noseWingRightRotZ", data.NoseWingRightRotZ);
                    WriteIntNode(writer, "tongueCategoryEnabled", data.TongueCategoryEnabled ? 1 : 0);
                    WriteValueNode(writer, "tongue1PosZ", data.Tongue1PosZ);
                    WriteValueNode(writer, "tongue1RotY", data.Tongue1RotY);
                    WriteValueNode(writer, "tongue2PosZ", data.Tongue2PosZ);
                    WriteValueNode(writer, "tongue2RotX", data.Tongue2RotX);
                    WriteValueNode(writer, "tongue2RotY", data.Tongue2RotY);
                    WriteIntNode(writer, "tearDropActive", data.TearDropActive ? 1 : 0);
                    WriteValueNode(writer, "tearDropLevel", Mathf.Clamp(data.TearDropLevel, 0f, 1f));

                    writer.WriteEndElement(); // config
                
                    writer.WriteEndElement(); // character
                }
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

        private float ParseFloat(string value)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
            return 0f;
        }        

        private int ParseInt(string value, int fallback)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                return result;
            return fallback;
        }

        private float ReadFloat(XmlNode parent, string nodeName, float fallback)
        {
            XmlNode valueNode = parent.SelectSingleNode(nodeName);
            if (valueNode == null)
                return fallback;
            return ParseFloat(valueNode.Attributes["value"]?.Value);
        }

        private int ReadInt(XmlNode parent, string nodeName, int fallback)
        {
            XmlNode valueNode = parent.SelectSingleNode(nodeName);
            if (valueNode == null)
                return fallback;
            return ParseInt(valueNode.Attributes["value"]?.Value, fallback);
        }

        private bool HasNode(XmlNode parent, string nodeName)
        {
            return parent.SelectSingleNode(nodeName) != null;
        }

        private void WriteValueNode(XmlTextWriter writer, string nodeName, float value)
        {
            writer.WriteStartElement(nodeName);
            writer.WriteAttributeString("value", value.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        private void WriteIntNode(XmlTextWriter writer, string nodeName, int value)
        {
            writer.WriteStartElement(nodeName);
            writer.WriteAttributeString("value", value.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

#endif
    }
}
