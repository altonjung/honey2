using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;

using UILib.EventHandlers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.Threading.Tasks;
using System.Numerics;

#if IPA
using Harmony;
using IllusionPlugin;
#elif BEPINEX
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
#endif
#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using ADV.Commands.Camera;
using KKAPI.Studio;
using IllusionUtility.GetUtility;
using ADV.Commands.Object;
#endif
using RootMotion.FinalIK;

#if AISHOUJO || HONEYSELECT2
using AIChara;
using static Illusion.Utils;
using System.Runtime.Remoting.Messaging;
#endif
using KKAPI.Studio;
using KKAPI.Studio.UI.Toolbars;
using KKAPI.Utilities;
using KKAPI.Chara;
using static CharaUtils.Expression;
using KKAPI;



namespace FacialQuickTransform
{
    public class FacialQuickTransformController : CharaCustomFunctionController
    {
        internal FacialQuickTransformData expressionData;
        protected override void OnCardBeingSaved(GameMode currentGameMode) { }

        internal FacialQuickTransformData GetData()
        {
            return expressionData;
        }

        internal void ResetFacialQuickTransformData()
        {
            if (expressionData != null)
                expressionData.Reset();
        }

        internal FacialQuickTransformData CreateData(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            ChaControl chaCtrl = ociChar.GetChaControl();
            expressionData = new FacialQuickTransformData();
            expressionData.chaCtrl = chaCtrl;

            string bone_prefix_str = "cf_";
            if (chaCtrl.sex == 0)
                bone_prefix_str = "cm_";

            expressionData._eye_01_L = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_Eye01_L");
            expressionData._eye_02_L = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_Eye02_L");
            expressionData._eye_03_L = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_Eye03_L");
            expressionData._eye_04_L = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_Eye04_L");            

            expressionData._eye_01_R = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_Eye01_R");
            expressionData._eye_02_R = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_Eye02_R");
            expressionData._eye_03_R = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_Eye03_R");
            expressionData._eye_04_R = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_Eye04_R");   

            expressionData._eye_ball_L = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_EyePos_rz_L");
            expressionData._eye_ball_R = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_EyePos_rz_R");
            expressionData._nose = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_NoseBase_trs");
            expressionData._mouth = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_MouthBase_tr");

            expressionData.CaptureEyeBaseRotations();
            expressionData.CaptureEyeBaseTransformRotations();
            expressionData.CaptureFaceBaseTransforms();

            SetFaceBlendShapes();

            return expressionData;
        }

        public void SetBlendShape(float weight, int targetIdx)
        {
            if (expressionData == null || expressionData.chaCtrl == null || expressionData.chaCtrl.objBody == null)
                return;
            
            if (targetIdx < 0)
                return;

            SkinnedMeshRenderer[] bodyRenderers = expressionData.chaCtrl.objBody.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                SkinnedMeshRenderer render = bodyRenderers[i];
                if (render == null || render.sharedMesh == null)
                    continue;

                UnityEngine.Mesh mesh = render.sharedMesh;
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

        internal void SetFaceBlendShapes()
        {
            if (expressionData != null)
            {
                foreach (var fbsTarget in expressionData.chaCtrl.fbsCtrl.EyesCtrl.FBSTarget)
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
                                        expressionData.eye_close_idx_in_head_of_eyectrl = idx;
                                    else if (name.Contains("namida."))
                                        expressionData.eye_close_idx_in_namida_of_eyectrl = idx;
                                    else
                                        expressionData.eye_close_idx_in_lash_of_eyectrl = idx;
                                }
                            }
                            else if (name.Contains("_wink_R"))
                            {
                                if (name.Contains("head."))
                                    expressionData.eye_wink_idx_in_head_of_eyectrl = idx;
                                else if (name.Contains("namida."))
                                    expressionData.eye_wink_idx_in_namida_of_eyectrl = idx;
                                else
                                    expressionData.eye_wink_idx_in_lash_of_eyectrl = idx;
                            }
                        }
                    }
                }

                foreach (var fbsTarget in expressionData.chaCtrl.fbsCtrl.MouthCtrl.FBSTarget)
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
                                        expressionData.eye_close_idx_in_head_of_mouthctrl = idx;
                                    else if (name.Contains("namida."))
                                        expressionData.eye_close_idx_in_namida_of_mouthctrl = idx;
                                }
                            }
                            else if (name.Contains("_wink_R"))
                            {
                                if (name.Contains("head."))
                                    expressionData.eye_wink_idx_in_head_of_mouthctrl = idx;
                                else if (name.Contains("namida."))
                                    expressionData.eye_wink_idx_in_namida_of_mouthctrl = idx;
                            }
                        }
                    }
                }
            }           
        }             
    }

    class FacialQuickTransformData
    {
        public ChaControl chaCtrl;

        // eye transform
        // eye sleep 
        public Transform _eye_01_L;
        public Transform _eye_02_L;
        // eye smile
        public Transform _eye_03_L;
        public Transform _eye_04_L;

        // eye sleep         
        public Transform _eye_01_R;
        public Transform _eye_02_R;
        // eye smile
        public Transform _eye_03_R;
        public Transform _eye_04_R;

        public Quaternion _eye_01_base_rot_L = Quaternion.identity;
        public Quaternion _eye_02_base_rot_L = Quaternion.identity;
        public Quaternion _eye_03_base_rot_L = Quaternion.identity;
        public Quaternion _eye_04_base_rot_L = Quaternion.identity;
        public Quaternion _eye_01_base_rot_R = Quaternion.identity;
        public Quaternion _eye_02_base_rot_R = Quaternion.identity;
        public Quaternion _eye_03_base_rot_R = Quaternion.identity;
        public Quaternion _eye_04_base_rot_R = Quaternion.identity;
        public bool _eye_base_rot_ready;

        // eye ball transform
        public Transform _eye_ball_L;
        public Transform _eye_ball_R;
        public Quaternion _eye_ball_base_rot_L = Quaternion.identity;
        public Quaternion _eye_ball_base_rot_R = Quaternion.identity;
        public bool _eye_ball_base_rot_ready;
        
        // eye transform
        public Transform _eye_inside_L;
        public Transform _eye_inside_R;

        public Transform _eye_outside_L;
        public Transform _eye_outside_R;

        // nose transform
        public Transform _nose;
        public Vector3 _nose_base_pos = Vector3.zero;
        public Quaternion _nose_base_rot = Quaternion.identity;
        public bool _nose_base_ready;

        // mouth transform 
        public Transform _mouth;
        public Vector3 _mouth_base_pos = Vector3.zero;
        public Quaternion _mouth_base_rot = Quaternion.identity;
        public bool _mouth_base_ready;


        // eye ball control
        public int EyeBallCategory;
        public int EyeBallEditTarget;
        public float EyeBallLeftX;
        public float EyeBallLeftY;
        public float EyeBallRightX;
        public float EyeBallRightY;
        public float EyeLidUpRotX;
        public float EyeLidDnRotX;
        public float EyeSmileInRotX;
        public float EyeSmileOutRotX;

        // mouth control
        public float MouthPosX;
        public float MouthPosY;
        public float MouthPosZ;
        public float MouthRotX;
        public float MouthRotY;
        public float MouthRotZ;

        // nose control
        public float NosePosX;
        public float NosePosY;
        public float NosePosZ;
        public float NoseRotX;
        public float NoseRotY;
        public float NoseRotZ;


        // blendshape idx
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


        internal void Reset()
        {
            EyeBallCategory = 0;
            EyeBallEditTarget = 0;
            EyeBallLeftX = 0f;
            EyeBallLeftY = 0f;
            EyeBallRightX = 0f;
            EyeBallRightY = 0f;
            EyeLidUpRotX = 0f;
            EyeLidDnRotX = 0f;
            EyeSmileInRotX = 0f;
            EyeSmileOutRotX = 0f;
            MouthPosX = 0f;
            MouthPosY = 0f;
            MouthPosZ = 0f;
            MouthRotX = 0f;
            MouthRotY = 0f;
            MouthRotZ = 0f;
            NosePosX = 0f;
            NosePosY = 0f;
            NosePosZ = 0f;
            NoseRotX = 0f;
            NoseRotY = 0f;
            NoseRotZ = 0f;
            CaptureEyeBaseRotations();
            CaptureEyeBaseTransformRotations();
            CaptureFaceBaseTransforms();
        }

        internal void CaptureEyeBaseRotations()
        {
            if (_eye_ball_L != null)
                _eye_ball_base_rot_L = _eye_ball_L.localRotation;
            if (_eye_ball_R != null)
                _eye_ball_base_rot_R = _eye_ball_R.localRotation;

            _eye_ball_base_rot_ready = _eye_ball_L != null || _eye_ball_R != null;
        }

        internal void CaptureEyeBaseTransformRotations()
        {
            if (_eye_01_L != null) _eye_01_base_rot_L = _eye_01_L.localRotation;
            if (_eye_02_L != null) _eye_02_base_rot_L = _eye_02_L.localRotation;
            if (_eye_03_L != null) _eye_03_base_rot_L = _eye_03_L.localRotation;
            if (_eye_04_L != null) _eye_04_base_rot_L = _eye_04_L.localRotation;

            if (_eye_01_R != null) _eye_01_base_rot_R = _eye_01_R.localRotation;
            if (_eye_02_R != null) _eye_02_base_rot_R = _eye_02_R.localRotation;
            if (_eye_03_R != null) _eye_03_base_rot_R = _eye_03_R.localRotation;
            if (_eye_04_R != null) _eye_04_base_rot_R = _eye_04_R.localRotation;

            _eye_base_rot_ready =
                _eye_01_L != null || _eye_02_L != null || _eye_03_L != null || _eye_04_L != null ||
                _eye_01_R != null || _eye_02_R != null || _eye_03_R != null || _eye_04_R != null;
        }

        internal void CaptureFaceBaseTransforms()
        {
            if (_mouth != null)
            {
                _mouth_base_pos = _mouth.localPosition;
                _mouth_base_rot = _mouth.localRotation;
                _mouth_base_ready = true;
            }

            if (_nose != null)
            {
                _nose_base_pos = _nose.localPosition;
                _nose_base_rot = _nose.localRotation;
                _nose_base_ready = true;
            }
        }
    }
}
