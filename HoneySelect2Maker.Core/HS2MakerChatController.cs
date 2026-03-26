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
        internal static List<Message> ManageChatHistory(List<Message> chatHistory)
        {
            int MAX_PAIR_LIMIT = 5;

            if (chatHistory.Count > (MAX_PAIR_LIMIT * 2 + 1))
            {
                var newList = new List<Message>();
                newList.Add(chatHistory[0]); // system 유지

                newList.AddRange(chatHistory.GetRange(
                    chatHistory.Count - MAX_PAIR_LIMIT * 2,
                    MAX_PAIR_LIMIT * 2
                ));

                return newList;
            }

            return chatHistory;
        }

        internal static string ExtractContent(string json)
        {
            var key = "\"content\":\"";
            int start = json.IndexOf(key);
            if (start < 0) return "";

            start += key.Length;
            int end = json.IndexOf("\"", start);

            return json.Substring(start, end - start);
        }        

        internal static string MakeSystemMsg(ChatUser user, ChatUser heroin)
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

        internal static async Task<string> SendChatAsync(
            string host,
            string model,
            int maxToken,
            float temperature,
            List<Message> chatHistory,
            ChatUser user,
            ChatUser heroin,
            string message
        )
        {
            chatHistory = ManageChatHistory(chatHistory);

            if (chatHistory.Count == 0)
            {
                chatHistory.Add(new Message
                {
                    role = "system",
                    content = MakeSystemMsg(user, heroin)
                });
            }

            chatHistory.Add(new Message
            {
                role = "user",
                content = MakeHumanMsg(message)
            });            

            var payloadObj = new Payload
            {
                model = model,
                messages = chatHistory,
                max_tokens = maxToken,
                temperature = temperature
            };

            string json = UnityEngine.JsonUtility.ToJson(payloadObj);

            var request = new UnityWebRequest($"{host}/v1/chat/completions", "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");

            UnityEngine.Debug.Log($"> 요청: {host}/v1/chat/completions");

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
            return ExtractContent(responseText);
        }
        internal static string MakeHumanMsg(string instruction)
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
    public class Message
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class Payload
    {
        public string model;
        public List<Message> messages;
        public int max_tokens;
        public float temperature;
    }    
}
