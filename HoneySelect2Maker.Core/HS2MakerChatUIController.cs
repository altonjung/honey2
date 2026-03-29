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
        1) UI 생성: var chatUI = new HS2ChatUIController(); chatUI.CreateChatUI(chatController, actionController, user, heroin);
        2) 텍스트 출력: chatUI.AppendChat("System", "메시지");
        3) UI 해제: chatUI.DestroyChatUI();

        참고
        - 입력창에 메시지를 입력하고 엔터를 치면 로그로 출력됩니다.
        - EventSystem이 없으면 자동으로 생성됩니다.

        font 사용
        C:\Users\<사용자>\AppData\Local\Microsoft\Windows\Fonts 폴더에 ttf 혹은 otf 파일 설치        

    */
    // UGUI(Canvas + InputField/Text) 생성
    public class HS2ChatUIController
    {
        public enum FontColorOption
        {
            White,
            Black
        }

        private GameObject _chatCanvasGO;
        private GameObject _chatRootGO;
        private Text _chatLogText;
        private InputField _chatInput;
        private bool _initialized;
        private int _fontSize = 24;
        private FontColorOption _userFontColor = FontColorOption.White;
        private FontColorOption _systemFontColor = FontColorOption.White;
        private HS2ChatController _chatController;
        private HS2ActionController _actionController;
        private Font _customFont;
        private string _customFontResourcePath;
        private string[] _customOSFontNames;

        private ChatUser _user;
        private ChatUser _heroin;

        // Chat UI 생성 함수: 캔버스/패널/로그/입력창을 생성하고 하단에 고정한다.
        internal void CreateChatUI(
            HS2ChatController chatController,
            HS2ActionController actionController,
            ChatUser user, 
            ChatUser heroin,
            int sortingOrder = 19999)
        {
            _user = user;
            _heroin = heroin;
           
            if (chatController == null)
                throw new ArgumentNullException(nameof(chatController));
            if (actionController == null)
                throw new ArgumentNullException(nameof(actionController));

            _chatController = chatController;
            _actionController = actionController;

            if (_initialized)
            {
                _chatInput?.ActivateInputField();
                return;
            }

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

            // var bg = _chatRootGO.AddComponent<Image>();
            // bg.color = new Color(0.0f, 0.0f, 0.0f, 0.0f);

            var logGO = new GameObject("ChatLog");
            logGO.transform.SetParent(_chatRootGO.transform, false);
            var logRect = logGO.AddComponent<RectTransform>();
            logRect.anchorMin = new Vector2(0.0f, 0.25f);
            logRect.anchorMax = new Vector2(1.0f, 1.0f);
            logRect.offsetMin = new Vector2(12.0f, 12.0f);
            logRect.offsetMax = new Vector2(-12.0f, -12.0f);

            _chatLogText = logGO.AddComponent<Text>();
            _chatLogText.font = GetFont();
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
        private async void OnSubmitInput(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (_chatController == null || _actionController == null)
            {
                UnityEngine.Debug.LogWarning("Chat controllers are not set. Call CreateChatUI with controllers.");
                return;
            }

            AppendChat("You", value.Trim());
            _chatInput.text = "waiting..";
  
            UnityEngine.Debug.Log($">> User Chat Input: {value.Trim()}");

            string result = await _chatController.SendChatAsync(
                4096,
                0.2f,
                CHAT_EVENT.CHAT_Bump,
                _user,
                _heroin,
                value.Trim());
            AppendChat(_heroin.name, result.Trim());
            
            _chatInput.text = "";
            _chatInput.ActivateInputField();
        }

        // Chat UI 내 Prompt 출력 함수: 채팅 로그 텍스트에 한 줄을 추가한다.
        internal void AppendChat(string speaker, string message)
        {
            if (_chatLogText == null)
                return;

            bool isUser = string.Equals(speaker, "You", StringComparison.OrdinalIgnoreCase);
            _chatLogText.color = GetFontColor(isUser);

            if (_chatLogText.text.Length > 0)
                _chatLogText.text += "\n";

            _chatLogText.text += $"[{speaker}] {message}";
        }

        // Chat UI 해제 함수: 생성된 UI 오브젝트를 제거하고 상태를 초기화한다.
        internal void DestroyChatUI()
        {
            if (_chatCanvasGO != null)
            {
                GameObject.Destroy(_chatCanvasGO);
                _chatCanvasGO = null;
            }

            _chatRootGO = null;
            _chatLogText = null;
            _chatInput = null;
            _chatController = null;
            _actionController = null;
            _initialized = false;

            HS2SceneController.DestroyCurrentRender();
        }

        // 폰트 크기 설정 함수: 이후 생성되는 메시지/입력 텍스트에 적용된다.
        internal void SetFontSize(int fontSize)
        {
            _fontSize = Mathf.Clamp(fontSize, 10, 40);
        }

        // 폰트 색상 설정 함수: 사용자/시스템 각각 흰색/검정 중 선택한다.
        internal void SetFontColors(FontColorOption userColor, FontColorOption systemColor)
        {
            _userFontColor = userColor;
            _systemFontColor = systemColor;
        }

        // EventSystem 보장 함수: 없으면 생성한다.
        private void EnsureEventSystem()
        {
            if (GameObject.FindObjectOfType<EventSystem>() != null)
                return;

            var es = new GameObject("EventSystem");
            GameObject.DontDestroyOnLoad(es);
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // 텍스트 자식 생성 함수: InputField 텍스트/플레이스홀더에 사용한다.
        private Text CreateTextChild(Transform parent, string name, int fontSize, Color color, TextAnchor anchor, string text = "")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8.0f, 6.0f);
            rect.offsetMax = new Vector2(-8.0f, -6.0f);

            var t = go.AddComponent<Text>();
            t.font = GetFont();
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = text;

            return t;
        }

        private void CreateCloseButton(Transform parent)
        {
            var buttonGO = new GameObject("ChatCloseButton");
            buttonGO.transform.SetParent(parent, false);

            var rect = buttonGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1.0f, 1.0f);
            rect.anchorMax = new Vector2(1.0f, 1.0f);
            rect.pivot = new Vector2(1.0f, 1.0f);
            rect.sizeDelta = new Vector2(180.0f, 56.0f);
            rect.anchoredPosition = new Vector2(-12.0f, -12.0f);

            var image = buttonGO.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            var button = buttonGO.AddComponent<Button>();
            button.onClick.AddListener(DestroyChatUI);

            var text = CreateTextChild(buttonGO.transform, "Label", 28, Color.white, TextAnchor.MiddleCenter, "End Chat");
            text.raycastTarget = false;
        }

        private Color GetFontColor(bool isUser)
        {
            var option = isUser ? _userFontColor : _systemFontColor;
            return option == FontColorOption.Black ? Color.black : Color.white;
        }

        // 폰트 지정: 유니티 Resources에 포함된 폰트를 경로로 지정한다. (예: "Fonts/NotoSansCJK")
        internal void SetFontResourcePath(string resourcePath)
        {
            _customFontResourcePath = resourcePath;
            _customFont = null;
            _customOSFontNames = null;
        }

        // 폰트 지정: 외부에서 로드된 Font를 직접 주입한다.
        internal void SetFont(Font font)
        {
            _customFont = font;
            _customFontResourcePath = null;
            _customOSFontNames = null;
        }

        // 폰트 지정: OS에 설치된 폰트를 사용한다. (예: "Noto Sans CJK KR")
        internal void SetFontFromOS(params string[] fontNames)
        {
            _customOSFontNames = fontNames;
            _customFont = null;
            _customFontResourcePath = null;
        }

        // OS 시스템 언어에 맞춰 폰트를 자동 선택한다.
        internal void SetFontFromOSBySystemLanguage()
        {
            UnityEngine.Debug.Log($"SetFontFromOSBySystemLanguage {Application.systemLanguage}");

            switch (Application.systemLanguage)
            {
                case SystemLanguage.Korean:
                    SetFontFromOS("Noto Sans CJK KR", "Malgun Gothic", "Noto Sans CJK");
                    break;
                case SystemLanguage.Japanese:
                    SetFontFromOS("Noto Sans CJK JP", "Yu Gothic", "Meiryo", "Noto Sans CJK");
                    break;
                case SystemLanguage.ChineseSimplified:
                    SetFontFromOS("Noto Sans CJK SC", "Microsoft YaHei", "SimHei", "Noto Sans CJK");
                    break;
                case SystemLanguage.ChineseTraditional:
                    SetFontFromOS("Noto Sans CJK TC", "Microsoft JhengHei", "PMingLiU", "Noto Sans CJK");
                    break;
                default:
                    SetFontFromOS("Noto Sans CJK KR", "Noto Sans CJK", "Arial");
                    break;
            }
        }

        private Font GetFont()
        {
            if (_customFont != null)
                return _customFont;

            if (_customOSFontNames != null && _customOSFontNames.Length > 0)
            {
                foreach (var fontName in _customOSFontNames)
                {
                    if (string.IsNullOrWhiteSpace(fontName))
                        continue;

                    _customFont = Font.CreateDynamicFontFromOSFont(fontName, _fontSize);
                    if (_customFont != null)
                        return _customFont;
                }

                UnityEngine.Debug.LogWarning("OS Font not found from provided list. Fallback to Arial.");
            }

            if (!string.IsNullOrWhiteSpace(_customFontResourcePath))
            {
                _customFont = Resources.Load<Font>(_customFontResourcePath);
                if (_customFont != null)
                    return _customFont;

                UnityEngine.Debug.LogWarning($"Font not found at Resources path: {_customFontResourcePath}. Fallback to Arial.");
            }

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }
}
