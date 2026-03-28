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

                // 2차 파싱
                var inner = JsonConvert.DeserializeObject<AnswerWrapper>(content);

                return inner.answer;
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
                    # rule
                        - chatting must be very natural way in single {user.nationality} language.
                        - when you hard to respond, just say 'sorry' to user and leave.
                    # your role
                        - name is {heroin.name} who {heroin.age} old from {heroin.nationality}, {heroin.gender}.
                        - live is in {heroin.address}.
                        - job is {heroin.job}
                        - character is {heroin.character1} and {heroin.character2}.
                        - talking style is {heroin.talking_style}.
                    # chat_state
                        - stay | leave
                    # output format
                    ```json
                    {{
                    ""answer"": """",
                    ""emotion"": """",
                    ""emotion_score"": ""-5 to 5"",
                    ""next_chat_state"": """"
                    }}
                    ```";

            return SYS_PROMPT;
        }

        internal  string MakeBumpSystemMsg(ChatUser user, ChatUser heroin)
        {
            string SYS_PROMPT = $@"
                    # rule
                        - chatting must be very natural way in single {user.nationality} language.
                        - when you hard to respond, just say 'sorry' to user and leave.
                    # your role
                        - name is {heroin.name} who {heroin.age} old from {heroin.nationality}, {heroin.gender}.
                        - live is in {heroin.address}.
                        - job is {heroin.job}
                        - character is {heroin.character1} and {heroin.character2}.
                        - talking style is {heroin.talking_style}.
                    # chat_state
                        - stay | leave
                    # output format
                    ```json
                    {{
                    ""answer"": """",
                    ""emotion"": """",
                    ""next_chat_state"": """"
                    }}
                    ```";

            return SYS_PROMPT;
        }        

        internal async Task<string> SendChatAsync(
            string host,
            string model,
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

            var request = new UnityWebRequest($"{host}/v1/chat/completions", "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");

            UnityEngine.Debug.Log($"> 요청: {host}/v1/chat/completions,  {json}");

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
    }

    class ChatUser
    {
        public string gender; // = "boy";
        public string name; // = "";
        public int    age; // = 18;
        public string nationality; // = "korean";
        public string address; // = "Seoul";
        public string job; // = "studying in high-school";        
        public string character1; // = "cautious and depensive";  // aggressive, submissive, cautious
        public string character2; // = "girlish"; // feminine, girlish
        public string talking_style; // too much talk

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
        public string emotion;
        public string next_chat_state;
    }

    enum CHAT_EVENT{
        CHAT_Normal,
        CHAT_Bump        
    }
}
