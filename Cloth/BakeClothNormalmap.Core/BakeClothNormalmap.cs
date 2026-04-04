using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UILib.ContextMenu;
using UILib.EventHandlers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KK_PregnancyPlus;
using System.Threading.Tasks;
using System.Drawing;
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
using IllusionUtility.GetUtility;
using ADV.Commands.Object;
#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif


namespace BakeClothNormalmap
{

#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class BakeClothNormalmap : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "BakeClothNormalmap";
        public const string Version = "0.9.0";
        public const string GUID = "com.alton.illusionplugins.bakenormalmap";
        internal const string _ownerId = "BakeClothNormalmap";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "BakeClothNormalmap";
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
        internal static BakeClothNormalmap _self;

        private static string _assemblyLocation;
        private bool _loaded = false;

        private OCIChar _selectedOciChar;
        // end
        private ComputeShader _mergeShader;

        internal static ConfigEntry<KeyboardShortcut> ConfigShortcut { get; private set; }

        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            _self = this;

            Logger = base.Logger;
            
            ConfigShortcut = Config.Bind("ShortKey", "Toggle effect key", new KeyboardShortcut(KeyCode.B, KeyCode.LeftControl, KeyCode.LeftShift));

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
        }

#if HONEYSELECT
        protected override void LevelLoaded(int level)
        {
            if (level == 3)
                this.Init();
        }
#elif SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }

#elif KOIKATSU
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 1)
                Init();
        }
#endif

        protected override void Update()
        {
            if (_loaded == false)
                return;

            if (Input.anyKeyDown)
            {
                if (ConfigShortcut.Value.IsDown() && _selectedOciChar != null)
                {
                    RealHumanData realHumanData = CreateRealData(_selectedOciChar);
                    _selectedOciChar.charInfo.StartCoroutine(ExecuteAfterFrame(realHumanData));
                }
            }
        }
        #endregion

        #region Private Methods
        private void Init()
        {
            UnityEngine.Debug.Log($">> Init");

            UIUtility.Init();
            _loaded = true;

            string pluginDir = Path.GetDirectoryName(Info.Location);

            string bundlePath = Application.dataPath + "/../abdata/bake/realgirlbundle.unity3d";

            // 번들 파일 경로            
            if (!File.Exists(bundlePath))
            {
                UnityEngine.Debug.Log($">> AssetBundle not found at: {bundlePath}");
                return;
            }

            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                UnityEngine.Debug.Log(">> Failed to load AssetBundle!");
                return;
            }

            _mergeShader = bundle.LoadAsset<ComputeShader>("MergeBump");         

        }


        private void SceneInit()
        {
        }

        #endregion

        #region Public Methods
        #endregion

        #region Patches


        private static Texture2D MakeReadableTexture(Texture2D texture)
        {
            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 24, RenderTextureFormat.ARGB32);

            RenderTexture cachedActive = RenderTexture.active;
            RenderTexture.active = rt;
            Graphics.Blit(texture, rt);

            Texture2D tex = new Texture2D(texture.width, texture.height, TextureFormat.ARGB32, true);
            tex.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
            tex.Apply();

            RenderTexture.active = cachedActive;
            RenderTexture.ReleaseTemporary(rt);

            return tex;
        }


        private static Texture2D EnsureSize(Texture2D source, int width, int height)
        {
            int targetWidth = width;
            int targetHeight = height;

            if (source.width == targetWidth && source.height == targetHeight)
            {
                // 이미 맞는 사이즈
                return source;
            }

            // RenderTexture를 이용한 다운사이징
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            resized.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return resized;
        }


        private static void SaveAsPNG(Texture2D tex, string path)
        {
            if (tex == null)
            {
                Debug.LogError("Texture is null, cannot save.");
                return;
            }

            byte[] bytes = tex.EncodeToPNG();
            try
            {
                File.WriteAllBytes(path, bytes);
                Debug.Log($"Texture saved to: {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to save texture: {e}");
            }
        }

        private static Texture2D CreateRegionMask(Mesh mesh, List<int> backIndices, int textureWidth, int textureHeight)
        {
            Color[] clearColors = new Color[textureWidth * textureHeight]; // 모두 (0,0,0,0)
            Texture2D mask = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
            mask.SetPixels(clearColors);
            mask.Apply(); // GPU 반영

            Vector2[] uv = mesh.uv;
            int[] triangles = mesh.triangles;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int v0 = triangles[i];
                int v1 = triangles[i + 1];
                int v2 = triangles[i + 2];

                if (backIndices.Contains(v0) && backIndices.Contains(v1) && backIndices.Contains(v2))
                {
                    FillTriangleBasic(mask, uv[v0], uv[v1], uv[v2], textureWidth, textureHeight);
                }
            }

            mask.Apply();
            return mask;
        }

        private static void FillTriangleBasic(Texture2D tex, Vector2 uv0, Vector2 uv1, Vector2 uv2, int texWidth, int texHeight)
        {
            int x0 = Mathf.RoundToInt(uv0.x * (texWidth - 1));
            int y0 = Mathf.RoundToInt(uv0.y * (texHeight - 1));
            int x1 = Mathf.RoundToInt(uv1.x * (texWidth - 1));
            int y1 = Mathf.RoundToInt(uv1.y * (texHeight - 1));
            int x2 = Mathf.RoundToInt(uv2.x * (texWidth - 1));
            int y2 = Mathf.RoundToInt(uv2.y * (texHeight - 1));

            int minX = Mathf.Max(0, Mathf.Min(x0, Mathf.Min(x1, x2)));
            int maxX = Mathf.Min(texWidth - 1, Mathf.Max(x0, Mathf.Max(x1, x2)));
            int minY = Mathf.Max(0, Mathf.Min(y0, Mathf.Min(y1, y2)));
            int maxY = Mathf.Min(texHeight - 1, Mathf.Max(y0, Mathf.Max(y1, y2)));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (IsPointInTriangleBasic(x, y, x0, y0, x1, y1, x2, y2))
                        tex.SetPixel(x, y, Color.white);
                }
            }
        }

        private static bool IsPointInTriangleBasic(float px, float py,
            float x0, float y0, float x1, float y1, float x2, float y2)
        {
            float dX = px - x2;
            float dY = py - y2;
            float dX21 = x2 - x1;
            float dY12 = y1 - y2;
            float D = dY12 * (x0 - x2) + dX21 * (y0 - y2);
            float s = dY12 * dX + dX21 * dY;
            float t = (y2 - y0) * dX + (x0 - x2) * dY;

            if (D < 0) return (s <= 0) && (t <= 0) && (s + t >= D);
            return (s >= 0) && (t >= 0) && (s + t <= D);
        }

        private static List<int> ExtractBackTriangles(Mesh mesh, Transform meshTransform)
        {
            var tris = mesh.triangles;
            var verts = mesh.vertices;

            List<int> backTris = new List<int>();

            for (int i = 0; i < tris.Length; i += 3)
            {
                int t0 = tris[i];
                int t1 = tris[i + 1];
                int t2 = tris[i + 2];

                Vector3 v0 = meshTransform.TransformPoint(verts[t0]);
                Vector3 v1 = meshTransform.TransformPoint(verts[t1]);
                Vector3 v2 = meshTransform.TransformPoint(verts[t2]);

                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                // 뒤쪽 region 기준 (필요하면 axis 바꿀 수 있음)
                if (normal.z < 0f)
                {
                    backTris.Add(t0);
                    backTris.Add(t1);
                    backTris.Add(t2);
                }
            }

            return backTris;
        }

        private static List<TriangleData> CreateTriangleDataList(
            Mesh mesh,
            Transform meshTransform,
            List<int> backsideTriangles,
            int textureWidth,
            int textureHeight)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;

            List<TriangleData> triangleList = new List<TriangleData>(backsideTriangles.Count / 3);

            for (int i = 0; i < backsideTriangles.Count; i += 3)
            {
                int i0 = backsideTriangles[i];
                int i1 = backsideTriangles[i + 1];
                int i2 = backsideTriangles[i + 2];

                Vector3 v0 = vertices[i0];
                Vector3 v1 = vertices[i1];
                Vector3 v2 = vertices[i2];

                Vector2 uv0 = uvs[i0];
                Vector2 uv1 = uvs[i1];
                Vector2 uv2 = uvs[i2];

                // ─────────────────────────────────────────────
                // ① Degenerate triangle (면적 0) 제거
                // Cross(e1, e2).sqrMagnitude == 0 → 삼각형 아님
                // ─────────────────────────────────────────────
                Vector3 e1 = v1 - v0;
                Vector3 e2 = v2 - v0;
                if (Vector3.Cross(e1, e2).sqrMagnitude < 1e-12f)
                {
                    // Debug.LogWarning($"Degenerate triangle skipped: {i0},{i1},{i2}");
                    continue;
                }

                // pixel 기준 UV AABB 계산
                float minUx = Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x));
                float maxUx = Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x));
                float minUy = Mathf.Min(uv0.y, Mathf.Min(uv1.y, uv2.y));
                float maxUy = Mathf.Max(uv0.y, Mathf.Max(uv1.y, uv2.y));

                int xMin = Mathf.Clamp(Mathf.FloorToInt(minUx * textureWidth) - 1, 0, textureWidth - 1);
                int xMax = Mathf.Clamp(Mathf.CeilToInt(maxUx * textureWidth) + 1, 0, textureWidth - 1);
                int yMin = Mathf.Clamp(Mathf.FloorToInt(minUy * textureHeight) - 1, 0, textureHeight - 1);
                int yMax = Mathf.Clamp(Mathf.CeilToInt(maxUy * textureHeight) + 1, 0, textureHeight - 1);

                // ② 정상 삼각형만 리스트에 추가
                triangleList.Add(new TriangleData
                {
                    pos0 = v0,
                    pos1 = v1,
                    pos2 = v2,
                    uv0 = uv0,
                    uv1 = uv1,
                    uv2 = uv2,
                    xMin = xMin,
                    xMax = xMax,
                    yMin = yMin,
                    yMax = yMax
                });
            }
            
            return triangleList;
        }

        private static Vector3[,] BuildUVToWorldCache2D(
            SkinnedMeshRenderer smr,
            List<TriangleData> flat,
            Texture2D mask,
            float baryMargin = -1e-5f)  // margin 추가
        {
            int width = mask.width;
            int height = mask.height;
            Color[] maskPixels = mask.GetPixels();
            Vector3[,] cache = new Vector3[width, height];

            float invWidth = 1f / width;
            float invHeight = 1f / height;

            foreach (var tri in flat)
            {
                // 삼각형 AABB: 1픽셀 여유 포함
                int xMin = Mathf.Clamp(tri.xMin, 0, width - 1);
                int xMax = Mathf.Clamp(tri.xMax + 1, 0, width - 1);   // +1로 경계 포함
                int yMin = Mathf.Clamp(tri.yMin, 0, height - 1);
                int yMax = Mathf.Clamp(tri.yMax + 1, 0, height - 1);

                for (int y = yMin; y <= yMax; y++)
                {
                    float py = (y + 0.5f) * invHeight; // 픽셀 중심
                    for (int x = xMin; x <= xMax; x++)
                    {
                        float px = (x + 0.5f) * invWidth;
                        Vector2 uv = new Vector2(px, py);

                        if (!IsPointInTriangle2D(uv, tri.uv0, tri.uv1, tri.uv2))
                            continue;

                        int idx = y * width + x;
                        if (maskPixels[idx].a <= 0.01f) continue;

                        Vector3 bary = Barycentric2D(uv, tri.uv0, tri.uv1, tri.uv2);

                        // margin 적용
                        if (bary.x < baryMargin || bary.y < baryMargin || bary.z < baryMargin)
                            continue;

                        Vector3 localPos = tri.pos0 * bary.x + tri.pos1 * bary.y + tri.pos2 * bary.z;
                        cache[x, y] = smr.transform.TransformPoint(localPos);
                    }
                }
            }

            return cache;
        }

        private static Vector2[,] BuildWorldToUVCache2D_FullOptimized(
            SkinnedMeshRenderer smrA,
            List<TriangleData> flatA,
            Vector3[,] uvToWorldB,
            Texture2D mask,
            int nx = 40, int ny = 40, int nz = 40,
            float baryMargin = -1e-5f)
        {
            int width = uvToWorldB.GetLength(0);
            int height = uvToWorldB.GetLength(1);
            Vector2[,] cache = new Vector2[width, height];

            Color[] maskPixels = mask != null ? mask.GetPixels() : null;

            // -----------------------------
            // 1) triGrid 구축
            // -----------------------------
            Vector3 gridMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 gridMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var t in flatA)
            {
                gridMin = Vector3.Min(gridMin, t.pos0);
                gridMin = Vector3.Min(gridMin, t.pos1);
                gridMin = Vector3.Min(gridMin, t.pos2);

                gridMax = Vector3.Max(gridMax, t.pos0);
                gridMax = Vector3.Max(gridMax, t.pos1);
                gridMax = Vector3.Max(gridMax, t.pos2);
            }

            TriangleGrid3D gridA = new TriangleGrid3D(gridMin, gridMax, nx, ny, nz);
            for (int i = 0; i < flatA.Count; i++)
            {
                var t = flatA[i];
                gridA.AddTriangle(i, t.pos0, t.pos1, t.pos2);
            }

            // -----------------------------
            // 2) 픽셀 단위 UV → A 메쉬 UV 변환
            // -----------------------------
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (maskPixels != null && maskPixels[y * width + x].a <= 0f)
                        continue;

                    Vector3 worldPos = uvToWorldB[x, y];
                    if (worldPos == Vector3.zero)
                        continue;

                    Vector3 localPos = smrA.transform.InverseTransformPoint(worldPos);

                    // Grid 후보 삼각형 가져오기
                    List<int> candidates = gridA.Query(localPos);

                    Vector2 uvA = Vector2.zero;
                    bool found = false;

                    if (candidates != null && candidates.Count > 0)
                    {
                        for (int ci = 0; ci < candidates.Count; ci++)
                        {
                            int triIndex = candidates[ci];
                            if (TryBary(localPos, flatA[triIndex], baryMargin, out uvA))
                            {
                                cache[x, y] = uvA;
                                found = true;
                                break;
                            }
                        }
                    }

                    // -----------------------------
                    // fallback: 주변 후보에서 nearest triangle
                    // -----------------------------
                    if (!found)
                    {
                        float minDist = float.MaxValue;
                        Vector2 bestUV = Vector2.zero;

                        // 주변 3x3x3 cell 후보만 탐색
                        List<int> neighborCandidates = gridA.QueryNeighbors(localPos, 1); // 1-cell 반경
                        foreach (var triIndex in neighborCandidates)
                        {
                            Vector3 proj = ClosestPointOnTriangle(localPos,
                                flatA[triIndex].pos0,
                                flatA[triIndex].pos1,
                                flatA[triIndex].pos2);
                            float dist = (localPos - proj).sqrMagnitude;
                            if (dist < minDist)
                            {
                                minDist = dist;

                                CalculateBarycentric3D(proj,
                                    flatA[triIndex].pos0,
                                    flatA[triIndex].pos1,
                                    flatA[triIndex].pos2,
                                    out float w0, out float w1, out float w2);

                                bestUV = flatA[triIndex].uv0 * w0 +
                                        flatA[triIndex].uv1 * w1 +
                                        flatA[triIndex].uv2 * w2;
                            }
                        }

                        cache[x, y] = bestUV;
                    }
                }
            }

            return cache;
        }

        private static bool TryBary(Vector3 p, TriangleData tri, float baryMargin, out Vector2 uv)
        {

            CalculateBarycentric3D(p, tri.pos0, tri.pos1, tri.pos2,
                out float w0, out float w1, out float w2);

            if (w0 < baryMargin || w1 < baryMargin || w2 < baryMargin)
            {
                uv = Vector2.zero;
                return false;
            }

            uv = tri.uv0 * w0 + tri.uv1 * w1 + tri.uv2 * w2;
            return true;
        }
        
        // 점 p를 삼각형(a,b,c) 위에서 가장 가까운 점으로 투영
        public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            // 삼각형 변 벡터
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;

            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);

            if (d1 <= 0f && d2 <= 0f) return a; // vertex a에 가장 가까움

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b; // vertex b에 가장 가까움

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c; // vertex c에 가장 가까움

            // edge AB 위의 projection
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            // edge AC 위의 projection
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            // edge BC 위의 projection
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            // 삼각형 내부
            float denom = 1f / (va + vb + vc);
            float v2 = vb * denom;
            float w2 = vc * denom;
            return a + ab * v2 + ac * w2;
        }

        private static void CalculateBarycentric3D(Vector3 p, Vector3 a, Vector3 b, Vector3 c,
            out float w0, out float w1, out float w2)
        {
            Vector3 v0 = b - a;
            Vector3 v1 = c - a;
            Vector3 v2 = p - a;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;
            w1 = (d11 * d20 - d01 * d21) / denom;
            w2 = (d00 * d21 - d01 * d20) / denom;
            w0 = 1f - w1 - w2;
        }

        private static Texture2D ProjectBumpBontoA(
            Texture2D bumpB,
            Vector2[,] worldToUvA,
            int widthA, int heightA,
            int mode = 1   // 1 = 2x2, 2 = 3x3
        )
        {
            Texture2D resultA = new Texture2D(widthA, heightA, TextureFormat.RGBA32, false);
            Color[] pixelsA = new Color[widthA * heightA];
            for (int i = 0; i < pixelsA.Length; i++)
                pixelsA[i] = new Color(0, 0, 0, 0);

            int widthB = bumpB.width;
            int heightB = bumpB.height;
            Color[] pixelsB = bumpB.GetPixels();

            // --- mode → splatting range 변환 ---
            int start, end;

            if (mode == 1)
            {
                // 2x2: (0,1)
                start = 0;
                end = 1;
            }
            else
            {
                // 3x3: (-1,0,+1)
                start = -1;
                end = 1;
            }

            for (int yB = 0; yB < heightB; yB++)
            {
                for (int xB = 0; xB < widthB; xB++)
                {
                    Vector2 uvA = worldToUvA[xB, yB];
                    if (uvA == Vector2.zero) continue;

                    int xBase = Mathf.FloorToInt(uvA.x * widthA);
                    int yBase = Mathf.FloorToInt(uvA.y * heightA);

                    int idxB = yB * widthB + xB;
                    Color cB = pixelsB[idxB];

                    // ---------------- SPLATTING ----------------
                    for (int dy = start; dy <= end; dy++)
                    {
                        for (int dx = start; dx <= end; dx++)
                        {
                            int xA = xBase + dx;
                            int yA = yBase + dy;

                            if (xA < 0 || xA >= widthA || yA < 0 || yA >= heightA)
                                continue;

                            int idxA = yA * widthA + xA;
                            pixelsA[idxA] = cB;
                        }
                    }
                }
            }

            resultA.SetPixels(pixelsA);
            resultA.Apply();
            return resultA;
        }


        private static Texture2D BlendBumpmap(
            Texture2D bumpA,            // 원본 A bumpmap
            Texture2D mergeTexture,     // B 머징용 texture (A UV 좌표계에 맞춰져 있음)
            float weight = 0.5f         // blending weight (0~1)
        )
        {
            int width = bumpA.width;
            int height = bumpA.height;

            Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);

            Color[] pixelsA = bumpA.GetPixels();
            Color[] pixelsB = mergeTexture.GetPixels();
            Color[] pixelsResult = new Color[width * height];

            int mergeHeight = mergeTexture.height;

            for (int y = 0; y < height; y++)
            {
                int yB = Mathf.Min(y, mergeHeight - 1); // clamp y
                for (int x = 0; x < width; x++)
                {
                    int idxA = y * width + x;
                    int idxB = yB * mergeTexture.width + x; // mergeTexture width는 항상 1024

                    Color a = pixelsA[idxA];
                    Color b = pixelsB[idxB];

                    if (b.a <= 0.01f)
                    {
                        pixelsResult[idxA] = a;
                    }
                    else if (b.a >= 0.99f)
                    {
                        Color target = new Color(
                            Mathf.Lerp(a.r, 1.0f, weight),
                            Mathf.Lerp(a.g, 0.74f, weight),
                            Mathf.Lerp(a.b, 0.74f, weight),
                            Mathf.Lerp(a.a, 0.5f, weight)
                        );
                        pixelsResult[idxA] = target;
                    }
                    else
                    {
                        pixelsResult[idxA] = Color.Lerp(a, b, weight);
                    }
                }
            }

            result.SetPixels(pixelsResult);
            result.Apply();
            return result;
        }


        public static Vector3 Barycentric2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector2.Dot(v0, v0);
            float d01 = Vector2.Dot(v0, v1);
            float d11 = Vector2.Dot(v1, v1);
            float d20 = Vector2.Dot(v2, v0);
            float d21 = Vector2.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;
            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1f - v - w;
            return new Vector3(u, v, w);
        }
        
            
        public static bool IsPointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            // Compute vectors
            Vector2 v0 = c - a;
            Vector2 v1 = b - a;
            Vector2 v2 = p - a;

            // Compute dot products (inner product)
            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);

            // Compute barycentric coordinates
            float invDenom = 1f / (dot00 * dot11 - dot01 * dot01);
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;

            // Check if point is in triangle
            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }
        


        private static void ApplyShowUnderwear(RealHumanData realHumanData)
        {

            OCIChar ociChar = realHumanData.ociChar;
            SkinnedMeshRenderer clothUpperRender = null;
            SkinnedMeshRenderer clothBottomRender = null;
            SkinnedMeshRenderer clothUnderwearRender = null;

            int width_A = 0;
            int height_A = 0;

            if (ociChar.charInfo.objClothes[1] != null)
            {
                // bottom
                SkinnedMeshRenderer[] _sks = ociChar.charInfo.objClothes[1].GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (SkinnedMeshRenderer render in _sks.ToList())
                {
                    foreach (var mat in render.sharedMaterials)
                    {
                        if (mat.GetTexture("_BumpMap"))
                        {
                            width_A = mat.GetTexture("_BumpMap").width;
                            height_A = mat.GetTexture("_BumpMap").height;

                            if (width_A == height_A)
                            {
                                width_A = 1024;
                                height_A = 1024;
                            } else
                            {
                                width_A = 1024;
                                height_A = 512;                
                            }
                            
                            clothBottomRender = render;
                            UnityEngine.Debug.Log($">> found clothBottomRender: + {render.name}, {mat.name}");
                            break;
                        }
                    }
                }
            }
            
            if (ociChar.charInfo.objClothes[3] != null)
            {
                // underwear
                SkinnedMeshRenderer[] _sks = ociChar.charInfo.objClothes[3].GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (SkinnedMeshRenderer render in _sks.ToList())
                {
                    foreach (var mat in render.sharedMaterials)
                    {
                        if (mat.GetTexture("_BumpMap"))
                        {
                            clothUnderwearRender = render;
                            UnityEngine.Debug.Log($">> found clothUnderwear: + {render.name}, {mat.name}");
                            break;
                        }
                    }
                }
            }
 
            if ((clothBottomRender != null) && clothUnderwearRender != null)
            {
                 // ComputeShader mergeShader = _self._mergeShader; // AssetBundle에서 로드한 Shader

                Texture2D bottomBump = EnsureSize(clothBottomRender.material.GetTexture("_BumpMap") as Texture2D, width_A, height_A);
                Texture2D bumpMapA = MakeReadableTexture(bottomBump);

                Texture2D underBump = EnsureSize(clothUnderwearRender.material.GetTexture("_BumpMap") as Texture2D, 1024, 512);
                Texture2D bumpMapB = MakeReadableTexture(underBump);

                Mesh meshA = new Mesh();
                Mesh meshB = new Mesh();
                
                clothBottomRender.BakeMesh(meshA);
                clothUnderwearRender.BakeMesh(meshB);

                Transform transformA = clothBottomRender.transform;
                Transform transformB = clothUnderwearRender.transform;

                List<int> aBackTriangles = ExtractBackTriangles(meshA, transformA);
                List<int> bBackTriangles = ExtractBackTriangles(meshB, transformB);

                Texture2D maskB = CreateRegionMask(meshB, bBackTriangles, bumpMapB.width, bumpMapB.height);
                // SaveAsPNG(maskB, "maskB.png");

                var trianglesA = CreateTriangleDataList(meshA, transformA, aBackTriangles, bumpMapA.width, bumpMapA.height);
                var trianglesB = CreateTriangleDataList(meshB, transformB, bBackTriangles, bumpMapB.width, bumpMapB.height);

                var bToWorldCache = BuildUVToWorldCache2D(clothUnderwearRender, trianglesB, bumpMapB);
                var worldToA = BuildWorldToUVCache2D_FullOptimized(clothBottomRender, trianglesA, bToWorldCache, maskB);

                Texture2D mergedB = ProjectBumpBontoA(bumpMapB, worldToA, bumpMapA.width, bumpMapA.height);
                SaveAsPNG(mergedB, "mergedB.png");

                Texture2D blended = BlendBumpmap(bumpMapA, mergedB, 0.5f);
                SaveAsPNG(blended, "blended.png");

                clothBottomRender.material.SetTexture("_BumpMap", blended);
                // SaveAsPNG(maskB, "maskB.png");
            }
            else
            {
                if (clothBottomRender == null) 
                    Logger.LogMessage($"Not Wear Bottom Cloth");

                if (clothUnderwearRender == null)
                    Logger.LogMessage($"Not Wear Underwear Cloth");
            }
        }

        private static RealHumanData CreateRealData(OCIChar ociChar)
        {
            RealHumanData realHumanData = new RealHumanData();

            realHumanData.ociChar = ociChar;            

            return realHumanData;
        }

       [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                if(Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() == 0) {
                    _self._selectedOciChar = null;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);

                _self._selectedOciChar = objectCtrlInfo as OCIChar;
                // RealHumanData realHumanData = CreateRealData(selectedOciChar);
                // selectedOciChar.charInfo.StartCoroutine(ExecuteAfterFrame(realHumanData));

                return true;
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

    class TriangleGrid3D
    {
        private List<int>[,,] cells;
        private Vector3 min, max;
        private int nx, ny, nz;
        private Vector3 cellSize;

        public TriangleGrid3D(Vector3 min, Vector3 max, int nx, int ny, int nz)
        {
            this.min = min;
            this.max = max;
            this.nx = nx;
            this.ny = ny;
            this.nz = nz;

            cells = new List<int>[nx, ny, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                        cells[i, j, k] = new List<int>();

            cellSize = new Vector3(
                (max.x - min.x) / nx,
                (max.y - min.y) / ny,
                (max.z - min.z) / nz
            );
        }

        public void AddTriangle(int triIndex, Vector3 v0, Vector3 v1, Vector3 v2)
        {
            // 삼각형 AABB
            Vector3 tMin = Vector3.Min(v0, Vector3.Min(v1, v2));
            Vector3 tMax = Vector3.Max(v0, Vector3.Max(v1, v2));

            int ix0 = Mathf.Clamp((int)((tMin.x - min.x) / cellSize.x), 0, nx - 1);
            int iy0 = Mathf.Clamp((int)((tMin.y - min.y) / cellSize.y), 0, ny - 1);
            int iz0 = Mathf.Clamp((int)((tMin.z - min.z) / cellSize.z), 0, nz - 1);

            int ix1 = Mathf.Clamp((int)((tMax.x - min.x) / cellSize.x), 0, nx - 1);
            int iy1 = Mathf.Clamp((int)((tMax.y - min.y) / cellSize.y), 0, ny - 1);
            int iz1 = Mathf.Clamp((int)((tMax.z - min.z) / cellSize.z), 0, nz - 1);

            for (int ix = ix0; ix <= ix1; ix++)
                for (int iy = iy0; iy <= iy1; iy++)
                    for (int iz = iz0; iz <= iz1; iz++)
                        cells[ix, iy, iz].Add(triIndex);
        }

        public List<int> Query(Vector3 pos)
        {
            int ix = Mathf.Clamp((int)((pos.x - min.x) / cellSize.x), 0, nx - 1);
            int iy = Mathf.Clamp((int)((pos.y - min.y) / cellSize.y), 0, ny - 1);
            int iz = Mathf.Clamp((int)((pos.z - min.z) / cellSize.z), 0, nz - 1);

            return cells[ix, iy, iz];
        }

        // -----------------------------
        // 주변 radius 검색
        // -----------------------------
        public List<int> QueryNeighbors(Vector3 pos, int radius)
        {
            int ix = Mathf.Clamp((int)((pos.x - min.x) / cellSize.x), 0, nx - 1);
            int iy = Mathf.Clamp((int)((pos.y - min.y) / cellSize.y), 0, ny - 1);

            HashSet<int> result = new HashSet<int>();

            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                int cx = Mathf.Clamp(ix + dx, 0, nx - 1);
                int cy = Mathf.Clamp(iy + dy, 0, ny - 1);

                // z loop 제거 → XY plane만
                for (int cz = 0; cz < nz; cz++)
                    result.UnionWith(cells[cx, cy, cz]);
            }

            return result.ToList();
        }
    }
    class RealHumanData
    {
        public OCIChar ociChar;

        public RealHumanData()
        {

        }
    }

    struct TriangleData
    {
        public Vector3 pos0, pos1, pos2; // Local vertex positions
        public Vector2 uv0, uv1, uv2;    // UV 좌표
        public int xMin, xMax, yMin, yMax;
        public Vector2 uvCenter; 

    }


    #endregion

}