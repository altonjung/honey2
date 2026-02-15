using Studio;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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
using ADV.Commands.Camera;
using KKAPI.Studio;
using System;
using static Studio.GuideInput;
using static RootMotion.FinalIK.IKSolver;
using IllusionUtility.GetUtility;
using ADV.Commands.Object;
using static Illusion.Utils;
using KKAPI.Studio;
using KKAPI.Studio.UI.Toolbars;
using KKAPI.Utilities;

#endif

namespace ClothPhysicsGen
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class ClothPhysicsGen : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "ClothPhysicsGen";
        public const string Version = "0.9.0.0"; //
        public const string GUID = "com.alton.illusionplugins.ClothPhysicsGen";
        internal const string _ownerId = "alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "cloth_physics_gen";
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
        internal static ClothPhysicsGen _self;

        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;
		
        private const int _uniqueId = ('C' << 24) | ('L' << 16) | ('H' << 8) | 'G';

        private Rect _windowRect = new Rect(70, 10, 600, 10);

        private static string _assemblyLocation;
        private bool _loaded = false;
        private OCIChar _selectedOCI;

        // internal enum Update_Mode
        // {
        //     SELECTION,
        //     CHANGE
        // }        

        #endregion

        #region Accessors     
        #endregion


        #region Unity Methods
        protected override void Awake()
        {

            base.Awake();

            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmony = HarmonyExtensions.CreateInstance(GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());


            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open ClothPhysicsGen window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(ClothPhysicsGen).Assembly).LoadTexture(),
                false, this, val => _ShowUI = val);
            ToolbarManager.AddLeftToolbarControl(_toolbarButton);
        }

#if SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }
#endif

        private void SceneInit()
        {
            _selectedOCI = null;            
        }

        protected override void Update()
        {
            if (_loaded == false)
                return;
        }

        protected override void OnGUI()
        {
            if (_ShowUI == false)
                return;
            
            if (StudioAPI.InsideStudio)
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "Cloth Physics Gen" + Version);
        }

        private void WindowFunc(int id)
        {
            var studio = Studio.Studio.Instance;

            // ⭐ UI 조작 중이면 Studio 입력 막기
            if (Event.current.type == EventType.MouseDown ||
                Event.current.type == EventType.MouseDrag)
            {
                studio.cameraCtrl.noCtrlCondition = () => true;
            }

            // ⭐ 마우스 떼면 해제
            if (Event.current.type == EventType.MouseUp)
            {
                studio.cameraCtrl.noCtrlCondition = null;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate")) {
            }  

            if (GUILayout.Button("Close"))
                _ShowUI = false;
            
            GUILayout.EndHorizontal();

            // ⭐ 툴팁 직접 그리기
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUI.Label(new Rect(mousePos.x + 10, mousePos.y + 10, 150, 20), GUI.tooltip, GUI.skin.box);
            }

            GUI.DragWindow();
        }

        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            UIUtility.Init();
            _loaded = true;
        }
        #endregion

        #region Patches

        public static Cloth SetupCloth(
            GameObject clothObj,
            Animator animator,
            bool isTop)
        {
            var smr = clothObj.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return null;

            // 1. Cloth 추가
            var cloth = clothObj.GetComponent<Cloth>() ?? clothObj.AddComponent<Cloth>();

            // 2. collider 연결 (이미 있다 했으니 생략 가능)
            var root = animator.GetBoneTransform(HumanBodyBones.Hips);
            cloth.capsuleColliders = root.GetComponentsInChildren<CapsuleCollider>();

            // 3. 기준 bone 선택
            Transform reference =
                isTop ?
                animator.GetBoneTransform(HumanBodyBones.Spine) :
                animator.GetBoneTransform(HumanBodyBones.Hips);

            // 4. Auto paint
            AutoPaintByDistance(cloth, reference, 0.02f, 0.4f, 0.08f);

            return cloth;
        }

        public static void AutoPaintByDistance(
            Cloth cloth,
            Transform referenceBone,   // 허리, 가슴 등 기준
            float minDistance,         // 이 거리 이하면 고정
            float maxDistance,         // 이 거리 이상이면 최대
            float maxMove)             // 끝단 maxDistance
        {
            var smr = cloth.GetComponent<SkinnedMeshRenderer>();
            var mesh = smr.sharedMesh;
            var vertices = mesh.vertices;

            var coeff = cloth.coefficients;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = smr.transform.TransformPoint(vertices[i]);
                float d = Vector3.Distance(world, referenceBone.position);

                float t = Mathf.InverseLerp(minDistance, maxDistance, d);
                t = Mathf.SmoothStep(0f, 1f, t);   // 부드럽게

                coeff[i].maxDistance = t * maxMove;
                coeff[i].collisionSphereDistance = 0f;
            }

            cloth.coefficients = coeff;
        }

        private static IEnumerator ExecuteAfterFrame(OCIChar ociChar)
        {
            int frameCount = 10;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            AddVisualColliders(ociChar);
        }

//         // 옷 부분 변경
        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                PhysicCollider value = null;
                if (_self._ociCharMgmt.TryGetValue(__instance, out value))
                {
                    ClearPhysicCollier(value);
                    _self._ociCharMgmt.Remove(__instance);
                }  
            }
        }

        
        [HarmonyPatch(typeof(ChaControl), "ChangeAccessory", typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool))]
        private static class ChaControl_ChangeAccessory_Patches
        {
            private static void Postfix(ChaControl __instance, int slotNo, int type, int id, string parentKey, bool forceChange)
            {                 
                PhysicCollider value = null;
                if (_self._ociCharMgmt.TryGetValue(__instance.GetOCIChar(), out value))
                {
                    ClearPhysicCollier(value);
                    _self._ociCharMgmt.Remove(__instance.GetOCIChar());
                }                     
            } 
        }
        

        [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        private static class ChaControl_ChangeClothes_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
            {
                UnityEngine.Debug.Log($">> ChangeClothes");
                if (kind < 2)
                {
                    PhysicCollider value = null;
                    if (_self._ociCharMgmt.TryGetValue(__instance.GetOCIChar(), out value))
                    {
                        ClearPhysicCollier(value);
                        _self._ociCharMgmt.Remove(__instance.GetOCIChar());
                    }                    
                }
            }
        }

//         // 옷 전체 변경
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {
                UnityEngine.Debug.Log($">> SetAccessoryStateAll");

                PhysicCollider value = null;
                if (_self._ociCharMgmt.TryGetValue(__instance.GetOCIChar(), out value))
                {
                    ClearPhysicCollier(value);
                    _self._ociCharMgmt.Remove(__instance.GetOCIChar());
                }
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



    class ClothData
    {
        public ObjectCtrlInfo objectCtrlInfo;

        public SkinnedMeshRenderer clothTopRender;

        public SkinnedMeshRenderer clothBottomRender;

        public ClothData()
        {
        }
    }
}
