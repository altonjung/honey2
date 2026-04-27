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

        //    if (realHumanData.pregnancyController != null)
        //        initBellySize = realHumanData.pregnancyController.infConfig.inflationSize;

           while (true)
           {
	               if (status == Status.RUN) // play
	                {
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
                    float sinValue = (Mathf.Sin(time) + 1f) * 0.5f;
                    
                    if (realHumanData.chaCtrl.sex == 1) {
                    
                        if (RealHumanSupport.EyeShakeActive.Value)
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

                        if (realHumanData.ActiveRealPlay)
                        {
                            float playStrong = Mathf.Max(0.1f, realHumanData.realPlayStrong);
                            float anusPullWeight = sinValue * 15f * playStrong;
                            float vaginaFrontWeight = sinValue * 30f * playStrong;
                            float vaginaSqueezeWeight = sinValue * 60f * playStrong;
                            float vaginaOutsideWeight = sinValue * 40f * playStrong;
                            float footWeight = sinValue * 80f * playStrong;
                            float thighWeight = sinValue * 20f;
                            // float crotchWeight = sinValue * 5f;
                            // float thighSliderValue = Mathf.Clamp(MapWeightToSliderRange(thighWeight) * playStrong, -1f, 1f);
                            //float crotchSliderValue = Mathf.Clamp(MapWeightToSliderRange(crotchWeight) * playStrong, -1f, 1f);
        #if FEATURE_BODY_BLENDSHAPE_SUPPORT
                            SetBlendShape(anusPullWeight, realHumanData.anus_pullout_idx_in_body);
                            SetBlendShape(vaginaFrontWeight, realHumanData.vagina_up_idx_in_body);
                            SetBlendShape(vaginaSqueezeWeight, realHumanData.vagina_open_squeeze_idx_in_body);
                            SetBlendShape(vaginaOutsideWeight, realHumanData.vagina_open_all_outside_idx_in_body);
        #endif
                            if (realHumanData.jointCorrectionSliderController != null)
                            {
                                // realHumanData.jointCorrectionSliderController.SetThigh(thighSliderValue);   
                                // realHumanData.jointCorrectionSliderController.SetCrotch(crotchSliderValue);
                                // realHumanData.jointCorrectionSliderController.SetBlendShape(footWeight, realHumanData.foot_left_idx_in_body);
                                // realHumanData.jointCorrectionSliderController.SetBlendShape(footWeight, realHumanData.foot_right_idx_in_body);
                            }
                        }
                        else
                        {
                            float vaginaSqueezeWeight = sinValue * 10f;
        #if FEATURE_BODY_BLENDSHAPE_SUPPORT
                            SetBlendShape(0f, realHumanData.anus_pullout_idx_in_body);
                            SetBlendShape(0f, realHumanData.vagina_up_idx_in_body);
                            SetBlendShape(vaginaSqueezeWeight, realHumanData.vagina_open_squeeze_idx_in_body);
                            SetBlendShape(0f, realHumanData.vagina_open_all_outside_idx_in_body);
        #endif
                        }

	                   } 
	                   else
	                   {
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
