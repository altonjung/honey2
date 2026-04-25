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

namespace ExpressionSlider
{
    public partial class ExpressionSlider
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

                ExpressionSliderData data = GetDataAndCreate(ociChar);

                foreach (XmlNode boneNode in charNode.SelectNodes("config"))
                {
                    string configName = boneNode.Attributes["name"]?.Value;
                    if (string.IsNullOrEmpty(configName)) continue;
                    if (!string.Equals(configName, "expression", StringComparison.OrdinalIgnoreCase))
                        continue;
    
                    data.EyeBallCategory = Mathf.Clamp(ReadInt(boneNode, "eyeBallCategory", data.EyeBallCategory), 0, 1);
                    data.EyeBallEditTarget = Mathf.Clamp(ReadInt(boneNode, "eyeBallEditTarget", data.EyeBallEditTarget), 0, 2);
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
                }
            }
        }

        private void SceneWrite(string path, XmlTextWriter writer)
        {
            foreach (TreeNodeObject treeNode in Singleton<Studio.Studio>.Instance.treeNodeCtrl.m_TreeNodeObject)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(treeNode);
                OCIChar ociChar = objectCtrlInfo as OCIChar;

                ExpressionSliderData data = GetDataAndCreate(ociChar);

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
                    WriteValueNode(writer, "eyeBallLeftX", data.EyeBallLeftX);
                    WriteValueNode(writer, "eyeBallLeftY", data.EyeBallLeftY);
                    WriteValueNode(writer, "eyeBallRightX", data.EyeBallRightX);
                    WriteValueNode(writer, "eyeBallRightY", data.EyeBallRightY);

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
