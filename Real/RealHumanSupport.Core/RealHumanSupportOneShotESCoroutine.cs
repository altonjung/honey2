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
        // One-shot elastic shape: base -> peak -> base
        internal void PlayOneShotES(string oneShotType = "enter", float duration = 0.45f, float strength = 1.0f)
        {
            PlayOneShotES(oneShotType, duration, 0f, strength);
        }

        // One-shot elastic shape: base -> peak -> base
        internal void PlayOneShotES(string oneShotType, float duration, float baseValue, float peakValue)
        {
            UnityEngine.Debug.Log($">> PlayOneShotES: base: {baseValue}, peak: {peakValue}, realHumanData: {realHumanData} ");
            if (realHumanData == null)
                return;

            if (_oneShotESCoroutine != null)
                return;

            _oneShotESCoroutine = StartCoroutine(PlayOneShotESCoroutine(oneShotType, duration, baseValue, peakValue));
        }

        private IEnumerator PlayOneShotESCoroutine(string oneShotType, float duration, float baseValue, float peakValue)
        {
            if (realHumanData == null)
                yield break;

            duration = Mathf.Max(0.01f, duration);
            baseValue = Mathf.Clamp(baseValue, 0f, 100f);
            peakValue = Mathf.Clamp(peakValue, 0f, 100f);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                // 0 -> 1 -> 0 profile over one-shot window.
                float wave01 = Mathf.Sin(t * Mathf.PI);
                float slider = Mathf.Lerp(baseValue, peakValue, wave01);

                switch (oneShotType)
                {
                    case "enter":
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                        SetBlendShape(slider, realHumanData.vagina_up_idx_in_body);
                        SetBlendShape(slider, realHumanData.vagina_open_squeeze_idx_in_body);
                        SetBlendShape(slider, realHumanData.vagina_open_all_outside_idx_in_body);
                        SetBlendShape(slider, realHumanData.vagina_open_all_outside_idx_in_body);     
#endif                        
                        break;
                    case "exit":
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                        SetBlendShape(slider, realHumanData.vagina_up_idx_in_body);
                        SetBlendShape(slider, realHumanData.vagina_open_squeeze_idx_in_body);
                        SetBlendShape(slider, realHumanData.vagina_open_all_outside_idx_in_body);
                        SetBlendShape(slider, realHumanData.vagina_open_all_outside_idx_in_body);   
#endif                        
                        break;                  
                    default:
                        break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Ensure the end state restores to the specified baseline.
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
            SetBlendShape(baseValue, realHumanData.vagina_up_idx_in_body);
            SetBlendShape(baseValue, realHumanData.vagina_open_squeeze_idx_in_body);
            SetBlendShape(baseValue, realHumanData.vagina_open_all_outside_idx_in_body);
#endif
            if (oneShotType == "enter")
                realHumanData.RealPlayActive = true;
            else if (oneShotType == "exit")
                realHumanData.RealPlayActive = false;

            _oneShotESCoroutine = null;
        }
    }
}
