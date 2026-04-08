using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

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
using KKAPI;
using KKAPI.Chara;
#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif

namespace ClothQuickTransform
{
    public class ClothQuickTransformController: CharaCustomFunctionController
    {
        ClothQuickTransformMapData clothQuickTransformMapData;
        protected override void OnCardBeingSaved(GameMode currentGameMode) { }

        // 현재 맵 데이터를 반환한다.
        internal ClothQuickTransformMapData GetData()
        {
            return clothQuickTransformMapData;
        }

        // 필요 시 맵 데이터를 생성하고 반환한다.
        internal ClothQuickTransformMapData GetOrCreateData(OCIChar _ociChar)
        {
            if (clothQuickTransformMapData == null)
                clothQuickTransformMapData = new ClothQuickTransformMapData();
            if (_ociChar != null)
                clothQuickTransformMapData.ociChar = _ociChar;
            return clothQuickTransformMapData;
        }

        // 캐릭터 기준으로 맵 데이터 초기화한다.
        internal void InitClothQuickTransformMapData(OCIChar _ociChar)
        {
            GetOrCreateData(_ociChar);
        }
    }

    class ClothQuickTransformMapData
    {
        public OCIChar ociChar;
        public List<ClothQuickTransform.TransferEntry> transferEntries = new List<ClothQuickTransform.TransferEntry>();
        public int selectedTransferIndex = -1;
        public Dictionary<string, ClothQuickTransform.SavedAdjustment> savedAdjustments =
            new Dictionary<string, ClothQuickTransform.SavedAdjustment>();
        public bool pendingAutoRemap;
        public Vector2 transferScroll;
    }
}
