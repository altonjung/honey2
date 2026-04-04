using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UILib.ContextMenu;
using UILib.EventHandlers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Diagnostics;
using UniRx;
using UniRx.Triggers;

#if IPA
using Harmony;
using IllusionPlugin;
#elif BEPINEX
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
#endif
#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using KKAPI.Studio;
using IllusionUtility.GetUtility;
#endif

// 추가 작업 예정
// - direction 자동 360도 회전

namespace FKIK_Animation
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class FKIK_Animation : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "FKIK_Animation";
        public const string Version = "0.9.0.0";
        public const string GUID = "com.alton.illusionplugins.fkikanim";
        internal const string _ownerId = "alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "FKIK_Animation";
#endif
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types
        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static FKIK_Animation _self;
        internal static ConfigEntry<bool> ConfigKeyEnable { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyEnableShortcut { get; private set; }

        private static string _assemblyLocation;
        private bool _loaded = false;

        private ObjectCtrlInfo _selectedOCI;
        
        private Coroutine _AnimationPlayRoutine;

        #endregion

        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            // option 
            ConfigKeyEnable = Config.Bind("Options", "Animation", false, "enabled/disabled");

            ConfigKeyEnableShortcut = Config.Bind("ShortKey", "Toggle animation key", new KeyboardShortcut(KeyCode.A));

            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmony = HarmonyExtensions.CreateInstance(GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());                     
        }

#if SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }
#endif

        protected override void Update()
        {
            if (_loaded == false)
                return;

            if (ConfigKeyEnableShortcut.Value.IsDown())
            {   
                if (ConfigKeyEnable.Value)
                {
                    if (_AnimationPlayRoutine != null)
                        StopCoroutine(_AnimationPlayRoutine);
                    
                    _AnimationPlayRoutine = StartCoroutine(AnimationPlayRoutine());
                } else
                {
                    if (_AnimationPlayRoutine != null)
                    {
                        StopCoroutine(_AnimationPlayRoutine);
                        _AnimationPlayRoutine = null;
                    }
                }
                ConfigKeyEnable.Value = !ConfigKeyEnable.Value;
            }
        }

        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            // UIUtility.Init();
            _loaded = true;
        }

        private void SceneInit()
        {
            if (_AnimationPlayRoutine != null)
                StopCoroutine(_AnimationPlayRoutine);
            
            _AnimationPlayRoutine = null;
            _selectedOCI = null;
        }

        private void DoNothing() {
        }

        private void DoFKIK() {
            OCIChar character = _selectedOCI as OCIChar;
            SetCopyBoneFK(character, (OIBoneInfo.BoneGroup)353);
            SetCopyBoneIK(character, (OIBoneInfo.BoneGroup)31);
        }

        private void DoFK() {        
            OCIChar character = _selectedOCI as OCIChar;
            SetCopyBoneFK(character, (OIBoneInfo.BoneGroup)353);
        }

        private void DoIK() {                    
            OCIChar character = _selectedOCI as OCIChar;
            SetCopyBoneIK(character, (OIBoneInfo.BoneGroup)31);
        }

        private void SetCopyBoneFK(OCIChar ociChar, OIBoneInfo.BoneGroup _group)
		{         
			SingleAssignmentDisposable _disposableFK = new SingleAssignmentDisposable();
			_disposableFK.Disposable = this.LateUpdateAsObservable().Take(1).Subscribe(delegate(Unit _)
			{
				ociChar.fkCtrl.CopyBone(_group);
			}, delegate()
			{
				_disposableFK.Dispose();
			});
		}

		private void SetCopyBoneIK(OCIChar ociChar, OIBoneInfo.BoneGroup _group)
		{
			SingleAssignmentDisposable _disposableIK = new SingleAssignmentDisposable();
			_disposableIK.Disposable = this.LateUpdateAsObservable().Take(1).Subscribe(delegate(Unit _)
			{
                ociChar.ikCtrl.CopyBone(_group);
			}, delegate()
			{
				_disposableIK.Dispose();         
			});
		}


        // n 개 대상 아이템에 대해 active/inactive 동시 적용 처리 
        private IEnumerator AnimationPlayRoutine()
        {
            OCIChar character = _selectedOCI as OCIChar;
            if (character == null)
                yield break;

            Action reflectAnimationToFKIK = DoNothing;

            if (character.oiCharInfo.enableFK && character.oiCharInfo.enableIK) {
                reflectAnimationToFKIK = DoFKIK;
            } else if (character.oiCharInfo.enableFK && !character.oiCharInfo.enableIK) {
                reflectAnimationToFKIK = DoFK;
            } else if (!character.oiCharInfo.enableFK && character.oiCharInfo.enableIK) {
                reflectAnimationToFKIK = DoIK;
            }

            if (reflectAnimationToFKIK == DoNothing) {
                yield break;
            }
            
            while (true)
            {
                if (_loaded == true)
                {
                    reflectAnimationToFKIK();
                    yield return new WaitForEndOfFrame();
                }

                // yield return new WaitForSeconds(1.0f); // 1.0초 대기
            }
        }
        #endregion

        #region Patches
        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objCtrlInfo = Studio.Studio.GetCtrlInfo(_node);

                _self._selectedOCI = objCtrlInfo;
             
                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                _self._selectedOCI = null;

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeleteNode), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeleteNode_Patches
        {
            internal static class WorkspaceCtrl_OnSelectSingle_Patches
            {
                private static bool Prefix(object __instance, TreeNodeObject _node)
                {
                    ObjectCtrlInfo objCtrlInfo = Studio.Studio.GetCtrlInfo(_node);

                    _self._selectedOCI = objCtrlInfo;
                
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                _self._selectedOCI = __instance;
            }
        }

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.SceneInit();
                return true;
            }
        }

        #endregion
    }   
}