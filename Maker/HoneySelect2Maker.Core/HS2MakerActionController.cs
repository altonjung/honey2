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
    public class HS2ActionController
    {
        internal void DoAction(HS2ChatUIController controller, string action)
        {
            if (action == "leave")
            {
                if (controller == null)
                    return;

                if (HoneySelect2Maker._self != null)
                {
                    HoneySelect2Maker._self.StartCoroutine(EndDialogue(controller, 3.0f));
                }                
            }
        }

        private IEnumerator EndDialogue(HS2ChatUIController controller, float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);

            if (controller != null)
                controller.DestroyChatUI();
        }        

        internal void GetArchivement(string archivement)
        {
            
        }   
    }
}
