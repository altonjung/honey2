﻿using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UILib;
using UILib.ContextMenu;
using UILib.EventHandlers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;

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
#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace RealHumanSupport
{
    public class Logic
    {
        #region Private Methods            

        internal static void DeleteExtraDynamicBoneCollider(GameObject pivotObj)
        {

            if (RealHumanSupport._self.extraColliderDebugObjAdded) {
                Transform[] children = pivotObj.GetComponentsInChildren<Transform>(true);

                for (int i = 0; i < children.Length; i++)
                {
                    Transform t = children[i];

                    // 자기 자신은 제외
                    if (t == pivotObj.transform)
                        continue;

                    if (t.name.Contains("_DBC_DebugSphere")  || t.name.Contains("_DBC_CapEndA") || t.name.Contains("_DBC_CapEndB") || t.name.Contains("_DBC_CapBody"))
                    {
                        UnityEngine.Object.Destroy(t.gameObject);
                    }
                }
            }

            RealHumanSupport._self.extraColliderDebugObjAdded = false;
        }

        internal static DynamicBoneCollider AddExtraDynamicBoneCollider(
            Transform target,
            DynamicBoneColliderBase.Direction direction,
            float radius,
            float height,
            Vector3 offset)
        {
            string pivotName = target.name + "_DBC_Pivot";
            string colliderName = target.name + "_DynamicBoneCollider";

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

            if (RealHumanSupport.ExtraColliderDebug.Value)
            {
                // ----- Sphere -----
                Transform debugTf = pivotTf.Find(debugName);
                if (debugTf == null)
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = debugName;
                    RemoveCollider(go);
                    SetupDebugRenderer(go);
                    go.transform.SetParent(pivotTf, true);
                    debugTf = go.transform;
                }

                Vector3 centerWorld = colliderTf.TransformPoint(dbc.m_Center);
                debugTf.position = centerWorld;
                debugTf.localScale = Vector3.one * (radius * 2f);

                // ----- Capsule -----
                Transform capEndATf = pivotTf.Find(capEndAName);
                Transform capEndBTf = pivotTf.Find(capEndBName);
                Transform capBodyTf = pivotTf.Find(capBodyName);

                if (capEndATf == null)
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = capEndAName;
                    RemoveCollider(go);
                    SetupDebugRenderer(go);
                    go.transform.SetParent(pivotTf, true);
                    capEndATf = go.transform;
                }

                if (capEndBTf == null)
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = capEndBName;
                    RemoveCollider(go);
                    SetupDebugRenderer(go);
                    go.transform.SetParent(pivotTf, true);
                    capEndBTf = go.transform;
                }

                if (capBodyTf == null)
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.name = capBodyName;
                    RemoveCollider(go);
                    SetupDebugRenderer(go);
                    go.transform.SetParent(pivotTf, true);
                    capBodyTf = go.transform;
                }

                if (height > 0f)
                {
                    Vector3 axisLocal = Vector3.forward;
                    switch (direction)
                    {
                        case DynamicBoneColliderBase.Direction.X:
                            axisLocal = Vector3.right;
                            break;
                        case DynamicBoneColliderBase.Direction.Y:
                            axisLocal = Vector3.up;
                            break;
                        case DynamicBoneColliderBase.Direction.Z:
                        default:
                            axisLocal = Vector3.forward;
                            break;
                    }

                    Vector3 axisWorld = colliderTf.TransformDirection(axisLocal).normalized;
                    float bodyLen = Mathf.Max(0f, height - radius * 2f);

                    Vector3 p1 = centerWorld + axisWorld * (bodyLen * 0.5f);
                    Vector3 p2 = centerWorld - axisWorld * (bodyLen * 0.5f);

                    capEndATf.position = p1;
                    capEndBTf.position = p2;
                    capEndATf.localScale = Vector3.one * (radius * 2f);
                    capEndBTf.localScale = Vector3.one * (radius * 2f);

                    capBodyTf.position = (p1 + p2) * 0.5f;
                    capBodyTf.up = (p1 - p2).normalized;
                    capBodyTf.localScale = new Vector3(
                        radius * 2f,
                        bodyLen * 0.5f,
                        radius * 2f
                    );

                    capEndATf.gameObject.SetActive(true);
                    capEndBTf.gameObject.SetActive(true);
                    capBodyTf.gameObject.SetActive(true);
                }
                else
                {
                    if (capEndATf != null) capEndATf.gameObject.SetActive(false);
                    if (capEndBTf != null) capEndBTf.gameObject.SetActive(false);
                    if (capBodyTf != null) capBodyTf.gameObject.SetActive(false);
                }

                RealHumanSupport._self.extraColliderDebugObjAdded = true;
            }
            else
            {
                void DestroyIfExists(string name)
                {
                    Transform t = pivotTf.Find(name);
                    if (t != null)
                        UnityEngine.Object.Destroy(t.gameObject);
                }

                DestroyIfExists(debugName);
                DestroyIfExists(capEndAName);
                DestroyIfExists(capEndBName);
                DestroyIfExists(capBodyName);

                RealHumanSupport._self.extraColliderDebugObjAdded = false;
            }

            return dbc;
        }


        internal static void SetTearDrops(ChaControl chaCtrl, RealHumanData realHumanData) {
            string bone_prefix_str = "cf_";
            if(chaCtrl.sex == 0)
                bone_prefix_str = "cm_";
    
            realHumanData.nose_bridge_s = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_NoseBridge_s"); 
            realHumanData.mouse_l = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Mouth_L"); 

            // test
            realHumanData.cf_J_LegUp00_L = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_LegUp02_L");
            realHumanData.cf_J_LegLow01_L = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_LegUp03_L");
            realHumanData.cf_J_LegUp00_R = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_LegUp02_R");
            realHumanData.cf_J_LegLow01_R = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_LegUp03_R");
            realHumanData.legup00_L_basePos = realHumanData.cf_J_LegUp00_L.localPosition;
            realHumanData.legLow01_L_basePos = realHumanData.cf_J_LegLow01_L.localPosition;
            realHumanData.legup00_R_basePos = realHumanData.cf_J_LegUp00_R.localPosition;
            realHumanData.legLow01_R_basePos = realHumanData.cf_J_LegLow01_R.localPosition;
        }        
        
        internal static void SetHairDown(ChaControl chaCtrl, RealHumanData realHumanData) {
            string bone_prefix_str = "cf_";
            if(chaCtrl.sex == 0)
                bone_prefix_str = "cm_";

            if (realHumanData.root_bone != null)
            {
                foreach (DynamicBone bone in realHumanData.hairDynamicBones)
                {
                    if (bone == null)
                        continue;

                    Transform hairTip;
                    if (!realHumanData.hairTipCache.TryGetValue(bone, out hairTip))
                        continue;

                    Vector3 dirToRoot =
                        (realHumanData.root_bone.position - hairTip.position).normalized;

                    bone.m_Gravity = dirToRoot * 0.015f; // 디버그용
                    bone.m_Force = Vector3.zero;

                    bone.m_Damping    = 0.13f;
                    bone.m_Elasticity = 0.05f;
                    bone.m_Stiffness  = 0.13f;
                }
            }
            // if (realHumanData.root_bone != null)
            // foreach (DynamicBone bone in realHumanData.hairDynamicBones)
            // {
            //     if (bone == null)
            //         continue;

            //     Transform hairTip;
            //     if (!realHumanData.hairTipCache.TryGetValue(bone, out hairTip))
            //         continue;

            //     // --- 1. 월드 기준: hair tip → 캐릭터 중심
            //     Vector3 dirWorld =
            //         (realHumanData.root_bone.position - hairTip.position).normalized;

            //     // --- 2. DynamicBone은 로컬 force만 먹음
            //     Vector3 dirLocal =
            //         bone.transform.InverseTransformDirection(dirWorld);

            //     // --- 3. Gravity는 "늘어짐 유지" 전용 (고정)
            //     bone.m_Gravity = Vector3.down * 0.003f;

            //     // --- 4. Force는 보간으로 누적 (이게 핵심)
            //     bone.m_Force = Vector3.Lerp(
            //         bone.m_Force,
            //         dirLocal * 0.02f,
            //         0.15f
            //     );

            //     // --- 5. 안정 파라미터
            //     bone.m_Damping    = 0.25f;
            //     bone.m_Elasticity = 0.04f;
            //     bone.m_Stiffness  = 0.12f;
            // }
        }

#if FEATURE_STRAPON_SUPPORT        
        private static void SupportExtraDeviceRigidBody(CharContrl chaCtrl, RealHumanData realHumanData) {
            string childName = "RHTriggerRigidBody"; // 자식 이름

            Transform danObject = chaCtrl.objBodyBone.transform.FindLoop("cm_J_dan_top");            
            Transform existingChild = danObject.Find(childName);

            if (existingChild != null)
            {             
                return;
            }

            // 1. Rigidbody 추가
            Rigidbody rb = capsuleObj.AddComponent<Rigidbody>();
            rb.isKinematic = true; // 물리 반응 없이 Trigger 이벤트만 받음            

            // 2. 부모 연결
            rb.transform.SetParent(danObject, false);             
            rb.transform.localPosition = Vector3.zero;
        }        

        private static void SupportExtraDeviceCollision(CharContrl chaCtrl, RealHumanData realHumanData) {
            string bone_prefix_str = "cf_";
            if(chaCtrl.sex == 0)
                bone_prefix_str = "cm_";

            string childName = "RHTriggerCapsuleObj"; // 자식 이름
            Transform kosi1Object = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Kosi01");            
            Transform existingChild = kosi1Object.Find(childName);

            if (existingChild != null)
            {             
                return;
            }

            // 1. Capsule GameObject 생성
            GameObject capsuleObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);

            // 2. Capsule Collider 가져오기
            CapsuleCollider capsuleCollider = capsuleObj.GetComponent<CapsuleCollider>();
            capsuleCollider.isTrigger = true; // Trigger 모드 활성화

            // 3. CapsuleTrigger 스크립트 추가
            capsuleObj.AddComponent<CapsuleTrigger>();

            // 4. 부모 연결
            capsuleObj.transform.SetParent(kosi1Object, false);         
            capsuleObj.transform.localPosition = Vector3.zero; 
        }
#endif
        internal static void SupportExtraDynamicBones(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            string bone_prefix_str = "cf_";
            if(chaCtrl.sex == 0)
                bone_prefix_str = "cm_";

            if (RealHumanSupport.ExBoneColliderActive.Value && chaCtrl.sex == 1)
            {
                //boob/butt에 y축 gravity 자동 부여
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

                // boob/butt dynamicbone에 leg&arm&finger collider 연결                    
                DynamicBoneCollider[] existingDynamicBoneColliders = chaCtrl.transform.FindLoop(bone_prefix_str+"J_Root").GetComponentsInChildren<DynamicBoneCollider>(true);
                List<DynamicBoneCollider> extraLegAndArmColliders = new List<DynamicBoneCollider>();

                foreach (DynamicBoneCollider collider in existingDynamicBoneColliders)
                {
                    if (collider.name.Contains("Leg") || collider.name.Contains("Arm") || collider.name.Contains("Hand"))
                    {
                        extraLegAndArmColliders.Add(collider);
                    }
                }
                
                foreach (var collider in extraLegAndArmColliders)
                {
                    if (collider == null)
                        continue;

                    if (!realHumanData.leftBoob.Colliders.Contains(collider))
                    {
                        realHumanData.leftBoob.Colliders.Add(collider);
                    }
                }

                foreach (var collider in extraLegAndArmColliders) 
                {
                    if (collider == null)
                        continue;

                    if (!realHumanData.rightBoob.Colliders.Contains(collider))
                    {
                        realHumanData.rightBoob.Colliders.Add(collider);
                    }
                }

                foreach (var collider in extraLegAndArmColliders){
                    if (collider == null)
                        continue;

                    if (!realHumanData.leftButtCheek.Colliders.Contains(collider))
                    {
                        realHumanData.leftButtCheek.Colliders.Add(collider);
                    }                    
                }

                foreach (var collider in extraLegAndArmColliders) {
                    if (collider == null)
                        continue;

                    if (!realHumanData.rightButtCheek.Colliders.Contains(collider))
                    {
                        realHumanData.rightButtCheek.Colliders.Add(collider);
                    }                    
                }

                
                SetTearDrops(chaCtrl, realHumanData);
                SetHairDown(chaCtrl, realHumanData);     

                // hair dynamic bone 연결 대상 finger collider 생성
                List<DynamicBoneCollider> extraHairColliders = new List<DynamicBoneCollider>();       

                Transform fingerThumb2LObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Thumb02_L");
                Transform fingerThumb3LObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Thumb03_L");

                Transform fingerIdx2LObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Index02_L");
                Transform fingerIdx3LObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Index03_L");
                
                Transform fingerMiddle2LObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Middle02_L");
                Transform fingerMiddle3LObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Middle03_L");
                
                Transform fingerRing2LObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Ring02_L");
                Transform fingerRing3LObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Ring03_L");

                Transform fingerThumb2RObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Thumb02_R");
                Transform fingerThumb3RObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Thumb03_R");

                Transform fingerIdx2RObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Index02_R");
                Transform fingerIdx3RObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Index03_R");

                Transform fingerMiddle2RObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Middle02_R");
                Transform fingerMiddle3RObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Middle03_R");
                
                Transform fingerRing2RObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Ring02_R");
                Transform fingerRing3RObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Hand_Ring03_R");

                // List<DynamicBoneCollider> extraFingerColliders = new List<DynamicBoneCollider>();

                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerThumb2LObject, DynamicBoneColliderBase.Direction.X, 0.07f, 0, new Vector3(0f, 0f, 0f)));
                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerThumb3LObject, DynamicBoneColliderBase.Direction.X, 0.07f, 0.24f, new Vector3(0f, 0f, 0f)));

                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerIdx2LObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0, new Vector3(0f, 0f, 0f)));
                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerIdx3LObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0.24f, new Vector3(0f, 0f, 0f)));

                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerMiddle2LObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0, new Vector3(0f, 0f, 0f)));
                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerMiddle3LObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0.24f, new Vector3(0f, 0f, 0f)));
          
                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerRing2LObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0, new Vector3(0f, 0f, 0f)));
                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerRing3LObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0.24f, new Vector3(0f, 0f, 0f)));

                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerThumb2RObject, DynamicBoneColliderBase.Direction.X, 0.07f, 0, new Vector3(0f, 0f, 0f)));
                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerThumb3RObject, DynamicBoneColliderBase.Direction.X, 0.07f, 0.24f, new Vector3(0f, 0f, 0f)));

                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerIdx2RObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0, new Vector3(0f, 0f, 0f)));
                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerIdx3RObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0.24f, new Vector3(0f, 0f, 0f)));

                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerMiddle2RObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0, new Vector3(0f, 0f, 0f)));
                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerMiddle3RObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0.24f, new Vector3(0f, 0f, 0f)));
         
                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerRing2RObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0, new Vector3(0f, 0f, 0f)));
                extraHairColliders.Add(AddExtraDynamicBoneCollider(fingerRing3RObject, DynamicBoneColliderBase.Direction.X, 0.06f, 0.24f, new Vector3(0f, 0f, 0f)));

                // foreach (var bone in hairbones)
                // {
                //     if (bone == null)
                //         continue;

                //     foreach (var collider in extraFingerColliders)
                //     {
                //         if (collider == null)
                //             continue;

                //         if (!bone.m_Colliders.Contains(collider))
                //         {
                //             bone.m_Colliders.Add(collider);
                //         }
                //     }
                // }

                // hair dynamic bone 연결 대상 shoulder collider 생성                
                // List<DynamicBoneCollider> extraHairColliders = new List<DynamicBoneCollider>();
                Transform leftShouderObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_ArmUp00_L");
                Transform rightShouderObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_ArmUp00_R");

                extraHairColliders.Add(AddExtraDynamicBoneCollider(leftShouderObject, DynamicBoneColliderBase.Direction.X, RealHumanSupport.ExtraColliderScale.Value * 0.53f, 0.0f , new Vector3(0.0f, -0.38f, -0.06f)));
                extraHairColliders.Add(AddExtraDynamicBoneCollider(rightShouderObject, DynamicBoneColliderBase.Direction.X, RealHumanSupport.ExtraColliderScale.Value * 0.53f, 0.0f, new Vector3(0.0f, -0.38f, -0.06f)));

                // hair dynamic bone 연결 대상 spine collider 생성
                Transform spine2Object = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Spine02");
                Transform spine3Object = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Spine03");
                
                float spine2_radius = 0.9f;
                float spine3_radius = 0.875f;

                spine2_radius = spine2_radius * RealHumanSupport.ExtraColliderScale.Value;
                spine3_radius = spine3_radius * RealHumanSupport.ExtraColliderScale.Value;

                extraHairColliders.Add(AddExtraDynamicBoneCollider(spine2Object, DynamicBoneColliderBase.Direction.Y, spine2_radius, spine2_radius * 3.0f, new Vector3(0.0f, 0.0f, 0.04f)));
                extraHairColliders.Add(AddExtraDynamicBoneCollider(spine3Object, DynamicBoneColliderBase.Direction.X, spine3_radius, spine3_radius * 3.4f, Vector3.zero));

                // hair dynamic bone 연결 대상 nipple collider 생성  
                Transform leftNippleObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Mune02_L");
                Transform rightNippleObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Mune02_R");

                extraHairColliders.Add(AddExtraDynamicBoneCollider(leftNippleObject, DynamicBoneColliderBase.Direction.X, 0.385f, 0.385f, new Vector3(0.0f, 0.02f, 0.02f)));
                extraHairColliders.Add(AddExtraDynamicBoneCollider(rightNippleObject, DynamicBoneColliderBase.Direction.X, 0.385f, 0.385f, new Vector3(0.0f, 0.02f, 0.02f)));

                // hair dynamic bone 연결 대상 골반 collider 생성
                Transform kosi2Object = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Kosi02");
                float kosi2_radius = 1.17f;
                
                kosi2_radius = kosi2_radius * RealHumanSupport.ExtraColliderScale.Value;

                extraHairColliders.Add(AddExtraDynamicBoneCollider(kosi2Object, DynamicBoneColliderBase.Direction.X, kosi2_radius, kosi2_radius * 2.8f, new Vector3(0.0f, -0.05f, -0.05f)));

                foreach (var bone in realHumanData.hairDynamicBones)
                {
                    if (bone == null)
                        continue;

                    foreach (var collider in extraHairColliders)
                    {
                        if (collider == null)
                            continue;

                        if (!bone.m_Colliders.Contains(collider))
                        {
                            bone.m_Colliders.Add(collider);
                        }
                    }
                }
            }
        }

        internal static Texture2D MergeRGBAlphaMaps(
            Texture2D rgbA,
            Texture2D rgbB,
            List<BArea> areas = null)
        {
            int w = Mathf.Min(rgbA.width, rgbB.width);
            int h = Mathf.Min(rgbA.height, rgbB.height);

            bool useArea = (areas != null && areas.Count > 0);

            Color[] cur = rgbA.GetPixels(0, 0, w, h);
            Color[] B   = rgbB.GetPixels(0, 0, w, h);

            if (!useArea)
            {
                Texture2D onlyA = new Texture2D(w, h, TextureFormat.RGBA32, false);
                onlyA.SetPixels(cur);
                onlyA.Apply();
                return onlyA;
            }

            foreach (var area in areas)
            {
                float rx = area.RadiusX > 0f ? area.RadiusX : area.RadiusY;
                float ry = area.RadiusY > 0f ? area.RadiusY : area.RadiusX;
                if (rx <= 0f || ry <= 0f)
                    continue;

                float invRx = 1f / rx;
                float invRy = 1f / ry;

                float areaY = (h - 1) - area.Y;

                float rgbStrength  = Mathf.Clamp01(area.BumpBooster);
                float alphaBooster = Mathf.Max(1f, area.Strong);

                Parallel.For(0, h, y =>
                {
                    float dy = y - areaY;

                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;

                        float dx = x - area.X;

                        // ellipse test
                        float nx = dx * invRx;
                        float ny = dy * invRy;
                        float ellipseVal = nx * nx + ny * ny;
                        if (ellipseVal > 1f)
                            continue;

                        // extreme falloff
                        float t = Mathf.Sqrt(ellipseVal);
                        float falloff = Mathf.Clamp01(1f - t * t * t);

                        Color a = cur[idx];
                        Color b = B[idx];

                        // ===== RGB =====
                        float rgbWeight = rgbStrength * falloff;
                        float invRgb = 1f - rgbWeight;

                        float g = a.g * invRgb + b.g * rgbWeight;
                        float bb = a.b * invRgb + b.b * rgbWeight;

                        // ===== Alpha =====
                        float boostFactor = Mathf.Lerp(1f, alphaBooster, falloff);
                        float boostedBAlpha = Mathf.Clamp01(b.a * boostFactor);
                        float aMerged = Mathf.Lerp(a.a, boostedBAlpha, falloff);

                        cur[idx] = new Color(
                            1f,
                            Mathf.Clamp01(g),
                            Mathf.Clamp01(bb),
                            Mathf.Clamp01(aMerged)
                        );
                    }
                });
            }

            Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.SetPixels(cur);
            result.Apply();
            return result;
        }
        internal static PositionData GetBoneRotationFromIK(OCIChar.IKInfo info)
        {
            Transform t = info.guideObject.transform;
            Vector3 fwd = t.forward;

            // 앞 / 뒤 (Pitch)
            Vector3 fwdYZ = Vector3.ProjectOnPlane(fwd, Vector3.right).normalized;
            float pitch = Vector3.SignedAngle(
                Vector3.forward,
                fwdYZ,
                Vector3.right
            );

            // 좌 / 우 (sideZ)
            Vector3 right = t.right;

            Vector3 rightXY = Vector3.ProjectOnPlane(right, Vector3.forward).normalized;

            float sideZ = Vector3.SignedAngle(
                Vector3.right,
                rightXY,
                Vector3.forward
            );

            PositionData data = new PositionData(info.guideObject.transform.rotation, pitch, sideZ);
            return data;
        }


        internal static PositionData GetBoneRotationFromTF(Transform t)
        {
            Vector3 fwd = t.forward;

            // 앞 / 뒤 (Pitch)
            Vector3 fwdYZ = Vector3.ProjectOnPlane(fwd, Vector3.right).normalized;
            float pitch = Vector3.SignedAngle(
                Vector3.forward,
                fwdYZ,
                Vector3.right
            );

            // 좌 / 우 (sideZ)
            Vector3 right = t.right;

            Vector3 rightXY = Vector3.ProjectOnPlane(right, Vector3.forward).normalized;

            float sideZ = Vector3.SignedAngle(
                Vector3.right,
                rightXY,
                Vector3.forward
            );

            PositionData data = new PositionData(t.rotation, pitch, sideZ);
            return data;
        }

        internal static PositionData GetBoneRotationFromFK(OCIChar.BoneInfo info)
        {
            Transform t = info.guideObject.transform;
            Vector3 fwd = t.forward;

            // 앞 / 뒤 (Pitch)
            Vector3 fwdYZ = Vector3.ProjectOnPlane(fwd, Vector3.right).normalized;
            float pitch = Vector3.SignedAngle(
                Vector3.forward,
                fwdYZ,
                Vector3.right
            );

            // 좌 / 우 (sideZ)
            Vector3 right = t.right;

            Vector3 rightXY = Vector3.ProjectOnPlane(right, Vector3.forward).normalized;

            float sideZ = Vector3.SignedAngle(
                Vector3.right,
                rightXY,
                Vector3.forward
            );

            PositionData data = new PositionData(info.guideObject.transform.rotation, pitch, sideZ);
            return data;
        }

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

        internal static RealHumanData AllocateBumpMap(ChaControl charControl, RealHumanData realHumanData)
        {
            Texture2D headOriginTexture = null;
            Texture2D bodyOriginTexture = null;
#if FEATURE_FACEBUMP_SUPPORT  
            if (realHumanData.m_skin_head.GetTexture(realHumanData.head_bumpmap_name) as Texture2D == null)
                headOriginTexture = ConvertToTexture2D(realHumanData.m_skin_head.GetTexture(realHumanData.head_bumpmap_name) as RenderTexture);
            else 
                headOriginTexture = realHumanData.m_skin_head.GetTexture(realHumanData.head_bumpmap_name) as Texture2D;
#endif
            if (realHumanData.m_skin_body.GetTexture(realHumanData.body_bumpmap_name) as Texture2D == null)
                bodyOriginTexture = ConvertToTexture2D(realHumanData.m_skin_body.GetTexture(realHumanData.body_bumpmap_name) as RenderTexture);
            else
                bodyOriginTexture = realHumanData.m_skin_body.GetTexture(realHumanData.body_bumpmap_name) as Texture2D;
#if FEATURE_FACEBUMP_SUPPORT  
            realHumanData.headOriginTexture = SetTextureSize(MakeWritableTexture(headOriginTexture), RealHumanSupport._self._faceExpressionFemaleBumpMap2.width, RealHumanSupport._self._faceExpressionFemaleBumpMap2.height);
#endif            
            realHumanData.bodyOriginTexture = SetTextureSize(MakeWritableTexture(bodyOriginTexture), RealHumanSupport._self._bodyStrongFemale_A_BumpMap2.width, RealHumanSupport._self._bodyStrongFemale_A_BumpMap2.height);
    
            return realHumanData;
        }

        internal static RealHumanData GetMaterials(ChaControl charCtrl, RealHumanData realHumanData)
        {
            //UnityEngine.Debug.Log($">> GetMaterials {charCtrl}");
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
                        Texture mainTex = realHumanData.m_skin_body.mainTexture;

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
                        Texture mainTex = realHumanData.m_skin_head.mainTexture;

                        // UnityEngine.Debug.Log($">> mainTex.isReadable {mainTex.isReadable}");

                        // SaveAsPNG(CaptureMaterialOutput(realHumanData.m_skin_head, 2048, 2048), "c:/Temp/face_mainTex.png");
                    }
                    else if (name.Contains("c_m_eye_01") || name.Contains("c_m_eye_02"))
                    {
                        realHumanData.c_m_eye.Add(mat);
                    }
                    else if (name.Contains("c_m_eye_namida")) {
                        realHumanData.m_tear_eye = mat;
                    }
                }
            }

            return realHumanData;
        }

        internal static  BArea InitBArea(float x, float y, float radiusX, float radiusY, float bumpBooster=0.3f)
        {
            return new BArea
            {
                X = x,
                Y = y,
                RadiusX = radiusX,
                RadiusY = radiusY,
                BumpBooster = bumpBooster,     
                Strong = Remap(bumpBooster, 0.0f, 1.0f, 1.0f, 1.5f)
            };
        }

        internal static Transform FindHairTip(Transform hairRoot)
        {
            Transform tip = hairRoot;
            float maxDist = 0f;

            foreach (Transform t in hairRoot.GetComponentsInChildren<Transform>(true))
            {
                float d = Vector3.Distance(hairRoot.position, t.position);
                if (d > maxDist)
                {
                    maxDist = d;
                    tip = t;
                }
            }
            return tip;
        }

        internal static RealHumanData InitRealHumanData(ChaControl chaCtrl, RealHumanData realHumanData)
        {
#if FEATURE_FIX_EXISTING
            SyncExistingFeatures(charCtrl);
#endif
            {
                realHumanData.charControl = chaCtrl;

                realHumanData.c_m_eye.Clear();

                realHumanData = GetMaterials(chaCtrl, realHumanData);

                // UnityEngine.Debug.Log($">> realHumanData.m_skin_body {realHumanData.m_skin_body}");

                if (realHumanData.m_skin_body != null && realHumanData.m_skin_body.GetTexture("_BumpMap2") != null)
                {
                    realHumanData.body_bumpmap_name = "_BumpMap2";
                } 
                else if (realHumanData.m_skin_body != null && realHumanData.m_skin_body.GetTexture("_BumpMap") != null)
                {
                    realHumanData.body_bumpmap_name = "_BumpMap";
                }
                else
                {
                    realHumanData.body_bumpmap_name = "";
                }
                
                if (realHumanData.m_skin_head != null && realHumanData.m_skin_head.GetTexture("_BumpMap2") != null)
                {
                    realHumanData.head_bumpmap_name = "_BumpMap2";
                }
                else if (realHumanData.m_skin_head != null && realHumanData.m_skin_head.GetTexture("_BumpMap") != null)
                {
                    realHumanData.head_bumpmap_name = "_BumpMap";
                }
                else
                {
                    realHumanData.head_bumpmap_name = "";
                }

                if (!realHumanData.body_bumpmap_name.Contains("_BumpMap2"))
                    return null;
                else
                {
                    realHumanData.hairDynamicBones.Clear();
                    realHumanData.hairTipCache.Clear();

                    string bone_prefix_str = "cf_";
                    if (chaCtrl.sex == 0)
                        bone_prefix_str = "cm_";

                    realHumanData.root_bone = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Root");
                    realHumanData.head_bone = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Head");
                    realHumanData.neck_bone = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Neck");      

                    if (realHumanData.head_bone)
                    {
                        DynamicBone[] hairbones = realHumanData.head_bone.GetComponentsInChildren<DynamicBone>(true);  
                    
                        realHumanData.hairDynamicBones = hairbones.ToList();

                        foreach (DynamicBone bone in hairbones)
                        {
                            if (bone == null)
                                continue;

                            if (!realHumanData.hairTipCache.ContainsKey(bone))
                            {
                                Transform tip = FindHairTip(bone.transform);
                                realHumanData.hairTipCache.Add(bone, tip);
                            }
                        }
                    }

                    if (StudioAPI.InsideStudio) {
                        OCIChar ociChar = chaCtrl.GetOCIChar();

                        realHumanData = AllocateBumpMap(chaCtrl, realHumanData);
                        
                        foreach (OCIChar.BoneInfo bone in ociChar.listBones)
                        {
                            if (bone.guideObject != null && bone.guideObject.transformTarget != null) {
                                if(bone.guideObject.transformTarget.name.Contains("_J_Spine01"))
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
                                // else if (bone.guideObject.transformTarget.name.Contains("_J_Hand_L"))
                                // {
                                //     realHumanData.fk_left_hand_bone = bone;
                                // }
                                // else if (bone.guideObject.transformTarget.name.Contains("_J_Hand_R"))
                                // {
                                //     realHumanData.fk_right_hand_bone = bone;
                                // }
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
                                // else if (bone.guideObject.transformTarget.name.Contains("_J_Toes01_L"))
                                // {
                                //     realHumanData.fk_left_toes_bone = bone;
                                // }   
                                // else if (bone.guideObject.transformTarget.name.Contains("_J_Toes01_R"))
                                // {
                                //     realHumanData.fk_right_toes_bone = bone;
                                // } 
                            }
                        }

                        // if (chaCtrl.sex == 1) {
                        //     realHumanData.lk_left_foot_bone = ociChar.listIKTarget[9]; // left foot
                        //     realHumanData.lk_right_foot_bone = ociChar.listIKTarget[12]; // right foot
                        // }
                    }
                }   
            }         

            return realHumanData;
        }

#if FEATURE_FIX_EXISTING
        internal static void SyncExistingFeatures(ChaControl chaCtrl) {            
            string bone_prefix_str = "cf_";
            if (chaCtrl.sex == 0)
                bone_prefix_str = "cm_";
            
            // 기존 dynamic collider 오류 교정
            Transform spine02_s = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Spine02_s");
            if (spine02_s != null)
            {
                DynamicBoneCollider [] dbcs = spine02_s.GetComponentsInChildren<DynamicBoneCollider>(true);  

                foreach (DynamicBoneCollider dbc in dbcs)
                {
                    dbc.m_Radius    = 1.30f * RealHumanSupport.ExtraBoneScale.Value;
                    dbc.m_Height    = 2.65f * RealHumanSupport.ExtraBoneScale.Value;
                }
            }

            Transform kosi02_hit = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"hit_Kosi02_s");
            if (kosi02_hit != null)
            {
                DynamicBoneCollider [] dbcs = kosi02_hit.GetComponentsInChildren<DynamicBoneCollider>(true);

                kosi02_hit.transform.localPosition = new Vector3(0, 0.2f, 0);
                foreach (DynamicBoneCollider dbc in dbcs)
                {
                    dbc.m_Radius    = 1.0f * RealHumanSupport.ExtraBoneScale.Value;
                    dbc.m_Height    = 3.6f * RealHumanSupport.ExtraBoneScale.Value;
                }
            }
        }
#endif

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


        internal static void SupportEyeFastBlinkEffect(ChaControl chaCtrl, RealHumanData realHumanData) {
            if (chaCtrl.fbsCtrl != null)
                chaCtrl.fbsCtrl.BlinkCtrl.BaseSpeed = 0.05f; // 작을수록 blink 속도가 높아짐..

        }

#if FEATURE_FACEBUMP_SUPPORT
        internal static void SupportFaceBumpEffect(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            if (realHumanData.m_skin_head == null)
                return;

            if (RealHumanSupport.FaceBlendingActive.Value)
            {
                Texture2D origin_texture = realHumanData.headOriginTexture;
                Texture2D express_texture = null;

                if (chaCtrl.sex == 1) // female
                {
                    express_texture = RealHumanSupport._self._faceExpressionFemaleBumpMap2;
                }
                else
                {
                    express_texture = RealHumanSupport._self._faceExpressionMaleBumpMap2;
                }

                List<BArea> areas = new List<BArea>();

                RealFaceData realFaceData = null;

                if (RealHumanSupport._self._faceMouthMgmt.TryGetValue(RealHumanSupport._self._mouth_type, out realFaceData))
                {
                    foreach(BArea area in realFaceData.areas)
                    {
                        areas.Add(area);
                    }
                }

                if (RealHumanSupport._self._faceEyesMgmt.TryGetValue(RealHumanSupport._self._eye_type, out realFaceData))
                {
                    foreach(BArea area in realFaceData.areas)
                    {
                        areas.Add(area);
                    }
                }

                if (origin_texture != null)
                {
                    int kernel = RealHumanSupport._self._mergeComputeShader.FindKernel("CSMain");
                    int w = 1024;
                    int h = 1024;

                    // RenderTexture 초기화 및 재사용
                    if (RealHumanSupport._self._head_rt == null || RealHumanSupport._self._head_rt.width != w || RealHumanSupport._self._head_rt.height != h)
                    {
                        if (RealHumanSupport._self._head_rt != null) RealHumanSupport._self._head_rt.Release();
                        RealHumanSupport._self._head_rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                        RealHumanSupport._self._head_rt.enableRandomWrite = true;
                        RealHumanSupport._self._head_rt.Create();
                    }        

                // 영역 데이터가 변경된 경우만 업데이트
                    if (areas.Count > 0)
                    {                         
                        RealHumanSupport._self._head_areaBuffer.SetData(areas.ToArray());
                        // 셰이더 파라미터 설정
                        RealHumanSupport._self._mergeComputeShader.SetInt("Width", w);
                        RealHumanSupport._self._mergeComputeShader.SetInt("Height", h);
                        RealHumanSupport._self._mergeComputeShader.SetInt("AreaCount", areas.Count);
                        RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexA", origin_texture);
                        RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexB", express_texture);
                        RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "Result", RealHumanSupport._self._head_rt);
                        RealHumanSupport._self._mergeComputeShader.SetBuffer(kernel, "Areas", RealHumanSupport._self._head_areaBuffer);

                        // Dispatch 실행
                        RealHumanSupport._self._mergeComputeShader.Dispatch(kernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);

                        // 결과를 바로 Material에 적용 (CPU로 복사 안 함)    
                        realHumanData.m_skin_head.SetTexture(realHumanData.head_bumpmap_name, RealHumanSupport._self._head_rt);                
                    }
                }                
            } 
            else
            {
                realHumanData.m_skin_head.SetTexture(realHumanData.head_bumpmap_name, realHumanData.headOriginTexture);
            }
        }
#endif        

        internal static void SupportBodyBumpEffect(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            // UnityEngine.Debug.Log($">> SupportBodybumpEffect {RealHumanSupport._self._ociCharMgmt.Count}, {realHumanData.m_skin_head}, {realHumanData.m_skin_body}");
            if (realHumanData.m_skin_body == null)
                return;

            if (RealHumanSupport.BodyBlendingActive.Value && StudioAPI.InsideStudio)
            {
                OCIChar ociChar = chaCtrl.GetOCIChar();

                List<BArea> areas = new List<BArea>();

                // PositionData lk_left_foot = GetBoneRotationFromIK(realHumanData.lk_left_foot_bone);
                // PositionData lk_right_foot = GetBoneRotationFromIK(realHumanData.lk_right_foot_bone);
                
                PositionData fk_left_foot = GetBoneRotationFromFK(realHumanData.fk_left_foot_bone);
                PositionData fk_right_foot = GetBoneRotationFromFK(realHumanData.fk_right_foot_bone);

                PositionData fk_left_knee = GetBoneRotationFromFK(realHumanData.fk_left_knee_bone);
                PositionData fk_right_knee = GetBoneRotationFromFK(realHumanData.fk_right_knee_bone);
                
                PositionData fk_left_thigh = GetBoneRotationFromFK(realHumanData.fk_left_thigh_bone);
                PositionData fk_right_thigh = GetBoneRotationFromFK(realHumanData.fk_right_thigh_bone);

                PositionData fk_spine01 = GetBoneRotationFromFK(realHumanData.fk_spine01_bone);
                PositionData fk_spine02 = GetBoneRotationFromFK(realHumanData.fk_spine02_bone);

                PositionData fk_left_shoulder= GetBoneRotationFromFK(realHumanData.fk_left_shoulder_bone);
                PositionData fk_right_shoulder= GetBoneRotationFromFK(realHumanData.fk_right_shoulder_bone);

                PositionData fk_head= GetBoneRotationFromFK(realHumanData.fk_head_bone);
                PositionData fk_neck= GetBoneRotationFromFK(realHumanData.fk_neck_bone);

                Texture2D origin_texture = realHumanData.bodyOriginTexture;
                Texture2D strong_texture = null;

                if (ociChar.charInfo.sex == 1) // female
                {
                    strong_texture = RealHumanSupport._self._bodyStrongFemale_A_BumpMap2;
                }
                else
                {
                    strong_texture = RealHumanSupport._self._bodyStrongMale_A_BumpMap2;
                }

                float left_shin = 0.0f;
                float left_calf_bs = 0.0f;
                float left_butt_bs = 0.0f;
                float left_thigh_ft_bs = 0.0f;
                float left_thigh_bk_bs = 0.0f;
                float left_thigh_inside_bs = 0.0f;
                float left_spine_bs = 0.0f;
                float left_neck_bs = 0.0f;

                float right_shin = 0.0f;
                float right_calf_bs = 0.0f;            
                float right_butt_bs = 0.0f;
                float right_thigh_ft_bs = 0.0f;
                float right_thigh_bk_bs = 0.0f;
                float right_thigh_inside_bs = 0.0f;
                float right_spine_bs = 0.0f;
                float right_neck_bs = 0.0f;

                float spine_bs = 0.0f;            
                float neck_bs = 0.0f;           

                float bumpscale = 0.0f;

                if (ociChar.oiCharInfo.enableFK) {
            // 허벅지 왼쪽
                    if (fk_left_thigh._front > 1.0f)
                    {   // 뒷방향
                        bumpscale = Math.Min(Remap(fk_left_thigh._front, 0.0f, 120.0f, 0.1f, 1.0f), 1.0f);                     
                        left_butt_bs += bumpscale * 0.7f;
                        left_thigh_bk_bs += bumpscale * 0.5f;                         
                    } 
                    else
                    {   // 앞방향
                        float angle = Math.Abs(fk_right_thigh._front);
                        bumpscale = Math.Min(Remap(angle, 0.0f, 120.0f, 0.0f, 1.0f), 1.0f);
                        left_thigh_ft_bs += bumpscale * 0.5f; 
                        if (angle >= 20.0f) {
                            // 내전근 강조 
                            left_thigh_bk_bs += Math.Min(Remap(angle, 20.0f, 120.0f, 0.2f, 1.0f), 1.0f);
                            left_thigh_inside_bs += bumpscale * 0.5f;
                            // 허벅지 open 여부 확인
                        }
                    }
            // 허벅지 오른쪽
                    if (fk_right_thigh._front > 1.0f)
                    {   // 뒷방향      
                        bumpscale = Math.Min(Remap(fk_right_thigh._front, 0.0f, 120.0f, 0.1f, 1.0f), 1.0f);
                        right_butt_bs += bumpscale * 0.7f;
                        right_thigh_bk_bs += bumpscale * 0.5f; 
                    }  
                    else
                    {   // 앞방향
                        float angle = Math.Abs(fk_right_thigh._front);
                        bumpscale = Math.Min(Remap(angle, 0.0f, 120.0f, 0.0f, 1.0f), 1.0f);
                        right_thigh_ft_bs += bumpscale * 0.5f;
                        if (angle >= 20.0f) {      
                            // 내전근 강조                      
                            right_thigh_bk_bs += Math.Min(Remap(angle, 20.0f, 120.0f, 0.2f, 1.0f), 1.0f);
                            right_thigh_inside_bs += bumpscale * 0.5f;
                            // 허벅지 open 여부 확인
                        }
                    }
            // 무릎 왼쪽
                    // 허벅지 기준 무릅이 뒷방향으로 굽힘
                    if (fk_left_knee._front > fk_left_thigh._front) //if (fk_left_knee._front >= 0.0f)
                    { 
                        float angle = GetRelativePosition(fk_left_thigh._front, fk_left_knee._front);
                        bumpscale = Math.Min(Remap(angle, 0.0f, 90.0f, 0.1f, 1.0f), 1.0f);
                        left_butt_bs += bumpscale * 0.3f;
                        left_thigh_bk_bs += bumpscale * 0.3f;
                        left_thigh_ft_bs += bumpscale * 0.3f;
                        
                        if (angle >= 110)  {
                            bumpscale = Math.Min(Remap(angle, 110.0f, 160.0f, 0.2f, 1.0f), 1.0f);
                            left_shin += bumpscale * 0.5f;
                            left_thigh_inside_bs += bumpscale * 0.9f;
                            left_thigh_bk_bs += bumpscale * 0.5f;
                        }
                    }
            // 무릎 오른쪽
                    // 허벅지 기준 무릅이 뒷방향으로 굽힘
                    if (fk_right_knee._front > fk_right_thigh._front)
                    {  
                        float angle = GetRelativePosition(fk_right_thigh._front, fk_right_knee._front);
                        bumpscale = Math.Min(Remap(angle, 0.0f, 90.0f, 0.1f, 1.0f), 1.0f);
                        right_butt_bs += bumpscale * 0.3f;                        
                        right_thigh_ft_bs += bumpscale * 0.3f;
                        right_thigh_bk_bs += bumpscale * 0.3f;

                        if (angle >= 110)  {
                            bumpscale = Math.Min(Remap(angle, 110.0f, 160.0f, 0.2f, 1.0f), 1.0f);
                            right_shin += bumpscale * 0.5f;
                            right_thigh_inside_bs += bumpscale * 0.9f;
                            right_thigh_bk_bs += bumpscale * 0.5f;
                        }
                    }
            // 발목
            //         if (ociChar.oiCharInfo.enableIK) {          
            //             if (lk_left_foot._front > 0.0f)
            //             {   // 뒷방향
            //                 bumpscale = Math.Min(Remap(lk_left_foot._front, 0.0f, 70.0f, 0.1f, 1f), 1f);
            //                 left_shin += bumpscale * 0.3f;
            //                 left_thigh_ft_bs += bumpscale * 0.2f;
            //                 left_thigh_bk_bs += bumpscale * 0.2f;
            //                 left_thigh_inside_bs += bumpscale * 0.2f;
            //                 left_calf_bs += bumpscale * 1.2f;

            //                 // TODO
            //                 // 발목 강조                        
            //             }
            //             if (lk_right_foot._front > 0.0f)
            //             {   // 뒷방향         
            //                 bumpscale = Math.Min(Remap(lk_right_foot._front, 0.0f, 7.0f, 0.1f, 1f), 1f);
            //                 right_shin += bumpscale * 0.3f;
            //                 right_thigh_ft_bs += bumpscale * 0.2f;
            //                 right_thigh_bk_bs += bumpscale * 0.2f;
            //                 right_thigh_inside_bs += bumpscale * 0.2f;
            //                 right_calf_bs += bumpscale * 1.2f;                                           
            //                 // TODO
            //                 // 발목 강조
            //             }
                    //} else 
                    if (ociChar.oiCharInfo.enableFK)
                    {
                        // 무릅 기준 발목이 뒷방향으로 굽힘
                        if (fk_left_foot._front > fk_left_knee._front)
                        {    
                            float angle = GetRelativePosition(fk_left_knee._front, fk_left_foot._front);
                            bumpscale = Math.Min(Remap(angle, 0.0f, 70.0f, 0.1f, 1f), 1f);
                            left_shin += bumpscale * 0.3f;
                            left_thigh_ft_bs += bumpscale * 0.4f;
                            left_thigh_bk_bs += bumpscale * 0.2f;
                            left_calf_bs += bumpscale * 1.2f;                           
                            // TODO
                            // 발목 강조                  
                        }
                        // 무릅 기준 발목이 뒷방향으로 굽힘
                        if (fk_right_foot._front > fk_right_knee._front)
                        {   // 뒷방향      
                            float angle = GetRelativePosition(fk_right_knee._front, fk_right_foot._front);
                            bumpscale = Math.Min(Remap(angle, 0.0f, 70.0f, 0.1f, 1f), 1f);
                            right_shin += bumpscale * 0.3f;
                            right_thigh_ft_bs += bumpscale * 0.4f;
                            right_thigh_bk_bs += bumpscale * 0.2f;
                            right_calf_bs += bumpscale * 1.2f;                 
                            // TODO
                            // 발목 강조
                        }
                    }                

            // 허리
                    if (fk_spine02._front > 1.0f)
                    { 
                        // 앞으로 숙이기
                        bumpscale = Math.Min(Remap(fk_spine02._front, 0.0f, 120.0f, 0.0f, 1.0f), 1.0f);
                        spine_bs = -bumpscale * 1.0f;
                    } 
                    else
                    {   // 뒤로 숙이기
                        bumpscale = Math.Min(Remap(Math.Abs(fk_spine02._front), 0.0f, 70.0f, 0.0f, 1.0f), 1.0f);
                        spine_bs = bumpscale * 0.8f;
                    }

                    if (fk_spine02._side > 1.0f)
                    {   // 왼쪽 기울기                                            
                        bumpscale = Math.Min(Remap(fk_spine02._side, 0.0f, 70.0f, 0.0f, 1.0f), 1.0f);
                        if (left_spine_bs != 0.0f)
                            left_spine_bs += -bumpscale * 0.3f;
                        else
                            left_spine_bs = -bumpscale * 0.3f;           
                    } 
                    else
                    {   // 오른쪽 기울기
                        bumpscale = Math.Min(Remap(Math.Abs(fk_spine02._side), 0.0f, 70.0f, 0.0f, 1.0f), 1.0f);
                        if (right_spine_bs != 0.0f)
                            right_spine_bs += -bumpscale * 0.3f;    
                        else
                            right_spine_bs = -bumpscale * 0.3f; 
                    }

            // 목
                    if (fk_neck._front > fk_head._front)
                    {   // 목 기준 머리가 뒷방향으로 굽힘               
                        float angle = GetRelativePosition(fk_neck._front, fk_head._front);
                        bumpscale = Math.Min(Remap(angle, 0.0f, 50.0f, 0.0f, 1.0f), 1.0f);
                        neck_bs = bumpscale * 0.9f;    
                    } else 
                    {   // 목 기준 머리가 앞방향으로 굽힘
                        float angle = GetRelativePosition(fk_neck._front, fk_head._front);
                        bumpscale = Math.Min(Remap(angle, 0.0f, 70.0f, 0.0f, 1.0f), 1.0f);
                        neck_bs = -bumpscale * 0.9f;                        
                    }

                    if (fk_head._side > 1.0f)
                    {   // 왼쪽 기울기            
                        bumpscale = Math.Min(Remap(fk_head._side, 0.0f, 90.0f, 0.0f, 1.0f), 1.0f);    
                        left_neck_bs += bumpscale * 0.3f;
                    } else
                    {   // 오른쪽 기울기
                        bumpscale = Math.Min(Remap(Math.Abs(fk_head._side), 0.0f, 90.0f, 0.0f, 1.0f), 1.0f);                 
                        right_neck_bs += bumpscale * 0.3f;                             
                    } 
                }
                
          
            // 가운데
                if (neck_bs >= 0.0f)
                {
                    areas.Add(InitBArea(260, 100, 140, 90, Math.Min(Math.Abs(neck_bs), 2.0f))); // 목선
                } else {
                    areas.Add(InitBArea(770, 200, 140, 90, Math.Min(Math.Abs(neck_bs), 2.0f))); // 척추
                }

                if (spine_bs >= 0.0f)
                {
                    areas.Add(InitBArea(260, 530, 250, 200, Math.Min(Math.Abs(spine_bs), 2.0f))); // 갈비                    
                } else {
                    areas.Add(InitBArea(770, 260, 220, 240, Math.Min(Math.Abs(spine_bs), 2.0f))); // 척추
                }

            // 왼쪽 강조
                if (left_neck_bs != 0.0f)
                {
                    areas.Add(InitBArea(220, 90, 35, 80, Math.Min(left_neck_bs, 2.0f))); 
                }
                if (left_spine_bs != 0.0f)
                {
                    areas.Add(InitBArea(150, 520, 145, 160, Math.Min(left_spine_bs, 2.0f)));   
                }                
                if (left_thigh_ft_bs != 0.0f)
                {
                    areas.Add(InitBArea(365, 970, 95, 210, Math.Min(left_thigh_ft_bs, 2.0f))); // 앞 허벅지        
                }                                                       
                if (left_thigh_inside_bs != 0.0f)
                {
                    areas.Add(InitBArea(340, 970, 55, 120, Math.Min(left_thigh_inside_bs, 2.0f))); // 앞 허벅지 인쪽
                }                 
                if (left_shin != 0.0f)
                {
                    areas.Add(InitBArea(400, 1450, 115, 300, Math.Min(left_shin, 2.0f))); // 앞 정강이                                        
                }
                if (left_thigh_bk_bs != 0.0f)
                { 
                    areas.Add(InitBArea(660, 1030, 95, 160, Math.Min(left_thigh_bk_bs, 2.0f))); // 뒷 허벅지                  
                }                                      
                if (left_butt_bs != 0.0f)
                {
                    areas.Add(InitBArea(650, 850, 85, 90, Math.Min(left_butt_bs, 2.0f))); // 뒷 엉덩이                                                
                }        
                if (left_calf_bs != 0.0f)
                {                    
                    areas.Add(InitBArea(670, 1420, 95, 140, Math.Min(left_calf_bs, 2.0f))); // 뒷 종아리 강조                                                     
                }     

            // 오른쪽 강조
                if (right_neck_bs != 0.0f)
                {
                    areas.Add(InitBArea(300, 90, 35, 80, Math.Min(right_neck_bs, 2.0f)));
                }   
                if (right_spine_bs != 0.0f)
                {
                    areas.Add(InitBArea(370, 520, 145, 160, Math.Min(right_spine_bs, 2.0f)));
                }
                if (right_thigh_ft_bs != 0.0f)
                {
                    areas.Add(InitBArea(145, 970, 95, 210, Math.Min(right_thigh_ft_bs, 2.0f))); // 앞 허벅지                         
                }
                if (right_thigh_inside_bs != 0.0f)
                {
                    areas.Add(InitBArea(180, 970, 55, 120, Math.Min(right_thigh_inside_bs, 2.0f))); // 앞 허벅지 안쪽                          
                } 
                if (right_shin != 0.0f)
                {
                    areas.Add(InitBArea(120, 1450, 115, 300, Math.Min(right_shin, 1.2f))); // 앞 정강이                         
                }
                if (right_thigh_bk_bs != 0.0f)
                {
                    areas.Add(InitBArea(900, 1030, 95, 140, Math.Min(right_thigh_bk_bs, 1.5f))); // 뒷 허벅지
                }                    
                if (right_butt_bs != 0.0f)
                {
                    areas.Add(InitBArea(920, 850, 85, 90, Math.Min(right_butt_bs, 1.5f))); // 뒷 엉덩이                        
                }
                if (right_calf_bs != 0.0f)
                {
                    areas.Add(InitBArea(890, 1420, 95, 140, Math.Min(right_calf_bs, 2.0f))); // 뒷 종아리 강조                                                         
                }

                // UnityEngine.Debug.Log($">> right_thigh_inside_bs {right_thigh_inside_bs}");

                //realHumanData.prev_lk_left_foot_rot = lk_left_foot._q;
                //realHumanData.prev_lk_right_foot_rot = lk_right_foot._q;
                realHumanData.prev_fk_left_foot_rot = fk_left_foot._q;
                realHumanData.prev_fk_right_foot_rot = fk_right_foot._q;     
                realHumanData.prev_fk_left_knee_rot = fk_left_knee._q;
                realHumanData.prev_fk_right_knee_rot = fk_right_knee._q;
                realHumanData.prev_fk_left_thigh_rot = fk_left_thigh._q;
                realHumanData.prev_fk_right_thigh_rot = fk_right_thigh._q;
                realHumanData.prev_fk_spine01_rot = fk_spine01._q;
                realHumanData.prev_fk_spine02_rot = fk_spine02._q;
                realHumanData.prev_fk_head_rot = fk_head._q;
                realHumanData.prev_fk_left_shoulder_rot = fk_left_shoulder._q;
                realHumanData.prev_fk_right_shoulder_rot = fk_right_shoulder._q;
           
                if (origin_texture != null)
                {
                    int kernel = RealHumanSupport._self._mergeComputeShader.FindKernel("CSMain");

                    int w = 2048;
                    int h = 2048;
                    // int currentAreaCount = 0;

                    // RenderTexture 초기화 및 재사용
                    if (RealHumanSupport._self._body_rt == null || RealHumanSupport._self._body_rt.width != w || RealHumanSupport._self._body_rt.height != h)
                    {
                        if (RealHumanSupport._self._body_rt != null) RealHumanSupport._self._body_rt.Release();
                        RealHumanSupport._self._body_rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                        RealHumanSupport._self._body_rt.enableRandomWrite = true;
                        RealHumanSupport._self._body_rt.Create();
                    }

                    // 영역 데이터가 변경된 경우만 업데이트
                    if (areas.Count > 0)
                    {        
                        RealHumanSupport._self._body_areaBuffer.SetData(areas.ToArray());
                        // 셰이더 파라미터 설정
                        RealHumanSupport._self._mergeComputeShader.SetInt("Width", w);
                        RealHumanSupport._self._mergeComputeShader.SetInt("Height", h);
                        RealHumanSupport._self._mergeComputeShader.SetInt("AreaCount", areas.Count);
                        RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexA", origin_texture);
                        RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "TexB", strong_texture);
                        RealHumanSupport._self._mergeComputeShader.SetTexture(kernel, "Result", RealHumanSupport._self._body_rt);
                        RealHumanSupport._self._mergeComputeShader.SetBuffer(kernel, "Areas", RealHumanSupport._self._body_areaBuffer);

                        // Dispatch 실행
                        RealHumanSupport._self._mergeComputeShader.Dispatch(kernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);                 
                        
                        realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_name, RealHumanSupport._self._body_rt);

                        // Texture2D merged =  MergeRGBAlphaMaps(origin_texture, strong_texture, areas);    
                        // realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_name, merged);
                        // SaveAsPNG(merged, "./body_merge.png");
                        // SaveAsPNG(strong_texture, "./body_strong.png");
                        // SaveAsPNG(RenderTextureToTexture2D(RealHumanSupport._self._body_rt), "./body_merged.png");                     
                    }
                }
            }
            else
            {
                realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_name, realHumanData.bodyOriginTexture);
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

        internal static void SaveAsPNG(Texture tex, string path)
        {
            if (tex == null)
            {
                UnityEngine.Debug.LogError("Texture is null");
                return;
            }

            Texture2D tex2D = null;

            if (tex is Texture2D t2d)
            {
                tex2D = t2d;
            }
            else if (tex is RenderTexture rt)
            {
                tex2D = RenderTextureToTexture2D(rt);
            }
            else
            {
                UnityEngine.Debug.LogError($"Unsupported texture type: {tex.GetType()}");
                return;
            }

            byte[] bytes = tex2D.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
        }


        internal static Texture2D MakeWritableTexture(Texture texture)
        {
            if (texture == null)
                return null;

            int width = texture.width;
            int height = texture.height;

            RenderTexture rt = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32
                // RenderTextureReadWrite.Linear // 검사
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

        #endregion
    }

    // enum BODY_SHADER
    // {
    //     HANMAN,
    //     DEFAULT
    // }
    
    class RealFaceData
    {
        public List<BArea> areas = new List<BArea>();

        public RealFaceData()
        {
        }
        public RealFaceData(BArea barea)
        {
           this.areas.Add(barea);
        }

        public RealFaceData(BArea barea1, BArea barea2)
        {
            this.areas.Add(barea1);
            this.areas.Add(barea2);
        }

        public RealFaceData(BArea barea1, BArea barea2, BArea barea3)
        {
            this.areas.Add(barea1);
            this.areas.Add(barea2);
            this.areas.Add(barea3);
        }

        public RealFaceData(BArea barea1, BArea barea2, BArea barea3, BArea barea4)
        {
            this.areas.Add(barea1);
            this.areas.Add(barea2);
            this.areas.Add(barea3);
            this.areas.Add(barea4);
        }

        public void Add(BArea area)
        {
            this.areas.Add(area);
        }
    }

    struct BArea
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float RadiusX; // 가로 반지름
        public float RadiusY; // 세로 반지름
        public float Strong; // G/B 방향 강조
        public float BumpBooster; // 범프 세기 강조
    }    
    
    class PositionData
    {
        public Quaternion _q;
        public  float   _front;
        public  float   _side;

        public PositionData(Quaternion q, float front, float side)
        {   
            _q = q;
            _front = front;
            _side = side;
        }        
    }

    class RealHumanData
    {
        public ChaControl   charControl;
        public Coroutine coroutine;

        public string head_bumpmap_name;
        public string body_bumpmap_name;
        
        public DynamicBone_Ver02 rightBoob;
        public DynamicBone_Ver02 leftBoob;
        public DynamicBone_Ver02 rightButtCheek;
        public DynamicBone_Ver02 leftButtCheek;

        public bool shouldTearing;
        public Material m_tear_eye;
        public Material m_skin_head;
        public Material m_skin_body;

        public List<Material> c_m_eye = new List<Material>();

        public Texture2D headOriginTexture;

        public Texture2D bodyOriginTexture;

        public OCIChar.BoneInfo fk_spine01_bone;

        public OCIChar.BoneInfo fk_spine02_bone;

        public OCIChar.BoneInfo fk_head_bone;

        public OCIChar.BoneInfo fk_neck_bone;

        public OCIChar.BoneInfo fk_left_shoulder_bone;

        public OCIChar.BoneInfo fk_right_shoulder_bone;

        public OCIChar.BoneInfo fk_left_thigh_bone;

        public OCIChar.BoneInfo fk_right_thigh_bone;

        public OCIChar.BoneInfo fk_left_knee_bone;

        public OCIChar.BoneInfo fk_right_knee_bone;


        // public OCIChar.BoneInfo fk_left_hand_bone;

        // public OCIChar.BoneInfo fk_right_hand_bone;

        public OCIChar.BoneInfo  fk_right_foot_bone;

        public OCIChar.BoneInfo  fk_left_foot_bone;

        // public OCIChar.BoneInfo  fk_right_toes_bone;

        // public OCIChar.BoneInfo  fk_left_toes_bone;

        // public OCIChar.IKInfo  lk_right_foot_bone;

        // public OCIChar.IKInfo  lk_left_foot_bone;



        public Quaternion  prev_fk_spine01_rot;
        public Quaternion  prev_fk_spine02_rot;

        public Quaternion  prev_fk_head_rot;
        public Quaternion  prev_fk_neck_rot;
        public Quaternion  prev_fk_right_shoulder_rot;
        public Quaternion  prev_fk_left_shoulder_rot;
        public Quaternion  prev_fk_right_thigh_rot;
        public Quaternion  prev_fk_left_thigh_rot;
        public Quaternion  prev_fk_right_knee_rot;
        
        public Quaternion  prev_fk_left_knee_rot;        
        public Quaternion  prev_fk_right_foot_rot;
        public Quaternion  prev_fk_left_foot_rot;        
        // public Quaternion  prev_lk_right_foot_rot;
        // public Quaternion  prev_lk_left_foot_rot;

        public Transform head_bone;

        public Transform neck_bone;

        public Transform root_bone;

        public Transform nose_bridge_s;
        public Transform mouse_l;

        public List<DynamicBone> hairDynamicBones = new List<DynamicBone>();

        public Dictionary<DynamicBone, Transform> hairTipCache = new Dictionary<DynamicBone, Transform>();

        // public BODY_SHADER bodyShaderType = BODY_SHADER.DEFAULT;
        // public BODY_SHADER faceShaderType = BODY_SHADER.DEFAULT;

        // public Transform tf_j_l_foot; // 왼발 발목
        // public Transform tf_j_r_foot; // 오른발 발목

        // public Transform tf_j_l_leg_up; // 왼발 고관절
        // public Transform tf_j_r_leg_up; // 오른발 고관절

        // public Transform tf_j_l_leg_knee; // 왼발 무릎
        // public Transform tf_j_r_leg_knee; // 오른발 무릎

        // public Transform tf_j_neck; // 목
        // public Transform tf_j_head; // 머리

        // public Transform ik_target_l_foot;
        // public Transform ik_target_r_foot;

        // public Transform ik_target_l_thigh;
        // public Transform ik_target_r_thigh;
        
        // public Transform ik_target_l_knee;
        // public Transform ik_target_r_knee;
        // public float ik_weight;


        // test
        public Transform cf_J_LegUp00_L;
        public Transform cf_J_LegLow01_L;

        public Transform cf_J_LegUp00_R;
        public Transform cf_J_LegLow01_R;

        public Vector3 legup00_L_basePos;
        public Vector3 legLow01_L_basePos;
        public Vector3 legup00_R_basePos;
        public Vector3 legLow01_R_basePos;

        public RealHumanData()
        {
        }        
    }

#if FEATURE_STRAPON_SUPPORT  
    public class CapsuleTrigger : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {            
            UnityEngine.Debug.Log("Trigger Enter: " + other.name);
        }

        void OnTriggerStay(Collider other)
        {
            UnityEngine.Debug.Log("Trigger Stay: " + other.name);
        }

        void OnTriggerExit(Collider other)
        {
            UnityEngine.Debug.Log("Trigger Exit: " + other.name);
        }
    }
#endif
}