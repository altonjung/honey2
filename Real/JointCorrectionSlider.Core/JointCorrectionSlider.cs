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
using UILib;

using UILib.EventHandlers;
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

/*

/*
    Agent 코드 수행

    목적:
    - 활성화된 캐릭터의 bone 조정 UI 제공

    용어:
    - OCIChar: 캐릭터 
        > GetCurrentOCI 함수를 통해 현재 씬내 활성화된 캐리터를 획득

    최소 요구 기능:
        1) onGUI 내에 아래 UI를 구성해야 한다.
            1.1) n개의 미리 정의된 bone 정보를 slider 형태로 제공             
        2) sceneWrite, sceneRead가 가능한데, 현재 씬을 저장 후 다시 복원하는 기능이다.            
            2.1) sceneWrite 함수는 씬내 각 캐릭터의 각 JointCorrectionSliderData 가 보유한 bone 이름과 bone 의 속성(position, scale) 정보를 xml에 저장한다.
            2.2) sceneRead는 함수는 scenewrite 에서 저장한 xml 정보를 다시 JointCorrectionSliderData 로 업데이트 해야 한다.

    추가 요구 기능:
        N/A


    현 버전 문제점:
        onGUI에 노출된 각 bone의 slider 값 활용 이슈 존재

    참고 사항
        category = 0  armup L
        category = 1  armup R
        category = 2  Knee L
        category = 3  Knee R
        category = 4  armLow L
        category = 5  armLow R
        category = 6  legup L, siri
        category = 7  legup R, siri
*/

namespace JointCorrectionSlider
{

#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class JointCorrectionSlider : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "JointCorrectionSlider";
        public const string Version = "0.9.1.0";
        public const string GUID = "com.alton.illusionplugins.JointCorrectionSlider";
        internal const string _ownerId = "Alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "joint_correction_slider";
#endif
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types

        enum WinkState { Idle, Playing }
        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static JointCorrectionSlider _self;

        private static string _assemblyLocation;
        internal bool _loaded = false;

        private AssetBundle _bundle;

        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;
		
        private const int _uniqueId = ('J' << 24) | ('T' << 16) | ('C' << 8) | 'S';

        private Rect _windowRect = new Rect(140, 10, 340, 10);
        private static readonly Color ModifiedEntryColor = new Color(1f, 0f, 0f, 1f);
        private static readonly Color UnmodifiedEntryColor = new Color(0.75f, 0.95f, 0.75f, 1f);
        private const float ValueCompareEpsilon = 0.001f;
        private static readonly float[] SliderStepOptions = new float[] { 1f, 0.1f, 0.01f, 0.001f };
        private int _correctionStepIndex = 2;

        private static readonly int[] CorrectionCategoryIds = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
    
        private int   _creating_char_sex = 0;

        private GUIStyle _richLabel;

        private GUIStyle RichLabel
        {
            get
            {
                if (_richLabel == null)
                {
                    _richLabel = new GUIStyle(GUI.skin.label);
                    _richLabel.richText = true;
                }
                return _richLabel;
            }
        }


        // Config

        #region Accessors
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

#if IPA
            HSExtSave.HSExtSave.RegisterHandler("rendererEditor", null, null, this.OnSceneLoad, this.OnSceneImport, this.OnSceneSave, null, null);
#elif BEPINEX
            ExtendedSave.SceneBeingLoaded += this.OnSceneLoad;
            ExtendedSave.SceneBeingImported += this.OnSceneImport;
            ExtendedSave.SceneBeingSaved += this.OnSceneSave;
#endif

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open JointCorrection window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(JointCorrectionSlider).Assembly).LoadTexture(),
                false, this, val => _ShowUI = val);
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);        

            CharacterApi.RegisterExtraBehaviour<JointCorrectionSliderController>(GUID);

            Logger.LogMessage($"{Name} {Version}.. by unbreakable dreamer");      
        }

#if SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }
#endif


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

            // xml에서 character/config 노드를 읽어 캐릭터별 보정값을 복원한다.
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

                // config 노드를 순회해 보정값을 읽는다.
                foreach (XmlNode boneNode in charNode.SelectNodes("config"))
                {
                    string configName = boneNode.Attributes["name"]?.Value;
                    if (string.IsNullOrEmpty(configName)) continue;
                    if (!string.Equals(configName, "correction", StringComparison.OrdinalIgnoreCase))
                        continue;
    
                    data.LeftShoulderValue = ReadFloat(boneNode, "leftShoulder", data.LeftShoulderValue);
                    data.RightShoulderValue = ReadFloat(boneNode, "rightShoulder", data.RightShoulderValue);
                    data.LeftArmUpperValue = ReadFloat(boneNode, "leftArm", data.LeftArmUpperValue);
                    data.RightArmUpperValue = ReadFloat(boneNode, "rightArm", data.RightArmUpperValue);
                    data.LeftArmLowerValue = ReadFloat(boneNode, "leftArmLower", data.LeftArmLowerValue);
                    data.RightArmLowerValue = ReadFloat(boneNode, "rightArmLower", data.RightArmLowerValue);
                    data.LeftElbowValue = ReadFloat(boneNode, "leftElbow", data.LeftElbowValue);
                    data.RightElbowValue = ReadFloat(boneNode, "rightElbow", data.RightElbowValue);
                    data.LeftLegValue = ReadFloat(boneNode, "leftLeg", data.LeftLegValue);
                    data.RightLegValue = ReadFloat(boneNode, "rightLeg", data.RightLegValue);
                    data.LeftKneeValue = ReadFloat(boneNode, "leftKnee", data.LeftKneeValue);
                    data.RightKneeValue = ReadFloat(boneNode, "rightKnee", data.RightKneeValue);
#if FEATURE_DAN_CORRECTION
                    data.DanScaleValue = ReadFloat(boneNode, "danScale", data.DanScaleValue);
                    data.DanLengthValue = ReadFloat(boneNode, "danLength", data.DanLengthValue);
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

                // 각 character 노드 아래 config 노드로 JointCorrectionSliderData 값을 저장한다.
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

                    WriteValueNode(writer, "leftArm", data.LeftArmUpperValue);
                    WriteValueNode(writer, "rightArm", data.RightArmUpperValue);

                    WriteValueNode(writer, "leftArmLower", data.LeftArmLowerValue);
                    WriteValueNode(writer, "rightArmLower", data.RightArmLowerValue);

                    WriteValueNode(writer, "leftElbow", data.LeftElbowValue);
                    WriteValueNode(writer, "rightElbow", data.RightElbowValue);

                    WriteValueNode(writer, "leftLeg", data.LeftLegValue);
                    WriteValueNode(writer, "rightLeg", data.RightLegValue);
                    WriteValueNode(writer, "leftKnee", data.LeftKneeValue);
                    WriteValueNode(writer, "rightKnee", data.RightKneeValue);
                    WriteScriptInfoBaseNode(writer, data);
#if FEATURE_DAN_CORRECTION
                    WriteValueNode(writer, "danScale", data.DanScaleValue);
                    WriteValueNode(writer, "danLength", data.DanLengthValue);
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

        // 유틸: 문자열을 float 값으로 파싱한다.
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

        private void WriteValueNode(XmlTextWriter writer, string nodeName, float value)
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

        private void EnsureScriptInfoBase(OCIChar ociChar, JointCorrectionSliderData data)
        {
            if (ociChar == null || data == null)
                return;

            if (data.ScriptInfoBaseInitialized)
                return;

            foreach (int categoryId in CorrectionCategoryIds)
            {
                ScriptMinMax? baseInfo = GetScriptInfo(ociChar, categoryId);
                if (baseInfo.HasValue)
                    data.ScriptInfoBaseByCategory[categoryId] = baseInfo.Value;
            }

            data.ScriptInfoBaseInitialized = true;
        }

        // base 정보 기준에서 correct 값 변경 (slider value is delta)
        private void SetScriptInfo(OCIChar ociChar, JointCorrectionSliderData data, int categoryId, float value)
        {
            // UnityEngine.Debug.Log($">> SetScriptInfo {categoryId}, {value}");

            foreach (Expression.ScriptInfo scriptInfo in ociChar.charInfo.expression.info)
            {   
                if (scriptInfo.categoryNo == categoryId) {
                    if (!data.ScriptInfoBaseByCategory.TryGetValue(categoryId, out ScriptMinMax baseInfo))
                        continue;

                    scriptInfo.enable = true;
#if FEATURE_DEBUG
                    scriptInfo.correct.useRX = RXConfig.Value;
                    scriptInfo.correct.useRY = RYConfig.Value;
                    scriptInfo.correct.useRZ = RZConfig.Value;
#endif
                    if(scriptInfo.correct.useRX)
                    {
                        if (value == 0)
                        {
                            scriptInfo.correct.valRXMin = baseInfo.RXMin;
                            scriptInfo.correct.valRXMax = baseInfo.RXMax;
                        } else {
                            scriptInfo.correct.valRXMin = value;
                            scriptInfo.correct.valRXMax = value;                                                                
                        }
                    }

                    if(scriptInfo.correct.useRY)
                    {
                        if (value == 0)
                        {
                            scriptInfo.correct.valRYMin = baseInfo.RYMin;
                            scriptInfo.correct.valRYMax = baseInfo.RYMax;
                        } else
                        {
                            scriptInfo.correct.valRYMin = value;
                            scriptInfo.correct.valRYMax = value;                                  
                        }  
                    }

                    if (scriptInfo.correct.useRZ)
                    {
                        if (value == 0)
                        {
                            scriptInfo.correct.valRZMin = baseInfo.RZMin;
                            scriptInfo.correct.valRZMax = baseInfo.RZMax;    
                            
                        } else
                        {
                            scriptInfo.correct.valRZMin = value;
                            scriptInfo.correct.valRZMax = value;    
                            
                        }
                    }
                }
            } 
        }

        // category 별 base correct 정보 제공
        private ScriptMinMax?  GetScriptInfo(OCIChar ociChar, int categoryId)
        {
            foreach (Expression.ScriptInfo scriptInfo in ociChar.charInfo.expression.info)
            {   
                if (scriptInfo.categoryNo == categoryId) {
                    return new ScriptMinMax
                    {
                        RXMin = scriptInfo.correct.valRXMin,
                        RXMax = scriptInfo.correct.valRXMax,
                        RYMin = scriptInfo.correct.valRYMin,
                        RYMax = scriptInfo.correct.valRYMax,
                        RZMin = scriptInfo.correct.valRZMin,
                        RZMax = scriptInfo.correct.valRZMax
                    };
                }
            } 

            return null;
        }        

        protected override void Update()
        {
            if (_loaded == false)
                return;

            JointCorrectionSliderData data = GetCurrentData();
            OCIChar currentOCIChar = GetCurrentOCI();

            if (data != null)
            {
                EnsureScriptInfoBase(currentOCIChar, data);

                if (data.LeftArmUpperValue != data._prevLeftArmUp)
                {
                    data._prevLeftArmUp = data.LeftArmUpperValue;
                    SetScriptInfo(currentOCIChar, data, 0, data.LeftArmUpperValue);
                }
                if (data.RightArmUpperValue != data._prevRightArmUp)
                {
                    data._prevRightArmUp = data.RightArmUpperValue;
                    SetScriptInfo(currentOCIChar, data, 1, data.RightArmUpperValue);
                }
                if (data.LeftArmLowerValue != data._prevLeftArmDn)
                {
                    data._prevLeftArmDn = data.LeftArmLowerValue;
                    SetScriptInfo(currentOCIChar, data, 4, data.LeftArmLowerValue);
                }
                if (data.RightArmLowerValue != data._prevRightArmDn)
                {
                    data._prevRightArmDn = data.RightArmLowerValue;
                    SetScriptInfo(currentOCIChar, data, 5, data.RightArmLowerValue);
                }
                // leg            
                if (data.LeftLegValue != data._prevLeftLeg)
                {
                    data._prevLeftLeg = data.LeftLegValue;
                    SetScriptInfo(currentOCIChar, data, 6, data.LeftLegValue);
                }
                if (data.RightLegValue != data._prevRightLeg)
                {
                    data._prevRightLeg = data.RightLegValue;
                    SetScriptInfo(currentOCIChar, data, 7, data.RightLegValue);
                }

                if (data.LeftKneeValue != data._prevLeftKnee)
                {
                    data._prevLeftKnee = data.LeftKneeValue;
                    SetScriptInfo(currentOCIChar, data, 2, data.LeftKneeValue);
                }

                if (data.RightKneeValue != data._prevRightKnee)
                {
                    data._prevRightKnee = data.RightKneeValue;
                    SetScriptInfo(currentOCIChar, data, 3, data.RightKneeValue);
                }

                if (data.LeftShoulderValue != data._prevLeftShoulder)
                {
                    data._prevLeftShoulder = data.LeftShoulderValue;
                    SetScriptInfo(currentOCIChar, data, 8, data.LeftShoulderValue);
                }
                if (data.RightShoulderValue != data._prevRightShoulder)
                {
                    data._prevRightShoulder = data.RightShoulderValue;
                    SetScriptInfo(currentOCIChar, data, 9, data.RightShoulderValue);
                }

                if (data.LeftElbowValue != data._prevLeftElbow)
                {
                    data._prevLeftElbow = data.LeftElbowValue;
                    SetScriptInfo(currentOCIChar, data, 10, data.LeftElbowValue);
                }
                if (data.RightElbowValue != data._prevRightElbow)
                {
                    data._prevRightElbow = data.RightElbowValue;
                    SetScriptInfo(currentOCIChar, data, 11, data.RightElbowValue);
                }
            }
        }

        protected override void LateUpdate()
        {
            JointCorrectionSliderData data = GetCurrentData();

            if (data != null)
            {
                if (data._shoulder02_s_L != null)
                    ApplyBoneTransform(data._shoulder02_s_L, data.LeftShoulderValue, ref data._shoulder02BaseSetL, ref data._shoulder02BasePosL, ref data._shoulder02BaseScaleL, TargetDirection.X_POS);

                if (data._shoulder02_s_R != null)
                    ApplyBoneTransform(data._shoulder02_s_R, data.RightShoulderValue, ref data._shoulder02BaseSetR, ref data._shoulder02BasePosR, ref data._shoulder02BaseScaleR, TargetDirection.X_POS);

#if FEATURE_DAN_CORRECTION
                if (data._dan_root != null)
                    ApplyDanTransform(data._dan_root, data.DanLengthValue, data.DanScaleValue, ref data._danRootPosBaseSet, ref data._danRootPosBasePos, ref data._danRootScaleBaseSet, ref data._danRootScaleBasePos, TargetDirection.Z_POS);

                if (data._dan_tip1 != null)
                    ApplyDanTransform(data._dan_tip1, data.DanLengthValue, data.DanScaleValue, ref data._danTip1PosBaseSet, ref data._danTip1PosBasePos, ref data._danTip1ScaleBaseSet, ref data._danTip1ScaleBasePos, TargetDirection.Z_POS);

                if (data._dan_tip2 != null)
                    ApplyDanTransform(data._dan_tip2, data.DanLengthValue, data.DanScaleValue, ref data._danTip2PosBaseSet, ref data._danTip2PosBasePos, ref data._danTip2ScaleBaseSet, ref data._danTip2ScaleBasePos, TargetDirection.Z_POS);

                if (data._dan_tip3 != null)
                    ApplyDanTransform(data._dan_tip3, data.DanLengthValue, data.DanScaleValue, ref data._danTip3PosBaseSet, ref data._danTip3PosBasePos, ref data._danTip3ScaleBaseSet, ref data._danTip3ScaleBasePos, TargetDirection.Z_POS);
#endif
            }
        }

        private OCIChar GetCurrentOCI()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null)
                return null;

            TreeNodeObject node  = Studio.Studio.Instance.treeNodeCtrl.selectNodes
                .LastOrDefault();

            return  node != null ? Studio.Studio.GetCtrlInfo(node) as OCIChar : null;
        }

        internal JointCorrectionSliderController GetCurrentControl()
        {
            OCIChar ociChar = GetCurrentOCI();
            if (ociChar != null)
            {
                return ociChar.GetChaControl().GetComponent<JointCorrectionSliderController>();
            }
            return null;
        }

        private JointCorrectionSliderData GetCurrentData()
        {
            OCIChar ociChar = GetCurrentOCI();
            if (ociChar != null && ociChar.GetChaControl() != null) {
                var controller = ociChar.GetChaControl().GetComponent<JointCorrectionSliderController>();
                if (controller == null)
                    return null;

                JointCorrectionSliderData data = controller.GetData();                
                return data;
            } 

            return null;
        }

        private JointCorrectionSliderData GetDataAndCreate(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            var controller = ociChar.GetChaControl().GetComponent<JointCorrectionSliderController>();
            if (controller == null)
                return null;

            JointCorrectionSliderData data = controller.GetData() ?? controller.CreateData(ociChar);
            EnsureScriptInfoBase(ociChar, data);
            return data;
        }

        private void InitConfig()
        {
            var controller = GetCurrentControl();
            if (controller)
            {
                controller.ResetJointCorrectionSliderData();
            }
        }

        protected override void OnGUI()
        {
            if (_loaded == false)
                return;
            
            if (StudioAPI.InsideStudio)
            {
                if (_ShowUI == false)             
                    return;

                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "JointCorrection " + Version);
            }
        }
        private void WindowFunc(int id)
        {
            var studio = Studio.Studio.Instance;

            bool guiUsingMouse = GUIUtility.hotControl != 0;
            bool mouseInWindow = _windowRect.Contains(Event.current.mousePosition);

            if (guiUsingMouse || mouseInWindow)
            {
                studio.cameraCtrl.noCtrlCondition = () => true;
            }
            else
            {
                studio.cameraCtrl.noCtrlCondition = null;
            }

            // ================= UI =================

            JointCorrectionSliderData data = GetCurrentData();

            if (data == null)
            {
                data = GetDataAndCreate(GetCurrentOCI());
            }

            if (data != null)
            {
                DrawStepSelector(ref _correctionStepIndex, "Step");
                float correctionStep = SliderStepOptions[_correctionStepIndex];

                // UnityEngine.Debug.Log($">> data.LeftShoulderValue  {data.LeftShoulderValue}");
                GUILayout.Label("<color=orange>Shoulder</color>", RichLabel);
                data.LeftShoulderValue = DrawCorrectionRow("Shdr(L)", "Left", data.LeftShoulderValue, -1.0f, 1.0f, IsModifiedValue(data.LeftShoulderValue), correctionStep);
                data.RightShoulderValue = DrawCorrectionRow("Shdr(R)", "Right", data.RightShoulderValue, -1.0f, 1.0f, IsModifiedValue(data.RightShoulderValue), correctionStep);

                // Top
                GUILayout.Label("<color=orange>Arm_Up</color>", RichLabel);
                data.LeftArmUpperValue = DrawCorrectionRow("ArmUp(L)", "Left", data.LeftArmUpperValue, -1.0f, 1.0f, IsModifiedValue(data.LeftArmUpperValue), correctionStep);
                data.RightArmUpperValue = DrawCorrectionRow("ArmUp(R)", "Right", data.RightArmUpperValue, -1.0f, 1.0f, IsModifiedValue(data.RightArmUpperValue), correctionStep);

                GUILayout.Label("<color=orange>Arm_Dn</color>", RichLabel);
                data.LeftArmLowerValue = DrawCorrectionRow("ArmDn(L)", "Left", data.LeftArmLowerValue, -1.0f, 1.0f, IsModifiedValue(data.LeftArmLowerValue), correctionStep);
                data.RightArmLowerValue = DrawCorrectionRow("ArmDn(R)", "Right", data.RightArmLowerValue, -1.0f, 1.0f, IsModifiedValue(data.RightArmLowerValue), correctionStep);

                GUILayout.Label("<color=orange>Elbow</color>", RichLabel);
                data.LeftElbowValue = DrawCorrectionRow("Elbow(L)", "Left", data.LeftElbowValue, -1.0f, 1.0f, IsModifiedValue(data.LeftElbowValue), correctionStep);
                data.RightElbowValue = DrawCorrectionRow("Elbow(R)", "Right", data.RightElbowValue, -1.0f, 1.0f, IsModifiedValue(data.RightElbowValue), correctionStep);

                // Bottom
                GUILayout.Label("<color=orange>Thigh</color>", RichLabel);            
                data.LeftLegValue = DrawCorrectionRow("Thigh(L)", "Left", data.LeftLegValue, -1.0f, 1.0f, IsModifiedValue(data.LeftLegValue), correctionStep);
                data.RightLegValue = DrawCorrectionRow("Thigh(R)", "Right", data.RightLegValue, -1.0f, 1.0f, IsModifiedValue(data.RightLegValue), correctionStep);

                GUILayout.Label("<color=orange>Knee</color>", RichLabel);            
                data.LeftKneeValue = DrawCorrectionRow("Knee(L)", "Back", data.LeftKneeValue, -1.0f, 1.0f, IsModifiedValue(data.LeftKneeValue), correctionStep);
                data.RightKneeValue = DrawCorrectionRow("Knee(R)", "Back", data.RightKneeValue, -1.0f, 1.0f, IsModifiedValue(data.RightKneeValue), correctionStep);
                
#if FEATURE_DAN_CORRECTION
                GUILayout.Label("<color=orange>Dan</color>", RichLabel);   
                data.DanScaleValue = DrawCorrectionRow("Dan", "Scale", data.DanScaleValue, -1.0f, 1.0f, IsModifiedValue(data.DanScaleValue), correctionStep);
                data.DanLengthValue = DrawCorrectionRow("Dan", "Length", data.DanLengthValue, -1.0f, 1.0f, IsModifiedValue(data.DanLengthValue), correctionStep);
#endif
                if (GUILayout.Button("Default"))
                    InitConfig();
//
            }   
            else
            {
                GUILayout.Label("<color=white>Nothing to select</color>", RichLabel);
            }         

            if (GUILayout.Button("Close")) {
                Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
                _ShowUI = false;
            }

            // 툴팁 표시
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 150, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }

        private bool IsModifiedValue(float value)
        {
            return Mathf.Abs(value) > ValueCompareEpsilon;
        }

        private float DrawCorrectionRow(string label, string tooltip, float value, float min, float max, bool isModified, float step)
        {
            GUILayout.BeginHorizontal();
            Color prevContentColor = GUI.contentColor;
            GUI.contentColor = isModified ? ModifiedEntryColor : UnmodifiedEntryColor;
            GUILayout.Label(new GUIContent(label, tooltip), GUILayout.Width(60));
            GUI.contentColor = prevContentColor;
            if (GUILayout.Button("-", GUILayout.Width(22)))
                value -= step;
            value = GUILayout.HorizontalSlider(value, min, max);
            if (GUILayout.Button("+", GUILayout.Width(22)))
                value += step;
            value = Quantize(value, step, min, max);
            GUILayout.Label(FormatByStep(value, step), GUILayout.Width(50));
            if (GUILayout.Button("Reset", GUILayout.Width(52)))
                value = 0.0f;
            GUILayout.EndHorizontal();
            return value;
        }

        private void DrawStepSelector(ref int stepIndex, string label)
        {
            if (stepIndex < 0 || stepIndex >= SliderStepOptions.Length)
                stepIndex = 0;

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            for (int i = 0; i < SliderStepOptions.Length; i++)
            {
                string stepLabel = SliderStepOptions[i].ToString("0.###", CultureInfo.InvariantCulture);
                if (GUILayout.Toggle(stepIndex == i, stepLabel, GUI.skin.button, GUILayout.Width(44)))
                    stepIndex = i;
            }
            GUILayout.EndHorizontal();
        }

        private float Quantize(float value, float step, float min, float max)
        {
            if (step <= 0f)
                return Mathf.Clamp(value, min, max);

            float quantized = Mathf.Round(value / step) * step;
            return Mathf.Clamp(quantized, min, max);
        }

        private string FormatByStep(float value, float step)
        {
            int decimals = 2;
            if (step > 0f)
                decimals = Mathf.Clamp(Mathf.CeilToInt(-Mathf.Log10(step)), 0, 4);

            string fmt = decimals == 0 ? "0" : ("0." + new string('0', decimals));
            return value.ToString(fmt, CultureInfo.InvariantCulture);
        }
        #endregion

        #region Private Methods
        private void Init()
        {
            _loaded = true;
        }

        private void SceneInit()
        {
            Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
			_ShowUI = false;     
        }

        private const float Shoulder02PosXRange = 0.8f;
        private const float Shoulder02ScaleMin = 0.5f;
        private const float Shoulder02ScaleMax = 1.5f;
#if FEATURE_DAN_CORRECTION
        private const float DanLengthPosRange = 0.5f;
        private const float DanScaleMin = 0.5f;
        private const float DanScaleMax = 1.5f;
#endif

        private void ApplyBoneTransform(
            Transform tr,
            float value,
            ref bool baseSet,
            ref Vector3 basePos,
            ref Vector3 baseScale,
            params TargetDirection[] directions)
        {
            // 1) 최초 1회 기준값을 캐싱한다.
            if (!baseSet)
            {
                basePos = tr.localPosition;
                baseScale = tr.localScale;
                baseSet = true;
            }

            // 2) 입력값을 -1~1 범위로 제한한다.
            value = Mathf.Clamp(value, -1f, 1f);

            // 3) 위치 오프셋을 계산한다.
            float posOffset = value * Shoulder02PosXRange;

            Vector3 newPos = basePos;

            // directions가 비어 있지 않을 때만 축별 위치를 적용한다.
            if (directions != null)
            {
                for (int i = 0; i < directions.Length; i++)
                {
                    switch (directions[i])
                    {
                        case TargetDirection.X_POS:
                            newPos.x += posOffset;
                            break;

                        case TargetDirection.Y_POS:
                            newPos.y += posOffset;
                            break;

                        case TargetDirection.Z_POS:
                            newPos.z += posOffset;
                            break;
                    }
                }
            }

            // 4) 스케일 보정 계수를 계산한다.
            float scaleFactor = (value >= 0f)
                ? Mathf.Lerp(1f, Shoulder02ScaleMax, value)
                : Mathf.Lerp(1f, Shoulder02ScaleMin, -value);

            // 5) 계산된 위치/스케일을 적용한다.
            tr.localPosition = newPos;
            tr.localScale = baseScale * scaleFactor;
        }

#if FEATURE_DAN_CORRECTION
        private void ApplyDanTransform(
            Transform tr,
            float lengthValue,
            float scaleValue,
            ref bool posBaseSet,
            ref Vector3 posBase,
            ref bool scaleBaseSet,
            ref Vector3 scaleBase,
            params TargetDirection[] directions)
        {
            if (!posBaseSet)
            {
                posBase = tr.localPosition;
                posBaseSet = true;
            }

            if (!scaleBaseSet)
            {
                scaleBase = tr.localScale;
                scaleBaseSet = true;
            }

            lengthValue = Mathf.Clamp(lengthValue, -1f, 1f);
            scaleValue = Mathf.Clamp(scaleValue, -1f, 1f);

            float posOffset = lengthValue * DanLengthPosRange;
            Vector3 newPos = posBase;

            if (directions != null)
            {
                for (int i = 0; i < directions.Length; i++)
                {
                    switch (directions[i])
                    {
                        case TargetDirection.X_POS:
                            newPos.x += posOffset;
                            break;
                        case TargetDirection.Y_POS:
                            newPos.y += posOffset;
                            break;
                        case TargetDirection.Z_POS:
                            newPos.z += posOffset;
                            break;
                    }
                }
            }

            float scaleFactor = (scaleValue >= 0f)
                ? Mathf.Lerp(1f, DanScaleMax, scaleValue)
                : Mathf.Lerp(1f, DanScaleMin, -scaleValue);

            tr.localPosition = newPos;
            tr.localScale = scaleBase * scaleFactor;
        }
#endif
        #endregion
        
        #region Patches
        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.SceneInit();
                return true;
            }
        }
        
       [HarmonyPatch(typeof(ChaControl), "InitializeExpression", typeof(int), typeof(bool))]
        private static class ChaControl_InitializeExpression_Patches
        {
            private static bool Prefix(ChaControl __instance, int sex, bool _enable, ref bool __result)
            {
                // UnityEngine.Debug.Log(Environment.StackTrace);

                _self._creating_char_sex = sex;
                string text = "list/expression.unity3d";
                string text2 = (sex == 0) ? "cm_expression" : "cf_expression";
                if (!global::AssetBundleCheck.IsFile(text, text2))
                {
                    __result = false;
                }
                __instance.expression = __instance.objRoot.AddComponent<Expression>();
                __instance.expression.LoadSetting(text, text2);
                int[] array = new int[]
                {
                    0,
                    0,
                    4,
                    0,
                    0,
                    0,
                    0,
                    1,
                    1,
                    5,
                    1,
                    1,
                    1,
                    1,
                    6,
                    6,
                    6,
                    2,
                    2,
                    6,
                    7,
                    7,
                    7,
                    3,
                    3,
                    7,
                    8,
                    9,
                    10,
                    11,
                };
                for (int i = 0; i < __instance.expression.info.Length; i++)
                {
                    __instance.expression.info[i].categoryNo = array[i];
                }
                for (int i = __instance.expression.info.Length - 4 ; i < __instance.expression.info.Length; i++)
                {
                    __instance.expression.info[i].enable = false;
                }

                __instance.expression.SetCharaTransform(__instance.objRoot.transform);
                __instance.expression.Initialize();
                __instance.expression.enable = _enable;
                __result = true;

                return false;
            }
        }

        [HarmonyPatch(typeof(CharaUtils.Expression), nameof(Expression.LoadSettingSub), typeof(List<string>))]
        internal static class Expression_LoadSettingSub_Patches
        {
            private static bool Prefix(Expression __instance, List<string> slist, ref bool __result)
            {
                // UnityEngine.Debug.Log(Environment.StackTrace);                
                string prefix = _self._creating_char_sex == 0 ? "cm_" : "cf_";        
                var rawList = new List<string>
                {
                "30\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0\t0",
                "0\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmUp01_dam_L\tcf_J_ArmUp00_L\tEuler\tYZX\t0.5\t○\t-0.66\t-0.66\t×\t0\t0\t×\t0\t0",
                "0\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmUp02_dam_L\tcf_J_ArmUp00_L\tEuler\tYZX\t0.5\t○\t-0.33\t-0.33\t×\t0\t0\t×\t0\t0",
                "0\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmLow02_dam_L\tcf_J_Hand_L\tEuler\tZYX\t0\t○\t0.5\t0.5\t×\t0\t0\t×\t0\t0",
                "0\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmElbo_dam_01_L\tcf_J_ArmLow01_L\tEuler\tXZY\t0\t×\t0\t0\t○\t0.6\t0.6\t×\t0\t0",
                "0\t○\tcf_J_ArmElboura_dam_L\tcf_J_ArmLow02_dam_L\tRevX\tcf_J_ArmElbo_dam_01_L\tY\tY\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "0\t○\tcf_J_Hand_Wrist_dam_L\tcf_J_ArmLow02_dam_L\tX\tcf_J_Hand_L\tZ\tZ\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "0\t○\tcf_J_Hand_dam_L\tcf_J_Hand_Middle01_L\tRevX\tcf_J_Hand_Wrist_dam_L\tY\tY\tZ\tZYX\t-180\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "1\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmUp01_dam_R\tcf_J_ArmUp00_R\tEuler\tYZX\t0.5\t○\t-0.66\t-0.66\t×\t0\t0\t×\t0\t0",
                "1\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmUp02_dam_R\tcf_J_ArmUp00_R\tEuler\tYZX\t0.5\t○\t-0.33\t-0.33\t×\t0\t0\t×\t0\t0",
                "1\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmLow02_dam_R\tcf_J_Hand_R\tEuler\tZYX\t0\t○\t0.5\t0.5\t×\t0\t0\t×\t0\t0",
                "1\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmElbo_dam_01_R\tcf_J_ArmLow01_R\tEuler\tXZY\t0\t×\t0\t0\t○\t0.6\t0.6\t×\t0\t0",
                "1\t○\tcf_J_ArmElboura_dam_R\tcf_J_ArmLow02_dam_R\tX\tcf_J_ArmElbo_dam_01_R\tZ\tZ\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "1\t○\tcf_J_Hand_Wrist_dam_R\tcf_J_ArmLow02_dam_R\tRevX\tcf_J_Hand_R\tZ\tZ\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "1\t○\tcf_J_Hand_dam_R\tcf_J_Hand_Middle01_R\tX\tcf_J_Hand_Wrist_dam_R\tZ\tZ\tZ\tZYX\t0\t180\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "2\t○\tcf_J_LegUpDam_L\tcf_J_LegLow01_L\tRevY\tcf_J_Kosi02\tX\tX\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "2\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegUp01_L\tcf_J_LegUp00_L\tEuler\tYZX\t0\t×\t0\t0\t○\t-0.85\t-0.85\t×\t0\t0",
                "2\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegUp02_L\tcf_J_LegUp00_L\tEuler\tYZX\t0\t×\t0\t0\t○\t-0.5\t-0.5\t×\t0\t0",
                "2\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegKnee_dam_L\tcf_J_LegLow01_L\tEuler\tZYX\t0\t○\t0.5\t0.5\t×\t0\t0\t×\t0\t0",
                "2\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegKnee_back_L\tcf_J_LegLow01_L\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "2\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_SiriDam_L\tcf_J_LegUp00_L\tEuler\tYZX\t0\t○\t0.5\t0.5\t○\t0.2\t0.2\t○\t0.25\t0.25",
                "3\t○\tcf_J_LegUpDam_R\tcf_J_LegLow01_R\tRevY\tcf_J_Kosi02\tX\tX\tNone\tZYX\t0\t0\t×\t0\t0\tEuler\tYXZ\t0\t×\t0\t0\t×\t0\t0\t×\t0\t0",
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegUp01_R\tcf_J_LegUp00_R\tEuler\tYZX\t0\t×\t0\t0\t○\t-0.85\t-0.85\t×\t0\t0",
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegUp02_R\tcf_J_LegUp00_R\tEuler\tYZX\t0\t×\t0\t0\t○\t-0.5\t-0.5\t×\t0\t0",
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegKnee_dam_R\tcf_J_LegLow01_R\tEuler\tZYX\t0\t○\t0.5\t0.5\t×\t0\t0\t×\t0\t0",
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_LegKnee_back_R\tcf_J_LegLow01_R\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "3\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_SiriDam_R\tcf_J_LegUp00_R\tEuler\tYZX\t0\t○\t0.5\t0.5\t○\t0.2\t0.2\t○\t0.25\t0.25",                        
                "4\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_Shoulder02_s_L\tcf_J_Shoulder_L\tEuler\tXZY\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "4\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_Shoulder02_s_R\tcf_J_Shoulder_R\tEuler\tXZY\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "4\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmElbo_dam_01_L\tcf_J_ArmLow01_L\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                "4\t×\t0\t0\tZ\t0\tY\tY\tNone\tYXZ\t0\t0\t○\tcf_J_ArmElbo_dam_01_R\tcf_J_ArmLow01_R\tEuler\tZYX\t0\t○\t1\t1\t○\t1\t1\t○\t1\t1",
                };

                     var _slist = rawList
                    .Select(line => line.Replace("cf_", prefix))
                    .ToList();

                if (_slist.Count == 0)
                {
                    __result = false;
                }
                string[] array = _slist[0].Split(new char[]
                {
                    '\t'
                });
                int num = int.Parse(array[0]);
                if (num > _slist.Count - 1)
                {
                    __result = false;
                }
                __instance.info = new Expression.ScriptInfo[num];
                for (int i = 0; i < num; i++)
                {
                    array = _slist[i + 1].Split(new char[]
                    {
                        '\t'
                    });
                    __instance.info[i] = new Expression.ScriptInfo();
                    __instance.info[i].index = i;
                    int num2 = 0;
                    __instance.info[i].categoryNo = int.Parse(array[num2++]);
                    __instance.info[i].enableLookAt = (array[num2++] == "○");
                    if (__instance.info[i].enableLookAt)
                    {
                        __instance.info[i].lookAt.lookAtName = array[num2++];
                        if ("0" == __instance.info[i].lookAt.lookAtName)
                        {
                            __instance.info[i].lookAt.lookAtName = "";
                        }
                        else
                        {
                            __instance.info[i].elementName = __instance.info[i].lookAt.lookAtName;
                        }
                        __instance.info[i].lookAt.targetName = array[num2++];
                        if ("0" == __instance.info[i].lookAt.targetName)
                        {
                            __instance.info[i].lookAt.targetName = "";
                        }
                        __instance.info[i].lookAt.targetAxisType = (Expression.LookAt.AxisType)Enum.Parse(typeof(Expression.LookAt.AxisType), array[num2++]);
                        __instance.info[i].lookAt.upAxisName = array[num2++];
                        if ("0" == __instance.info[i].lookAt.upAxisName)
                        {
                            __instance.info[i].lookAt.upAxisName = "";
                        }
                        __instance.info[i].lookAt.upAxisType = (Expression.LookAt.AxisType)Enum.Parse(typeof(Expression.LookAt.AxisType), array[num2++]);
                        __instance.info[i].lookAt.sourceAxisType = (Expression.LookAt.AxisType)Enum.Parse(typeof(Expression.LookAt.AxisType), array[num2++]);
                        __instance.info[i].lookAt.limitAxisType = (Expression.LookAt.AxisType)Enum.Parse(typeof(Expression.LookAt.AxisType), array[num2++]);
                        __instance.info[i].lookAt.rotOrder = (Expression.LookAt.RotationOrder)Enum.Parse(typeof(Expression.LookAt.RotationOrder), array[num2++]);
                        __instance.info[i].lookAt.limitMin = float.Parse(array[num2++]);
                        __instance.info[i].lookAt.limitMax = float.Parse(array[num2++]);
                    }
                    else
                    {
                        num2 += 10;
                    }
                    __instance.info[i].enableCorrect = (array[num2++] == "○");
                    if (__instance.info[i].enableCorrect)
                    {
                        __instance.info[i].correct.correctName = array[num2++];
                        if ("0" == __instance.info[i].correct.correctName)
                        {
                            __instance.info[i].correct.correctName = "";
                        }
                        else
                        {
                            __instance.info[i].elementName = __instance.info[i].correct.correctName;
                        }
                        __instance.info[i].correct.referenceName = array[num2++];
                        if ("0" == __instance.info[i].correct.referenceName)
                        {
                            __instance.info[i].correct.referenceName = "";
                        }
                        __instance.info[i].correct.calcType = (Expression.Correct.CalcType)Enum.Parse(typeof(Expression.Correct.CalcType), array[num2++]);
                        __instance.info[i].correct.rotOrder = (Expression.Correct.RotationOrder)Enum.Parse(typeof(Expression.Correct.RotationOrder), array[num2++]);
                        __instance.info[i].correct.charmRate = float.Parse(array[num2++]);
                        __instance.info[i].correct.useRX = (array[num2++] == "○");
                        __instance.info[i].correct.valRXMin = float.Parse(array[num2++]);
                        __instance.info[i].correct.valRXMax = float.Parse(array[num2++]);
                        __instance.info[i].correct.useRY = (array[num2++] == "○");
                        __instance.info[i].correct.valRYMin = float.Parse(array[num2++]);
                        __instance.info[i].correct.valRYMax = float.Parse(array[num2++]);
                        __instance.info[i].correct.useRZ = (array[num2++] == "○");
                        __instance.info[i].correct.valRZMin = float.Parse(array[num2++]);
                        __instance.info[i].correct.valRZMax = float.Parse(array[num2++]);
                    }
                }

                __result = true;

                return false;
            }
        }
        #endregion
    }

    enum TargetDirection
    {
        X_POS,
        Y_POS,
        Z_POS
    }

    struct ScriptMinMax
    {
        public float RXMin;
        public float RXMax;
        public float RYMin;
        public float RYMax;
        public float RZMin;
        public float RZMax;
    }    
#endregion
}
