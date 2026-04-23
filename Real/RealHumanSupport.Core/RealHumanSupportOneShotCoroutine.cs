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
        // type (0: 0 -> strength, 1: strength -> 0)
        internal void PlayOneShot(string oneShotType = "insert", float duration = 0.45f, float strength = 1.0f, int type = 0)
        {
            if (realHumanData == null)
                return;

            if (_oneShotCoroutine != null)
                return;

            _oneShotCoroutine = StartCoroutine(PlayOneShotCoroutine(oneShotType, duration, strength, type));
        }

        private IEnumerator PlayOneShotCoroutine(string oneShotType, float duration, float strength, int type)
        {
            if (realHumanData == null)
                yield break;

            duration = Mathf.Max(0.01f, duration);
            strength = Mathf.Clamp01(strength);
            type = Mathf.Clamp(type, 0, 1);
            float elapsed = 0f;
            float startValue = type == 0 ? 0f : strength;
            float endValue = type == 0 ? strength : 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float easedT = Mathf.SmoothStep(0f, 1f, t);
                float slider = Mathf.Lerp(startValue, endValue, easedT);

                switch (oneShotType)
                {
                    case "insert":
                        SetBlendShape(strength, realHumanData.vagina_up_idx_in_body);
                        SetBlendShape(strength, realHumanData.vagina_open_squeeze_idx_in_body);
                        SetBlendShape(strength, realHumanData.vagina_open_all_outside_idx_in_body);
                        SetBlendShape(strength, realHumanData.vagina_open_all_outside_idx_in_body);                    
                        break;
                    case "remove":    
                        SetBlendShape(strength, realHumanData.vagina_up_idx_in_body);
                        SetBlendShape(strength, realHumanData.vagina_open_squeeze_idx_in_body);
                        SetBlendShape(strength, realHumanData.vagina_open_all_outside_idx_in_body);
                        SetBlendShape(strength, realHumanData.vagina_open_all_outside_idx_in_body);                       
                        break;                  
                    default:
                        break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            _oneShotCoroutine = null;
        }
    }
}
