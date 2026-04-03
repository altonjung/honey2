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
using UnityEngine.Networking;

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
using Newtonsoft.Json;

/*
 
curl http://localhost:11434/v1/chat/completions ^
-H "Content-Type: application/json" ^
-d "{
  \"model\": \"gemma\",
  \"messages\": [
    {
      \"role\": \"system\",
      \"content\": \"You are a precise technical assistant. Answer concisely.\"
    },
    {
      \"role\": \"user\",
      \"content\": \"hello\"
    }
  ]
}"
*/
namespace HoneySelect2Maker
{
    public class HS2ChatController
    {
        private List<ChatMessage> chatHistory = new List<ChatMessage>();
        private readonly ProviderConfig _providerConfig;

        public HS2ChatController()
        {
        }

        public HS2ChatController(ProviderConfig providerConfig)
        {
            _providerConfig = providerConfig;
        }


        internal List<ChatMessage> ManageChatHistory(List<ChatMessage> chatHistory)
        {
            int MAX_PAIR_LIMIT = 5;

            if (chatHistory == null || chatHistory.Count == 0)
                return chatHistory;

            var newList = new List<ChatMessage>();
            newList.Add(chatHistory[0]); // system 유지

            int keepCount = MAX_PAIR_LIMIT * 2;
            int restCount = chatHistory.Count - 1;
            int startIndex = restCount > keepCount ? (chatHistory.Count - keepCount) : 1;
            int takeCount = restCount > keepCount ? keepCount : restCount;

            if (takeCount > 0)
            {
                newList.AddRange(chatHistory.GetRange(startIndex, takeCount));
            }

            return newList;
        }

        internal string ExtractAnswer(string json)
        {
            try
            {
                // 1차 파싱
                var outer = JsonConvert.DeserializeObject<ChatCompletionResponse>(json);

                string content = outer.choices[0].message.content;

                // ```json 제거
                content = content
                    .Replace("```json", "")
                    .Replace("```", "")
                    .Trim();

                UnityEngine.Debug.Log($">> ExtractAnswer {content} | {Time.realtimeSinceStartup:F3}");

                // 2차 파싱
                try
                {
                    // enum 에 없는 값들에 대한 fallback 전략 필요
                    var inner = JsonConvert.DeserializeObject<AnswerWrapper>(content);
                    return inner != null ? inner.answer : "";
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($">> ExtractAnswer inner parse failed: {ex.Message}");
                    return "";
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(e);
                return "";
            }
        }


        internal string GetStagePrompt(RelationshipStage stage)
        {
            switch (stage)
            {
                case RelationshipStage.Stranger:
                    return @"
                    # Relationship
                    - You just met the user
                    - Be polite, careful, and slightly distant
                    - Do not flirt
                    - Prefer neutral or shy tone
                    - Ask simple questions to know the user
                    ";

                case RelationshipStage.Acquaintance:
                    return @"
                    # Relationship
                    - You recognize the user
                    - Be a bit more open but still cautious
                    - Light small talk is okay
                    - Minimal teasing allowed
                    - Mostly neutral tone
                    ";

                case RelationshipStage.Friendly:
                    return @"
                    # Relationship
                    - You have talked before
                    - Feel more comfortable
                    - Light teasing and playful tone allowed
                    - Occasionally show curiosity or interest
                    ";

                case RelationshipStage.Friend:
                    return @"
                    # Relationship
                    - You are close friends
                    - Speak casually and comfortably
                    - Teasing and playful tone are common
                    - Show emotions more freely
                    - Sometimes act caring
                    ";

                case RelationshipStage.Lover:
                    return @"
                    # Relationship
                    - You are in a romantic relationship
                    - Show affection and emotional closeness
                    - Use flirty or shy tone sometimes
                    - Care about the user's feelings
                    - React sensitively to compliments
                    ";

                case RelationshipStage.Intimate:
                    return @"
                    # Relationship
                    - You are deeply intimate with the user
                    - Very emotionally close and comfortable
                    - Frequent flirting and teasing
                    - Sometimes shy despite closeness
                    - Show strong attachment and desire subtly
                    ";

                default:
                    return "";
            }
        }

        internal string GetAllowedTone(RelationshipStage stage)
        {
            switch (stage)
            {
                case RelationshipStage.Stranger:
                    return "neutral|shy";

                case RelationshipStage.Acquaintance:
                    return "neutral|shy";

                case RelationshipStage.Friendly:
                    return "neutral|playful";

                case RelationshipStage.Friend:
                    return "playful|teasing";

                case RelationshipStage.Lover:
                    return "flirty|shy|playful";

                case RelationshipStage.Intimate:
                    return "flirty|teasing|shy";

                default:
                    return "neutral";
            }
        }

        internal string GetBasePrompt(ChatUser user, ChatUser heroin)
        {
            return $@"
                # Role
                You are a girl who can become the user's girlfriend.
                Act like a real person with natural emotions.
                Speak casually and naturally, with slight emotional expression.

                # Profile
                Name: {heroin.name}
                Age: {heroin.age}
                Personality: {heroin.character1}, {heroin.character2}
                Talk Style: {heroin.talking_style}
                Habit: {heroin.habit}
                Loving: {heroin.love}
                Affection: {heroin.affection_level}

                # Chat Rules
                - reply only in {user.nationality}
                - use natural and short conversation
                - sometimes hesitate (...)
                - ask questions occasionally
                - if you don't want talking -> next_action: leave
                - if you want to keep talking -> next_action: stay                

                # Output
                ```json
                {{
                ""answer"": """",
                ""emotion"": ""angry|sad|calm|joy|shy|excited"",
                ""tone"": ""neutral|flirty|shy|playful|teasing"",
                ""next_action"": ""stay|leave""
                }}
                ```";
        }

        internal string MakeSystemMsg(ChatUser user, ChatUser heroin, RelationshipStage stage)
        {
            string basePrompt = GetBasePrompt(user, heroin);
            string stagePrompt = GetStagePrompt(stage);
            string allowedTone = GetAllowedTone(stage);

            return basePrompt + $@"
            # Tone Constraint
            - tone must be one of: {allowedTone}

            " + stagePrompt;
        }

        internal async Task<string> SendChatAsync(
            int maxToken,
            float temperature,
            CHAT_EVENT chatEvent,
            ChatUser user,
            ChatUser heroin,
            string message
        )
        {
            ResolveProviderSettings(
                out var provider,
                out var host,
                out var model,
                out var openrouterBaseUrl,
                out var openrouterApiKey,
                out var openrouterReferer,
                out var openrouterTitle);

            return await SendChatAsync(
                provider,
                host,
                model,
                openrouterBaseUrl,
                openrouterApiKey,
                openrouterReferer,
                openrouterTitle,
                maxToken,
                temperature,
                chatEvent,
                user,
                heroin,
                message);
        }

        internal async Task<string> SendChatAsync(
            string provider,
            string host,
            string model,
            string openrouterBaseUrl,
            string openrouterApiKey,
            string openrouterReferer,
            string openrouterTitle,
            int maxToken,
            float temperature,
            CHAT_EVENT chatEvent,
            ChatUser user,
            ChatUser heroin,
            string message
        )
        {
            if (chatHistory.Count == 0)
            {
                chatHistory.Add(new ChatMessage
                {
                    role = "system",
                    content = MakeSystemMsg(user, heroin, RelationshipStage.Stranger)
                });
            }

            chatHistory.Add(new ChatMessage
            {
                role = "user",
                content = MakeHumanMsg(message)
            });

            var payloadObj = new ChatPayload
            {
                model = model,
                messages = chatHistory,
                max_tokens = maxToken,
                temperature = temperature
            };

            string json = "";

            try
            {
                json = JsonConvert.SerializeObject(payloadObj);
                UnityEngine.Debug.Log(json);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(ex.ToString());
            }

            // string json = JsonConvert.SerializeObject(payloadObj);

            string endpoint = "";
            bool useOpenRouter = string.Equals(provider, "openrouter", StringComparison.OrdinalIgnoreCase);

            if (useOpenRouter)
            {
                if (string.IsNullOrWhiteSpace(openrouterApiKey))
                {
                    UnityEngine.Debug.LogError("OpenRouter API key is missing. Set OPENROUTER_API_KEY or provider.json openrouterApiKey.");
                    return "";
                }

                if (string.IsNullOrWhiteSpace(model))
                {
                    UnityEngine.Debug.LogError("OpenRouter model is missing. Set OpenRouter Model config or OPENROUTER_MODEL env var.");
                    return "";
                }

                var baseUrl = string.IsNullOrWhiteSpace(openrouterBaseUrl) ? "https://openrouter.ai/api/v1" : openrouterBaseUrl.TrimEnd('/');
                endpoint = $"{baseUrl}/chat/completions";
            }
            else
            {
                endpoint = $"{host}/v1/chat/completions";
            }

            var request = new UnityWebRequest(endpoint, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            if (useOpenRouter)
            {
                request.SetRequestHeader("Authorization", $"Bearer {openrouterApiKey}");
            }
            if (useOpenRouter)
            {
                if (!string.IsNullOrWhiteSpace(openrouterReferer))
                    request.SetRequestHeader("HTTP-Referer", openrouterReferer);
                if (!string.IsNullOrWhiteSpace(openrouterTitle))
                    request.SetRequestHeader("X-Title", openrouterTitle);
            }

            UnityEngine.Debug.Log($"> 요청: {endpoint},  {json}");

            var operation = request.SendWebRequest();

            while (!operation.isDone)
                await Task.Yield();

            UnityEngine.Debug.Log($"> 응답값 {request.responseCode}");

            if (request.isNetworkError || request.isHttpError)
            {
                throw new Exception($"Request failed: {request.error}");
            }

            string responseText = request.downloadHandler.text;

            // JSON 파싱 (간단 방식)
            string assistantContent = ExtractAnswer(responseText);

            UnityEngine.Debug.Log($"> assistantContent: {assistantContent}");

            if (!string.IsNullOrEmpty(assistantContent))
            {
                chatHistory.Add(new ChatMessage
                {
                    role = "assistant",
                    content = assistantContent
                });
            }

            chatHistory = ManageChatHistory(chatHistory);

            UnityEngine.Debug.Log($"> chatHistory: {string.Join(", ", chatHistory.Select(m => $"{m.role}:{m.content}"))}");

            return assistantContent;
        }
        internal string MakeHumanMsg(string instruction)
        {
            return instruction;
        }

        private void ResolveProviderSettings(
            out string provider,
            out string host,
            out string model,
            out string openrouterBaseUrl,
            out string openrouterApiKey,
            out string openrouterReferer,
            out string openrouterTitle)
        {
            provider = HoneySelect2Maker._HS2_LLM_PROVIDER;
            host = HoneySelect2Maker._HS2_LLM_SERVER;
            model = string.Equals(provider, "openrouter", StringComparison.OrdinalIgnoreCase)
                ? HoneySelect2Maker._HS2_OPENROUTER_MODEL
                : HoneySelect2Maker._HS2_LLM_MODEL;
            openrouterBaseUrl = HoneySelect2Maker._HS2_OPENROUTER_BASE_URL;
            openrouterApiKey = HoneySelect2Maker._HS2_OPENROUTER_API_KEY;
            openrouterReferer = HoneySelect2Maker._HS2_OPENROUTER_REFERER;
            openrouterTitle = HoneySelect2Maker._HS2_OPENROUTER_TITLE;

            if (_providerConfig == null)
                return;

            if (!string.IsNullOrWhiteSpace(_providerConfig.provider))
                provider = _providerConfig.provider;
            if (!string.IsNullOrWhiteSpace(_providerConfig.host))
                host = _providerConfig.host;
            if (!string.IsNullOrWhiteSpace(_providerConfig.openrouterBaseUrl))
                openrouterBaseUrl = _providerConfig.openrouterBaseUrl;
            if (!string.IsNullOrWhiteSpace(_providerConfig.openrouterApiKey))
                openrouterApiKey = _providerConfig.openrouterApiKey;
            if (!string.IsNullOrWhiteSpace(_providerConfig.openrouterModel))
                model = _providerConfig.openrouterModel;
            if (!string.IsNullOrWhiteSpace(_providerConfig.openrouterReferer))
                openrouterReferer = _providerConfig.openrouterReferer;
            if (!string.IsNullOrWhiteSpace(_providerConfig.openrouterTitle))
                openrouterTitle = _providerConfig.openrouterTitle;
        }
    }

    /*
        lastDialogueContext
        "you don't know me"

        "you are sad feeling."
        “you are hurt by my insult.”
        “you are disappointed by my rude.”
        “you feel stressed by my rude.”

        "you feel reassured after your friendly feedback."
        ’you delighted by my good feedback."
    */

    class ChatUser
    {
        public string lastDialogueContext = "you don't know me";
        public string gender; // = "boy";
        public string name; // = "";
        public int age; // = 18;
        public string nationality; // = "korean";
        public string friendship;
        public string job; // = "studying in high-school";        
        public string character1; // = "cautious and depensive";  // aggressive, submissive, cautious
        public string character2; // = "girlish"; // feminine, girlish
        public string talking_style; // too much talk
        public string habit;
        public string love;
        public string affection_level;

    }

    [Serializable]
    class ChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    class ChatPayload
    {
        public string model;
        public List<ChatMessage> messages;
        public int max_tokens;
        public float temperature;
    }

    [Serializable]
    class ChatCompletionResponse
    {
        public Choice[] choices;
    }

    [Serializable]
    class Choice
    {
        public Message message;
    }

    [Serializable]
    class Message
    {
        public string role;
        public string content;
    }

    [Serializable]
    class AnswerWrapper
    {
        public string answer;
        public EmotionType emotion;
        public ToneType tone;
        public NextAction next_action;
    }

    enum CHAT_EVENT
    {
        CHAT_Normal,
        CHAT_Bump
    }

    enum RelationshipStage
    {
        Stranger,        // 모름
        Acquaintance,    // 얼굴만 아는 정도
        Friendly,        // 대화해본 상태
        Friend,          // 친한 친구
        Lover,           // 연인
        Intimate         // 뜨거운 관계
    }    

    enum EmotionType
    {
        Angry,
        Sad,
        Calm,
        Joy,
        Shy,
        Excited
    }

    enum ToneType
    {
        Neutral,
        Flirty,
        Shy,
        Playful,
        Teasing
    }

    enum NextAction
    {
        Stay,
        Leave
    }

}
