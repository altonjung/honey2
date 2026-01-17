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
using UnityEngine.Video;

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

namespace StudioNeoMaker
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class StudioNeoMaker : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "StudioNeoMaker";
        public const string Version = "0.9.0.0";
        public const string GUID = "com.alton.illusionplugins.StudioNeoMaker";
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
        internal static StudioNeoMaker _self;

        private static Studio.FrameCtrl cachedFrameCtrl;
        private static Studio.BackgroundCtrl cachedBgCtrl;

        private static VideoPlayer frameVideoPlayer;
        private static RenderTexture frameVideoRT;

        private static VideoPlayer bgVideoPlayer;
        private static RenderTexture bgVideoRT;

        private static string _assemblyLocation;
        private bool _loaded = false;

        private AssetBundle _bundle;
        #endregion

        #region Accessors
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

        static void SetupFrameVideoPlayer(Camera targetCamera)
        {
            if (frameVideoPlayer != null) return;

            GameObject go = new GameObject("FrameVideoPlayer");
            UnityEngine.Object.DontDestroyOnLoad(go);

            frameVideoPlayer = go.AddComponent<UnityEngine.Video.VideoPlayer>();
            frameVideoPlayer.playOnAwake = false;
            frameVideoPlayer.isLooping = true;
            frameVideoPlayer.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
            frameVideoPlayer.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
        }

        static void OnFrameVideoPrepared(UnityEngine.Video.VideoPlayer vp)
        {
            if (cachedFrameCtrl == null || frameVideoRT == null)
                return;

            cachedFrameCtrl.imageFrame.texture = frameVideoRT;
            cachedFrameCtrl.imageFrame.enabled = true;
            cachedFrameCtrl.cameraUI.enabled = true;

            vp.Play();
        }

        static void StopFrameVideoIfPlaying()
        {
            if (frameVideoPlayer == null)
                return;

            frameVideoPlayer.Stop();
            frameVideoPlayer.prepareCompleted -= OnFrameVideoPrepared;

            if (frameVideoRT != null)
            {
                frameVideoRT.Release();
                UnityEngine.Object.Destroy(frameVideoRT);
                frameVideoRT = null;
            }
        }

        static void SetupBgVideoPlayer()
        {
            if (bgVideoPlayer != null) return;

            GameObject go = new GameObject("BackgroundVideoPlayer");
            UnityEngine.Object.DontDestroyOnLoad(go);

            bgVideoPlayer = go.AddComponent<UnityEngine.Video.VideoPlayer>();
            bgVideoPlayer.playOnAwake = false;
            bgVideoPlayer.isLooping = true;
            bgVideoPlayer.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
            bgVideoPlayer.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
        }

        static void OnBgVideoPrepared(UnityEngine.Video.VideoPlayer vp)
        {
            if (cachedBgCtrl == null || bgVideoRT == null)
                return;

            Material mat = cachedBgCtrl.meshRenderer.material;
            mat.SetTexture("_MainTex", bgVideoRT);
            cachedBgCtrl.meshRenderer.material = mat;

            vp.Play();
        }

        static void StopBgVideoIfPlaying()
        {
            if (bgVideoPlayer == null)
                return;

            bgVideoPlayer.Stop();
            bgVideoPlayer.prepareCompleted -= OnBgVideoPrepared;

            if (bgVideoRT != null)
            {
                bgVideoRT.Release();
                UnityEngine.Object.Destroy(bgVideoRT);
                bgVideoRT = null;
            }
        }

        // static void SetupBackgroundVideo()
        // {
        //     if (bgVideoPlayer != null) return;

        //     GameObject go = new GameObject("BGVideoPlayer");
        //     UnityEngine.Object.DontDestroyOnLoad(go);

        //     bgVideoPlayer = go.AddComponent<VideoPlayer>();
        //     bgVideoPlayer.playOnAwake = false;
        //     bgVideoPlayer.isLooping = true;
        //     bgVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        //     bgVideoPlayer.renderMode = VideoRenderMode.RenderTexture;

        //     bgVideoRT = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
        //     bgVideoRT.Create();

        //     bgVideoPlayer.targetTexture = bgVideoRT;
        // }
        
        // static void StopBgVideoIfPlaying()
        // {
        //     if (bgVideoPlayer != null && bgVideoPlayer.isPlaying)
        //     {
        //         bgVideoPlayer.Stop();
        //     }
        // }

        [HarmonyPatch]
        private static class Directory_Patch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(System.IO.Directory),
                    "GetFiles",
                    new Type[]
                    {
                        typeof(string),
                        typeof(string)
                    }
                );
            }

            static bool Prefix(Studio.FrameCtrl __instance, string path, string searchPattern, ref string[] __result)
            {
                // UnityEngine.Debug.Log($">> CreatePngScreen in GameScreenShot {_width}, {_height}");

                if (path.Equals(UserData.Create("frame")) || path.Equals(UserData.Create("bg")))
                {
                    if (path == null)
                    {
                        throw new ArgumentNullException("path");
                    }
                    if (searchPattern == null)
                    {
                        throw new ArgumentNullException("searchPattern");
                    }

                    string[] files = Directory.GetFiles(
                            path,         
                            "*.*",  
                            SearchOption.TopDirectoryOnly

                        );

                    __result = files;

                     return false;

                } 
                return true;
            }
        }


        [HarmonyPatch]
        private static class BackgroundCtrl_Patch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(Studio.BackgroundCtrl),
                    "Load",
                    new Type[] { typeof(string) }
                );
            }

            static bool Prefix(Studio.BackgroundCtrl __instance, string _file, ref bool __result)
            {
                UnityEngine.Debug.Log(">> Load in BackgroundCtrl");

                __result = false;
                cachedBgCtrl = __instance;

                StopBgVideoIfPlaying();

                string path = Singleton<Studio.Studio>.Instance.ApplicationPath + _file;
                if (!File.Exists(path))
                {
                    __instance.isVisible = false;
                    Singleton<Studio.Studio>.Instance.sceneInfo.background = "";
                    return false;
                }

                string ext = Path.GetExtension(path).ToLower();
                Material material = __instance.meshRenderer.material;

                // =========================
                // IMAGE
                // =========================
                if (ext == ".png")
                {
                    Texture texture = PngAssist.LoadTexture(path);
                    if (texture == null)
                    {
                        __instance.isVisible = false;
                        return false;
                    }

                    material.SetTexture("_MainTex", texture);
                    __instance.meshRenderer.material = material;

                    __instance.isVisible = true;
                    __result = true;
                }
                // =========================
                // VIDEO
                // =========================
                else if (ext == ".mp4" || ext == ".webm" || ext == ".ogv")
                {
                    SetupBgVideoPlayer();

                    // 🔴 완전 초기화
                    bgVideoPlayer.Stop();
                    bgVideoPlayer.prepareCompleted -= OnBgVideoPrepared;

                    // 🔴 RenderTexture 재생성 (중요)
                    if (bgVideoRT != null)
                    {
                        bgVideoRT.Release();
                        UnityEngine.Object.Destroy(bgVideoRT);
                    }

                    bgVideoRT = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                    bgVideoRT.Create();

                    bgVideoPlayer.targetTexture = bgVideoRT;
                    bgVideoPlayer.url = path;
                    bgVideoPlayer.prepareCompleted += OnBgVideoPrepared;
                    bgVideoPlayer.Prepare();
                    try {
                        bgVideoPlayer.Prepare();
                    } catch
                    {
                        Logger.LogMessage($"mp4 and webm(v8) only support");
                    }

                    __instance.isVisible = true;
                    __result = true;
                }

                Singleton<Studio.Studio>.Instance.sceneInfo.background = _file;

                Resources.UnloadUnusedAssets();
                GC.Collect();

                return false;
            }
        }


        [HarmonyPatch]
        private static class FrameCtrl_Patch
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(Studio.FrameCtrl),
                    "Load",
                    new Type[]
                    {
                        typeof(string)
                    }
                );
            }

            static bool Prefix(Studio.FrameCtrl __instance, string _file, ref bool __result)
            {
                UnityEngine.Debug.Log($">> Load in FrameCtrl");
                __result = false;
                cachedFrameCtrl = __instance;

                __instance.Release();
                StopFrameVideoIfPlaying();

                string path = UserData.Path + "frame/" + _file;
                if (!File.Exists(path))
                {
                    Singleton<Studio.Studio>.Instance.sceneInfo.frame = "";
                    return false;
                }

                string ext = Path.GetExtension(path).ToLower();

                // =========================
                // PNG / IMAGE
                // =========================
                if (ext == ".png")
                {
                    Texture texture = PngAssist.LoadTexture(path);
                    if (texture == null) return false;

                    __instance.imageFrame.texture = texture;
                    __instance.imageFrame.enabled = true;
                    __instance.cameraUI.enabled = true;

                    __result = true;
                }
                // =========================
                // VIDEO
                // =========================
                else if (ext == ".mp4" || ext == ".webm" || ext == ".ogv")
                {
                    SetupFrameVideoPlayer(__instance.cameraUI);

                    // 🔴 반드시 초기화
                    frameVideoPlayer.Stop();
                    frameVideoPlayer.prepareCompleted -= OnFrameVideoPrepared;

                    // 🔴 RenderTexture 재생성 (매우 중요)
                    if (frameVideoRT != null)
                    {
                        frameVideoRT.Release();
                        UnityEngine.Object.Destroy(frameVideoRT);
                    }

                    frameVideoRT = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                    frameVideoRT.Create();

                    frameVideoPlayer.targetTexture = frameVideoRT;
                    frameVideoPlayer.url = path;
                    frameVideoPlayer.prepareCompleted += OnFrameVideoPrepared;
                    try {
                        frameVideoPlayer.Prepare();
                    } catch
                    {
                        Logger.LogMessage($"mp4 and webm(v8) only support");
                    }
                    __result = true;
                }

                Singleton<Studio.Studio>.Instance.sceneInfo.frame = _file;

                Resources.UnloadUnusedAssets();
                GC.Collect();

                return false;
            }
        }


        [HarmonyPatch(typeof(Studio.FrameCtrl), "Release")]
        internal static class FrameCtrl_Release_Patches
        {
            public static void Postfix(Studio.FrameCtrl __instance)
            {
                StopFrameVideoIfPlaying();
            }
        }
    
#endregion
    }
}