using Studio;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Numerics;

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
using KKAPI.Studio;
using KKAPI.Studio.UI.Toolbars;
using KKAPI.Utilities;
using static KKAPI.Maker.MakerConstants;
#endif

/*
    Core logic

    1) Build undress colliders from TOP/BOTTOM cloth templates.
    2) Back up existing cloth-collider plugin colliders before applying undress colliders.
    3) Restore original colliders after undress processing.
    4) Move and scale pivot colliders to amplify the undress effect.
        - Top cloth: uses neck-based colliders and dynamic radius scaling.
        - Bottom cloth: uses spine-based colliders and dynamic radius scaling.
    Known issue
        - N/A
*/
namespace UndressPhysics
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class UndressPhysics : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "UndressPhysics";
        public const string Version = "0.9.1.2";
        public const string GUID = "com.alton.illusionplugins.UndressPhysics";
        internal const string _ownerId = "Alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "undress_physics";
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
        internal static UndressPhysics _self;
        private static string _assemblyLocation;

        private static bool _ShowUI = false;
        private static SimpleToolbarToggle _toolbarButton;
		
        private const int _uniqueId = ('U' << 24) | ('D' << 16) | ('S' << 8) | 'S';

        private Rect _windowRect = new Rect(140, 10, 340, 10);

        internal const string UNDRESS_COLLIDER_PREFIX = "UndressPhyics_Collider";
        
        internal static List<SphereColliderPair> _sphereColliders = new List < SphereColliderPair >();
        internal static List<CapsuleColliderData> _capsuleColliders = new List<CapsuleColliderData>();

        internal static readonly Dictionary<Cloth, ClothColliderBackup> _clothColliderBackup
                        = new Dictionary<Cloth, ClothColliderBackup>();

        private bool _loaded = false;

        private bool _quickStop = false;
        private int _undressAttemptSeq = 0;
        private Status _status = Status.IDLE;
        internal List<UndressData> _undressDataList = new List<UndressData>();

        private AnimationCurve PullCurve;

        private AnimationCurve UndressCurve;

        private GUIStyle _richLabel;

        private GUIStyle RichLabel
        {
            get
            {
                if (_richLabel == null)
                {
                    _richLabel = new GUIStyle(GUI.skin.label);
                    _richLabel.richText = true;
                }
                return _richLabel;
            }
        }

        internal static ConfigEntry<float> ClothUndressForce { get; private set; }
        internal static ConfigEntry<float> ClothUndressDuration { get; private set; }
        internal static ConfigEntry<float> ClothStiffness { get; private set; }   
        internal static ConfigEntry<float> ClothDamping { get; private set; }   

        internal enum Status
        {
            RUN,
            DESTORY,
            IDLE
        }

        #endregion

        #region Accessors
        #endregion

        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            ClothStiffness = Config.Bind("Cloth", "Stiffness", 0.3f, new ConfigDescription("", new AcceptableValueRange<float>(0.1f, 1.0f)));

            ClothDamping = Config.Bind("Cloth", "Damping", 0.5f, new ConfigDescription("", new AcceptableValueRange<float>(0.1f, 1.0f)));

            ClothUndressForce = Config.Bind("Option", "Force", 10.0f, new ConfigDescription("multiple", new AcceptableValueRange<float>(5f, 20f)));

            ClothUndressDuration = Config.Bind("Option", "Duration", 15.0f, new ConfigDescription("undress duration", new AcceptableValueRange<float>(0.0f, 60.0f)));

            UndressCurve = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 2.5f),
                new Keyframe(0.25f, 0.45f, 1.2f, 0.6f),
                new Keyframe(1f, 1f, 0.2f, 0f)
            );

            PullCurve = new AnimationCurve(
                new Keyframe(0.25f, 0.35f, 1.2f, 0.8f),
                new Keyframe(0.7f, 0.8f, 0.8f, 0.4f),
                new Keyframe(1f, 1f, 0.2f, 0f)
            );

            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;

            var harmony = HarmonyExtensions.CreateInstance(GUID);

            harmony.PatchAll(Assembly.GetExecutingAssembly());

            _toolbarButton = new SimpleToolbarToggle(
                "Open window",
                "Open UndressPhysics window",
                () => ResourceUtils.GetEmbeddedResource("toolbar_icon.png", typeof(UndressPhysics).Assembly).LoadTexture(),
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

        protected override void Update()
        {
            if (_loaded == false)
                return;
        }

        private void SceneInit()
        {
            Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
			_ShowUI = false;     
        }

        protected override void OnGUI()
        {
            if (_ShowUI == false)
                return;            
#if FEATURE_PUBLIC 
            if (StudioAPI.InsideStudio)
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "Undress Physics(P) " + Version);
#else
            if (StudioAPI.InsideStudio)
                this._windowRect = GUILayout.Window(_uniqueId + 1, this._windowRect, this.WindowFunc, "Undress Physics " + Version);
#endif                
        }

        private void WindowFunc(int id)
        {
            var studio = Studio.Studio.Instance;
  
            // Always restore the default camera-control condition.
            studio.cameraCtrl.noCtrlCondition = null;
			
            bool guiUsingMouse = GUIUtility.hotControl != 0;
            bool mouseInWindow = _windowRect.Contains(Event.current.mousePosition);

            if (guiUsingMouse || mouseInWindow)
            {
                studio.cameraCtrl.noCtrlCondition = () => true;
            }
            else
            {
                studio.cameraCtrl.noCtrlCondition = null;
            }

            // ================= UI =================
// Global
            GUILayout.Label("<color=orange>Option</color>", RichLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("silk"))
                ApplyClothPreset("silk");
            if (GUILayout.Button("wool"))
                ApplyClothPreset("wool");
            if (GUILayout.Button("denim"))
                ApplyClothPreset("denim");
            if (GUILayout.Button("span"))
                ApplyClothPreset("span");                
            GUILayout.EndHorizontal();

            GUILayout.Label("<color=orange>Global</color>", RichLabel);            
            // Duration
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Duration", "Undress Duration"), GUILayout.Width(80));
            ClothUndressDuration.Value = GUILayout.HorizontalSlider(ClothUndressDuration.Value, 0.0f, 60.0f);
            GUILayout.Label(ClothUndressDuration.Value.ToString("0.00"), GUILayout.Width(40));
            GUILayout.EndHorizontal();

#if !FEATURE_PUBLIC
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Force", "PullDown Force"), GUILayout.Width(80));
            ClothUndressForce.Value = GUILayout.HorizontalSlider(ClothUndressForce.Value, 5f, 20f);
            GUILayout.Label(ClothUndressForce.Value.ToString("0.00"), GUILayout.Width(40));
            GUILayout.EndHorizontal();
#endif

// Cloth
#if !FEATURE_PUBLIC
            GUILayout.Label("<color=orange>Cloth</color>", RichLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Stiffness", "Stiffness"), GUILayout.Width(80));
            ClothStiffness.Value = GUILayout.HorizontalSlider(ClothStiffness.Value, 0.1f, 1.0f);
            GUILayout.Label(ClothStiffness.Value.ToString("0.00"), GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Damping", "Damping"), GUILayout.Width(80));
            ClothDamping.Value = GUILayout.HorizontalSlider(ClothDamping.Value, 0.1f, 1.0f);
            GUILayout.Label(ClothDamping.Value.ToString("0.00"), GUILayout.Width(40));
            GUILayout.EndHorizontal();            
#endif
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Undress"))
                DoUndressAll();
           
            if(GUILayout.Button("Stop Undress"))
            {
                _quickStop = true;
            }

            if(GUILayout.Button("Default"))
            {
                InitConfig();   
            }

            if (GUILayout.Button("Close")) {
                 Studio.Studio.Instance.cameraCtrl.noCtrlCondition = null;
				_ShowUI = false;
			}
            GUILayout.EndHorizontal();

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

        private void InitConfig()
        {
            ClothStiffness.Value = (float)ClothStiffness.DefaultValue;
            ClothDamping.Value = (float)ClothDamping.DefaultValue;            
            ClothUndressForce.Value = (float)ClothUndressForce.DefaultValue;
            ClothUndressDuration.Value = (float)ClothUndressDuration.DefaultValue;
        }

        private void ApplyClothPreset(string preset)
        {
            switch (preset)
            {
                case "silk": // Silk: soft and flowing
                    ClothStiffness.Value = 0.10f;
                    ClothDamping.Value   = 0.10f;
                    break;

                case "wool": // Wool: heavy and dull
                    ClothStiffness.Value = 0.50f;
                    ClothDamping.Value   = 0.50f;
                    break;

                case "denim": // Denim: stiff but not fully rigid
                    ClothStiffness.Value = 0.75f;
                    ClothDamping.Value   = 0.80f;
                    break;

                case "span": // Spandex: elasticity + recovery
                    ClothStiffness.Value = 0.55f;
                    ClothDamping.Value   = 1.00f;
                    break;
            }

            ClothStiffness.Value = Mathf.Clamp(ClothStiffness.Value, 0.1f, 1.0f);
            ClothDamping.Value = Mathf.Clamp(ClothDamping.Value, 0.1f, 1.0f);
        }

        private IEnumerator UndressPartCoroutine(
           UndressData undressData,
           Cloth cloth)
        {
            if (cloth == null)
                yield break;

            cloth.useGravity = true;
            cloth.damping = ClothDamping.Value;
            cloth.stiffnessFrequency = ClothStiffness.Value;
            cloth.worldAccelerationScale = 1.0f;
            cloth.worldVelocityScale = 0.0f;

            if (cloth != null)
            {
                var coeffs = cloth.coefficients;
                int vertCount = coeffs.Length;

                const float MaxDistanceSentinelThreshold = 100000f;
                const float NormalizedStartDistance = 0f;

                float[] startDistances = new float[vertCount];
                for (int i = 0; i < vertCount; i++)
                {
                    float raw = coeffs[i].maxDistance;

                    // Some outfits use float.MaxValue-like sentinel distances.
                    // Normalize them so Lerp can produce meaningful pull-down values.
                    if (raw >= MaxDistanceSentinelThreshold)
                        raw = NormalizedStartDistance;

                    startDistances[i] = raw;
                }


                SkinnedMeshRenderer smr = cloth.GetComponent<SkinnedMeshRenderer>();

                if (smr != null)
                {
                    Vector3[] vertices = smr.sharedMesh.vertices;

                    // 🔹 아래로 당기는 힘 설정
                    float startPull = 0.0f;    // 시작은 거의 없음
                    float endPull   = 5.0f;    // 끝날 때 강한 하강력 (튜닝 포인트)

                    // y좌표 기반 정규화
                    float minY = float.MaxValue;
                    float maxY = float.MinValue;
                    foreach (var v in vertices)
                    {
                        float y = smr.transform.TransformPoint(v).y;
                        minY = Mathf.Min(minY, y);
                        maxY = Mathf.Max(maxY, y);
                    }
                    float rangeY = maxY - minY;

                    float[] normalizedYs = new float[vertices.Length];
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        float y = smr.transform.TransformPoint(vertices[i]).y;
                        normalizedYs[i] = (y - minY) / rangeY;
                    }

                    float timer = 0f;
                    float topMaxDistance = 3.0f * ClothUndressForce.Value;
                    float midMaxDistance = 5.0f * ClothUndressForce.Value;
                    float bottomMaxDistance = 3.0f * ClothUndressForce.Value;

                    float startRadius = undressData.IsTop ? 0.5f : 1.0f; // Initial collider size for push-down.
                    float endRadius = undressData.IsTop ? 1.6f : 2.4f; // Final collider size for push-down.
                    var pivotCollider = undressData.collider;
                    Vector3 startColliderCenter = pivotCollider != null ? pivotCollider.center : Vector3.zero;
                    float endCenterYOffset = undressData.IsTop ? -0.25f : -1.00f;

                    float duration = ClothUndressDuration.Value;
                    while (timer < duration && !_quickStop)
                    {
                        if (cloth == null)
                            break;

                        topMaxDistance = 3.0f * ClothUndressForce.Value;
                        midMaxDistance = 5.0f * ClothUndressForce.Value;
                        bottomMaxDistance = 3.0f * ClothUndressForce.Value;

                        float t = timer / duration;
                        float tSmooth = Mathf.SmoothStep(0f, 1f, t);

                        if (pivotCollider != null) {
                            pivotCollider.radius = Mathf.Lerp(startRadius, endRadius, t);
                            pivotCollider.height = pivotCollider.radius * endRadius;
                            float yOffset = Mathf.Lerp(0f, endCenterYOffset, t);
                            pivotCollider.center = startColliderCenter + new Vector3(0f, yOffset, 0f);
                        }

                        float pullForce = Mathf.Lerp(startPull, endPull, tSmooth);
                        if (cloth != null)
                            cloth.externalAcceleration = Vector3.down * pullForce;

                        float topScale = Mathf.Lerp(1f, 1.5f, tSmooth);
                        float midScale = Mathf.Lerp(1f, 2.0f, tSmooth);
                        float bottomScale = Mathf.Lerp(1f, 2.5f, tSmooth);

                        for (int i = 0; i < coeffs.Length; i++)
                        {
                            var c = coeffs[i];

                            float targetMaxDistance;
                            if (normalizedYs[i] > 0.80f)
                                targetMaxDistance = Mathf.Lerp(startDistances[i], topMaxDistance * topScale, tSmooth);
                            else if (normalizedYs[i] > 0.40f)
                                targetMaxDistance = Mathf.Lerp(startDistances[i], midMaxDistance * midScale, tSmooth);
                            else
                                targetMaxDistance = Mathf.Lerp(startDistances[i], bottomMaxDistance * bottomScale, tSmooth);

                            c.maxDistance = targetMaxDistance;
                            coeffs[i] = c;
                        }

                        timer += Time.deltaTime;
                        if (cloth != null)
                            cloth.coefficients = coeffs;

                        yield return null;     
                    }
                }
            }       
        }

        private IEnumerator DoUnressCoroutine(UndressData undressData, Cloth cloth)
        {
            if (cloth != null)
            {
                while (!_loaded)
                    yield return null;

                // Warm up the cloth solver once so the first undress attempt
                // starts from a consistent runtime state.
                yield return StartCoroutine(WarmupClothSolver(cloth));
                // UndressPhysicsUtils.RestoreMaxDistances(undressData);       

                int sphereCount = cloth.sphereColliders != null ? cloth.sphereColliders.Length : 0;
                int capsuleCount = cloth.capsuleColliders != null ? cloth.capsuleColliders.Length : 0;
                bool hasPivotCapsule = false;
                if (undressData.collider != null && cloth.capsuleColliders != null)
                {
                    foreach (var c in cloth.capsuleColliders)
                    {
                        if (ReferenceEquals(c, undressData.collider))
                        {
                            hasPivotCapsule = true;
                            break;
                        }
                    }
                }

                // Back up cloth coefficients.
                ClothSkinningCoefficient[] coeffs = cloth.coefficients;
                float[] maxDistances = new float[coeffs.Length];
                for (int i = 0; i < coeffs.Length; i++)
                {
                    maxDistances[i] = coeffs[i].maxDistance;
                }
                undressData.originalMaxDistances[cloth] = maxDistances;

                yield return StartCoroutine(UndressPartCoroutine(undressData, cloth));         
            }

            // Restore default spine collider.
            if (undressData.collider) {

                float resetRadius = 0.6f;
                if (undressData.IsTop)
                {
                    resetRadius = 0.3f;
                }
                undressData.collider.radius = resetRadius;
                undressData.collider.height = resetRadius * 2.0f;
            }

            undressData.coroutine = null;
            UndressPhysicsUtils.RestoreMaxDistances(undressData);

            int endCoroutineCnt = 0;
            // Count completed coroutines.
            foreach (UndressData item in _undressDataList)
            {
                if (item.coroutine == null)
                    endCoroutineCnt++;
            }

            if (endCoroutineCnt == _undressDataList.Count)
            {
                Logger.LogMessage("undress done");
                EndUndress(undressData.ociChar.GetChaControl());
            }
        }

        private IEnumerator WarmupClothSolver(Cloth cloth)
        {
            if (cloth == null)
                yield break;

            // One physics step to settle solver state before undress starts.
            // yield return new WaitForFixedUpdate();

            // Rebind coefficients while disabled so Unity's native cloth solver
            // starts in the same state as the post-restore path.
            // var coeffs = cloth.coefficients;

            cloth.enabled = false;
            cloth.enabled = true;
            cloth.ClearTransformMotion();
            // cloth.coefficients = coeffs;

            cloth.externalAcceleration = Vector3.zero;

            // One physics step to settle solver state before undress starts.
            yield return new WaitForFixedUpdate();
        }

        private IEnumerator StartUndressDelayed(UndressData data)
        {
            yield return new WaitForFixedUpdate();

            data.coroutine = StartCoroutine(DoUnressCoroutine(data, data.cloth));
        }

        private void DoUndressAll(){
            int endCoroutineCnt = 0;
            
            _quickStop = false;
            
            // Count completed coroutines.
            foreach (UndressData undressData in _undressDataList)
            {
                if (undressData.coroutine == null)
                    endCoroutineCnt++;
            }

            if (endCoroutineCnt != _undressDataList.Count)
            {
                return;
            }

            if (Studio.Studio.Instance.treeNodeCtrl.selectNodes.Length != 0)
            {
                var nodes = Studio.Studio.Instance.treeNodeCtrl.selectNodes;

                TreeNodeObject lastNode = nodes[nodes.Length - 1];

                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(lastNode);

                OCIChar ociChar = objectCtrlInfo as OCIChar;

                if (ociChar != null)
                {   
                    ChaControl chaCtrl = ociChar.GetChaControl();   
                    
                    var clothTop = chaCtrl.objClothes[0];
                    var clothBottom = chaCtrl.objClothes[1];

                    if (clothTop != null) {
                        Cloth[] clothes = clothTop.GetComponentsInChildren<Cloth>(true);
                        foreach (Cloth c in clothes)
                        {
                            var smr = c.GetComponent<SkinnedMeshRenderer>();
                            string meshName = smr != null && smr.sharedMesh != null ? smr.sharedMesh.name : "null";
                        }
                        if (clothes.Length > 0) {
                            UndressPhysicsUtils.AllocateClothColliders(chaCtrl, UndressPhysicsUtils.topManifestXml, "top", "999999990", clothes, true);
                            UndressPhysicsUtils.AllocateClothColliders(chaCtrl, UndressPhysicsUtils.bottomManifestXml, "bottom", "8888888880", clothes, false);
                            AllocateUndressData(chaCtrl, clothes[0], true);                            
                        }
                    }

                    if (clothBottom != null) {
                        Cloth[] clothes = clothBottom.GetComponentsInChildren<Cloth>(true);
                        foreach (Cloth c in clothes)
                        {
                            var smr = c.GetComponent<SkinnedMeshRenderer>();
                            string meshName = smr != null && smr.sharedMesh != null ? smr.sharedMesh.name : "null";
                        }
                        if (clothes.Length > 0) {
                            UndressPhysicsUtils.AllocateClothColliders(chaCtrl, UndressPhysicsUtils.bottomManifestXml, "bottom", "8888888880", clothes, false);                         
                            AllocateUndressData(chaCtrl, clothes[0], false);                            
                        }
                    }

                    if (_undressDataList.Count == 0)
                    {
                        Logger.LogMessage("No physics cloths found");
                    } else
                    {
                        // undress cloth collider auto assignment
                        foreach (UndressData undressData in _undressDataList)
                        {
                            if (undressData != null)
                            {
                                if (undressData.coroutine == null) {
                                    undressData.coroutine = StartCoroutine(StartUndressDelayed(undressData));
                                }
                            }
                        }
                    }
                }   
            }         
        }

        private void EndUndress(ChaControl chaCtrl)
        {
            // Reset state.
            foreach(UndressData undressData in _undressDataList)
            {
                if (undressData.coroutine != null) {
                    StopCoroutine(undressData.coroutine);
                }

                UndressPhysicsUtils.RestoreClothColliders(undressData.cloth); // back to origin clothes
            }

            _undressDataList.Clear();
            _sphereColliders.Clear();
            _capsuleColliders.Clear();

            var transforms = chaCtrl.objBodyBone.GetComponentsInChildren<Transform>(true);

            foreach (var tr in transforms)
            {
                if (tr.name.StartsWith(UndressPhysics.UNDRESS_COLLIDER_PREFIX))
                {
                    GameObject.Destroy(tr.gameObject); // Remove entire GameObject.
                }
            }
        }

        private void AllocateUndressData(ChaControl chaCtrl, Cloth cloth, bool isTop)
        {   
            if (chaCtrl != null)
            {
                UndressData undressData = UndressPhysicsUtils.CreateUndressData(cloth, chaCtrl, isTop);
                _undressDataList.Add(undressData);        
                // foreach(Cloth cloth in clothes)
                // {
                //     UndressData undressData = UndressPhysicsUtils.CreateUndressData(cloth, chaCtrl, isTop);
                //     _undressDataList.Add(undressData);
                // }
            }
        }

        private static string GetTransformPath(Transform tr)
        {
            if (tr == null) return "null";
            var stack = new Stack<string>();
            Transform cur = tr;
            while (cur != null)
            {
                stack.Push(cur.name);
                cur = cur.parent;
            }
            return string.Join("/", stack.ToArray());
        }

        #endregion

        #region Patches

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.SceneInit();
                UndressPhysicsUtils.ClearBackup();
                return true;
            }
        }

        #endregion
    }
}
