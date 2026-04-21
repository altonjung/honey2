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
           float tearValue = 0;
           float noseValue = 0;
           bool tearIncreasing = true;
           bool noseIncreasing = true;
           float nextHeadRaycastTime = 0f;
           RaycastHit[] headRayHits = new RaycastHit[8];
           
           float previosBellySize = 0.0f;
           float initBellySize = 0.0f;

           if (realHumanData.pregnancyController != null)
               initBellySize = realHumanData.pregnancyController.infConfig.inflationSize;

           while (true)
           {
               if (status == Status.RUN) // play
                {
                   // [Head Raycast Detection]
                   // head_bone 기준으로 일정 주기마다 Raycast를 발사해 isTrigger collider를 감지한다.
                    if (realHumanData.head_bone != null && Time.time >= nextHeadRaycastTime)
                    {
                       // 다음 Raycast 실행 시각 갱신(과도한 Physics 호출 방지)
                       nextHeadRaycastTime = Time.time + Mathf.Max(0.01f, realHumanData.headRaycastInterval);

                       // 이번 검사 프레임의 감지 결과를 초기화
                       realHumanData.headTriggerDetected = false;
                       realHumanData.headLastTriggerCollider = null;
                       realHumanData.headLastTriggerRigidbody = null;
                       realHumanData.headRigidbodyDetected = false;
                       realHumanData.headLastDetectedRigidbody = null;
                       realHumanData.headLastRigidbodyCollider = null;

                       // head_bone local offset을 world로 변환한 위치에서 forward 방향으로 캐스팅
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

                       // isTrigger collider만 유효 대상으로 채택
                       for (int i = 0; i < hitCount; i++)
                       {
                           Collider hitCollider = headRayHits[i].collider;
                           if (hitCollider == null)
                               continue;

                           Rigidbody hitRigidbody = headRayHits[i].rigidbody != null
                               ? headRayHits[i].rigidbody
                               : hitCollider.attachedRigidbody;

                           // Rigidbody 히트 대상도 별도로 저장/출력
                           if (!realHumanData.headRigidbodyDetected && hitRigidbody != null)
                           {
                               realHumanData.headRigidbodyDetected = true;
                               realHumanData.headLastDetectedRigidbody = hitRigidbody;
                               realHumanData.headLastRigidbodyCollider = hitCollider;
                               UnityEngine.Debug.Log($"[HeadRaycast] Rigidbody detected: rigidbody={hitRigidbody.name}, collider={hitCollider.name}");
                           }

                           if (!hitCollider.isTrigger)
                               continue;

                           // 첫 번째 유효 트리거 히트를 결과로 저장
                           realHumanData.headTriggerDetected = true;
                           realHumanData.headLastTriggerCollider = hitCollider;
                           realHumanData.headLastTriggerRigidbody = hitRigidbody;
                           // 감지된 isTrigger 대상 이름 출력
                           UnityEngine.Debug.Log($"[HeadRaycast] Trigger detected: collider={hitCollider.name}, rigidbody={(hitRigidbody != null ? hitRigidbody.name : "null")}");
                           break;
                       }
                    }
            
                    float time = Time.time;
                    float sinValue = (Mathf.Sin(time * realHumanData.BreathInterval) + 1f) * 0.5f;
                    
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
            #if FEATURE_BODY_BLENDSHAPE_SUPPORT
                                SetBlendShape(anusPullWeight, realHumanData.anus_pullout_idx_in_body);
                                SetBlendShape(vaginaFrontWeight, realHumanData.vagina_up_idx_in_body);
                                SetBlendShape(vaginaSqueezeWeight, realHumanData.vagina_open_squeeze_idx_in_body);
                                SetBlendShape(vaginaOutsideWeight, realHumanData.vagina_open_all_outside_idx_in_body);
            #endif
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
                            
                        if (RealHumanSupport.BreathActive.Value)
                        {
                            if (realHumanData.pregnancyController != null)
                            {   
                                if(previosBellySize != realHumanData.pregnancyController.infConfig.inflationSize)
                                {
                                    initBellySize = previosBellySize = realHumanData.pregnancyController.infConfig.inflationSize;

                                    if (initBellySize > 30.0f)
                                    {
                                        initBellySize = 30.0f;
                                    }
                                } else
                                {
                                    realHumanData.pregnancyController.infConfig.inflationSize = initBellySize + (1f - sinValue) * 10f * realHumanData.BreathStrong;
                                    realHumanData.pregnancyController.MeshInflate(new MeshInflateFlags(realHumanData.pregnancyController), "StudioSlider");
                                    previosBellySize = realHumanData.pregnancyController.infConfig.inflationSize;
                                }
                            }
                        }

        #if FEATURE_TEARDROP_SUPPORT
                        if (RealHumanSupport.TearDropActive.Value)
                        {
                            float deltaTear = Time.deltaTime / 10f; // Comment normalized to English.

                            if (tearIncreasing)
                            {
                                tearValue += deltaTear;
                                if (tearValue >= 1f)
                                {
                                    tearValue = 1f;
                                    tearIncreasing = false;
                                }
                            }
                            else
                            {
                                tearValue -= deltaTear;
                                if (tearValue <= 0.3f)
                                {
                                    tearValue = 0.3f;
                                    tearIncreasing = true;
                                }
                            }
                                
                            float tearSin = Mathf.Sin(tearValue * Mathf.PI);

                            // Comment normalized to English.
                            if (realHumanData.m_tear_eye != null) {
                                realHumanData.m_tear_eye.SetFloat("_NamidaScale", realHumanData.tearDropRate);
                                realHumanData.m_tear_eye.SetFloat("_RefractionScale", tearSin); 
                            }

                            float deltaNose = Time.deltaTime / 1.5f; // Comment normalized to English.

                            if (noseIncreasing)
                            {
                                noseValue += deltaNose;
                                if (noseValue >= 1f)
                                {
                                    noseValue = 1f;
                                    noseIncreasing = false;
                                }
                            }
                            else
                            {
                                noseValue -= deltaNose;
                                if (noseValue <= 0.1f)
                                {
                                    noseValue = 0.1f;
                                    noseIncreasing = true;
                                }
                            }
                                
                            float noseSin = Mathf.Sin(noseValue * Mathf.PI);
                            // Comment normalized to English.
                            if (realHumanData.nose_wing_l_tr != null) {

                                float noseScaleFactor = 1f + (noseSin * 0.3f);
                                Vector3 scalel = realHumanData.noseBaseScale;
                                Vector3 scaler = realHumanData.noseBaseScale;

                                scalel.x = realHumanData.noseBaseScale.x * noseScaleFactor;
                                scaler.x = realHumanData.noseBaseScale.x * noseScaleFactor;

                                realHumanData.nose_wing_l_tr.localScale = scalel;
                                realHumanData.nose_wing_r_tr.localScale = scaler;
                            }
                        } 
                        else
                        {
                            if (realHumanData.m_tear_eye != null) {
                                realHumanData.m_tear_eye.SetFloat("_NamidaScale", 0f);
                                realHumanData.m_tear_eye.SetFloat("_RefractionScale", 0f);
                            }

                            if (realHumanData.noseScaleInitialized)
                            {
                                if (realHumanData.nose_wing_l_tr != null)
                                    realHumanData.nose_wing_l_tr.localScale = realHumanData.noseBaseScale;

                                if (realHumanData.nose_wing_r_tr != null)
                                    realHumanData.nose_wing_r_tr.localScale = realHumanData.noseBaseScale;
                            }
                        }
#endif
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

    }
}
