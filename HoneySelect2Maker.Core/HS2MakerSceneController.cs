﻿using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UILib;
using UILib.ContextMenu;
using UILib.EventHandlers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.Threading.Tasks;

#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using ADV.Commands.Camera;
using ADV.Commands.Object;
using IllusionUtility.GetUtility;
using KKAPI.Studio;
using KKAPI.Maker;
using System.Text;
#endif
using Newtonsoft.Json;

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace HoneySelect2Maker
{
    public class HS2SceneController
    {

#if FEATURE_PUBLIC_RELEASE
        internal const int VIDEO_MAX_COUNT = 10;
#else
        internal const int VIDEO_MAX_COUNT = 30;
#endif

        internal static UnityEngine.Video.VideoPlayer _sceneVideoPlayer;

        internal static Canvas _sceneCanvas;
        internal static RenderTexture _sceneRT;
        internal static GameObject _overlayCanvasGO;
        internal static UnityEngine.UI.RawImage _rawImage;

        internal static Action _onSceneVideoCompleted;

        internal static void Initialize()
        {
            GameObject videoObj = new GameObject("sceneVideoPlayer");
            GameObject.DontDestroyOnLoad(videoObj);
            _sceneVideoPlayer = videoObj.AddComponent<UnityEngine.Video.VideoPlayer>();

        }

        internal static UnityEngine.Video.VideoPlayer GetVideoPlayer()
        {
            return _sceneVideoPlayer;   
        }

        internal static List<string> GetVideoFiles(string folderPath, string pattern = null)
        {
            List<string> mp4List = new List<string>();

            if (!Directory.Exists(folderPath))
                return mp4List;

            // 모든 mp4 파일 가져오기
            string[] files = Directory.GetFiles(folderPath)
                              .Where(f =>
                                  f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                  f.EndsWith(".hs2m", StringComparison.OrdinalIgnoreCase))
                              .ToArray();

            foreach (string filePath in files)
            {
                string fileName = Path.GetFileName(filePath);

                // pattern 이 null 또는 빈값이면 전체 반환
                if (string.IsNullOrWhiteSpace(pattern) ||
                    fileName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    mp4List.Add(fileName);
                }
            }

            return mp4List;
        }

        internal static List<string> GetPngFiles(string folderPath, string pattern = null)
        {
            List<string> pngList = new List<string>();

            if (!Directory.Exists(folderPath))
                return pngList;

            // 모든 mp4 파일 가져오기
            string[] files = Directory.GetFiles(folderPath)
                              .Where(f =>
                                  f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                              .ToArray();

            foreach (string filePath in files)
            {
                string fileName = Path.GetFileName(filePath);

                // pattern 이 null 또는 빈값이면 전체 반환
                if (string.IsNullOrWhiteSpace(pattern) ||
                    fileName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    pngList.Add(fileName);
                }
            }

            return pngList;
        }        
        
        internal static void EnsureOverlayCanvas(int sortingOrder)
        {
            if (_overlayCanvasGO != null)
            {
                _sceneCanvas.enabled = true;
                _sceneCanvas.sortingOrder = sortingOrder;
                return;
            }

            _overlayCanvasGO = new GameObject("SceneVideoCanvas");
            GameObject.DontDestroyOnLoad(_overlayCanvasGO);

            var canvas = _overlayCanvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder; // 최상단

            _sceneCanvas = canvas;

            _overlayCanvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            _overlayCanvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // RawImage
            GameObject rawGO = new GameObject("SceneVideoRawImage");
            rawGO.transform.SetParent(_overlayCanvasGO.transform, false);

            _rawImage = rawGO.AddComponent<UnityEngine.UI.RawImage>();
            _rawImage.raycastTarget = false;

            var rect = _rawImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _rawImage.color = Color.white;
        }

        private static string ConvertHs2mToTempMp4(string hs2mPath)
        {
            const int SIGNATURE_SIZE = 8;

            byte[] allBytes = File.ReadAllBytes(hs2mPath);

            if (allBytes.Length <= SIGNATURE_SIZE)
                throw new Exception("Invalid hs2m file.");

            // signature 체크
            string signature = System.Text.Encoding.ASCII.GetString(allBytes, 0, SIGNATURE_SIZE);
            if (signature != "hs2maker")
                throw new Exception("Invalid hs2m signature.");

            // 임시 mp4 경로
            string tempPath = Path.Combine(Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(hs2mPath) + "_temp.mp4");

            // 8byte 이후만 저장
            File.WriteAllBytes(tempPath, allBytes.Skip(SIGNATURE_SIZE).ToArray());

            return tempPath;
        }

        internal static void PlaySceneVideo(
            string path,
            bool loop = false,
            bool audio = false,
            int sortingOrder = -100,
            Action callback = null)
        {

            UnityEngine.Debug.Log($">> PlaySceneVideo {path} | loop {loop} | audio {audio} | sortingOrder {sortingOrder} | {DateTime.Now:HH:mm:ss.fff}");

            EnsureOverlayCanvas(sortingOrder);

            var vp = _sceneVideoPlayer;

            vp.Stop();

            vp.prepareCompleted -= OnPrepared;
            if (!loop)
                vp.loopPointReached -= OnVideoCompleted;

            // RenderTexture 생성
            if (_sceneRT != null)
            {
                _sceneRT.Release();
                UnityEngine.Object.Destroy(_sceneRT);
            }

            _sceneRT = new RenderTexture(Screen.width, Screen.height, 0);
            _sceneRT.Create();

            vp.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
            vp.targetTexture = _sceneRT;

            _rawImage.texture = _sceneRT;
            _rawImage.enabled = true;

            vp.source = UnityEngine.Video.VideoSource.Url;

            string finalPath = path;
            if (Path.GetExtension(path).Equals(".hs2m", StringComparison.OrdinalIgnoreCase))
            {
                finalPath = ConvertHs2mToTempMp4(path);
            }
            vp.url = finalPath;
            vp.isLooping = loop;
            vp.playOnAwake = false;

            if (audio)
            {
                vp.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.Direct;
                // AudioSource source = vp.GetTargetAudioSource(0);
                // if (source != null)
                // {
                //     source.volume = 1.0f;   // 기본 최대값
                // }
            }
            else
                vp.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
            
            _onSceneVideoCompleted = callback;

            vp.prepareCompleted += OnPrepared;

            if (!loop)
                vp.loopPointReached += OnVideoCompleted;

            vp.Prepare();
        }

        /*
            var pngPath = UserData.Path + "/hs2maker/backgrounds/mybg.png";
            var texture = HS2SceneController.LoadTextureFromPng(pngPath);
            SceneController.PlaySceneImage(texture);
        */
        internal static void PlaySceneImage(Texture texture, int sortingOrder = 9999)
        {
            if (texture == null)
                return;

            EnsureOverlayCanvas(sortingOrder);

            var vp = _sceneVideoPlayer;
            vp.Stop();

            if (_sceneRT != null)
            {
                _sceneRT.Release();
                UnityEngine.Object.Destroy(_sceneRT);
                _sceneRT = null;
            }
            
            _rawImage.texture = texture;
            _rawImage.enabled = true;
        }

        internal static void OnPrepared(UnityEngine.Video.VideoPlayer vp)
        {
            vp.prepareCompleted -= OnPrepared;

            // UnityEngine.Debug.Log($">> Video Prepared | {DateTime.Now:HH:mm:ss.fff}");

            vp.Play();
        }

        internal static void OnVideoCompleted(UnityEngine.Video.VideoPlayer vp)
        {
            //UnityEngine.Debug.Log($">> OnVideoCompleted() | isLoop {vp.isLooping}| {Time.realtimeSinceStartup:F3}");

            if (vp.isLooping)
                return;

            vp.loopPointReached -= OnVideoCompleted;
            vp.Stop();

            if (_rawImage != null)
                _rawImage.enabled = false;

            _onSceneVideoCompleted?.Invoke();
            _onSceneVideoCompleted = null;
            HoneySelect2Maker._videoFinished = true;
        }
        internal static bool IsVideoFileExists(string videoPath)
        {
            if (string.IsNullOrEmpty(videoPath))
                return false;

            return File.Exists(videoPath);
        }
        internal static void PlayVideo(string video_path, bool isLoop=false, bool isAudio=false, int sortingOrder = 9999)
        {
            HoneySelect2Maker._videoFinished = false;

            UnityEngine.Debug.Log($">> PlayVideo() | video_path {video_path}, exist {IsVideoFileExists(video_path)}| {Time.realtimeSinceStartup:F3}");

            if (IsVideoFileExists(video_path))
            {
                if (isLoop)
                {
                    PlaySceneVideo(
                        video_path,
                        isLoop,
                        isAudio,
                        sortingOrder
                    );
                }
                else
                {

                    PlaySceneVideo(
                        video_path,
                        isLoop,
                        isAudio,
                        sortingOrder,
                        () =>
                        {
                            if (sortingOrder == 9999)
                            {
                                // 컷신용 제거
                                StopSceneVideo();
                            }
                        }
                    );
                }
            } 
            else
            {
                HoneySelect2Maker._videoFinished = true;
            }
        }

        internal static void PlayVideoRandom(string video_folder, bool isAudio=false, int sortingOrder = 9999)
        {
            HoneySelect2Maker._videoFinished = false;

            List<string> video_files  = new List<string>();
            bool isLoop=false;

            video_files = GetVideoFiles(video_folder);

            if (video_files.Count > 0)
            {
                int idx = UnityEngine.Random.Range(0, Mathf.Min(VIDEO_MAX_COUNT, video_files.Count));
                string path = video_folder + video_files[idx];

                if (path.Contains("loop"))
                {
                    isLoop = true;
                }

                if (isLoop)
                {
                    PlaySceneVideo(
                        path,
                        isLoop,
                        isAudio,
                        sortingOrder
                    );   
                } 
                else
                {

                    PlaySceneVideo(
                        path,
                        isLoop,
                        isAudio,
                        sortingOrder,
                        () =>
                        {
                            if (sortingOrder == 9999)
                            { 
                                // 컷신용 제거
                                StopSceneVideo();
                            }
                        }
                    );
                }
            }
            else
            {
                HoneySelect2Maker._videoFinished = true;
            } 
        }

        internal static void PlayVideoWithChat(string video_folder, bool isAudio=false, int sortingOrder = 9999)
        {
            HoneySelect2Maker._videoFinished = false;

            List<string> video_files  = new List<string>();
            bool isLoop=true;

            video_files = GetVideoFiles(video_folder);

            if (video_files.Count > 0)
            {
                int idx = UnityEngine.Random.Range(0, Mathf.Min(VIDEO_MAX_COUNT, video_files.Count));
                string path = video_folder + video_files[idx];

                PlaySceneVideo(
                        path,
                        isLoop,
                        isAudio,
                        sortingOrder
                    );  
            }
            else
            {
                HoneySelect2Maker._videoFinished = true;
            } 
        }        


        internal static void PlayImageRandom(string png_folder, int sortingOrder = 9999)
        {
            HoneySelect2Maker._videoFinished = false;

            List<string> png_files  = new List<string>();                

            png_files = GetPngFiles(png_folder);

            UnityEngine.Debug.Log($">> PlayImageRandom {png_files.Count} | {Time.realtimeSinceStartup:F3}");

            if (png_files.Count > 0)
            {
                int idx = UnityEngine.Random.Range(0, Mathf.Min(VIDEO_MAX_COUNT, png_files.Count));
                string path = png_folder + png_files[idx];

                var texture = LoadTextureFromPng(path);

                PlaySceneImage(
                    texture,
                    sortingOrder
                );   

            }
            else
            {
                HoneySelect2Maker._videoFinished = true;
            } 
        }        

        // StartCoroutine(PlayBGSoundRandom("C:/temp/test.mp3"));
        //internal static void PlayBGSound(string path, AudioSource audioSource)
        //{
        //    string url = "file://" + path;

        //    if (audioSource != null)
        //    {
        //        using (UnityWebRequest req =
        //            UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        //        {
        //            yield return req.SendWebRequest();

        //            if (req.result != UnityWebRequest.Result.Success)
        //            {
        //                Debug.LogError(req.error);
        //                yield break;
        //            }

        //            AudioClip clip =
        //                DownloadHandlerAudioClip.GetContent(req);

        //            audioSource.clip = clip;
        //            audioSource.Play();
        //        }                
        //    }
        //}

        internal static void StopBGSound(AudioSource audioSource)
        {
            if (audioSource != null)
                audioSource.Stop();
        }  

        internal static void DestroyCurrentRender()
        {
            _sceneVideoPlayer.Stop();
            if (_rawImage != null)
                _rawImage.enabled = false;

            _onSceneVideoCompleted?.Invoke();
            _onSceneVideoCompleted = null;
            HoneySelect2Maker._videoFinished = true;
        }

        internal static void StopSceneVideo()
        {
            _sceneVideoPlayer.Stop();
            _sceneCanvas.enabled = false;
        }

        internal static void UnlockAchivementAll()
        {
			Manager.Game instance = Singleton<Manager.Game>.Instance;
			SaveData saveData = instance.saveData;

            for (int i = 0; i < saveData.achievement.Count; i++)
            {
                SaveData.SetAchievementAchieve(i);
            }
        }

        internal static List<string> FindAllHeroinPathsInRoomList()
        {
            List<string> result = new List<string>();

            Manager.Game instance = Singleton<Manager.Game>.Instance;
            SaveData saveData = instance.saveData;

            UnityEngine.Debug.Log($">> saveData.selectGroup {saveData.selectGroup} | {DateTime.Now:HH:mm:ss.fff}");

            foreach (List<string> list in saveData.roomList)
            {
                List<string> list2 = new List<string>(list);

                for (int j = 0; j < list2.Count; j++)
                {
                    StringBuilder stringBuilder = new StringBuilder(UserData.Path + "chara/female/");
                    stringBuilder.Append(list2[j]).Append(".png");
                    // StringBuilder stringBuilder = new StringBuilder(list2[j]);
                    UnityEngine.Debug.Log($" >>>> {stringBuilder.ToString()} in FindAllCharsInRoomList");
                    // 이름 추출 후 리스트에 추가
                    result.Add(stringBuilder.ToString());
                }
            }

            return result;
        }

        internal static Texture2D LoadTextureFromPng(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                UnityEngine.Debug.LogWarning($"PNG not found: {path}");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(path);

            // 2x2는 placeholder 사이즈 (LoadImage 호출 시 실제 크기로 변경됨)
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!texture.LoadImage(fileData))
            {
                UnityEngine.Debug.LogError($"Failed to load PNG: {path}");
                GameObject.Destroy(texture);
                return null;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            return texture;
        }

        internal static Dictionary<string, List<ChatUser>> LoadHeroinProfile(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                UnityEngine.Debug.LogWarning($"file not found: {jsonPath}");
                return new Dictionary<string, List<ChatUser>>();
            }

            try
            {
                string json = File.ReadAllText(jsonPath);

                var result =
                    JsonConvert.DeserializeObject<Dictionary<string, List<ChatUser>>>(json);

                return result ?? new Dictionary<string, List<ChatUser>>();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(ex);
                return new Dictionary<string, List<ChatUser>>();
            }
        }

        internal static void LoadHeroin(string pngPath)
        {
            Manager.Game instance = Singleton<Manager.Game>.Instance;
            ChaFileControl chaFileControl = new ChaFileControl();
            chaFileControl.LoadCharaFile(pngPath, 1, false, true);
            ChaControl chaControl = Singleton<Manager.Character>.Instance.CreateChara(1, Manager.Scene.commonSpace, 0, chaFileControl);
            chaControl.ChangeNowCoordinate(false, true);
            chaControl.releaseCustomInputTexture = false;
            chaControl.Load(false);
        }

        internal static SystemLanguage GetSystemLanguage()
        {
            UnityEngine.Debug.Log($"SetFontFromOSBySystemLanguage {Application.systemLanguage}");
            return Application.systemLanguage;
        }
    }

    class HeroinData
    {
        public string fullname = "";
	    public int birthMonth = 1;
		public int birthDay = 1;
        public int personality = 0;                		
		public float voiceRate = 0.5f;
		public int trait = 0;
		public int mind = 0;
		public int hAttribute = 0;
        public int age;
        public int encounterCnt;
        public int relationship;
    }
}
