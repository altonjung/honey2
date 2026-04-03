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
        private ScrollRect _chatScrollRect;
        private RectTransform _chatContent;
        private InputField _chatInput;
        private bool _initialized;
        private int _fontSize = 24;
        private readonly List<GameObject> _messageItems = new List<GameObject>();
        private const int MaxMessageCount = 100;
        private FontColorOption _userFontColor = FontColorOption.White;
        private FontColorOption _systemFontColor = FontColorOption.White;
        private HS2ChatController _chatController;
        private HS2ActionController _actionController;
        private Font _customFont;
        private string _customFontResourcePath;
        private string[] _customOSFontNames;

        private ChatUser _user;
        private ChatUser _heroin;
        private readonly Dictionary<string, Sprite> _avatarSpriteCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static Sprite _circleMaskSprite;
        private string _avatarFolderPath = UserData.Path + "/hs2maker/chat/avatar/";

        private const float MaxBubbleWidth = 520.0f;
        private const float BubblePaddingX = 18.0f;
        private const float BubblePaddingY = 12.0f;

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
            rootRect.anchorMax = new Vector2(1.0f, 1.0f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = Vector2.zero;
            rootRect.anchoredPosition = Vector2.zero;

            var bg = _chatRootGO.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.06f, 0.25f);

            CreateScrollArea(_chatRootGO.transform);

            var inputGO = new GameObject("ChatInput");
            inputGO.transform.SetParent(_chatRootGO.transform, false);
            var inputRect = inputGO.AddComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0.0f, 0.0f);
            inputRect.anchorMax = new Vector2(1.0f, 0.20f);
            inputRect.offsetMin = new Vector2(24.0f, 24.0f);
            inputRect.offsetMax = new Vector2(-24.0f, -24.0f);

            var inputBg = inputGO.AddComponent<Image>();
            inputBg.color = new Color(0.08f, 0.08f, 0.1f, 0.9f);

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
            if (_chatContent == null)
                return;

            bool isUser = string.Equals(speaker, "You", StringComparison.OrdinalIgnoreCase);
            var line = CreateMessageLine(_chatContent, speaker, message, isUser);
            _messageItems.Add(line);
            TrimMessagesIfNeeded();

            LayoutRebuilder.ForceRebuildLayoutImmediate(_chatContent);
            Canvas.ForceUpdateCanvases();
            if (_chatScrollRect != null)
            {
                _chatScrollRect.verticalNormalizedPosition = 0.0f;
                _chatScrollRect.velocity = Vector2.zero;
            }
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
            _chatScrollRect = null;
            _chatContent = null;
            _chatInput = null;
            _chatController = null;
            _actionController = null;
            _messageItems.Clear();
            _avatarSpriteCache.Clear();
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

        private void CreateScrollArea(Transform parent)
        {
            var scrollGO = new GameObject("ChatScroll");
            scrollGO.transform.SetParent(parent, false);
            var scrollRect = scrollGO.AddComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.0f, 0.20f);
            scrollRect.anchorMax = new Vector2(1.0f, 1.0f);
            scrollRect.offsetMin = new Vector2(24.0f, 24.0f);
            scrollRect.offsetMax = new Vector2(-24.0f, -24.0f);

            var scrollBg = scrollGO.AddComponent<Image>();
            scrollBg.color = new Color(0.03f, 0.03f, 0.06f, 0.45f);

            _chatScrollRect = scrollGO.AddComponent<ScrollRect>();
            _chatScrollRect.horizontal = false;
            _chatScrollRect.vertical = true;
            _chatScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _chatScrollRect.scrollSensitivity = 30.0f;

            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);
            var viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            var viewportImage = viewportGO.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            viewportImage.raycastTarget = false;
            viewportGO.AddComponent<RectMask2D>();

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            _chatContent = contentGO.AddComponent<RectTransform>();
            _chatContent.anchorMin = new Vector2(0.0f, 1.0f);
            _chatContent.anchorMax = new Vector2(1.0f, 1.0f);
            _chatContent.pivot = new Vector2(0.5f, 1.0f);
            _chatContent.anchoredPosition = Vector2.zero;
            _chatContent.sizeDelta = Vector2.zero;

            var vLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(10, 10, 10, 10);
            vLayout.spacing = 10;
            vLayout.childAlignment = TextAnchor.UpperLeft;
            vLayout.childControlHeight = true;
            vLayout.childControlWidth = true;
            vLayout.childForceExpandHeight = false;
            vLayout.childForceExpandWidth = true;

            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _chatScrollRect.viewport = viewportRect;
            _chatScrollRect.content = _chatContent;
        }

        private GameObject CreateMessageLine(Transform parent, string speaker, string message, bool isUser)
        {
            var lineGO = new GameObject("ChatLine");
            lineGO.transform.SetParent(parent, false);

            var lineRect = lineGO.AddComponent<RectTransform>();
            lineRect.anchorMin = new Vector2(0.0f, 1.0f);
            lineRect.anchorMax = new Vector2(1.0f, 1.0f);
            lineRect.pivot = new Vector2(0.5f, 1.0f);

            var lineLayout = lineGO.AddComponent<HorizontalLayoutGroup>();
            lineLayout.padding = new RectOffset(6, 6, 2, 2);
            lineLayout.spacing = 8.0f;
            lineLayout.childAlignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            lineLayout.childControlHeight = true;
            lineLayout.childControlWidth = true;
            lineLayout.childForceExpandHeight = false;
            lineLayout.childForceExpandWidth = false;

            if (isUser)
                CreateSpacer(lineGO.transform);

            var rowGO = new GameObject("MessageRow");
            rowGO.transform.SetParent(lineGO.transform, false);

            var rowLayoutElem = rowGO.AddComponent<LayoutElement>();
            rowLayoutElem.flexibleWidth = 0.0f;
            rowLayoutElem.minWidth = 220.0f;

            var rowLayout = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10.0f;
            rowLayout.padding = new RectOffset(8, 8, 8, 8);
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            if (!isUser)
                CreateAvatarWidget(rowGO.transform, speaker, isUser);

            var textAreaGO = new GameObject("TextArea");
            textAreaGO.transform.SetParent(rowGO.transform, false);
            var textAreaLayout = textAreaGO.AddComponent<VerticalLayoutGroup>();
            textAreaLayout.spacing = 4.0f;
            textAreaLayout.padding = new RectOffset(0, 0, 0, 0);
            textAreaLayout.childAlignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            textAreaLayout.childControlWidth = true;
            textAreaLayout.childControlHeight = true;
            textAreaLayout.childForceExpandWidth = false;
            textAreaLayout.childForceExpandHeight = false;

            var textAreaLE = textAreaGO.AddComponent<LayoutElement>();
            textAreaLE.preferredWidth = MaxBubbleWidth;

            var nameGO = new GameObject("SpeakerName");
            nameGO.transform.SetParent(textAreaGO.transform, false);
            var nameText = nameGO.AddComponent<Text>();
            nameText.font = GetFont();
            nameText.fontSize = Mathf.Max(14, _fontSize - 6);
            nameText.color = isUser ? new Color(0.75f, 0.88f, 1.0f, 0.95f) : new Color(1.0f, 0.82f, 0.9f, 0.95f);
            nameText.alignment = isUser ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameText.verticalOverflow = VerticalWrapMode.Overflow;
            nameText.text = speaker;

            var nameLE = nameGO.AddComponent<LayoutElement>();
            nameLE.preferredHeight = Mathf.Max(18, _fontSize - 2);

            var bubbleGO = new GameObject("Bubble");
            bubbleGO.transform.SetParent(textAreaGO.transform, false);
            var bubbleImage = bubbleGO.AddComponent<Image>();
            bubbleImage.color = isUser ? new Color(0.12f, 0.40f, 0.88f, 0.85f) : new Color(0.87f, 0.33f, 0.58f, 0.82f);
            bubbleImage.raycastTarget = false;

            var bubbleLE = bubbleGO.AddComponent<LayoutElement>();
            bubbleLE.minWidth = 100.0f;

            var bubbleTextGO = new GameObject("Message");
            bubbleTextGO.transform.SetParent(bubbleGO.transform, false);
            var bubbleRect = bubbleTextGO.AddComponent<RectTransform>();
            bubbleRect.anchorMin = new Vector2(0.0f, 1.0f);
            bubbleRect.anchorMax = new Vector2(0.0f, 1.0f);
            bubbleRect.pivot = new Vector2(0.0f, 1.0f);
            bubbleRect.anchoredPosition = new Vector2(BubblePaddingX, -BubblePaddingY);

            var bodyText = bubbleTextGO.AddComponent<Text>();
            bodyText.font = GetFont();
            bodyText.fontSize = _fontSize;
            bodyText.color = GetFontColor(isUser);
            bodyText.alignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            bodyText.supportRichText = true;
            bodyText.text = message ?? "";
            bodyText.raycastTarget = false;

            var bodyLE = bubbleTextGO.AddComponent<LayoutElement>();
            bodyLE.flexibleWidth = 0.0f;

            var measureSettings = bodyText.GetGenerationSettings(new Vector2(10000f, 0f));
            measureSettings.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rawTextWidth = bodyText.cachedTextGeneratorForLayout.GetPreferredWidth(bodyText.text, measureSettings) / bodyText.pixelsPerUnit;
            var targetTextWidth = Mathf.Min(MaxBubbleWidth - (BubblePaddingX * 2.0f), rawTextWidth);

            bodyLE.preferredWidth = targetTextWidth;
            bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetTextWidth);

            var heightSettings = bodyText.GetGenerationSettings(new Vector2(targetTextWidth, 0f));
            heightSettings.horizontalOverflow = HorizontalWrapMode.Wrap;
            var textHeight = bodyText.cachedTextGeneratorForLayout.GetPreferredHeight(bodyText.text, heightSettings) / bodyText.pixelsPerUnit;
            bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight);

            var bubbleWidth = targetTextWidth + (BubblePaddingX * 2.0f);
            var bubbleHeight = textHeight + (BubblePaddingY * 2.0f);
            bubbleLE.preferredWidth = Mathf.Max(bubbleLE.minWidth, bubbleWidth);
            bubbleLE.preferredHeight = bubbleHeight;
            bubbleLE.minHeight = bubbleHeight;

            if (isUser)
                CreateAvatarWidget(rowGO.transform, speaker, isUser);

            if (!isUser)
                CreateSpacer(lineGO.transform);

            var lineLE = lineGO.AddComponent<LayoutElement>();
            lineLE.preferredHeight = Mathf.Max(74.0f, bubbleHeight + 26.0f);
            lineLE.minHeight = lineLE.preferredHeight;

            return lineGO;
        }

        private void CreateAvatarWidget(Transform parent, string speaker, bool isUser)
        {
            var avatarGO = new GameObject("Avatar");
            avatarGO.transform.SetParent(parent, false);

            var avatarLE = avatarGO.AddComponent<LayoutElement>();
            avatarLE.preferredWidth = 58.0f;
            avatarLE.preferredHeight = 58.0f;
            avatarLE.minWidth = 58.0f;
            avatarLE.minHeight = 58.0f;

            var avatarBg = avatarGO.AddComponent<Image>();
            avatarBg.color = isUser ? new Color(0.18f, 0.30f, 0.56f, 0.9f) : new Color(0.58f, 0.22f, 0.42f, 0.9f);
            avatarBg.sprite = GetCircleMaskSprite();
            avatarBg.type = Image.Type.Simple;
            avatarBg.raycastTarget = false;

            var maskGO = new GameObject("AvatarMask");
            maskGO.transform.SetParent(avatarGO.transform, false);
            var maskRect = maskGO.AddComponent<RectTransform>();
            maskRect.anchorMin = Vector2.zero;
            maskRect.anchorMax = Vector2.one;
            maskRect.offsetMin = new Vector2(4f, 4f);
            maskRect.offsetMax = new Vector2(-4f, -4f);

            var maskImage = maskGO.AddComponent<Image>();
            maskImage.sprite = GetCircleMaskSprite();
            maskImage.type = Image.Type.Simple;
            maskImage.color = Color.white;
            maskImage.raycastTarget = false;
            maskGO.AddComponent<Mask>().showMaskGraphic = false;

            var avatarImageGO = new GameObject("AvatarImage");
            avatarImageGO.transform.SetParent(maskGO.transform, false);
            var avatarImageRect = avatarImageGO.AddComponent<RectTransform>();
            avatarImageRect.anchorMin = Vector2.zero;
            avatarImageRect.anchorMax = Vector2.one;
            avatarImageRect.offsetMin = Vector2.zero;
            avatarImageRect.offsetMax = Vector2.zero;

            var avatarImage = avatarImageGO.AddComponent<Image>();
            avatarImage.raycastTarget = false;
            avatarImage.preserveAspect = true;
            avatarImage.color = Color.white;

            var avatarSprite = GetAvatarSprite(speaker, isUser);
            if (avatarSprite != null)
            {
                avatarImage.sprite = avatarSprite;
            }
            else
            {
                avatarImage.color = new Color(0f, 0f, 0f, 0f);
                var initialsGO = new GameObject("AvatarInitials");
                initialsGO.transform.SetParent(maskGO.transform, false);
                var initialsRect = initialsGO.AddComponent<RectTransform>();
                initialsRect.anchorMin = Vector2.zero;
                initialsRect.anchorMax = Vector2.one;
                initialsRect.offsetMin = Vector2.zero;
                initialsRect.offsetMax = Vector2.zero;

                var initialsText = initialsGO.AddComponent<Text>();
                initialsText.font = GetFont();
                initialsText.fontSize = Mathf.Max(14, _fontSize - 6);
                initialsText.alignment = TextAnchor.MiddleCenter;
                initialsText.color = Color.white;
                initialsText.text = GetInitials(speaker);
                initialsText.raycastTarget = false;
            }
        }

        private GameObject CreateSpacer(Transform parent)
        {
            var spacerGO = new GameObject("Spacer");
            spacerGO.transform.SetParent(parent, false);
            var spacerLE = spacerGO.AddComponent<LayoutElement>();
            spacerLE.flexibleWidth = 1.0f;
            spacerLE.minWidth = 0.0f;
            return spacerGO;
        }

        private Sprite GetAvatarSprite(string speaker, bool isUser)
        {
            var key = $"{speaker}|{isUser}";
            if (_avatarSpriteCache.TryGetValue(key, out var cached))
                return cached;

            var candidates = new List<string>();
            var speakerSafe = SanitizeFileName(speaker);
            var userSafe = SanitizeFileName(_user != null ? _user.name : "you");
            var heroinSafe = SanitizeFileName(_heroin != null ? _heroin.name : "heroin");

            if (isUser)
            {
                candidates.Add(Path.Combine(_avatarFolderPath, "you.png"));
                candidates.Add(Path.Combine(_avatarFolderPath, userSafe + ".png"));
                candidates.Add(Path.Combine(_avatarFolderPath, "user.png"));
            }
            else
            {
                candidates.Add(Path.Combine(_avatarFolderPath, speakerSafe + ".png"));
                candidates.Add(Path.Combine(_avatarFolderPath, heroinSafe + ".png"));
                candidates.Add(Path.Combine(_avatarFolderPath, "heroin.png"));
                candidates.Add(Path.Combine(_avatarFolderPath, "npc.png"));
            }

            candidates.Add(Path.Combine(_avatarFolderPath, "default.png"));

            foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path))
                    continue;

                Texture2D tex = HS2SceneController.LoadTextureFromPng(path);
                if (tex == null)
                    continue;

                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                _avatarSpriteCache[key] = sprite;
                return sprite;
            }

            _avatarSpriteCache[key] = null;
            return null;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        private static string GetInitials(string speaker)
        {
            if (string.IsNullOrWhiteSpace(speaker))
                return "?";

            var tokens = speaker.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return speaker.Substring(0, 1).ToUpperInvariant();

            if (tokens.Length == 1)
                return tokens[0].Substring(0, 1).ToUpperInvariant();

            return (tokens[0].Substring(0, 1) + tokens[tokens.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        private static Sprite GetCircleMaskSprite()
        {
            if (_circleMaskSprite != null)
                return _circleMaskSprite;

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = (size - 1) * 0.5f;
            var radius = size * 0.5f;
            var radiusSq = radius * radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    bool inside = (dx * dx + dy * dy) <= radiusSq;
                    texture.SetPixel(x, y, inside ? Color.white : new Color(1f, 1f, 1f, 0f));
                }
            }

            texture.Apply();
            _circleMaskSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circleMaskSprite;
        }

        private void TrimMessagesIfNeeded()
        {
            while (_messageItems.Count > MaxMessageCount)
            {
                var oldest = _messageItems[0];
                _messageItems.RemoveAt(0);
                if (oldest != null)
                    GameObject.Destroy(oldest);
            }
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

        // 아바타 폴더 지정: 메시지 화자명 기반으로 `{speaker}.png`를 조회한다.
        internal void SetAvatarFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            _avatarFolderPath = folderPath;
            _avatarSpriteCache.Clear();
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
