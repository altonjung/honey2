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
using HS2;
using Illusion.Anime;
using Illusion.Extensions;
using Manager;

#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif
using Newtonsoft.Json;

/*
    해야 할 일:        
        - hscene 처리
            -> 각 행위별 처리
        - sleeping scene 처리
            -> sleeping 시 scene event 제공
        - 자신만의 캐릭터 별 scene event 제공
        - home scene/advance scene 처리
            -> AI chat 기능 연동        
        - AI chat 기능 제공
            -> action controller 기능을 통해 chat action 수행 제공
                => 인사, 화내기, 좋아하기, 싫어하기, 춤추기, 대화 종료
        - AI chat 대화창 개발
            -> 사용자 대화창 개발
*/
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
        public const string Version = "0.9.1.2";
        public const string GUID = "com.alton.illusionplugins.HoneySelect2Maker";
        internal const string _ownerId = "Alton";

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

// Video
        internal string _provider_config_path = UserData.Path + "/hs2maker/system/config/provider.json";
        internal string _fallback_config_path = UserData.Path + "/hs2maker/system/message/fallback.json";
        internal string _bgm_home_folder = UserData.Path + "/hs2maker/bgm/home/";
        internal string _video_title_scene_folder = UserData.Path + "/hs2maker/title/";
        internal string _video_title_scene_loop_path = UserData.Path + "/hs2maker/title/loop.hs2m";
        internal string _video_home_scene_folder = UserData.Path + "/hs2maker/home/";
        internal string _video_home_evt_scene_folder = UserData.Path + "/hs2maker/home/event/";
        internal string _video_home_sleep_scene_folder = UserData.Path + "/hs2maker/home/sleep/";
        internal string _video_lobby_scene_folder = UserData.Path + "/hs2maker/lobby/";
        internal string _video_concierge_scene_folder = UserData.Path + "/hs2maker/concierge/";     
        internal string _video_adv_japaneses_scene_folder = UserData.Path + "/hs2maker/adv/japanese/";
        internal string _video_adv_lobby_scene_folder = UserData.Path + "/hs2maker/adv/lobby/";

// Image
        internal string _img_chat_scene_folder = UserData.Path + "/hs2maker/home/event/chat/";

// LLM
        internal static bool _videoFinished = false;
        internal static string _HS2_LLM_SERVER = "";
        internal static string _HS2_LLM_MODEL = "";
        internal static string _HS2_LLM_PROVIDER = "";
        internal static string _HS2_OPENROUTER_MODEL = "";
        internal static string _HS2_OPENROUTER_BASE_URL = "";
        internal static string _HS2_OPENROUTER_API_KEY = "";
        internal static string _HS2_OPENROUTER_REFERER = "";
        internal static string _HS2_OPENROUTER_TITLE = "";

        internal bool _isAbleTitleVideo;
       
        internal List<Canvas> _disabledCanvasCache = new List<Canvas>();

        internal Dictionary<string, HeroinData> _playingHeroinNames = new Dictionary<string, HeroinData>();

        private static string _assemblyLocation;
        internal static bool _reEntryHarmony = false;

        private bool _loaded = false;

        private AssetBundle _bundle;
        #endregion
        private static ProviderConfig _providerConfig;
        internal static ProviderConfig ProviderConfig => _providerConfig;

        #region Accessors
        internal static ConfigEntry<bool> VideoModeActive { get; private set; }
        internal static ConfigEntry<string> LLMProvider { get; private set; }
        internal static ConfigEntry<string> OpenRouterModel { get; private set; }
        internal static ConfigEntry<string> OpenRouterBaseUrl { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            VideoModeActive = Config.Bind("InGame", "Video Play", true, new ConfigDescription("Enable/Disable"));
            LLMProvider = Config.Bind("LLM", "Provider", "ollama", new ConfigDescription("LLM provider: ollama | openrouter"));
            OpenRouterModel = Config.Bind("LLM", "OpenRouter Model", "openai/gpt-4o", new ConfigDescription("OpenRouter model id."));
            OpenRouterBaseUrl = Config.Bind("LLM", "OpenRouter Base URL", "https://openrouter.ai/api/v1", new ConfigDescription("OpenRouter API base URL"));

            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _providerConfig = LoadProviderConfig(_provider_config_path);
            
            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            HS2SceneController.Initialize();

            // title
            if (HS2SceneController.GetVideoFiles(_self._video_title_scene_folder).Count > 0)
            {
                _isAbleTitleVideo = true;
            }

            _HS2_LLM_SERVER = Environment.GetEnvironmentVariable("hs2_llm_host");

            if (_HS2_LLM_SERVER == null || _HS2_LLM_SERVER.Equals(""))
                _HS2_LLM_SERVER = "http://localhost:11434";

            _HS2_LLM_MODEL = Environment.GetEnvironmentVariable("hs2_llm_model");

            if (_HS2_LLM_MODEL == null || _HS2_LLM_MODEL.Equals(""))
                _HS2_LLM_MODEL = "realgirl";

            _HS2_LLM_PROVIDER = Environment.GetEnvironmentVariable("hs2_llm_provider");
            if (string.IsNullOrWhiteSpace(_HS2_LLM_PROVIDER))
                _HS2_LLM_PROVIDER = LLMProvider.Value;

            _HS2_OPENROUTER_MODEL = Environment.GetEnvironmentVariable("OPENROUTER_MODEL");
            if (string.IsNullOrWhiteSpace(_HS2_OPENROUTER_MODEL))
                _HS2_OPENROUTER_MODEL = OpenRouterModel.Value;

            _HS2_OPENROUTER_BASE_URL = Environment.GetEnvironmentVariable("OPENROUTER_BASE_URL");
            if (string.IsNullOrWhiteSpace(_HS2_OPENROUTER_BASE_URL))
                _HS2_OPENROUTER_BASE_URL = OpenRouterBaseUrl.Value;

            _HS2_OPENROUTER_API_KEY = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            _HS2_OPENROUTER_REFERER = Environment.GetEnvironmentVariable("OPENROUTER_REFERER");
            _HS2_OPENROUTER_TITLE = Environment.GetEnvironmentVariable("OPENROUTER_TITLE");

            if (_providerConfig != null)
            {
                if (!string.IsNullOrWhiteSpace(_providerConfig.provider))
                    _HS2_LLM_PROVIDER = _providerConfig.provider;
                if (!string.IsNullOrWhiteSpace(_providerConfig.host))
                    _HS2_LLM_SERVER = _providerConfig.host;
                if (!string.IsNullOrWhiteSpace(_providerConfig.openrouterBaseUrl))
                    _HS2_OPENROUTER_BASE_URL = _providerConfig.openrouterBaseUrl;
                if (!string.IsNullOrWhiteSpace(_providerConfig.openrouterApiKey))
                    _HS2_OPENROUTER_API_KEY = _providerConfig.openrouterApiKey;
                if (!string.IsNullOrWhiteSpace(_providerConfig.openrouterModel))
                    _HS2_OPENROUTER_MODEL = _providerConfig.openrouterModel;
                if (!string.IsNullOrWhiteSpace(_providerConfig.openrouterReferer))
                    _HS2_OPENROUTER_REFERER = _providerConfig.openrouterReferer;
                if (!string.IsNullOrWhiteSpace(_providerConfig.openrouterTitle))
                    _HS2_OPENROUTER_TITLE = _providerConfig.openrouterTitle;
            }

            UnityEngine.Debug.Log($"> LLM provider = {_HS2_LLM_PROVIDER}, ollama = {_HS2_LLM_SERVER}, ollama model = {_HS2_LLM_MODEL}, base = {_HS2_OPENROUTER_BASE_URL}, model = {_HS2_OPENROUTER_MODEL}");
        }

        private static ProviderConfig LoadProviderConfig(string configPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(configPath))
                    return null;

                if (!File.Exists(configPath))
                    return null;

                var json = File.ReadAllText(configPath);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                return JsonConvert.DeserializeObject<ProviderConfig>(json);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"> Failed to read provider.json: {e.Message}");
                return null;
            }
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
        protected override void LevelLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
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

        private bool IsAvailableVideo(string path)
        {
            if (HS2SceneController.GetVideoFiles(path).Count > 0)
            {
                return true;
            }
            return false;
        }

        private void UpdateHeroins() {
			Manager.Game instance = Singleton<Manager.Game>.Instance;
			SaveData saveData = instance.saveData;
			int[] eventNos = Enumerable.Repeat<int>(-1, saveData.roomList[saveData.selectGroup].Count).ToArray<int>();
            foreach (int no in eventNos) {
                UnityEngine.Debug.Log($">> no {no} | {DateTime.Now:HH:mm:ss.fff}");
            }

            foreach (ValueTuple<string, int> valueTuple in saveData.roomList[saveData.selectGroup].ToForEachTuples<string>())
            {
                string item = valueTuple.Item1;
                int item2 = valueTuple.Item2;
                
                UnityEngine.Debug.Log($">> item {item} | item2 {item} |  {DateTime.Now:HH:mm:ss.fff}");
                ChaFileControl charFileControl = new ChaFileControl().LoadCharaFile(item, 1, false, true);
                UnityEngine.Debug.Log($">> charaFileName {charFileControl.charaFileName} |  {DateTime.Now:HH:mm:ss.fff}");

                // 여기서 heroinKey 와 heroinData 추가
                HeroinData heroinData = new HeroinData();

                heroinData.fullname = charFileControl.charFile.parameter.fullname;
                heroinData.personality = charFileControl.charFile.parameter.personality ;
                heroinData.birthMonth = charFileControl.charFile.parameter.birthMonth;
                heroinData.birthDay = charFileControl.charFile.parameter.birthDay;
                heroinData.voiceRate = charFileControl.charFile.parameter2.voiceRate;
                heroinData.trait = charFileControl.charFile.parameter2.trait;
                heroinData.mind = charFileControl.charFile.parameter2.mind;
                heroinData.hAttribute = charFileControl.charFile.parameter2.hAttribute;
                heroinData.age = 20;

                _self._playingHeroinNames[charFileControl.charaFileName] = heroinData;
            }        
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

                if (_self.IsAvailableVideo(_self._video_title_scene_folder)) {
                    _self.StartCoroutine(PlayTitleVideos());
                }
            }
        }
        private static IEnumerator PlayTitleVideos()
        {
            // 첫 번째 영상
            yield return new WaitForEndOfFrame();
            HS2SceneController.PlayVideoRandom(_self._video_title_scene_folder, false, -100);
            yield return new WaitUntil(() => _videoFinished);

            string currentSceneName = SceneManager.GetActiveScene().name;
            
            UnityEngine.Debug.Log($">> currentSceneName {currentSceneName} | {DateTime.Now:HH:mm:ss.fff}");

            if (currentSceneName.Equals("Title"))
            {
                // 두 번째 영상
                yield return new WaitForEndOfFrame();
                HS2SceneController.PlayVideo(_self._video_title_scene_loop_path, true, false, -100);
                yield return new WaitUntil(() => _videoFinished);  
            }
            // 필요하면 계속 추가 가능
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
                // UnityEngine.Debug.Log($">> Start in HomeScene {_reEntryHarmony} | {DateTime.Now:HH:mm:ss.fff}");    
                if (_self.IsAvailableVideo(_self._video_home_scene_folder)) {
                    if (_reEntryHarmony)
                    {
                        _reEntryHarmony = false;
                        return true; // 원본 실행 허용
                    }

                    // int value = UnityEngine.Random.Range(0, 10); // 70% 확률로 cut scene 발생
                    // if (value <= 3)
                    // {
                    //     return true;
                    // }

                    _self.UpdateHeroins();
                    _self.StartCoroutine(WaitHomeSceneCallWithChat(__instance));
                    //if (_self._playingHeroinNames.Count == 0) {
                    //    _self.StartCoroutine(WaitHomeSceneCallWithChat(__instance));
                    //} else
                    //{
                    //    _self.StartCoroutine(WaitHomeSceneCall(__instance));
                    //}
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
            HS2SceneController.PlayVideoRandom(_self._video_home_scene_folder, true);
            yield return new WaitUntil(() => _videoFinished);

            UnityEngine.Debug.Log($">> WaitHomeSceneCall videoFinished | {Time.realtimeSinceStartup:F3}");

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

        private static IEnumerator WaitHomeSceneCallWithChat(HS2.HomeScene __instance)
        {
            string currentSceneName = SceneManager.GetActiveScene().name;
            UnityEngine.Debug.Log($">>currentSceneName {currentSceneName} in WaitHomeSceneCallWithChat | {Time.realtimeSinceStartup:F3}");

            yield return new WaitForEndOfFrame();            

             //var chatUI = new HS2ChatUIController();
            var chatUI = new HS2ChatSUIController();
            chatUI.SetFontFromOSBySystemLanguage();

            var user = new ChatUser
            {
                gender = "boy",
                name = "Honey",
                age = 20,
                nationality = "English",
                friendship = "",
                character1 = "cautious",
                character2 = "friendly",
                talking_style = "normal",
                habit="",
                love=""
            };

            var heroin = new ChatUser
            {
                gender = "girl",
                name = "Jenny",
                age = 20,
                nationality = "English",
                friendship = "unknown",
                job = "student",
                character1 = "cautious",
                character2 = "girlish",
                talking_style = "short talking",
                habit="workout,sing",
                love="love,book,pet"
            };

            chatUI.CreateChatUI(new HS2ChatController(HoneySelect2Maker.ProviderConfig), new HS2ActionController(), user, heroin);
            HS2SceneController.PlayImageRandom(_self._img_chat_scene_folder);
            yield return new WaitUntil(() => _videoFinished);
            chatUI.DestroyChatUI();
            

            // UnityEngine.Debug.Log($">> WaitHomeSceneCallWithChat videoFinished | {Time.realtimeSinceStartup:F3}");

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

                if (_self.IsAvailableVideo(_self._video_lobby_scene_folder)) {
                    // UnityEngine.Debug.Log(Environment.StackTrace);
                    // UnityEngine.Debug.Log($">> Start in LobbyScene {_reEntryHarmony} | {DateTime.Now:HH:mm:ss.fff}");
                    if (_reEntryHarmony)
                    {
                        _reEntryHarmony = false;
                        return true; // 원본 실행 허용
                    }

                    _self.StartCoroutine(WaitLobbySceneCall(__instance));
                    return false;
                }

                return true;
            }
        }

        // [HarmonyPatch(typeof(Manager.LobbySceneManager), "Start")]
        // private static class LobbySceneManager_Start_Patches
        // {
        //     private static void Postfix(Manager.LobbySceneManager __instance)
        //     {
        //         if (__instance != null && __instance.heroines.Length > 0)
        //         {
        //             UnityEngine.Debug.Log($">> LobbySceneManager is not null | {DateTime.Now:HH:mm:ss.fff}");

        //             foreach (Actor.Heroine heroin in __instance.heroines) {
        //                 if (heroin != null) {
        //                     string heroinName = heroin.chaFile.parameter.fullname;
        //                     UnityEngine.Debug.Log($">> heroine name {heroinName} in LobbyScene | {DateTime.Now:HH:mm:ss.fff}");

        //                     // 여기서 heroinKey 와 heroinData 추가
        //                     HeroinData heroinData = new HeroinData();
        //                     _self._playingHeroinNames[heroinName] = heroinData;

        //                     UnityEngine.Debug.Log($">> heroinName {heroinName} | {DateTime.Now:HH:mm:ss.fff}");
        //                 }
        //             }
        //         } else
        //         {
        //             UnityEngine.Debug.Log($">> LobbySceneManager is null | {DateTime.Now:HH:mm:ss.fff}");
        //         }                
        //     }
        // }

        private static IEnumerator WaitLobbySceneCall(HS2.LobbyScene __instance)
        {
// Test 영역
            yield return new WaitUntil(() => Singleton<Manager.Character>.IsInstance());
			yield return new WaitUntil(() => Singleton<Manager.Game>.IsInstance());

// Test 영역
            string currentSceneName = SceneManager.GetActiveScene().name;
            // UnityEngine.Debug.Log($">> currentSceneName {currentSceneName} in WaitLobbySceneCall | {Time.realtimeSinceStartup:F3}");

            yield return new WaitForEndOfFrame();
            HS2SceneController.PlayVideoRandom(_self._video_lobby_scene_folder, true);
            yield return new WaitUntil(() => _videoFinished);


            _self.UpdateHeroins();

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

        //FurRoom
        [HarmonyPatch(typeof(HS2.FurRoomScene), "Start")]
         private static class FurRoomScene_Start_Patches
        {
            private static bool Prefix(HS2.FurRoomScene __instance)
            {
                UnityEngine.Debug.Log($">> Start in FurRoomScene {_reEntryHarmony} | {DateTime.Now:HH:mm:ss.fff}");
                //if (_reEntryHarmony)
                //{
                //    _reEntryHarmony = false;
                //    return true; // 원본 실행 허용
                //}

                //if (_self._isAbleHomeVideo)
                //{
                //    _self.StartCoroutine(WaitFurRoomSceneCall(__instance));
                //    return false;
                //}

                return true;
            }
        }

        // private static IEnumerator WaitFurRoomSceneCall(HS2.FurRoomScene __instance)
        // {
        //     string currentSceneName = SceneManager.GetActiveScene().name;
        //     // UnityEngine.Debug.Log($">> currentSceneName {currentSceneName} in WaitFurRoomSceneCall | {Camera.main} | {Time.realtimeSinceStartup:F3}");

        //     yield return new WaitForEndOfFrame();
        //     HS2SceneController.PlayVideo(_self._video_furroom_scene_path, false);
        //     yield return new WaitUntil(() => _videoFinished);

        //     // 🔥 원 함수 실행
        //     var method = typeof(HS2.FurRoomScene)
        //         .GetMethod("Start", System.Reflection.BindingFlags.Instance | 
        //                             System.Reflection.BindingFlags.NonPublic);

        //     _reEntryHarmony = true;
        //     if (method != null)
        //     {
        //         var enumerator = (IEnumerator)method.Invoke(__instance, null);
        //         yield return __instance.StartCoroutine(enumerator);
        //     }
        // }

        // Concierge
        [HarmonyPatch(typeof(HS2.HomeUI), "CallConcierge")]
        private static class HomeUI_CallConcierge_Patches
        {
            private static bool Prefix(HS2.HomeUI __instance)
            {
                // UnityEngine.Debug.Log($">> action CallConcierge | {DateTime.Now:HH:mm:ss.fff}");                
                if (_self.IsAvailableVideo(_self._video_concierge_scene_folder)) {
                    if (_reEntryHarmony)
                    {
                        _reEntryHarmony = false;
                        return true; // 원본 실행 허용
                    }

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
            // UnityEngine.Debug.Log($">> currentSceneName {currentSceneName} in WaitCallConcierge |{Time.realtimeSinceStartup:F3}");

            yield return new WaitForEndOfFrame();
            HS2SceneController.PlayVideoRandom(_self._video_concierge_scene_folder, true);
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

        [HarmonyPatch(typeof(ADV.ADVMainScene), "Open")]
        private static class ADVMainScene_Open_Patches
        {
            private static bool Prefix(ADV.ADVMainScene __instance)
            {
                // UnityEngine.Debug.Log($">> start in ADVMainScene {scene.name}, {_reEntryHarmony} | {DateTime.Now:HH:mm:ss.fff}");
                UnityEngine.SceneManagement.Scene scene = SceneManager.GetActiveScene();

                if (_reEntryHarmony)
                {
                    _reEntryHarmony = false;
                    return true;
                }

                Game instance = Singleton<Game>.Instance;
                string heroinName = "";
                if (instance.heroineList.Count > 0)
                {
                    heroinName = instance.heroineList[0].chaFile.parameter.fullname;
                }

                UnityEngine.Debug.Log($">> start in ADVMainScene {instance.heroineList.Count}, {heroinName} | {DateTime.Now:HH:mm:ss.fff}");

                _self.StartCoroutine(WaitAdvMainSceneCall(scene.name, __instance, heroinName));
                return false;
            }
        }

        private static IEnumerator WaitAdvMainSceneCall(string sceneName, ADV.ADVMainScene __instance, string heroinName)
        {
            Manager.ADVManager instance = Singleton<Manager.ADVManager>.Instance;
            // UnityEngine.Debug.Log($">> WaitAdvSceneCall {__instance.packData.MapName},  {__instance.packData.EventCGName} | {DateTime.Now:HH:mm:ss.fff}");

            string method_name = "Open";
            string currentSceneName = SceneManager.GetActiveScene().name;

            yield return new WaitForEndOfFrame();

            if (sceneName.Equals("MyRoom")) {
                if (__instance.packData.EventCGName.Equals("My room_Event36")) {
                    UnityEngine.Debug.Log($">> Advance MyRoom with Sleep");
                    if (_self.IsAvailableVideo(_self._video_home_sleep_scene_folder)) {  
                        // sleep
                        HS2SceneController.PlayVideoRandom(_self._video_home_sleep_scene_folder, true);
                        yield return new WaitUntil(() => _videoFinished);
                        method_name = "Start";
                    }
                } 
                else
                {   // enjoy
                    UnityEngine.Debug.Log($">> Advance MyRoom with enjoy");
                    //     
                }
            } else if  (sceneName.Equals("Japanese")) { 
                if (_self.IsAvailableVideo(_self._video_adv_japaneses_scene_folder)) { 

                    HS2SceneController.PlayVideoRandom(_self._video_adv_japaneses_scene_folder, true);
                    yield return new WaitUntil(() => _videoFinished);
                }
            } else if  (sceneName.Equals("TortureRoom")) { 
                
            } else if  (sceneName.Equals("Garden_suny")) { 
                
            } else if  (sceneName.Equals("Garden_rain")) { 
                
            } else if  (sceneName.Equals("Lobby")) { 
                if (_self.IsAvailableVideo(_self._video_adv_lobby_scene_folder)) { 
                    HS2SceneController.PlayVideoRandom(_self._video_adv_lobby_scene_folder, true);
                    yield return new WaitUntil(() => _videoFinished);
                }
            } else if  (sceneName.Equals("SuiteRoom")) { 
                
            } else if  (sceneName.Equals("FrontOfBath")) { 
                
            } else if  (sceneName.Equals("PublicToilet")) { 
                
            } else if  (sceneName.Equals("PublicBath")) { 
                
            } else if  (sceneName.Equals("MyRoom")) { 
                
            } else if  (sceneName.Equals("Office")) { 
                
            } else if  (sceneName.Equals("StaffRoom")) { 
                
            } else if  (sceneName.Equals("ClassRoom")) { 
                
            }

            // 🔥 원 함수 실행
            var method = typeof(ADV.ADVMainScene)
                .GetMethod(method_name, System.Reflection.BindingFlags.Instance |
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
                // UnityEngine.Debug.Log($">> Start in HSceneManager | {DateTime.Now:HH:mm:ss.fff}");
                return true;
            }
        }

        [HarmonyPatch(typeof(Manager.HSceneManager), "SetFemaleState", typeof(ChaControl[]))]
        private static class HSceneManager_SetFemaleState_Patches
        {
            private static void Postfix(Manager.HSceneManager __instance, ChaControl[] female)
            {
                // UnityEngine.Debug.Log($">> SetFemaleState in HSceneManager | {DateTime.Now:HH:mm:ss.fff}");
                // foreach (ChaControl eachFemale in female)
                // {
                //     if (eachFemale != null)
                //     {
                //         // UnityEngine.Debug.Log($">> eachFemale {eachFemale.chaFile.parameter.fullname}");
                //     }
                // }

				// SaveData saveData = Singleton<Manager.Game>.Instance.saveData;
                // if (saveData != null) {
                //     UnityEngine.Debug.Log($">> instance.hero {saveData.hCount}");
                // }

                // Manager.Game instance = Singleton<Manager.Game>.Instance; 

                // if (instance != null)
                // {
                //     // Manager.Game.Instance.saveData 내에 player 정보 저장 
                //     UnityEngine.Debug.Log($">> instance.heroineList {instance.heroineList.Count}");
                // }
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

                if (HS2SceneController.GetVideoPlayer().isPlaying)
                {
                    HS2SceneController.GetVideoPlayer().Pause();
                }

                HS2SceneController.StopSceneVideo();
            }
        }
        
        [HarmonyPatch(typeof(HS2.TitleScene), "OnUpload")]
        private static class TitleScene_OnUpload_Patches
        {
           private static void Postfix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return;

               if (HS2SceneController.GetVideoPlayer().isPlaying)
               {
                    HS2SceneController.GetVideoPlayer().Pause();
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

               if (HS2SceneController.GetVideoPlayer().isPlaying)
               {
                    HS2SceneController.GetVideoPlayer().Pause();
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

               if (HS2SceneController.GetVideoPlayer().isPlaying)
               {
                    HS2SceneController.GetVideoPlayer().Pause();
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

                if (HS2SceneController.GetVideoPlayer().isPlaying)
                {
                    HS2SceneController.GetVideoPlayer().Pause();
                }

                HS2SceneController.StopSceneVideo();
            }
        }

        [HarmonyPatch(typeof(HS2.TitleScene), "OnMakeMale")]
        private static class TitleScene_OnMakeMale_Patches
        {
           private static void Postfix(HS2.TitleScene __instance)
           {
                if (!VideoModeActive.Value)
                    return;

                if (HS2SceneController.GetVideoPlayer().isPlaying)
                {
                    HS2SceneController.GetVideoPlayer().Pause();
                }

                HS2SceneController.StopSceneVideo();
            }
        }

// Common
        [HarmonyPatch(typeof(Manager.BaseMap), "ChangeAsync", typeof(int), typeof(FadeCanvas.Fade), typeof(bool))]
        private static class BaseMap_ChangeAsync_Patches
        {
           private static bool Prefix(Manager.BaseMap __instance, int _no, FadeCanvas.Fade fadeType = FadeCanvas.Fade.InOut, bool isForce = false)
           {
                UnityEngine.SceneManagement.Scene scene = SceneManager.GetActiveScene();

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
                UnityEngine.SceneManagement.Scene scene = SceneManager.GetActiveScene();
                
                if (!VideoModeActive.Value)
                    return true;

                // if (__instance != null && root != null)
                //     UnityEngine.Debug.Log($">> SetRoot {root.name}, scene {scene.name}, charName {__instance.Name}, charBirthDay {__instance.birthMonth}/{__instance.birthDay} | {DateTime.Now:HH:mm:ss.fff}");

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

    [Serializable]
    public class ProviderConfig
    {
        public string provider;
        public string host;
        public string openrouterBaseUrl;
        public string openrouterApiKey;
        public string openrouterModel;
        public string openrouterReferer;
        public string openrouterTitle;
    }
}
