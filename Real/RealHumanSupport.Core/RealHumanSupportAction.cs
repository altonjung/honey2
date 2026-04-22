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
        internal static void DoExpression(ChaControl chaCtrl, ChaControl targetChaCtrl, int eyeType, int mouthType, RealHumanData realHumanData)
        {
			chaCtrl.ChangeEyebrowPtn(0, true);
			chaCtrl.ChangeEyesPtn(eyeType, true);
			chaCtrl.ChangeMouthPtn(mouthType, true);
			chaCtrl.ChangeLookEyesPtn(0);
            chaCtrl.ChangeLookEyesTarget(0, targetChaCtrl != null ? targetChaCtrl.transform: null, 0.5f, 0f, 1f, 2f);
			chaCtrl.ChangeLookNeckPtn(1, 1f);
			chaCtrl.ChangeLookNeckTarget(0, targetChaCtrl != null ? targetChaCtrl.transform : null, 0.5f, 0f, 1f, 0.8f);            
        }

        internal static void DoBodyBumpEffect(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            if (chaCtrl.sex == 0)
                return;

            if (!RealHumanSupport.BodyBumpMapActive.Value)
                return;

            if (realHumanData.m_skin_body == null)
                return;

            OCIChar ociChar = chaCtrl.GetOCIChar();

            Texture origin_texture = realHumanData.bodyOriginTexture;

            realHumanData.areas.Clear();

            PositionData fk_neck = GetRelativeBoneAngle(realHumanData.fk_hip_bone, realHumanData.fk_neck_bone);
            PositionData fk_head = GetRelativeBoneAngle(realHumanData.fk_head_bone, realHumanData.fk_neck_bone);

            PositionData fk_left_foot = GetRelativeBoneAngle(realHumanData.fk_left_knee_bone, realHumanData.fk_left_foot_bone);
            PositionData fk_right_foot = GetRelativeBoneAngle(realHumanData.fk_right_knee_bone, realHumanData.fk_right_foot_bone);

            PositionData fk_left_knee = GetRelativeBoneAngle(realHumanData.fk_left_thigh_bone, realHumanData.fk_left_knee_bone);
            PositionData fk_right_knee = GetRelativeBoneAngle(realHumanData.fk_right_thigh_bone, realHumanData.fk_right_knee_bone);

            PositionData fk_left_thigh = GetRelativeBoneAngle(realHumanData.fk_hip_bone, realHumanData.fk_left_thigh_bone);
            PositionData fk_right_thigh = GetRelativeBoneAngle(realHumanData.fk_hip_bone, realHumanData.fk_right_thigh_bone);

            PositionData fk_spine01 = GetRelativeBoneAngle(realHumanData.fk_hip_bone, realHumanData.fk_spine01_bone);
            PositionData fk_spine02 = GetRelativeBoneAngle(realHumanData.fk_spine01_bone, realHumanData.fk_spine02_bone);

            PositionData fk_left_shoulder= GetRelativeBoneAngle(realHumanData.fk_neck_bone, realHumanData.fk_left_shoulder_bone);
            PositionData fk_right_shoulder= GetRelativeBoneAngle(realHumanData.fk_neck_bone, realHumanData.fk_right_shoulder_bone);

            PositionData fk_left_armup= GetRelativeBoneAngle(realHumanData.fk_left_shoulder_bone, realHumanData.fk_left_armup_bone);
            PositionData fk_right_armup= GetRelativeBoneAngle(realHumanData.fk_right_shoulder_bone, realHumanData.fk_right_armup_bone);

            PositionData fk_left_armdown= GetRelativeBoneAngle(realHumanData.fk_left_armup_bone, realHumanData.fk_left_armdown_bone);
            PositionData fk_right_armdown= GetRelativeBoneAngle(realHumanData.fk_right_armup_bone, realHumanData.fk_right_armdown_bone);
            
            float left_shin_bs = 0.0f;
            float left_calf_bs = 0.0f;
            float left_butt_bs = 0.0f;
            float left_thigh_ft_bs = 0.0f;
            float left_thigh_bk_bs = 0.0f;
            float left_thigh_inside_bs = 0.0f;
            float left_ribs_bs = 0.0f;
            float left_neck_bs = 0.0f;

            float right_shin_bs = 0.0f;
            float right_calf_bs = 0.0f;
            float right_butt_bs = 0.0f;
            float right_thigh_ft_bs = 0.0f;
            float right_thigh_bk_bs = 0.0f;
            float right_thigh_inside_bs = 0.0f;
            float right_ribs_bs = 0.0f;
            float right_neck_bs = 0.0f;

            float spine_bs = 0.0f;       
            float neck_bs = 0.0f;

            float left_armup_bs = 0.0f;
            float left_armdown_bs = 0.0f;

            float right_armup_bs = 0.0f;
            float right_armdown_bs = 0.0f;

            float bumpscale = 0.0f;
            float angle = 0.0f;

            float Scale(float value, float inMin, float inMax, float outMin, float outMax, float cap)
            {
                return Math.Min(Remap(value, inMin, inMax, outMin, outMax), cap);
            }

            void AddAreaIfNonZero(int x, int y, int w, int h, float value, float cap)
            {
                if (value != 0.0f)
                    realHumanData.areas.Add(InitBArea(x, y, w, h, Math.Min(value, cap)));
            }

            void SetPrev(ref Quaternion prev, Quaternion cur)
            {
                prev = cur;
            }

            // if (ociChar.oiCharInfo.enableFK) 
            {
        // Comment normalized to English.
                angle = Math.Abs(fk_head._frontback);
                if (fk_head._frontback > 3.0f) // Comment normalized to English.
                {
                    bumpscale = Scale(angle, 3.0f, 50.0f, 0.0f, 1.0f, 1.0f);
                    neck_bs = bumpscale * 0.5f;
                } else
                { 
                    bumpscale = Scale(angle, 3.0f, 70.0f, 0.0f, 1.0f, 1.0f);
                    spine_bs = bumpscale * 0.5f;
                }
                // UnityEngine.Debug.Log($">> head angle {angle}, front {fk_head._frontback}, spine_bs {spine_bs},  neck_bs {neck_bs}");
        // Comment normalized to English.
                angle = Math.Abs(fk_neck._frontback);
                if (fk_neck._frontback > 3.0f) // Comment normalized to English.
                {
                    bumpscale = Scale(angle, 3.0f, 70.0f, 0.0f, 1.0f, 1.0f);
                    spine_bs = bumpscale * 0.5f;
                } else
                {
                    bumpscale = Scale(angle, 3.0f, 50.0f, 0.0f, 1.0f, 1.0f);
                    neck_bs = bumpscale * 0.5f;
                }
                // UnityEngine.Debug.Log($">> neck angle {angle}, front {fk_neck._frontback}, spine_bs {spine_bs},  neck_bs {neck_bs}");
                // Comment normalized to English.

        // Comment normalized to English.
                angle = Math.Abs(fk_spine01._frontback);   
                if (fk_spine01._frontback > 5.0f)
                { 
                    // Comment normalized to English.
                    bumpscale = Scale(angle, 5.0f, 90.0f, 0.0f, 1.0f, 1.0f);
                    spine_bs = bumpscale * 0.9f;
                } 
                else
                {   
                    // Comment normalized to English.
                    bumpscale = Scale(angle, 5.0f, 60.0f, 0.0f, 1.0f, 1.0f);
                    left_ribs_bs = bumpscale * 0.5f;
                    right_ribs_bs = bumpscale * 0.5f;
                }

                // UnityEngine.Debug.Log($">> fk_spine0-1 angle {angle}, front {fk_spine01._frontback}, spine_bs {spine_bs},  rib_bs {left_ribs_bs}");

                angle = Math.Abs(fk_spine02._frontback);   
                if (fk_spine02._frontback > 5.0f)
                { 
                    // Comment normalized to English.
                    bumpscale = Scale(angle, 5.0f, 90.0f, 0.0f, 1.0f, 1.0f);
                    spine_bs = bumpscale * 0.9f;
                } 
                else
                {   
                    // Comment normalized to English.
                    bumpscale = Scale(angle, 5.0f, 60.0f, 0.0f, 1.0f, 1.0f);
                    left_ribs_bs = bumpscale * 0.5f;
                    right_ribs_bs = bumpscale * 0.5f;
                }

                // UnityEngine.Debug.Log($">> fk_spine02 angle {angle}, front {fk_neck._frontback}, spine_bs {spine_bs},  rib_bs {left_ribs_bs}");

                if (fk_spine02._leftright > 5.0f)
                {   // Comment normalized to English.
                    bumpscale = Scale(Math.Abs(fk_spine02._leftright), 5.0f, 60.0f, 0.0f, 1.0f, 1.0f);
                    left_ribs_bs += bumpscale * 0.5f;                  
                } 
                else
                {   // Comment normalized to English.
                    bumpscale = Scale(Math.Abs(fk_spine02._leftright), 5.0f, 60.0f, 0.0f, 1.0f, 1.0f);
                    right_ribs_bs += bumpscale * 0.5f;
                }

        // Comment normalized to English.
                angle = Math.Abs(fk_left_thigh._frontback);
                if (fk_left_thigh._frontback > 5.0f)
                {
                        // Comment normalized to English.
                    bumpscale = Scale(angle, 5.0f, 120.0f, 0.1f, 1.0f, 1.0f);
                    left_butt_bs += bumpscale * 0.7f;
                    left_thigh_bk_bs += bumpscale * 0.7f;
                } 
                else
                {
                    // Comment normalized to English.
                    bumpscale = Scale(angle, 0.0f, 120.0f, 0.0f, 1.0f, 1.0f);
                    left_thigh_ft_bs += bumpscale * 0.5f; 
                    if (angle >= 15.0f) {
                        // Comment normalized to English.
                        bumpscale = Scale(angle, 15.0f, 90.0f, 0.15f, 1.0f, 1.0f);
                        left_thigh_inside_bs += bumpscale * 0.6f;
                        // Comment normalized to English.
                    }                    
                }

        // Comment normalized to English.
                angle = Math.Abs(fk_right_thigh._frontback);
                if (fk_right_thigh._frontback > 5.0f)
                {
                    // Comment normalized to English.
                    bumpscale = Scale(angle, 5.0f, 120.0f, 0.1f, 1.0f, 1.0f);
                    right_butt_bs += bumpscale * 0.7f;
                    right_thigh_bk_bs += bumpscale * 0.7f;                         
                }  
                else
                {
                    // Comment normalized to English.
                    bumpscale = Scale(angle, 0.0f, 120.0f, 0.0f, 1.0f, 1.0f);
                    right_thigh_ft_bs += bumpscale * 0.5f;
                    if (angle >= 15.0f) {
                        // Comment normalized to English.
                        bumpscale = Scale(angle, 15.0f, 90.0f, 0.15f, 1.0f, 1.0f);
                        right_thigh_inside_bs += bumpscale * 0.6f;
                        // Comment normalized to English.
                    }                           
                }

        // Comment normalized to English.
                // Comment normalized to English.
                if (fk_left_knee._frontback > 5) 
                {  // Comment normalized to English.
                    angle = Math.Abs(fk_left_knee._frontback);                    
                    bumpscale = Scale(angle, 5.0f, 90.0f, 0.1f, 1.0f, 1.0f);
                    left_butt_bs += bumpscale * 0.3f;     
                    left_thigh_bk_bs += bumpscale * 0.3f;
                    
                    if (angle >= 90)  {
                        bumpscale = Scale(angle, 90.0f, 160.0f, 0.4f, 1.0f, 1.0f);
                        left_shin_bs += bumpscale * 0.3f;
                        left_thigh_inside_bs += bumpscale * 0.3f;
                    }
                }

        // Comment normalized to English.
                // Comment normalized to English.
                if (fk_right_knee._frontback > 5)
                {  // Comment normalized to English.
                    angle = Math.Abs(fk_right_knee._frontback);                    
                    bumpscale = Scale(angle, 5.0f, 90.0f, 0.1f, 1.0f, 1.0f);
                    right_butt_bs += bumpscale * 0.3f;
                    right_thigh_bk_bs += bumpscale * 0.3f;

                    if (angle >= 90)  {
                        bumpscale = Scale(angle, 90.0f, 160.0f, 0.4f, 1.0f, 1.0f);
                        right_shin_bs += bumpscale * 0.3f;
                        right_thigh_inside_bs += bumpscale * 0.3f;
                    }
                }
        // Comment normalized to English.
                // Comment normalized to English.
                if (fk_left_foot._frontback > 5)
                {   // Comment normalized to English.
                    angle = Math.Abs(fk_left_foot._frontback);                    
                    bumpscale = Scale(angle, 5.0f, 70.0f, 0.1f, 1f, 1f);
                    left_shin_bs += bumpscale * 0.2f;
                    left_thigh_bk_bs += bumpscale * 0.3f;
                    left_calf_bs += bumpscale * 0.7f;       
                    // TODO
                    // Comment normalized to English.
                }

                // Comment normalized to English.
                if (fk_right_foot._frontback > 5)
                {   // Comment normalized to English.
                    angle = Math.Abs(fk_right_foot._frontback);
                    bumpscale = Scale(angle, 5.0f, 70.0f, 0.1f, 1f, 1f);
                    right_shin_bs += bumpscale * 0.2f;
                    right_thigh_bk_bs += bumpscale * 0.3f;
                    right_calf_bs += bumpscale * 0.7f;
                    // TODO
                    // Comment normalized to English.
                }

        // Comment normalized to English.
                // Comment normalized to English.
#if FEATURE_BODYBUMP_ARM_SUPPORT                    
                if (fk_left_armup._frontback > 5)
                {   // Comment normalized to English.
                    angle = Math.Abs(fk_left_armup._frontback);
                    bumpscale = Scale(angle, 5.0f, 90.0f, 0.1f, 1f, 1f);
                    left_armup_bs += bumpscale * 0.8f;
                }

                // Comment normalized to English.
                if (fk_right_armup._frontback > 5)
                {   // Comment normalized to English.
                    angle = Math.Abs(fk_right_armup._frontback);
                    bumpscale = Scale(angle, 5.0f, 90.0f, 0.1f, 1f, 1f);
                    right_armup_bs += bumpscale * 0.8f;
                }


                // Comment normalized to English.
                if (fk_left_armdown._frontback > 5)
                {   // Comment normalized to English.
                    angle = Math.Abs(fk_left_armdown._frontback);
                    bumpscale = Scale(angle, 5.0f, 90.0f, 0.1f, 1f, 1f);
                    left_armdown_bs += bumpscale * 0.6f;
                }

                // Comment normalized to English.
                if (fk_right_armdown._frontback > 5)
                {   // Comment normalized to English.
                    angle = Math.Abs(fk_right_armdown._frontback);
                    bumpscale = Scale(angle, 5.0f, 90.0f, 0.1f, 1f, 1f);
                    right_armdown_bs += bumpscale * 0.6f;
                }                    

                // UnityEngine.Debug.Log($">> fk_left_armdown._frontback {fk_left_armdown._frontback} in body");
                // UnityEngine.Debug.Log($">> fk_right_armdown._frontback {fk_right_armdown._frontback} in body");
                // UnityEngine.Debug.Log($">> fk_left_armup._frontback {fk_left_armup._frontback} in body");
                // UnityEngine.Debug.Log($">> fk_right_armup._frontback {fk_right_armup._frontback} in body");
#endif                    
            }
            
        // Comment normalized to English.
            if (neck_bs >= 0.0f)
            {
                realHumanData.areas.Add(InitBArea(260, 100, 140, 90, Math.Min(Math.Abs(neck_bs), 1.8f))); // Comment normalized to English.
            } else {
                realHumanData.areas.Add(InitBArea(770, 200, 140, 90, Math.Min(Math.Abs(neck_bs), 1.8f))); // Comment normalized to English.
            }
        // Comment normalized to English.
            if (spine_bs >= 0.0f)
            {
                realHumanData.areas.Add(InitBArea(770, 300, 80, 240, Math.Min(Math.Abs(spine_bs), 1.8f))); // Comment normalized to English.
            }
        // Comment normalized to English.
            AddAreaIfNonZero(220, 90, 35, 80, left_neck_bs, 1.8f);
            AddAreaIfNonZero(150, 520, 145, 160, left_ribs_bs, 2.0f);
            AddAreaIfNonZero(365, 970, 80, 120, left_thigh_ft_bs, 1.8f); // Comment normalized to English.
            AddAreaIfNonZero(330, 900, 50, 180, left_thigh_inside_bs, 1.8f); // Comment normalized to English.
            AddAreaIfNonZero(400, 1450, 110, 300, left_shin_bs, 1.8f); // Comment normalized to English.
            AddAreaIfNonZero(660, 1030, 60, 160, left_thigh_bk_bs, 1.8f); // Comment normalized to English.
            AddAreaIfNonZero(650, 850, 85, 90, left_butt_bs, 1.8f); // Comment normalized to English.
            AddAreaIfNonZero(670, 1420, 95, 140, left_calf_bs, 1.8f); // Comment normalized to English.

        // Comment normalized to English.
            AddAreaIfNonZero(300, 90, 35, 80, right_neck_bs, 1.8f);
            AddAreaIfNonZero(370, 520, 145, 160, right_ribs_bs, 2.0f);
            AddAreaIfNonZero(145, 970, 80, 120, right_thigh_ft_bs, 1.8f); // Comment normalized to English.
            AddAreaIfNonZero(180, 900, 50, 180, right_thigh_inside_bs, 1.8f); // Comment normalized to English.
            AddAreaIfNonZero(120, 1450, 110, 300, right_shin_bs, 1.8f); // Comment normalized to English.
            AddAreaIfNonZero(900, 1030, 60, 160, right_thigh_bk_bs, 1.8f); // Comment normalized to English.
            AddAreaIfNonZero(920, 850, 85, 90, right_butt_bs, 1.8f); // Comment normalized to English.
            AddAreaIfNonZero(890, 1420, 95, 140, right_calf_bs, 1.8f); // Comment normalized to English.

            SetPrev(ref realHumanData.prev_fk_left_foot_rot, fk_left_foot._q);
            SetPrev(ref realHumanData.prev_fk_right_foot_rot, fk_right_foot._q);
            SetPrev(ref realHumanData.prev_fk_left_knee_rot, fk_left_knee._q);
            SetPrev(ref realHumanData.prev_fk_right_knee_rot, fk_right_knee._q);
            SetPrev(ref realHumanData.prev_fk_left_thigh_rot, fk_left_thigh._q);
            SetPrev(ref realHumanData.prev_fk_right_thigh_rot, fk_right_thigh._q);
            SetPrev(ref realHumanData.prev_fk_spine01_rot, fk_spine01._q);
            SetPrev(ref realHumanData.prev_fk_spine02_rot, fk_spine02._q);
            SetPrev(ref realHumanData.prev_fk_head_rot, fk_head._q);
            SetPrev(ref realHumanData.prev_fk_left_shoulder_rot, fk_left_shoulder._q);
            SetPrev(ref realHumanData.prev_fk_right_shoulder_rot, fk_right_shoulder._q);
            SetPrev(ref realHumanData.prev_fk_right_armup_rot, fk_right_armup._q);
            SetPrev(ref realHumanData.prev_fk_left_armup_rot, fk_left_armup._q);
            SetPrev(ref realHumanData.prev_fk_right_armdown_rot, fk_right_armdown._q);
            SetPrev(ref realHumanData.prev_fk_left_armdown_rot, fk_left_armdown._q);

            if (origin_texture != null)
            {
                int kernel = RealHumanSupport._self._mergeComputeShader.FindKernel("CSMain");

                int w = 2048;
                int h = 2048;

                // Comment normalized to English.
                if (realHumanData._body_rt == null || realHumanData._body_rt.width != w || realHumanData._body_rt.height != h)
                {
                    if (realHumanData._body_rt != null) realHumanData._body_rt.Release();
                    realHumanData._body_rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    realHumanData._body_rt.enableRandomWrite = true;
                    realHumanData._body_rt.Create();
                }

                // Comment normalized to English.
                if (realHumanData.areas.Count > 0 && realHumanData.body_areaBuffer != null)
                {
                    realHumanData.body_areaBuffer.SetData(realHumanData.areas.ToArray());
                    // Comment normalized to English.
                    RealHumanSupport._self._mergeComputeShader.SetInt("Width", w);
                    RealHumanSupport._self._mergeComputeShader.SetInt("Height", h);
                    RealHumanSupport._self._mergeComputeShader.SetInt("AreaCount", realHumanData.areas.Count);
                    RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexA", origin_texture);
                    RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexB", RealHumanSupport._self._bodyStrongFemaleBumpMap2);
                    RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "Result", realHumanData._body_rt);
                    RealHumanSupport._self._mergeComputeShader.SetBuffer(kernel, "Areas", realHumanData.body_areaBuffer);

                    // Comment normalized to English.
                    RealHumanSupport._self._mergeComputeShader.Dispatch(kernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);                 
                    
                    realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_type, realHumanData._body_rt);

                    // Texture2D merged =  MergeRGBAlphaMaps(origin_texture, strong_texture, areas);    
                    // realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_type, merged);
                    // SaveAsPNG(merged, "./body_merge.png");
                    // SaveAsPNG(strong_texture, "./body_strong.png");
                    // SaveAsPNG(RenderTextureToTexture2D(RealHumanSupport._self._body_rt), "./body_merged.png");                     
                }
            }            
            else
            {
                realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_type, realHumanData.bodyOriginTexture);
            }
        }

        internal static PositionData GetBoneRotationFromFK(OCIChar.BoneInfo info)
        {
            Transform t = info.guideObject.transform;
            Transform p = t.parent;

            Vector3 baseForward = p ? p.forward : Vector3.forward;
            Vector3 baseRight   = p ? p.right   : Vector3.right;
            Vector3 baseUp      = p ? p.up      : Vector3.up;

            Vector3 fwd = t.forward;

            // Comment normalized to English.
            Vector3 fwdPlane = Vector3.ProjectOnPlane(fwd, baseRight).normalized;
            float pitch = Vector3.SignedAngle(
                baseForward,
                fwdPlane,
                baseRight
            );

            // Comment normalized to English.
            Vector3 right = t.right;
            Vector3 rightPlane = Vector3.ProjectOnPlane(right, baseForward).normalized;
            float sideZ = Vector3.SignedAngle(
                baseRight,
                rightPlane,
                baseForward
            );

            return new PositionData(t.rotation, pitch, sideZ);
        }

        internal static PositionData GetRelativeBoneAngle(
            OCIChar.BoneInfo parentInfo,
            OCIChar.BoneInfo childInfo)
        {
            Transform parent   = parentInfo.guideObject.transform;
            Transform child = childInfo.guideObject.transform;

            // Comment normalized to English.
            Vector3 baseForward = parent.forward;
            Vector3 baseRight   = parent.right;
            Vector3 baseUp      = parent.up;

            // Comment normalized to English.
            Vector3 childForward = child.forward;
            Vector3 childRight   = child.right;

            // =========================
            // Comment normalized to English.
            // Comment normalized to English.
            // =========================
            Vector3 pitchProjected =
                Vector3.ProjectOnPlane(childForward, baseRight).normalized;

            float pitch = Vector3.SignedAngle(
                baseForward,
                pitchProjected,
                baseRight
            );

            // =========================
            // Comment normalized to English.
            // Comment normalized to English.
            // =========================
            Vector3 sideProjected =
                Vector3.ProjectOnPlane(childRight, baseForward).normalized;

            float side = Vector3.SignedAngle(
                baseRight,
                sideProjected,
                baseForward
            );

            return new PositionData(
                child.rotation,   // Comment normalized to English.
                pitch,            // Comment normalized to English.
                side              // Comment normalized to English.
            );
        }

        internal static void DoFaceBumpEffect(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            if (chaCtrl.sex == 0)
                return;

            if (realHumanData.m_skin_head == null)
                return;

            if (RealHumanSupport.BodyBumpMapActive.Value)
            {
                Texture origin_texture = realHumanData.headOriginTexture;
                Texture express_texture = null;

                if (chaCtrl.sex == 1) // female
                {
                    express_texture = RealHumanSupport._self._faceExpressionFemaleBumpMap2;
                    List<BAreaData> areas = new List<BAreaData>();

                    // face
                    areas.Add(InitBArea(512, 512, 180, 180, 0.5f));

                    if (origin_texture != null)
                    {
                        int kernel = RealHumanSupport._self._mergeComputeShader.FindKernel("CSMain");
                        int w = 1024;
                        int h = 1024;

                        // Comment normalized to English.
                        if (realHumanData._head_rt == null || realHumanData._head_rt.width != w || realHumanData._head_rt.height != h)
                        {
                            if (realHumanData._head_rt != null) realHumanData._head_rt.Release();
                            realHumanData._head_rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                            realHumanData._head_rt.enableRandomWrite = true;
                            realHumanData._head_rt.Create();
                        }        

                    // Comment normalized to English.
                        if (areas.Count > 0 && realHumanData.head_areaBuffer != null)
                        {
                            realHumanData.head_areaBuffer.SetData(areas.ToArray());
                            // Comment normalized to English.
                            RealHumanSupport._self._mergeComputeShader.SetInt("Width", w);
                            RealHumanSupport._self._mergeComputeShader.SetInt("Height", h);
                            RealHumanSupport._self._mergeComputeShader.SetInt("AreaCount", areas.Count);
                            RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexA", origin_texture);
                            RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexB", express_texture);
                            RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "Result", realHumanData._head_rt);
                            RealHumanSupport._self._mergeComputeShader.SetBuffer(kernel, "Areas", realHumanData.head_areaBuffer);

                            // Comment normalized to English.
                            RealHumanSupport._self._mergeComputeShader.Dispatch(kernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);

                            // Comment normalized to English.
                            realHumanData.m_skin_head.SetTexture(realHumanData.head_bumpmap_type, realHumanData._head_rt);                
                        }
                    }                
                }
            } 
            else
            {
                realHumanData.m_skin_head.SetTexture(realHumanData.head_bumpmap_type, realHumanData.headOriginTexture);
            }
        }
    }
}
