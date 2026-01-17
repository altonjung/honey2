using Studio;
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

namespace HoneySelect2Maker
{
    public class Logic
    {
        #region Private Methods            

#if FEATURE_IK_INGAME
        internal static void SupportIKOnScene(ChaControl chaCtrl, RealHumanData realHumanData) {
            if (!StudioAPI.InsideStudio && !MakerAPI.InsideMaker) {

                string bone_prefix_str = "cf_";
                if(chaCtrl.sex == 0)
                    bone_prefix_str = "cm_";
                    
                realHumanData.head_ik_data = new HeadIKData();
                realHumanData.l_leg_ik_data = new LegIKData();
                realHumanData.r_leg_ik_data = new LegIKData();

                Transform[] bones = realHumanData.charControl.objAnim.transform.GetComponentsInChildren<Transform>();
  
                // UnityEngine.Debug.Log($">> SupportIKOnScene {headbones.Length}");

                foreach (Transform bone in bones) {
    
                    if (bone.name.Equals(bone_prefix_str+"J_Head"))
                    {
                        realHumanData.head_ik_data.head = bone;
                        realHumanData.head_ik_data.baseHeadRotation = bone.localRotation;
                    }
                    else if (bone.name.Equals(bone_prefix_str + "J_Neck")) {
                        realHumanData.head_ik_data.neck = bone;
                        realHumanData.head_ik_data.baseNeckRotation = bone.localRotation;
                        realHumanData.head_ik_data.proxyCollider = CreateHitProxy(realHumanData.head_ik_data.neck, "Neck");
                    }
                }

                //Transform[] bodybones = realHumanData.charControl.objHead.GetComponentsInChildren<Transform>();

                //foreach (Transform bone in bodybones) {
                //    if (bone.gameObject.name.Contains("_J_Foot01_L"))
                //    {
                //        l_leg_ik_data.foot = bone;
                //    }   
                //    else if (bone.gameObject.name.Contains("_J_Foot01_R"))
                //    {
                //        r_leg_ik_data.foot = bone;
                //    } 
                //    else if (bone.gameObject.name.Contains("_J_LegLow01_L"))
                //    {
                //        l_leg_ik_data.knee = bone;
                //    }
                //    else if (bone.gameObject.name.Contains("_J_LegLow01_R"))
                //    {
                //        r_leg_ik_data.knee = bone;
                //    } 
                //    else if (bone.gameObject.name.Contains("_J_LegUp00_L"))
                //    {
                //        l_leg_ik_data.thigh = bone;
                //    }
                //    else if (bone.gameObject.name.Contains("_J_LegUp00_R"))
                //    {
                //        r_leg_ik_data.thigh = bone;
                //    }                    
                //}
            }
        }

        internal static void SolveLegIK(
            Transform thigh,
            Transform knee,
            Transform foot,
            Transform target,
            float weight) {
    
            Vector3 rootPos = thigh.position;
            Vector3 midPos = knee.position;
            Vector3 endPos = foot.position;
            Vector3 targetPos = target.position;

            float lenUpper = Vector3.Distance(rootPos, midPos);
            float lenLower = Vector3.Distance(midPos, endPos);
            float lenTarget = Vector3.Distance(rootPos, targetPos);

            // 거리 제한
            lenTarget = Mathf.Min(lenTarget, lenUpper + lenLower - 0.0001f);

            // ======================
            // Knee angle (Cosine law)
            // ======================
            float cosKnee =
                (lenUpper * lenUpper + lenLower * lenLower - lenTarget * lenTarget) /
                (2f * lenUpper * lenLower);

            cosKnee = Mathf.Clamp(cosKnee, -1f, 1f);
            float kneeAngle = Mathf.Acos(cosKnee) * Mathf.Rad2Deg;

            // ======================
            // Thigh rotation
            // ======================
            Vector3 dirToTarget = (targetPos - rootPos).normalized;
            Quaternion thighRot = Quaternion.LookRotation(dirToTarget, thigh.up);

            thigh.rotation = Quaternion.Slerp(
                thigh.rotation,
                thighRot,
                weight
            );

            // ======================
            // Knee rotation (local)
            // ======================
            Quaternion kneeRot = Quaternion.Euler(-kneeAngle, 0, 0);

            knee.localRotation = Quaternion.Slerp(
                knee.localRotation,
                kneeRot,
                weight
            );

            // ======================
            // Foot rotation
            // ======================
            foot.rotation = Quaternion.Slerp(
                foot.rotation,
                target.rotation,
                weight
            );
        }

        // ==========================
        // Head / Neck rotation only
        // ==========================
        //internal static void RotateHead(Vector2 mouseDelta)
        //{
        //    float yaw   = mouseDelta.x * 0.15f;
        //    float pitch = -mouseDelta.y * 0.15f;

        //    Vector3 euler = head.localEulerAngles;
        //    euler.x = ClampAngle(euler.x + pitch, -40f, 40f);
        //    euler.y = ClampAngle(euler.y + yaw, -60f, 60f);

        //    head.localEulerAngles = euler;
        //}

        internal static float ClampAngle(float angle, float min, float max)
        {
            angle = Mathf.Repeat(angle + 180f, 360f) - 180f;
            return Mathf.Clamp(angle, min, max);
        }

        internal static GameObject CreateHitProxy(Transform neck, string name, float radius = 0.6f)
        {
            GameObject go = new GameObject($"{name}_HitProxy");

            go.transform.SetParent(neck, false);
            go.transform.localPosition = new Vector3(0.0f, 0.3f, 0.0f);
            go.transform.localRotation = Quaternion.identity;

            SphereCollider col = go.AddComponent<SphereCollider>();
            col.radius = radius; // 목 크기에 맞게 조절
            col.isTrigger = true;

            return go;
            // === Hit Proxy Debug ===
            // GameObject go = new GameObject($"{name}_HitProxy");
            // go.transform.SetParent(neck, false);
            // go.transform.localPosition = new Vector3(0.0f, 0.3f, 0.0f);
            // go.transform.localRotation = Quaternion.identity;
            // go.layer = LayerMask.NameToLayer("Ignore Raycast"); // 선택 사항

            // // === Collider ===
            // SphereCollider col = go.AddComponent<SphereCollider>();
            // col.radius = radius;
            // col.isTrigger = true;

            // // === Visual Sphere ===
            // GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            // visual.name = "Visual";
            // visual.transform.SetParent(go.transform, false);
            // visual.transform.localPosition = Vector3.zero;
            // visual.transform.localRotation = Quaternion.identity;

            // // Sphere는 지름 기준이므로 * 2
            // visual.transform.localScale = Vector3.one * radius * 2f;

            // // 시각용이므로 Collider 제거
            // UnityEngine.Object.Destroy(visual.GetComponent<Collider>());

            // // === Material (녹색 반투명) ===
            // Material mat = new Material(Shader.Find("Unlit/Color"));
            // mat.color = new Color(0f, 1f, 0f, 0.25f); // RGBA
            // visual.GetComponent<MeshRenderer>().material = mat;

            // return go;            
        }

        internal static void UpdateIKHead(RealHumanData realHumanData, float mouseX, float mouseY) {
            // float yaw   = Input.GetAxis("Mouse X") * 2.0f;
            // float pitch = -Input.GetAxis("Mouse Y") * 2.0f;

            realHumanData.head_ik_data.inputEuler.x += -mouseX * 2f; // yaw
            realHumanData.head_ik_data.inputEuler.y += mouseY * 2f; // pitch

            // Clamp (중요)
            realHumanData.head_ik_data.inputEuler.x = Mathf.Clamp(realHumanData.head_ik_data.inputEuler.x, -60f, 60f);
            realHumanData.head_ik_data.inputEuler.y = Mathf.Clamp(realHumanData.head_ik_data.inputEuler.y, -40f, 40f);            
        }

        internal static void ReflectIKToCamera(
            RealHumanData realHumanData,
            Camera cam)
        {
            if (realHumanData.head_ik_data.weight <= 0f)
                return;

            Transform neck = realHumanData.head_ik_data.neck;
            Transform head = realHumanData.head_ik_data.head;

            // 기준 forward (카메라 정면)
            Vector3 targetForward = cam.transform.forward;

            // 월드 → 로컬 변환
            Vector3 neckLocalForward =
                neck.parent.InverseTransformDirection(targetForward);

            Vector3 headLocalForward =
                head.parent.InverseTransformDirection(targetForward);

            Quaternion neckTarget =
                Quaternion.LookRotation(neckLocalForward, Vector3.up);

            Quaternion headTarget =
                Quaternion.LookRotation(headLocalForward, Vector3.up);

            // base 회전 기준으로 보정
            Quaternion neckOffset =
                Quaternion.Inverse(realHumanData.head_ik_data.baseNeckRotation) * neckTarget;

            Quaternion headOffset =
                Quaternion.Inverse(realHumanData.head_ik_data.baseHeadRotation) * headTarget;

            neck.localRotation =
                Quaternion.Slerp(
                    realHumanData.head_ik_data.baseNeckRotation,
                    realHumanData.head_ik_data.baseNeckRotation * neckOffset,
                    realHumanData.head_ik_data.weight * 0.4f
                );

            head.localRotation =
                Quaternion.Slerp(
                    realHumanData.head_ik_data.baseHeadRotation,
                    realHumanData.head_ik_data.baseHeadRotation * headOffset,
                    realHumanData.head_ik_data.weight * 0.6f
                );
        }

        internal static void ReflectIKToAnimation(RealHumanData realHumanData) {
            if (realHumanData.head_ik_data.weight <= 0f)
                return;

            // 목표 회전
            Quaternion neckOffset = Quaternion.Euler(
                realHumanData.head_ik_data.inputEuler.y * 0.4f,
                realHumanData.head_ik_data.inputEuler.x * 0.4f,
                0f
            );

            Quaternion headOffset = Quaternion.Euler(
                realHumanData.head_ik_data.inputEuler.y * 0.6f,
                realHumanData.head_ik_data.inputEuler.x * 0.6f,
                0f
            );

            realHumanData.head_ik_data.neck.localRotation =
                Quaternion.Slerp(
                    realHumanData.head_ik_data.baseNeckRotation,
                    realHumanData.head_ik_data.baseNeckRotation * neckOffset,
                    realHumanData.head_ik_data.weight
                );

            realHumanData.head_ik_data.head.localRotation =
                Quaternion.Slerp(
                    realHumanData.head_ik_data.baseHeadRotation,
                    realHumanData.head_ik_data.baseHeadRotation * headOffset,
                    realHumanData.head_ik_data.weight
                );
        }
#endif                
        #endregion
    }

    // enum BODY_SHADER
    // {
    //     HANMAN,
    //     DEFAULT
    // }
    

    class RealHumanData
    {
        public ChaControl   charControl;
        public Coroutine coroutine;

#if FEATURE_IK_INGAME
        public LegIKData l_leg_ik_data;
        public LegIKData r_leg_ik_data;
        public HeadIKData head_ik_data;
#endif        
        public RealHumanData()
        {
        }        
    }
    
#if FEATURE_IK_INGAME
    public class  LegIKData {
        public Transform thigh;
        public Transform knee;
        public Transform foot;

        public Transform target;
        public Transform pole;

        public GameObject proxyCollider;

        public float weight;
    }

    public class  HeadIKData {
        public Transform neck;
        public Transform head;

        public GameObject proxyCollider;
                
        public Quaternion baseNeckRotation;
        public Quaternion baseHeadRotation;

        public Vector2 inputEuler;

        public float weight;

        public HeadIKData()
        {
            this.inputEuler = Vector2.zero;
            this.weight = 1f;
        }       
    }

#endif

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