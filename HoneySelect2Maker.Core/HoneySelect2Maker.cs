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

        internal string[] _myroom_requiredFiles =
        {
            "idle.mp4",
            "option_menu.mp4",
            "calling.mp4",
            "concierge_in.mp4",
            "concierge_idle.mp4"
        };

        internal string _myroom_scene_stage = "idle";


        internal GameObject titleSceneVideoObj;
        internal GameObject myroomSceneVideoObj;

        internal UnityEngine.Video.VideoPlayer sceneVideoPlayer;

        internal bool _isAvaiableTitleVideo;
        internal bool _isAvaiableMyRoomVideo;
        
#if FEATURE_IK_INGAME
        internal bool _isNeckPressed;
#endif


        private static string _assemblyLocation;
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
            sceneVideoPlayer = videoObj.AddComponent<UnityEngine.Video.VideoPlayer>();

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            // title
            if (GetVideoFiles(_self._video_title_scene_path).Count > 0)
            {
                _isAvaiableTitleVideo = true;
            }        

            // myroom
            List<string> myroomfileList = GetVideoFiles(_self._video_myroom_scene_path);
            _isAvaiableMyRoomVideo = true;
            foreach (var required in _myroom_requiredFiles)
            {
                if (!myroomfileList.Contains(required))
                {
                    // UnityEngine.Debug.Log($">> false ?? {required}");
                    _isAvaiableMyRoomVideo = false;
                    break;
                }
            }

            UnityEngine.Debug.Log($">> _isAvaiableTitleVideo {_isAvaiableTitleVideo}");
            UnityEngine.Debug.Log($">> _isAvaiableMyRoomVideo {_isAvaiableMyRoomVideo}");

            VideoModeActive = Config.Bind("InGame", "Video Play", true, new ConfigDescription("Enable/Disable"));
        }

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

        
#if FEATURE_IK_INGAME           
        protected override void Update()
        {
            if (_loaded == false)
                return;

            if (Input.GetMouseButtonDown(0)) {
                CheckNeckClick();
            }
        }

        protected override void LateUpdate() {
            if (_loaded == false)
                return;

            // 마우스가 놓였을때.. 처리
            if (_isNeckPressed) {
                if (_ociCharMgmt.Count > 0) {                
                    foreach (var kvp in _self._ociCharMgmt) {
                        RealHumanData realHumanData = kvp.Value;

                        if (realHumanData != null && realHumanData.charControl != null) {

                            float yaw   = Input.GetAxis("Mouse X") * 2.0f;
                            float pitch = -Input.GetAxis("Mouse Y") * 2.0f;     
                            Logic.UpdateIKHead(realHumanData, yaw, pitch);
                            Logic.ReflectIKToAnimation(realHumanData);
                        }
                    }
                }                
            }
        }

        void CheckNeckClick()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            if (!Physics.Raycast(ray, out RaycastHit hit, 50f))
                return;

            if (IsNeckClicked(hit))
            {
                _isNeckPressed = true;
                // 여기서 neck 선택 처리                
            } else {
                _isNeckPressed = false;
            }
        }

        bool IsNeckClicked(RaycastHit hit)
        {
            // Raycast에 맞은 Collider가
            // neck 본의 자식인가?
            foreach (var kvp in _ociCharMgmt)
            {
                RealHumanData realHumanData = kvp.Value;
                if (realHumanData != null && realHumanData.charControl != null)
                {
                    if (hit.collider.transform.IsChildOf(realHumanData.head_ik_data.neck))
                        return true;
                }
            }

            return false;
        }
#else
        protected override void Update()
       {
            if (_loaded == false)
                return;
        }
      
#endif    

        #region Private Methods
        private void Init()
        {
            _loaded = true;
        }

        private void SceneInit()
        {
            // UnityEngine.Debug.Log($">> SceneInit()");
        }

        private IEnumerator ExecuteAfterFrame(ChaControl chaControl, RealHumanData realHumanData)
        {
            int frameCount = 30;
            for (int i = 0; i < frameCount; i++)
                yield return null;

#if FEATURE_IK_INGAME
            Logic.SupportIKOnScene(chaControl, realHumanData);
#endif            
        }   

        #endregion

        #region Public Methods
        public void OnVideoEnd() {

        }
        #endregion

        #region Patches

        public static List<string> GetVideoFiles(string folderPath)
        {
            List<string> mp4List = new List<string>();

            if (!Directory.Exists(folderPath))
            {
                return mp4List;
            }

            string[] files = Directory.GetFiles(folderPath, "*.mp4");

            foreach (string filePath in files)
            {
                // 확장자 포함 파일명만
                string fileName = Path.GetFileName(filePath);
                mp4List.Add(fileName);
            }

            return mp4List;
        }

        private static void PlaySceneVideo(string videoPath, bool isLoop = false) {

            // UnityEngine.Debug.Log($">> PlaySceneVideo in Scene");
            // 1. 메인 카메라 확보
            Camera cam = Camera.main ;
            if (cam == null)
            {
                UnityEngine.Debug.LogError("Main Camera not found");
                return;
            }

            if (_self.sceneVideoPlayer.isPaused)
            {
                _self.sceneVideoPlayer.targetCamera = cam;
                _self.sceneVideoPlayer.Play();
            } 
            else 
            {
                // 1. 비디오 설정
                _self.sceneVideoPlayer.source = UnityEngine.Video.VideoSource.Url;
                _self.sceneVideoPlayer.url = videoPath;

                _self.sceneVideoPlayer.playOnAwake = false;
                _self.sceneVideoPlayer.isLooping = isLoop;
                _self.sceneVideoPlayer.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;

                // 2. 카메라 Near Plane 출력 (Quad 없음)
                _self.sceneVideoPlayer.renderMode = UnityEngine.Video.VideoRenderMode.CameraNearPlane;
                _self.sceneVideoPlayer.targetCamera = cam;

                // 화면 채우기 옵션
                _self.sceneVideoPlayer.aspectRatio = UnityEngine.Video.VideoAspectRatio.FitVertically;
                _self.sceneVideoPlayer.targetCameraAlpha = 1.0f;

                // 3. 준비 후 재생
                _self.sceneVideoPlayer.prepareCompleted -= OnPrepared; // ★ 추가
                _self.sceneVideoPlayer.prepareCompleted += OnPrepared; // ★ 교체
                _self.sceneVideoPlayer.Prepare();
            }
        }

        private static void StopPlaySceneVideo()
        {
            _self.sceneVideoPlayer.Stop();
        }

        private static void OnPrepared(UnityEngine.Video.VideoPlayer vp)
        {
            vp.prepareCompleted -= OnPrepared; // 1회용
            vp.Play();
            // UnityEngine.Debug.Log($">> OnPrepared {vp.isLooping}");
        }

        // private static void PlayMyroomSceneVideo(string videoPath) {

        //     UnityEngine.Debug.Log($">> PlayMyroomSceneVideo in myroomScene");
        //     // 1. 메인 카메라 확보
        //     Camera cam = Camera.main ;
        //     if (cam == null)
        //     {
        //         UnityEngine.Debug.LogError("Main Camera not found");
        //         return;
        //     }

        //     if (_self.myroomSceneVideoPlayer.isPaused)
        //     {
        //         _self.myroomSceneVideoPlayer.targetCamera = cam;
        //         _self.myroomSceneVideoPlayer.Play();
        //     } 
        //     else 
        //     {
        //         // 1. 비디오 설정
        //         _self.myroomSceneVideoPlayer.source = UnityEngine.Video.VideoSource.Url;
        //         _self.myroomSceneVideoPlayer.url = videoPath;

        //         _self.myroomSceneVideoPlayer.playOnAwake = false;
        //         _self.myroomSceneVideoPlayer.isLooping = true;
        //         _self.myroomSceneVideoPlayer.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;

        //         // 2. 카메라 Near Plane 출력 (Quad 없음)
        //         _self.myroomSceneVideoPlayer.renderMode = UnityEngine.Video.VideoRenderMode.CameraNearPlane;
        //         _self.myroomSceneVideoPlayer.targetCamera = cam;

        //         // 화면 채우기 옵션
        //         _self.myroomSceneVideoPlayer.aspectRatio = UnityEngine.Video.VideoAspectRatio.FitVertically;
        //         _self.myroomSceneVideoPlayer.targetCameraAlpha = 1.0f;

        //         // 3. 준비 후 재생
        //         _self.myroomSceneVideoPlayer.Prepare();
        //         _self.myroomSceneVideoPlayer.prepareCompleted += _ =>
        //         {
        //             _self.myroomSceneVideoPlayer.Play();
        //         };   
        //     }
        // }

        // private static void RemoveActorsAndMap(Scene scene)
        // {
        //     GameObject[] roots = scene.GetRootGameObjects();

        //     foreach (GameObject _root in roots)
        //     {    
        //        if (_root == null)
        //            continue;

        //        foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
        //        {    
        //            if ( t == null)
        //                continue;

        //            GameObject go = t.gameObject;
                    
        //            if (go == null)
        //                continue;

        //            if (go.name.Contains("chaF_001") || go.name.Contains("chaF_002") || go.name.Contains("chaM_001") || go.name.Contains("Map") || go.name.Contains("Reflection") || go.name.Contains("Background"))
        //            {
        //                go.SetActive(false);
        //            //    UnityEngine.Debug.Log($"inactive {go.name}");
        //            }

        //            // UnityEngine.Debug.Log(go.name);
        //        }
        //     }
        // }

        // [HarmonyPatch(typeof(Manager.Sound), "Play",  typeof(Manager.Sound.Loader))]
        // private static class Sound_Play_Patches
        // {
        //    private static bool Prefix(Manager.Sound __instance, Manager.Sound.Loader loader)
        //    {    
        //         // UnityEngine.Debug.Log(Environment.StackTrace);
        //         Scene scene = SceneManager.GetActiveScene();
        //         // UnityEngine.Debug.Log($">> Play in Sound  {loader.bundle}, {loader.asset}");

        //         if (scene.name.Contains("Title") || scene.name.Contains("NightPool"))
        //         {
        //             // title 맵은 로딩 제외
        //             if (_self._isAvaiableTitleVideo && loader.asset.Contains("hs2a_bgm_00"))  // title 에서 배경음
        //             {
        //                 return false;
        //             }
        //         }

        //         return true;
        //     }
        // }

// Common

        [HarmonyPatch(typeof(Actor.CharaData), "SetRoot", typeof(GameObject))]
        private static class CharaData_SetRoot_Patches
        {
            private static bool Prefix(Actor.CharaData __instance, GameObject root)
            {
                // UnityEngine.Debug.Log(Environment.StackTrace);

                Scene scene = SceneManager.GetActiveScene();
                // UnityEngine.Debug.Log($">> SetRoot {root.name}, scene {scene.name}");

                if (scene.name.Contains("Title") || scene.name.Contains("NightPool"))
                {
                    if (_self._isAvaiableTitleVideo)
                    {
                        root.SetActive(false);
                    }
                }
                else if (scene.name.Contains("Home") || scene.name.Contains("MyRoom"))// myroom에는 컨시어지 캐릭터 존재할 수 있음..
                {
                    if (_self._isAvaiableMyRoomVideo)
                    {
                        root.SetActive(true);
                    }
                }

                return true;
            }
        }


        [HarmonyPatch(typeof(Manager.BaseMap), "ChangeAsync", typeof(int), typeof(FadeCanvas.Fade), typeof(bool))]
        private static class BaseMap_ChangeAsync_Patches
        {
           private static bool Prefix(Manager.BaseMap __instance, int _no, FadeCanvas.Fade fadeType = FadeCanvas.Fade.InOut, bool isForce = false)
           {    
                // UnityEngine.Debug.Log(Environment.StackTrace);
                Scene scene = SceneManager.GetActiveScene();
                UnityEngine.Debug.Log($">> ChangeAsync in basemap _no {_no}, scene {scene.name}");

                if (scene.name.Contains("Title") || scene.name.Contains("NightPool"))
                { 
                    // title 맵은 로딩 제외
                    if(_self._isAvaiableTitleVideo && _no == 18)
                    {
                        return false;
                    }
                } 
                else if (scene.name.Contains("Home") || scene.name.Contains("MyRoom"))
                {
                    // myroom 맵은 로딩 제외
                    if(_self._isAvaiableMyRoomVideo == true)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        // 장면 동작 구동 시
        [HarmonyPatch(typeof(FadeCanvas), "StartFade", typeof(FadeCanvas.Fade), typeof(bool))]
        private static class FadeCanvas_StartFade_Patches
        {
           private static bool Prefix(FadeCanvas __instance, FadeCanvas.Fade fade, bool throwOnError = false)
           {
                if (!VideoModeActive.Value)
                    return true;

                Scene scene = SceneManager.GetActiveScene();
                List<string> video_files  = new List<string>();
                string video_path = "";
                bool isLoop = false;

                UnityEngine.Debug.Log($">> StartFade scene {scene.name}");

                if (scene.name.Contains("Title") || scene.name.Contains("NightPool")) {
                    if (_self._isAvaiableTitleVideo) {
                        video_path = _self._video_title_scene_path;
                        video_files =  GetVideoFiles(video_path);

                        if (video_files.Count > 0)
                        {                      
                            int idx = UnityEngine.Random.Range(0, Mathf.Min(VIDEO_MAX_COUNT, video_files.Count));
                            string path = video_path + video_files[idx];

                            if (video_files[idx].Contains("loop_") || video_files[idx].Contains("_loop") || video_files[idx].Contains("lp_") || video_files[idx].Contains("_lp"))
                            {
                                isLoop = true;
                            }

                            StopPlaySceneVideo();
                            PlaySceneVideo(path, isLoop);
                        }
                    }
                }
                else if (scene.name.Contains("Home") || scene.name.Contains("MyRoom")) {
                    if (_self._isAvaiableMyRoomVideo) {
                        string path = _self._video_myroom_scene_path + _self._myroom_scene_stage + ".mp4";

                        if (_self._myroom_scene_stage.Contains("idle"))
                        {
                            isLoop = true;
                        }
                        
                        StopPlaySceneVideo();
                        PlaySceneVideo(path, isLoop);
                    }

                    // UnityEngine.Debug.Log($">> play {} in myroom");

                }
                

                // if (video_files.Count > 0)
                // {                      
                //     int idx = UnityEngine.Random.Range(0, Mathf.Min(VIDEO_MAX_COUNT, video_files.Count));
                //     string path = video_path + video_files[idx];
                //     bool isLoop = false;

                //     if (video_files[idx].Contains("loop_") || video_files[idx].Contains("_loop") || video_files[idx].Contains("lp_") || video_files[idx].Contains("_lp"))
                //     {
                //         isLoop = true;
                //     }

                //     // if (scene.Equals("title")) {
                //     //     PlaySceneVideo(path, isLoop);
                //     // }
                //     // else if (scene.Equals("myroom"))
                //     // {
                        
                //     // }
                // }

               return true;        
           }
        }

// Title Scene
        [HarmonyPatch(typeof(HS2.TitleScene), "OnPlay")]
        private static class TitleScene_OnPlay_Patches
        {
           private static bool Prefix(HS2.TitleScene __instance)
           {    
                if (!VideoModeActive.Value)
                    return true;

                if (_self.sceneVideoPlayer.isPlaying)
                {
                    _self.sceneVideoPlayer.Pause();
                }

                return true;
            }
        }
        
        [HarmonyPatch(typeof(HS2.TitleScene), "OnUpload")]
        private static class TitleScene_OnUpload_Patches
        {
           private static bool Prefix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return true;

               if (_self.sceneVideoPlayer.isPlaying)
               {
                  _self.sceneVideoPlayer.Pause();
               }
               
               return true;       
           }
        }

        [HarmonyPatch(typeof(HS2.TitleScene), "OnDownload")]
        private static class TitleScene_OnDownload_Patches
        {
           private static bool Prefix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return true;

               if (_self.sceneVideoPlayer.isPlaying)
               {
                  _self.sceneVideoPlayer.Pause();
               }
               
               return true;       
           }
        }

        [HarmonyPatch(typeof(HS2.TitleScene), "OnDownloadAI")]
        private static class TitleScene_OnDownloadAI_Patches
        {
           private static bool Prefix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return true;

               if (_self.sceneVideoPlayer.isPlaying)
               {
                  _self.sceneVideoPlayer.Pause();
               }
               
               return true;       
           }
        }

        [HarmonyPatch(typeof(HS2.TitleScene), "OnMakeFemale")]
        private static class TitleScene_OnMakeFemale_Patches
        {
           private static bool Prefix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return true;

                if (_self.sceneVideoPlayer.isPlaying)
                {
                    _self.sceneVideoPlayer.Pause();
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(HS2.TitleScene), "OnMakeMale")]
        private static class TitleScene_OnMakeMale_Patches
        {
           private static bool Prefix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return true;

                if (_self.sceneVideoPlayer.isPlaying)
                {
                    _self.sceneVideoPlayer.Pause();
                }

                return true;
            }
        }

// Myroom Scene
        [HarmonyPatch(typeof(HS2.HomeUI), "OpenOption")]
        private static class HomeUI_OpenOption_Patches
        {
            private static bool Prefix(HS2.HomeUI __instance)
            {
                _self._myroom_scene_stage = "option_menu";
                return true;
            }
        }

        [HarmonyPatch(typeof(HS2.HomeUI), "CallConcierge")]
        private static class HomeUI_CallConcierge_Patches
        {
            private static bool Prefix(HS2.HomeUI __instance)
            {
                _self._myroom_scene_stage = "calling";
                return true;
            }
        }

        [HarmonyPatch(typeof(HS2.HomeUI), "Sleep")]
        private static class HomeUI_Sleep_Patches
        {
            private static bool Prefix(HS2.HomeUI __instance)
            {
                return true;
            }
        }

        // [HarmonyPatch(typeof(Manager.HomeSceneManager), "AnimationDoor", typeof(bool))]
        // private static class HomeSceneManager_AnimationDoor_Patches
        // {
        //    private static bool Prefix(Manager.HomeSceneManager __instance, bool _isOpen)
        //    {
        //        UnityEngine.Debug.Log($">> AnimationDoor {_isOpen}");

        //        UnityEngine.Debug.Log(Environment.StackTrace);
        //        return true;        
        //    }
        // }     

        // [HarmonyPatch(typeof(Manager.HomeSceneManager), "AnimationDesk", typeof(bool))]
        // private static class HomeSceneManager_AnimationDesk_Patches
        // {
        //    private static bool Prefix(Manager.HomeSceneManager __instance, bool _isOpen)
        //    {
        //        UnityEngine.Debug.Log($">> AnimationDesk {_isOpen}");

        //        UnityEngine.Debug.Log(Environment.StackTrace);
        //        return true;        
        //    }
        // }     

        // [HarmonyPatch(typeof(Manager.HomeSceneManager), "AnimationMenu", typeof(bool))]
        // private static class HomeSceneManager_AnimationMenu_Patches
        // {
        //    private static bool Prefix(Manager.HomeSceneManager __instance, bool _isOpen)
        //    {
        //        UnityEngine.Debug.Log($">> AnimationMenu {_isOpen}");

        //        UnityEngine.Debug.Log(Environment.StackTrace);
        //        return true;        
        //    }
        // }

// Voice
        // [HarmonyPatch(typeof(Manager.Voice), "Play", typeof(Manager.Voice.Loader))]
        // private static class Voice_Play_Patches
        // {
        //    private static bool Prefix(Manager.Voice __instance, Manager.Voice.Loader loader)
        //    {
        //        UnityEngine.Debug.Log($">> Play Voice {loader.bundle}, {loader.asset}");
        //     //    UnityEngine.Debug.Log(Environment.StackTrace);
        //        return true;        
        //    }
        // }

        // [HarmonyPatch(typeof(Manager.Voice), "PlayAsync", typeof(Manager.Voice.Loader), typeof(Action<AudioSource>))]
        // private static class Voice_PlayAsync_Patches
        // {
        //    private static bool Prefix(Manager.Voice __instance, Manager.Voice.Loader loader, Action<AudioSource> action)
        //    {
        //        UnityEngine.Debug.Log($">> OncePlay Voice {loader.bundle}, {loader.asset} in HScene");
        //        return true;        
        //    }
        // }

        // [HarmonyPatch(typeof(Manager.Voice), "OncePlayAsync", typeof(Manager.Voice.Loader), typeof(Action<AudioSource>))]
        // private static class Voice_OncePlayAsync_Patches
        // {
        //    private static bool Prefix(Manager.Voice __instance, Manager.Voice.Loader loader, Action<AudioSource> action)
        //    {
        //        UnityEngine.Debug.Log($">> OncePlay Voice {loader.bundle}, {loader.asset} in HScene");
        //        return true;        
        //    }
        // }

// Advance Scene
        // [HarmonyPatch(typeof(ADV.TextScenario), "ConfigProc")]
        // private static class TextScenario_ConfigProc_Patches
        // {
        //    private static void Postfix(ADV.TextScenario __instance)
        //     {
        //         Scene scene = SceneManager.GetActiveScene();
        //         UnityEngine.Debug.Log($">> ConfigProc scene {scene.name} in ADV");

        //         List<string> mp4files = new List<string>();
        //         string adv_path = "";

        //         _self.sceneVideoPlayer.Stop();  

        //         // if(scene.name.Contains("Home"))
        //         // {
        //         //     mp4files = GetVideoFiles(_self._video_adv_home_scene_path);
        //         //     adv_path = _self._video_adv_home_scene_path;
        //         // }
        //         // else if(scene.name.Contains("MyRoom"))
        //         // {
        //         //     mp4files = GetVideoFiles(_self._video_adv_myroom_scene_path);
        //         //     adv_path = _self._video_adv_myroom_scene_path;
        //         // }
        //         if(scene.name.Contains("Lobby"))
        //         {
        //             mp4files = GetVideoFiles(_self._video_adv_lobby_scene_path);
        //             adv_path = _self._video_adv_lobby_scene_path;
        //         } 
        //         else if(scene.name.Contains("PublicBath"))
        //         {
        //             mp4files = GetVideoFiles(_self._video_adv_publicbath_scene_path);
        //             adv_path = _self._video_adv_publicbath_scene_path;
        //         }
        //         else if(scene.name.Contains("Shower"))
        //         {
        //             mp4files = GetVideoFiles(_self._video_adv_shower_scene_path);
        //             adv_path = _self._video_adv_shower_scene_path;
        //         }
        //         else if(scene.name.Contains("FrontOfBath"))
        //         {
        //             mp4files = GetVideoFiles(_self._video_adv_bath_scene_path);
        //             adv_path = _self._video_adv_bath_scene_path;
        //         }
        //         else if(scene.name.Contains("SuiteRoom"))
        //         {
        //             mp4files = GetVideoFiles(_self._video_adv_girlsroom_scene_path);
        //             adv_path = _self._video_adv_girlsroom_scene_path;
        //         }
        //         else if(scene.name.Contains("Japanese"))
        //         {
        //             mp4files = GetVideoFiles(_self._video_adv_tatamiroom_scene_path);
        //             adv_path = _self._video_adv_tatamiroom_scene_path;
        //         }
        //         else if(scene.name.Contains("TortureRoom"))
        //         {
        //             mp4files = GetVideoFiles(_self._video_adv_tortureroom_scene_path);
        //             adv_path = _self._video_adv_tortureroom_scene_path;
        //         }
        //         else if(scene.name.Contains("Garden_suny"))
        //         {
        //             mp4files = GetVideoFiles(_self._video_adv_backyard_scene_path);
        //             adv_path = _self._video_adv_backyard_scene_path;
        //         }


        //         if (mp4files.Count > 0)
        //         {
    
        //             GameObject[] roots = scene.GetRootGameObjects();
        //             GameObject female = null;
        //             bool found = false;
        //             foreach (GameObject _root in roots)
        //             {
        //                 if (_root != null)
        //                 {
        //                     foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
        //                     {
        //                         if (t != null) {
        //                             GameObject go = t.gameObject;

        //                             if (go.name.Contains("Map"))
        //                             {
        //                                 go.SetActive(false);
        //                                 found = true;
        //                                 break;
        //                             }
        //                         }
        //                     }   
        //                 }

        //                 if (found)
        //                     break;
        //             }
                    
        //             int idx = UnityEngine.Random.Range(0, mp4files.Count);
        //             string path = adv_path += mp4files[idx];
        //             PlayADVSceneVideo(path);
        //         }
            
        //     }
        // }


        // [HarmonyPatch(typeof(ADV.TextScenario), "CrossFadeStart")]
        // private static class TextScenario_CrossFadeStart_Patches
        // {
        //    private static void Postfix(ADV.TextScenario __instance)
        //     {
        //         UnityEngine.Debug.Log($">> CrossFadeStart in ADV");
        //     }
        // }

// Concierge
        // [HarmonyPatch(typeof(Manager.FurRoomSceneManager), "LoadConciergeBody")]
        // private static class FurRoomSceneManager_LoadConciergeBody_Patches
        // {
        //     private static void Postfix(Manager.FurRoomSceneManager __instance)
        //     {                
        //         UnityEngine.Debug.Log($">> LoadConciergeBody in FurRoom");          
        //     }
        // }

        // [HarmonyPatch(typeof(Manager.LobbySceneManager), "LoadConciergeBody")]
        // private static class LobbySceneManager_LoadConciergeBody_Patches
        // {
        //     private static void Postfix(Manager.LobbySceneManager __instance)
        //     {                
        //         UnityEngine.Debug.Log($">> LoadConciergeBody in Lobby");
        //     }
        // }

        // [HarmonyPatch(typeof(Manager.HomeSceneManager), "LoadConciergeBody")]
        // private static class HomeSceneManager_LoadConciergeBody_Patches
        // {
        //     private static void Postfix(Manager.HomeSceneManager __instance)
        //     {                
        //         UnityEngine.Debug.Log($">> LoadConciergeBody in Home");
        //     }
        // }

        // [HarmonyPatch(typeof(Manager.SpecialTreatmentRoomManager), "LoadConciergeBody")]
        // private static class SpecialTreatmentRoomManager_LoadConciergeBody_Patches
        // {
        //     private static void Postfix(Manager.SpecialTreatmentRoomManager __instance)
        //     {                
        //         UnityEngine.Debug.Log($">> LoadConciergeBody in RoomManager");          
        //     }
        // }        

        // [HarmonyPatch(typeof(Manager.HSceneManager), "SetFemaleState", typeof(ChaControl[]))]
        // private static class HSceneManager_SetFemaleState_Patches
        // {
        //     private static void Postfix(Manager.HSceneManager __instance, ChaControl[] female)
        //     {

        //         KK_PregnancyPlus.PregnancyPlusCharaController controller = female[0].GetComponent<KK_PregnancyPlus.PregnancyPlusCharaController>();

        //         UnityEngine.Debug.Log($">> SetFemaleState in HSceneManager {controller}");
        //     }
        // }

        // [HarmonyPatch(typeof(HScene), "Start")]
        // private static class HScene_Start_Patches
        // {
        //     private static void Postfix(HScene  __instance)
        //     {
        //         UnityEngine.Debug.Log($">> Start in HScene");
        //     }
        // }

        // [HarmonyPatch(typeof(HSceneSpriteClothCondition), "SetClothCharacter", typeof(bool))]
        // private static class HSceneSpriteClothCondition_SetClothCharacter_Patches
        // {
        //     private static bool Prefix(HSceneSpriteClothCondition __instance, bool init)
        //     {
        //         // mode = 1 -> cloth
        //         // mode = 2 -> accessory
        //         UnityEngine.Debug.Log($">> SetClothCharacter {init} in HScene");
        //         return true;        
        //     }
        // }


        // [HarmonyPatch(typeof(Manager.BaseMap), "MapVisible", typeof(bool))]
        // private static class BaseMap_MapVisible_Patches
        // {
        //     private static void Postfix(Manager.BaseMap __instance, bool _visible)
        //     {
        //         UnityEngine.Debug.Log($">> MapVisible in BaseMap {_visible}");
        //     }
        // }

        // [HarmonyPatch(typeof(CameraControl_Ver2), "Start")]
        // private static class CameraControl_Ver2_Start_Patches
        // {
        //    private static void Postfix(CameraControl_Ver2 __instance)
        //    {
        //        UnityEngine.Debug.Log($">> Start in CameraControl");
        //    }
        // }

        // [HarmonyPatch(typeof(HScene), "ChangeCoodinate")]
        // private static class HScene_ChangeCoodinate_Patches
        // {
        //     private static bool Prefix(HScene __instance)
        //     {
        //         UnityEngine.Debug.Log($">> ChangeCoodinate in HScene");
        //         return true;
        //     }
        // }
    
        // [HarmonyPatch(typeof(HScene), "ChangeAnimation", typeof(HScene.AnimationListInfo), typeof(bool), typeof(bool), typeof(bool))]
        // private static class HScene_ChangeAnimation_Patches
        // {
        //     private static bool Prefix(HScene __instance, HScene.AnimationListInfo _info, bool _isForceResetCamera, bool _isForceLoopAction = false, bool _UseFade = true)
        //     {
        //         UnityEngine.Debug.Log($">> ChangeAnimation in HScene");
        //         return true;        
        //     }
        // }
        
        // [HarmonyPatch(typeof(HScene), "SetStartAnimationInfo")]
        // private static class HScene_StartAnim_Patches
        // {
        //     private static bool Prefix(HScene __instance)
        //     {
        //         UnityEngine.Debug.Log($">> SetStartAnimationInfo in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
        //         return true;        
        //     }
        // }   

        // [HarmonyPatch(typeof(HSceneSprite), "OnClickFinish")]
        // private static class HSceneSprite_OnClickFinish_Patches
        // {
        //     private static bool Prefix(HSceneSprite __instance)
        //     {
        //         UnityEngine.Debug.Log($">> OnClickFinish in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
        //         return true;        
        //     }
        // }

        // [HarmonyPatch(typeof(HSceneSprite), "OnClickFinishInSide")]
        // private static class HSceneSprite_OnClickFinishInSide_Patches
        // {
        //     private static bool Prefix(HSceneSprite __instance)
        //     {
        //         UnityEngine.Debug.Log($">> OnClickFinishInSide in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
        //         return true;        
        //     }
        // }

        // [HarmonyPatch(typeof(HSceneSprite), "OnClickFinishOutSide")]
        // private static class HSceneSprite_OnClickFinishOutSide_Patches
        // {
        //     private static bool Prefix(HSceneSprite __instance)
        //     {
        //         UnityEngine.Debug.Log($">> OnClickFinishOutSide in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
        //         return true;        
        //     }
        // }                

        // [HarmonyPatch(typeof(HSceneSprite), "OnClickFinishDrink")]
        // private static class HSceneSprite_OnClickFinishDrink_Patches
        // {
        //     private static bool Prefix(HSceneSprite __instance)
        //     {
        //         UnityEngine.Debug.Log($">> OnClickFinishDrink in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
        //         return true;        
        //     }
        // }

        // [HarmonyPatch(typeof(HSceneSprite), "OnClickSpanking")]
        // private static class HSceneSprite_OnClickSpanking_Patches
        // {
        //     private static bool Prefix(HSceneSprite __instance)
        //     {
        //         UnityEngine.Debug.Log($">> OnClickSpanking in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
        //         return true;        
        //     }
        // }

        // [HarmonyPatch(typeof(HSceneSprite), "OnClickSceneEnd")]
        // private static class HSceneSprite_OnClickSceneEnd_Patches
        // {
        //     private static bool Prefix(HSceneSprite __instance)
        //     {
        //         UnityEngine.Debug.Log($">> OnClickSceneEnd in HScene eventNo: {Singleton<Manager.Game>.Instance.eventNo}, peek: {Singleton<Manager.Game>.Instance.peepKind}");
        //         return true;        
        //     }
        // }

        // [HarmonyPatch(typeof(HSceneSprite), "OnClickCloth", typeof(int))]
        // private static class HSceneSprite_OnClickCloth_Patches
        // {
        //     // 너무 심하게 굴면.. this.ctrlFlag.click = HSceneFlagCtrl.ClickKind.LeaveItToYou;
        //     private static bool Prefix(HSceneSprite __instance, int mode)
        //     {
        //         // mode = 1 -> cloth
        //         // mode = 2 -> accessory
        //         UnityEngine.Debug.Log($">> OnClickCloth {mode} in HScene");
        //         return true;        
        //     }
        // }

        // [HarmonyPatch(typeof(HSceneSprite), "OnClickMotion", typeof(int))]
        // private static class HSceneSprite_OnClickMotion_Patches
        // {
        //     private static bool Prefix(HSceneSprite __instance, int _motion)
        //     {
        //         UnityEngine.Debug.Log($">> OnClickMotion {_motion} in HScene");
        //         return true;        
        //     }
        // }     
        #endregion
    }

    #endregion
}
