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
using System.Text;

#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace HoneySelect2Maker
{
    public class ActionController
    {
        internal static void LoadHeroin(string pngPath)
        {
            Manager.Game instance = Singleton<Manager.Game>.Instance;
            ChaFileControl chaFileControl = new ChaFileControl();
            chaFileControl.LoadCharaFile(pngPath, 1, false, true);
            ChaControl chaControl = Singleton<Manager.Character>.Instance.CreateChara(1, Manager.Scene.commonSpace, 0, chaFileControl);
            chaControl.ChangeNowCoordinate(false, true);
            chaControl.releaseCustomInputTexture = false;
            chaControl.Load(false);
        }

        internal static void LoadAction(string action)
        {
            if (action == "facial")
            {
                // 표정 변화
                
            } else if (action == "greet")
            {
                
            } else if (action == "like")
            {
                
            } else if (action == "hate")
            {
                
            } else if (action == "refuse")
            {
                
            } else if (action == "quit")
            {
                
            } else if (action == "dance")
            {
                
            }
        }

        internal static void LoadDialogue(string dialogue) {

            // headers = {
            //         "Authorization": "test",
            //         "Content-Type": "application/json"
            // }

            // GET
            // string json = await SendRequestAsync("http://localhost:5000/api/test");
            
            // POST
            // string body = "{\"name\":\"test\"}";
            // string result = await SendRequestAsync("http://localhost:5000/api/test", "POST", body);   
        }

        internal static void GetArchivement(string archivement)
        {
            
        }   
    }
}