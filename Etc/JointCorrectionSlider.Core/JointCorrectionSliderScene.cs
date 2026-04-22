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

namespace JointCorrectionSlider
{
    public partial class JointCorrectionSlider
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

                JointCorrectionSliderData data = GetDataAndCreate(ociChar);

                foreach (XmlNode boneNode in charNode.SelectNodes("config"))
                {
                    string configName = boneNode.Attributes["name"]?.Value;
                    if (string.IsNullOrEmpty(configName)) continue;
                    if (!string.Equals(configName, "correction", StringComparison.OrdinalIgnoreCase))
                        continue;
    
                    data.LeftShoulderValue = ReadFloat(boneNode, "leftShoulder", data.LeftShoulderValue);
                    data.RightShoulderValue = ReadFloat(boneNode, "rightShoulder", data.RightShoulderValue);
                    data.ShoulderValue = ReadFloat(
                        boneNode,
                        "shoulder",
                        (data.LeftShoulderValue + data.RightShoulderValue) * 0.5f);
                    data.LeftArmUpperValue = ReadFloat(boneNode, "leftArm", data.LeftArmUpperValue);
                    data.RightArmUpperValue = ReadFloat(boneNode, "rightArm", data.RightArmUpperValue);
                    data.LeftArmLowerValue = ReadFloat(boneNode, "leftArmLower", data.LeftArmLowerValue);
                    data.RightArmLowerValue = ReadFloat(boneNode, "rightArmLower", data.RightArmLowerValue);
                    data.LeftElbowValue = ReadFloat(boneNode, "leftElbow", data.LeftElbowValue);
                    data.RightElbowValue = ReadFloat(boneNode, "rightElbow", data.RightElbowValue);
                    data.LeftLegValue = ReadFloat(boneNode, "leftLeg", data.LeftLegValue);
                    data.RightLegValue = ReadFloat(boneNode, "rightLeg", data.RightLegValue);
                    data.ThighValue = ReadFloat(
                        boneNode,
                        "thigh",
                        (data.LeftLegValue + data.RightLegValue) * 0.5f);
                    data.LeftKneeValue = ReadFloat(boneNode, "leftKnee", data.LeftKneeValue);
                    data.RightKneeValue = ReadFloat(boneNode, "rightKnee", data.RightKneeValue);
#if FEATURE_DAN_CORRECTION
                    data.DanRootScaleValue = ReadFloat(boneNode, "danRootScale", data.DanRootScaleValue);
                    data.DanRootPosValue = ReadFloat(boneNode, "danRootLength", data.DanRootPosValue);
                    data.DanRootRotateValue = ReadFloat(boneNode, "danRootRotate", data.DanRootRotateValue);
                    data.DanTop1ScaleValue = ReadFloat(boneNode, "danTop1Scale", data.DanTop1ScaleValue);
                    data.DanTop1PosValue = ReadFloat(boneNode, "danTop1Length", data.DanTop1PosValue);
                    data.DanTop1RotateValue = ReadFloat(boneNode, "danTop1Rotate", data.DanTop1RotateValue);
                    data.DanTop2ScaleValue = ReadFloat(boneNode, "danTop2Scale", data.DanTop2ScaleValue);
                    data.DanTop2PosValue = ReadFloat(boneNode, "danTop2Length", data.DanTop2PosValue);
                    data.DanTop2RotateValue = ReadFloat(boneNode, "danTop2Rotate", data.DanTop2RotateValue);
                    data.DanTop3ScaleValue = ReadFloat(boneNode, "danTop3Scale", data.DanTop3ScaleValue);
                    data.DanTop3PosValue = ReadFloat(boneNode, "danTop3Length", data.DanTop3PosValue);
                    data.DanTop3RotateValue = ReadFloat(boneNode, "danTop3Rotate", data.DanTop3RotateValue);
                    data.DanTop4ScaleValue = ReadFloat(boneNode, "danTop4Scale", data.DanTop4ScaleValue);
                    data.DanTop4PosValue = ReadFloat(boneNode, "DanRootLength", data.DanTop4PosValue);
                    data.DanTop4RotateValue = ReadFloat(boneNode, "danTop4Rotate", data.DanTop4RotateValue);
#endif
#if FEATURE_BUTT_CORRECTION
                    float siriPosX = ReadFloat(
                        boneNode,
                        "siriPosX",
                        ReadFloat(boneNode, "siriPos", data.SiriPosLValue));
                    float siriPosY = ReadFloat(
                        boneNode,
                        "siriPosY",
                        ReadFloat(boneNode, "siriPos", data.SiriPosRValue));
                    float siriScale = ReadFloat(
                        boneNode,
                        "siriScale",
                        ReadFloat(boneNode, "siriScaleL", data.SiriScaleLValue));
                    data.SiriPosLValue = siriPosX;
                    data.SiriPosRValue = siriPosY;
                    data.SiriScaleLValue = siriScale;
                    data.SiriScaleRValue = siriScale;
#endif
#if FEATURE_CROTCH_CORRECTION
                    data.KosiCorrectionValue = ReadFloat(
                        boneNode,
                        "kosiCorrection",
                        ReadFloat(boneNode, "kosiPosX", data.KosiCorrectionValue));
#endif
#if FEATURE_CHEST_CORRECTION
                    float munePosX = ReadFloat(
                        boneNode,
                        "munePosX",
                        ReadFloat(boneNode, "munePos", data.MunePosLValue));
                    float munePosY = ReadFloat(
                        boneNode,
                        "munePosY",
                        ReadFloat(boneNode, "munePos", data.MunePosRValue));
                    float muneScale = ReadFloat(
                        boneNode,
                        "muneScale",
                        ReadFloat(boneNode, "muneScaleL", data.MuneScaleLValue));
                    data.MunePosLValue = munePosX;
                    data.MunePosRValue = munePosY;
                    data.MuneScaleLValue = muneScale;
                    data.MuneScaleRValue = muneScale;
#endif
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                    data.fullLegValue = Mathf.Clamp(ReadInt(boneNode, "fullLeg", data.fullLegValue), 0, 100);
                    XmlNode buttchecks1Node = boneNode.SelectSingleNode("buttchecks1");
                    if (buttchecks1Node != null)
                        data.buttchecks1Value = Mathf.Clamp(ParseInt(buttchecks1Node.Attributes["value"]?.Value, data.buttchecks1Value), 0, 100);                                        
#endif
                    if (ReadScriptInfoBaseNode(boneNode, data))
                        data.ScriptInfoBaseInitialized = true;
                }
            }
        }

        private void SceneWrite(string path, XmlTextWriter writer)
        {
            foreach (TreeNodeObject treeNode in Singleton<Studio.Studio>.Instance.treeNodeCtrl.m_TreeNodeObject)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(treeNode);
                OCIChar ociChar = objectCtrlInfo as OCIChar;

                JointCorrectionSliderData data = GetDataAndCreate(ociChar);

                if (ociChar != null && data != null) {
                    int dicKey;
                    bool hasDicKey = TryGetDicKey(ociChar.GetChaControl(), out dicKey);

                    writer.WriteStartElement("character");
                    if (hasDicKey)
                        writer.WriteAttributeString("dicKey", dicKey.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("hash", ociChar.GetChaControl().GetHashCode().ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("name", ociChar.charInfo != null ? ociChar.charInfo.name : string.Empty);

                    writer.WriteStartElement("config");
                    writer.WriteAttributeString("name", "correction");
                    EnsureScriptInfoBase(ociChar, data);

                    WriteValueNode(writer, "leftShoulder", data.LeftShoulderValue);
                    WriteValueNode(writer, "rightShoulder", data.RightShoulderValue);
                    WriteValueNode(writer, "shoulder", data.ShoulderValue);

                    WriteValueNode(writer, "leftArm", data.LeftArmUpperValue);
                    WriteValueNode(writer, "rightArm", data.RightArmUpperValue);

                    WriteValueNode(writer, "leftArmLower", data.LeftArmLowerValue);
                    WriteValueNode(writer, "rightArmLower", data.RightArmLowerValue);

                    WriteValueNode(writer, "leftElbow", data.LeftElbowValue);
                    WriteValueNode(writer, "rightElbow", data.RightElbowValue);

                    WriteValueNode(writer, "leftLeg", data.LeftLegValue);
                    WriteValueNode(writer, "rightLeg", data.RightLegValue);
                    WriteValueNode(writer, "thigh", data.ThighValue);
                    
                    WriteValueNode(writer, "leftKnee", data.LeftKneeValue);
                    WriteValueNode(writer, "rightKnee", data.RightKneeValue);
                    WriteScriptInfoBaseNode(writer, data);
#if FEATURE_DAN_CORRECTION
                    WriteValueNode(writer, "danRootScale", data.DanRootScaleValue);
                    WriteValueNode(writer, "danRootLength", data.DanRootPosValue);
                    WriteValueNode(writer, "danRootRotate", data.DanRootRotateValue);
                    WriteValueNode(writer, "danTop1Scale", data.DanTop1ScaleValue);
                    WriteValueNode(writer, "danTop1Length", data.DanTop1PosValue);
                    WriteValueNode(writer, "danTop1Rotate", data.DanTop1RotateValue);
                    WriteValueNode(writer, "danTop2Scale", data.DanTop2ScaleValue);
                    WriteValueNode(writer, "danTop2Length", data.DanTop2PosValue);
                    WriteValueNode(writer, "danTop2Rotate", data.DanTop2RotateValue);
                    WriteValueNode(writer, "danTop3Scale", data.DanTop3ScaleValue);
                    WriteValueNode(writer, "danTop3Length", data.DanTop3PosValue);
                    WriteValueNode(writer, "danTop3Rotate", data.DanTop3RotateValue);
                    WriteValueNode(writer, "danTop4Scale", data.DanTop4ScaleValue);
                    WriteValueNode(writer, "DanRootLength", data.DanTop4PosValue);
                    WriteValueNode(writer, "danTop4Rotate", data.DanTop4RotateValue);
#endif
#if FEATURE_BUTT_CORRECTION
                    WriteValueNode(writer, "siriPosX", data.SiriPosLValue);
                    WriteValueNode(writer, "siriPosY", data.SiriPosRValue);
                    WriteValueNode(writer, "siriScale", data.SiriScaleLValue);
#endif
#if FEATURE_CROTCH_CORRECTION
                    WriteValueNode(writer, "kosiCorrection", data.KosiCorrectionValue);
#endif
#if FEATURE_CHEST_CORRECTION
                    WriteValueNode(writer, "munePosX", data.MunePosLValue);
                    WriteValueNode(writer, "munePosY", data.MunePosRValue);
                    WriteValueNode(writer, "muneScale", data.MuneScaleLValue);
#endif
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                    WriteIntNode(writer, "fullLeg", data.fullLegValue);
                    WriteIntNode(writer, "buttchecks1", data.buttchecks1Value);
#endif
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

        private void WriteScriptInfoBaseNode(XmlTextWriter writer, JointCorrectionSliderData data)
        {
            writer.WriteStartElement("scriptInfoBase");
            foreach (int categoryId in CorrectionCategoryIds)
            {
                if (!data.ScriptInfoBaseByCategory.TryGetValue(categoryId, out ScriptMinMax baseInfo))
                    continue;

                writer.WriteStartElement("category");
                writer.WriteAttributeString("id", categoryId.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("rxMin", baseInfo.RXMin.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("rxMax", baseInfo.RXMax.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("ryMin", baseInfo.RYMin.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("ryMax", baseInfo.RYMax.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("rzMin", baseInfo.RZMin.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("rzMax", baseInfo.RZMax.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        private bool ReadScriptInfoBaseNode(XmlNode configNode, JointCorrectionSliderData data)
        {
            XmlNode scriptInfoBaseNode = configNode.SelectSingleNode("scriptInfoBase");
            if (scriptInfoBaseNode == null)
                return false;

            data.ScriptInfoBaseByCategory.Clear();

            foreach (XmlNode categoryNode in scriptInfoBaseNode.SelectNodes("category"))
            {
                int categoryId = ParseInt(categoryNode.Attributes["id"]?.Value, -1);
                if (categoryId < 0)
                    continue;

                ScriptMinMax baseInfo = new ScriptMinMax
                {
                    RXMin = ParseFloat(categoryNode.Attributes["rxMin"]?.Value),
                    RXMax = ParseFloat(categoryNode.Attributes["rxMax"]?.Value),
                    RYMin = ParseFloat(categoryNode.Attributes["ryMin"]?.Value),
                    RYMax = ParseFloat(categoryNode.Attributes["ryMax"]?.Value),
                    RZMin = ParseFloat(categoryNode.Attributes["rzMin"]?.Value),
                    RZMax = ParseFloat(categoryNode.Attributes["rzMax"]?.Value)
                };

                data.ScriptInfoBaseByCategory[categoryId] = baseInfo;
            }

            return data.ScriptInfoBaseByCategory.Count > 0;
        }
#endif
    }
}
