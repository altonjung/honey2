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
using UnityEngine;
using UnityEngine.SceneManagement;

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
using KKAPI.Studio;
using IllusionUtility.GetUtility;
using KKAPI.Maker;

#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace UpscaleThumbnail
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("HoneySelect2")]
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class UpscaleThumbnail : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "UpscaleThumbnail";
        public const string Version = "0.9.0.0";
        public const string GUID = "com.alton.illusionplugins.upscalethumbnail";
        internal const string _ownerId = "Alton";
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types
        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static UpscaleThumbnail _self;

        private static string _assemblyLocation;
        private bool _loaded = false;

        private AssetBundle _bundle;
        #endregion

        #region Accessors
        internal static ConfigEntry<int> CardDownScale { get; private set; }
        internal static ConfigEntry<int> SceneDownScale { get; private set; }
        internal static ConfigEntry<int> ChoicePos  { get; private set; }

        #endregion

        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            CardDownScale = Config.Bind("Scale", "Card DownScale", 1, new ConfigDescription("Card resolution downgrade", new AcceptableValueRange<int>(1, 3)));
     
            SceneDownScale = Config.Bind("Scale", "Scene DownScale", 2, new ConfigDescription("Scene resolution downgrade", new AcceptableValueRange<int>(1, 5)));
      
            ChoicePos = Config.Bind("Pose", "Coordinate Pose", 2, new ConfigDescription("Pose of coordinate", new AcceptableValueRange<int>(1, 3)));
        }

#if SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }
#endif        
        protected override void Update()
        {
            if (_loaded == false)
                return;
        }
        #endregion

        #region Private Methods
        private void Init()
        {
            _loaded = true;
        }        

        #endregion

        #region Public Methods        
        #endregion

        #region Patches
        
        // [HarmonyPatch(typeof(PngAssist), "SavePng",  typeof(BinaryWriter), typeof(int), typeof(int), typeof(int), typeof(int), typeof(float), typeof(bool), typeof(bool))]
        // private static class PngAssist_SavePng_Patches
        // {
        //     private static bool Prefix(BinaryWriter writer, int capW = 504, int capH = 704, int createW = 252, int createH = 352, float renderRate = 1f, bool drawBackSp = true, bool drawFrontSp = true)
        //     {                
        //         UnityEngine.Debug.Log($">> SavePng in PngAssist");

        //         byte[] array = null;

        //         if (createW == 252 && createH == 352) {
        //             createW = (int)CardDownScale.Value * createW;
        //             createH = (int)CardDownScale.Value * createH;

        //             //if (_self.CaptureFull) {
        //             //    Vector2 screenSize = ScreenInfo.GetScreenSize();
        //             //    capW = (int)screeSize.x;
        //             //    capH = (int)screeSize.y;
        //             //}

        //             PngAssist.CreatePng(ref array, capW, capH, createW, createH, renderRate, drawBackSp, drawFrontSp);
        //             if (array == null)
        //             {
        //                 return true;
        //             }
        //             writer.Write(array);
        //             array = null;                

        //             Logger.LogMessage($"Capture Vis UpscaleThumbnail w:{createW}, h:{createH}");            
        //             return false;
        //         }

        //         return true;
        //     }
        // }

        // [HarmonyPatch(typeof(CharaCustom.CustomCapture), "CapCharaCard", typeof(bool), typeof(SaveFrameAssist), typeof(bool))]
        // private static class CustomCapture_CapCharaCard_Patches
        // {
        //     private static bool Prefix(CharaCustom.CustomCapture __instance, bool enableBG, SaveFrameAssist saveFrameAssist, bool forceHideBackFrame = false)
        //     {                
        //         UnityEngine.Debug.Log($">> CapCharaCard in CustomCapture");

        //         // byte[] result = null;
        //         // bool flag = !(null == saveFrameAssist) && saveFrameAssist.backFrameDraw;
        //         // if (forceHideBackFrame)
        //         // {
        //         //     flag = false;
        //         // }
        //         // bool flag2 = !(null == saveFrameAssist) && saveFrameAssist.frontFrameDraw;
        //         // Camera camera = (null == saveFrameAssist) ? null : (flag ? saveFrameAssist.backFrameCam : null);
        //         // Camera camFrontFrame = (null == saveFrameAssist) ? null : (flag2 ? saveFrameAssist.frontFrameCam : null);
        //         // CustomCapture.CreatePng(ref result, enableBG ? this.camBG : null, camera, this.camMain, camFrontFrame);
        //         // camera = ((null != saveFrameAssist) ? saveFrameAssist.backFrameCam : null);
        //         // if (null != camera)
        //         // {
        //         //     camera.targetTexture = null;
        //         // }
        //         // return result;

        //         return true;
        //     }
        // }   

        // [HarmonyPatch(typeof(CharaCustom.CustomCapture), "CapCoordinateCard", typeof(bool), typeof(SaveFrameAssist), typeof(Camera))]
        // private static class CustomCapture_CapCoordinateCard_Patches
        // {
        //     private static bool Prefix(CharaCustom.CustomCapture __instance, bool enableBG, SaveFrameAssist saveFrameAssist, Camera main)
        //     {                
        //         UnityEngine.Debug.Log($">> CapCoordinateCard in CustomCapture");

        //         // byte[] result = null;
        //         // bool flag = !(null == saveFrameAssist) && saveFrameAssist.backFrameDraw;
        //         // bool flag2 = !(null == saveFrameAssist) && saveFrameAssist.frontFrameDraw;
        //         // Camera camera = (null == saveFrameAssist) ? null : (flag ? saveFrameAssist.backFrameCam : null);
        //         // Camera camFrontFrame = (null == saveFrameAssist) ? null : (flag2 ? saveFrameAssist.frontFrameCam : null);
        //         // CustomCapture.CreatePng(ref result, enableBG ? this.camBG : null, camera, main, camFrontFrame);
        //         // camera = ((null != saveFrameAssist) ? saveFrameAssist.backFrameCam : null);
        //         // if (null != camera)
        //         // {
        //         //     camera.targetTexture = null;
        //         // }
        //         // if (null != this.camMain)
        //         // {
        //         //     this.camMain.targetTexture = null;
        //         // }
        //         // if (null != this.camBG)
        //         // {
        //         //     this.camBG.targetTexture = null;
        //         // }
        //         // return result;

        //         return true;
        //     }
        // }

        
        [HarmonyPatch(typeof(CharaCustom.CustomBase), "ChangeAnimationNo")]
        private static class CustomBase_ChangeAnimationNo_Patches
        {
            private static bool Prefix(CharaCustom.CustomBase __instance, int no, bool mannequin = false)
            {
                // UnityEngine.Debug.Log(Environment.StackTrace);
                // UnityEngine.Debug.Log($">> CustomBase in CustomCapture {MakerAPI.InsideMaker}, {no}, {mannequin}");
                if (MakerAPI.InsideMaker && mannequin)
                {
                    int[] alternative = new int[] {4, 9, 24};

                    if (null == __instance.chaCtrl)
                    {
                        return true;
                    }
                    ChaListDefine.CategoryNo type = (__instance.chaCtrl.sex == 0) ? ChaListDefine.CategoryNo.custom_pose_m : ChaListDefine.CategoryNo.custom_pose_f;
                    int[] array = Singleton<Manager.Character>.Instance.chaListCtrl.GetCategoryInfo(type).Keys.ToArray<int>();

                    int idx = alternative[ChoicePos.Value-1];

                    // UnityEngine.Debug.Log($">> array.Length {array.Length}");
                    if (idx < array.Length)
                    {
                        no = idx;
                    } else
                    {
                        no = array.Length - 1;
                    }

                    __instance.poseNo = no;

                    return false;
                }

                return true;
            }
        }

        // scene
        [HarmonyPatch]
        private static class CreatePngScreen_Patch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(Studio.GameScreenShot),
                    "CreatePngScreen",
                    new Type[]
                    {
                        typeof(int),
                        typeof(int),
                        typeof(bool),
                        typeof(bool)
                    }
                );
            }

            static bool Prefix(Studio.GameScreenShot __instance, int _width, int _height, bool _ARGB, bool _cap, ref byte[] __result)
            {
                // UnityEngine.Debug.Log($">> CreatePngScreen in GameScreenShot {_width}, {_height}");

                // scene 저장
                if (_width == 320 && _height == 180)
                {
                    _width = 320 * 5;
                    _height = 180 * 5; 
                    
                    _width = _width / SceneDownScale.Value;
                    _height = _height / SceneDownScale.Value; 

                    Texture2D texture2D = new Texture2D(_width, _height, _ARGB ? TextureFormat.ARGB32 : TextureFormat.RGB24, false);
                    int antiAliasing = (QualitySettings.antiAliasing == 0) ? 1 : QualitySettings.antiAliasing;
                    RenderTexture temporary = RenderTexture.GetTemporary(texture2D.width, texture2D.height, 24, RenderTextureFormat.Default, RenderTextureReadWrite.Default, antiAliasing);
                    if (_cap)
                    {
                        __instance.imageCap.enabled = true;
                    }
                    Graphics.SetRenderTarget(temporary);
                    GL.Clear(true, true, Color.black);
                    Graphics.SetRenderTarget(null);
                    bool sRGBWrite = GL.sRGBWrite;
                    GL.sRGBWrite = true;
                    foreach (Camera camera in __instance.renderCam)
                    {
                        if (!(null == camera))
                        {
                            int cullingMask = camera.cullingMask;
                            camera.cullingMask &= ~(1 << LayerMask.NameToLayer("Studio/Camera"));
                            bool enabled = camera.enabled;
                            RenderTexture targetTexture = camera.targetTexture;
                            Rect rect = camera.rect;
                            camera.enabled = true;
                            camera.targetTexture = temporary;
                            camera.Render();
                            camera.targetTexture = targetTexture;
                            camera.rect = rect;
                            camera.enabled = enabled;
                            camera.cullingMask = cullingMask;
                        }
                    }
                    if (_cap)
                    {
                        __instance.imageCap.enabled = false;
                    }
                    GL.sRGBWrite = sRGBWrite;
                    RenderTexture.active = temporary;
                    texture2D.ReadPixels(new Rect(0f, 0f, (float)texture2D.width, (float)texture2D.height), 0, 0);
                    texture2D.Apply();
                    RenderTexture.active = null;
                    byte[] result = texture2D.EncodeToPNG();
                    RenderTexture.ReleaseTemporary(temporary);
                    UnityEngine.Object.Destroy(texture2D);
                    UnityEngine.Resources.UnloadUnusedAssets();
                    __result = result;   
                    return false;
                }

                return true;
            }
        }


        [HarmonyPatch]
        private static class CustomCapture_CreatePng_Patches
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(CharaCustom.CustomCapture),
                    "CreatePng",
                    new Type[]
                    {
                typeof(byte[]).MakeByRefType(),
                typeof(Camera),
                typeof(Camera),
                typeof(Camera),
                typeof(Camera)
                    }
                );
            }

            private static bool Prefix(
                ref byte[] pngData,
                Camera _camBG,
                Camera _camBackFrame,
                Camera _camMain,
                Camera _camFrontFrame
            )
            {
                // UnityEngine.Debug.Log($">> CreatePng in CustomCapture ");
                // UnityEngine.Debug.Log(Environment.StackTrace);

                int num = 1920;
                int num2 = 1080;
                int num3 = 756; // 756
                int num4 = 1056; // 1056
                RenderTexture temporary;
                if (QualitySettings.antiAliasing == 0)
                {
                    temporary = RenderTexture.GetTemporary(num, num2, 24);
                }
                else
                {
                    temporary = RenderTexture.GetTemporary(num, num2, 24, RenderTextureFormat.Default, RenderTextureReadWrite.Default, QualitySettings.antiAliasing);
                }
                bool sRGBWrite = GL.sRGBWrite;
                GL.sRGBWrite = true;
                if (null != _camMain)
                {
                    RenderTexture targetTexture = _camMain.targetTexture;
                    bool allowHDR = _camMain.allowHDR;
                    _camMain.allowHDR = false;
                    _camMain.targetTexture = temporary;
                    _camMain.Render();
                    _camMain.targetTexture = targetTexture;
                    _camMain.allowHDR = allowHDR;
                }
                if (null != _camBG)
                {
                    bool allowHDR2 = _camBG.allowHDR;
                    _camBG.allowHDR = false;
                    _camBG.targetTexture = temporary;
                    _camBG.Render();
                    _camBG.targetTexture = null;
                    _camBG.allowHDR = allowHDR2;
                }
                if (null != _camBackFrame)
                {
                    _camBackFrame.targetTexture = temporary;
                    _camBackFrame.Render();
                    _camBackFrame.targetTexture = null;
                }
                if (null != _camFrontFrame)
                {
                    _camFrontFrame.targetTexture = temporary;
                    _camFrontFrame.Render();
                    _camFrontFrame.targetTexture = null;
                }
                GL.sRGBWrite = sRGBWrite;
                Texture2D texture2D = new Texture2D(num3, num4, TextureFormat.RGB24, false, true);
                RenderTexture.active = temporary;
                texture2D.ReadPixels(new Rect((float)(num - num3) / 2f, (float)(num2 - num4) / 2f, (float)num3, (float)num4), 0, 0);
                texture2D.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(temporary);
                TextureScale.Bilinear(texture2D, num3 / CardDownScale.Value, num4 / CardDownScale.Value);
                pngData = texture2D.EncodeToPNG();
                UnityEngine.Object.Destroy(texture2D);

                return false;
            }
        }

        #endregion
    }
}