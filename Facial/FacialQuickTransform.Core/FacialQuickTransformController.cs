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

            expressionData._mouth_lip_up = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_Mouthup");
            expressionData._mouth_lip_dn = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_MouthLow");

            expressionData._mouth_smile_l = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_Mouth_L");
            expressionData._mouth_smile_r = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_Mouth_R");

            expressionData._mouth_cavity = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_MouthCavity");

            expressionData._nose_wing_l = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_NoseWing_tx_L");
            expressionData._nose_wing_r = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_NoseWing_tx_R");

            expressionData._tongue_s1 = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str + "J_Tang_S_01_at");
            expressionData._tongue_s2 = chaCtrl.objBodyBone.transform.FindLoop(bone_prefix_str + "J_Tang_S_02_at");

            expressionData.CaptureEyeBaseRotations();
            expressionData.CaptureEyeBaseTransformRotations();
            expressionData.CaptureFaceBaseTransforms();

            //SetFaceBlendShapes();

            return expressionData;
        }

        // public void SetBlendShape(float weight, int targetIdx)
        // {
        //     if (expressionData == null || expressionData.chaCtrl == null || expressionData.chaCtrl.objBody == null)
        //         return;
            
        //     if (targetIdx < 0)
        //         return;

        //     SkinnedMeshRenderer[] bodyRenderers = expressionData.chaCtrl.objBody.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        //     for (int i = 0; i < bodyRenderers.Length; i++)
        //     {
        //         SkinnedMeshRenderer render = bodyRenderers[i];
        //         if (render == null || render.sharedMesh == null)
        //             continue;

        //         UnityEngine.Mesh mesh = render.sharedMesh;
        //         if (targetIdx < mesh.blendShapeCount)
        //         {
        //             string nameAtTarget = mesh.GetBlendShapeName(targetIdx);
        //             if (nameAtTarget != null)
        //             {
        //                 render.SetBlendShapeWeight(targetIdx, weight);
        //                 continue;
        //             }
        //         }

        //         for (int idx = 0; idx < mesh.blendShapeCount; idx++)
        //         {
        //             string name = mesh.GetBlendShapeName(idx);
        //             if (name != null)
        //             {
        //                 render.SetBlendShapeWeight(idx, weight);
        //                 break;
        //             }
        //         }
        //     }
        // }

        // internal void SetFaceBlendShapes()
        // {
        //     if (expressionData != null)
        //     {
        //         foreach (var fbsTarget in expressionData.chaCtrl.fbsCtrl.EyesCtrl.FBSTarget)
        //         {
        //             SkinnedMeshRenderer srender = fbsTarget.GetSkinnedMeshRenderer();
        //             var mesh = srender.sharedMesh;
        //             if (mesh && mesh.blendShapeCount > 0)
        //             {
        //                 for (int idx = 0; idx < mesh.blendShapeCount; idx++)
        //                 {
        //                     string name = mesh.GetBlendShapeName(idx);

        //                     if (name.Contains("_close"))
        //                     {
        //                         if (!name.Contains("_close_L") && !name.Contains("_close_R"))
        //                         {
        //                             if (name.Contains("head."))
        //                                 expressionData.eye_close_idx_in_head_of_eyectrl = idx;
        //                             else if (name.Contains("namida."))
        //                                 expressionData.eye_close_idx_in_namida_of_eyectrl = idx;
        //                             else
        //                                 expressionData.eye_close_idx_in_lash_of_eyectrl = idx;
        //                         }
        //                     }
        //                     else if (name.Contains("_wink_R"))
        //                     {
        //                         if (name.Contains("head."))
        //                             expressionData.eye_wink_idx_in_head_of_eyectrl = idx;
        //                         else if (name.Contains("namida."))
        //                             expressionData.eye_wink_idx_in_namida_of_eyectrl = idx;
        //                         else
        //                             expressionData.eye_wink_idx_in_lash_of_eyectrl = idx;
        //                     }
        //                 }
        //             }
        //         }

        //         foreach (var fbsTarget in expressionData.chaCtrl.fbsCtrl.MouthCtrl.FBSTarget)
        //         {
        //             SkinnedMeshRenderer srender = fbsTarget.GetSkinnedMeshRenderer();
        //             var mesh = srender.sharedMesh;
        //             if (mesh && mesh.blendShapeCount > 0)
        //             {
        //                 for (int idx = 0; idx < mesh.blendShapeCount; idx++)
        //                 {
        //                     string name = mesh.GetBlendShapeName(idx);

        //                     if (name.Contains("_close"))
        //                     {
        //                         if (!name.Contains("_close_L") && !name.Contains("_close_R"))
        //                         {
        //                             if (name.Contains("head."))
        //                                 expressionData.eye_close_idx_in_head_of_mouthctrl = idx;
        //                             else if (name.Contains("namida."))
        //                                 expressionData.eye_close_idx_in_namida_of_mouthctrl = idx;
        //                         }
        //                     }
        //                     else if (name.Contains("_wink_R"))
        //                     {
        //                         if (name.Contains("head."))
        //                             expressionData.eye_wink_idx_in_head_of_mouthctrl = idx;
        //                         else if (name.Contains("namida."))
        //                             expressionData.eye_wink_idx_in_namida_of_mouthctrl = idx;
        //                     }
        //                 }
        //             }
        //         }
        //     }           
        // }             
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
        public Quaternion _nose_base_rot = Quaternion.identity;
        public bool _nose_base_ready;

        // nose wing transform 
        public Transform _nose_wing_l;
        public Quaternion _nose_wing_base_rot_l = Quaternion.identity;
        
        public Transform _nose_wing_r;
        public Quaternion _nose_wing_base_rot_r = Quaternion.identity;

        // mouth transform 
        public Transform _mouth;
        public Quaternion _mouth_base_rot = Quaternion.identity;
        public bool _mouth_base_ready;

        // mouth cavity transform 
        public Transform _mouth_cavity;
        public Vector3 _mouth_cavity_base_pos = Vector3.zero;
        public bool _mouth_cavity_base_pos_ready;

        // mouth smile
        public Transform _mouth_smile_l;
        public Vector3 _mouth_smile_base_pos_l = Vector3.zero;
        public bool _mouth_smile_base_pos_ready_l;

        public Transform _mouth_smile_r;
        public Vector3 _mouth_smile_base_pos_r = Vector3.zero;
        public bool _mouth_smile_base_pos_ready_r;
        

        // lip transform 
        public Transform _mouth_lip_up;
        public Quaternion _mouth_lip_up_base_rot = Quaternion.identity;

        // lip transform 
        public Transform _mouth_lip_dn;
        public Quaternion _mouth_lip_dn_base_rot = Quaternion.identity;


        // lip transform 
        public Transform _tongue_s1;
        public Quaternion _tongue_s1_base_rot = Quaternion.identity;
        public Vector3 _tongue_s1_base_pos = Vector3.zero;
        public bool _tongue_s1_base_pos_ready;

        public Transform _tongue_s2;
        public Quaternion _tongue_s2_base_rot = Quaternion.identity;
        public Vector3 _tongue_s2_base_pos = Vector3.zero;
        public bool _tongue_s2_base_pos_ready;
        public bool _tongue_base_ready;

        // eye ball control
        public int EyeBallCategory;
        public int EyeBallEditTarget;
        public int EyebrowTypeIndex;
        public int EyebrowTypeLastAppliedIndex = -1;
        public float EyeBallLeftX;
        public float EyeBallLeftY;
        public float EyeBallRightX;
        public float EyeBallRightY;
        public float EyeLidUpRotX;
        public float EyeLidDnRotX;
        public float EyeSmileInRotX;
        public float EyeSmileOutRotX;
        public float EyeWinkLeftRotX;
        public float EyeWinkRightRotX;

        // mouth control
        public float MouthRotX;
        public float MouthRotY;
        public float MouthRotZ;
        public float MouthLipUpRotX;
        public float MouthLipUpRotY;
        public float MouthLipUpRotZ;
        public float MouthLipDnRotX;
        public float MouthLipDnRotY;
        public float MouthLipDnRotZ;
        public float MouthCavityPosZ;
        public int MouthTypeIndex;
        public int MouthTypeLastAppliedIndex = -1;
        public float MouthSmileLeftPosX;
        public float MouthSmileLeftPosY;
        public float MouthSmileRightPosX;
        public float MouthSmileRightPosY;

        // nose control
        public float NoseRotX;
        public float NoseRotY;
        public float NoseRotZ;
        public float NoseWingLeftRotX;
        public float NoseWingLeftRotY;
        public float NoseWingLeftRotZ;
        public float NoseWingRightRotX;
        public float NoseWingRightRotY;
        public float NoseWingRightRotZ;

        // tongue control
        public bool TongueCategoryEnabled;
        public float Tongue1PosZ;
        public float Tongue1RotX;
        public float Tongue1RotY;
        public float Tongue1RotZ;
        public float Tongue2PosZ;
        public float Tongue2RotX;
        public float Tongue2RotY;
        public float Tongue2RotZ;


        // // blendshape idx
        // public int eye_close_idx_in_head_of_eyectrl;
        // public int eye_close_idx_in_namida_of_eyectrl;
        // public int eye_close_idx_in_lash_of_eyectrl;

        // public int eye_wink_idx_in_head_of_eyectrl;
        // public int eye_wink_idx_in_namida_of_eyectrl;
        // public int eye_wink_idx_in_lash_of_eyectrl;

        // public int eye_close_idx_in_head_of_mouthctrl;
        // public int eye_close_idx_in_namida_of_mouthctrl;

        // public int eye_wink_idx_in_head_of_mouthctrl;
        // public int eye_wink_idx_in_namida_of_mouthctrl;


        internal void Reset()
        {
            EyeBallCategory = 0;
            EyeBallEditTarget = 0;
            EyebrowTypeIndex = 0;
            EyebrowTypeLastAppliedIndex = -1;
            EyeBallLeftX = 0f;
            EyeBallLeftY = 0f;
            EyeBallRightX = 0f;
            EyeBallRightY = 0f;
            EyeLidUpRotX = 0f;
            EyeLidDnRotX = 0f;
            EyeSmileInRotX = 0f;
            EyeSmileOutRotX = 0f;
            EyeWinkLeftRotX = 0f;
            EyeWinkRightRotX = 0f;
            MouthRotX = 0f;
            MouthRotY = 0f;
            MouthRotZ = 0f;
            MouthLipUpRotX = 0f;
            MouthLipUpRotY = 0f;
            MouthLipUpRotZ = 0f;
            MouthLipDnRotX = 0f;
            MouthLipDnRotY = 0f;
            MouthLipDnRotZ = 0f;
            MouthCavityPosZ = 0f;
            MouthTypeIndex = 0;
            MouthTypeLastAppliedIndex = -1;
            MouthSmileLeftPosX = 0f;
            MouthSmileLeftPosY = 0f;
            MouthSmileRightPosX = 0f;
            MouthSmileRightPosY = 0f;
            NoseRotX = 0f;
            NoseRotY = 0f;
            NoseRotZ = 0f;
            NoseWingLeftRotX = 0f;
            NoseWingLeftRotY = 0f;
            NoseWingLeftRotZ = 0f;
            NoseWingRightRotX = 0f;
            NoseWingRightRotY = 0f;
            NoseWingRightRotZ = 0f;
            TongueCategoryEnabled = false;
            Tongue1PosZ = 0f;
            Tongue1RotX = 0f;
            Tongue1RotY = 0f;
            Tongue1RotZ = 0f;
            Tongue2PosZ = 0f;
            Tongue2RotX = 0f;
            Tongue2RotY = 0f;
            Tongue2RotZ = 0f;
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
                _mouth_base_rot = _mouth.localRotation;
            if (_mouth_lip_up != null)
                _mouth_lip_up_base_rot = _mouth_lip_up.localRotation;
            if (_mouth_lip_dn != null)
                _mouth_lip_dn_base_rot = _mouth_lip_dn.localRotation;
            if (_mouth_cavity != null)
                _mouth_cavity_base_pos = _mouth_cavity.localPosition;
            if (_mouth_smile_l != null)
                _mouth_smile_base_pos_l = _mouth_smile_l.localPosition;
            if (_mouth_smile_r != null)
                _mouth_smile_base_pos_r = _mouth_smile_r.localPosition;
            if (_tongue_s1 != null)
            {
                _tongue_s1_base_rot = _tongue_s1.localRotation;
                _tongue_s1_base_pos = _tongue_s1.localPosition;
            }
            if (_tongue_s2 != null)
            {
                _tongue_s2_base_rot = _tongue_s2.localRotation;
                _tongue_s2_base_pos = _tongue_s2.localPosition;
            }

            if (_nose != null)
                _nose_base_rot = _nose.localRotation;
            if (_nose_wing_l != null)
                _nose_wing_base_rot_l = _nose_wing_l.localRotation;
            if (_nose_wing_r != null)
                _nose_wing_base_rot_r = _nose_wing_r.localRotation;

            _mouth_cavity_base_pos_ready = _mouth_cavity != null;
            _mouth_smile_base_pos_ready_l = _mouth_smile_l != null;
            _mouth_smile_base_pos_ready_r = _mouth_smile_r != null;
            _tongue_s1_base_pos_ready = _tongue_s1 != null;
            _tongue_s2_base_pos_ready = _tongue_s2 != null;
            _mouth_base_ready = _mouth != null
                || _mouth_lip_up != null
                || _mouth_lip_dn != null
                || _mouth_cavity != null
                || _mouth_smile_l != null
                || _mouth_smile_r != null;
            _nose_base_ready = _nose != null || _nose_wing_l != null || _nose_wing_r != null;
            _tongue_base_ready = _tongue_s1 != null || _tongue_s2 != null;
        }
    }
}
