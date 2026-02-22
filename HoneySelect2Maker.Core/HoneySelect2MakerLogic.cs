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
#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace HoneySelect2Maker
{
    public class Logic
    {

        internal static List<string> GetVideoFiles(string folderPath)
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
        
        internal static void EnsureOverlayCanvas(int sortingOrder)
        {
            if (HoneySelect2Maker._overlayCanvasGO != null)
            {
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

        internal static void PlaySceneVideo(
            string path,
            bool loop = false,
            int sortingOrder = -100,
            Action callback = null)
        {
            EnsureOverlayCanvas(sortingOrder);

            var vp = HoneySelect2Maker._self._sceneVideoPlayer;

            vp.Stop();

            vp.prepareCompleted -= OnPrepared;
            vp.loopPointReached -= OnVideoCompleted;

            // RenderTexture 생성
            if (HoneySelect2Maker._sceneRT != null)
            {
                HoneySelect2Maker._sceneRT.Release();
                UnityEngine.Object.Destroy(HoneySelect2Maker._sceneRT);
            }

            HoneySelect2Maker._sceneRT = new RenderTexture(1920, 1080, 0);
            HoneySelect2Maker._sceneRT.Create();

            vp.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
            vp.targetTexture = HoneySelect2Maker._sceneRT;

            HoneySelect2Maker._rawImage.texture = HoneySelect2Maker._sceneRT;
            HoneySelect2Maker._rawImage.enabled = true;

            vp.source = UnityEngine.Video.VideoSource.Url;
            vp.url = path;
            vp.isLooping = loop;
            vp.playOnAwake = false;
            vp.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;

            HoneySelect2Maker._onSceneVideoCompleted = callback;

            vp.prepareCompleted += OnPrepared;
            vp.loopPointReached += OnVideoCompleted;

            vp.Prepare();
        }

        internal static void OnPrepared(UnityEngine.Video.VideoPlayer vp)
        {
            vp.prepareCompleted -= OnPrepared;

            UnityEngine.Debug.Log($">> Video Prepared | {DateTime.Now:HH:mm:ss.fff}");

            vp.Play();
        }

        internal static void OnVideoCompleted(UnityEngine.Video.VideoPlayer vp)
        {
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

        internal static void PlayVideo(string video_path, bool isTitle = true, int sortingOrder = 9999)
        {
                HoneySelect2Maker._videoFinished = false;
                UnityEngine.Debug.Log($">> PlayVideo {video_path} | {DateTime.Now:HH:mm:ss.fff}");

                List<string> video_files  = new List<string>();
                bool isMainCamera = true;
                bool isLoop=false;

                if (isTitle) {
                    sortingOrder = -100;
                }

                video_files = GetVideoFiles(video_path);

                if (video_files.Count > 0)
                {
                    int idx = UnityEngine.Random.Range(0, Mathf.Min(HoneySelect2Maker.VIDEO_MAX_COUNT, video_files.Count));
                    string path = video_path + video_files[idx];

                    if (path.Contains("loop"))
                    {
                        isLoop = true;
                    }

                    if (isLoop)
                    {
                        PlaySceneVideo(
                            path,
                            true,
                            sortingOrder
                        );   
                    } 
                    else
                    {

                        PlaySceneVideo(
                            path,
                            false,
                            sortingOrder,
                            () =>
                            {   
                                if (!isTitle) {
                                // 컷신용 제거
                                    StopSceneVideo();
                                }
                            }
                        );
                    }
                }          
        }

        internal static void StopSceneVideo()
        {
            HoneySelect2Maker._self._sceneVideoPlayer.Stop();
        }     
    }
}