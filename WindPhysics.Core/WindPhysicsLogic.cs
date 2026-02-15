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

                windData.head_bone = ociChar.GetChaControl().objAnim.transform.FindLoop(bone_prefix_str + "J_Head");
            }

            return windData;
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

        internal static void TryAllocateObject(List<ObjectCtrlInfo> curObjCtrlInfos) {

            WindPhysics._self._selectedOCIs.Clear();

            foreach (ObjectCtrlInfo curObjCtrlInfo in curObjCtrlInfos)
            {
                WindPhysics._self._selectedOCIs.Add(curObjCtrlInfo);

                OCIChar ociChar = curObjCtrlInfo as OCIChar;
                OCIItem ociItem = curObjCtrlInfo as OCIItem;

                // 기존 선택된 대상인지 여부 확인
                if (WindPhysics._self._ociObjectMgmt.TryGetValue(curObjCtrlInfo.GetHashCode(), out var windData))
                {
                    if (ociChar != null)
                        ociChar.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
    #if FEATURE_ITEM_SUPPORT
                    if (ociItem != null)
                        ociItem.guideObject.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
    #endif
                } 
                else
                {
                    //신규 등록
                    WindData windData2 = CreateWindData(curObjCtrlInfo);
                    WindPhysics._self._ociObjectMgmt.Add(curObjCtrlInfo.GetHashCode(), windData2);

                    if (ociChar != null) {
                        ociChar.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(windData2));
                    }
    #if FEATURE_ITEM_SUPPORT
                    if (ociItem != null) {
                        ociItem.guideObject.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData2));
                    }
    #endif
                }   
            }
        }
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
        STOP
    }

    class WindData
    {
        public ObjectCtrlInfo objectCtrlInfo;

        public Coroutine coroutine;
        public List<Cloth> clothes = new List<Cloth>();        

        public List<DynamicBone> hairDynamicBones = new List<DynamicBone>();

        public List<DynamicBone> accesoriesDynamicBones = new List<DynamicBone>();

        public Transform head_bone;

        // public SkinnedMeshRenderer clothTopRender;

        // public SkinnedMeshRenderer clothBottomRender;

        // public SkinnedMeshRenderer hairRender;

        // public SkinnedMeshRenderer headRender;

        // public SkinnedMeshRenderer bodyRender;

        public Status wind_status = Status.IDLE;

        public Cloth_Status cloth_status = Cloth_Status.EMPTY;

        public WindData()
        {
        }
    }
}