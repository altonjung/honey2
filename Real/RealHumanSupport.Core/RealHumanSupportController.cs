// Runtime controller logic for real-human effects, collider setup, and FK-driven updates.
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
using JointCorrectionSlider;

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

/*  
    - seperate features in main-game or studio
*/

namespace RealHumanSupport
{
    public partial class RealHumanSupportController: CharaCustomFunctionController
    {
        internal RealHumanData realHumanData;
        internal Status status;// 0: init, 1: pause, 2: play

        private Coroutine _oneShotESCoroutine;

        private Coroutine _oneShotCoroutine;

        protected override void OnCardBeingSaved(GameMode currentGameMode) { }

        private static void ReleaseRenderTexture(ref RenderTexture renderTexture)
        {
            if (renderTexture == null)
                return;

            renderTexture.Release();
            UnityEngine.Object.Destroy(renderTexture);
            renderTexture = null;
        }

        private static void ReleaseComputeBuffer(ref ComputeBuffer computeBuffer)
        {
            if (computeBuffer == null)
                return;

            computeBuffer.Release();
            computeBuffer = null;
        }

        private void ReleaseGpuResources()
        {
            if (realHumanData == null)
                return;

            ReleaseComputeBuffer(ref realHumanData.head_areaBuffer);
            ReleaseComputeBuffer(ref realHumanData.body_areaBuffer);

            ReleaseRenderTexture(ref realHumanData._head_rt);
            ReleaseRenderTexture(ref realHumanData._body_rt);

            if (realHumanData.headOriginBumpMap2Texture is RenderTexture headOriginRenderTexture)
            {
                headOriginRenderTexture.Release();
                UnityEngine.Object.Destroy(headOriginRenderTexture);
            }
            realHumanData.headOriginBumpMap2Texture = null;

            if (realHumanData.bodyOriginBumpMap2Texture is RenderTexture bodyOriginRenderTexture)
            {
                bodyOriginRenderTexture.Release();
                UnityEngine.Object.Destroy(bodyOriginRenderTexture);
            }
            realHumanData.bodyOriginBumpMap2Texture = null;
        }

        private void OnDisable()
        {
            ReleaseGpuResources();
        }

        private void OnDestroy()
        {
            ReleaseGpuResources();
        }


        internal RealHumanData GetRealHumanData()
        {
            return realHumanData;
        }

        internal void ResetRealHumanData()
        {
            SetTearDropActive(false);
            realHumanData.BreathActive = false;
            realHumanData.EyeShakeActive = true;
            realHumanData.BodyBumpMapActive = true;
            realHumanData.RealPlayActive = false;            
            realHumanData.TearDropLevel = 0.3f;
            realHumanData.BreathInterval = 1.5f;
            realHumanData.BreathStrong = 0.45f;
            realHumanData.RealPlayStrong = 1.0f;
            realHumanData.RealPlayBlendShapeTarget = 0f;
            realHumanData.RealPlayOscillationPercent = 0.10f;
        }

        internal void UpdateRealHumanData()
        {
            // UnityEngine.Debug.Log($">> UpdateRealHumanData {realHumanData.chaCtrl.objClothes.Length}");
            if (realHumanData.chaCtrl != null)
            {
                string bone_prefix_str = "cf_";
                if (realHumanData.chaCtrl.sex == 0)
                    bone_prefix_str = "cm_";

                realHumanData.root_bone = realHumanData.chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Root");
                realHumanData.head_bone = realHumanData.chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Head");
                realHumanData.neck_bone = realHumanData.chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Neck");                
                realHumanData.dan_bone = realHumanData.chaCtrl.objAnim.transform.FindLoop("cm_J_dan119_00");
                status = Status.INIT;

                if (realHumanData.chaCtrl.sex == 1) {

                    realHumanData.jointCorrectionSliderController = realHumanData.chaCtrl.GetComponent<JointCorrectionSlider.JointCorrectionSliderController>();
                    realHumanData.pregnancyController = realHumanData.chaCtrl.GetComponent<KK_PregnancyPlus.PregnancyPlusCharaController>();

                    if (realHumanData.extraBodyColliders != null)
                        realHumanData.extraBodyColliders.Clear();

                    if (realHumanData.coroutine != null)
                    {
                        realHumanData.chaCtrl.StopCoroutine(realHumanData.coroutine);
                        realHumanData.coroutine = null;
                    }

#if FEATURE_TEARDROP_SUPPORT
                    realHumanData.tearDropRate = realHumanData.TearDropLevel;
#endif                    

                    realHumanData.hairDynamicBones.Clear();
                    
                    if (realHumanData.head_bone)
                    {
                        DynamicBone[] hairbones = realHumanData.head_bone.GetComponentsInChildren<DynamicBone>(true);  
                        realHumanData.hairDynamicBones = hairbones.ToList();
                    }
                    
                    if(StudioAPI.InsideStudio)
                    {
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

                        realHumanData.head_areaBuffer = new ComputeBuffer(20, sizeof(float) * 6);
                        realHumanData.body_areaBuffer = new ComputeBuffer(30, sizeof(float) * 6);   

                        realHumanData = GetMaterials(realHumanData.chaCtrl, realHumanData);
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
                            OCIChar ociChar = realHumanData.chaCtrl.GetOCIChar();

                            if (realHumanData.BodyBumpMapActive)
                            {
                                AllocateBumpMap();
                                AllocateFKBones();
                                AllocateAnimBones();
                            }                                                            
                        }                        
                    }
                   
                    realHumanData.leftBoob = realHumanData.chaCtrl.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.BreastL);
                    realHumanData.rightBoob = realHumanData.chaCtrl.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.BreastR);
                    realHumanData.leftButtCheek = realHumanData.chaCtrl.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.HipL);
                    realHumanData.rightButtCheek = realHumanData.chaCtrl.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.HipR);

                    SupportDynamicBoobGravity(realHumanData.chaCtrl, realHumanData);
                    SupportExtraDynamicBonesForHair(realHumanData.chaCtrl, realHumanData);                    
                    SupportTearDrop(realHumanData.chaCtrl, realHumanData);
                    SupportEyeFastBlinkEffect(realHumanData.chaCtrl, realHumanData);
                    SupportBlendShapes(realHumanData.chaCtrl, realHumanData);

                    if (StudioAPI.InsideStudio) {
                        if (realHumanData.BodyBumpMapActive) {
                            DoBodyBumpEffect(realHumanData.chaCtrl, realHumanData);
                            DoFaceBumpEffect(realHumanData.chaCtrl, realHumanData);
                        }
                    }

                    status = Status.RUN;
                    if (realHumanData.coroutine == null) {
                        realHumanData.coroutine = realHumanData.chaCtrl.StartCoroutine(CoroutineProcess(realHumanData));
                    }
                } else
                {
                    status = Status.RUN;
                    if (realHumanData.coroutine == null) {
                        realHumanData.coroutine = realHumanData.chaCtrl.StartCoroutine(CoroutineProcess(realHumanData));
                    }

                }
                
    #if FEATURE_REALPLAY_SUPPORT
                SupportRealPlay();
    #endif               
            }
        }

        internal void AllocateFKBones()
        {
            if (realHumanData != null)
            {
                OCIChar ociChar = realHumanData.chaCtrl.GetOCIChar();

                foreach (OCIChar.BoneInfo bone in ociChar.listBones)
                {
                    if (bone.guideObject != null && bone.guideObject.transformTarget != null) {

                        if (bone.guideObject.transformTarget.name.Contains("_J_Hips"))
                        {
                            realHumanData.fk_hip_bone = bone; // Cache FK hip bone reference.
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("_J_Spine01"))
                        {
                            realHumanData.fk_spine01_bone = bone; // Cache FK spine01 bone reference.
                        }
                        else if(bone.guideObject.transformTarget.name.Contains("_J_Spine02"))
                        {
                            realHumanData.fk_spine02_bone = bone; // Cache FK spine02 bone reference.
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

        internal void AllocateAnimBones()
        {
            if (realHumanData == null || realHumanData.chaCtrl == null || realHumanData.chaCtrl.objAnim == null)
                return;

            string bonePrefix = realHumanData.chaCtrl.sex == 0 ? "cm_" : "cf_";
            Transform animRoot = realHumanData.chaCtrl.objAnim.transform;

            realHumanData.anim_hip_bone = animRoot.FindLoop(bonePrefix + "J_Hips");
            realHumanData.anim_spine01_bone = animRoot.FindLoop(bonePrefix + "J_Spine01");
            realHumanData.anim_spine02_bone = animRoot.FindLoop(bonePrefix + "J_Spine02");
            realHumanData.anim_head_bone = animRoot.FindLoop(bonePrefix + "J_Head");
            realHumanData.anim_neck_bone = animRoot.FindLoop(bonePrefix + "J_Neck");
            realHumanData.anim_left_shoulder_bone = animRoot.FindLoop(bonePrefix + "J_Shoulder_L");
            realHumanData.anim_right_shoulder_bone = animRoot.FindLoop(bonePrefix + "J_Shoulder_R");
            realHumanData.anim_left_armup_bone = animRoot.FindLoop(bonePrefix + "J_ArmUp00_L");
            realHumanData.anim_right_armup_bone = animRoot.FindLoop(bonePrefix + "J_ArmUp00_R");
            realHumanData.anim_left_armdown_bone = animRoot.FindLoop(bonePrefix + "J_ArmLow01_L");
            realHumanData.anim_right_armdown_bone = animRoot.FindLoop(bonePrefix + "J_ArmLow01_R");
            realHumanData.anim_left_thigh_bone = animRoot.FindLoop(bonePrefix + "J_LegUp00_L");
            realHumanData.anim_right_thigh_bone = animRoot.FindLoop(bonePrefix + "J_LegUp00_R");
            realHumanData.anim_left_knee_bone = animRoot.FindLoop(bonePrefix + "J_LegLow01_L");
            realHumanData.anim_right_knee_bone = animRoot.FindLoop(bonePrefix + "J_LegLow01_R");
            realHumanData.anim_left_foot_bone = animRoot.FindLoop(bonePrefix + "J_Foot01_L");
            realHumanData.anim_right_foot_bone = animRoot.FindLoop(bonePrefix + "J_Foot01_R");
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

        internal void SupportDynamicBoobGravity(ChaControl chaCtrl, RealHumanData realHumanData)
        {
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
        }

        internal void SupportExtraDynamicBonesForHair(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            if (chaCtrl.sex == 0 || realHumanData.hairDynamicBones.Count == 0)
                return;

            DynamicBoneCollider[] existingDynamicBoneColliders = chaCtrl.transform.FindLoop("cf_J_Root").GetComponentsInChildren<DynamicBoneCollider>(true);
            List<DynamicBoneCollider> extraBoobColliders = new List<DynamicBoneCollider>();

            Transform handLObject = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Hand_L");
            Transform handRObject = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Hand_R");
            
            Transform fingerIdx2LObject = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Hand_Index02_L");
            Transform fingerIdx2RObject = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Hand_Index02_R");
            
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
            // Register reusable colliders on chest and hip dynamic bones.
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

            extraBodyColliders.AddRange(extraHandsColliders); // Merge hand colliders into body collider set.

            Transform faceObject = chaCtrl.objBodyBone.transform.FindLoop("cf_J_FaceLow_s");
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(faceObject, DynamicBoneColliderBase.Direction.Y, 0.65f, 2.5f, new Vector3(0.0f, 0.0f, 0.3f)));

            // Add chest colliders used by hair/body interactions.
            Transform leftBoobObject = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Mune01_L");
            Transform rightBoobObject = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Mune01_R");
            
            float boob_radius = 0.4f;

            extraBodyColliders.Add(AddExtraDynamicBoneCollider(leftBoobObject, DynamicBoneColliderBase.Direction.Y, boob_radius, boob_radius * 3.0f , new Vector3(0.0f, 0.0f, 0.0f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(rightBoobObject, DynamicBoneColliderBase.Direction.Y, boob_radius, boob_radius * 3.0f, new Vector3(0.0f, 0.0f, 0.0f)));

            // Add shoulder colliders to improve upper-body collision response.
            Transform leftShoulderObject = chaCtrl.objBodyBone.transform.FindLoop("cf_J_ArmUp00_L");
            Transform rightShoulderObject = chaCtrl.objBodyBone.transform.FindLoop("cf_J_ArmUp00_R");

            float shoulder_radius = 0.4f;

            extraBodyColliders.Add(AddExtraDynamicBoneCollider(leftShoulderObject, DynamicBoneColliderBase.Direction.Y, shoulder_radius, shoulder_radius * 3.0f , new Vector3(0.0f, 0.0f, 0.0f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(rightShoulderObject, DynamicBoneColliderBase.Direction.Y, shoulder_radius, shoulder_radius * 3.0f, new Vector3(0.0f, 0.0f, 0.0f)));

            // Add torso colliders along spine bones.
            Transform spine1Object = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Spine01");
            Transform spine2Object = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Spine02");
            Transform spine3Object = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Spine03");
   
            float spine1_radius = 0.8f;
            float spine2_radius = 0.9f;
            float spine3_radius = 0.8f;
   
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(spine1Object, DynamicBoneColliderBase.Direction.Y, spine1_radius, spine1_radius * 4.0f, new Vector3(0.0f, 0.0f, 0.0f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(spine2Object, DynamicBoneColliderBase.Direction.Y, spine2_radius, spine2_radius * 3.5f, new Vector3(0.0f, 0.0f, 0.2f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(spine3Object, DynamicBoneColliderBase.Direction.X, spine3_radius, spine3_radius * 4.0f, new Vector3(0.0f, 0.0f, 0.0f)));

            // Add pelvis/hip colliders for lower-body collision support.
            Transform kosi2Object = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Kosi02");
           
            float kosi2_radius = 0.8f;

            extraBodyColliders.Add(AddExtraDynamicBoneCollider(kosi2Object, DynamicBoneColliderBase.Direction.X, kosi2_radius, kosi2_radius * 4.0f, new Vector3(0.0f, -0.15f, -0.05f)));

            Transform siriLObject = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Siri_L");
            Transform siriRObject = chaCtrl.objBodyBone.transform.FindLoop("cf_J_Siri_R");

            extraBodyColliders.Add(AddExtraDynamicBoneCollider(siriLObject, DynamicBoneColliderBase.Direction.X, 0.5f, 1.8f, new Vector3(0.0f, -0.25f, 0.0f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(siriRObject, DynamicBoneColliderBase.Direction.X, 0.5f, 1.8f, new Vector3(0.0f, -0.25f, 0.0f)));

            realHumanData.extraBodyColliders = extraBodyColliders
                .Where(v => v != null)
                .Distinct()
                .ToList();

            // Attach extra body colliders to all active hair dynamic bones.
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

            SyncExtraColliderSnapshotsAfterBuild();
            SetHairDown();            
        }


        // internal static Texture2D RenderTextureToTexture2D(RenderTexture rt)
        // {
        //     if (rt == null) return null;

        //     // Backup active render target.
        //     RenderTexture prev = RenderTexture.active;

        //     // Bind source render texture for pixel readback.
        //     RenderTexture.active = rt;

        //     // Allocate CPU texture buffer.
        //     Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);

        //     // Copy pixels from render texture and finalize.
        //     tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        //     tex.Apply();

        //     // Restore previous render target.
        //     RenderTexture.active = prev;

        //     return tex;
        // }

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

        internal static RenderTexture ConvertToRenderTexture(Texture sourceTexture, int targetWidth, int targetHeight)
        {
            if (sourceTexture == null)
                return null;

            int width = targetWidth > 0 ? targetWidth : sourceTexture.width;
            int height = targetHeight > 0 ? targetHeight : sourceTexture.height;

            RenderTexture result = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            result.Create();
            Graphics.Blit(sourceTexture, result);
            return result;
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
            // Helper: remove any non-dynamic collider components on debug objects.
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
            // Ensure pivot object exists under target bone.
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
            // Ensure collider child exists under pivot and has DynamicBoneCollider.
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
            // Apply collider shape settings.
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

            // Enumerate all descendants under the root transform.
            Transform[] allChildren = parent.GetComponentsInChildren<Transform>(true);

            foreach (Transform tr in allChildren)
            {
                // Process only generated extra dynamic-bone collider transforms.
                if (!tr.name.EndsWith("_ExtDBoneCollider")) continue;

                // Apply requested local scale override.
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
                        else if (name.Contains("Anus Open Large 3")) 
                        {
                            realHumanData.anus_open_idx_in_body = idx;
                        }
                        else if (name.Contains("Anus Pull Out"))
                        {
                            realHumanData.anus_pullout_idx_in_body = idx;
                        }
                        else if (name.Contains("Vagina Up"))
                        {
                            realHumanData.vagina_up_idx_in_body = idx;
                        }
                        else if (name.Contains("Vagina Top"))
                        {
                            realHumanData.vagina_top_idx_in_body = idx;
                        }                                                                     
                        else if (name.Contains("Vagina Open Front"))
                        {
                            realHumanData.vagina_open_front_idx_in_body = idx;
                        }                                                
                        else if (name.Contains("Vagina Open All Outside"))
                        {
                            realHumanData.vagina_open_all_outside_idx_in_body = idx;
                        }
                        else if (name.Contains("Vagina Open Squeeze"))
                        {
                            realHumanData.vagina_open_squeeze_idx_in_body = idx;
                        }                                            
                        // UnityEngine.Debug.Log($">> blendShape {name}, {idx} in body");
                    }
                }
            }                       
        }

        internal void SetBlendShape(float weight, int targetIdx)
        {
            if (realHumanData == null || realHumanData.chaCtrl == null || realHumanData.chaCtrl.objBody == null)
                return;
            
            if (targetIdx < 0)
                return;

            SkinnedMeshRenderer[] bodyRenderers = realHumanData.chaCtrl.objBody.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                SkinnedMeshRenderer render = bodyRenderers[i];
                if (render == null || render.sharedMesh == null)
                    continue;

                Mesh mesh = render.sharedMesh;
                if (targetIdx < mesh.blendShapeCount)
                {
                    string nameAtTarget = mesh.GetBlendShapeName(targetIdx);
                    if (nameAtTarget != null)
                    {
                        render.SetBlendShapeWeight(targetIdx, weight);
                        continue;
                    }
                }

                for (int idx = 0; idx < mesh.blendShapeCount; idx++)
                {
                    string name = mesh.GetBlendShapeName(idx);
                    if (name != null)
                    {
                        render.SetBlendShapeWeight(idx, weight);
                        break;
                    }
                }
            }
        }
#endif

#if FEATURE_REALPLAY_SUPPORT
        // Create or refresh kinematic rigidbody + capsule collider on a target bone.
        internal void SetRigidBodyOnObject(string boneName, float radius = 0.2f, float height = 0.5f)
        {
            // UnityEngine.Debug.Log($">> SetRigidBodyOnObject {boneName}");

            if (realHumanData != null)
            {
                Transform danObject = realHumanData.chaCtrl.objBodyBone.transform.FindLoop(boneName);

                if (danObject != null)
                {
                    string childName = "RG_" + boneName;
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

                    // Ensure capsule collider exists on generated rigidbody object.
                    CapsuleCollider capsule = childObj.GetComponent<CapsuleCollider>();
                    if (capsule == null)
                    {
                        capsule = childObj.AddComponent<CapsuleCollider>();
                        capsule.radius = radius;      // Apply configured capsule radius.
                        capsule.height = height;
                        capsule.direction = 0;
                        capsule.center = Vector3.zero;
                        capsule.isTrigger = false;  // Keep as physical collider (non-trigger).
                    }

                    UnityEngine.Debug.Log($">> created rigidBody + collider on {boneName}");
                } else {
                    UnityEngine.Debug.Log($">> failed created rigidBody + collider on {boneName}");
                }
            } else {
                UnityEngine.Debug.Log($">> failed created rigidBody + collider on {boneName}");
            }
        }

        // Create/refresh trigger sphere object and bind CapsuleTrigger handler.
        internal void SetCollisionOnOnObject(string bone_name)
        {
            UnityEngine.Debug.Log($">> SetCollisionOnOnObject {realHumanData}");

            if (realHumanData != null && realHumanData.chaCtrl.sex == 1)
            {
                // string bone_prefix_str = "cf_";
                string childName = "RGTriggerCollision";

                Transform kosiObject = realHumanData.chaCtrl.objBodyBone.transform.FindLoop(bone_name);

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

                    CapsuleTrigger trigger = childObj.GetComponent<CapsuleTrigger>();
                    if (trigger != null)
                        trigger.Bind(this);

                    UnityEngine.Debug.Log("$>> created sphere trigger on {bone_name}");
                } else {
                    UnityEngine.Debug.Log($">> failed created sphere trigger on {bone_name}");
                }
            } else {
                UnityEngine.Debug.Log($">> failed created sphere trigger on {bone_name}");
            }
        }
#endif

        internal static PositionData GetBoneRotationFromTF(Transform t)
        {
            // Read current local rotation for FK angle extraction.
            Quaternion localRot = t.localRotation;

            Vector3 localEuler = localRot.eulerAngles;

            // Normalize Euler angle to [-180, 180] range.
            float Normalize(float angle)
            {
                if (angle > 180f)
                    angle -= 360f;
                return angle;
            }

            float frontback = Normalize(localEuler.x);  // Pitch-like forward/back component.
            float leftright = Normalize(localEuler.z);  // Roll-like left/right component.

            PositionData data = new PositionData(
                t.rotation,     // World-space rotation snapshot.
                frontback,
                leftright
            );

            return data;
        }

        internal void AllocateBumpMap()
        {   
            if (realHumanData == null)
                return;

            // Cache original bump textures only once; reuse on re-init to avoid cumulative blending.
            if (realHumanData.headOriginBumpMap2Texture == null)
            {
                Texture headOriginBumpMap2Texture = realHumanData.m_skin_head.GetTexture(realHumanData.head_bumpmap_type);
                realHumanData.headOriginBumpMap2Texture = ConvertToRenderTexture(
                    headOriginBumpMap2Texture,
                    RealHumanSupport._self._faceExpressionFemaleBumpMap2.width,
                    RealHumanSupport._self._faceExpressionFemaleBumpMap2.height);
            }

            if (realHumanData.bodyOriginBumpMap2Texture == null)
            {
                Texture bodyOriginBumpMap2Texture = realHumanData.m_skin_body.GetTexture(realHumanData.body_bumpmap_type);
                realHumanData.bodyOriginBumpMap2Texture = ConvertToRenderTexture(
                    bodyOriginBumpMap2Texture,
                    RealHumanSupport._self._bodyStrongFemaleBumpMap2.width,
                    RealHumanSupport._self._bodyStrongFemaleBumpMap2.height);
            }            
        }

        internal static RealHumanData GetMaterials(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            SkinnedMeshRenderer[] bodyRenderers = realHumanData.chaCtrl.objBody.GetComponentsInChildren<SkinnedMeshRenderer>();
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
            

            SkinnedMeshRenderer[] headRenderers = realHumanData.chaCtrl.objHead.GetComponentsInChildren<SkinnedMeshRenderer>();
            // UnityEngine.Debug.Log($">> headRenderers {headRenderers.Length}");
            realHumanData.c_m_eye.Clear();

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

        internal static float Remap(float value, float inMin, float inMax, float outMin, float outMax)
        {
            return outMin + (value - inMin) * (outMax - outMin) / (inMax - inMin);
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

        internal void SupportTearDrop(ChaControl chaCtrl, RealHumanData realHumanData) 
        {
#if FEATURE_TEARDROP_SUPPORT            
            if (chaCtrl.sex == 0)
                return;
            SetTearDrops();
#endif            
        }
                
#if FEATURE_REALPLAY_SUPPORT
        internal void SupportRealPlay() 
        {
            if (realHumanData == null)
                return;

            if (realHumanData.chaCtrl.sex == 0) {
                SetRigidBodyOnObject("cm_J_Hand_Index02_L", 0.05f, 0.15f);
                SetRigidBodyOnObject("cm_J_Hand_Index02_R", 0.05f, 0.15f); 
                // SetRigidBodyOnObject("cm_J_Hand_Index01_L", 0.05f, 0.15f);
                // SetRigidBodyOnObject("cm_J_Hand_Index01_R", 0.05f, 0.15f); 

                SetRigidBodyOnObject("cm_J_Hand_Middle02_L", 0.1f, 0.22f);
                SetRigidBodyOnObject("cm_J_Hand_Middle02_R", 0.1f, 0.22f); 
                SetRigidBodyOnObject("cm_J_Hand_Middle01_L", 0.1f, 0.22f);
                SetRigidBodyOnObject("cm_J_Hand_Middle01_R", 0.1f, 0.22f);

                SetRigidBodyOnObject("cm_J_Hand_L", 0.20f, 0.45f);
                SetRigidBodyOnObject("cm_J_Hand_R", 0.20f, 0.45f);

                // SetRigidBodyOnObject("cm_J_Kosi02", 0.5f, 1.25f);

                SetRigidBodyOnObject("cm_J_dan119_00", 0.15f, 0.38f);          
                SetRigidBodyOnObject("cm_J_dan108_00", 0.15f, 0.4f); 
                SetRigidBodyOnObject("cm_J_dan105_00", 0.15f, 0.4f); 
                // SetRigidBodyOnObject("cm_J_dan100_00", 0.15f, 0.4f);               
            } else {
                SetRigidBodyOnObject("cf_J_Hand_Index02_L", 0.1f, 0.25f);
                SetRigidBodyOnObject("cf_J_Hand_Index02_R", 0.1f, 0.25f);  
                // SetRigidBodyOnObject("cf_J_Hand_Index01_L", 0.1f, 0.22f);
                // SetRigidBodyOnObject("cf_J_Hand_Index01_R", 0.1f, 0.22f);  
                
                // SetRigidBodyOnObject("cm_J_Kosi02", 0.5f, 1.25f);

                SetRigidBodyOnObject("cf_J_Hand_Middle02_L", 0.1f, 0.22f);
                SetRigidBodyOnObject("cf_J_Hand_Middle02_R", 0.1f, 0.22f);
                SetRigidBodyOnObject("cf_J_Hand_Middle01_L", 0.1f, 0.22f);
                SetRigidBodyOnObject("cf_J_Hand_Middle01_R", 0.1f, 0.22f);

                SetRigidBodyOnObject("cf_J_Hand_L", 0.20f, 0.45f);
                SetRigidBodyOnObject("cf_J_Hand_R", 0.20f, 0.45f);

                SetCollisionOnOnObject("cf_J_Vagina_root");
            }
        }
#endif

        internal void SupportEyeFastBlinkEffect(ChaControl chaCtrl, RealHumanData realHumanData) 
        {
            if (chaCtrl.fbsCtrl != null)
                chaCtrl.fbsCtrl.BlinkCtrl.BaseSpeed = 0.05f; // Increase blink cadence.
        }

        internal void SetTearDropActive(bool active)
        {
            if (realHumanData == null)
                return;

            if (realHumanData.TearDropActive == active)
                return;

            realHumanData.TearDropActive = active;
            if (!active)
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
        }

#endregion
    }

    class RealHumanData
    {
        public ChaControl chaCtrl;
        public Coroutine coroutine;

        // Coroutine control flag used by runtime update loops.
        public bool coroutine_pause;

        public List<BAreaData> areas = new List<BAreaData>();

        // Head raycast config.
        public Transform head_bone;
        public Vector3 headRaycastOriginOffset = new Vector3(0f, 0f, 0.02f);
        public float headRaycastDistance = 0.8f;
        public float headRaycastInterval = 0.05f;
        // [Head Raycast Result]
        // Whether an isTrigger collider was detected in the last check.
        public bool headTriggerDetected = false;
        // Last detected trigger collider.
        public Collider headLastTriggerCollider = null;
        // Rigidbody linked to the last detected trigger collider (can be null).
        public Rigidbody headLastTriggerRigidbody = null;
        
        public bool RealPlayActive = false;
        public bool TearDropActive = false;
        public bool BreathActive = false;
        public bool EyeShakeActive = true;
        public bool BodyBumpMapActive = true;
        // Whether a rigidbody was detected in the last check.
        public bool headRigidbodyDetected = false;
        // Last detected rigidbody.
        public Rigidbody headLastDetectedRigidbody = null;
        // Collider that produced the last rigidbody detection hit.
        public Collider headLastRigidbodyCollider = null;
        public Transform neck_bone;
        public Transform root_bone;  // Cached root transform.
        public Transform dan_bone;

        public List<DynamicBone> hairDynamicBones = new List<DynamicBone>(); // Cached hair dynamic bone list.
        public List<DynamicBoneCollider> extraBodyColliders = new List<DynamicBoneCollider>();
        public Dictionary<string, float[]> extraColliderOriginalSnapshots = new Dictionary<string, float[]>();
        public Dictionary<string, float[]> extraColliderCurrentSnapshots = new Dictionary<string, float[]>();

        // DynamicBone references for chest and hip soft-body simulation.
        public DynamicBone_Ver02 rightBoob;
        public DynamicBone_Ver02 leftBoob;
        public DynamicBone_Ver02 rightButtCheek;
        public DynamicBone_Ver02 leftButtCheek;

        // Core skin/tear materials used for effect s.
        public Material m_tear_eye;
        public Material m_skin_head;
        public Material m_skin_body;

        public Texture headOriginBumpMap2Texture;
        public Texture bodyOriginBumpMap2Texture;

        public RenderTexture _head_rt;
        public ComputeBuffer head_areaBuffer;

        public RenderTexture _body_rt;
        public ComputeBuffer body_areaBuffer;

        // Selected bump-map property names per material.
        public string head_bumpmap_type;
        public string body_bumpmap_type;

        // Previous FK rotations used for delta-based calculations.
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

        public Transform anim_hip_bone;
        public Transform anim_spine01_bone;
        public Transform anim_spine02_bone;
        public Transform anim_head_bone;
        public Transform anim_neck_bone;
        public Transform anim_left_shoulder_bone;
        public Transform anim_right_shoulder_bone;
        public Transform anim_left_armup_bone;
        public Transform anim_right_armup_bone;
        public Transform anim_left_armdown_bone;
        public Transform anim_right_armdown_bone;
        public Transform anim_left_thigh_bone;
        public Transform anim_right_thigh_bone;
        public Transform anim_left_knee_bone;
        public Transform anim_right_knee_bone;
        public Transform anim_left_foot_bone;
        public Transform anim_right_foot_bone;

        // Optional integration hook for pregnancy controller.
        public PregnancyPlusCharaController pregnancyController;
        public JointCorrectionSliderController jointCorrectionSliderController;

        // Eye materials affected by face/tear effects.
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
        public int anus_pullout_idx_in_body;
        public int vagina_top_idx_in_body;
        public int vagina_open_front_idx_in_body;
        public int vagina_open_all_outside_idx_in_body;
        public int vagina_open_squeeze_idx_in_body;
        public int vagina_up_idx_in_body;
#endif

        public float TearDropLevel = 0.3f;
        public float BreathInterval = 1.5f;
        public float BreathStrong = 0.45f;
        public float RealPlayStrong = 1.0f;
        public float RealPlayBlendShapeTarget = 0f;
        // Centered oscillation ratio for real-play blendshapes (0.10 = +/-10%).
        public float RealPlayOscillationPercent = 0.10f;
        public RealHumanData()
        {
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
            fulleg_idx_in_body = -1;
            buttchecks1_idx_in_body = -1;
            buttchecks2_idx_in_body = -1;
            anus_open_idx_in_body = -1;
            anus_pullout_idx_in_body = -1;
            vagina_top_idx_in_body = -1;
            vagina_open_front_idx_in_body = -1;
            vagina_open_all_outside_idx_in_body = -1;
            vagina_open_squeeze_idx_in_body = -1;
            vagina_up_idx_in_body = -1;
#endif
        }     
    }

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

    // Packed area data used by compute-shader bump influence passes.
    struct BAreaData
    {  
        public float X { get; set; }
        public float Y { get; set; }
        public float RadiusX; // Horizontal area radius.
        public float RadiusY; // Vertical area radius.
        public float BumpBooster; // Intensity multiplier for bump contribution.
        public float Padding;   // Extra margin for area blending.
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

    enum Status
    {
        INIT,
        PAUSE,
        RUN
    }

}
