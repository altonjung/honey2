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
	      internal IEnumerator CoroutineProcess(RealHumanData realHumanData)
	      {
            float nextHeadRaycastTime = 0f;
            RaycastHit[] headRayHits = new RaycastHit[8];
           
            float previosBellySize = 0.0f;
            float initBellySize = 0.0f;

            if (realHumanData.pregnancyController != null)
               initBellySize = realHumanData.pregnancyController.infConfig.inflationSize;

            while (true)
            {
                    if (status == Status.RUN) {
                        // [Head Raycast Detection]
                        // Cast a ray from head_bone at fixed intervals and detect isTrigger colliders.
                        if (realHumanData.head_bone != null && Time.time >= nextHeadRaycastTime)
                        {
                            // Update next raycast timestamp to avoid excessive physics calls.
                            nextHeadRaycastTime = Time.time + Mathf.Max(0.01f, realHumanData.headRaycastInterval);

                            // Reset detection results for this check frame.
                            realHumanData.headTriggerDetected = false;
                            realHumanData.headLastTriggerCollider = null;
                            realHumanData.headLastTriggerRigidbody = null;
                            realHumanData.headRigidbodyDetected = false;
                            realHumanData.headLastDetectedRigidbody = null;
                            realHumanData.headLastRigidbodyCollider = null;

                            // Cast from the world position converted from head_bone local offset toward forward.
                            Vector3 origin = realHumanData.head_bone.TransformPoint(realHumanData.headRaycastOriginOffset);
                            Vector3 direction = realHumanData.head_bone.forward;
                            float distance = Mathf.Max(0.01f, realHumanData.headRaycastDistance);

                            int hitCount = Physics.RaycastNonAlloc(
                                origin,
                                direction,
                                headRayHits,
                                distance,
                                ~0,
                                QueryTriggerInteraction.Collide);

                            // Keep only isTrigger colliders as valid targets.
                            for (int i = 0; i < hitCount; i++)
                            {
                                Collider hitCollider = headRayHits[i].collider;
                                if (hitCollider == null)
                                    continue;

                                Rigidbody hitRigidbody = headRayHits[i].rigidbody != null
                                    ? headRayHits[i].rigidbody
                                    : hitCollider.attachedRigidbody;

                                // Store and log rigidbody hits separately.
                                if (!realHumanData.headRigidbodyDetected && hitRigidbody != null)
                                {
                                    realHumanData.headRigidbodyDetected = true;
                                    realHumanData.headLastDetectedRigidbody = hitRigidbody;
                                    realHumanData.headLastRigidbodyCollider = hitCollider;
                                    UnityEngine.Debug.Log($"[HeadRaycast] Rigidbody detected: rigidbody={hitRigidbody.name}, collider={hitCollider.name}");
                                }

                                if (!hitCollider.isTrigger)
                                    continue;

                                // Save the first valid trigger hit as the result.
                                realHumanData.headTriggerDetected = true;
                                realHumanData.headLastTriggerCollider = hitCollider;
                                realHumanData.headLastTriggerRigidbody = hitRigidbody;
                                // Log the detected isTrigger target name.
                                UnityEngine.Debug.Log($"[HeadRaycast] Trigger detected: collider={hitCollider.name}, rigidbody={(hitRigidbody != null ? hitRigidbody.name : "null")}");
                                break;
                            }
                        }
                
                        float time = Time.time;
                        // Mathf.Sin(...) is in [-1, 1]. Shift and scale it to normalize into [0, 1].
                        float sinValue = (Mathf.Sin(time * realHumanData.BreathInterval) + 1f) * 0.5f;
                        
                        // male/ female both
                        if (realHumanData.BreathActive)
                        {
                            if (realHumanData.pregnancyController != null)
                            {
                                if (previosBellySize != realHumanData.pregnancyController.infConfig.inflationSize)
                                {
                                    initBellySize = previosBellySize = realHumanData.pregnancyController.infConfig.inflationSize;

                                    if (initBellySize > 30.0f)
                                    {
                                        initBellySize = 30.0f;
                                    }
                                }
                                else
                                {
                                    realHumanData.pregnancyController.infConfig.inflationSize = initBellySize + (1f - sinValue) * 10f * realHumanData.BreathStrong;
                                    realHumanData.pregnancyController.MeshInflate(new MeshInflateFlags(realHumanData.pregnancyController), "StudioSlider");
                                    previosBellySize = realHumanData.pregnancyController.infConfig.inflationSize;
                                }
                            }
                        }

                        if (realHumanData.chaCtrl.sex == 1) {
                            // female
                            if (realHumanData.EyeShakeActive)
                            {
                                foreach (Material mat in realHumanData.c_m_eye)
                                {
                                    if (mat == null)
                                        continue;
                                    // Comment normalized to English.
                                    float easedBump = (Mathf.Sin(time * Mathf.PI * 3.5f * 2f) + 1f) * 0.5f;

                                    float eyeScale = Mathf.Lerp(0.18f, 0.21f, easedBump);
                                    mat.SetFloat("_Texture4Rotator", eyeScale);

                                    eyeScale = Mathf.Lerp(0.1f, 0.2f, easedBump);
                                    mat.SetFloat("_Parallax", eyeScale);
                                }
                            }

                            if (realHumanData.RealPlayActive)
                            {
                                float playStrong = Mathf.Max(0.1f, realHumanData.RealPlayStrong);
                                float blendTarget = Mathf.Clamp(realHumanData.RealPlayBlendShapeTarget, 0f, 100f);
                                // Oscillate around the target center value instead of [0, target].
                                // Example: center=30 and percent=0.10 -> [27, 33].
                                float baseOscillationPercent = Mathf.Clamp(realHumanData.RealPlayOscillationPercent, 0f, 1f);
                                float oscillationPercent = Mathf.Clamp(baseOscillationPercent * playStrong, 0f, 1f);
                                float sinSigned = (sinValue * 2f) - 1f; // [0,1] -> [-1,1]
                                float oscillationRange = blendTarget * oscillationPercent;
                                float centeredWeight = Mathf.Clamp(blendTarget + (sinSigned * oscillationRange), 0f, 100f);

                                float anusPullWeight = centeredWeight;
                                float vaginaFrontWeight = centeredWeight;
                                float vaginaSqueezeWeight = centeredWeight;
                                float vaginaOutsideWeight = centeredWeight;
                                float vaginaTopWeight = centeredWeight;
                                // float footWeight = centeredWeight;
                                // float crotchWeight = centeredWeight;
                                // float crotchSliderValue = Mathf.Clamp(MapWeightToSliderRange(crotchWeight), -1f, 1f);

            #if FEATURE_BODY_BLENDSHAPE_SUPPORT
                                SetBlendShape(anusPullWeight, realHumanData.anus_pullout_idx_in_body);
                                SetBlendShape(vaginaFrontWeight, realHumanData.vagina_up_idx_in_body);
                                SetBlendShape(vaginaSqueezeWeight, realHumanData.vagina_open_squeeze_idx_in_body);
                                SetBlendShape(vaginaTopWeight, realHumanData.vagina_open_all_outside_idx_in_body);
                                SetBlendShape(vaginaOutsideWeight, realHumanData.vagina_open_all_outside_idx_in_body);
            #endif
                            }
                            else if (realHumanData.jointCorrectionSliderController != null)
                            {
                                // Restore to base pose when real-play is disabled.
                                realHumanData.jointCorrectionSliderController.SetLegShrink(0f);
                            }

                        } 
                        else
                        {
                            // male
                            if (realHumanData.dan_bone != null)
                            {
                                // dan periodic scale value
                                float danScale = Mathf.Lerp(0.95f, 1.05f, sinValue);
                                realHumanData.dan_bone.localScale = new Vector3(danScale, danScale, danScale);
                            }
                        }
                    
                    yield return null;
                }
                else
                {
                    yield return new WaitForSeconds(1);
                }
            }
	    }   

        private static float MapWeightToSliderRange(float weight)
        {
            float normalized = Mathf.Clamp(weight, 0f, 100f) / 100f;
            return Mathf.Lerp(-1f, 1f, normalized);
        }
    }
}
