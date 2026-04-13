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
using ExtensibleSaveFormat;
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
    public class RealHumanSupportController: CharaCustomFunctionController
    {
        internal RealHumanData realHumanData;
        internal Status status;// 0: init, 1: pause, 2: play


        protected override void OnCardBeingSaved(GameMode currentGameMode) { }


        internal RealHumanData GetRealHumanData()
        {
            return realHumanData;
        }

        internal void ResetRealHumanData()
        {
            realHumanData.TearDropLevel = 0.3f;
            realHumanData.BreathInterval = 1.5f;
            realHumanData.BreathStrong = 0.45f;
    
        }

        internal void SupportExtraDynamicBones(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            if (chaCtrl.sex == 0)
                return;

            string bone_prefix_str = "cf_";
            if(chaCtrl.sex == 0)
                bone_prefix_str = "cm_";

            //boob/butt에 gravity 자동 부여
            realHumanData.leftBoob = chaCtrl.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.BreastL);
            realHumanData.rightBoob = chaCtrl.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.BreastR);
            realHumanData.leftButtCheek = chaCtrl.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.HipL);
            realHumanData.rightButtCheek = chaCtrl.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.HipR);

            realHumanData.leftBoob.ReflectSpeed = 0.5f;
            realHumanData.leftBoob.Gravity = new Vector3(0, -0.005f, 0);
            realHumanData.leftBoob.Force = new Vector3(0, -0.01f, 0);
            realHumanData.leftBoob.HeavyLoopMaxCount = 5;

            realHumanData.rightBoob.ReflectSpeed = 0.5f;
            realHumanData.rightBoob.Gravity = new Vector3(0, -0.005f, 0);
            realHumanData.rightBoob.Force = new Vector3(0, -0.01f, 0);
            realHumanData.rightBoob.HeavyLoopMaxCount = 5;

            realHumanData.leftButtCheek.Gravity = new Vector3(0, -0.005f, 0);
            realHumanData.leftButtCheek.Force = new Vector3(0, -0.01f, 0);
            realHumanData.leftButtCheek.HeavyLoopMaxCount = 4;

            realHumanData.rightButtCheek.Gravity = new Vector3(0, -0.005f, 0);
            realHumanData.rightButtCheek.Force = new Vector3(0, -0.01f, 0);
            realHumanData.rightButtCheek.HeavyLoopMaxCount = 4;

            // boob/butt/hair dynamicbone에 body&leg&arm&finger collider 연결
            DynamicBoneCollider[] existingDynamicBoneColliders = chaCtrl.transform.FindLoop(bone_prefix_str+"J_Root").GetComponentsInChildren<DynamicBoneCollider>(true);
            List<DynamicBoneCollider> extraBoobColliders = new List<DynamicBoneCollider>();

            Transform handLObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_L");
            Transform handRObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_R");
            
            Transform fingerIdx2LObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Index02_L");
            Transform fingerIdx2RObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Index02_R");
            
            List<DynamicBoneCollider> extraHandsColliders = new List<DynamicBoneCollider>();
  
            extraHandsColliders.Add(AddExtraDynamicBoneCollider(handLObject, DynamicBoneColliderBase.Direction.X, 0.25f, 1.2f, new Vector3(-0.4f, 0, 0)));
            extraHandsColliders.Add(AddExtraDynamicBoneCollider(handRObject, DynamicBoneColliderBase.Direction.X, 0.25f, 1.2f, new Vector3(0.4f, 0, 0)));
            
            extraHandsColliders.Add(AddExtraDynamicBoneCollider(fingerIdx2LObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0.8f, new Vector3(-0.05f, 0, 0)));
            extraHandsColliders.Add(AddExtraDynamicBoneCollider(fingerIdx2RObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0.8f, new Vector3(0.05f, 0, 0)));
            
            extraBoobColliders.AddRange(extraHandsColliders);
            
            foreach (DynamicBoneCollider collider in existingDynamicBoneColliders)
            {
                if (collider.name.Contains("Leg") || collider.name.Contains("Arm"))
                {
                    extraBoobColliders.Add(collider);                    
                }
            }
            // finger/leg/arm collider를 boob/butt bone 에 주입            
            foreach (var collider in extraBoobColliders)
            {
                if (collider == null)
                    continue;

                if (!realHumanData.leftBoob.Colliders.Contains(collider))
                {
                    realHumanData.leftBoob.Colliders.Add(collider);
                }

                if (!realHumanData.rightBoob.Colliders.Contains(collider))
                {
                    realHumanData.rightBoob.Colliders.Add(collider);
                }   

                if (!realHumanData.leftButtCheek.Colliders.Contains(collider))
                {
                    realHumanData.leftButtCheek.Colliders.Add(collider);
                }   

                if (!realHumanData.rightButtCheek.Colliders.Contains(collider))
                {
                    realHumanData.rightButtCheek.Colliders.Add(collider);
                }                                               
            }

            List<DynamicBoneCollider> extraBodyColliders = new List<DynamicBoneCollider>();       

            extraBodyColliders.AddRange(extraHandsColliders); // 손(손가락), 다리 collider 모두 bodyCollider 에 주입
            extraBodyColliders.AddRange(extraBoobColliders);

            Transform faceObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_FaceLow_s");
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(faceObject, DynamicBoneColliderBase.Direction.Y, 0.65f, 2.5f, new Vector3(0.0f, 0.0f, 0.3f)));

            // shoulder collider 생성
            Transform leftShoulderObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_ArmUp00_L");
            Transform rightShoulderObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_ArmUp00_R");

            float leftShoulder_radius = 0.5f;
            float rightShoulder_radius = 0.5f;

            extraBodyColliders.Add(AddExtraDynamicBoneCollider(leftShoulderObject, DynamicBoneColliderBase.Direction.Y, leftShoulder_radius, leftShoulder_radius * 3.0f , new Vector3(0.0f, 0.0f, 0.0f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(rightShoulderObject, DynamicBoneColliderBase.Direction.Y, rightShoulder_radius, rightShoulder_radius * 3.0f, new Vector3(0.0f, 0.0f, 0.0f)));

            // spine collider 생성
            Transform spine1Object = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Spine01");
            Transform spine2Object = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Spine02");
            Transform spine3Object = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Spine03");
   
            float spine1_radius = 0.8f;
            float spine2_radius = 0.9f;
            float spine3_radius = 0.8f;
   
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(spine1Object, DynamicBoneColliderBase.Direction.Y, spine1_radius, spine1_radius * 4.0f, new Vector3(0.0f, 0.0f, 0.0f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(spine2Object, DynamicBoneColliderBase.Direction.Y, spine2_radius, spine2_radius * 3.5f, new Vector3(0.0f, 0.0f, 0.2f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(spine3Object, DynamicBoneColliderBase.Direction.X, spine3_radius, spine3_radius * 4.0f, new Vector3(0.0f, 0.0f, 0.0f)));

            // pelvis collider 생성
            Transform kosi2Object = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Kosi02");
           
            float kosi2_radius = 1.0f;

            extraBodyColliders.Add(AddExtraDynamicBoneCollider(kosi2Object, DynamicBoneColliderBase.Direction.X, kosi2_radius, kosi2_radius * 4.0f, new Vector3(0.0f, -0.15f, -0.05f)));

            Transform siriLObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Siri_L");
            Transform siriRObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Siri_R");

            extraBodyColliders.Add(AddExtraDynamicBoneCollider(siriLObject, DynamicBoneColliderBase.Direction.X, 0.5f, 1.8f, new Vector3(0.0f, -0.25f, 0.0f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(siriRObject, DynamicBoneColliderBase.Direction.X, 0.5f, 1.8f, new Vector3(0.0f, -0.25f, 0.0f)));

            realHumanData.extraBodyColliders = extraBodyColliders
                .Where(v => v != null)
                .Distinct()
                .ToList();

            // body collider를 hair bone 에 주입
            foreach (var bone in realHumanData.hairDynamicBones)
            {
                if (bone == null)
                    continue;

                foreach (var collider in extraBodyColliders)
                {
                    if (collider == null)
                        continue;

                    if (!bone.m_Colliders.Contains(collider))
                    {
                        bone.m_Colliders.Add(collider);
                    }
                }
            }

            SetHairDown();
        }


        internal static void SupportBodyBumpEffect(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            if (chaCtrl.sex == 0)
                return;

            if (!RealHumanSupport.BodyBlendingActive.Value)
                return;

            if (realHumanData.m_skin_body == null)
                return;

            if (StudioAPI.InsideStudio)
            {
                OCIChar ociChar = chaCtrl.GetOCIChar();

                Texture2D origin_texture = realHumanData.bodyOriginTexture;

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
            // 머리 (목하고 반대)
                    angle = Math.Abs(fk_head._frontback);
                    if (fk_head._frontback > 3.0f) // 뒤쪽으로 숙임
                    {
                        bumpscale = Scale(angle, 3.0f, 50.0f, 0.0f, 1.0f, 1.0f);
                        neck_bs = bumpscale * 0.5f;
                    } else
                    { 
                        bumpscale = Scale(angle, 3.0f, 70.0f, 0.0f, 1.0f, 1.0f);
                        spine_bs = bumpscale * 0.5f;
                    }
                    // UnityEngine.Debug.Log($">> head angle {angle}, front {fk_head._frontback}, spine_bs {spine_bs},  neck_bs {neck_bs}");
            // 목 (머리하고 반대)
                    angle = Math.Abs(fk_neck._frontback);
                    if (fk_neck._frontback > 3.0f) // 앞쪽으로 숙임
                    {
                        bumpscale = Scale(angle, 3.0f, 70.0f, 0.0f, 1.0f, 1.0f);
                        spine_bs = bumpscale * 0.5f;
                    } else
                    {
                        bumpscale = Scale(angle, 3.0f, 50.0f, 0.0f, 1.0f, 1.0f);
                        neck_bs = bumpscale * 0.5f;
                    }
                    // UnityEngine.Debug.Log($">> neck angle {angle}, front {fk_neck._frontback}, spine_bs {spine_bs},  neck_bs {neck_bs}");
                    // 좌/우 처리는 나중에

            // 허리
                    angle = Math.Abs(fk_spine01._frontback);   
                    if (fk_spine01._frontback > 5.0f)
                    { 
                        // 앞으로 숙이기
                        bumpscale = Scale(angle, 5.0f, 90.0f, 0.0f, 1.0f, 1.0f);
                        spine_bs = bumpscale * 0.9f;
                    } 
                    else
                    {   
                        // 뒤로 숙이기                    
                        bumpscale = Scale(angle, 5.0f, 60.0f, 0.0f, 1.0f, 1.0f);
                        left_ribs_bs = bumpscale * 0.5f;
                        right_ribs_bs = bumpscale * 0.5f;
                    }

                    // UnityEngine.Debug.Log($">> fk_spine0-1 angle {angle}, front {fk_spine01._frontback}, spine_bs {spine_bs},  rib_bs {left_ribs_bs}");

                    angle = Math.Abs(fk_spine02._frontback);   
                    if (fk_spine02._frontback > 5.0f)
                    { 
                        // 앞으로 숙이기
                        bumpscale = Scale(angle, 5.0f, 90.0f, 0.0f, 1.0f, 1.0f);
                        spine_bs = bumpscale * 0.9f;
                    } 
                    else
                    {   
                        // 뒤로 숙이기                     
                        bumpscale = Scale(angle, 5.0f, 60.0f, 0.0f, 1.0f, 1.0f);
                        left_ribs_bs = bumpscale * 0.5f;
                        right_ribs_bs = bumpscale * 0.5f;
                    }

                    // UnityEngine.Debug.Log($">> fk_spine02 angle {angle}, front {fk_neck._frontback}, spine_bs {spine_bs},  rib_bs {left_ribs_bs}");

                    if (fk_spine02._leftright > 5.0f)
                    {   // 왼쪽 기울기                                            
                        bumpscale = Scale(Math.Abs(fk_spine02._leftright), 5.0f, 60.0f, 0.0f, 1.0f, 1.0f);
                        left_ribs_bs += bumpscale * 0.5f;                  
                    } 
                    else
                    {   // 오른쪽 기울기
                        bumpscale = Scale(Math.Abs(fk_spine02._leftright), 5.0f, 60.0f, 0.0f, 1.0f, 1.0f);
                        right_ribs_bs += bumpscale * 0.5f;
                    }

            // 허벅지 왼쪽
                    angle = Math.Abs(fk_left_thigh._frontback);
                    if (fk_left_thigh._frontback > 5.0f)
                    {
                         // 뒷방향
                        bumpscale = Scale(angle, 5.0f, 120.0f, 0.1f, 1.0f, 1.0f);
                        left_butt_bs += bumpscale * 0.7f;
                        left_thigh_bk_bs += bumpscale * 0.7f;
                    } 
                    else
                    {
                        // 앞방향
                        bumpscale = Scale(angle, 0.0f, 120.0f, 0.0f, 1.0f, 1.0f);
                        left_thigh_ft_bs += bumpscale * 0.5f; 
                        if (angle >= 15.0f) {
                            // 내전근 강조 
                            bumpscale = Scale(angle, 15.0f, 90.0f, 0.15f, 1.0f, 1.0f);
                            left_thigh_inside_bs += bumpscale * 0.6f;
                            // 허벅지 open 여부 확인
                        }                    
                    }

            // 허벅지 오른쪽
                    angle = Math.Abs(fk_right_thigh._frontback);
                    if (fk_right_thigh._frontback > 5.0f)
                    {
                        // 뒷방향                     
                        bumpscale = Scale(angle, 5.0f, 120.0f, 0.1f, 1.0f, 1.0f);
                        right_butt_bs += bumpscale * 0.7f;
                        right_thigh_bk_bs += bumpscale * 0.7f;                         
                    }  
                    else
                    {
                        // 앞방향
                        bumpscale = Scale(angle, 0.0f, 120.0f, 0.0f, 1.0f, 1.0f);
                        right_thigh_ft_bs += bumpscale * 0.5f;
                        if (angle >= 15.0f) {
                            // 내전근 강조                   
                            bumpscale = Scale(angle, 15.0f, 90.0f, 0.15f, 1.0f, 1.0f);
                            right_thigh_inside_bs += bumpscale * 0.6f;
                            // 허벅지 open 여부 확인
                        }                           
                    }

            // 무릎 왼쪽
                    // 허벅지 기준 무릅이 뒷방향으로 굽힘                     
                    if (fk_left_knee._frontback > 5) 
                    {  // 뒷쪽
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

            // 무릎 오른쪽
                    // 허벅지 기준 무릅이 뒷방향으로 굽힘 
                    if (fk_right_knee._frontback > 5)
                    {  // 뒷쪽
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
            // 발목
                    // 왼쪽 무릅 기준 발목이 뒷방향으로 굽힘                                           
                    if (fk_left_foot._frontback > 5)
                    {   // 뒷쪽
                        angle = Math.Abs(fk_left_foot._frontback);                    
                        bumpscale = Scale(angle, 5.0f, 70.0f, 0.1f, 1f, 1f);
                        left_shin_bs += bumpscale * 0.2f;
                        left_thigh_bk_bs += bumpscale * 0.3f;
                        left_calf_bs += bumpscale * 0.7f;       
                        // TODO
                        // 발목 강조                  
                    }

                    // 오른쪽 무릅 기준 발목이 뒷방향으로 굽힘
                    if (fk_right_foot._frontback > 5)
                    {   // 뒷쪽
                        angle = Math.Abs(fk_right_foot._frontback);
                        bumpscale = Scale(angle, 5.0f, 70.0f, 0.1f, 1f, 1f);
                        right_shin_bs += bumpscale * 0.2f;
                        right_thigh_bk_bs += bumpscale * 0.3f;
                        right_calf_bs += bumpscale * 0.7f;
                        // TODO
                        // 발목 강조
                    }

            // 팔         
                    // 왼쪽 어깨 앞쪽으로 굽힘
#if FEATURE_BODYBUMP_ARM_SUPPORT                    
                    if (fk_left_armup._frontback > 5)
                    {   // 뒷쪽
                        angle = Math.Abs(fk_left_armup._frontback);
                        bumpscale = Scale(angle, 5.0f, 90.0f, 0.1f, 1f, 1f);
                        left_armup_bs += bumpscale * 0.8f;
                    }

                    // 오른쪽 어깨 앞쪽으로 굽힘
                    if (fk_right_armup._frontback > 5)
                    {   // 뒷쪽
                        angle = Math.Abs(fk_right_armup._frontback);
                        bumpscale = Scale(angle, 5.0f, 90.0f, 0.1f, 1f, 1f);
                        right_armup_bs += bumpscale * 0.8f;
                    }


                    // 왼쪽 팔 앞쪽으로 굽힘
                    if (fk_left_armdown._frontback > 5)
                    {   // 뒷쪽
                        angle = Math.Abs(fk_left_armdown._frontback);
                        bumpscale = Scale(angle, 5.0f, 90.0f, 0.1f, 1f, 1f);
                        left_armdown_bs += bumpscale * 0.6f;
                    }

                    // 오른쪽 팔 앞쪽으로 굽힘
                    if (fk_right_armdown._frontback > 5)
                    {   // 뒷쪽
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
                
            // 목
                if (neck_bs >= 0.0f)
                {
                    realHumanData.areas.Add(InitBArea(260, 100, 140, 90, Math.Min(Math.Abs(neck_bs), 1.8f))); // 목선
                } else {
                    realHumanData.areas.Add(InitBArea(770, 200, 140, 90, Math.Min(Math.Abs(neck_bs), 1.8f))); // 척추
                }
            // 허리
                if (spine_bs >= 0.0f)
                {
                    realHumanData.areas.Add(InitBArea(770, 300, 80, 240, Math.Min(Math.Abs(spine_bs), 1.8f))); // 척추
                }
            // 왼쪽 강조
                AddAreaIfNonZero(220, 90, 35, 80, left_neck_bs, 1.8f);
                AddAreaIfNonZero(150, 520, 145, 160, left_ribs_bs, 2.0f);
                AddAreaIfNonZero(365, 970, 80, 120, left_thigh_ft_bs, 1.8f); // 앞 허벅지
                AddAreaIfNonZero(330, 900, 50, 180, left_thigh_inside_bs, 1.8f); // 앞 허벅지
                AddAreaIfNonZero(400, 1450, 110, 300, left_shin_bs, 1.8f); // 앞 정강이
                AddAreaIfNonZero(660, 1030, 60, 160, left_thigh_bk_bs, 1.8f); // 뒷 허벅지
                AddAreaIfNonZero(650, 850, 85, 90, left_butt_bs, 1.8f); // 뒷 엉덩이
                AddAreaIfNonZero(670, 1420, 95, 140, left_calf_bs, 1.8f); // 뒷 종아리 강조

            // 오른쪽 강조
                AddAreaIfNonZero(300, 90, 35, 80, right_neck_bs, 1.8f);
                AddAreaIfNonZero(370, 520, 145, 160, right_ribs_bs, 2.0f);
                AddAreaIfNonZero(145, 970, 80, 120, right_thigh_ft_bs, 1.8f); // 앞 허벅지
                AddAreaIfNonZero(180, 900, 50, 180, right_thigh_inside_bs, 1.8f); // 앞 허벅지 안쪽
                AddAreaIfNonZero(120, 1450, 110, 300, right_shin_bs, 1.8f); // 앞 정강이
                AddAreaIfNonZero(900, 1030, 60, 160, right_thigh_bk_bs, 1.8f); // 뒷 허벅지
                AddAreaIfNonZero(920, 850, 85, 90, right_butt_bs, 1.8f); // 뒷 엉덩이
                AddAreaIfNonZero(890, 1420, 95, 140, right_calf_bs, 1.8f); // 뒷 종아리 강조

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

                    // RenderTexture 초기화 및 재사용
                    if (realHumanData._body_rt == null || realHumanData._body_rt.width != w || realHumanData._body_rt.height != h)
                    {
                        if (realHumanData._body_rt != null) realHumanData._body_rt.Release();
                        realHumanData._body_rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                        realHumanData._body_rt.enableRandomWrite = true;
                        realHumanData._body_rt.Create();
                    }

                    // 영역 데이터가 변경된 경우만 업데이트
                    if (realHumanData.areas.Count > 0 && realHumanData.body_areaBuffer != null)
                    {
                        realHumanData.body_areaBuffer.SetData(realHumanData.areas.ToArray());
                        // 셰이더 파라미터 설정
                        RealHumanSupport._self._mergeComputeShader.SetInt("Width", w);
                        RealHumanSupport._self._mergeComputeShader.SetInt("Height", h);
                        RealHumanSupport._self._mergeComputeShader.SetInt("AreaCount", realHumanData.areas.Count);
                        RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexA", origin_texture);
                        RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexB", RealHumanSupport._self._bodyStrongFemaleBumpMap2);
                        RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "Result", realHumanData._body_rt);
                        RealHumanSupport._self._mergeComputeShader.SetBuffer(kernel, "Areas", realHumanData.body_areaBuffer);

                        // Dispatch 실행
                        RealHumanSupport._self._mergeComputeShader.Dispatch(kernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);                 
                        
                        realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_type, realHumanData._body_rt);

                        // Texture2D merged =  MergeRGBAlphaMaps(origin_texture, strong_texture, areas);    
                        // realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_type, merged);
                        // SaveAsPNG(merged, "./body_merge.png");
                        // SaveAsPNG(strong_texture, "./body_strong.png");
                        // SaveAsPNG(RenderTextureToTexture2D(RealHumanSupport._self._body_rt), "./body_merged.png");                     
                    }
                }
            }
            else
            {
                realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_type, realHumanData.bodyOriginTexture);
            }
        }
        internal static void SupportFaceBumpEffect(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            if (chaCtrl.sex == 0)
                return;

            if (realHumanData.m_skin_head == null)
                return;

            if (RealHumanSupport.BodyBlendingActive.Value)
            {
                Texture2D origin_texture = realHumanData.headOriginTexture;
                Texture2D express_texture = null;

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

                        // RenderTexture 초기화 및 재사용
                        if (realHumanData._head_rt == null || realHumanData._head_rt.width != w || realHumanData._head_rt.height != h)
                        {
                            if (realHumanData._head_rt != null) realHumanData._head_rt.Release();
                            realHumanData._head_rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                            realHumanData._head_rt.enableRandomWrite = true;
                            realHumanData._head_rt.Create();
                        }        

                    // 영역 데이터가 변경된 경우만 업데이트
                        if (areas.Count > 0 && realHumanData.head_areaBuffer != null)
                        {
                            realHumanData.head_areaBuffer.SetData(areas.ToArray());
                            // 셰이더 파라미터 설정
                            RealHumanSupport._self._mergeComputeShader.SetInt("Width", w);
                            RealHumanSupport._self._mergeComputeShader.SetInt("Height", h);
                            RealHumanSupport._self._mergeComputeShader.SetInt("AreaCount", areas.Count);
                            RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexA", origin_texture);
                            RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexB", express_texture);
                            RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "Result", realHumanData._head_rt);
                            RealHumanSupport._self._mergeComputeShader.SetBuffer(kernel, "Areas", realHumanData.head_areaBuffer);

                            // Dispatch 실행
                            RealHumanSupport._self._mergeComputeShader.Dispatch(kernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);

                            // 결과를 바로 Material에 적용 (CPU로 복사 안 함)    
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

        internal static Texture2D RenderTextureToTexture2D(RenderTexture rt)
        {
            if (rt == null) return null;

            // 현재 활성화된 RenderTexture 저장
            RenderTexture prev = RenderTexture.active;

            // RenderTexture 활성화
            RenderTexture.active = rt;

            // Texture2D 생성 (포맷 ARGB32 권장)
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);

            // 픽셀 복사
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            // RenderTexture 원래대로 복원
            RenderTexture.active = prev;

            return tex;
        }

        // internal static void SaveAsPNG(Texture tex, string path)
        // {
        //     if (tex == null)
        //     {
        //         UnityEngine.Debug.LogError("Texture is null");
        //         return;
        //     }

        //     Texture2D tex2D = null;

        //     if (tex is Texture2D t2d)
        //     {
        //         tex2D = t2d;
        //     }
        //     else if (tex is RenderTexture rt)
        //     {
        //         tex2D = RenderTextureToTexture2D(rt);
        //     }
        //     else
        //     {
        //         UnityEngine.Debug.LogError($"Unsupported texture type: {tex.GetType()}");
        //         return;
        //     }

        //     byte[] bytes = tex2D.EncodeToPNG();
        //     File.WriteAllBytes(path, bytes);
        // }


        internal static Texture2D MakeWritableTexture(Texture texture)
        {
            if (texture == null)
                return null;

            int width = texture.width;
            int height = texture.height;

            RenderTexture rt = RenderTexture.GetTemporary(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32
            );

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            // Texture2D / RenderTexture 모두 대응
            Graphics.Blit(texture, rt);

            Texture2D tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            return tex;
        }

        internal static Texture2D CaptureMaterialOutput(Material mat, int width, int height)
        {
            RenderTexture rt = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32
            );

            Graphics.Blit(null, rt, mat);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            return tex;
        }

        internal static Texture2D SetTextureSize(Texture2D rexture, int width, int height)
        {
            int targetWidth = width;
            int targetHeight = height;

            if (rexture.width == targetWidth && rexture.height == targetHeight)
            {
                // 이미 맞는 사이즈
                return rexture;
            }

            // RenderTexture를 이용한 다운사이징
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 24, RenderTextureFormat.ARGB32);
            Graphics.Blit(rexture, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.ARGB32, false);
            resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            resized.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return resized;
        }        

        #region Private Methods

        internal static DynamicBoneCollider AddExtraDynamicBoneCollider(
            Transform target,
            DynamicBoneColliderBase.Direction direction,
            float radius,
            float height,
            Vector3 offset)
        {
            string pivotName = target.name + "_DBC_Pivot";
            string colliderName = target.name + "_ExtDBoneCollider";

            // ===============================
            // 0. 로컬 헬퍼
            // ===============================
            void RemoveCollider(GameObject go)
            {
                Collider col = go.GetComponent<Collider>();
                if (col != null)
                    UnityEngine.Object.Destroy(col);
            }

            void SetupDebugRenderer(GameObject go)
            {
                MeshRenderer r = go.GetComponent<MeshRenderer>();
                if (r != null)
                {
                    r.material = new Material(r.sharedMaterial);
                    r.material.color = new Color(0f, 1f, 0f, 0.25f);
                }

                go.layer = LayerMask.NameToLayer("Ignore Raycast");
            }

            // ===============================
            // 1. Pivot 생성 / 재사용
            // ===============================
            Transform pivotTf = target.Find(pivotName);
            if (pivotTf == null)
            {
                GameObject pivotObj = new GameObject(pivotName);
                pivotObj.transform.SetParent(target, true);
                pivotObj.transform.localPosition = Vector3.zero;
                pivotObj.transform.localRotation = Quaternion.identity;
                pivotObj.transform.localScale = Vector3.one;
                pivotTf = pivotObj.transform;
            }

            // ===============================
            // 2. Collider 생성 / 재사용
            // ===============================
            Transform colliderTf = pivotTf.Find(colliderName);
            DynamicBoneCollider dbc;

            if (colliderTf == null)
            {
                GameObject colliderObj = new GameObject(colliderName);
                colliderObj.transform.SetParent(pivotTf, true);
                colliderObj.transform.localPosition = Vector3.zero;
                colliderObj.transform.localRotation = Quaternion.identity;
                dbc = colliderObj.AddComponent<DynamicBoneCollider>();
                colliderTf = colliderObj.transform;
            }
            else
            {
                dbc = colliderTf.GetComponent<DynamicBoneCollider>();
                if (dbc == null)
                    dbc = colliderTf.gameObject.AddComponent<DynamicBoneCollider>();
            }

            // ===============================
            // 3. 값 갱신
            // ===============================
            dbc.m_Radius = radius;
            dbc.m_Height = height;
            dbc.m_Direction = direction;
            dbc.m_Bound = DynamicBoneColliderBase.Bound.Outside;
            dbc.m_Center = offset;

            // ===============================
            // 4. Debug Visualization
            // ===============================
            string debugName = target.name + "_DBC_DebugSphere";
            string capEndAName = target.name + "_DBC_CapEndA";
            string capEndBName = target.name + "_DBC_CapEndB";
            string capBodyName = target.name + "_DBC_CapBody";

            return dbc;
        }

        internal static void ApplyScaleToExtraDynamicBoneColliders(
            Transform parent,
            Vector3 targetScale)
        {
            if (parent == null) return;

            // 모든 하위 Transform 가져오기 (비활성 포함)
            Transform[] allChildren = parent.GetComponentsInChildren<Transform>(true);

            foreach (Transform tr in allChildren)
            {
                // 이름 규칙 체크
                if (!tr.name.EndsWith("_ExtDBoneCollider")) continue;

                // scale 적용
                tr.localScale = targetScale;
            }
        }

        internal void SetHairDown(bool force = false) {

            if (realHumanData != null && realHumanData.head_bone != null)
            {
                Vector3 worldGravity = Vector3.down * 0.015f;
                foreach (DynamicBone bone in realHumanData.hairDynamicBones)
                {
                    if (bone == null)
                        continue;

                    // Ground direction (world down) -> convert to local.
                    bone.m_Gravity = realHumanData.head_bone.InverseTransformDirection(worldGravity);
                    bone.m_Force = realHumanData.head_bone.InverseTransformDirection(worldGravity);
                    bone.m_Damping    = 0.13f;
                    bone.m_Stiffness  = 0.02f;
                    bone.m_Elasticity = 0.01f;

                }
            }        
        }

        internal void SetPregnancyRoundness(float roundNess) {
            if (realHumanData != null && realHumanData.pregnancyController != null)
                realHumanData.pregnancyController.infConfig.inflationRoundness += roundNess;        
        }        

#if FEATURE_TEARDROP_SUPPORT
        internal void SetTearDrops() {
            if (realHumanData != null)
            {
                string bone_prefix_str = "cf_";
                if (realHumanData.chaCtrl.sex == 0)
                    bone_prefix_str = "cm_";

                realHumanData.nose_wing_l_tr = realHumanData.chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_NoseWing_tx_L");
                realHumanData.nose_wing_r_tr = realHumanData.chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_NoseWing_tx_R");
                if (realHumanData.nose_wing_l_tr != null)
                {
                    realHumanData.noseBaseScale = new Vector3(1.0f, 1.0f, 1.0f);
                    realHumanData.noseScaleInitialized = true;
                }
            }
        }        

        internal void SetTearDropRate(float tearDropRate) {                    

            if (realHumanData != null)
                realHumanData.tearDropRate = tearDropRate;
        }          
#endif


#if FEATURE_FACE_BLENDSHAPE_SUPPORT || FEATURE_WINK_SUPPORT
        internal void SetFaceBlendShapes()
        {
            if (realHumanData != null)
            {
                foreach (var fbsTarget in realHumanData.chaCtrl.fbsCtrl.EyesCtrl.FBSTarget)
                {
                    SkinnedMeshRenderer srender = fbsTarget.GetSkinnedMeshRenderer();
                    var mesh = srender.sharedMesh;
                    if (mesh && mesh.blendShapeCount > 0)
                    {
                        for (int idx = 0; idx < mesh.blendShapeCount; idx++)
                        {
                            string name = mesh.GetBlendShapeName(idx);

                            if (name.Contains("_close"))
                            {
                                if (!name.Contains("_close_L") && !name.Contains("_close_R"))
                                {
                                    if (name.Contains("head."))
                                        realHumanData.eye_close_idx_in_head_of_eyectrl = idx;
                                    else if (name.Contains("namida."))
                                        realHumanData.eye_close_idx_in_namida_of_eyectrl = idx;
                                    else
                                        realHumanData.eye_close_idx_in_lash_of_eyectrl = idx;
                                }
                            }
                            else if (name.Contains("_wink_R"))
                            {
                                if (name.Contains("head."))
                                    realHumanData.eye_wink_idx_in_head_of_eyectrl = idx;
                                else if (name.Contains("namida."))
                                    realHumanData.eye_wink_idx_in_namida_of_eyectrl = idx;
                                else
                                    realHumanData.eye_wink_idx_in_lash_of_eyectrl = idx;
                            }
                        }
                    }
                }

                foreach (var fbsTarget in realHumanData.chaCtrl.fbsCtrl.MouthCtrl.FBSTarget)
                {
                    SkinnedMeshRenderer srender = fbsTarget.GetSkinnedMeshRenderer();
                    var mesh = srender.sharedMesh;
                    if (mesh && mesh.blendShapeCount > 0)
                    {
                        for (int idx = 0; idx < mesh.blendShapeCount; idx++)
                        {
                            string name = mesh.GetBlendShapeName(idx);

                            if (name.Contains("_close"))
                            {
                                if (!name.Contains("_close_L") && !name.Contains("_close_R"))
                                {
                                    if (name.Contains("head."))
                                        realHumanData.eye_close_idx_in_head_of_mouthctrl = idx;
                                    else if (name.Contains("namida."))
                                        realHumanData.eye_close_idx_in_namida_of_mouthctrl = idx;
                                }
                            }
                            else if (name.Contains("_wink_R"))
                            {
                                if (name.Contains("head."))
                                    realHumanData.eye_wink_idx_in_head_of_mouthctrl = idx;
                                else if (name.Contains("namida."))
                                    realHumanData.eye_wink_idx_in_namida_of_mouthctrl = idx;
                            }
                        }
                    }
                }
            }           
        }
#endif 


#if FEATURE_BODY_BLENDSHAPE_SUPPORT
        internal void SetBodyBlendShapes()
        {
            if (realHumanData != null)
            {
                SkinnedMeshRenderer[] bodyRenderers = realHumanData.chaCtrl.objBody.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (SkinnedMeshRenderer render in bodyRenderers.ToList())
                {
                    var mesh = render.sharedMesh;
                    for (int idx = 0; idx < mesh.blendShapeCount; idx++)
                    {
                        string name = mesh.GetBlendShapeName(idx);

                        if (name.Contains("Legs Pull BothSide"))
                        {
                            realHumanData.fulleg_idx_in_body = idx;
                        }
                        else if (name.Contains("open Buttcheeks1"))
                        {
                            realHumanData.buttchecks1_idx_in_body = idx;
                        }
                        else if (name.Contains("open Buttcheeks2"))
                        {
                            realHumanData.buttchecks2_idx_in_body = idx;
                        }
                        else if (name.Contains("GP.Anus Open Large"))
                        {
                            realHumanData.anus_open_idx_in_body = idx;
                        }
                        else if (name.Contains("GP.Vagina Open All Inside"))
                        {
                            realHumanData.vagina_open_inside_idx_in_body = idx;
                        }
                        else if (name.Contains("GP.Vagina Open All Outside"))
                        {
                            realHumanData.vagina_open_outside_idx_in_body = idx;
                        }
                        else if (name.Contains("RG.Thigh Left Bent"))
                        {
                            realHumanData.thigh_left_bent_idx_in_body = idx;
                        }
                        else if (name.Contains("RG.Thigh Right Bent"))
                        {
                            realHumanData.thigh_right_bent_idx_in_body = idx;
                        }
                        else if (name.Contains("RG.Pubis Left Bent"))
                        {
                            realHumanData.pubis_left_bent_idx_in_body = idx;
                        }
                        else if (name.Contains("RG.Pubis Right Bent"))
                        {
                            realHumanData.pubis_right_bent_idx_in_body = idx;
                        }

                        // UnityEngine.Debug.Log($">> blendShape {name}, {idx} in body"); 
                    }
                }
            }                       
        }
#endif

#if FEATURE_STRAPON_SUPPORT
        // 남성에게만 부여
        internal void SetRigidBodyOnDan()
        {
            // UnityEngine.Debug.Log($">> SetRigidBodyOnDan {realHumanData}");

            if (realHumanData != null)
            {
                string bone_prefix_str = "cf_";
                if (realHumanData.chaCtrl.sex == 0)
                    bone_prefix_str = "cm_";

                Transform danObject = realHumanData.chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str + "J_dan_top");

                if (danObject != null)
                {
                    string childName = "RGDanRigidBody";
                    Transform existingChild = danObject.Find(childName);

                    GameObject childObj;

                    if (existingChild == null)
                    {
                        childObj = new GameObject(childName);
                        childObj.transform.SetParent(danObject, false);
                        childObj.transform.localPosition = Vector3.zero;
                    }
                    else
                    {
                        childObj = existingChild.gameObject;
                    }

                    // Rigidbody
                    Rigidbody rb = childObj.GetComponent<Rigidbody>();
                    if (rb == null)
                    {
                        rb = childObj.AddComponent<Rigidbody>();
                        rb.isKinematic = true;
                        rb.useGravity = false;
                    }

                    // Collider (남자 충돌용)
                    CapsuleCollider capsule = childObj.GetComponent<CapsuleCollider>();
                    if (capsule == null)
                    {
                        capsule = childObj.AddComponent<CapsuleCollider>();
                        capsule.radius = 0.2f;      // 필요에 맞게 조절
                        capsule.height = 0.8f;
                        capsule.direction = 0;
                        capsule.center = Vector3.zero;
                        capsule.isTrigger = false;  // 실제 collider
                    }

                    // UnityEngine.Debug.Log($">> created rigidBody + collider on {bone_prefix_str}J_dan_top");
                }
                else
                {
                    // UnityEngine.Debug.Log(">> dan_top not found");
                }
            }
        }

        // 여성에게만 부여
        internal void SetCollisionOnOnKosi()
        {
            // UnityEngine.Debug.Log($">> SetCollisionOnOnKosi {realHumanData}");

            if (realHumanData != null && realHumanData.chaCtrl.sex == 1)
            {
                string bone_prefix_str = "cf_";
                string childName = "RGTriggerCollision";

                Transform kosiObject = realHumanData.chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str + "J_Kokan");

                if (kosiObject != null)
                {
                    Transform existingChild = kosiObject.Find(childName);
                    GameObject childObj;

                    if (existingChild == null)
                    {
                        childObj = new GameObject(childName);
                        childObj.transform.SetParent(kosiObject, false);
                        childObj.transform.localPosition = Vector3.zero;
                    }
                    else
                    {
                        childObj = existingChild.gameObject;
                    }

                    SphereCollider sphereCollider = childObj.GetComponent<SphereCollider>();
                    if (sphereCollider == null)
                    {
                        sphereCollider = childObj.AddComponent<SphereCollider>();
                        sphereCollider.isTrigger = true;
                        sphereCollider.radius = 0.3f;
                        sphereCollider.center = new Vector3(0f, 0.2f, 0f);
                    }

                    if (childObj.GetComponent<CapsuleTrigger>() == null)
                    {
                        childObj.AddComponent<CapsuleTrigger>();
                    }

                    // UnityEngine.Debug.Log(">> created sphere trigger on J_Kokan");
                }
                else
                {
                    // UnityEngine.Debug.Log(">> J_Kokan not found");
                }
            }
        }
#endif

        internal static PositionData GetBoneRotationFromTF(Transform t)
        {
            // 부모 기준 회전
            Quaternion localRot = t.localRotation;

            Vector3 localEuler = localRot.eulerAngles;

            // 0~360 → -180~180 변환
            float Normalize(float angle)
            {
                if (angle > 180f)
                    angle -= 360f;
                return angle;
            }

            float frontback = Normalize(localEuler.x);  // 앞(+)/뒤(-)
            float leftright = Normalize(localEuler.z);  // 좌(+)/우(-)

            PositionData data = new PositionData(
                t.rotation,     // 월드 회전은 그대로 유지
                frontback,
                leftright
            );

            return data;
        }

        internal static PositionData GetBoneRotationFromFK(OCIChar.BoneInfo info)
        {
            Transform t = info.guideObject.transform;
            Transform p = t.parent;

            Vector3 baseForward = p ? p.forward : Vector3.forward;
            Vector3 baseRight   = p ? p.right   : Vector3.right;
            Vector3 baseUp      = p ? p.up      : Vector3.up;

            Vector3 fwd = t.forward;

            // 앞 / 뒤
            Vector3 fwdPlane = Vector3.ProjectOnPlane(fwd, baseRight).normalized;
            float pitch = Vector3.SignedAngle(
                baseForward,
                fwdPlane,
                baseRight
            );

            // 좌 / 우
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

            // --- hip 기준 축 ---
            Vector3 baseForward = parent.forward;
            Vector3 baseRight   = parent.right;
            Vector3 baseUp      = parent.up;

            // --- thigh 방향 ---
            Vector3 childForward = child.forward;
            Vector3 childRight   = child.right;

            // =========================
            // 1️⃣ 앞 / 뒤 (Pitch)
            // hip.right 축을 기준으로 회전
            // =========================
            Vector3 pitchProjected =
                Vector3.ProjectOnPlane(childForward, baseRight).normalized;

            float pitch = Vector3.SignedAngle(
                baseForward,
                pitchProjected,
                baseRight
            );

            // =========================
            // 2️⃣ 좌 / 우 (Abduction)
            // hip.forward 축을 기준으로 회전
            // =========================
            Vector3 sideProjected =
                Vector3.ProjectOnPlane(childRight, baseForward).normalized;

            float side = Vector3.SignedAngle(
                baseRight,
                sideProjected,
                baseForward
            );

            return new PositionData(
                child.rotation,   // 기존 구조 유지
                pitch,            // 앞/뒤
                side              // 좌/우
            );
        }
        // internal static PositionData GetBoneRotationFromFK(OCIChar.BoneInfo info)
        // {
        //     Transform t = info.guideObject.transform;

        //     // 부모 기준 회전
        //     Quaternion localRot = t.localRotation;

        //     Vector3 localEuler = localRot.eulerAngles;

        //     // 0~360 → -180~180 변환
        //     float Normalize(float angle)
        //     {
        //         if (angle > 180f)
        //             angle -= 360f;
        //         return angle;
        //     }

        //     float frontback = Normalize(localEuler.x);  // 앞(+)/뒤(-)
        //     float leftright = Normalize(localEuler.z);  // 좌(+)/우(-)

        //     PositionData data = new PositionData(
        //         t.rotation,     // 월드 회전은 그대로 유지
        //         frontback,
        //         leftright
        //     );

        //     return data;
        // }

        internal static Texture2D ConvertToTexture2D(RenderTexture renderTex)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTex;

            Texture2D tex = new Texture2D(renderTex.width, renderTex.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
            tex.Apply();

            RenderTexture.active = previous;

            return tex;
        }

        internal static RealHumanData AllocateBumpMap(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            // Cache original bump textures only once; reuse on re-init to avoid cumulative blending.
            if (realHumanData.headOriginTexture == null)
            {
                Texture2D headOriginTexture = null;
                if (realHumanData.m_skin_head.GetTexture(realHumanData.head_bumpmap_type) as Texture2D == null)
                    headOriginTexture = ConvertToTexture2D(realHumanData.m_skin_head.GetTexture(realHumanData.head_bumpmap_type) as RenderTexture);
                else
                    headOriginTexture = realHumanData.m_skin_head.GetTexture(realHumanData.head_bumpmap_type) as Texture2D;

                realHumanData.headOriginTexture = SetTextureSize(
                    MakeWritableTexture(headOriginTexture),
                    RealHumanSupport._self._faceExpressionFemaleBumpMap2.width,
                    RealHumanSupport._self._faceExpressionFemaleBumpMap2.height);
            }

            if (realHumanData.bodyOriginTexture == null)
            {
                Texture2D bodyOriginTexture = null;
                if (realHumanData.m_skin_body.GetTexture(realHumanData.body_bumpmap_type) as Texture2D == null)
                    bodyOriginTexture = ConvertToTexture2D(realHumanData.m_skin_body.GetTexture(realHumanData.body_bumpmap_type) as RenderTexture);
                else
                    bodyOriginTexture = realHumanData.m_skin_body.GetTexture(realHumanData.body_bumpmap_type) as Texture2D;

                realHumanData.bodyOriginTexture = SetTextureSize(
                    MakeWritableTexture(bodyOriginTexture),
                    RealHumanSupport._self._bodyStrongFemaleBumpMap2.width,
                    RealHumanSupport._self._bodyStrongFemaleBumpMap2.height);
            }

            return realHumanData;
        }

        internal static RealHumanData GetMaterials(ChaControl charCtrl, RealHumanData realHumanData)
        {
            SkinnedMeshRenderer[] bodyRenderers = charCtrl.objBody.GetComponentsInChildren<SkinnedMeshRenderer>();
            // UnityEngine.Debug.Log($">> bodyRenderers {bodyRenderers.Length}");

            foreach (SkinnedMeshRenderer render in bodyRenderers.ToList())
            {

                foreach (var mat in render.sharedMaterials)
                {
                    string name = mat.name.ToLower();

                    if (name.Contains("_m_skin_body"))
                    {
                        realHumanData.m_skin_body = mat;
                        // Texture mainTex = realHumanData.m_skin_body.mainTexture;
                        // UnityEngine.Debug.Log($">> mainTex.isReadable {mainTex.isReadable}");
                        // SaveAsPNG(CaptureMaterialOutput(realHumanData.m_skin_body, 4096, 4096), "c:/Temp/body_mainTex.png");
                    }
                }
            }
            

            SkinnedMeshRenderer[] headRenderers = charCtrl.objHead.GetComponentsInChildren<SkinnedMeshRenderer>();
            // UnityEngine.Debug.Log($">> headRenderers {headRenderers.Length}");

            foreach (SkinnedMeshRenderer render in headRenderers.ToList())
            {
                foreach (var mat in render.sharedMaterials)
                {
                    string name = mat.name.ToLower();

                    if (name.Contains("_m_skin_head"))
                    {
                        realHumanData.m_skin_head = mat;
                        // Texture mainTex = realHumanData.m_skin_head.mainTexture;
                        // UnityEngine.Debug.Log($">> mainTex.isReadable {mainTex.isReadable}");
                        // SaveAsPNG(CaptureMaterialOutput(realHumanData.m_skin_head, 2048, 2048), "c:/Temp/face_mainTex.png");
                    }
#if FEATURE_TEARDROP_SUPPORT
                    else if (name.Contains("c_m_eye_01") || name.Contains("c_m_eye_02"))
                    {
                        realHumanData.c_m_eye.Add(mat);
                    }
                    else if (name.Contains("c_m_eye_namida")) {
                        realHumanData.m_tear_eye = mat;
                        realHumanData.m_tear_eye.SetTexture("_MainTex", RealHumanSupport._self._TearDropImg);
                    }
#endif
                }
            }
            return realHumanData;
        }

        internal static BAreaData InitBArea(float x, float y, float radiusX, float radiusY, float bumpBooster=0.3f)
        {
            return new BAreaData
            {
                X = x,
                Y = y,
                RadiusX = radiusX,
                RadiusY = radiusY,
                BumpBooster = bumpBooster
            };
        }
        
        internal RealHumanData InitRealHumanData(ChaControl chaCtrl)
        {
            
            if (realHumanData == null)
            {
                realHumanData = new RealHumanData();
            }

            realHumanData.chaCtrl = chaCtrl;
            return realHumanData;
        }

        internal void UpdateRealHumanData()
        {
            // UnityEngine.Debug.Log($">> UpdateRealHumanData {realHumanData.chaCtrl.objClothes.Length}");
            if (realHumanData.chaCtrl != null && realHumanData.chaCtrl.sex == 1)
            {
                if (realHumanData.extraBodyColliders != null)
                    realHumanData.extraBodyColliders.Clear();

                if (realHumanData.coroutine != null)
                {
                    realHumanData.chaCtrl.StopCoroutine(realHumanData.coroutine);
                    realHumanData.coroutine = null;
                }
                if (realHumanData.head_areaBuffer != null)
                {
                    realHumanData.head_areaBuffer.Release();
                    realHumanData.head_areaBuffer = null;
                }
                if (realHumanData.body_areaBuffer != null)
                {
                    realHumanData.body_areaBuffer.Release();
                    realHumanData.body_areaBuffer = null;
                }
                status = Status.INIT;
                realHumanData.head_areaBuffer = new ComputeBuffer(20, sizeof(float) * 6);
                realHumanData.body_areaBuffer = new ComputeBuffer(30, sizeof(float) * 6);

#if FEATURE_TEARDROP_SUPPORT
                realHumanData.tearDropRate = realHumanData.TearDropLevel;
#endif
                realHumanData.c_m_eye.Clear();

                realHumanData = GetMaterials(realHumanData.chaCtrl, realHumanData);

                realHumanData.pregnancyController = realHumanData.chaCtrl.GetComponent<KK_PregnancyPlus.PregnancyPlusCharaController>();

                if (realHumanData.m_skin_body != null && realHumanData.m_skin_body.GetTexture("_BumpMap2") != null)
                {
                    realHumanData.body_bumpmap_type = "_BumpMap2";
                    realHumanData.m_skin_body.SetFloat("_BumpScale2", 0.80f);
                } 
                else if (realHumanData.m_skin_body != null && realHumanData.m_skin_body.GetTexture("_BumpMap") != null)
                {
                    realHumanData.body_bumpmap_type = "_BumpMap";
                }
                else
                {
                    realHumanData.body_bumpmap_type = "";
                }
                
                if (realHumanData.m_skin_head != null && realHumanData.m_skin_head.GetTexture("_BumpMap2") != null)
                {
                    realHumanData.head_bumpmap_type = "_BumpMap2";
                    realHumanData.m_skin_head.SetFloat("_BumpScale2", 0.80f);
                }
                else if (realHumanData.m_skin_head != null && realHumanData.m_skin_head.GetTexture("_BumpMap") != null)
                {
                    realHumanData.head_bumpmap_type = "_BumpMap";
                }
                else
                {
                    realHumanData.head_bumpmap_type = "";
                }

                if (!realHumanData.body_bumpmap_type.Contains("_BumpMap2"))
                    return;
                else
                {
                    realHumanData.hairDynamicBones.Clear();
                    
                    string bone_prefix_str = "cf_";
                    if (realHumanData.chaCtrl.sex == 0)
                        bone_prefix_str = "cm_";

                    realHumanData.root_bone = realHumanData.chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Root");
                    realHumanData.head_bone = realHumanData.chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Head");
                    realHumanData.neck_bone = realHumanData.chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Neck");      

                    if (realHumanData.head_bone)
                    {
                        DynamicBone[] hairbones = realHumanData.head_bone.GetComponentsInChildren<DynamicBone>(true);  
                        realHumanData.hairDynamicBones = hairbones.ToList();
                    }

                    if (StudioAPI.InsideStudio) {
                        OCIChar ociChar = realHumanData.chaCtrl.GetOCIChar();

                        realHumanData = AllocateBumpMap(realHumanData.chaCtrl, realHumanData);
                        
                        foreach (OCIChar.BoneInfo bone in ociChar.listBones)
                        {
                            if (bone.guideObject != null && bone.guideObject.transformTarget != null) {

                                if (bone.guideObject.transformTarget.name.Contains("_J_Hips"))
                                {
                                    realHumanData.fk_hip_bone = bone; // 하단
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_Spine01"))
                                {
                                    realHumanData.fk_spine01_bone = bone; // 하단
                                }
                                else if(bone.guideObject.transformTarget.name.Contains("_J_Spine02"))
                                {
                                    realHumanData.fk_spine02_bone = bone; // 상단
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_Shoulder_L"))
                                {
                                    realHumanData.fk_left_shoulder_bone = bone;
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_Shoulder_R"))
                                {
                                    realHumanData.fk_right_shoulder_bone = bone;
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_ArmUp00_L"))
                                {
                                    realHumanData.fk_left_armup_bone = bone;
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_ArmUp00_R"))
                                {
                                    realHumanData.fk_right_armup_bone = bone;
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_ArmLow01_L"))
                                {
                                    realHumanData.fk_left_armdown_bone = bone;
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_ArmLow01_R"))
                                {
                                    realHumanData.fk_right_armdown_bone = bone;
                                }                                                                        
                                else if (bone.guideObject.transformTarget.name.Contains("_J_LegUp00_L"))
                                {
                                    realHumanData.fk_left_thigh_bone = bone;
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_LegUp00_R"))
                                {
                                    realHumanData.fk_right_thigh_bone = bone;
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_LegLow01_L"))
                                {
                                    realHumanData.fk_left_knee_bone = bone;
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_LegLow01_R"))
                                {
                                    realHumanData.fk_right_knee_bone = bone;
                                }                        
                                else if (bone.guideObject.transformTarget.name.Contains("_J_Head"))
                                {
                                    realHumanData.fk_head_bone = bone;
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_Neck"))
                                {
                                    realHumanData.fk_neck_bone = bone;
                                }
                                else if (bone.guideObject.transformTarget.name.Contains("_J_Foot01_L"))
                                {
                                    realHumanData.fk_left_foot_bone = bone;
                                }   
                                else if (bone.guideObject.transformTarget.name.Contains("_J_Foot01_R"))
                                {
                                    realHumanData.fk_right_foot_bone = bone;
                                }
                            }
                        }
                    }
                }   

                SupportExtraDynamicBones(realHumanData.chaCtrl, realHumanData);
                SupportBlendShapes(realHumanData.chaCtrl, realHumanData);
#if FEATURE_TEARDROP_SUPPORT
                SupportTearDrop(realHumanData.chaCtrl, realHumanData);
#endif
#if FEATURE_STRAPON_SUPPORT
                SupportStrapOn(realHumanData.chaCtrl, realHumanData);
#endif
                SupportEyeFastBlinkEffect(realHumanData.chaCtrl, realHumanData);
                SupportBodyBumpEffect(realHumanData.chaCtrl, realHumanData);
                SupportFaceBumpEffect(realHumanData.chaCtrl, realHumanData);                

                status = Status.RUN;
                if (realHumanData.coroutine == null)
                    realHumanData.coroutine = realHumanData.chaCtrl.StartCoroutine(CoroutineProcess(realHumanData));
            }
        }

        internal IEnumerator ExecuteRealHumanDelayed()
        {
            int frameCount = 10;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            UpdateRealHumanData();
        }     

        internal void ExecuteRealHumanEffect(ChaControl chaControl)
        {
            // UnityEngine.Debug.Log($">> ExecuteWindEffect {chaControl}");

            if (chaControl != null) {
                realHumanData = InitRealHumanData(chaControl);
                chaControl.StartCoroutine(ExecuteRealHumanDelayed());
            }            
        }
        internal static float Remap(float value, float inMin, float inMax, float outMin, float outMax)
        {
            return outMin + (value - inMin) * (outMax - outMin) / (inMax - inMin);
        }

        internal static float GetRelativePosition(float a, float b)
        {
            bool sameSign = (a >= 0 && b >= 0) || (a < 0 && b < 0);

            if (sameSign)
                return Math.Abs(Math.Abs(a) - Math.Abs(b)); // 동일 부호: 절댓값 빼기
            else
                return Math.Abs(Math.Abs(a) + Math.Abs(b)); // 부호 다름: 절댓값 더하기
        }

        internal static bool IsFront(float a, float b)
        {
            if (a > b) 
                return true;
            else 
                return false;
        }

        internal static bool IsWide(float a, float b)
        {
            if (a > b) 
                return true;
            else 
                return false;
        }

        internal void SupportBlendShapes(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            if (chaCtrl.sex == 0)
                return;
#if FEATURE_FACE_BLENDSHAPE_SUPPORT || FEATURE_WINK_SUPPORT
                SetFaceBlendShapes();
#endif
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
                SetBodyBlendShapes();
#endif
        }

#if FEATURE_TEARDROP_SUPPORT
        internal void SupportTearDrop(ChaControl chaCtrl, RealHumanData realHumanData) 
        {
            if (chaCtrl.sex == 0)
                return;
            SetTearDrops();
        }
#endif

#if FEATURE_STRAPON_SUPPORT
        internal void SupportStrapOn(ChaControl chaCtrl, RealHumanData realHumanData) 
        {
            SetRigidBodyOnDan();
            SetCollisionOnOnKosi();
        }
#endif

        internal static void SupportEyeFastBlinkEffect(ChaControl chaCtrl, RealHumanData realHumanData) 
        {
            if (chaCtrl.fbsCtrl != null)
                chaCtrl.fbsCtrl.BlinkCtrl.BaseSpeed = 0.05f; // 작을수록 blink 속도가 높아짐..
        }

       internal IEnumerator CoroutineProcess(RealHumanData realHumanData)
       {
           float tearValue = 0;
           float noseValue = 0;
           bool tearIncreasing = true;
           bool noseIncreasing = true;
           
           float previosBellySize = 0.0f;
           float initBellySize = 0.0f;

           if (realHumanData.pregnancyController != null)
               initBellySize = realHumanData.pregnancyController.infConfig.inflationSize;

           while (true)
           {
                if (status == Status.RUN) // play
                {
                   float time = Time.time;
                   if (RealHumanSupport.EyeShakeActive.Value)
                   {
                       foreach (Material mat in realHumanData.c_m_eye)
                       {
                           if (mat == null)
                               continue;
                           // sin 파형 (0 ~ 1로 정규화)
                           float easedBump = (Mathf.Sin(time * Mathf.PI * 3.5f * 2f) + 1f) * 0.5f;

                           float eyeScale = Mathf.Lerp(0.18f, 0.21f, easedBump);
                           mat.SetFloat("_Texture4Rotator", eyeScale);

                           eyeScale = Mathf.Lerp(0.1f, 0.2f, easedBump);
                           mat.SetFloat("_Parallax", eyeScale);
                       }
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
                               float sinValue = (Mathf.Sin(time * realHumanData.BreathInterval) + 1f) * 0.5f;
                     
                               realHumanData.pregnancyController.infConfig.inflationSize = initBellySize + (1f - sinValue) * 10f * realHumanData.BreathStrong;
                               realHumanData.pregnancyController.MeshInflate(new MeshInflateFlags(realHumanData.pregnancyController), "StudioSlider");
                               previosBellySize = realHumanData.pregnancyController.infConfig.inflationSize;
                           }
                       }
                   }

#if FEATURE_TEARDROP_SUPPORT
                   if (RealHumanSupport.TearDropActive.Value)
                   {
                       float deltaTear = Time.deltaTime / 10f; // ← (10초)

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

                       //  ---------------- 눈물 생성 ----------------
                       if (realHumanData.m_tear_eye != null) {
                           realHumanData.m_tear_eye.SetFloat("_NamidaScale", realHumanData.tearDropRate);
                           realHumanData.m_tear_eye.SetFloat("_RefractionScale", tearSin); 
                       }

                       float deltaNose = Time.deltaTime / 1.5f; // ← (1.5초)

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
                       // ----------------- 코평수 처리 -----------------
                       if (realHumanData.nose_wing_l_tr != null) {

                           float noseScaleFactor = 1f + (noseSin * 0.3f);
                           Vector3 scalel = realHumanData.noseBaseScale;
                           Vector3 scaler = realHumanData.noseBaseScale;

                           scalel.x = realHumanData.noseBaseScale.x * noseScaleFactor;
                           scaler.x = realHumanData.noseBaseScale.x * noseScaleFactor;

                           realHumanData.nose_wing_l_tr.localScale = scalel;
                           realHumanData.nose_wing_r_tr.localScale = scaler;
                       }
                   } else
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
                   yield return null;
               }
               else
               {
                   yield return new WaitForSeconds(1);
               }
           }
       }    

#endregion
    }

    // enum BODY_SHADER
    // {
    //     HANMAN,
    //     DEFAULT
    // }
    
    class RealFaceData
    {
        public List<BAreaData> areas = new List<BAreaData>();

        public RealFaceData()
        {
        }
        public RealFaceData(BAreaData barea)
        {
           this.areas.Add(barea);
        }

        public RealFaceData(BAreaData barea1, BAreaData barea2)
        {
            this.areas.Add(barea1);
            this.areas.Add(barea2);
        }

        public RealFaceData(BAreaData barea1, BAreaData barea2, BAreaData barea3)
        {
            this.areas.Add(barea1);
            this.areas.Add(barea2);
            this.areas.Add(barea3);
        }

        public RealFaceData(BAreaData barea1, BAreaData barea2, BAreaData barea3, BAreaData barea4)
        {
            this.areas.Add(barea1);
            this.areas.Add(barea2);
            this.areas.Add(barea3);
            this.areas.Add(barea4);
        }

        public void Add(BAreaData area)
        {
            this.areas.Add(area);
        }
    }

    // 24 byte 크기를 맞추기 위해 padding 추가
    struct BAreaData
    {  
        public float X { get; set; }
        public float Y { get; set; }
        public float RadiusX; // 가로 반지름
        public float RadiusY; // 세로 반지름
        public float BumpBooster; // 범프 세기 강조
        public float Padding;   // ← 추가
    }    
    
    class PositionData
    {
        public Quaternion _q;
        public  float   _frontback;
        public  float   _leftright;

        public PositionData(Quaternion q, float frontback, float leftright)
        {   
            _q = q;
            _frontback = frontback;
            _leftright = leftright;
        }        
    }

    class RealHumanData
    {
        public ChaControl chaCtrl;
        public Coroutine coroutine;

        // 코루틴 제어
        public bool coroutine_pause;

        public List<BAreaData> areas = new List<BAreaData>();

        // hair down 제어
        public Transform head_bone;
        public Transform neck_bone;
        public Transform root_bone;  // hair down 지원인데, 확인 필요..
        public List<DynamicBone> hairDynamicBones = new List<DynamicBone>(); // hair down 지원인데, 확인 필요..
        public List<DynamicBoneCollider> extraBodyColliders = new List<DynamicBoneCollider>();

        // 가슴/엉덩이에 gravity 제어
        public DynamicBone_Ver02 rightBoob;
        public DynamicBone_Ver02 leftBoob;
        public DynamicBone_Ver02 rightButtCheek;
        public DynamicBone_Ver02 leftButtCheek;

        // Bumpmap 및 관련 속성 제어
        public Material m_tear_eye;
        public Material m_skin_head;
        public Material m_skin_body;

        public Texture2D headOriginTexture;
        public Texture2D bodyOriginTexture;

        public RenderTexture _head_rt;
        public ComputeBuffer head_areaBuffer;

        public RenderTexture _body_rt;
        public ComputeBuffer body_areaBuffer;

        // bumpmap 제어 임시 저장값
        public string head_bumpmap_type;
        public string body_bumpmap_type;

        // bumpmap 지원 pos 변화량 체크
        public Quaternion  prev_fk_spine01_rot;
        public Quaternion  prev_fk_spine02_rot;
        public Quaternion  prev_fk_head_rot;
        public Quaternion  prev_fk_neck_rot;
        public Quaternion  prev_fk_right_shoulder_rot;
        public Quaternion  prev_fk_left_shoulder_rot;
        public Quaternion  prev_fk_right_armup_rot;
        public Quaternion  prev_fk_left_armup_rot;
        public Quaternion  prev_fk_right_armdown_rot;
        public Quaternion  prev_fk_left_armdown_rot;        
        public Quaternion  prev_fk_right_thigh_rot;
        public Quaternion  prev_fk_left_thigh_rot;
        public Quaternion  prev_fk_right_knee_rot;        
        public Quaternion  prev_fk_left_knee_rot;        
        public Quaternion  prev_fk_right_foot_rot;
        public Quaternion  prev_fk_left_foot_rot;

        public OCIChar.BoneInfo fk_hip_bone;
        public OCIChar.BoneInfo fk_spine01_bone;
        public OCIChar.BoneInfo fk_spine02_bone;
        public OCIChar.BoneInfo fk_head_bone;
        public OCIChar.BoneInfo fk_neck_bone;
        public OCIChar.BoneInfo fk_left_shoulder_bone;
        public OCIChar.BoneInfo fk_right_shoulder_bone;
        public OCIChar.BoneInfo fk_left_armup_bone;
        public OCIChar.BoneInfo fk_right_armup_bone;
        public OCIChar.BoneInfo fk_left_armdown_bone;
        public OCIChar.BoneInfo fk_right_armdown_bone;
        public OCIChar.BoneInfo fk_left_thigh_bone;
        public OCIChar.BoneInfo fk_right_thigh_bone;
        public OCIChar.BoneInfo fk_left_knee_bone;
        public OCIChar.BoneInfo fk_right_knee_bone;
        public OCIChar.BoneInfo  fk_right_foot_bone;
        public OCIChar.BoneInfo  fk_left_foot_bone;

        // Belly 효과
        public PregnancyPlusCharaController pregnancyController;

        // 눈물 효과
        public List<Material> c_m_eye = new List<Material>();
#if FEATURE_TEARDROP_SUPPORT
        public Transform nose_wing_l_tr;
        public Transform nose_wing_r_tr;
        public Vector3 noseBaseScale;
        public bool noseScaleInitialized = false;     

        public float tearDropRate;   
#endif

#if FEATURE_FACE_BLENDSHAPE_SUPPORT || FEATURE_WINK_SUPPORT
        public int eye_close_idx_in_head_of_eyectrl;
        public int eye_close_idx_in_namida_of_eyectrl;
        public int eye_close_idx_in_lash_of_eyectrl;

        public int eye_wink_idx_in_head_of_eyectrl;
        public int eye_wink_idx_in_namida_of_eyectrl;
        public int eye_wink_idx_in_lash_of_eyectrl;

        public int eye_close_idx_in_head_of_mouthctrl;
        public int eye_close_idx_in_namida_of_mouthctrl;

        public int eye_wink_idx_in_head_of_mouthctrl;
        public int eye_wink_idx_in_namida_of_mouthctrl;

        public int originMouthType;        
#endif

#if FEATURE_BODY_BLENDSHAPE_SUPPORT
        public int fulleg_idx_in_body;
        public int buttchecks1_idx_in_body;
        public int buttchecks2_idx_in_body;
        public int anus_open_idx_in_body;
        public int vagina_open_inside_idx_in_body;
        public int vagina_open_outside_idx_in_body;

        public int thigh_left_bent_idx_in_body;
        public int thigh_right_bent_idx_in_body;

        public int pubis_left_bent_idx_in_body;
        public int pubis_right_bent_idx_in_body;
#endif

        public float TearDropLevel = 0.3f;
        public float BreathInterval = 1.5f;
        public float BreathStrong = 0.45f;

        public RealHumanData()
        {
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
            fulleg_idx_in_body = -1;
            buttchecks1_idx_in_body = -1;
            buttchecks2_idx_in_body = -1;
            anus_open_idx_in_body = -1;
            vagina_open_inside_idx_in_body = -1;
            vagina_open_outside_idx_in_body = -1;

            // fixed
            thigh_left_bent_idx_in_body = -1;
            thigh_right_bent_idx_in_body = -1;
            pubis_left_bent_idx_in_body = -1;
            pubis_right_bent_idx_in_body = -1;
#endif
        }     
    }
    enum Status
    {
        INIT,
        PAUSE,
        RUN
    }
#if FEATURE_STRAPON_SUPPORT
    public class CapsuleTrigger : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            if (other.attachedRigidbody == null)
                return;

            if (other.attachedRigidbody.name != "RGDanRigidBody")
                return;

            UnityEngine.Debug.Log("Trigger Enter: " + other.name);
        }

        void OnTriggerStay(Collider other)
        {
            if (other.attachedRigidbody == null)
                return;

            // if (other.attachedRigidbody.name != "RGDanRigidBody")
            //     return;

            UnityEngine.Debug.Log("Trigger Stay: " + other.name);
        }

        void OnTriggerExit(Collider other)
        {
            UnityEngine.Debug.Log("Trigger Exit: " + other.name);
        }
    }
#endif
}
