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
        internal string GetExtraColliderSnapshotKey(Transform root, Transform target)
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

        internal bool TryGetExtraColliderOriginalSnapshot(DynamicBoneCollider collider, out float[] snapshot)
        {
            snapshot = null;

            if (collider == null || realHumanData == null || realHumanData.chaCtrl == null || realHumanData.chaCtrl.objBodyBone == null)
                return false;

            if (realHumanData.extraColliderOriginalSnapshots == null)
                realHumanData.extraColliderOriginalSnapshots = new Dictionary<string, float[]>();

            string key = GetExtraColliderSnapshotKey(realHumanData.chaCtrl.objBodyBone.transform, collider.transform);
            if (string.IsNullOrEmpty(key))
                return false;

            if (realHumanData.extraColliderOriginalSnapshots.TryGetValue(key, out float[] original) &&
                original != null &&
                original.Length >= 6)
            {
                snapshot = CloneSnapshot(original);
                return true;
            }

            float[] captured = CaptureTransformSnapshot(collider.transform);
            realHumanData.extraColliderOriginalSnapshots[key] = CloneSnapshot(captured);
            snapshot = captured;
            return true;
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

        internal void SyncExtraColliderSnapshotsAfterBuild()
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
    }

#if FEATURE_REALPLAY_SUPPORT
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
        private float transitionDuration = 0.2f;
        private bool isTransitionActive = false;

        public ContactResponseProfile ResponseProfile = ContactResponseProfile.Soft;
        public float EnterDuration = 0.55f; // enter: slow -> fast
        public float ExitDuration = 0.15f;  // exit: fast -> slow
        public float GlobalResponseSpeed = 1.0f; // scales all profiles (0.1 ~ 3.0 recommended)
        private const float RootDanEnterScale = 0.85f;
        private const float RootDanExitScale = 1.0f;

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
            _controller.SetBlendShape(currentValue/2, _data.vagina_open_all_outside_idx_in_body);
    #endif
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsValidDriverCollider(other))
                return;

            TryApplyRealPlayStateByCollider(other, true);

            float desiredTarget = GetAdvancedStayTargetByBone(other);
            StartTransition(desiredTarget);

            if (IsPenetrationBone(other))
            {
                TrySetDanBoneScaleFromHit(other, RootDanEnterScale);
                if (_data != null)
                    _data.ActiveRealPlay = true;                
            }
                 
            UnityEngine.Debug.Log("Trigger Enter: " + other.name);
        }

        void OnTriggerStay(Collider other)
        {
            if (!IsValidDriverCollider(other))
                return;

            TryApplyRealPlayStateByCollider(other, true);

            float desiredTarget = GetAdvancedStayTargetByBone(other);
            if (!Mathf.Approximately(targetValue, desiredTarget))
                StartTransition(desiredTarget);
        }

        void OnTriggerExit(Collider other)
        {
            if (!IsValidDriverCollider(other))
                return;

            TryApplyRealPlayStateByCollider(other, false);

            StartTransition(0f);

            if (IsPenetrationBone(other))
            {
                TrySetDanBoneScaleFromHit(other, RootDanExitScale);
                if (_data != null)
                    _data.ActiveRealPlay = false;
            }

            UnityEngine.Debug.Log("Trigger Exit: " + other.name);
        }

        private bool TryApplyRealPlayStateByCollider(Collider other, bool isActive)
        {
            if (_data == null || other == null || other.attachedRigidbody == null)
                return false;

            if (!TryGetRealPlayStrongByColliderName(other.attachedRigidbody.name, out float strong))
                return false;

            _data.ActiveRealPlay = isActive;
            _data.realPlayStrong = isActive ? strong : 1.0f;
            return true;
        }

        private bool TryGetRealPlayStrongByColliderName(string colliderName, out float strong)
        {
            strong = 1.0f;
            if (string.IsNullOrEmpty(colliderName))
                return false;

            if (colliderName.Contains("cm_J_dan119_00"))
            {
                strong = 0.6f;
                return true;
            }

            if (colliderName.Contains("cm_J_dan108_00"))
            {
                strong = 0.9f;
                return true;
            }

            if (colliderName.Contains("cm_J_dan105_00"))
            {
                strong = 1.2f;
                return true;
            }

            if (colliderName.Contains("cm_J_Hand_Index") || colliderName.Contains("cf_J_Hand_Index"))
            {
                strong = 0.3f;
                return true;
            }

            if (colliderName.Contains("cm_J_Hand_Middle") || colliderName.Contains("cf_J_Hand_Middle"))
            {
                strong = 0.8f;
                return true;
            }

            return false;
        }

        private bool TrySetDanBoneScaleFromHit(Collider other, float scale)
        {
            ChaControl hitCha =
                other?.GetComponentInParent<ChaControl>()
                ?? other?.attachedRigidbody?.GetComponentInParent<ChaControl>();

            if (hitCha == null)
                return false;

            RealHumanSupportController controller = hitCha.GetComponent<RealHumanSupportController>();
            if (controller == null)
                return false;

            RealHumanData data = controller.GetRealHumanData();

            if (data == null || data.dan_bone == null)
                return false;

            float clampedScale = Mathf.Max(0.01f, scale);
            data.dan_bone.localScale = new Vector3(clampedScale, clampedScale, clampedScale);
            return true;
        }

        private float GetAdvancedStayTargetByBone(Collider other)
        {
            if (other == null || other.attachedRigidbody == null)
                return 0f;

            string rbName = other.attachedRigidbody.name;

            if (rbName.Contains("cf_J_Hand"))
                return 30f;

            if (rbName.Contains("cm_J_Hand"))
                return 50f;

            if (rbName.Contains("cm_J_dan119_00"))
                return 40f;

            if (rbName.Contains("cm_J_dan108_00"))
                return 80f;

            if (rbName.Contains("cm_J_dan105_00") || rbName.Contains("cm_J_dan100_00"))
                return 100f;

            return 0f;
        }

        private bool IsPenetrationBone(Collider other)
        {
            if (other == null || other.attachedRigidbody == null)
                return false;

            string rbName = other.attachedRigidbody.name;

            if (rbName.Contains("cm_J_dan108_00"))
                return true;

            if (rbName.Contains("cm_J_Hand") || rbName.Contains("cf_J_Hand"))
                return true;

            return false;
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
