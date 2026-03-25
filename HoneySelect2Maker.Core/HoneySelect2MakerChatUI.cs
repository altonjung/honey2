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
using UnityEngine.UI;
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

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace HoneySelect2Maker
{
    /*
        사용법
        1) UI 생성: ChatUIController.CreateChatUI();
        2) 텍스트 출력: ChatUIController.AppendChat("System", "메시지");
        3) UI 해제: ChatUIController.DestroyChatUI();

        참고
        - 입력창에 메시지를 입력하고 엔터를 치면 로그로 출력됩니다.
        - EventSystem이 없으면 자동으로 생성됩니다.
    */
    // UGUI(Canvas + InputField/Text) 생성
    public class ChatUIController
    {
        public enum FontColorOption
        {
            White,
            Black
        }

        private static GameObject _chatCanvasGO;
        private static GameObject _chatRootGO;
        private static Text _chatLogText;
        private static InputField _chatInput;
        private static bool _initialized;
        private static int _fontSize = 16;
        private static FontColorOption _userFontColor = FontColorOption.White;
        private static FontColorOption _systemFontColor = FontColorOption.White;

        // Chat UI 생성 함수: 캔버스/패널/로그/입력창을 생성하고 하단에 고정한다.
        internal static void CreateChatUI(int sortingOrder = 19999)
        {
            if (_initialized)
                return;

            EnsureEventSystem();

            _chatCanvasGO = new GameObject("HS2MakerChatCanvas");
            GameObject.DontDestroyOnLoad(_chatCanvasGO);

            var canvas = _chatCanvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            _chatCanvasGO.AddComponent<CanvasScaler>();
            _chatCanvasGO.AddComponent<GraphicRaycaster>();

            _chatRootGO = new GameObject("ChatRoot");
            _chatRootGO.transform.SetParent(_chatCanvasGO.transform, false);

            var rootRect = _chatRootGO.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.0f, 0.0f);
            rootRect.anchorMax = new Vector2(1.0f, 0.0f);
            rootRect.pivot = new Vector2(0.5f, 0.0f);
            rootRect.sizeDelta = new Vector2(0.0f, 260.0f);
            rootRect.anchoredPosition = Vector2.zero;

            var bg = _chatRootGO.AddComponent<Image>();
            bg.color = new Color(0.0f, 0.0f, 0.0f, 0.6f);

            var logGO = new GameObject("ChatLog");
            logGO.transform.SetParent(_chatRootGO.transform, false);
            var logRect = logGO.AddComponent<RectTransform>();
            logRect.anchorMin = new Vector2(0.0f, 0.25f);
            logRect.anchorMax = new Vector2(1.0f, 1.0f);
            logRect.offsetMin = new Vector2(12.0f, 12.0f);
            logRect.offsetMax = new Vector2(-12.0f, -12.0f);

            _chatLogText = logGO.AddComponent<Text>();
            _chatLogText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _chatLogText.fontSize = _fontSize;
            _chatLogText.alignment = TextAnchor.LowerLeft;
            _chatLogText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _chatLogText.verticalOverflow = VerticalWrapMode.Overflow;
            _chatLogText.color = GetFontColor(false);
            _chatLogText.text = "";

            var inputGO = new GameObject("ChatInput");
            inputGO.transform.SetParent(_chatRootGO.transform, false);
            var inputRect = inputGO.AddComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0.0f, 0.0f);
            inputRect.anchorMax = new Vector2(1.0f, 0.25f);
            inputRect.offsetMin = new Vector2(12.0f, 12.0f);
            inputRect.offsetMax = new Vector2(-12.0f, -12.0f);

            var inputBg = inputGO.AddComponent<Image>();
            inputBg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            _chatInput = inputGO.AddComponent<InputField>();
            _chatInput.textComponent = CreateTextChild(inputGO.transform, "InputText", _fontSize, Color.white, TextAnchor.MiddleLeft);
            _chatInput.placeholder = CreateTextChild(inputGO.transform, "Placeholder", _fontSize, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft, "Type a message...");
            _chatInput.lineType = InputField.LineType.SingleLine;
            _chatInput.onEndEdit.AddListener(OnSubmitInput);

            CreateCloseButton(_chatRootGO.transform);

            _initialized = true;

            _chatInput.ActivateInputField();
        }

        // Chat UI 사용자 Prompt 수집 함수: 엔터 입력 시 호출되어 로그에 출력한다.
        private static void OnSubmitInput(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            AppendChat("User", value.Trim());
            UnityEngine.Debug.Log($">> Chat Input: {value.Trim()}");

            _chatInput.text = "";
            _chatInput.ActivateInputField();
        }

        // Chat UI 내 Prompt 출력 함수: 채팅 로그 텍스트에 한 줄을 추가한다.
        internal static void AppendChat(string speaker, string message)
        {
            if (_chatLogText == null)
                return;

            bool isUser = string.Equals(speaker, "User", StringComparison.OrdinalIgnoreCase);
            _chatLogText.color = GetFontColor(isUser);

            if (_chatLogText.text.Length > 0)
                _chatLogText.text += "\n";

            _chatLogText.text += $"[{speaker}] {message}";
        }

        // Chat UI 해제 함수: 생성된 UI 오브젝트를 제거하고 상태를 초기화한다.
        internal static void DestroyChatUI()
        {
            if (_chatCanvasGO != null)
            {
                GameObject.Destroy(_chatCanvasGO);
                _chatCanvasGO = null;
            }

            _chatRootGO = null;
            _chatLogText = null;
            _chatInput = null;
            _initialized = false;
        }

        // 폰트 크기 설정 함수: 이후 생성되는 메시지/입력 텍스트에 적용된다.
        internal static void SetFontSize(int fontSize)
        {
            _fontSize = Mathf.Clamp(fontSize, 10, 40);
        }

        // 폰트 색상 설정 함수: 사용자/시스템 각각 흰색/검정 중 선택한다.
        internal static void SetFontColors(FontColorOption userColor, FontColorOption systemColor)
        {
            _userFontColor = userColor;
            _systemFontColor = systemColor;
        }

        // EventSystem 보장 함수: 없으면 생성한다.
        private static void EnsureEventSystem()
        {
            if (GameObject.FindObjectOfType<EventSystem>() != null)
                return;

            var es = new GameObject("EventSystem");
            GameObject.DontDestroyOnLoad(es);
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // 텍스트 자식 생성 함수: InputField 텍스트/플레이스홀더에 사용한다.
        private static Text CreateTextChild(Transform parent, string name, int fontSize, Color color, TextAnchor anchor, string text = "")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8.0f, 6.0f);
            rect.offsetMax = new Vector2(-8.0f, -6.0f);

            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = text;

            return t;
        }

        private static void CreateCloseButton(Transform parent)
        {
            var buttonGO = new GameObject("ChatCloseButton");
            buttonGO.transform.SetParent(parent, false);

            var rect = buttonGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1.0f, 1.0f);
            rect.anchorMax = new Vector2(1.0f, 1.0f);
            rect.pivot = new Vector2(1.0f, 1.0f);
            rect.sizeDelta = new Vector2(90.0f, 28.0f);
            rect.anchoredPosition = new Vector2(-12.0f, -12.0f);

            var image = buttonGO.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            var button = buttonGO.AddComponent<Button>();
            button.onClick.AddListener(DestroyChatUI);

            var text = CreateTextChild(buttonGO.transform, "Label", 14, Color.white, TextAnchor.MiddleCenter, "Close");
            text.raycastTarget = false;
        }

        private static Color GetFontColor(bool isUser)
        {
            var option = isUser ? _userFontColor : _systemFontColor;
            return option == FontColorOption.Black ? Color.black : Color.white;
        }
    }
}
