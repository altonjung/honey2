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
using System.Security.Cryptography.X509Certificates;

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

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace HoneySelect2Maker
{
    public class Logic
    {
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
        
        internal static void EnsureOverlayCanvas(int sortingOrder)
        {
            if (HoneySelect2Maker._overlayCanvasGO != null)
            {
                HoneySelect2Maker._sceneCanvas.enabled = true;
                HoneySelect2Maker._sceneCanvas.sortingOrder = sortingOrder;
                return;
            }

            HoneySelect2Maker._overlayCanvasGO = new GameObject("SceneVideoCanvas");
            GameObject.DontDestroyOnLoad(HoneySelect2Maker._overlayCanvasGO);

            var canvas = HoneySelect2Maker._overlayCanvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder; // 최상단

            HoneySelect2Maker._sceneCanvas = canvas;

            HoneySelect2Maker._overlayCanvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            HoneySelect2Maker._overlayCanvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // RawImage
            GameObject rawGO = new GameObject("SceneVideoRawImage");
            rawGO.transform.SetParent(HoneySelect2Maker._overlayCanvasGO.transform, false);

            HoneySelect2Maker._rawImage = rawGO.AddComponent<UnityEngine.UI.RawImage>();

            var rect = HoneySelect2Maker._rawImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            HoneySelect2Maker._rawImage.color = Color.white;
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

            //UnityEngine.Debug.Log($">> PlaySceneVideo {path} | loop {loop} | audio {audio} | sortingOrder {sortingOrder} | {DateTime.Now:HH:mm:ss.fff}");

            EnsureOverlayCanvas(sortingOrder);

            var vp = HoneySelect2Maker._self._sceneVideoPlayer;

            vp.Stop();

            vp.prepareCompleted -= OnPrepared;
            if (!loop)
                vp.loopPointReached -= OnVideoCompleted;

            // RenderTexture 생성
            if (HoneySelect2Maker._sceneRT != null)
            {
                HoneySelect2Maker._sceneRT.Release();
                UnityEngine.Object.Destroy(HoneySelect2Maker._sceneRT);
            }

            HoneySelect2Maker._sceneRT = new RenderTexture(Screen.width, Screen.height, 0);
            HoneySelect2Maker._sceneRT.Create();

            vp.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
            vp.targetTexture = HoneySelect2Maker._sceneRT;

            HoneySelect2Maker._rawImage.texture = HoneySelect2Maker._sceneRT;
            HoneySelect2Maker._rawImage.enabled = true;

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
            
            HoneySelect2Maker._onSceneVideoCompleted = callback;

            vp.prepareCompleted += OnPrepared;

            if (!loop)
                vp.loopPointReached += OnVideoCompleted;

            vp.Prepare();
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

            if (HoneySelect2Maker._rawImage != null)
                HoneySelect2Maker._rawImage.enabled = false;

            HoneySelect2Maker._onSceneVideoCompleted?.Invoke();
            HoneySelect2Maker._onSceneVideoCompleted = null;
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
                    int idx = UnityEngine.Random.Range(0, Mathf.Min(HoneySelect2Maker.VIDEO_MAX_COUNT, video_files.Count));
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

        internal static void StopSceneVideo()
        {
            HoneySelect2Maker._self._sceneVideoPlayer.Stop();
            HoneySelect2Maker._sceneCanvas.enabled = false;
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
    }

    class HeroinData
    {
        public int favorite;
        public int age;
        public int encounterCnt;
    }
}