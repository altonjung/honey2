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

        // 캐릭터 별 맵 데이터를 생성하고 반환한다.
        internal ClothQuickTransformMapData CreateData(OCIChar _ociChar)
        {
            if (_ociChar != null) {
                if (clothQuickTransformMapData == null)
                    clothQuickTransformMapData = new ClothQuickTransformMapData();

                clothQuickTransformMapData.ociChar = _ociChar;
            }
            return clothQuickTransformMapData;
        }
    }

    class ClothQuickTransformMapData
    {
        public OCIChar ociChar;
        public Dictionary<int, List<ClothQuickTransform.TransferEntry>> transferEntriesBySlot =
            new Dictionary<int, List<ClothQuickTransform.TransferEntry>>();
        public Dictionary<int, int> selectedTransferIndexBySlot =
            new Dictionary<int, int>();
        public Dictionary<int, Dictionary<string, ClothQuickTransform.SavedAdjustment>> savedAdjustmentsBySlot =
            new Dictionary<int, Dictionary<string, ClothQuickTransform.SavedAdjustment>>();
        public bool pendingAutoRemap;
        public Dictionary<int, Vector2> transferScrollBySlot =
            new Dictionary<int, Vector2>();
    }
}
