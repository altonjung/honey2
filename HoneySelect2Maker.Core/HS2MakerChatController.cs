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

        internal string MakeNormalSystemMsg(ChatUser user, ChatUser heroin)
        {
            string SYS_PROMPT = $@"
            # Role
            You are a girl who can be my girlfriend. so you act like a girl.
            # Your profile:
            You are {heroin.name}, {heroin.age}, {heroin.gender}.
            Job: {heroin.job}.
            Personality: {heroin.character1}, {heroin.character2}.
            Relation: {heroin.friendship}.
            Talk Style: {heroin.talking_style}.
            Habit: {heroin.habit},
            Loving: {heroin.love}.
            # Chat Rules:
            - reply only in {user.nationality}
            - natural conversation
            - when user rude -> next_action: leave
            - when user gentle -> next_action: stay
            # output format
                ```json
                {{
                ""answer"": """",
                ""next_action"": ""stay|leave""
                }}
                ```";

            return SYS_PROMPT;
        }

        internal  string MakeBumpSystemMsg(ChatUser user, ChatUser heroin)
        {
            string SYS_PROMPT = $@"
            # Role
            You are a girl who can be my girlfriend. so you act like a girl.
            # Your profile:
            You are {heroin.name}, {heroin.age}, {heroin.gender}.
            Job: {heroin.job}.
            Personality: {heroin.character1}, {heroin.character2}.
            Relation: {heroin.friendship}.
            Talk Style: {heroin.talking_style}.
            Habit: {heroin.habit},
            Loving: {heroin.love}.
            # Chat Rules
            - reply only in {user.nationality}
            - natural conversation
            - when user rude -> next_action: leave
            - when user gentle -> next_action: stay
            # output format
                ```json
                {{
                ""answer"": """",
                ""next_action"": ""stay|leave""
                }}
                ```";

            return SYS_PROMPT;
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
                if (chatEvent == CHAT_EVENT.CHAT_Bump)
                {
                    chatHistory.Add(new ChatMessage
                    {
                        role = "system",
                        content = MakeBumpSystemMsg(user, heroin)
                    });
                } else
                {
                    chatHistory.Add(new ChatMessage
                    {
                        role = "system",
                        content = MakeNormalSystemMsg(user, heroin)
                    });                    
                }
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

    class ChatUser
    {
        public string gender; // = "boy";
        public string name; // = "";
        public int    age; // = 18;
        public string nationality; // = "korean";
        public string friendship;
        public string job; // = "studying in high-school";        
        public string character1; // = "cautious and depensive";  // aggressive, submissive, cautious
        public string character2; // = "girlish"; // feminine, girlish
        public string talking_style; // too much talk
        public string habit;
        public string love;

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
    public class ChatCompletionResponse
    {
        public Choice[] choices;
    }

    [Serializable]
    public class Choice
    {
        public Message message;
    }

    [Serializable]
    public class Message
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class AnswerWrapper
    {
        public string answer;
        // public string emotion;
        public string next_action;
    }

    enum CHAT_EVENT{
        CHAT_Normal,
        CHAT_Bump        
    }
}
