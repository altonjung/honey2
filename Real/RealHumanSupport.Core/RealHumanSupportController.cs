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

        internal bool IsExtraColliderModified(DynamicBoneCollider collider)
        {
            if (collider == null || realHumanData == null || realHumanData.chaCtrl == null || realHumanData.chaCtrl.objBodyBone == null)
                return false;

            string key = GetExtraColliderSnapshotKey(realHumanData.chaCtrl.objBodyBone.transform, collider.transform);
            if (string.IsNullOrEmpty(key))
                return false;

            if (realHumanData.extraColliderOriginalSnapshots == null)
                realHumanData.extraColliderOriginalSnapshots = new Dictionary<string, float[]>();

            if (!realHumanData.extraColliderOriginalSnapshots.TryGetValue(key, out float[] original))
            {
                original = CaptureTransformSnapshot(collider.transform);
                realHumanData.extraColliderOriginalSnapshots[key] = CloneSnapshot(original);
            }

            float[] current = CaptureTransformSnapshot(collider.transform);
            return !AreSnapshotsEqual(original, current);
        }

        internal void TrackExtraColliderCurrentSnapshot(DynamicBoneCollider collider)
        {
            if (collider == null || realHumanData == null || realHumanData.chaCtrl == null || realHumanData.chaCtrl.objBodyBone == null)
                return;

            if (realHumanData.extraColliderCurrentSnapshots == null)
                realHumanData.extraColliderCurrentSnapshots = new Dictionary<string, float[]>();

            string key = GetExtraColliderSnapshotKey(realHumanData.chaCtrl.objBodyBone.transform, collider.transform);
            if (string.IsNullOrEmpty(key))
                return;

            realHumanData.extraColliderCurrentSnapshots[key] = CaptureTransformSnapshot(collider.transform);
        }

        internal void ResetAllExtraColliderTransformsToOriginal(List<DynamicBoneCollider> colliders)
        {
            if (colliders == null || colliders.Count == 0 || realHumanData == null || realHumanData.chaCtrl == null || realHumanData.chaCtrl.objBodyBone == null)
                return;

            Transform root = realHumanData.chaCtrl.objBodyBone.transform;

            for (int i = 0; i < colliders.Count; i++)
            {
                DynamicBoneCollider collider = colliders[i];
                if (collider == null)
                    continue;

                string key = GetExtraColliderSnapshotKey(root, collider.transform);
                if (string.IsNullOrEmpty(key))
                    continue;

                if (realHumanData.extraColliderOriginalSnapshots != null &&
                    realHumanData.extraColliderOriginalSnapshots.TryGetValue(key, out float[] original) &&
                    TryApplySnapshot(collider.transform, original))
                {
                    TrackExtraColliderCurrentSnapshot(collider);
                    continue;
                }

                collider.transform.localPosition = Vector3.zero;
                collider.transform.localScale = Vector3.one;
                TrackExtraColliderCurrentSnapshot(collider);
            }
        }

        private void SyncExtraColliderSnapshotsAfterBuild()
        {
            if (realHumanData == null || realHumanData.chaCtrl == null || realHumanData.chaCtrl.objBodyBone == null || realHumanData.extraBodyColliders == null)
                return;

            if (realHumanData.extraColliderOriginalSnapshots == null)
                realHumanData.extraColliderOriginalSnapshots = new Dictionary<string, float[]>();
            if (realHumanData.extraColliderCurrentSnapshots == null)
                realHumanData.extraColliderCurrentSnapshots = new Dictionary<string, float[]>();

            Transform root = realHumanData.chaCtrl.objBodyBone.transform;

            for (int i = 0; i < realHumanData.extraBodyColliders.Count; i++)
            {
                DynamicBoneCollider collider = realHumanData.extraBodyColliders[i];
                if (collider == null)
                    continue;

                string key = GetExtraColliderSnapshotKey(root, collider.transform);
                if (string.IsNullOrEmpty(key))
                    continue;

                if (!realHumanData.extraColliderOriginalSnapshots.ContainsKey(key))
                    realHumanData.extraColliderOriginalSnapshots[key] = CaptureTransformSnapshot(collider.transform);
            }

            realHumanData.extraColliderCurrentSnapshots.Clear();
            for (int i = 0; i < realHumanData.extraBodyColliders.Count; i++)
            {
                DynamicBoneCollider collider = realHumanData.extraBodyColliders[i];
                if (collider == null)
                    continue;

                string key = GetExtraColliderSnapshotKey(root, collider.transform);
                if (string.IsNullOrEmpty(key))
                    continue;

                realHumanData.extraColliderCurrentSnapshots[key] = CaptureTransformSnapshot(collider.transform);
            }
        }

        private static bool AreSnapshotsEqual(float[] a, float[] b, float epsilon = 0.0001f)
        {
            if (a == null || b == null || a.Length < 6 || b.Length < 6)
                return false;

            for (int i = 0; i < 6; i++)
            {
                if (Mathf.Abs(a[i] - b[i]) > epsilon)
                    return false;
            }

            return true;
        }

        private static float[] CloneSnapshot(float[] snapshot)
        {
            if (snapshot == null || snapshot.Length < 6)
                return new float[6];

            float[] clone = new float[6];
            Array.Copy(snapshot, clone, 6);
            return clone;
        }

        private static float[] CaptureTransformSnapshot(Transform tr)
        {
            if (tr == null)
                return new float[6];

            return new[]
            {
                tr.localPosition.x, tr.localPosition.y, tr.localPosition.z,
                tr.localScale.x, tr.localScale.y, tr.localScale.z
            };
        }

        private static bool TryApplySnapshot(Transform tr, float[] snapshot)
        {
            if (tr == null || snapshot == null || snapshot.Length < 6)
                return false;

            tr.localPosition = new Vector3(snapshot[0], snapshot[1], snapshot[2]);
            tr.localScale = new Vector3(snapshot[3], snapshot[4], snapshot[5]);
            return true;
        }

        private static string GetExtraColliderSnapshotKey(Transform root, Transform target)
        {
            if (root == null || target == null)
                return null;

            if (root == target)
                return string.Empty;

            Stack<string> path = new Stack<string>();
            Transform current = target;

            while (current != null && current != root)
            {
                path.Push(current.name);
                current = current.parent;
            }

            if (current != root)
                return target.name;

            return string.Join("/", path.ToArray());
        }

        internal void SupportExtraDynamicBones(ChaControl chaCtrl, RealHumanData realHumanData)
        {
            if (chaCtrl.sex == 0)
                return;

            string bone_prefix_str = "cf_";
            // if(chaCtrl.sex == 0)
            //     bone_prefix_str = "cm_";

            // Comment normalized to English.
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

            // Comment normalized to English.
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
            // Comment normalized to English.
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

            extraBodyColliders.AddRange(extraHandsColliders); // Comment normalized to English.

            Transform faceObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_FaceLow_s");
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(faceObject, DynamicBoneColliderBase.Direction.Y, 0.65f, 2.5f, new Vector3(0.0f, 0.0f, 0.3f)));

            // Comment normalized to English.
            Transform leftBoobObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Mune01_L");
            Transform rightBoobObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Mune01_R");
            
            float boob_radius = 0.4f;

            extraBodyColliders.Add(AddExtraDynamicBoneCollider(leftBoobObject, DynamicBoneColliderBase.Direction.Y, boob_radius, boob_radius * 3.0f , new Vector3(0.0f, 0.0f, 0.0f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(rightBoobObject, DynamicBoneColliderBase.Direction.Y, boob_radius, boob_radius * 3.0f, new Vector3(0.0f, 0.0f, 0.0f)));

            // Comment normalized to English.
            Transform leftShoulderObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_ArmUp00_L");
            Transform rightShoulderObject = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_ArmUp00_R");

            float shoulder_radius = 0.4f;

            extraBodyColliders.Add(AddExtraDynamicBoneCollider(leftShoulderObject, DynamicBoneColliderBase.Direction.Y, shoulder_radius, shoulder_radius * 3.0f , new Vector3(0.0f, 0.0f, 0.0f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(rightShoulderObject, DynamicBoneColliderBase.Direction.Y, shoulder_radius, shoulder_radius * 3.0f, new Vector3(0.0f, 0.0f, 0.0f)));

            // Comment normalized to English.
            Transform spine1Object = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Spine01");
            Transform spine2Object = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Spine02");
            Transform spine3Object = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Spine03");
   
            float spine1_radius = 0.8f;
            float spine2_radius = 0.9f;
            float spine3_radius = 0.8f;
   
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(spine1Object, DynamicBoneColliderBase.Direction.Y, spine1_radius, spine1_radius * 4.0f, new Vector3(0.0f, 0.0f, 0.0f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(spine2Object, DynamicBoneColliderBase.Direction.Y, spine2_radius, spine2_radius * 3.5f, new Vector3(0.0f, 0.0f, 0.2f)));
            extraBodyColliders.Add(AddExtraDynamicBoneCollider(spine3Object, DynamicBoneColliderBase.Direction.X, spine3_radius, spine3_radius * 4.0f, new Vector3(0.0f, 0.0f, 0.0f)));

            // Comment normalized to English.
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

            // Comment normalized to English.
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

        internal static Texture2D RenderTextureToTexture2D(RenderTexture rt)
        {
            if (rt == null) return null;

            // Comment normalized to English.
            RenderTexture prev = RenderTexture.active;

            // Comment normalized to English.
            RenderTexture.active = rt;

            // Comment normalized to English.
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);

            // Comment normalized to English.
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            // Comment normalized to English.
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

            // Comment normalized to English.
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
                // Comment normalized to English.
                return rexture;
            }

            // Comment normalized to English.
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
            // Comment normalized to English.
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
            // Comment normalized to English.
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
            // Comment normalized to English.
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
            // Comment normalized to English.
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

            // Comment normalized to English.
            Transform[] allChildren = parent.GetComponentsInChildren<Transform>(true);

            foreach (Transform tr in allChildren)
            {
                // Comment normalized to English.
                if (!tr.name.EndsWith("_ExtDBoneCollider")) continue;

                // Comment normalized to English.
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
                        else if (name.Contains("Breast Nipple Press Left")) 
                        {

                        }
                        else if (name.Contains("Breast Nipple Press Right")) 
                        {

                        }
                        else if (name.Contains("Anus Open Large 3")) 
                        {
                            realHumanData.anus_open_idx_in_body = idx;
                        }
                        else if (name.Contains("Anus Pull Out"))
                        {
                            realHumanData.anus_pullout_idx_in_body = idx;
                        }
                        else if (name.Contains("Vagina Up")) // 50 까지
                        {
                            realHumanData.vagina_up_idx_in_body = idx; // collider 유형에 따라 깊이 조정
                        }                                                
                        else if (name.Contains("Vagina Open Front"))
                        {
                            realHumanData.vagina_open_front_idx_in_body = idx; // 100 까지
                        }                                                
                        else if (name.Contains("Vagina Open All Outside"))
                        {
                            realHumanData.vagina_open_all_outside_idx_in_body = idx; // 30 까지 
                        }
                        else if (name.Contains("Vagina Open Squeeze"))
                        {
                            realHumanData.vagina_open_squeeze_idx_in_body = idx; // 주기적 처리 0  ~ 100 
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

#if FEATURE_STRAPON_SUPPORT
        // Comment normalized to English.
        internal void SetRigidBodyOnObject(string boneName)
        {
            // UnityEngine.Debug.Log($">> SetRigidBodyOnObject {boneName}");

            if (realHumanData != null)
            {
                // string bone_prefix_str = "cf_";
                // if (realHumanData.chaCtrl.sex == 0)
                //     bone_prefix_str = "cm_";

                Transform danObject = realHumanData.chaCtrl.objBodyBone.transform.FindLoop(boneName);

                if (danObject != null)
                {
                    string childName = "RGRigidBody_" + boneName;
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

                    // Comment normalized to English.
                    CapsuleCollider capsule = childObj.GetComponent<CapsuleCollider>();
                    if (capsule == null)
                    {
                        capsule = childObj.AddComponent<CapsuleCollider>();
                        capsule.radius = 0.2f;      // Comment normalized to English.
                        capsule.height = 0.8f;
                        capsule.direction = 0;
                        capsule.center = Vector3.zero;
                        capsule.isTrigger = false;  // Comment normalized to English.
                    }

                    UnityEngine.Debug.Log($">> created rigidBody + collider on {boneName}");
                } else {
                    UnityEngine.Debug.Log($">> failed created rigidBody + collider on {boneName}");
                }
            } else {
                UnityEngine.Debug.Log($">> failed created rigidBody + collider on {boneName}");
            }
        }

        // Comment normalized to English.
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
            // Comment normalized to English.
            Quaternion localRot = t.localRotation;

            Vector3 localEuler = localRot.eulerAngles;

            // Comment normalized to English.
            float Normalize(float angle)
            {
                if (angle > 180f)
                    angle -= 360f;
                return angle;
            }

            float frontback = Normalize(localEuler.x);  // Comment normalized to English.
            float leftright = Normalize(localEuler.z);  // Comment normalized to English.

            PositionData data = new PositionData(
                t.rotation,     // Comment normalized to English.
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
        // internal static PositionData GetBoneRotationFromFK(OCIChar.BoneInfo info)
        // {
        //     Transform t = info.guideObject.transform;

        // Comment normalized to English.
        //     Quaternion localRot = t.localRotation;

        //     Vector3 localEuler = localRot.eulerAngles;

        // Comment normalized to English.
        //     float Normalize(float angle)
        //     {
        //         if (angle > 180f)
        //             angle -= 360f;
        //         return angle;
        //     }

        // Comment normalized to English.
        // Comment normalized to English.

        //     PositionData data = new PositionData(
        // Comment normalized to English.
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
            if (realHumanData.chaCtrl != null)
            {
                if (realHumanData.chaCtrl.sex == 1) {
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
                                        realHumanData.fk_hip_bone = bone; // Comment normalized to English.
                                    }
                                    else if (bone.guideObject.transformTarget.name.Contains("_J_Spine01"))
                                    {
                                        realHumanData.fk_spine01_bone = bone; // Comment normalized to English.
                                    }
                                    else if(bone.guideObject.transformTarget.name.Contains("_J_Spine02"))
                                    {
                                        realHumanData.fk_spine02_bone = bone; // Comment normalized to English.
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
                    SyncExtraColliderSnapshotsAfterBuild();
                    SupportBlendShapes(realHumanData.chaCtrl, realHumanData);
    #if FEATURE_TEARDROP_SUPPORT
                    SupportTearDrop(realHumanData.chaCtrl, realHumanData);
    #endif
                    SupportEyeFastBlinkEffect(realHumanData.chaCtrl, realHumanData);

                    if (StudioAPI.InsideStudio) {
                        SupportBodyBumpEffect(realHumanData.chaCtrl, realHumanData);
                        SupportFaceBumpEffect(realHumanData.chaCtrl, realHumanData);
                    }

                    status = Status.RUN;
                    if (realHumanData.coroutine == null) {
                        realHumanData.coroutine = realHumanData.chaCtrl.StartCoroutine(CoroutineProcess(realHumanData));
                    }
                }
                
    #if FEATURE_STRAPON_SUPPORT
                SupportStrapOn(realHumanData.chaCtrl, realHumanData);
    #endif               
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
                return Math.Abs(Math.Abs(a) - Math.Abs(b)); // Comment normalized to English.
            else
                return Math.Abs(Math.Abs(a) + Math.Abs(b)); // Comment normalized to English.
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
            if (chaCtrl.sex == 0){
                SetRigidBodyOnObject("cm_J_dan119_00");                
                SetRigidBodyOnObject("cm_J_dan108_00");
                SetRigidBodyOnObject("cm_J_dan105_00");
                SetRigidBodyOnObject("cm_J_dan100_00");                
            } else {
                SetCollisionOnOnObject("cf_J_Vagina_root");
                // SetCollisionOnOnObject("cf_J_Ana_B");
            }
        }
#endif

        internal static void SupportEyeFastBlinkEffect(ChaControl chaCtrl, RealHumanData realHumanData) 
        {
            if (chaCtrl.fbsCtrl != null)
                chaCtrl.fbsCtrl.BlinkCtrl.BaseSpeed = 0.05f; // Comment normalized to English.
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
                           // Comment normalized to English.
                           float easedBump = (Mathf.Sin(time * Mathf.PI * 3.5f * 2f) + 1f) * 0.5f;

                           float eyeScale = Mathf.Lerp(0.18f, 0.21f, easedBump);
                           mat.SetFloat("_Texture4Rotator", eyeScale);

                           eyeScale = Mathf.Lerp(0.1f, 0.2f, easedBump);
                           mat.SetFloat("_Parallax", eyeScale);
                       }
                   }
                    
                   if (RealHumanSupport.BreathActive.Value)
                   {
                       float sinValue = (Mathf.Sin(time * realHumanData.BreathInterval) + 1f) * 0.5f;
                       float vaginaFrontWeight = sinValue * 30f;
#if FEATURE_BODY_BLENDSHAPE_SUPPORT                      
                       SetBlendShape(vaginaFrontWeight, realHumanData.anus_pullout_idx_in_body);
                       SetBlendShape(vaginaFrontWeight, realHumanData.vagina_up_idx_in_body);
#endif
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

    // Comment normalized to English.
    struct BAreaData
    {  
        public float X { get; set; }
        public float Y { get; set; }
        public float RadiusX; // Comment normalized to English.
        public float RadiusY; // Comment normalized to English.
        public float BumpBooster; // Comment normalized to English.
        public float Padding;   // Comment normalized to English.
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

        // Comment normalized to English.
        public bool coroutine_pause;

        public List<BAreaData> areas = new List<BAreaData>();

        // Comment normalized to English.
        public Transform head_bone;
        public Transform neck_bone;
        public Transform root_bone;  // Comment normalized to English.
        public List<DynamicBone> hairDynamicBones = new List<DynamicBone>(); // Comment normalized to English.
        public List<DynamicBoneCollider> extraBodyColliders = new List<DynamicBoneCollider>();
        public Dictionary<string, float[]> extraColliderOriginalSnapshots = new Dictionary<string, float[]>();
        public Dictionary<string, float[]> extraColliderCurrentSnapshots = new Dictionary<string, float[]>();

        // Comment normalized to English.
        public DynamicBone_Ver02 rightBoob;
        public DynamicBone_Ver02 leftBoob;
        public DynamicBone_Ver02 rightButtCheek;
        public DynamicBone_Ver02 leftButtCheek;

        // Comment normalized to English.
        public Material m_tear_eye;
        public Material m_skin_head;
        public Material m_skin_body;

        public Texture2D headOriginTexture;
        public Texture2D bodyOriginTexture;

        public RenderTexture _head_rt;
        public ComputeBuffer head_areaBuffer;

        public RenderTexture _body_rt;
        public ComputeBuffer body_areaBuffer;

        // Comment normalized to English.
        public string head_bumpmap_type;
        public string body_bumpmap_type;

        // Comment normalized to English.
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

        // Comment normalized to English.
        public PregnancyPlusCharaController pregnancyController;

        // Comment normalized to English.
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
        public int vagina_open_front_idx_in_body;        
        public int vagina_open_all_outside_idx_in_body;
        public int vagina_open_squeeze_idx_in_body;
        public int vagina_up_idx_in_body;                
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
            anus_pullout_idx_in_body = -1;
            vagina_open_front_idx_in_body = -1;
            vagina_open_all_outside_idx_in_body = -1;
            vagina_open_squeeze_idx_in_body = -1;
            vagina_up_idx_in_body = -1;            
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
    public enum ContactResponseProfile
    {
        Natural,
        Soft,
        Tight,
        Sticky,
        Elastic
    }

    private RealHumanSupportController _controller;
    private RealHumanData _data;

    private float currentValue = 0f;
    private float targetValue = 0f;
    private float transitionFrom = 0f;
    private float transitionTo = 0f;
    private float transitionElapsed = 0f;
    private float transitionDuration = 0.5f;
    private bool isTransitionActive = false;

    public ContactResponseProfile ResponseProfile = ContactResponseProfile.Elastic;
    public float EnterDuration = 0.52f; // enter: slow -> fast
        public float ExitDuration = 0.28f;  // exit: fast -> slow
        public float GlobalResponseSpeed = 1.0f; // scales all profiles (0.1 ~ 3.0 recommended)

    public void Bind(RealHumanSupportController controller)
    {
        _controller = controller;
        _data = _controller.GetRealHumanData();
    }

    void Update()
    {
#if FEATURE_BODY_BLENDSHAPE_SUPPORT
        if (_controller == null || _data == null)
            return;

        if (isTransitionActive)
        {
            transitionElapsed += Time.deltaTime;
            float speedScale = Mathf.Clamp(GlobalResponseSpeed, 0.1f, 3.0f);
            float duration = Mathf.Max(0.0001f, transitionDuration / speedScale);
            float t = Mathf.Clamp01(transitionElapsed / duration);

            bool isEntering = transitionTo > transitionFrom;
            float eased = EvaluateProfileCurve(ResponseProfile, t, isEntering);
            currentValue = Mathf.Lerp(transitionFrom, transitionTo, eased);

            if (t >= 1f)
            {
                isTransitionActive = false;
                currentValue = transitionTo;
            }
        }
        else
        {
            currentValue = targetValue;
        }
        
        _controller.SetBlendShape(currentValue, _data.vagina_open_front_idx_in_body);
        
        // _controller.SetBlendShape(currentValue, _data.vagina_open_all_outside_idx_in_body);
#endif
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsValidDriverCollider(other))
            return;

        float desiredTarget = GetAdvancedStayTargetByBone(other);
        StartTransition(desiredTarget);

        UnityEngine.Debug.Log("Trigger Enter: " + other.name);
    }

    void OnTriggerStay(Collider other)
    {
        if (!IsValidDriverCollider(other))
            return;

        float desiredTarget = GetAdvancedStayTargetByBone(other);
        if (!Mathf.Approximately(targetValue, desiredTarget))
            StartTransition(desiredTarget);
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsValidDriverCollider(other))
            return;

        StartTransition(0f);

        UnityEngine.Debug.Log("Trigger Exit: " + other.name);
    }

    private float GetAdvancedStayTargetByBone(Collider other)
    {
        if (other == null || other.attachedRigidbody == null)
            return 0f;

        string rbName = other.attachedRigidbody.name;

        if (rbName.Contains("cm_J_dan119_00"))
            return 30f;

        if (rbName.Contains("cm_J_dan108_00"))
            return 70f;

        if (rbName.Contains("cm_J_dan105_00") || rbName.Contains("cm_J_dan100_00"))
            return 100f;

        return 0f;
    }
    private bool IsValidDriverCollider(Collider other)
    {
        if (other == null || other.attachedRigidbody == null)
            return false;

        return other.attachedRigidbody.name.StartsWith("RGRigidBody_");
    }

    private void StartTransition(float nextTarget)
    {
        nextTarget = Mathf.Clamp(nextTarget, 0f, 100f);
        if (Mathf.Approximately(targetValue, nextTarget) && isTransitionActive)
            return;

        transitionFrom = currentValue;
        transitionTo = nextTarget;
        targetValue = nextTarget;
        transitionElapsed = 0f;
        transitionDuration = (nextTarget > currentValue) ? EnterDuration : ExitDuration;
        isTransitionActive = true;
    }

    private static float EvaluateProfileCurve(ContactResponseProfile profile, float t, bool isEntering)
    {
        t = Mathf.Clamp01(t);

        switch (profile)
        {
            case ContactResponseProfile.Soft:
                return isEntering
                    ? EaseOutQuart(t)
                    : EaseInQuad(t);

            case ContactResponseProfile.Tight:
                return isEntering
                    ? EaseOutExpo(t)
                    : EaseInExpo(t);

            case ContactResponseProfile.Sticky:
                if (isEntering)
                {
                    float baseCurve = EaseOutCubic(t);
                    return Mathf.Lerp(baseCurve, t, 0.18f);
                }
                return Mathf.Pow(t, 1.6f);

            case ContactResponseProfile.Elastic:
                if (isEntering)
                {
                    float baseCurve = EaseOutCubic(t);
                    float micro = Mathf.Sin(t * Mathf.PI * 2f) * (1f - t) * 0.04f;
                    return Mathf.Clamp01(baseCurve + micro);
                }
                return EaseInCubic(t);

            case ContactResponseProfile.Natural:
            default:
                return isEntering
                    ? EaseOutCubic(t) // Comment normalized to English.
                    : EaseInCubic(t); // Comment normalized to English.
        }
    }

    private static float EaseInQuad(float t) => t * t;
    private static float EaseInCubic(float t) => t * t * t;
    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    private static float EaseOutQuart(float t) => 1f - Mathf.Pow(1f - t, 4f);
    private static float EaseInExpo(float t) => (t <= 0f) ? 0f : Mathf.Pow(2f, 10f * (t - 1f));
    private static float EaseOutExpo(float t) => (t >= 1f) ? 1f : 1f - Mathf.Pow(2f, -10f * t);
}
#endif
}
