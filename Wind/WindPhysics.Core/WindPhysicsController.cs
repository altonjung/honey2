using Studio;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using KKAPI.Studio.UI.Toolbars;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Net.Http.Headers;

#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using KKAPI.Studio;
using KKAPI.Maker;
using KKAPI;
using KKAPI.Chara;
using IllusionUtility.GetUtility;
#endif


namespace WindPhysics
{
    public class WindPhysicsController: CharaCustomFunctionController
    {  
        internal WindData windData;

        protected override void OnCardBeingSaved(GameMode currentGameMode) { }

        internal WindData GetData()
        {
            return windData;
        }

        internal void ResetWindData()
        {
            if (windData != null)
            {
                //windData.ClotheForce = 1.0f;
                windData.ClothDamping = 0.5f;
                windData.ClothStiffness = 7.0f;
                // hair
                //windData.HairForce = 1.0f;
                windData.HairElastic = 0.15f;
                // accesories
                //windData.AccesoriesForce = 1.0f;
                windData.AccesoriesElastic = 0.7f;
            }
        }

        internal static List<ObjectCtrlInfo> GetSelectedObjects()
        {
            List<ObjectCtrlInfo> selectedObjCtrlInfos = new List<ObjectCtrlInfo>();
            foreach (TreeNodeObject node in Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes)
            {
                ObjectCtrlInfo ctrlInfo = Studio.Studio.GetCtrlInfo(node);
                if (ctrlInfo == null)
                    continue;

                selectedObjCtrlInfos.Add(ctrlInfo);                  
            }

            return selectedObjCtrlInfos;
        }

        
        internal void UpdateDynamicBones()
        {
            // 새로 자원 할당
            if (windData.chaCtrl != null)
            {
                string bone_prefix_str = "cf_";
                if (windData.chaCtrl.sex == 0)
                    bone_prefix_str = "cm_";                

                // Hair                
                DynamicBone[] bones = windData.chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str+"J_Head").GetComponentsInChildren<DynamicBone>(true);
                windData.hairDynamicBones = bones.ToList();

                // Accesories
                var newAccesories = new List<DynamicBone>();
                foreach (var accessory in windData.chaCtrl.objAccessory)
                {
                    if (accessory != null && accessory.GetComponentsInChildren<DynamicBone>().Length > 0)
                    {
                        newAccesories.Add(accessory.GetComponentsInChildren<DynamicBone>()[0]);
                    }
                }
                windData.accesoriesDynamicBones = newAccesories;

                // Cloth
                Cloth[] clothes = windData.chaCtrl.transform.GetComponentsInChildren<Cloth>(true);
                windData.clothes = clothes.ToList();

                if (windData.clothes.Count != 0 || windData.hairDynamicBones.Count != 0 || windData.accesoriesDynamicBones.Count != 0)
                {
                    // Coroutine
                    if (windData.coroutine == null) {
                        windData.wind_status = Status.RUN;
                        windData.coroutine = windData.chaCtrl.StartCoroutine(WindRoutine());
                    }
                } else
                {
                    windData.wind_status = Status.STOP;
                }

                // UnityEngine.Debug.Log($">> windData.wind_status {windData.wind_status}, {windData}");
            } 
        }

        internal void ExecuteWindEffect(ChaControl chaControl)
        {
            if (chaControl != null) {
                windData = CreateWindData(chaControl);
                chaControl.StartCoroutine(ExecuteWindEffectDelayed());            
            }            
        }

        internal IEnumerator ExecuteWindEffectDelayed()
        {
            int frameCount = 10;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            UpdateDynamicBones();
        }                    

#if FEATURE_FIX_LONGHAIR
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

        internal float Remap(float value, float inMin, float inMax, float outMin, float outMax)
        {
            return outMin + (value - inMin) * (outMax - outMin) / (inMax - inMin);
        }

        internal float GetRelativePosition(float a, float b)
        {
            bool sameSign = (a >= 0 && b >= 0) || (a < 0 && b < 0);

            if (sameSign)
                return Math.Abs(Math.Abs(a) - Math.Abs(b)); // 동일 부호: 절댓값 빼기
            else
                return Math.Abs(Math.Abs(a) + Math.Abs(b)); // 부호 다름: 절댓값 더하기
        }

        internal void StopWindEffect() {
            if (windData != null) {
                windData.wind_status = Status.STOP;
            }            
        }
#endif

        internal void SetHairDown()
        {
            // 월드 기준 방향
            Vector3 worldGravity = Vector3.down * 0.015f;
            if (windData != null)
            {
                foreach (DynamicBone bone in windData.hairDynamicBones)
                {
                    if (bone == null)
                        continue;

                    bone.m_Gravity = windData.head_bone.InverseTransformDirection(worldGravity);
                    bone.m_Force = Vector3.zero;         
                }
            }
        }

        internal WindData CreateWindData(ChaControl chaCtrl)
        {
            if (chaCtrl != null)
            {
                if (windData == null) {
                    windData = new WindData();
                }
            }

            windData.chaCtrl = chaCtrl;
            string bone_prefix_str = "cf_";
            if (chaCtrl.sex == 0)
                bone_prefix_str = "cm_";

            windData.root_bone = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Root");
            windData.head_bone = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Head");
            windData.neck_bone = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str+"J_Neck");                 
        
            return windData;
        }

        void ApplyWind(Vector3 windEffect, float factor, WindData windData)
        {
            float time = Time.time;

            windEffect *= factor;

            bool gravityUp = WindPhysics.Gravity.Value >= 0f;
            float gravity = WindPhysics.Gravity.Value;
            float windForce = WindPhysics.WindForce.Value;
            float windUpForce = WindPhysics.WindUpForce.Value;

            float hairElastic = windData.HairElastic;
            //float hairForce = windData.HairForce;

            float accessoriesElastic = windData.AccesoriesElastic;
            //float accessoriesForce = windData.AccesoriesForce;

            float clothDamping = windData.ClothDamping;
            float clothStiffness = windData.ClothStiffness;
            //float clothForce = windData.ClotheForce;

            float windWave = Mathf.Sin(time * WindPhysics.WindAmplitude.Value);
            float upWave = Mathf.SmoothStep(0f, 1f, Mathf.Max(windWave, 0f));
            float downWave = Mathf.SmoothStep(0f, 1f, Mathf.Max(-windWave, 0f));
            float verticalWave = upWave - downWave;
            const float downHalfCycleScale = 0.25f;
            float asymmetricVerticalWave = verticalWave >= 0f
                ? verticalWave
                : verticalWave * downHalfCycleScale;

            Vector3 hairFinalWind = windEffect * windForce;
            hairFinalWind.y += asymmetricVerticalWave * windUpForce * factor;

            Vector3 accessoriesFinalWind = windEffect * windForce;
            accessoriesFinalWind.y += asymmetricVerticalWave * windUpForce * factor;

            Vector3 baseWind = windEffect.sqrMagnitude > 0f ? windEffect.normalized : Vector3.zero;
            Vector3 externalWind = baseWind * windForce;
            float noise = (Mathf.PerlinNoise(time * 0.8f, 0f) - 0.5f) * 2f;

            Vector3 randomDirectionalWind = baseWind * noise * windForce;
            Vector3 randomVerticalWind = Vector3.up * (asymmetricVerticalWave * windUpForce);
            Vector3 randomWind = randomDirectionalWind + randomVerticalWind;

            // Keep upward lift, but preserve directional wind even when gravity is non-negative.
            Vector3 clothExternalUp = (Vector3.up * gravity) + (externalWind * 600f * factor);
            Vector3 clothExternalDown = externalWind * 600f * factor;
            // In upward-gravity mode, avoid strong horizontal noise that can cancel WindDirection.
            Vector3 clothRandomUp = randomVerticalWind * 400f * factor;
            Vector3 clothRandomDown = randomWind * 1600f * factor;

            Transform headTr = windData.head_bone;

            //--------------------------------
            // Hair
            //--------------------------------

            var hairBones = windData.hairDynamicBones;

            for (int i = 0; i < hairBones.Count; i++)
            {
                var hairBone = hairBones[i];
                if (hairBone == null)
                    continue;

                Transform hairBoneRoot = hairBone.m_Root != null ? hairBone.m_Root : hairBone.transform;

                float sideSign = 0f;

                if (headTr != null)
                {
                    Vector3 toHairBoneRoot = hairBoneRoot.position - headTr.position;
                    float side = Vector3.Dot(toHairBoneRoot, headTr.right);
                    sideSign = Mathf.Sign(side);
                }

                float sideAmount = hairFinalWind.magnitude * UnityEngine.Random.Range(0.001f, 0.5f);
                Vector3 sideWind = headTr != null ? headTr.right * sideSign * sideAmount : Vector3.zero;

                hairBone.m_Elasticity = hairElastic;
                hairBone.m_Damping = 0.015f;
                hairBone.m_Stiffness = 0.2f;

                // ---- FORCE (world -> local)
                Vector3 worldForce = hairFinalWind + sideWind;
                hairBone.m_Force = headTr != null
                    ? headTr.InverseTransformDirection(worldForce)
                    : worldForce;

                // ---- GRAVITY (world -> local)
                Vector3 worldGravity = new Vector3(
                    0,
                    gravity,
                    0f
                );

                hairBone.m_Gravity = headTr != null
                    ? headTr.InverseTransformDirection(worldGravity)
                    : worldGravity;
            }

            //--------------------------------
            // Accessories
            //--------------------------------

            var accessoryBones = windData.accesoriesDynamicBones;

            for (int i = 0; i < accessoryBones.Count; i++)
            {
                var bone = accessoryBones[i];
                if (bone == null)
                    continue;

                bone.m_Elasticity = accessoriesElastic + UnityEngine.Random.Range(-0.2f, 0.2f);

                // FORCE
                Vector3 worldForce = accessoriesFinalWind;

                bone.m_Force = headTr != null
                    ? headTr.InverseTransformDirection(worldForce)
                    : worldForce;

                // GRAVITY
                Vector3 worldGravity = new Vector3(
                    0f,
                    gravity,
                    0f
                );

                bone.m_Gravity = headTr != null
                    ? headTr.InverseTransformDirection(worldGravity)
                    : worldGravity;
            }

            //--------------------------------
            // Clothes
            //--------------------------------

            var clothes = windData.clothes;

            for (int i = 0; i < clothes.Count; i++)
            {
                var cloth = clothes[i];
                if (cloth == null)
                    continue;

                cloth.worldAccelerationScale = 1.0f;
                cloth.worldVelocityScale = 0.0f;

                if (gravityUp)
                {
                    cloth.useGravity = false;
                    cloth.externalAcceleration = clothExternalUp;
                    cloth.randomAcceleration = clothRandomUp;
                }
                else
                {
                    cloth.useGravity = true;
                    cloth.externalAcceleration = clothExternalDown;
                    cloth.randomAcceleration = clothRandomDown;
                }

                cloth.damping = clothDamping;
                cloth.stiffnessFrequency = clothStiffness;
            }
        }

        private IEnumerator FadeoutWindEffect_Cloth(
            List<Cloth> clothes,
            int settleFrames = 15,
            float settleForce = 0.2f)
        {
            if (clothes == null || clothes.Count == 0)
                yield break;

            // 1. Remove wind immediately and apply a small grounding force.
            foreach (var cloth in clothes)
            {
                if (cloth == null) continue;

                cloth.randomAcceleration = Vector3.zero;
                cloth.externalAcceleration = Vector3.down * settleForce;
            }

            // 2. Wait several frames so the cloth can settle.
            for (int i = 0; i < settleFrames; i++)
                yield return null; // Ensure at least one LateUpdate pass.

            // 3. Restore normal external acceleration.
            foreach (var cloth in clothes)
            {
                if (cloth == null) continue;

                cloth.externalAcceleration = Vector3.zero;
            }
        }

        private IEnumerator FadeoutWindEffect_DynamicBone(
            List<DynamicBone> dynamicBones,
            int settleFrames = 15,
            float settleGravity = 0.2f)
        {
            if (dynamicBones == null || dynamicBones.Count == 0)
                yield break;

            // 1. Remove wind force and apply temporary downward gravity.
            foreach (var bone in dynamicBones)
            {
                if (bone == null) continue;

                bone.m_Force = Vector3.zero;
                bone.m_Gravity = Vector3.down * settleGravity;
            }

            // 2. Wait several frames so bones stabilize.
            for (int i = 0; i < settleFrames; i++)
                yield return null; // Ensure at least one LateUpdate pass.

            // 3. Restore default gravity state.
            foreach (var bone in dynamicBones)
            {
                if (bone == null) continue;

                bone.m_Gravity = Vector3.zero;
            }
        }

        internal void StopWindEffect()
        {
            if (windData != null && windData.coroutine != null)
            {
                windData.wind_status = Status.STOP;
            }
        }

        internal IEnumerator WindRoutine()
        {
            while (true)
            {
                if (!WindPhysics._self._loaded)
                {
                    yield return new WaitForSeconds(0.2f); // 0.2초 대기
                }

                if (windData.wind_status == Status.RUN)
                {
                    // Gather Y-range data once per cycle.
                    foreach (var bone in windData.hairDynamicBones)
                    {
                        if (bone == null)
                            continue;

                        float y = bone.m_Root.position.y;
                        windData._minY = Mathf.Min(windData._minY, y);
                        windData._maxY = Mathf.Max(windData._maxY, y);
                    }

                    Quaternion globalRotation = Quaternion.Euler(0f, WindPhysics.WindDirection.Value, 0f);

                    // Add small directional variation for less repetitive motion.
                    float angleY = UnityEngine.Random.Range(-10, 10); // Front/back offset.
                    float angleX = UnityEngine.Random.Range(-5, 5);   // Left/right offset.
                    Quaternion localRotation = Quaternion.Euler(angleX, angleY, 0f);

                    Quaternion rotation = globalRotation * localRotation;
                    Vector3 direction = rotation * Vector3.back;

                    // Slightly randomize base wind strength.
                    Vector3 windEffect = direction.normalized * UnityEngine.Random.Range(0.1f, 0.15f);

                    ApplyWind(windEffect, 1.0f, windData);
                    yield return WaitForSecondsOrStop(0.2f);
                    if (windData.wind_status != Status.RUN)
                        continue;

                    // Fade out over the configured keep time.
                    WindPhysics.ClampWindKeepTimeToInterval();
                    float windInterval = Mathf.Max(0f, WindPhysics.WindInterval.Value);
                    float keepWindTime = Mathf.Clamp(WindPhysics.WindKeepTime.Value, 0f, windInterval);
                    float fadeTime = keepWindTime;

                    float t = 0f;
                    while (t < fadeTime)
                    {
                        if (windData.wind_status != Status.RUN)
                            break;

                        t += Time.deltaTime;
                        float fadeFactor = Mathf.SmoothStep(1f, 0f, t / fadeTime); // Smoothly decrease.
                        ApplyWind(windEffect, fadeFactor, windData);
                        yield return null;
                    }

                    float waitTime = windInterval - keepWindTime;
                    if (waitTime > 0f)
                        yield return WaitForSecondsOrStop(waitTime);
                    else
                        yield return null;
                } 
                else
                {
                    yield return StartCoroutine(FadeoutWindEffect_Cloth(windData.clothes));
                    yield return StartCoroutine(FadeoutWindEffect_DynamicBone(windData.hairDynamicBones));
                    yield return StartCoroutine(FadeoutWindEffect_DynamicBone(windData.accesoriesDynamicBones));
                    windData.coroutine = null;
                    SetHairDown();

                    yield break;
                }
            }
        }

        private IEnumerator WaitForSecondsOrStop(float duration)
        {
            if (duration <= 0f)
                yield break;

            float remaining = duration;
            while (remaining > 0f)
            {
                if (windData == null || windData.wind_status != Status.RUN)
                    yield break;

                yield return null;
                remaining -= Time.deltaTime;
            }
        }

    }

    enum Status
    {
        IDLE,
        RUN,
        STOP
    }

    class WindData
    {
        public ChaControl chaCtrl;
        public Coroutine coroutine;
        public bool enabled;    
        public List<Cloth> clothes = new List<Cloth>();        

        public List<DynamicBone> hairDynamicBones = new List<DynamicBone>();

        public List<DynamicBone> accesoriesDynamicBones = new List<DynamicBone>();

        public Transform head_bone;

        public Transform neck_bone;

        public Transform root_bone;

        public Status wind_status = Status.IDLE;

        public float _minY = float.MaxValue;
        public float _maxY = float.MinValue;


            // clothes
        // public float ClotheForce = 1.0f;
        public float ClothDamping = 0.5f;
        public float ClothStiffness = 7.0f;
        // hair
        // public float HairForce = 1.0f;
        public float HairElastic = 0.15f;
        // accesories
        // public float AccesoriesForce = 1.0f;
        public float AccesoriesElastic = 0.7f;

        public WindData()
        {
        }
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
}
