// Extra collider snapshot/trigger logic used by RealHumanSupport runtime.
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

/*      
    - replace dan_bone with dan of jointcorrection
    - collaborate with jointcorrection when penetrating    
*/
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
        private RealHumanSupportController _controller;
        private RealHumanData _data;

        private readonly HashSet<Collider> _activeDriverColliders = new HashSet<Collider>();
        private readonly HashSet<Collider> _activePenetrationColliders = new HashSet<Collider>();

        private const float RootDanEnterScale = 0.85f;
        private const float RootDanExitScale = 1.0f;

        public void Bind(RealHumanSupportController controller)
        {
            _controller = controller;
            _data = _controller.GetRealHumanData();
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsValidDriverCollider(other))
                return;

            _activeDriverColliders.Add(other);
            if (IsPenetrationBone(other))
            {
                _activePenetrationColliders.Add(other);
                TrySetDanBoneScaleFromHit(other, RootDanEnterScale);
                _controller?.PlayOneShot("insert", 0.5f, _data.RealPlayBlendShapeTarget, 0);
            }

            RefreshRealPlayState();

            UnityEngine.Debug.Log("Trigger Enter: " + other.name);
        }

        void OnTriggerStay(Collider other)
        {
            if (!IsValidDriverCollider(other))
                return;

            if (!_activeDriverColliders.Contains(other))
                _activeDriverColliders.Add(other);

            if (IsPenetrationBone(other) && !_activePenetrationColliders.Contains(other))
                _activePenetrationColliders.Add(other);

            RefreshRealPlayState();
        }

        void OnTriggerExit(Collider other)
        {
            if (!IsValidDriverCollider(other))
                return;

            _activeDriverColliders.Remove(other);
            _activePenetrationColliders.Remove(other);

            if (_activePenetrationColliders.Count == 0 && IsPenetrationBone(other)) {
                TrySetDanBoneScaleFromHit(other, RootDanExitScale);
                // 여기서 초기화 루틴 호출;
                _controller?.PlayOneShot("remove", 0.5f, _data.RealPlayBlendShapeTarget, 1);
            }

            RefreshRealPlayState();

            UnityEngine.Debug.Log("Trigger Exit: " + other.name);
        }

        private void RefreshRealPlayState()
        {
            if (_data == null)
                return;

            PruneDeadColliders(_activeDriverColliders);
            PruneDeadColliders(_activePenetrationColliders);

            float blendShapeTarget = 0f;
            float strongestPlayStrong = 1.0f;
            bool hasDriver = false;
            bool hasMappedStrong = false;

            foreach (Collider activeCollider in _activeDriverColliders)
            {
                if (activeCollider == null || activeCollider.attachedRigidbody == null)
                    continue;

                hasDriver = true;
                blendShapeTarget = Mathf.Max(blendShapeTarget, GetRealPlayBlendShapeValueByBone(activeCollider));

                if (TryGetRealPlayStrongByColliderName(activeCollider.attachedRigidbody.name, out float strong))
                {
                    if (!hasMappedStrong)
                    {
                        strongestPlayStrong = strong;
                        hasMappedStrong = true;
                    }
                    else
                    {
                        strongestPlayStrong = Mathf.Max(strongestPlayStrong, strong);
                    }
                }
            }

            _data.RealPlayStrong = hasDriver ? strongestPlayStrong : 1.0f;
            _data.RealPlayBlendShapeTarget = hasDriver ? Mathf.Clamp(blendShapeTarget, 0f, 100f) : 0f;
        }

        private static void PruneDeadColliders(HashSet<Collider> colliders)
        {
            if (colliders == null || colliders.Count == 0)
                return;

            colliders.RemoveWhere(collider => collider == null);
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

        private float GetRealPlayBlendShapeValueByBone(Collider other)
        {
            if (other == null || other.attachedRigidbody == null)
                return 0f;

            string rbName = other.attachedRigidbody.name;

            if (rbName.Contains("_Index"))
                return 40f;

            if (rbName.Contains("_Middle"))
                return 40f;

            if (rbName.Contains("_Hand_L") || rbName.Contains("_Hand_R"))
                return 100f;

            if (rbName.Contains("_dan"))
                return 50f;

            return 0f;
        }

        private bool TryGetRealPlayStrongByColliderName(string colliderName, out float strong)
        {
            strong = 1.0f;
            if (string.IsNullOrEmpty(colliderName))
                return false;

            if (colliderName.Contains("_dan119_00"))
            {
                strong = 0.7f;
                return true;
            }
            else if (colliderName.Contains("_dan108_00"))
            {
                strong = 1.3f;
                return true;
            }
            else if (colliderName.Contains("_dan105_00"))
            {
                strong = 1.7f;
                return true;
            }
            else if (colliderName.Contains("_J_Hand")) {

                if (colliderName.Contains("_Index02"))
                {
                    strong = 0.6f;
                    return true;
                }                
                else if (colliderName.Contains("_Index01"))
                {
                    strong = 0.9f;
                    return true;
                }                
                else if (colliderName.Contains("_Middle02"))
                {
                    strong = 0.7f;
                    return true;
                }
                else if (colliderName.Contains("_Middle01"))
                {
                    strong = 1.3f;
                    return true;
                }
            }

            return false;
        }

        private bool IsPenetrationBone(Collider other)
        {
            if (other == null || other.attachedRigidbody == null)
                return false;

            string rbName = other.attachedRigidbody.name;

            if (rbName.Contains("_dan108_00") || rbName.Contains("_Index02") || rbName.Contains("_Middle02"))
                return true;

            return false;
        }

        private bool IsValidDriverCollider(Collider other)
        {
            if (other == null || other.attachedRigidbody == null)
                return false;

            return other.attachedRigidbody.name.StartsWith("RG_");
        }

    }
    #endif    
}
