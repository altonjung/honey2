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
using KKAPI;


namespace JointCorrectionSlider
{
    public class JointCorrectionSliderController : CharaCustomFunctionController
    {
        internal JointCorrectionSliderData correctionData;
        protected override void OnCardBeingSaved(GameMode currentGameMode) { }

        internal JointCorrectionSliderData CreateData(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            ChaControl charControl = ociChar.GetChaControl();
            correctionData = new JointCorrectionSliderData();
            correctionData.charControl = charControl;

            string bone_prefix_str = "cf_";
            if (charControl.sex == 0)
                bone_prefix_str = "cm_";

            correctionData._shoulder02_s_L = charControl.objAnim.transform.FindLoop(bone_prefix_str + "J_Shoulder02_s_L");
            correctionData._shoulder02_s_R = charControl.objAnim.transform.FindLoop(bone_prefix_str + "J_Shoulder02_s_R");

            correctionData._shoulder02BaseSetL = false;
            correctionData._shoulder02BaseSetR = false;

#if FEATURE_DAN_CORRECTION
            correctionData._dan_root = charControl.objAnim.transform.FindLoop("cm_J_dan_s");
            correctionData._dan_top1 = charControl.objAnim.transform.FindLoop("cm_J_dan119_00");
            correctionData._dan_top2 = charControl.objAnim.transform.FindLoop("cm_J_dan108_00");
            correctionData._dan_top3 = charControl.objAnim.transform.FindLoop("cm_J_dan107_00");
            correctionData._dan_top4 = charControl.objAnim.transform.FindLoop("cm_J_dan100_00");

            correctionData._danRootPosBaseSet = false;
            correctionData._danRootScaleBaseSet = false;
            correctionData._danRootRotBaseSet = false;

            correctionData._danTop1PosBaseSet = false;
            correctionData._danTop1ScaleBaseSet = false;
            correctionData._danTop1RotBaseSet = false;

            correctionData._danTop2PosBaseSet = false;
            correctionData._danTop2ScaleBaseSet = false;
            correctionData._danTop2RotBaseSet = false;

            correctionData._danTop3PosBaseSet = false;
            correctionData._danTop3ScaleBaseSet = false;
            correctionData._danTop3RotBaseSet = false;
#endif

            correctionData.ScriptInfoBaseByCategory.Clear();
            if (ociChar.charInfo != null && ociChar.charInfo.expression != null && ociChar.charInfo.expression.info != null)
            {
                foreach (Expression.ScriptInfo scriptInfo in ociChar.charInfo.expression.info)
                {
                    if (!IsManagedCategory(scriptInfo.categoryNo))
                        continue;

                    correctionData.ScriptInfoBaseByCategory[scriptInfo.categoryNo] = new ScriptMinMax
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

            correctionData.ScriptInfoBaseInitialized = correctionData.ScriptInfoBaseByCategory.Count > 0;
            return correctionData;
        }

        private bool IsManagedCategory(int categoryId)
        {
            switch (categoryId)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                case 11:
                    return true;
                default:
                    return false;
            }
        }

        internal void ResetJointCorrectionSliderData()
        {
            if (correctionData != null)
            {
                correctionData.Reset();
            }
        }

        internal JointCorrectionSliderData GetData()
        {
            return correctionData;
        }
    }

    class JointCorrectionSliderData
    {
        public ChaControl charControl;

        public Dictionary<int, ScriptMinMax> ScriptInfoBaseByCategory = new Dictionary<int, ScriptMinMax>();
        public bool ScriptInfoBaseInitialized = false;

        public float LeftShoulderValue = 0.0f;
        public float RightShoulderValue = 0.0f;
        public float LeftArmUpperValue = 0.0f;
        public float RightArmUpperValue = 0.0f;
        public float LeftArmLowerValue = 0.0f;
        public float RightArmLowerValue = 0.0f;
        public float LeftElbowValue = 0.0f;
        public float RightElbowValue = 0.0f;
        public float LeftKneeValue = 0.0f;
        public float RightKneeValue = 0.0f;
        public float LeftLegValue = 0.0f;
        public float RightLegValue = 0.0f;

#if FEATURE_DAN_CORRECTION
        public float DanRootScaleValue = 0.0f;
        public float DanRootPosValue = 0.0f;
        public float DanRootRotateValue = 0.0f;

        public float DanTop1ScaleValue = 0.0f;
        public float DanTop1PosValue = 0.0f;
        public float DanTop1RotateValue = 0.0f;

        public float DanTop2ScaleValue = 0.0f;
        public float DanTop2PosValue = 0.0f;
        public float DanTop2RotateValue = 0.0f;

        public float DanTop3ScaleValue = 0.0f;
        public float DanTop3PosValue = 0.0f;
        public float DanTop3RotateValue = 0.0f;

        public float DanTop4ScaleValue = 0.0f;
        public float DanTop4PosValue = 0.0f;
        public float DanTop4RotateValue = 0.0f;
#endif

#if FEATURE_DEBUG
        public bool RXConfig  { get; private set; }
        public bool RYConfig  { get; private set; }
        public bool RZConfig  { get; private set; }
#endif

        public float _prevLeftShoulder = 0f;
        public float _prevRightShoulder = 0f;
        public float _prevLeftKnee = 0f;
        public float _prevRightKnee = 0f;
        public float _prevLeftKnee2 = 0f;
        public float _prevRightKnee2 = 0f;
        public float _prevLeftLeg = 0f;
        public float _prevRightLeg = 0f;
        public float _prevLeftArmUp = 0f;
        public float _prevRightArmUp = 0f;
        public float _prevLeftArmDn = 0f;
        public float _prevRightArmDn = 0f;
        public float _prevLeftElbow = 0f;
        public float _prevRightElbow = 0f;

        public Transform _shoulder02_s_L;
        public Transform _shoulder02_s_R;

        public UnityEngine.Vector3 _shoulder02BasePosL;
        public UnityEngine.Vector3 _shoulder02BasePosR;
        public UnityEngine.Vector3 _shoulder02BaseScaleL;
        public UnityEngine.Vector3 _shoulder02BaseScaleR;
        public bool _shoulder02BaseSetL;
        public bool _shoulder02BaseSetR;

#if FEATURE_DAN_CORRECTION
        public Transform _dan_root;
        public Transform _dan_top1;
        public Transform _dan_top2;
        public Transform _dan_top3;
        public Transform _dan_top4;

        public UnityEngine.Vector3 _danRootPosBasePos;
        public UnityEngine.Vector3 _danRootScaleBasePos;
        public UnityEngine.Vector3 _danRootRotBaseEuler;

        public UnityEngine.Vector3 _danTop1PosBasePos;
        public UnityEngine.Vector3 _danTop1ScaleBasePos;
        public UnityEngine.Vector3 _danTop1RotBaseEuler;

        public UnityEngine.Vector3 _danTop2PosBasePos;
        public UnityEngine.Vector3 _danTop2ScaleBasePos;
        public UnityEngine.Vector3 _danTop2RotBaseEuler;

        public UnityEngine.Vector3 _danTop3PosBasePos;
        public UnityEngine.Vector3 _danTop3ScaleBasePos;
        public UnityEngine.Vector3 _danTop3RotBaseEuler;

        public UnityEngine.Vector3 _danTop4PosBasePos;
        public UnityEngine.Vector3 _danTop4ScaleBasePos;
        public UnityEngine.Vector3 _danTop4RotBaseEuler;


        public bool _danRootPosBaseSet;
        public bool _danRootScaleBaseSet;
        public bool _danRootRotBaseSet;

        public bool _danTop1PosBaseSet;
        public bool _danTop1ScaleBaseSet;
        public bool _danTop1RotBaseSet;

        public bool _danTop2PosBaseSet;
        public bool _danTop2ScaleBaseSet;
        public bool _danTop2RotBaseSet;

        public bool _danTop3PosBaseSet;
        public bool _danTop3ScaleBaseSet;
        public bool _danTop3RotBaseSet;

        public bool _danTop4PosBaseSet;
        public bool _danTop4ScaleBaseSet;
        public bool _danTop4RotBaseSet;
#endif

        internal void Reset()
        {
            LeftShoulderValue = 0.0f;
            RightShoulderValue = 0.0f;

            LeftElbowValue = 0.0f;
            RightElbowValue = 0.0f;
            LeftArmUpperValue = 0.0f;
            RightArmUpperValue = 0.0f;
            LeftArmLowerValue = 0.0f;
            RightArmLowerValue = 0.0f;
            LeftKneeValue = 0.0f;
            RightKneeValue = 0.0f;
            LeftLegValue = 0.0f;
            RightLegValue = 0.0f;

#if FEATURE_DAN_CORRECTION
            DanRootScaleValue = 0.0f;
            DanRootPosValue = 0.0f;
            DanRootRotateValue = 0.0f;

            DanTop1ScaleValue = 0.0f;
            DanTop1PosValue = 0.0f;
            DanTop1RotateValue = 0.0f;

            DanTop2ScaleValue = 0.0f;
            DanTop2PosValue = 0.0f;
            DanTop2RotateValue = 0.0f;

            DanTop3ScaleValue = 0.0f;
            DanTop3PosValue = 0.0f;
            DanTop3RotateValue = 0.0f;

            DanTop4ScaleValue = 0.0f;
            DanTop4PosValue = 0.0f;
            DanTop4RotateValue = 0.0f;
#endif
        }
    }
}

