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
            아래 5개 에 대해, 추가적으로 rotation속성이 필요해(기존에는 position, scale 속성만 존재함)
            correctionData._dan_root
            correctionData._dan_top1
            correctionData._dan_top2
            correctionData._dan_top3
            correctionData._dan_top4


    현 버전 문제점:
        N/A

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
        public const string Version = "0.9.2.0";
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

        private enum CorrectionFieldId
        {
            LeftShoulder,
            RightShoulder,
            LeftArmUpper,
            RightArmUpper,
            LeftArmLower,
            RightArmLower,
            LeftElbow,
            RightElbow,
            LeftLeg,
            RightLeg,
            LeftKnee,
            RightKnee,
#if FEATURE_DAN_CORRECTION
            DanTop1Scale,
            DanTop2Scale,
            DanTop3Scale,
            DanTop4Length,
            DanRootScale,
#endif
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
            BlendFullLeg,
            BlendButtCreek,
#endif
        }

        private struct CorrectionFieldUi
        {
            public readonly CorrectionFieldId Id;
            public readonly string Label;
            public readonly string Tooltip;
            public readonly float Min;
            public readonly float Max;

            public CorrectionFieldUi(CorrectionFieldId id, string label, string tooltip, float min, float max)
            {
                Id = id;
                Label = label;
                Tooltip = tooltip;
                Min = min;
                Max = max;
            }
        }

        private class CorrectionCategoryUi
        {
            public readonly string Name;
            public readonly CorrectionFieldUi[] Fields;

            public CorrectionCategoryUi(string name, CorrectionFieldUi[] fields)
            {
                Name = name;
                Fields = fields;
            }
        }
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
        private Vector2 _categoryScroll;
        private Vector2 _targetScroll;
        private int _selectedCategoryIndex;
        private int _selectedTargetIndex;

        private static readonly int[] CorrectionCategoryIds = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        private static readonly CorrectionCategoryUi[] CorrectionUiCategories = new CorrectionCategoryUi[]
        {
            new CorrectionCategoryUi("Shoulder", new[]
            {
                new CorrectionFieldUi(CorrectionFieldId.LeftShoulder, "Sholdr(L)", "Left", -1.0f, 1.0f),
                new CorrectionFieldUi(CorrectionFieldId.RightShoulder, "Sholdr(R)", "Right", -1.0f, 1.0f)
            }),
            new CorrectionCategoryUi("Arm_Up", new[]
            {
                new CorrectionFieldUi(CorrectionFieldId.LeftArmUpper, "ArmUp(L)", "Left", -1.0f, 1.0f),
                new CorrectionFieldUi(CorrectionFieldId.RightArmUpper, "ArmUp(R)", "Right", -1.0f, 1.0f)
            }),
            new CorrectionCategoryUi("Arm_Dn", new[]
            {
                new CorrectionFieldUi(CorrectionFieldId.LeftArmLower, "ArmDn(L)", "Left", -1.0f, 1.0f),
                new CorrectionFieldUi(CorrectionFieldId.RightArmLower, "ArmDn(R)", "Right", -1.0f, 1.0f)
            }),
            new CorrectionCategoryUi("Elbow", new[]
            {
                new CorrectionFieldUi(CorrectionFieldId.LeftElbow, "Elbow(L)", "Left", -1.0f, 1.0f),
                new CorrectionFieldUi(CorrectionFieldId.RightElbow, "Elbow(R)", "Right", -1.0f, 1.0f)
            }),
            new CorrectionCategoryUi("Thigh", new[]
            {
                new CorrectionFieldUi(CorrectionFieldId.LeftLeg, "Thigh(L)", "Left", -1.0f, 1.0f),
                new CorrectionFieldUi(CorrectionFieldId.RightLeg, "Thigh(R)", "Right", -1.0f, 1.0f)
            }),
            new CorrectionCategoryUi("Knee", new[]
            {
                new CorrectionFieldUi(CorrectionFieldId.LeftKnee, "Knee(L)", "Back", -1.0f, 1.0f),
                new CorrectionFieldUi(CorrectionFieldId.RightKnee, "Knee(R)", "Back", -1.0f, 1.0f)
            }),
#if FEATURE_DAN_CORRECTION
            new CorrectionCategoryUi("Dan Top", new[]
            {
                new CorrectionFieldUi(CorrectionFieldId.DanTop1Scale, "Glans1(S)", "Glans1 Scale", -1.0f, 1.0f),
                new CorrectionFieldUi(CorrectionFieldId.DanTop2Scale, "Glans2(S)", "Glans2 Scale", -1.0f, 1.0f),
                new CorrectionFieldUi(CorrectionFieldId.DanTop3Scale, "Glans3(S)", "Glans3 Scale", -1.0f, 1.0f)
            }),
            new CorrectionCategoryUi("Dan Length", new[]
            {
                new CorrectionFieldUi(CorrectionFieldId.DanTop4Length, "Dan(L)", "Penis Length", -1.0f, 0.0f)
            }),
            new CorrectionCategoryUi("Dan Scale", new[]
            {
                new CorrectionFieldUi(CorrectionFieldId.DanRootScale, "Dan(S)", "Penis Scale", -0.5f, 1.0f)
            }),
#endif
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
            new CorrectionCategoryUi("Body BlendShape", new[]
            {
                new CorrectionFieldUi(CorrectionFieldId.BlendFullLeg, "Thigh", "Creek", 0f, 100f),
                new CorrectionFieldUi(CorrectionFieldId.BlendButtCreek, "Butt", "Creek", 0f, 100f),
            }),
#endif
        };
    
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
                    data.DanTop4PosValue = ReadFloat(boneNode, "danTop4Length", data.DanTop4PosValue);
                    data.DanTop4RotateValue = ReadFloat(boneNode, "danTop4Rotate", data.DanTop4RotateValue);
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
                    WriteValueNode(writer, "danTop4Length", data.DanTop4PosValue);
                    WriteValueNode(writer, "danTop4Rotate", data.DanTop4RotateValue);
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

#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                JointCorrectionSliderController controller = currentOCIChar != null && currentOCIChar.GetChaControl() != null
                    ? currentOCIChar.GetChaControl().GetComponent<JointCorrectionSliderController>()
                    : null;
                if (controller != null)
                {
                    EnsureBlendShapeIndices(data, controller);

                    if (data.fullLegValue != data._prevFullLegValue)
                    {
                        if (data.fulleg_idx_in_body >= 0)
                        {
                            data._prevFullLegValue = data.fullLegValue;
                            controller.SetBlendShape(data.fullLegValue, data.fulleg_idx_in_body);
                        }
                    }
                    if (data.buttchecks1Value != data._prevButtchecks1Value)
                    {
                        bool applied = false;
                        if (data.buttchecks1_idx_in_body >= 0)
                        {
                            controller.SetBlendShape(data.buttchecks1Value, data.buttchecks1_idx_in_body);
                            applied = true;
                        }
                        if (applied)
                            data._prevButtchecks1Value = data.buttchecks1Value;
                    }
                    // if (data.vaginOpenValue != data._prevVaginOpenValue)
                    // {
                    //     if (data.vagina_open_front_idx_in_body >= 0)
                    //     {
                    //         data._prevVaginOpenValue = data.vaginOpenValue;
                    //         controller.SetBlendShape(data.vaginOpenValue, data.vagina_open_front_idx_in_body);
                    //     }
                    // }
                }
#endif
            }
        }

        protected override void LateUpdate()
        {
            JointCorrectionSliderData data = GetCurrentData();

            if (data != null)
            {                
                ApplyBoneTransform(data._shoulder02_s_L, data.LeftShoulderValue, ref data._shoulder02BaseSetL, ref data._shoulder02BasePosL, ref data._shoulder02BaseScaleL, TargetDirection.X_POS);
                ApplyBoneTransform(data._shoulder02_s_R, data.RightShoulderValue, ref data._shoulder02BaseSetR, ref data._shoulder02BasePosR, ref data._shoulder02BaseScaleR, TargetDirection.X_POS);
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                ApplyFullLegPosition(data._legup01_L, data.fullLegValue, ref data._legup01BaseSetL, ref data._legup01BasePosL, ref data._legup01BaseScaleL, 1f);
                ApplyFullLegPosition(data._legup01_R, data.fullLegValue, ref data._legup01BaseSetR, ref data._legup01BasePosR, ref data._legup01BaseScaleR, -1f);
#endif
#if FEATURE_DAN_CORRECTION
                ApplyDanTransform(data._dan_top1, data.DanTop1PosValue, data.DanTop1ScaleValue, data.DanTop1RotateValue, ref data._danTop1PosBaseSet, ref data._danTop1PosBasePos, ref data._danTop1ScaleBaseSet, ref data._danTop1ScaleBasePos, ref data._danTop1RotBaseSet, ref data._danTop1RotBaseEuler, TargetDirection.Z_POS);
                ApplyDanTransform(data._dan_top2, data.DanTop2PosValue, data.DanTop2ScaleValue, data.DanTop2RotateValue, ref data._danTop2PosBaseSet, ref data._danTop2PosBasePos, ref data._danTop2ScaleBaseSet, ref data._danTop2ScaleBasePos, ref data._danTop2RotBaseSet, ref data._danTop2RotBaseEuler, TargetDirection.Z_POS);
                ApplyDanTransform(data._dan_top3, data.DanTop3PosValue, data.DanTop3ScaleValue, data.DanTop3RotateValue, ref data._danTop3PosBaseSet, ref data._danTop3PosBasePos, ref data._danTop3ScaleBaseSet, ref data._danTop3ScaleBasePos, ref data._danTop3RotBaseSet, ref data._danTop3RotBaseEuler, TargetDirection.Z_POS);
                ApplyDanTransform(data._dan_top4, data.DanTop4PosValue, data.DanTop4ScaleValue, data.DanTop4RotateValue, ref data._danTop4PosBaseSet, ref data._danTop4PosBasePos, ref data._danTop4ScaleBaseSet, ref data._danTop4ScaleBasePos, ref data._danTop4RotBaseSet, ref data._danTop4RotBaseEuler, TargetDirection.Z_POS);                
                ApplyDanTransform(data._dan_root, data.DanRootPosValue, data.DanRootScaleValue, data.DanRootRotateValue, ref data._danRootPosBaseSet, ref data._danRootPosBasePos, ref data._danRootScaleBaseSet, ref data._danRootScaleBasePos, ref data._danRootRotBaseSet, ref data._danRootRotBaseEuler, TargetDirection.Z_POS);                
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
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                EnsureBlendShapeIndices(data);
#endif
                DrawStepSelector(ref _correctionStepIndex, "Step");
                float correctionStep = SliderStepOptions[_correctionStepIndex];

                var visibleCategories = new List<CorrectionCategoryUi>();
                for (int i = 0; i < CorrectionUiCategories.Length; i++)
                {
                    CorrectionCategoryUi category = CorrectionUiCategories[i];
                    if (HasAvailableField(data, category))
                        visibleCategories.Add(category);
                }

                if (visibleCategories.Count > 0)
                {
                    _selectedCategoryIndex = Mathf.Clamp(_selectedCategoryIndex, 0, visibleCategories.Count - 1);
                    CorrectionCategoryUi selectedCategory = visibleCategories[_selectedCategoryIndex];
                    List<CorrectionFieldUi> availableFields = GetAvailableFields(data, selectedCategory);

                    if (availableFields.Count > 0)
                        _selectedTargetIndex = Mathf.Clamp(_selectedTargetIndex, 0, availableFields.Count - 1);
                    else
                        _selectedTargetIndex = 0;

                    GUILayout.BeginHorizontal();

                    GUILayout.BeginVertical(GUILayout.Width(160));
                    GUILayout.Label("<color=orange>Category</color>", RichLabel);
                    _categoryScroll = GUILayout.BeginScrollView(_categoryScroll, GUI.skin.box, GUILayout.Height(118));
                    for (int i = 0; i < visibleCategories.Count; i++)
                    {
                        CorrectionCategoryUi category = visibleCategories[i];
                        bool isSelected = i == _selectedCategoryIndex;
                        bool isModified = IsCategoryModified(data, category);
                        Color prevColor = GUI.contentColor;
                        GUI.contentColor = isModified ? ModifiedEntryColor : UnmodifiedEntryColor;
                        if (GUILayout.Toggle(isSelected, category.Name, GUI.skin.button))
                        {
                            if (!isSelected)
                            {
                                _selectedCategoryIndex = i;
                                _selectedTargetIndex = 0;
                            }
                        }
                        GUI.contentColor = prevColor;
                    }
                    GUILayout.EndScrollView();
                    GUILayout.EndVertical();

                    selectedCategory = visibleCategories[_selectedCategoryIndex];
                    availableFields = GetAvailableFields(data, selectedCategory);

                    GUILayout.BeginVertical(GUILayout.Width(160));
                    GUILayout.Label("<color=orange>Target</color>", RichLabel);
                    _targetScroll = GUILayout.BeginScrollView(_targetScroll, GUI.skin.box, GUILayout.Height(118));
                    for (int i = 0; i < availableFields.Count; i++)
                    {
                        CorrectionFieldUi field = availableFields[i];
                        bool isSelected = i == _selectedTargetIndex;
                        bool isModified = IsFieldModified(data, field.Id);
                        Color prevColor = GUI.contentColor;
                        GUI.contentColor = isModified ? ModifiedEntryColor : UnmodifiedEntryColor;
                        if (GUILayout.Toggle(isSelected, field.Label, GUI.skin.button))
                            _selectedTargetIndex = i;
                        GUI.contentColor = prevColor;
                    }
                    GUILayout.EndScrollView();
                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();

                    if (availableFields.Count > 0)
                    {
                        CorrectionFieldUi selectedField = availableFields[_selectedTargetIndex];
                        float currentValue = GetFieldValue(data, selectedField.Id);
                        float fieldStep = IsBlendShapeField(selectedField.Id) ? 1f : correctionStep;
                        float updatedValue = DrawCorrectionRow(
                            selectedField.Label,
                            selectedField.Tooltip,
                            currentValue,
                            selectedField.Min,
                            selectedField.Max,
                            IsModifiedValue(currentValue),
                            fieldStep);
                        SetFieldValue(data, selectedField.Id, updatedValue);

#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                        if (IsBlendShapeField(selectedField.Id) && GetBlendShapeTargetIndex(data, selectedField.Id) < 0)
                            GUILayout.Label("<color=red>Select Golden Palace Body</color>", RichLabel);
#endif
                    }
                }

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

            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 150, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }

        private void draw_seperate()
        {
            GUILayout.Space(5);
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(0.3f));
            GUI.Box(rect, GUIContent.none);
            GUILayout.Space(10);
        }

        private bool HasAvailableField(JointCorrectionSliderData data, CorrectionCategoryUi category)
        {
            if (data == null || category == null || category.Fields == null)
                return false;

            for (int i = 0; i < category.Fields.Length; i++)
            {
                if (IsFieldAvailable(data, category.Fields[i].Id))
                    return true;
            }
            return false;
        }

        private List<CorrectionFieldUi> GetAvailableFields(JointCorrectionSliderData data, CorrectionCategoryUi category)
        {
            List<CorrectionFieldUi> fields = new List<CorrectionFieldUi>();
            if (data == null || category == null || category.Fields == null)
                return fields;

            for (int i = 0; i < category.Fields.Length; i++)
            {
                CorrectionFieldUi field = category.Fields[i];
                if (IsFieldAvailable(data, field.Id))
                    fields.Add(field);
            }

            return fields;
        }

        private bool IsCategoryModified(JointCorrectionSliderData data, CorrectionCategoryUi category)
        {
            if (data == null || category == null || category.Fields == null)
                return false;

            for (int i = 0; i < category.Fields.Length; i++)
            {
                CorrectionFieldUi field = category.Fields[i];
                if (IsFieldAvailable(data, field.Id) && IsFieldModified(data, field.Id))
                    return true;
            }
            return false;
        }

        private bool IsFieldAvailable(JointCorrectionSliderData data, CorrectionFieldId fieldId)
        {
            if (data == null)
                return false;

#if FEATURE_DAN_CORRECTION
            bool isMale = data.charControl != null && data.charControl.sex == 0;
            bool hasDanTop = isMale && data._dan_top1 != null && data._dan_top2 != null && data._dan_top3 != null && data._dan_top4 != null;
            bool hasDanRoot = isMale && data._dan_root != null;
#endif
            switch (fieldId)
            {
#if FEATURE_DAN_CORRECTION
                case CorrectionFieldId.DanTop1Scale:
                case CorrectionFieldId.DanTop2Scale:
                case CorrectionFieldId.DanTop3Scale:
                case CorrectionFieldId.DanTop4Length:
                    return hasDanTop;
                case CorrectionFieldId.DanRootScale:
                    return hasDanRoot;
#endif
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                case CorrectionFieldId.BlendFullLeg:
                    return data.charControl != null && data.charControl.sex == 1;
                case CorrectionFieldId.BlendButtCreek:
                    return data.charControl != null && data.charControl.sex == 1;
#endif
                default:
                    return true;
            }
        }

        private bool IsFieldModified(JointCorrectionSliderData data, CorrectionFieldId fieldId)
        {
            return IsModifiedValue(GetFieldValue(data, fieldId));
        }

#if FEATURE_BODY_BLENDSHAPE_SUPPORT
        private void EnsureBlendShapeIndices(JointCorrectionSliderData data, JointCorrectionSliderController controller = null)
        {
            if (data == null || data.charControl == null)
                return;

            // female only
            if (data.charControl.sex != 1)
                return;

            bool needScan = data.fulleg_idx_in_body < 0
                || (data.buttchecks1_idx_in_body < 0);
                // || data.vagina_open_front_idx_in_body < 0;

            if (!needScan)
                return;

            if (controller == null)
                controller = data.charControl.GetComponent<JointCorrectionSliderController>();

            if (controller != null)
                controller.SetBodyBlendShapes();
        }

        private int GetBlendShapeTargetIndex(JointCorrectionSliderData data, CorrectionFieldId fieldId)
        {
            if (data == null)
                return -1;

            switch (fieldId)
            {
                case CorrectionFieldId.BlendFullLeg:
                    return data.fulleg_idx_in_body;
                case CorrectionFieldId.BlendButtCreek:
                    return data.buttchecks1_idx_in_body;
                default:
                    return -1;
            }
        }
#endif

        private bool IsBlendShapeField(CorrectionFieldId fieldId)
        {
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
            switch (fieldId)
            {
                case CorrectionFieldId.BlendFullLeg:
                case CorrectionFieldId.BlendButtCreek:
                    return true;
            }
#endif
            return false;
        }

        private float GetFieldValue(JointCorrectionSliderData data, CorrectionFieldId fieldId)
        {
            switch (fieldId)
            {
                case CorrectionFieldId.LeftShoulder: return data.LeftShoulderValue;
                case CorrectionFieldId.RightShoulder: return data.RightShoulderValue;
                case CorrectionFieldId.LeftArmUpper: return data.LeftArmUpperValue;
                case CorrectionFieldId.RightArmUpper: return data.RightArmUpperValue;
                case CorrectionFieldId.LeftArmLower: return data.LeftArmLowerValue;
                case CorrectionFieldId.RightArmLower: return data.RightArmLowerValue;
                case CorrectionFieldId.LeftElbow: return data.LeftElbowValue;
                case CorrectionFieldId.RightElbow: return data.RightElbowValue;
                case CorrectionFieldId.LeftLeg: return data.LeftLegValue;
                case CorrectionFieldId.RightLeg: return data.RightLegValue;
                case CorrectionFieldId.LeftKnee: return data.LeftKneeValue;
                case CorrectionFieldId.RightKnee: return data.RightKneeValue;
#if FEATURE_DAN_CORRECTION
                case CorrectionFieldId.DanTop1Scale: return data.DanTop1ScaleValue;
                case CorrectionFieldId.DanTop2Scale: return data.DanTop2ScaleValue;
                case CorrectionFieldId.DanTop3Scale: return data.DanTop3ScaleValue;
                case CorrectionFieldId.DanTop4Length: return data.DanTop4PosValue;
                case CorrectionFieldId.DanRootScale: return data.DanRootScaleValue;
#endif
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                case CorrectionFieldId.BlendFullLeg: return data.fullLegValue;
                case CorrectionFieldId.BlendButtCreek: return data.buttchecks1Value;
#endif
                default:
                    return 0.0f;
            }
        }

        private void SetFieldValue(JointCorrectionSliderData data, CorrectionFieldId fieldId, float value)
        {
            switch (fieldId)
            {
                case CorrectionFieldId.LeftShoulder: data.LeftShoulderValue = value; break;
                case CorrectionFieldId.RightShoulder: data.RightShoulderValue = value; break;
                case CorrectionFieldId.LeftArmUpper: data.LeftArmUpperValue = value; break;
                case CorrectionFieldId.RightArmUpper: data.RightArmUpperValue = value; break;
                case CorrectionFieldId.LeftArmLower: data.LeftArmLowerValue = value; break;
                case CorrectionFieldId.RightArmLower: data.RightArmLowerValue = value; break;
                case CorrectionFieldId.LeftElbow: data.LeftElbowValue = value; break;
                case CorrectionFieldId.RightElbow: data.RightElbowValue = value; break;
                case CorrectionFieldId.LeftLeg: data.LeftLegValue = value; break;
                case CorrectionFieldId.RightLeg: data.RightLegValue = value; break;
                case CorrectionFieldId.LeftKnee: data.LeftKneeValue = value; break;
                case CorrectionFieldId.RightKnee: data.RightKneeValue = value; break;
#if FEATURE_DAN_CORRECTION
                case CorrectionFieldId.DanTop1Scale: data.DanTop1ScaleValue = value; break;
                case CorrectionFieldId.DanTop2Scale: data.DanTop2ScaleValue = value; break;
                case CorrectionFieldId.DanTop3Scale: data.DanTop3ScaleValue = value; break;
                case CorrectionFieldId.DanTop4Length: data.DanTop4PosValue = value; break;
                case CorrectionFieldId.DanRootScale: data.DanRootScaleValue = value; break;
#endif
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                case CorrectionFieldId.BlendFullLeg: data.fullLegValue = Mathf.Clamp(Mathf.RoundToInt(value), 0, 100); break;
                case CorrectionFieldId.BlendButtCreek: data.buttchecks1Value = Mathf.Clamp(Mathf.RoundToInt(value), 0, 100); break;
#endif
            }
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
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
        private const float FullLegPosXRange = 0.2f;
#endif
#if FEATURE_DAN_CORRECTION
        private const float DanScaleMin = 0.5f;
        private const float DanScaleMax = 1.5f;
        private const float DanRotateMaxDegrees = 45f;
#endif

        private void ApplyBoneTransform(
            Transform tr,
            float value,
            ref bool baseSet,
            ref Vector3 basePos,
            ref Vector3 baseScale,
            params TargetDirection[] directions)
        {

            if (tr == null)
                return;

             if (!baseSet)
            {
                basePos = tr.localPosition;
                baseScale = tr.localScale;
                baseSet = true;
            }

            value = Mathf.Clamp(value, -1f, 1f);

            float posOffset = value * Shoulder02PosXRange;

            Vector3 newPos = basePos;

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

            float scaleFactor = (value >= 0f)
                ? Mathf.Lerp(1f, Shoulder02ScaleMax, value)
                : Mathf.Lerp(1f, Shoulder02ScaleMin, -value);

            tr.localPosition = newPos;
            tr.localScale = baseScale * scaleFactor;
        }

#if FEATURE_BODY_BLENDSHAPE_SUPPORT
        private void ApplyFullLegPosition(
            Transform tr,
            int fullLegValue,
            ref bool baseSet,
            ref Vector3 basePos,
            ref Vector3 baseScale,
            float defaultXSign)
        {
            if (tr == null)
                return;

            if (!baseSet)
            {
                basePos = tr.localPosition;
                baseScale = tr.localScale;
                baseSet = true;
            }

            float normalized = Mathf.Clamp01(fullLegValue / 100f);
            float posOffset = normalized * FullLegPosXRange;

            float xSign = Mathf.Abs(basePos.x) > 1e-6f ? Mathf.Sign(basePos.x) : defaultXSign;
            Vector3 newPos = basePos;
            newPos.x -= posOffset * xSign;
            tr.localPosition = newPos;
        }
#endif

#if FEATURE_DAN_CORRECTION
        private void ApplyDanTransform(
            Transform tr,
            float posValue,
            float scaleValue,
            float rotateValue,
            ref bool posBaseSet,
            ref Vector3 posBase,
            ref bool scaleBaseSet,
            ref Vector3 scaleBase,
            ref bool rotBaseSet,
            ref Vector3 rotBaseEuler,
            params TargetDirection[] directions)
        {
            if (tr == null)
                return;

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

            if (!rotBaseSet)
            {
                rotBaseEuler = tr.localEulerAngles;
                rotBaseSet = true;
            }

            posValue = Mathf.Clamp(posValue, -1f, 1f);
            scaleValue = Mathf.Clamp(scaleValue, -1f, 1f);
            rotateValue = Mathf.Clamp(rotateValue, -1f, 1f);

            float posOffset = posValue;
            Vector3 newPos = posBase;

            bool usedManualDirection = false;
            if (directions != null && directions.Length > 0)
            {
                for (int i = 0; i < directions.Length; i++)
                {
                    usedManualDirection = true;
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

            if (!usedManualDirection)
            {
                float ax = Mathf.Abs(posBase.x);
                float ay = Mathf.Abs(posBase.y);
                float az = Mathf.Abs(posBase.z);

                if (ax >= ay && ax >= az)
                    newPos.x += posOffset * (Mathf.Abs(posBase.x) > 1e-6f ? Mathf.Sign(posBase.x) : 1f);
                else if (ay >= ax && ay >= az)
                    newPos.y += posOffset * (Mathf.Abs(posBase.y) > 1e-6f ? Mathf.Sign(posBase.y) : 1f);
                else
                    newPos.z += posOffset * (Mathf.Abs(posBase.z) > 1e-6f ? Mathf.Sign(posBase.z) : 1f);
            }

            float scaleFactor = (scaleValue >= 0f)
                ? Mathf.Lerp(1f, DanScaleMax, scaleValue)
                : Mathf.Lerp(1f, DanScaleMin, -scaleValue);

            float rotOffset = rotateValue * DanRotateMaxDegrees;
            Vector3 newEuler = rotBaseEuler;
            if (directions != null && directions.Length > 0)
            {
                for (int i = 0; i < directions.Length; i++)
                {
                    switch (directions[i])
                    {
                        case TargetDirection.X_POS:
                            newEuler.x += rotOffset;
                            break;
                        case TargetDirection.Y_POS:
                            newEuler.y += rotOffset;
                            break;
                        case TargetDirection.Z_POS:
                            newEuler.z += rotOffset;
                            break;
                    }
                }
            }
            else
            {
                newEuler.z += rotOffset;
            }

            tr.localPosition = newPos;
            tr.localScale = scaleBase * scaleFactor;
            tr.localRotation = Quaternion.Euler(newEuler);

            // UnityEngine.Debug.Log($">> ApplyDanTransform posValue: {posValue}, pos: {tr.localPosition}, rotate: {tr.localRotation} scale: {tr.localScale}");
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
