// Comment normalized to English.
using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.Threading.Tasks;
using KK_PregnancyPlus;

#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using AIChara;
using System.Security.Cryptography;
using ADV.Commands.Camera;
using ADV.Commands.Object;
using IllusionUtility.GetUtility;
using KKAPI.Studio;
using KKAPI.Maker;
using KKAPI;
using KKAPI.Chara;
#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace RealHumanSupport
{
    public partial class RealHumanSupportController
    {
        internal void PlayElasticShot(string oneShotType = "leg", float duration = 0.45f, float strength = 1.0f, int cycleCount = 1)
        {
            if (realHumanData == null)
                return;

            if (_elasticShotCoroutine != null)
                return;

            _elasticShotCoroutine = StartCoroutine(PlayElasticShotCoroutine(oneShotType, duration, strength, cycleCount));
        }

        private IEnumerator PlayElasticShotCoroutine(string oneShotType, float duration, float strength, int cycleCount)
        {
            if (realHumanData == null)
                yield break;

            duration = Mathf.Max(0.01f, duration);
            strength = Mathf.Clamp01(strength);
            cycleCount = Mathf.Clamp(cycleCount, 1, 8);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float wave = Mathf.Sin(t * Mathf.PI * cycleCount);
                float slider = Mathf.Clamp(wave * 0.35f * strength, -1f, 1f);

                switch (oneShotType)
                {
                    case "head":
                        if (realHumanData.jointCorrectionSliderController != null)
                        {
                            realHumanData.jointCorrectionSliderController.SetHeadShrink(slider);
                        }
                        break;
                    case "leg":
                        // Example: one-cycle elastic deformation that returns to base.
                        if (realHumanData.jointCorrectionSliderController != null)
                        {
                            realHumanData.jointCorrectionSliderController.SetLegShrink(slider);
                        }
                        break;
                    case "crotch":                                            
                        if (realHumanData.jointCorrectionSliderController != null)
                        {
                            realHumanData.jointCorrectionSliderController.SetCrotchShrink(slider);
                        }
                        break;
                    case "foot":
                        if (realHumanData.jointCorrectionSliderController != null)
                        {
                            realHumanData.jointCorrectionSliderController.SetFootShrink(slider);
                            realHumanData.jointCorrectionSliderController.SetToeShrink(slider);
                        }
                        break;
                    case "toe":
                        if (realHumanData.jointCorrectionSliderController != null)
                        {
                            realHumanData.jointCorrectionSliderController.SetFootShrink(slider);
                            realHumanData.jointCorrectionSliderController.SetToeShrink(slider);
                        }
                        break;
                    default:
                        break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Always restore base state when one-shot ends.
            if (realHumanData.jointCorrectionSliderController != null)
            {
                realHumanData.jointCorrectionSliderController.SetHeadShrink(0f);
                realHumanData.jointCorrectionSliderController.SetLegShrink(0f);
                realHumanData.jointCorrectionSliderController.SetCrotchShrink(0f);
                realHumanData.jointCorrectionSliderController.SetFootShrink(-1f);
                realHumanData.jointCorrectionSliderController.SetToeShrink(-1f);
            }

            _elasticShotCoroutine = null;
        }
    }
}
