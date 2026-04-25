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



namespace ExpressionSlider
{
    public class ExpressionSliderController : CharaCustomFunctionController
    {
        internal ExpressionSliderData expressionData;
        protected override void OnCardBeingSaved(GameMode currentGameMode) { }

        internal ExpressionSliderData GetData()
        {
            return expressionData;
        }

        internal void ResetExpressionSliderData()
        {
            if (expressionData != null)
                expressionData.Reset();
        }

        internal ExpressionSliderData CreateData(OCIChar ociChar)
        {
            if (ociChar == null || ociChar.GetChaControl() == null)
                return null;

            ChaControl chaCtrl = ociChar.GetChaControl();
            expressionData = new ExpressionSliderData();
            expressionData.chaCtrl = chaCtrl;

            string bone_prefix_str = "cf_";
            if (chaCtrl.sex == 0)
                bone_prefix_str = "cm_";

            expressionData._eye_ball_L = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_EyePos_rz_L");
            expressionData._eye_ball_R = chaCtrl.objAnim.transform.FindLoop(bone_prefix_str + "J_EyePos_rz_R");
            expressionData.CaptureEyeBaseRotations();

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

    class ExpressionSliderData
    {
        public ChaControl chaCtrl;

        // eye ball transform
        public Transform _eye_ball_L;
        public Transform _eye_ball_R;
        public Quaternion _eye_ball_base_rot_L = Quaternion.identity;
        public Quaternion _eye_ball_base_rot_R = Quaternion.identity;
        public bool _eye_ball_base_rot_ready;

        // eye ball control
        public int EyeBallCategory;
        public int EyeBallEditTarget;
        public float EyeBallLeftX;
        public float EyeBallLeftY;
        public float EyeBallRightX;
        public float EyeBallRightY;


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
            CaptureEyeBaseRotations();
        }

        internal void CaptureEyeBaseRotations()
        {
            if (_eye_ball_L != null)
                _eye_ball_base_rot_L = _eye_ball_L.localRotation;
            if (_eye_ball_R != null)
                _eye_ball_base_rot_R = _eye_ball_R.localRotation;

            _eye_ball_base_rot_ready = _eye_ball_L != null || _eye_ball_R != null;
        }
    }
}
