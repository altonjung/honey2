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

        // end

        private ComputeShader _mergeShader;


        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            _self = this;

            Logger = base.Logger;

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
        

        public static Texture2D EnsureSize(Texture2D source, int width, int height)
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


        public static void SaveAsPNG(Texture2D tex, string path)
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

        private static List<int> GetBackIndices(Vector3[] worldVerts)
        {
            List<int> backIndices = new List<int>();
            for (int i = 0; i < worldVerts.Length; i++)
            {
                if (worldVerts[i].z < 0) // 뒤쪽 영역 기준 (z < 0)
                {
                    backIndices.Add(i);
                }
            }
            return backIndices; 
        }

        private static Texture2D ExtractRegionBumpMap(Texture2D bumpMap, Texture2D mask)
        {
            int width = bumpMap.width;
            int height = bumpMap.height;
            Texture2D regionMap = new Texture2D(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color maskColor = mask.GetPixel(x, y);
                    Color bumpColor = bumpMap.GetPixel(x, y);

                    if (maskColor.a > 0.5f)
                        regionMap.SetPixel(x, y, bumpColor);
                    else
                        regionMap.SetPixel(x, y, Color.clear);
                }
            }
            regionMap.Apply();
            return regionMap;
        }

        private static Vector3 Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            // 벡터 계산
            Vector2 v0 = b - a;
            Vector2 v1 = c - a;
            Vector2 v2 = p - a;

            float d00 = Vector2.Dot(v0, v0);
            float d01 = Vector2.Dot(v0, v1);
            float d11 = Vector2.Dot(v1, v1);
            float d20 = Vector2.Dot(v2, v0);
            float d21 = Vector2.Dot(v2, v1);

            float denom = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denom) < 1e-6f) return new Vector3(-1, -1, -1); // 삼각형 면적 0일 때

            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1.0f - v - w;

            return new Vector3(u, v, w);
        }

        private static Vector3[] GetWorldVertices(Mesh mesh, Transform transform, Vector3[] localVerts)
        {
            Vector3[] worldVerts = new Vector3[localVerts.Length];
            for (int i = 0; i < localVerts.Length; i++)
            {
                worldVerts[i] = transform.TransformPoint(localVerts[i]);
            }
            return worldVerts;
        }

        private static Texture2D CreateRegionMask(Mesh mesh, List<int> backIndices, int textureWidth, int textureHeight)
        {
            Texture2D mask = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
            Color clear = Color.clear;
            for (int y = 0; y < textureHeight; y++)
                for (int x = 0; x < textureWidth; x++)
                    mask.SetPixel(x, y, clear);

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

        // UV 삼각형 영역을 texture에 채우는 함수 (간단한 rasterization)
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

        // 점이 삼각형 내부인지 확인하는 함수
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


        private static Vector2 ConvertBVertexToAUV(Vector3 worldPosB, Mesh meshA, Transform transformA)
        {
            // 1. B vertex의 world pos → A의 local space로 변환
            Vector3 localA = transformA.InverseTransformPoint(worldPosB);

            // 2. A mesh의 삼각형 중, 이 localA가 포함되는 삼각형을 찾기
            //    (barycentric 좌표를 이용해서 interpolation 가능)
            // 3. 그 삼각형의 UV를 barycentric 보간해서, 해당 위치의 UV_A를 얻는다

            // (간단 버전: A vertex들 중 가장 가까운 UV를 사용하는 근사치)
            float minDist = float.MaxValue;
            int nearestIndex = 0;
            for (int i = 0; i < meshA.vertexCount; i++)
            {
                float dist = Vector3.Distance(localA, meshA.vertices[i]);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearestIndex = i;
                }
            }
            return meshA.uv[nearestIndex];
        }

        public static void CreateRegionBWorldTable(
            Mesh meshB,
            Transform transformB,
            Texture2D regionB,
            Texture2D maskB,
            out Vector3[] worldPositionsB,
            out Color[] colorsB)
        {
            int width = regionB.width;
            int height = regionB.height;

            Vector3[] meshVertsB = meshB.vertices;
            Vector2[] meshUVsB = meshB.uv;
            int[] trianglesB = meshB.triangles;

            // mesh vertex를 World Position으로 변환
            Vector3[] worldVertsB = new Vector3[meshVertsB.Length];
            for (int i = 0; i < meshVertsB.Length; i++)
                worldVertsB[i] = transformB.TransformPoint(meshVertsB[i]);

            worldPositionsB = new Vector3[width * height];
            colorsB = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;

                    // maskB가 없는 영역은 스킵
                    if (maskB.GetPixel(x, y).r <= 0.5f)
                    {
                        worldPositionsB[index] = Vector3.zero;
                        colorsB[index] = Color.clear;
                        continue;
                    }

                    Vector2 uvCoord = new Vector2(x / (float)width, y / (float)height);

                    // 픽셀이 속한 삼각형 찾기
                    if (FindContainingTriangleUV(uvCoord, meshUVsB, trianglesB, out int t0, out int t1, out int t2))
                    {
                        // Barycentric 좌표 계산
                        Vector3 bary = BarycentricUV(uvCoord, meshUVsB[t0], meshUVsB[t1], meshUVsB[t2]);

                        // World Position 보간
                        Vector3 worldPos = worldVertsB[t0] * bary.x +
                                        worldVertsB[t1] * bary.y +
                                        worldVertsB[t2] * bary.z;

                        worldPositionsB[index] = worldPos;
                        colorsB[index] = regionB.GetPixel(x, y);
                    }
                    else
                    {
                        worldPositionsB[index] = Vector3.zero;
                        colorsB[index] = Color.clear;
                    }
                }
            }
        }


        public static Texture2D MergeBumpMapWithRegionB(
    Mesh meshA,
    Transform transformA,
    Texture2D bumpMapA,
    Texture2D maskA,
    Vector3[] worldPositionsB,
    Color[] colorsB)
{
    int width = bumpMapA.width;
    int height = bumpMapA.height;

    Texture2D result = new Texture2D(width, height);
    Color[] resultColors = bumpMapA.GetPixels(); // 기존 bumpMapA 색상 복사

    // meshA의 world vertex 계산
    Vector3[] meshVertsA = meshA.vertices;
    Vector2[] uvA = meshA.uv;
    Vector3[] worldVertsA = new Vector3[meshVertsA.Length];
    for (int i = 0; i < meshVertsA.Length; i++)
        worldVertsA[i] = transformA.TransformPoint(meshVertsA[i]);

    // 픽셀 단위로 loop
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;

            // maskA가 0이면 그대로 bumpMapA 유지
            if (maskA.GetPixel(x, y).r <= 0.5f)
                continue;

            // UV -> World Position 변환
            Vector3 worldPosA = GetWorldPositionFromUVInMesh(x, y, width, height, meshA, uvA, worldVertsA);

            // World Position 기준으로 regionB 색상 찾기
            float minDist = float.MaxValue;
            Color nearestColorB = Color.clear;

            for (int i = 0; i < worldPositionsB.Length; i++)
            {
                if (colorsB[i] == Color.clear)
                    continue;

                float d = Vector3.Distance(worldPosA, worldPositionsB[i]);
                if (d < minDist)
                {
                    minDist = d;
                    nearestColorB = colorsB[i];
                }
            }

            if (nearestColorB != Color.clear)
            {
                // blending 규칙 적용
                Color blended = Color.Lerp(resultColors[index], nearestColorB, 0.5f);
                blended.r = 1.0f;
                blended.g = 189f / 255f;
                blended.b = 189f / 255f;
                blended.a = 0.5f;
                resultColors[index] = blended;
            }
        }
    }

    result.SetPixels(resultColors);
    result.Apply();
    return result;
}

        private static Vector3 GetWorldPositionFromUVInMesh(int x, int y, int width, int height, Mesh mesh, Vector2[] uv, Vector3[] worldVerts)
        {
            Vector2 uvCoord = new Vector2(x / (float)width, y / (float)height);
            if (FindContainingTriangleUV(uvCoord, uv, mesh.triangles, out int t0, out int t1, out int t2))
            {
                Vector3 bary = BarycentricUV(uvCoord, uv[t0], uv[t1], uv[t2]);
                return worldVerts[t0] * bary.x + worldVerts[t1] * bary.y + worldVerts[t2] * bary.z;
            }
            return Vector3.zero;
        }
        private static bool FindContainingTriangleUV(Vector2 uvCoord, Vector2[] uv, int[] tris, out int t0, out int t1, out int t2)
        {
            t0 = t1 = t2 = -1;

            for (int i = 0; i < tris.Length; i += 3)
            {
                int i0 = tris[i];
                int i1 = tris[i + 1];
                int i2 = tris[i + 2];

                Vector2 uv0 = uv[i0];
                Vector2 uv1 = uv[i1];
                Vector2 uv2 = uv[i2];

                if (IsPointInTriangle(uvCoord, uv0, uv1, uv2))
                {
                    t0 = i0;
                    t1 = i1;
                    t2 = i2;
                    return true;
                }
            }

            return false; // 포함되는 삼각형 없음
        }

        // worldPos: 찾고 싶은 3D 위치
        // verts: 삼각형들의 vertex 배열 (Mesh.vertices를 Transform 후 World Position 기준)
        // triangles: 삼각형 인덱스 배열 (Mesh.triangles)
        // out t0, t1, t2: 해당 위치를 포함하는 삼각형의 vertex 인덱스
        private static bool FindContainingTriangle(Vector3 worldPos, Vector3[] verts, int[] triangles, out int t0, out int t1, out int t2)
        {
            t0 = t1 = t2 = -1;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                Vector3 v0 = verts[i0];
                Vector3 v1 = verts[i1];
                Vector3 v2 = verts[i2];

                if (IsPointInTriangle3D(worldPos, v0, v1, v2))
                {
                    t0 = i0;
                    t1 = i1;
                    t2 = i2;
                    return true;
                }
            }

            return false;
        }

        
        // Barycentric 좌표 계산 (UV 기준)
        private static Vector3 BarycentricUV(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
{
    Vector2 v0 = b - a;
    Vector2 v1 = c - a;
    Vector2 v2 = p - a;

    float d00 = Vector2.Dot(v0, v0);
    float d01 = Vector2.Dot(v0, v1);
    float d11 = Vector2.Dot(v1, v1);
    float d20 = Vector2.Dot(v2, v0);
    float d21 = Vector2.Dot(v2, v1);

    float denom = d00 * d11 - d01 * d01;
    if (Mathf.Abs(denom) < 1e-6f) return new Vector3(-1, -1, -1);

    float v = (d11 * d20 - d01 * d21) / denom;
    float w = (d00 * d21 - d01 * d20) / denom;
    float u = 1.0f - v - w;

    return new Vector3(u, v, w);
}

// UV에 가장 가까운 vertex 찾기
private static int FindNearestUV(Vector2 uv, Vector2[] candidates)
{
    int nearest = 0;
    float minDist = float.MaxValue;
    for (int i = 0; i < candidates.Length; i++)
    {
        float d = Vector2.Distance(uv, candidates[i]);
        if (d < minDist)
        {
            minDist = d;
            nearest = i;
        }
    }
    return nearest;
}

// UV 삼각형 내부 확인
private static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
{
    Vector2 v0 = c - a;
    Vector2 v1 = b - a;
    Vector2 v2 = p - a;

    float dot00 = Vector2.Dot(v0, v0);
    float dot01 = Vector2.Dot(v0, v1);
    float dot02 = Vector2.Dot(v0, v2);
    float dot11 = Vector2.Dot(v1, v1);
    float dot12 = Vector2.Dot(v1, v2);

    float invDenom = 1f / (dot00 * dot11 - dot01 * dot01);
    float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
    float v = (dot00 * dot12 - dot01 * dot02) * invDenom;

    return (u >= 0) && (v >= 0) && (u + v <= 1);
}


        private static Vector3 BarycentricWorldPosition(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
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
            if (Mathf.Abs(denom) < 1e-6f) return new Vector3(-1, -1, -1);

            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1f - v - w;
            return new Vector3(u, v, w);
        }


        private static bool IsPointInTriangle3D(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            // 삼각형 평면의 법선
            Vector3 normal = Vector3.Cross(b - a, c - a);
            float area2 = normal.magnitude;

            // 점 p가 삼각형 평면상에 있는지 확인 (작은 허용 오차)
            float distance = Vector3.Dot(normal.normalized, p - a);
            if (Mathf.Abs(distance) > 1e-4f) return false;

            // barycentric 좌표 계산
            Vector3 v0 = b - a;
            Vector3 v1 = c - a;
            Vector3 v2 = p - a;

            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);

            float denom = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denom) < 1e-6f) return false;

            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1f - v - w;

            // u, v, w 모두 0~1 사이면 삼각형 안
            return (u >= 0f) && (v >= 0f) && (w >= 0f);
        }


        private static void ApplyShowUnderwear(RealHumanData realHumanData)
        {

            SkinnedMeshRenderer clothBottomRender = null;
            SkinnedMeshRenderer clothUnderwearRender = null;

            OCIChar ociChar = realHumanData.ociChar;

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

            Mesh meshA = clothBottomRender.sharedMesh;
            Mesh meshB = clothUnderwearRender.sharedMesh;
            Transform transformA = clothBottomRender.transform;
            Transform transformB = clothUnderwearRender.transform;

            // ComputeShader mergeShader = _self._mergeShader; // AssetBundle에서 로드한 Shader

            Texture2D bottomBump = EnsureSize(clothBottomRender.material.GetTexture("_BumpMap") as Texture2D, 1024, 512);
            Texture2D bumpMapA = MakeReadableTexture(bottomBump);

            Texture2D underBump = EnsureSize(clothUnderwearRender.material.GetTexture("_BumpMap") as Texture2D, 1024, 512);
            Texture2D bumpMapB = MakeReadableTexture(underBump);

            Vector3[] localVertsA = meshA.vertices;
            Vector3[] worldVertsA = new Vector3[localVertsA.Length];
            for (int i = 0; i < localVertsA.Length; i++)
            {
                worldVertsA[i] = transformA.TransformPoint(localVertsA[i]);
            }

            Vector3[] localVertsB = meshB.vertices;
            Vector3[] worldVertsB = new Vector3[localVertsB.Length];
            for (int i = 0; i < localVertsB.Length; i++)
            {
                worldVertsB[i] = transformB.TransformPoint(localVertsB[i]);
            }

            // 1. 뒤쪽 영역 인덱스 추출
            List<int> backIndicesA = GetBackIndices(worldVertsA);
            List<int> backIndicesB = GetBackIndices(worldVertsB);

            // 2. region mask 생성
            Texture2D maskA = CreateRegionMask(meshA, backIndicesA, bumpMapA.width, bumpMapA.height);
            Texture2D maskB = CreateRegionMask(meshB, backIndicesB, bumpMapB.width, bumpMapB.height);

            SaveAsPNG(maskA, "maskA.png");
            SaveAsPNG(maskB, "maskB.png");

            // // 3. bumpmap region 추출
            // //Texture2D regionA = ExtractRegionBumpMap(bumpMapA, maskA);
            // Texture2D regionB = ExtractRegionBumpMap(bumpMapB, maskB);

            // // SaveAsPNG(regionA, "regionA.png");
            // SaveAsPNG(regionB, "regionB.png");

            // // 4. region bumpmap 머징
            // // Texture2D mergedBumpMap = MergeRegionBumpMap(regionA, regionB, meshA, meshB, transformA, transformB);

            // Vector3[] worldPositionsB;
            // Color[] colorsB;

            // CreateRegionBWorldTable( meshB, transformB, regionB, maskB,  out worldPositionsB, out colorsB);

            // // Texture2D mergedBumpMap = MergeRegionBumpMapWorldSpace(meshA, transformA, bumpMapA, maskA, meshB, transformB, regionB);

            // Texture2D mergedBumpMap = MergeBumpMapWithRegionB(meshA, transformA, bumpMapA, maskA, worldPositionsB, colorsB );


            // // 5. blending
            // SaveAsPNG(mergedBumpMap, "merge_result.png");

            // clothBottomRender.material.SetTexture("_BumpMap", mergedBumpMap);
        }


        private static RealHumanData CreateRealData(OCIChar ociChar)
        {
            RealHumanData realHumanData = new RealHumanData();

            realHumanData.ociChar = ociChar;            

            return realHumanData;
        }

        private static IEnumerator ExecuteAfterFrame(RealHumanData realHumanData)
        {
            int frameCount = 30;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            ApplyShowUnderwear(realHumanData);
        }


        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);

                OCIChar selectedOciChar = objectCtrlInfo as OCIChar;

                RealHumanData realHumanData = CreateRealData(selectedOciChar);
                selectedOciChar.charInfo.StartCoroutine(ExecuteAfterFrame(realHumanData));

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

    struct FootprintPixel
    {
        public Vector2 uv;
        public Vector3 world;
        public Color bump;
    }
    
    class RealHumanData
    {
        public OCIChar ociChar;

        public RealHumanData()
        {

        }
    }
    public class BumpMapData
    {
        public Texture2D bumpMap;                 // 뒷면 bumpmap 텍스처
        public HashSet<int> backsideVertices;     // 뒷면 vertex 인덱스 집합
        public List<int> backsideTriangles;

        public BumpMapData(Texture2D bump, HashSet<int> vertices, List<int> triangles)
        {
            bumpMap = bump;
            backsideVertices = vertices;
            backsideTriangles = triangles;
        }        
    }

    #endregion

}