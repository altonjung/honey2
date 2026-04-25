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
    public partial class JointCorrectionSliderController
    {
    
        public bool SetCrotchShrink(float sliderValue)
        {
            if (correctionData == null)
                return false;

#if FEATURE_CROTCH_CORRECTION
            correctionData.KosiCorrectionValue = ClampSliderValue(sliderValue);
            return true;
#else
            return false;
#endif
        }

        public bool SetLegShrink(float sliderValue)
        {
            if (correctionData == null)
                return false;

            float clamped = ClampSliderValue(sliderValue);
            float offsetX = clamped * 180.0f; // slider [-1, 1] -> offset X [-180, 180]
            bool updated = false;

            if (correctionData._legup01_L != null)
            {
                if (!correctionData._legup01BaseSetL)
                {
                    correctionData._legup01BaseRotEulerL = correctionData._legup01_L.localEulerAngles;
                    correctionData._legup01BaseSetL = true;
                }

                UnityEngine.Vector3 euler = correctionData._legup01_L.localEulerAngles;
                float baseX = Mathf.DeltaAngle(0f, correctionData._legup01BaseRotEulerL.x);
                euler.x = Mathf.Repeat(baseX + offsetX + 360.0f, 360.0f);
                correctionData._legup01_L.localEulerAngles = euler;
                updated = true;
            }

            if (correctionData._legup01_R != null)
            {
                if (!correctionData._legup01BaseSetR)
                {
                    correctionData._legup01BaseRotEulerR = correctionData._legup01_R.localEulerAngles;
                    correctionData._legup01BaseSetR = true;
                }

                UnityEngine.Vector3 euler = correctionData._legup01_R.localEulerAngles;
                float baseX = Mathf.DeltaAngle(0f, correctionData._legup01BaseRotEulerR.x);
                euler.x = Mathf.Repeat(baseX + offsetX + 360.0f, 360.0f);
                correctionData._legup01_R.localEulerAngles = euler;
                updated = true;
            }

            return updated;
        }
        public bool SetFootShrink(float sliderValue)
        {
            if (correctionData == null)
                return false;

#if FEATURE_BODY_BLENDSHAPE_SUPPORT
            float clamped = ClampSliderValue(sliderValue);
            float blendshapeValue = (clamped + 1.0f) * 50.0f; // slider [-1, 1] -> blendshape [0, 100]

            bool updated = false;
            if (correctionData.footL_idx_in_body >= 0)
            {
                SetBlendShape(blendshapeValue, correctionData.footL_idx_in_body);
                updated = true;
            }

            if (correctionData.footR_idx_in_body >= 0)
            {
                SetBlendShape(blendshapeValue, correctionData.footR_idx_in_body);
                updated = true;
            }

            return updated;
#else
            return false;
#endif
        }
        public bool SetToeShrink(float sliderValue)
        {
            if (correctionData == null)
                return false;

#if FEATURE_BODY_BLENDSHAPE_SUPPORT
            float clamped = ClampSliderValue(sliderValue);
            float blendshapeValue = (clamped + 1.0f) * 50.0f; // slider [-1, 1] -> blendshape [0, 100]

            bool updated = false;
            if (correctionData.toeL_idx_in_body >= 0)
            {
                SetBlendShape(blendshapeValue, correctionData.toeL_idx_in_body);
                updated = true;
            }

            if (correctionData.toeR_idx_in_body >= 0)
            {
                SetBlendShape(blendshapeValue, correctionData.toeR_idx_in_body);
                updated = true;
            }

            return updated;
#else
            return false;
#endif
        }

        public bool SetHeadShrink(float sliderValue)
        {
            if (correctionData == null || correctionData._head_bone == null)
                return false;

            float clamped = ClampSliderValue(sliderValue);
            float offsetX = clamped * 180.0f; // slider [-1, 1] -> offset X [-180, 180]

            if (!correctionData._headBaseSet)
            {
                correctionData._headBaseRotEuler = correctionData._head_bone.localEulerAngles;
                correctionData._headBaseSet = true;
            }

            UnityEngine.Vector3 euler = correctionData._head_bone.localEulerAngles;
            float baseX = Mathf.DeltaAngle(0f, correctionData._headBaseRotEuler.x);
            euler.x = Mathf.Repeat(baseX + offsetX + 360.0f, 360.0f);
            correctionData._head_bone.localEulerAngles = euler;
            return true;
        }
    }
}

