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
    확인 사항
     - writer.WriteAttributeString("name", "" + ociChar.charInfo.name); 에서 name이 unique하지 못할거 같음.. unique 값 활용 필요 (collider 도 수정 피룡할 수 있음)    

    추가 개발
     - DAN bone 활용 지원
     - scene 저장
        > 먼저 속성이 확정되어야함

    활용 자료
     - charmRate 값 실시간 변경
     - orderRate 값 실시간 변경

        // category = 0  armup L
        // category = 1  armup R
        // category = 2  Knee L
        // category = 3  Knee R
        // category = 4  armLow L 
        // category = 5  armLow R 
        // category = 6  legup L, siri
        // category = 7  legup R, siri
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
        public const string Version = "0.9.0.2";
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

        private Rect _windowRect = new Rect(140, 10, 300, 10);
    
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

        private OCIChar _currentOCIChar = null;

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
// #if !FEATURE_PUBLIC
//             ExtendedSave.SceneBeingLoaded += this.OnSceneLoad;
//             ExtendedSave.SceneBeingImported += this.OnSceneImport;
//             ExtendedSave.SceneBeingSaved += this.OnSceneSave;
// #endif
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

//         private void OnSceneLoad(string path)
//         {
//             SceneInit();
//             PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
//             if (data == null)
//                 return;
//             XmlDocument doc = new XmlDocument();
//             doc.LoadXml((string)data.data["sceneInfo"]);
//             XmlNode node = doc.FirstChild;
//             if (node == null)
//                 return;
//             SceneLoad(path, node);
//         }

//         private void OnSceneImport(string path)
//         {
//             PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
//             if (data == null)
//                 return;
//             XmlDocument doc = new XmlDocument();
//             doc.LoadXml((string)data.data["sceneInfo"]);
//             XmlNode node = doc.FirstChild;
//             if (node == null)
//                 return;
//             SceneImport(path, node);
//         }

//         private void OnSceneSave(string path)
//         {
//             using (StringWriter stringWriter = new StringWriter())
//             using (XmlTextWriter xmlWriter = new XmlTextWriter(stringWriter))
//             {
//                 xmlWriter.WriteStartElement("root");
//                 SceneWrite(path, xmlWriter);
//                 xmlWriter.WriteEndElement();

//                 PluginData data = new PluginData();
//                 data.version = _saveVersion;
//                 data.data.Add("sceneInfo", stringWriter.ToString());

//                 ExtendedSave.SetSceneExtendedDataById(_extSaveKey, data);
//             }
//         }

//         private void SceneLoad(string path, XmlNode node)
//         {
//             if (node == null)
//                 return;
//             this.ExecuteDelayed2(() =>
//             {
//                 List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();

//                 List<OCIChar> ociChars = dic
//                     .Select(kv => kv.Value as OCIChar)   // ObjectCtrlInfo → OCIChar
//                     .Where(c => c != null)               // null 제거 (OCIChar가 아닌 경우 스킵)
//                     .ToList();

//                 SceneRead(node, ociChars);
//             }, 20);
//         }

//         private void SceneImport(string path, XmlNode node)
//         {
//             Dictionary<int, ObjectCtrlInfo> toIgnore = new Dictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
//             this.ExecuteDelayed2(() =>
//             {
//                 List<KeyValuePair<int, ObjectCtrlInfo>> dic = Studio.Studio.Instance.dicObjectCtrl
//                     .Where(e => toIgnore.ContainsKey(e.Key) == false)
//                     .OrderBy(e =>
//                     {
//                         int oldKey;
//                         return (SceneInfo_Import_Patches._newToOldKeys != null &&
//                                 SceneInfo_Import_Patches._newToOldKeys.TryGetValue(e.Key, out oldKey))
//                             ? oldKey
//                             : e.Key;
//                     })
//                     .ToList();

//                 List<OCIChar> ociChars = dic
//                     .Select(kv => kv.Value as OCIChar)   // ObjectCtrlInfo → OCIChar
//                     .Where(c => c != null)               // null 제거 (OCIChar가 아닌 경우 스킵)
//                     .ToList();

//                 SceneRead(node, ociChars);
//             }, 20);
//         }

//         private void SceneRead(XmlNode node, List<OCIChar> ociChars)
//         {            
//             // xml 조회는  character 하단에 config 하단에  각 bone 속성이 존재            
//             foreach (XmlNode charNode in node.SelectNodes("character"))
//             {
//                 string hash = charNode.Attributes["hash"]?.Value;
//                 if (string.IsNullOrEmpty(hash)) continue;
//                 if (!int.TryParse(hash, out int hashValue)) continue;

//                 // 이름으로 ociChar 찾기
//                 OCIChar ociChar = ociChars.FirstOrDefault(c => c.GetChaControl().GetHashCode() == hashValue);
//                 if (ociChar == null)
//                 {
//                     continue;
//                 }

//                 JointCorrectionSliderData data = GetData(ociChar);
//                 if (data == null)
//                     continue;

//                 // bone 노드 순회
//                 foreach (XmlNode boneNode in charNode.SelectNodes("config"))
//                 {
//                     string configName = boneNode.Attributes["name"]?.Value;
//                     if (string.IsNullOrEmpty(configName)) continue;
//                     if (!string.Equals(configName, "correction", StringComparison.OrdinalIgnoreCase))
//                         continue;
    
// #if FEATURE_SHOULDER_CORRECTION
//                     data.LeftShoulderValue = ReadFloat(boneNode, "leftShoulder", data.LeftShoulderValue);
//                     data.RightShoulderValue = ReadFloat(boneNode, "rightShoulder", data.RightShoulderValue);
// #endif
//                     data.LeftArmUpperValue = ReadFloat(boneNode, "leftArm", data.LeftArmUpperValue);
//                     data.RightArmUpperValue = ReadFloat(boneNode, "rightArm", data.RightArmUpperValue);
//                     data.LeftArmLowerValue = ReadFloat(boneNode, "leftArmLower", data.LeftArmLowerValue);
//                     data.RightArmLowerValue = ReadFloat(boneNode, "rightArmLower", data.RightArmLowerValue);
// #if FEATURE_ELBOW_CORRECTION
//                     data.LeftElbowValue = ReadFloat(boneNode, "leftElbow", data.LeftElbowValue);
//                     data.RightElbowValue = ReadFloat(boneNode, "rightElbow", data.RightElbowValue);
// #endif
//                     data.LeftLegValue = ReadFloat(boneNode, "leftLeg", data.LeftLegValue);
//                     data.RightLegValue = ReadFloat(boneNode, "rightLeg", data.RightLegValue);
//                     data.LeftKneeValue = ReadFloat(boneNode, "leftKnee", data.LeftKneeValue);
//                     data.RightKneeValue = ReadFloat(boneNode, "rightKnee", data.RightKneeValue);
// #if FEATURE_DAN_CORRECTION
//                     data.DanScaleValue = ReadFloat(boneNode, "danScale", data.DanScaleValue);
//                     data.DanLengthValue = ReadFloat(boneNode, "danLength", data.DanLengthValue);
// #endif        
//                 }
//             }
//         }

//         private void SceneWrite(string path, XmlTextWriter writer)
//         {
//             foreach (TreeNodeObject treeNode in Singleton<Studio.Studio>.Instance.treeNodeCtrl.m_TreeNodeObject)
//             {
//                 ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(treeNode);
//                 OCIChar ociChar = objectCtrlInfo as OCIChar;

//                 JointCorrectionSliderData data = GetData(ociChar);

//                 // n개의 character 에 대해 아래 JointCorrectionSliderData 값을 xml 로 저장
//                 // xml 저장은  character 하단에 config 하단에  각 bone 속성이 존재
//                 if (ociChar != null && data != null) {
//                     writer.WriteStartElement("character");
//                     writer.WriteAttributeString("hash", "" + ociChar.GetChaControl().GetHashCode());

//                     writer.WriteStartElement("config");
//                     writer.WriteAttributeString("name", "correction");
// #if FEATURE_SHOULDER_CORRECTION
//                     WriteValueNode(writer, "leftShoulder", data.LeftShoulderValue);

//                     WriteValueNode(writer, "rightShoulder", data.RightShoulderValue);
// #endif
//                     WriteValueNode(writer, "leftArm", data.LeftArmUpperValue);
//                     WriteValueNode(writer, "rightArm", data.RightArmUpperValue);

//                     WriteValueNode(writer, "leftArmLower", data.LeftArmLowerValue);
//                     WriteValueNode(writer, "rightArmLower", data.RightArmLowerValue);
// #if FEATURE_ELBOW_CORRECTION
//                     WriteValueNode(writer, "leftElbow", data.LeftElbowValue);
//                     WriteValueNode(writer, "rightElbow", data.RightElbowValue);
// #endif
//                     WriteValueNode(writer, "leftLeg", data.LeftLegValue);
//                     WriteValueNode(writer, "rightLeg", data.RightLegValue);
//                     WriteValueNode(writer, "leftKnee", data.LeftKneeValue);
//                     WriteValueNode(writer, "rightKnee", data.RightKneeValue);
// #if FEATURE_DAN_CORRECTION
//                     WriteValueNode(writer, "danScale", data.DanScaleValue);
//                     WriteValueNode(writer, "danLength", data.DanLengthValue);
// #endif
//                     writer.WriteEndElement(); // config
                
//                     writer.WriteEndElement(); // character
//                 }
//             }             
//         }

//         // 유틸: 안전한 float 파싱
//         private float ParseFloat(string value)
//         {
//             if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
//                 return result;
//             return 0f;
//         }        

//         private float ReadFloat(XmlNode parent, string nodeName, float fallback)
//         {
//             XmlNode valueNode = parent.SelectSingleNode(nodeName);
//             if (valueNode == null)
//                 return fallback;
//             return ParseFloat(valueNode.Attributes["value"]?.Value);
//         }

//         private void WriteValueNode(XmlTextWriter writer, string nodeName, float value)
//         {
//             writer.WriteStartElement(nodeName);
//             writer.WriteAttributeString("value", value.ToString(CultureInfo.InvariantCulture));
//             writer.WriteEndElement();
//         }

        private void SetScriptInfo(OCIChar ociChar, int categoryId, float value)
        {
            // UnityEngine.Debug.Log($">> SetScriptInfo {categoryId}, {value}");

            foreach (Expression.ScriptInfo scriptInfo in ociChar.charInfo.expression.info)
            {   
                if (scriptInfo.categoryNo == categoryId) {
                    scriptInfo.enable = true;
#if FEATURE_DEBUG
                    scriptInfo.correct.useRX = RXConfig.Value;
                    scriptInfo.correct.useRY = RYConfig.Value;
                    scriptInfo.correct.useRZ = RZConfig.Value;
                    // scriptInfo.correct.charmRate = CharmRateConfig.Value;
                    // scriptInfo.correct.rotOrder = (Correct.RotationOrder)RoTOrderConfig.Value;
#endif
                    if(scriptInfo.correct.useRX)
                    {
                        scriptInfo.correct.valRXMin = value;
                        scriptInfo.correct.valRXMax = value;        
                    }

                    if(scriptInfo.correct.useRY)
                    {
                        scriptInfo.correct.valRYMin = value;
                        scriptInfo.correct.valRYMax = value;        
                    }

                    if (scriptInfo.correct.useRY)
                    {
                        scriptInfo.correct.valRZMin = value;
                        scriptInfo.correct.valRZMax = value;    
                    }
                }
            } 
        }

        protected override void Update()
        {
            if (_loaded == false)
                return;

            JointCorrectionSliderData data = GetCurrentData();

            if (data != null)
            {
                if (data.LeftArmUpperValue != data._prevLeftArmUp)
                {
                    data._prevLeftArmUp = data.LeftArmUpperValue;
                    SetScriptInfo(_currentOCIChar, 0, data.LeftArmUpperValue);
                }
                if (data.RightArmUpperValue != data._prevRightArmUp)
                {
                    data._prevRightArmUp = data.RightArmUpperValue;
                    SetScriptInfo(_currentOCIChar, 1, data.RightArmUpperValue);
                }
                if (data.LeftArmLowerValue != data._prevLeftArmDn)
                {
                    data._prevLeftArmDn = data.LeftArmLowerValue;
                    SetScriptInfo(_currentOCIChar, 4, data.LeftArmLowerValue);
                }
                if (data.RightArmLowerValue != data._prevRightArmDn)
                {
                    data._prevRightArmDn = data.RightArmLowerValue;
                    SetScriptInfo(_currentOCIChar, 5, data.RightArmLowerValue);
                }
                // leg            
                if (data.LeftLegValue != data._prevLeftLeg)
                {
                    data._prevLeftLeg = data.LeftLegValue;
                    SetScriptInfo(_currentOCIChar, 6, data.LeftLegValue);
                }
                if (data.RightLegValue != data._prevRightLeg)
                {
                    data._prevRightLeg = data.RightLegValue;
                    SetScriptInfo(_currentOCIChar, 7, data.RightLegValue);
                }

                if (data.LeftKneeValue != data._prevLeftKnee)
                {
                    data._prevLeftKnee = data.LeftKneeValue;
                    SetScriptInfo(_currentOCIChar, 2, data.LeftKneeValue);
                }

                if (data.RightKneeValue != data._prevRightKnee)
                {
                    data._prevRightKnee = data.RightKneeValue;
                    SetScriptInfo(_currentOCIChar, 3, data.RightKneeValue);
                }
#if FEATURE_SHOULDER_CORRECTION
                if (data.LeftShoulderValue != data._prevLeftShoulder)
                {
                    data._prevLeftShoulder = data.LeftShoulderValue;
                    SetScriptInfo(_currentOCIChar, 8, data.LeftShoulderValue);
                }
                if (data.RightShoulderValue != data._prevRightShoulder)
                {
                    data._prevRightShoulder = data.RightShoulderValue;
                    SetScriptInfo(_currentOCIChar, 9, data.RightShoulderValue);
                }
#endif
#if FEATURE_ELBOW_CORRECTION
                if (data.LeftElbowValue != data._prevLeftElbow)
                {
                    data._prevLeftElbow = data.LeftElbowValue;
                    SetScriptInfo(_currentOCIChar, 10, data.LeftElbowValue);
                }
                if (data.RightElbowValue != data._prevRightElbow)
                {
                    data._prevRightElbow = data.RightElbowValue;
                    SetScriptInfo(_currentOCIChar, 11, data.RightElbowValue);
                }
#endif
            }
        }

        protected override void LateUpdate()
        {
            JointCorrectionSliderData data = GetCurrentData();

            if (data != null)
            {
#if FEATURE_SHOULDER_CORRECTION
                if (data._shoulder02_s_L != null)
                    ApplyBoneTransform(data._shoulder02_s_L, data.LeftShoulderValue, ref data._shoulder02BaseSetL, ref data._shoulder02BasePosL, ref data._shoulder02BaseScaleL, TargetDirection.X_POS);

                if (data._shoulder02_s_R != null)
                    ApplyBoneTransform(data._shoulder02_s_R, data.RightShoulderValue, ref data._shoulder02BaseSetR, ref data._shoulder02BasePosR, ref data._shoulder02BaseScaleR, TargetDirection.X_POS);
#endif                
            }
        }

        private JointCorrectionSliderData GetCurrentData()
        {
            if (_currentOCIChar != null && _currentOCIChar.GetChaControl() != null) {
                var controller = _currentOCIChar.GetChaControl().GetComponent<JointCorrectionSliderController>();
                if (controller == null)
                    return null;

                JointCorrectionSliderData data = controller.GetData();                
                return data;
            } 

            return null;
        }

        private JointCorrectionSliderData GetData(OCIChar ociChar)
        {
            if (ociChar != null && ociChar.GetChaControl() != null) {
                var controller = ociChar.GetChaControl().GetComponent<JointCorrectionSliderController>();
                if (controller == null)
                    return null;

                JointCorrectionSliderData data = controller.GetData();                
                return data;
            } 

            return null;
        }


        private JointCorrectionSliderController GetCurrentControl()
        {
            if (_currentOCIChar != null && _currentOCIChar.GetChaControl() != null) {
                return _currentOCIChar.GetChaControl().GetComponent<JointCorrectionSliderController>();         
            }

            return null;   
        }   

        private void InitConfig()
        {
            var controller = _currentOCIChar.GetChaControl().GetComponent<JointCorrectionSliderController>();
            if (controller)
            {
                controller.ResetJointCorrectionSliderData();
            }
        }

        protected override void OnGUI()
        {
            if (_ShowUI == false)
                return;
#if FEATURE_PUBLIC
            if (StudioAPI.InsideStudio)
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "JointCorrection(Public) " + Version);
#else 
            if (StudioAPI.InsideStudio)
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "JointCorrection " + Version);
#endif

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
            if (data != null)
            {
#if FEATURE_SHOULDER_CORRECTION
                // UnityEngine.Debug.Log($">> data.LeftShoulderValue  {data.LeftShoulderValue}");
#if FEATURE_PUBLIC
                GUILayout.Label("<color=red>scene save not support in public ver</color>", RichLabel);
                GUILayout.Label("<color=grey>Shoulder</color>", RichLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Shdr(L)", "Left"), GUILayout.Width(60));
                data.LeftShoulderValue = GUILayout.HorizontalSlider(data.LeftShoulderValue, 0.0f, 0.0f);
                GUILayout.Label(data.LeftShoulderValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Shdr(R)", "Right"), GUILayout.Width(60));
                data.RightShoulderValue = GUILayout.HorizontalSlider(data.RightShoulderValue, 0.0f, 0.0f);
                GUILayout.Label(data.RightShoulderValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();
#else 
                GUILayout.Label("<color=orange>Shoulder</color>", RichLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Shdr(L)", "Left"), GUILayout.Width(60));
                data.LeftShoulderValue = GUILayout.HorizontalSlider(data.LeftShoulderValue, -1.0f, 1.0f);
                GUILayout.Label(data.LeftShoulderValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Shdr(R)", "Right"), GUILayout.Width(60));
                data.RightShoulderValue = GUILayout.HorizontalSlider(data.RightShoulderValue, -1.0f, 1.0f);
                GUILayout.Label(data.RightShoulderValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();
#endif
#endif            
                // Top
                GUILayout.Label("<color=orange>Arm_Up</color>", RichLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("ArmUp(L)", "Left"), GUILayout.Width(60));
                data.LeftArmUpperValue = GUILayout.HorizontalSlider(data.LeftArmUpperValue, -1.0f, 1.0f);
                GUILayout.Label(data.LeftArmUpperValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();
                
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("ArmUp(R)", "Right"),GUILayout.Width(60));
                data.RightArmUpperValue = GUILayout.HorizontalSlider(data.RightArmUpperValue, -1.0f, 1.0f);
                GUILayout.Label(data.RightArmUpperValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.Label("<color=orange>Arm_Dn</color>", RichLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("ArmDn(L)", "Left"), GUILayout.Width(60));
                data.LeftArmLowerValue = GUILayout.HorizontalSlider(data.LeftArmLowerValue, -1.0f, 1.0f);
                GUILayout.Label(data.LeftArmLowerValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();
                
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("ArmDn(R)", "Right"), GUILayout.Width(60));
                data.RightArmLowerValue = GUILayout.HorizontalSlider(data.RightArmLowerValue, -1.0f, 1.0f);
                GUILayout.Label(data.RightArmLowerValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();
#if FEATURE_ELBOW_CORRECTION
                GUILayout.Label("<color=orange>Elbow</color>", RichLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Elbow(L)", "Left"), GUILayout.Width(60));
                data.LeftElbowValue = GUILayout.HorizontalSlider(data.LeftElbowValue, -1.0f, 1.0f);
                GUILayout.Label(data.LeftElbowValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Elbow(R)", "Right"), GUILayout.Width(60));
                data.RightElbowValue = GUILayout.HorizontalSlider(data.RightElbowValue, -1.0f, 1.0f);
                GUILayout.Label(data.RightElbowValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();
#endif
                // Bottom
                GUILayout.Label("<color=orange>Thigh</color>", RichLabel);            
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Thigh(L)", "Left"), GUILayout.Width(60));
                data.LeftLegValue = GUILayout.HorizontalSlider(data.LeftLegValue, -1.0f, 1.0f);
                GUILayout.Label(data.LeftLegValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Thigh(R)", "Right"), GUILayout.Width(60));
                data.RightLegValue = GUILayout.HorizontalSlider(data.RightLegValue, -1.0f, 1.0f);
                GUILayout.Label(data.RightLegValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.Label("<color=orange>Knee</color>", RichLabel);            
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Knee(L)", "Back"), GUILayout.Width(60));
                data.LeftKneeValue = GUILayout.HorizontalSlider(data.LeftKneeValue, -1.0f, 1.0f);
                GUILayout.Label(data.LeftKneeValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Knee(R)", "Back"), GUILayout.Width(60));
                data.RightKneeValue = GUILayout.HorizontalSlider(data.RightKneeValue, -1.0f, 1.0f);
                GUILayout.Label(data.RightKneeValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();
#if FEATURE_DAN_CORRECTION
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Dan", "Scale"), GUILayout.Width(60));
                data.DanScaleValue = GUILayout.HorizontalSlider(data.DanScaleValue, -1.0f, 1.0f);
                GUILayout.Label(data.DanScaleValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Dan", "Length"), GUILayout.Width(60));
                data.DanLengthValue = GUILayout.HorizontalSlider(data.DanLengthValue, -1.0f, 1.0f);
                GUILayout.Label(data.DanLengthValue.ToString("0.00"), GUILayout.Width(30));
                GUILayout.EndHorizontal();
#endif

                draw_seperate();  
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

            // ⭐ Tooltip
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
#endregion

#if FEATURE_SHOULDER_CORRECTION
        private const float Shoulder02PosXRange = 0.8f;
        private const float Shoulder02ScaleMin = 0.5f;
        private const float Shoulder02ScaleMax = 1.5f;

        private void ApplyBoneTransform(
            Transform tr,
            float value,
            ref bool baseSet,
            ref Vector3 basePos,
            ref Vector3 baseScale,
            params TargetDirection[] directions)
        {
            // -------------------------
            // 1. Base 값 캐싱 (최초 1회)
            // -------------------------
            if (!baseSet)
            {
                basePos = tr.localPosition;
                baseScale = tr.localScale;
                baseSet = true;
            }

            // -------------------------
            // 2. 입력 안정화
            // -------------------------
            value = Mathf.Clamp(value, -1f, 1f);

            // -------------------------
            // 3. Position 계산 (대칭 선형)
            // -------------------------
            float posOffset = value * Shoulder02PosXRange;

            Vector3 newPos = basePos;

            // directions가 null 또는 비어있으면 아무것도 안함
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

            // -------------------------
            // 4. Scale 계산 (0 기준 대칭)
            // -------------------------
            float scaleFactor = (value >= 0f)
                ? Mathf.Lerp(1f, Shoulder02ScaleMax, value)
                : Mathf.Lerp(1f, Shoulder02ScaleMin, -value);

            // -------------------------
            // 5. 적용
            // -------------------------
            tr.localPosition = newPos;
            tr.localScale = baseScale * scaleFactor;
        }

#endif

        #region Public Methods
        #endregion

        #region Patches
        
        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);   
                if (objectCtrlInfo == null)
                    return true;
                    
                OCIChar ociChar = objectCtrlInfo as OCIChar;

                if (ociChar != null)
                {
                    _self._currentOCIChar = ociChar;
                    ChaControl chaControl = ociChar.GetChaControl();

                    if (chaControl != null)
                    {
                        var controller = chaControl.GetComponent<JointCorrectionSliderController>();
                        if (controller)
                        {
                            if (controller.GetData() == null)
                                controller.InitJointCorrectionSliderData(chaControl);
                        }
                      }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.SceneInit();
                return true;
            }
        }

        [HarmonyPatch(typeof(SceneInfo), "Import", new[] { typeof(BinaryReader), typeof(Version) })]
        private static class SceneInfo_Import_Patches //This is here because I fucked up the save format making it impossible to import scenes correctly
        {
            internal static readonly Dictionary<int, int> _newToOldKeys = new Dictionary<int, int>();

            private static void Prefix()
            {
                _newToOldKeys.Clear();
            }
        }

#if FEATURE_JOINT_CORRECTION
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
#endif
        #endregion
    }

    enum TargetDirection
    {
        X_POS,
        Y_POS,
        Z_POS
    }
#endregion
}
