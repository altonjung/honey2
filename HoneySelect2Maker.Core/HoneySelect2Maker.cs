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
using static Studio.GuideInput;
using UnityEngine.Video;
using System.Runtime.CompilerServices;
using KKAPI.Chara;


#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace HoneySelect2Maker
{

#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("HoneySelect2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class HoneySelect2Maker : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "HoneySelect2Maker";
        public const string Version = "0.9.0.1";
        public const string GUID = "com.alton.illusionplugins.HoneySelect2Maker";
        internal const string _ownerId = "Alton";
#if FEATURE_PUBLIC_RELEASE
        internal const int VIDEO_MAX_COUNT = 5;
#else
        internal const int VIDEO_MAX_COUNT = 30;
#endif

#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
#endif
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
        internal static HoneySelect2Maker _self;

        internal string _video_title_scene_path = UserData.Path + "/hs2maker/title/";
        internal string _video_myroom_scene_path = UserData.Path + "/hs2maker/myroom/";
        internal string _video_lobby_scene_path = UserData.Path + "/hs2maker/lobby/";
        internal string _video_concierge_scene_path = UserData.Path + "/hs2maker/concierge/";
        internal string _video_furroom_scene_path = UserData.Path + "/hs2maker/furroom/";
        internal string _video_sleep_scene_path = UserData.Path + "/hs2maker/sleep/";
        internal string _video_adv_scene_path = UserData.Path + "/hs2maker/adv/";

        // internal static Camera _cutsceneCamera;

        // internal static GameObject _videoRoot;
         internal UnityEngine.Video.VideoPlayer _sceneVideoPlayer;
        // internal static RenderTexture _sceneVideoRenderTexture;
        // internal static GameObject _sceneVideoCanvas;
        // internal static UnityEngine.UI.RawImage _sceneVideoRawImage;
        internal static Canvas _sceneCanvas;
        internal static RenderTexture _sceneRT;
        internal static GameObject _overlayCanvasGO;
        internal static UnityEngine.UI.RawImage _rawImage;

        internal static Action _onSceneVideoCompleted;
        internal static bool _videoFinished = false;

        internal bool _isAbleTitleVideo;
        internal bool _isAbleMyRoomVideo;
        internal bool _isAbleConciergeVideo;
        internal bool _isAbleLobbyVideo;
        internal bool _isAbleFurVideo; 
        internal bool _isAbleSleepVideo;
        internal bool _isAbleAdvVideo;

        internal List<Canvas> _disabledCanvasCache = new List<Canvas>();
        private static string _assemblyLocation;
        internal static bool _reEntryHarmony = false;
        
        private bool _loaded = false;

        private AssetBundle _bundle;
        #endregion


        #region Accessors
         internal static ConfigEntry<bool> VideoModeActive { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            GameObject videoObj = new GameObject("sceneVideoPlayer");
            GameObject.DontDestroyOnLoad(videoObj);
            _sceneVideoPlayer = videoObj.AddComponent<UnityEngine.Video.VideoPlayer>();

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            // title
            if (Logic.GetVideoFiles(_self._video_title_scene_path).Count > 0)
            {
                _isAbleTitleVideo = true;
            }        

            // myroom
            if (Logic.GetVideoFiles(_self._video_myroom_scene_path).Count > 0)
            {
                _isAbleMyRoomVideo = true;
            }

            // concierge
            if (Logic.GetVideoFiles(_self._video_concierge_scene_path).Count > 0)
            {
                _isAbleConciergeVideo = true;
            }

           // lobby
            if (Logic.GetVideoFiles(_self._video_lobby_scene_path).Count > 0)
            {
                _isAbleLobbyVideo = true;
            }

           // fur
            if (Logic.GetVideoFiles(_self._video_furroom_scene_path).Count > 0)
            {
                _isAbleFurVideo = true;
            }

           // sleep
            if (Logic.GetVideoFiles(_self._video_sleep_scene_path).Count > 0)
            {
                _isAbleSleepVideo = true;
            }

           // adv
            if (Logic.GetVideoFiles(_self._video_adv_scene_path).Count > 0)
            {
                _isAbleAdvVideo = true;
            }

            // UnityEngine.Debug.Log($">> _isAbleTitleVideo {_isAbleTitleVideo}");
            // UnityEngine.Debug.Log($">> _isAbleMyRoomVideo {_isAbleMyRoomVideo}");
            // UnityEngine.Debug.Log($">> _isAbleConciergeVideo {_isAbleConciergeVideo}");
            // UnityEngine.Debug.Log($">> _isAbleLobbyVideo {_isAbleLobbyVideo}");
            // UnityEngine.Debug.Log($">> _isAbleSleepVideo {_isAbleSleepVideo}");
            // UnityEngine.Debug.Log($">> _isAbleAdvVideo {_isAbleAdvVideo}");

            VideoModeActive = Config.Bind("InGame", "Video Play", true, new ConfigDescription("Enable/Disable"));
        }

        // private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        // {
        //     UnityEngine.Debug.Log($">> Scene Load {scene.name} | {DateTime.Now:HH:mm:ss.fff}");
        // }

#if HONEYSELECT
        protected override void LevelLoaded(int level)
        {
            if (level == 3)
                this.Init();
        }
#elif SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }

#elif KOIKATSU
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 1)
                Init();
        }
#endif

        protected override void Update()
       {
            if (_loaded == false)
                return;
        } 

        #region Private Methods
        private void Init()
        {
            _loaded = true;
        }

        private void SceneInit()
        {
        }
        #endregion

        #region Patches
// Title
        [HarmonyPatch(typeof(HS2.TitleScene), "Start")]
        private static class TitleScene_Start_Patches
        {
           private static void Postfix(HS2.TitleScene __instance)
           {
                UnityEngine.Debug.Log($">> Start in Title | {DateTime.Now:HH:mm:ss.fff}");

                if (_self._isAbleTitleVideo) {
                    Logic.PlayVideo(_self._video_title_scene_path);
                    _self.StartCoroutine(WaitTitleScene());
                } 
           }
        }

        private static IEnumerator WaitTitleScene()
        {
            // 영상 끝날 때까지 대기
            yield return new WaitUntil(() => _videoFinished);
        }

// Home
        [HarmonyPatch(typeof(HS2.HomeScene), "Start")]
        private static class HomeScene_Start_Patches
        {
           private static bool Prefix(HS2.HomeScene __instance)
           {
                UnityEngine.Debug.Log($">> Start in HomeScene {_reEntryHarmony} | {DateTime.Now:HH:mm:ss.fff}");
                if (_reEntryHarmony)
                {
                    _reEntryHarmony = false;
                    return true; // 원본 실행 허용
                }  
                
                if (_self._isAbleMyRoomVideo) {
                    _self.StartCoroutine(WaitHomeSceneCall(__instance));
                    return false;
                }

                return true; 
           }
        }

        private static IEnumerator WaitHomeSceneCall(HS2.HomeScene __instance)
        {
            string currentSceneName = SceneManager.GetActiveScene().name;
            UnityEngine.Debug.Log($">>currentSceneName {currentSceneName} in WaitHomeSceneCall | {Time.realtimeSinceStartup:F3}");

            yield return new WaitForEndOfFrame();
            Logic.PlayVideo(_self._video_myroom_scene_path, false);
            yield return new WaitUntil(() => _videoFinished);

            // 🔥 원 함수 실행
            var method = typeof(HS2.HomeScene)
                .GetMethod("Start", System.Reflection.BindingFlags.Instance | 
                                    System.Reflection.BindingFlags.NonPublic);

            _reEntryHarmony = true;
            if (method != null)
            {
                var enumerator = (IEnumerator)method.Invoke(__instance, null);
                yield return __instance.StartCoroutine(enumerator);
            }
        }

// Lobby
        [HarmonyPatch(typeof(HS2.LobbyScene), "Start")]
        private static class LobbyScene_Start_Patches
        {
           private static bool Prefix(HS2.LobbyScene __instance)
           {
                UnityEngine.Debug.Log(Environment.StackTrace);
                UnityEngine.Debug.Log($">> Start in LobbyScene {_reEntryHarmony} | {DateTime.Now:HH:mm:ss.fff}");
                if (_reEntryHarmony)
                {
                    _reEntryHarmony = false;
                    return true; // 원본 실행 허용
                }  
                
                if (_self._isAbleLobbyVideo) {
                    _self.StartCoroutine(WaitLobbySceneCall(__instance));
                    return false;
                }

                return true; 
           }
        }

       private static IEnumerator WaitLobbySceneCall(HS2.LobbyScene __instance)
        {
            string currentSceneName = SceneManager.GetActiveScene().name;
            UnityEngine.Debug.Log($">> currentSceneName {currentSceneName} in WaitLobbySceneCall | {Time.realtimeSinceStartup:F3}");

            yield return new WaitForEndOfFrame();
            Logic.PlayVideo(_self._video_lobby_scene_path, false);
            yield return new WaitUntil(() => _videoFinished);

            // 🔥 원 함수 실행
            var method = typeof(HS2.LobbyScene)
                .GetMethod("Start", System.Reflection.BindingFlags.Instance | 
                                    System.Reflection.BindingFlags.NonPublic);

            _reEntryHarmony = true;
            if (method != null)
            {
                var enumerator = (IEnumerator)method.Invoke(__instance, null);
                yield return __instance.StartCoroutine(enumerator);
            }
        }

// FurRoom
        [HarmonyPatch(typeof(HS2.FurRoomScene), "Start")]
        private static class FurRoomScene_Start_Patches
        {
           private static bool Prefix(HS2.FurRoomScene __instance)
           {
                UnityEngine.Debug.Log($">> Start in FurRoomScene {_reEntryHarmony} | {DateTime.Now:HH:mm:ss.fff}");
                if (_reEntryHarmony)
                {
                    _reEntryHarmony = false;
                    return true; // 원본 실행 허용
                }  
                
                if (_self._isAbleMyRoomVideo) {
                    _self.StartCoroutine(WaitFurRoomSceneCall(__instance));
                    return false;
                }

                return true; 
           }
        }

        private static IEnumerator WaitFurRoomSceneCall(HS2.FurRoomScene __instance)
        {
            string currentSceneName = SceneManager.GetActiveScene().name;
            UnityEngine.Debug.Log($">> currentSceneName {currentSceneName} in WaitFurRoomSceneCall | {Camera.main} | {Time.realtimeSinceStartup:F3}");

            yield return new WaitForEndOfFrame();
            Logic.PlayVideo(_self._video_furroom_scene_path, false);
            yield return new WaitUntil(() => _videoFinished);

            // 🔥 원 함수 실행
            var method = typeof(HS2.FurRoomScene)
                .GetMethod("Start", System.Reflection.BindingFlags.Instance | 
                                    System.Reflection.BindingFlags.NonPublic);

            _reEntryHarmony = true;
            if (method != null)
            {
                var enumerator = (IEnumerator)method.Invoke(__instance, null);
                yield return __instance.StartCoroutine(enumerator);
            }
        }

// Concierge
        [HarmonyPatch(typeof(HS2.HomeUI), "CallConcierge")]
        private static class HomeUI_CallConcierge_Patches
        {
            private static bool Prefix(HS2.HomeUI __instance)
            {
                UnityEngine.Debug.Log($">> action CallConcierge | {DateTime.Now:HH:mm:ss.fff}");
                if (_reEntryHarmony)
                {
                    _reEntryHarmony = false;
                    return true; // 원본 실행 허용
                }  

                if (_self._isAbleConciergeVideo) {
                    _self.StartCoroutine(WaitCallConcierge(__instance));
                    return false; // 원본 StartFade 실행 차단  
                } 
                
                return true;
            }
        }

       
        private static IEnumerator WaitCallConcierge(
            HS2.HomeUI __instance)
        {
            string currentSceneName = SceneManager.GetActiveScene().name;
            UnityEngine.Debug.Log($">> currentSceneName {currentSceneName} in WaitCallConcierge |{Time.realtimeSinceStartup:F3}");

            yield return new WaitForEndOfFrame();
            Logic.PlayVideo(_self._video_concierge_scene_path, false);
            yield return new WaitUntil(() => _videoFinished);
            
            // 🔥 원 함수 실행
            var method = typeof(HS2.HomeUI)
                .GetMethod("CallConcierge", System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.NonPublic);
            _reEntryHarmony = true;

            if (method != null)
            {
                method.Invoke(__instance, null);
            }
        }

// ADV
        [HarmonyPatch(typeof(ADV.ADVMainScene), "Start")]
        private static class ADVMainScene_Start_Patches
        {
            private static bool Prefix(ADV.ADVMainScene __instance)
            {

                Scene scene = SceneManager.GetActiveScene();
                UnityEngine.Debug.Log($">> start in ADVMainScene {scene.name}, {_reEntryHarmony} | {DateTime.Now:HH:mm:ss.fff}");
                if (_reEntryHarmony)
                {
                    _reEntryHarmony = false;
                    return true;
                }

                if (_self._isAbleAdvVideo) {
                    _self.StartCoroutine(WaitAdvSceneCall(__instance));
                    return false;
                }

                // if(scene.name.Equals("PublicBath"))
                // {
                // }
                // else if(scene.name.Equals("FrontOfBath"))
                // {

                // }
                // else if(scene.name.Equals("SuiteRoom"))
                // {

                // }
                // else if(scene.name.Equals("Lobby"))
                // {

                // }
                // else if(scene.name.Equals("Japanese"))
                // {

                // }
                // else if(scene.name.Equals("TortureRoom"))
                // {

                // }
                // else if(scene.name.Equals("Garden_suny"))
                // {

                // }
                // else if(scene.name.Equals("Japanese"))
                // {

                // }
                // else if(scene.name.Equals("MyRoom"))
                // {

                // }

                return true;
            }
        }

        private static IEnumerator WaitAdvSceneCall(ADV.ADVMainScene __instance)
        {
            //Manager.Scene.sceneFadeCanvas.StartFade(FadeCanvas.Fade.Out, false);
            //Manager.Scene.sceneFadeCanvas.Reset();

            string currentSceneName = SceneManager.GetActiveScene().name;
            UnityEngine.Debug.Log($">> currentSceneName {currentSceneName} in WaitAdvSceneCall | {Camera.main} | {Time.realtimeSinceStartup:F3}");

            yield return new WaitForEndOfFrame();
            Logic.PlayVideo(_self._video_adv_scene_path, false);
            yield return new WaitUntil(() => _videoFinished);

            // 🔥 원 함수 실행
            var method = typeof(ADV.ADVMainScene)
                .GetMethod("Start", System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.NonPublic);
            _reEntryHarmony = true;

            if (method != null)
            {
                method.Invoke(__instance, null);
            }
        }
// HSCene
        [HarmonyPatch(typeof(Manager.HSceneManager), "Start")]
        private static class HSceneManager_Start_Patches
        {
            private static bool Prefix(Manager.HSceneManager __instance)
            {
                UnityEngine.Debug.Log($">> Start in HSceneManager | {DateTime.Now:HH:mm:ss.fff}");
                return true;
            }
        }

        [HarmonyPatch(typeof(Manager.HSceneManager), "SetFemaleState", typeof(ChaControl[]))]
        private static class HSceneManager_SetFemaleState_Patches
        {
            private static void Postfix(Manager.HSceneManager __instance, ChaControl[] female)
            {
                UnityEngine.Debug.Log($">> SetFemaleState in HSceneManager | {DateTime.Now:HH:mm:ss.fff}");
                foreach (ChaControl eachFemale in female)
                {
                    if (eachFemale != null)
                    {
                        UnityEngine.Debug.Log($">> eachFemale {eachFemale.chaFile.parameter.fullname}");
                    }
                }

				SaveData saveData = Singleton<Manager.Game>.Instance.saveData;
                if (saveData != null) {
                    UnityEngine.Debug.Log($">> instance.hero {saveData.hCount}");
                }

                Manager.Game instance = Singleton<Manager.Game>.Instance; 

                if (instance != null)
                {
                    // Manager.Game.Instance.saveData 내에 player 정보 저장 
                    UnityEngine.Debug.Log($">> instance.heroineList {instance.heroineList.Count}");
                }
            }
        }

        [HarmonyPatch(typeof(HScene), "StartAnim", typeof(HScene.AnimationListInfo))]
        private static class HScene_StartAnim_Patches
        {
            private static bool Prefix(HScene __instance, HScene.AnimationListInfo StartAnimInfo)
            {
               UnityEngine.Debug.Log($">> StartAnim in HScene {StartAnimInfo.nameAnimation}");
               return true;
            }
        }

        [HarmonyPatch(typeof(HScene), "ChangeAnimation", typeof(HScene.AnimationListInfo), typeof(bool), typeof(bool), typeof(bool))]
        private static class HScene_ChangeAnimation_Patches
        {
            private static bool Prefix(HScene __instance, HScene.AnimationListInfo _info, bool _isForceResetCamera, bool _isForceLoopAction = false, bool _UseFade = true)
            {
               UnityEngine.Debug.Log($">> ChangeAnimation in HScene {_info.nameAnimation}");
               return true;
            }
        }

        // [HarmonyPatch(typeof(HScene), "ChangeCoodinate", typeof(int), typeof(int), typeof(ChaControl), typeof(bool))]
        // private static class HScene_ChangeCoodinate_Patches
        // {
        //     private static bool Prefix(HScene __instance, int EventNo, int peep, ChaControl cha, bool Second = false)
        //     {
        //        UnityEngine.Debug.Log($">> ChangeCoodinate in HScene sex {cha.sex}, name {cha.chaFile.parameter.fullname}, peep {peep}, eventNo {EventNo}");
        //        return true;
        //     }
        // }

        [HarmonyPatch(typeof(HSceneSpriteClothCondition), "OnClickAllCloth")]
        private static class HSceneSpriteClothCondition_OnClickAllCloth_Patches
        {
            private static void Postfix(HSceneSpriteClothCondition __instance)
            {
               UnityEngine.Debug.Log($">> OnClickAllCloth in HSceneSpriteClothCondition");
            }
        }

        [HarmonyPatch(typeof(HSceneSpriteClothCondition), "OnClickCloth", typeof(int))]
        private static class HSceneSpriteClothCondition_OnClickCloth_Patches
        {
            private static void Postfix(HSceneSpriteClothCondition __instance, int _cloth)
            {
               UnityEngine.Debug.Log($">> OnClickCloth in HSceneSpriteClothCondition {_cloth}");
            }
        }

        [HarmonyPatch(typeof(HSceneSprite), "OnClickFinishBefore")]
        private static class HSceneSprite_OnClickFinishBefore_Patches
        {
            private static bool Prefix(HSceneSprite __instance)
            {
               UnityEngine.Debug.Log($">> OnClickFinishBefore in HSceneSprite");
               return true;
            }
        }

        [HarmonyPatch(typeof(HSceneSprite), "OnClickFinish")]
        private static class HSceneSprite_OnClickFinish_Patches
        {
            private static bool Prefix(HSceneSprite __instance)
            {
               UnityEngine.Debug.Log($">> OnClickFinish in HSceneSprite");
               return true;
            }
        }

        [HarmonyPatch(typeof(Manager.HSceneManager), "EndHScene")]
        private static class HSceneManager_EndHScene_Patches
        {
            private static bool Prefix(Manager.HSceneManager __instance)
            {
               UnityEngine.Debug.Log($">> EndHScene in HSceneManager");
               return true;
            }
        }

// Wait
        #endregion

        // Home Scene Option

        // Title Scene
        [HarmonyPatch(typeof(HS2.TitleScene), "OnPlay")]
        private static class TitleScene_OnPlay_Patches
        {
           private static void Postfix(HS2.TitleScene __instance)
           {    
                if (!VideoModeActive.Value)
                    return;

                if (_self._sceneVideoPlayer.isPlaying)
                {
                    _self._sceneVideoPlayer.Pause();
                }

                Logic.StopSceneVideo();
            }
        }
        
        [HarmonyPatch(typeof(HS2.TitleScene), "OnUpload")]
        private static class TitleScene_OnUpload_Patches
        {
           private static void Postfix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return;

               if (_self._sceneVideoPlayer.isPlaying)
               {
                  _self._sceneVideoPlayer.Pause();
               }
           }
        }

        [HarmonyPatch(typeof(HS2.TitleScene), "OnDownload")]
        private static class TitleScene_OnDownload_Patches
        {
           private static void Postfix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return;

               if (_self._sceneVideoPlayer.isPlaying)
               {
                  _self._sceneVideoPlayer.Pause();
               } 
           }
        }

        [HarmonyPatch(typeof(HS2.TitleScene), "OnDownloadAI")]
        private static class TitleScene_OnDownloadAI_Patches
        {
           private static void Postfix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return;

               if (_self._sceneVideoPlayer.isPlaying)
               {
                  _self._sceneVideoPlayer.Pause();
               }     
           }
        }

        [HarmonyPatch(typeof(HS2.TitleScene), "OnMakeFemale")]
        private static class TitleScene_OnMakeFemale_Patches
        {
           private static void Postfix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return;

                if (_self._sceneVideoPlayer.isPlaying)
                {
                    _self._sceneVideoPlayer.Pause();
                }

                Logic.StopSceneVideo();
            }
        }

        [HarmonyPatch(typeof(HS2.TitleScene), "OnMakeMale")]
        private static class TitleScene_OnMakeMale_Patches
        {
           private static void Postfix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return;

                if (_self._sceneVideoPlayer.isPlaying)
                {
                    _self._sceneVideoPlayer.Pause();
                }

                Logic.StopSceneVideo();
            }
        }

// Common
        [HarmonyPatch(typeof(Manager.BaseMap), "ChangeAsync", typeof(int), typeof(FadeCanvas.Fade), typeof(bool))]
        private static class BaseMap_ChangeAsync_Patches
        {
           private static bool Prefix(Manager.BaseMap __instance, int _no, FadeCanvas.Fade fadeType = FadeCanvas.Fade.InOut, bool isForce = false)
           {    
                Scene scene = SceneManager.GetActiveScene();

                if (!VideoModeActive.Value)
                    return true;

                if (scene.name.Equals("Title") || scene.name.Equals("NightPool"))
                { 
                    // title 맵은 로딩 제외
                    if(_self._isAbleTitleVideo && _no == 18)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Actor.CharaData), "SetRoot", typeof(GameObject))]
        private static class CharaData_SetRoot_Patches
        {
            private static bool Prefix(Actor.CharaData __instance, GameObject root)
            {
                // UnityEngine.Debug.Log(Environment.StackTrace);
                Scene scene = SceneManager.GetActiveScene();
                
                if (!VideoModeActive.Value)
                    return true;

                if (__instance != null && root != null)
                    UnityEngine.Debug.Log($">> SetRoot {root.name}, scene {scene.name}, charName {__instance.Name}, charBirthDay {__instance.birthMonth}/{__instance.birthDay} | {DateTime.Now:HH:mm:ss.fff}");

                if (scene.name.Equals("Title") || scene.name.Equals("NightPool"))
                {
                    if (_self._isAbleTitleVideo)
                    {
                        root.SetActive(false);
                    }
                }

                return true;
            }
        }
    }

    #endregion
}