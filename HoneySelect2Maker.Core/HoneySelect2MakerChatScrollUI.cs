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
        - 이 버전은 ScrollRect 기반으로 메시지가 스크롤됩니다.
    */
    // UGUI(Canvas + ScrollRect + InputField/Text) 생성
    public class ChatUIController
    {
        public enum FontColorOption
        {
            White,
            Black
        }

        private static GameObject _chatCanvasGO;
        private static GameObject _chatRootGO;
        private static ScrollRect _chatScrollRect;
        private static RectTransform _chatContent;
        private static InputField _chatInput;
        private static bool _initialized;
        private static readonly List<GameObject> _messageItems = new List<GameObject>();
        private const int MaxMessageCount = 100;
        private static int _fontSize = 16;
        private static FontColorOption _userFontColor = FontColorOption.White;
        private static FontColorOption _systemFontColor = FontColorOption.White;

        // Chat UI 생성 함수: 캔버스/패널/로그/입력창을 생성하고 하단에 고정한다.
        internal static void CreateChatUI(int sortingOrder = 1000)
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

            CreateScrollArea(_chatRootGO.transform);

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

            _initialized = true;
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

        // Chat UI 내 Prompt 출력 함수: 채팅 로그에 한 줄을 추가하고 스크롤을 아래로 내린다.
        /*
            사용자: AppendChat("User", "안녕하세요")
            시스템: AppendChat("System", "환영합니다")
        */
        internal static void AppendChat(string speaker, string message)
        {
            if (_chatContent == null)
                return;

            bool isUser = string.Equals(speaker, "User", StringComparison.OrdinalIgnoreCase);
            var line = CreateMessageLine(_chatContent, $"{speaker}", message, isUser);
            _messageItems.Add(line.gameObject);
            TrimMessagesIfNeeded();

            Canvas.ForceUpdateCanvases();
            if (_chatScrollRect != null)
                _chatScrollRect.verticalNormalizedPosition = 0.0f;
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
            _chatScrollRect = null;
            _chatContent = null;
            _chatInput = null;
            _messageItems.Clear();
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

        // 스크롤 영역 생성 함수: ScrollRect, Viewport, Content를 구성한다.
        private static void CreateScrollArea(Transform parent)
        {
            var scrollGO = new GameObject("ChatScroll");
            scrollGO.transform.SetParent(parent, false);
            var scrollRect = scrollGO.AddComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.0f, 0.25f);
            scrollRect.anchorMax = new Vector2(1.0f, 1.0f);
            scrollRect.offsetMin = new Vector2(12.0f, 12.0f);
            scrollRect.offsetMax = new Vector2(-12.0f, -12.0f);

            var scrollBg = scrollGO.AddComponent<Image>();
            scrollBg.color = new Color(0.0f, 0.0f, 0.0f, 0.0f);

            _chatScrollRect = scrollGO.AddComponent<ScrollRect>();
            _chatScrollRect.horizontal = false;
            _chatScrollRect.vertical = true;
            _chatScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _chatScrollRect.scrollSensitivity = 20.0f;

            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);
            var viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            var viewportImg = viewportGO.AddComponent<Image>();
            viewportImg.color = new Color(0f, 0f, 0f, 0f);
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            _chatContent = contentGO.AddComponent<RectTransform>();
            _chatContent.anchorMin = new Vector2(0.0f, 1.0f);
            _chatContent.anchorMax = new Vector2(1.0f, 1.0f);
            _chatContent.pivot = new Vector2(0.5f, 1.0f);
            _chatContent.anchoredPosition = Vector2.zero;
            _chatContent.sizeDelta = new Vector2(0.0f, 0.0f);

            var layout = contentGO.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _chatScrollRect.viewport = viewportRect;
            _chatScrollRect.content = _chatContent;
        }

        // 메시지 라인 생성 함수: Scroll Content에 Text를 추가한다.
        private static Text CreateMessageLine(Transform parent, string speaker, string message, bool isUser)
        {
            var lineGO = new GameObject("ChatLine");
            lineGO.transform.SetParent(parent, false);

            var rect = lineGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(1.0f, 1.0f);
            rect.pivot = new Vector2(0.5f, 1.0f);

            var row = lineGO.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(6, 6, 2, 2);
            row.spacing = 6;
            row.childAlignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            row.childControlHeight = true;
            row.childControlWidth = true;
            row.childForceExpandHeight = false;
            row.childForceExpandWidth = true;

            var bubbleGO = new GameObject("Bubble");
            bubbleGO.transform.SetParent(lineGO.transform, false);
            var bubbleRect = bubbleGO.AddComponent<RectTransform>();
            bubbleRect.pivot = new Vector2(isUser ? 1.0f : 0.0f, 1.0f);

            var bubbleImg = bubbleGO.AddComponent<Image>();
            bubbleImg.color = isUser ? new Color(0.15f, 0.45f, 0.95f, 0.9f) : new Color(0.2f, 0.2f, 0.2f, 0.85f);

            var bubbleLayout = bubbleGO.AddComponent<LayoutElement>();
            bubbleLayout.minWidth = 80.0f;
            bubbleLayout.preferredWidth = 400.0f;
            bubbleLayout.flexibleWidth = 0.0f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(bubbleGO.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10.0f, 6.0f);
            textRect.offsetMax = new Vector2(-10.0f, -6.0f);

            var text = textGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = _fontSize;
            text.color = GetFontColor(isUser);
            text.alignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = $"[{speaker}] {message}";

            var fitter = bubbleGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return text;
        }

        private static Color GetFontColor(bool isUser)
        {
            var option = isUser ? _userFontColor : _systemFontColor;
            return option == FontColorOption.Black ? Color.black : Color.white;
        }

        // 메시지 개수 제한 함수: 초과 시 가장 오래된 항목부터 제거한다.
        private static void TrimMessagesIfNeeded()
        {
            while (_messageItems.Count > MaxMessageCount)
            {
                var oldest = _messageItems[0];
                _messageItems.RemoveAt(0);
                if (oldest != null)
                    GameObject.Destroy(oldest);
            }
        }
    }
}
