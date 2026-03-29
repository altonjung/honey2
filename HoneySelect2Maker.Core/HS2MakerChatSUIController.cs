using Studio;
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
    public class HS2ChatSUIController
    {
        private const bool DebugVisuals = false;
        private const bool DebugAutoMessage = false;
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
        private static int _fontSize = 24;
        private static FontColorOption _userFontColor = FontColorOption.White;
        private static FontColorOption _systemFontColor = FontColorOption.White;
        private static HS2ChatController _chatController;
        private static HS2ActionController _actionController;
        private static ChatUser _user;
        private static ChatUser _heroin;
        private static Font _customFont;
        private static string _customFontResourcePath;
        private static string[] _customOSFontNames;

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

            if (_initialized && (_chatCanvasGO == null || _chatRootGO == null))
            {
                _initialized = false;
            }

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
            bg.color = new Color(0.0f, 0.0f, 0.0f, 0.15f);

            CreateScrollArea(_chatRootGO.transform);

            var inputGO = new GameObject("ChatInput");
            inputGO.transform.SetParent(_chatRootGO.transform, false);
            var inputRect = inputGO.AddComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0.0f, 0.0f);
            inputRect.anchorMax = new Vector2(1.0f, 0.20f);
            inputRect.offsetMin = new Vector2(24.0f, 24.0f);
            inputRect.offsetMax = new Vector2(-24.0f, -24.0f);

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

            if (DebugAutoMessage)
            {
                AppendChat("System", "DEBUG: First message injected.");
                LogUILayout("[SUI] After CreateChatUI");
            }
        }

        // Chat UI 사용자 Prompt 수집 함수: 엔터 입력 시 호출되어 로그에 출력한다.
        private async void OnSubmitInput(string value)
        {
            UnityEngine.Debug.Log($"[SUI] OnSubmitInput value='{value}'");
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

        // Chat UI 내 Prompt 출력 함수: 채팅 로그에 한 줄을 추가하고 스크롤을 아래로 내린다.
        /*
            사용자: AppendChat("User", "안녕하세요")
            시스템: AppendChat("System", "환영합니다")
        */
        internal void AppendChat(string speaker, string message)
        {
            UnityEngine.Debug.Log($"[SUI] AppendChat speaker='{speaker}' messageLen={message?.Length ?? 0}");
            if (_chatContent == null)
            {
                UnityEngine.Debug.LogWarning("[SUI] AppendChat aborted: _chatContent is null");
                return;
            }

            bool isUser = string.Equals(speaker, "You", StringComparison.OrdinalIgnoreCase);
            var line = CreateMessageLine(_chatContent, $"{speaker}", message, isUser);
            _messageItems.Add(line.gameObject);
            TrimMessagesIfNeeded();

            if (DebugVisuals)
            {
                LogRect("[SUI] ChatRoot", _chatRootGO);
                LogRect("[SUI] ScrollRect", _chatScrollRect != null ? _chatScrollRect.gameObject : null);
                LogRect("[SUI] Viewport", _chatScrollRect != null ? _chatScrollRect.viewport?.gameObject : null);
                LogRect("[SUI] Content", _chatContent != null ? _chatContent.gameObject : null);
            }

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
            _messageItems.Clear();
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
        // 폰트 지정: 유니티 Resources에 포함된 폰트를 경로로 지정한다. (예: \"Fonts/NotoSansCJK\")
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

        // 폰트 지정: OS에 설치된 폰트를 사용한다. (예: \"Noto Sans CJK KR\")
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
        // 스크롤 영역 생성 함수: ScrollRect, Viewport, Content를 구성한다.
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
            scrollBg.color = new Color(0.2f, 0.2f, 0.2f, 0.35f);

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
            viewportImg.raycastTarget = false;
            viewportGO.AddComponent<RectMask2D>();

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
        private Text CreateMessageLine(Transform parent, string speaker, string message, bool isUser)
        {
            UnityEngine.Debug.Log($"[SUI] CreateMessageLine speaker='{speaker}' isUser={isUser} parent='{parent?.name}'");
            var lineGO = new GameObject("ChatLine");
            lineGO.transform.SetParent(parent, false);

            var rect = lineGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(1.0f, 1.0f);
            rect.pivot = new Vector2(0.5f, 1.0f);

            var row = lineGO.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(3, 3, 1, 1);
            row.spacing = 3;
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlHeight = true;
            row.childControlWidth = true;
            row.childForceExpandHeight = false;
            row.childForceExpandWidth = true;

            if (isUser)
            {
                CreateSpacer(lineGO.transform);
            }

            var bubbleGO = new GameObject("Bubble");
            bubbleGO.transform.SetParent(lineGO.transform, false);
            var bubbleRect = bubbleGO.AddComponent<RectTransform>();
            bubbleRect.pivot = new Vector2(isUser ? 1.0f : 0.0f, 1.0f);

            var bubbleImg = bubbleGO.AddComponent<Image>();
            bubbleImg.color = isUser ? new Color(0.15f, 0.45f, 0.95f, 0.65f) : new Color(0.95f, 0.35f, 0.65f, 0.60f);

            var bubbleLayout = bubbleGO.AddComponent<LayoutElement>();
            bubbleLayout.minWidth = 40.0f;
            bubbleLayout.flexibleWidth = 0.0f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(bubbleGO.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            const float BubblePaddingX = 12.0f;
            const float BubblePaddingY = 8.0f;
            textRect.anchorMin = new Vector2(0.0f, 1.0f);
            textRect.anchorMax = new Vector2(0.0f, 1.0f);
            textRect.pivot = new Vector2(0.0f, 1.0f);
            textRect.anchoredPosition = new Vector2(BubblePaddingX * 0.5f, -BubblePaddingY * 0.5f);

            var text = textGO.AddComponent<Text>();
            text.font = GetFont();
            text.fontSize = _fontSize;
            text.color = GetFontColor(isUser);
            text.alignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.text = $"<b>{speaker}</b>\n{message}";

            if (DebugVisuals)
            {
                text.fontSize = Mathf.Clamp(_fontSize + 8, 10, 60);
                text.color = Color.yellow;
                var outline = textGO.AddComponent<Outline>();
                outline.effectColor = Color.black;
                outline.effectDistance = new Vector2(1.0f, -1.0f);
            }

            var textLayout = textGO.AddComponent<LayoutElement>();
            textLayout.flexibleWidth = 0.0f;

            const float MaxBubbleWidth = 360.0f;
            var textWidth = text.preferredWidth;
            var targetBubbleWidth = Mathf.Min(MaxBubbleWidth, textWidth + (BubblePaddingX * 2.0f));
            textLayout.preferredWidth = Mathf.Max(0.0f, targetBubbleWidth - (BubblePaddingX * 2.0f));
            bubbleLayout.preferredWidth = Mathf.Max(bubbleLayout.minWidth, targetBubbleWidth);

            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textLayout.preferredWidth);
            var textHeight = text.preferredHeight;
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight);
            var bubbleHeight = textHeight + (BubblePaddingY * 2.0f);
            bubbleLayout.preferredHeight = bubbleHeight;
            bubbleLayout.minHeight = bubbleHeight;

            var lineLayout = lineGO.AddComponent<LayoutElement>();
            lineLayout.preferredHeight = bubbleHeight + row.padding.vertical;
            lineLayout.minHeight = lineLayout.preferredHeight;

            UnityEngine.Debug.Log($"[SUI] Message created line='{lineGO.name}' bubble='{bubbleGO.name}' text='{text.text}'");

            if (!isUser)
            {
                CreateSpacer(lineGO.transform);
            }

            return text;
        }

        private void LogUILayout(string tag)
        {
            if (!DebugVisuals)
                return;

            UnityEngine.Debug.Log($"{tag} UISize root={GetRectInfo(_chatRootGO)} scroll={GetRectInfo(_chatScrollRect != null ? _chatScrollRect.gameObject : null)} viewport={GetRectInfo(_chatScrollRect != null ? _chatScrollRect.viewport?.gameObject : null)} content={GetRectInfo(_chatContent != null ? _chatContent.gameObject : null)}");
        }

        private void LogRect(string label, GameObject go)
        {
            UnityEngine.Debug.Log($"{label} {GetRectInfo(go)}");
        }

        private string GetRectInfo(GameObject go)
        {
            if (go == null)
                return "null";

            var rt = go.GetComponent<RectTransform>();
            if (rt == null)
                return $"{go.name} no-RectTransform";

            return $"{go.name} pos={rt.anchoredPosition} size={rt.rect.size} anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} pivot={rt.pivot}";
        }

        private GameObject CreateSpacer(Transform parent)
        {
            var spacerGO = new GameObject("Spacer");
            spacerGO.transform.SetParent(parent, false);
            var spacerLayout = spacerGO.AddComponent<LayoutElement>();
            spacerLayout.flexibleWidth = 1.0f;
            spacerLayout.minWidth = 0.0f;
            return spacerGO;
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

        private Color GetFontColor(bool isUser)
        {
            var option = isUser ? _userFontColor : _systemFontColor;
            return option == FontColorOption.Black ? Color.black : Color.white;
        }

        // 메시지 개수 제한 함수: 초과 시 가장 오래된 항목부터 제거한다.
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
    }
}
