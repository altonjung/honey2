using Studio;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using KKAPI.Studio;
using IllusionUtility.GetUtility;
#endif


namespace WindPhysics
{
    public class Logic
    {  
        internal static WindData CreateWindData(ObjectCtrlInfo objCtrlInfo)
        {
            OCIChar ociChar = objCtrlInfo as OCIChar;

            WindData windData = new WindData();
            windData.objectCtrlInfo = ociChar;

            if (ociChar != null)
            {
                string bone_prefix_str = "cf_";
                if (ociChar.GetChaControl().sex == 0)
                    bone_prefix_str = "cm_";

                windData.root_bone = ociChar.GetChaControl().objAnim.transform.FindLoop(bone_prefix_str+"J_Root");
                windData.head_bone = ociChar.GetChaControl().objAnim.transform.FindLoop(bone_prefix_str+"J_Head");
                windData.neck_bone = ociChar.GetChaControl().objAnim.transform.FindLoop(bone_prefix_str+"J_Neck"); 
            }

            return windData;
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

        internal static IEnumerator ExecuteDynamicBoneAfterFrame(WindData windData)
        {
            int frameCount = 20;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            ReallocateDynamicBones(windData);
        }


        internal static void ReallocateDynamicBones(WindData windData)
        {
            windData.wind_status = WindPhysics.ConfigKeyEnableWind.Value ? Status.RUN : Status.STOP;

            if (windData.objectCtrlInfo != null)
            {
                OCIChar ociChar = windData.objectCtrlInfo as OCIChar;

                if (ociChar != null) {
                    ChaControl baseCharControl = ociChar.charInfo;

                    // 신규 자원 할당
                    // Hair
                    DynamicBone[] bones = baseCharControl.objBodyBone.transform.FindLoop("cf_J_Head").GetComponentsInChildren<DynamicBone>(true);
                    windData.hairDynamicBones = bones.ToList();

                    // Accesories
                    foreach (var accessory in baseCharControl.objAccessory)
                    {
                        if (accessory != null && accessory.GetComponentsInChildren<DynamicBone>().Length > 0)
                        {
                            windData.accesoriesDynamicBones.Add(accessory.GetComponentsInChildren<DynamicBone>()[0]);
                        }
                    }

                    // Cloth
                    Cloth[] clothes = baseCharControl.transform.GetComponentsInChildren<Cloth>(true);

                    windData.clothes = clothes.ToList();
                    windData.cloth_status = windData.clothes.Count > 0 ? Cloth_Status.PHYSICS : Cloth_Status.EMPTY;
                }
                
#if FEATURE_ITEM_SUPPORT
                OCIItem ociItem = windData.objectCtrlInfo as OCIItem;
                
                if (ociItem != null) {                    
                    DynamicBone[] bones = ociItem.guideObject.transformTarget.gameObject.GetComponentsInChildren<DynamicBone>(true);
                    Cloth[] clothes = ociItem.guideObject.transformTarget.gameObject.GetComponentsInChildren<Cloth>(true);

                    windData.accesoriesDynamicBones = bones.ToList();
                    windData.clothes = clothes.ToList();
                }
#endif

                if (windData.clothes.Count != 0 || windData.hairDynamicBones.Count != 0 || windData.accesoriesDynamicBones.Count != 0)
                {
                      // Coroutine
                    if (ociChar != null) {
                            windData.coroutine = WindPhysics.ConfigKeyEnableWind.Value ? ociChar.charInfo.StartCoroutine(WindPhysics._self.WindRoutine(windData)) : null;  
                    }
#if FEATURE_ITEM_SUPPORT
                    if (ociItem != null) {
                        windData.coroutine = WindPhysics.ConfigKeyEnableWind.Value ? ociItem.guideObject.StartCoroutine(WindPhysics._self.WindRoutine(windData)) : null; 
                    }
#endif
                }
            }
        }

        internal static void TryAllocateObject(ObjectCtrlInfo curObjCtrlInfo) {
            WindData windData = null;
            //신규 등록
            if (WindPhysics._self._ociObjectMgmt.TryGetValue(curObjCtrlInfo.GetHashCode(), out var windData1))
            {
                windData = windData1;
            } else
            {
                windData = CreateWindData(curObjCtrlInfo);
                WindPhysics._self._ociObjectMgmt.Add(curObjCtrlInfo.GetHashCode(), windData);
            }

            windData.wind_status = Status.RUN;
            OCIChar ociChar = curObjCtrlInfo as OCIChar;
            OCIItem ociItem = curObjCtrlInfo as OCIItem;

            if (ociChar != null) {
                ociChar.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
            }
#if FEATURE_ITEM_SUPPORT
            if (ociItem != null) {
                ociItem.guideObject.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
            }
#endif
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

        internal static void SetHairDown(ChaControl chaCtrl, WindData windData) {

            // 월드 기준 방향
            Vector3 worldGravity = Vector3.down * 0.02f;
            Vector3 worldForce = new Vector3(0, -0.03f, -0.01f);
                        
            foreach (DynamicBone bone in windData.hairDynamicBones)
            {
                if (bone == null)
                    continue;

                bone.m_Gravity = realHumanData.head_bone.transform.InverseTransformDirection(worldGravity);
                bone.m_Force =  realHumanData.head_bone.transform.InverseTransformDirection(worldForce);
                bone.m_Damping = 0.13f;
                bone.m_Elasticity = 0.05f;
                bone.m_Stiffness = 0.13f;
            }
        }
#endif

    }

    enum Cloth_Status
    {
        PHYSICS,
        EMPTY
    }

    enum Status
    {
        IDLE,
        RUN,
        STOP,
        REMOVE
    }

    class WindData
    {
        public ObjectCtrlInfo objectCtrlInfo;

        public Coroutine coroutine;
        public List<Cloth> clothes = new List<Cloth>();        

        public List<DynamicBone> hairDynamicBones = new List<DynamicBone>();

        public List<DynamicBone> accesoriesDynamicBones = new List<DynamicBone>();

        public Transform head_bone;

        public Transform neck_bone;

        public Transform root_bone;

        public Status wind_status = Status.IDLE;

        public Cloth_Status cloth_status = Cloth_Status.EMPTY;

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