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

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace HoneySelect2Maker
{
    class HeroinData
    {
        public string fullname = "";
	    public int birthMonth = 1;
		public int birthDay = 1;
        public int personality = 0;                		
		public float voiceRate = 0.5f;
		public int trait = 0;
		public int mind = 0;
		public int hAttribute = 0;
        public int age;
        public int encounterCnt;
        public int relationship;
    }
}
