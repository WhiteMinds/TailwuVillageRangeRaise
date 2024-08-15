using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using TaiwuModdingLib.Core.Plugin;
using UnityEngine;
using GameData.Domains.Character;
using GameData.Utilities;
using GameData.Serializer;
using Config;
using System.Reflection;
using GameData.Domains.Building;
using GameData.Domains.Taiwu;
using GameData.Domains.Item.Display;
using GameData.GameDataBridge.VnPipe;
using GameData.Domains.Map;

namespace TailwuVillageRangeRaise
{
    [PluginConfig("TailwuVillageRangeRaise", "WhiteMind", "1.0.0")]
    public class TailwuVillageRangeRaiseFrontendPlugin : TaiwuRemakePlugin
    {
        static bool latestBuildingAreaIsTaiwuVillage = false;
        static sbyte newSize = 32;

        Harmony harmony;
        public override void Dispose()
        {
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }
        }

        public override void Initialize()
        {
            harmony = Harmony.CreateAndPatchAll(typeof(TailwuVillageRangeRaiseFrontendPlugin));
            Debug.Log("TailwuVillageRangeRaiseFrontendPlugin initialized");

            // 启动新游戏时会调用后端的 CreateWorld，后端会根据 BuildingAreaWidth 来创建一系列数据。
            // 等创建了之后再去修会比较复杂，且可能修复工作会随着版本更新而失效，所以目前只是简单的支持新存档。
            MapBlockItem mapBlockItem = MapBlock.Instance.GetItem((short)MapBlock.Instance.GetItemId("太吾村"));
            var buildingAreaWidthField = typeof(MapBlockItem).GetField("BuildingAreaWidth", BindingFlags.Public | BindingFlags.Instance);
            buildingAreaWidthField.SetValue(mapBlockItem, newSize);
            Debug.Log($"newSize {mapBlockItem.BuildingAreaWidth}");
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UI_BuildingArea), "OnEnable")]
        public static void UI_BuildingArea_OnEnable_Prefix(UI_BuildingArea __instance, bool ____isTaiwuVillage)
        {
            latestBuildingAreaIsTaiwuVillage = ____isTaiwuVillage;
        }

        // 每个 buildingBlock 都在 prefab 里有对应的 Refers，从 256 扩张到 1024 的这部分没有，所以在这里修复缺失的部分。
        [HarmonyPrefix, HarmonyPatch(typeof(UI_BuildingArea), "Awake")]
        public static void UI_BuildingArea_Awake_Prefix(UI_BuildingArea __instance, bool ____isTaiwuVillage, ref Stack<Refers> ____blockListSize1, ref Stack<Refers> ____blockListSize2)
        {
            Debug.Log($"UI_BuildingArea.Awake called {____isTaiwuVillage}");

            var sizeOneTargetCount = newSize * newSize;
            var sizeTwoTargetCount = newSize * newSize / (2 * 2);

            var _buildingBlockHolder = __instance.CGet<RectTransform>("BuildingBlockHolder");
            Refers[] blockRefers = _buildingBlockHolder.GetComponentsInChildren<Refers>();
            var sizeOneEls = blockRefers.Where(r => r.name.Contains("SizeOne"));
            var sizeTwoEls = blockRefers.Where(r => r.name.Contains("SizeTwo"));
            for (int i = sizeOneEls.Count(); i < sizeOneTargetCount; i++)
            {
                var cloned = GameObject.Instantiate(sizeOneEls.First(), _buildingBlockHolder);
                cloned.name = $"BuildingBlockPrefab_SizeOne_{i + 1}";
            }
            for (int i = sizeTwoEls.Count(); i < sizeTwoTargetCount; i++)
            {
                var cloned = GameObject.Instantiate(sizeTwoEls.First(), _buildingBlockHolder);
                cloned.name = $"BuildingBlockPrefab_SizeTwo_{i + 1}";
            }
            ____blockListSize1 = new Stack<Refers>(sizeOneTargetCount);
            ____blockListSize2 = new Stack<Refers>(sizeTwoTargetCount);
            
            Debug.Log("UI_BuildingArea.Awake called end");
        }

        // 在 newSize 应用后，InitBuildingArea 中会调用 ResLoader.Load 来加载 32_n 的，替换为 16_n。
        // ResLoader.Load 是个静态泛型函数，hook 之后会出问题导致大量资源无法正常加载，所以不直接 hook 它，而是 hook 了 string.Format。
        [HarmonyPrefix, HarmonyPatch(typeof(string), "Format", new Type[] { typeof(string), typeof(object), typeof(object) })]
        public static void String_Format_Prefix(string format, ref object arg0, ref object arg1)
        {
            if (latestBuildingAreaIsTaiwuVillage && format == "RemakeResources/Prefab/Core/Building/Border/{0}_{1}")
            {
                if ((sbyte)arg0 == (sbyte)newSize)
                {
                    arg0 = (sbyte)16;
                }
            }
        }

        // 这里要将 16_n 的资源放大到 32_n。
        [HarmonyPostfix, HarmonyPatch(typeof(UI_BuildingArea), "InitBuildingArea")]
        public static void UI_BuildingArea_InitBuildingArea_Postfix(UI_BuildingArea __instance, bool ____isTaiwuVillage, BuildingAreaData ____areaData, RectTransform ____moveRoot)
        {
            Debug.Log($"UI_BuildingArea.InitBuildingArea called {____isTaiwuVillage}");
            if (!____isTaiwuVillage) return;

            var borderRoot = __instance.CGet<RectTransform>("Border");
            MouseWheelScale scaler = ____moveRoot.GetComponent<MouseWheelScale>();
            
            // 原计划是通过 borderRoot.GetComponentInChildren<RectTransform>() 获取到 border，然后直接修改 size，但是修改后未生效，可能是需要在哪触发重新渲染。
            // 由于开发时间问题，这里就直接复制一个新的来覆盖上去。
            string borderPath = string.Format("RemakeResources/Prefab/Core/Building/Border/{0}_{1}", (sbyte)16, ____areaData.LandFormType);
            ResLoader.Load<GameObject>(borderPath, delegate (GameObject prefab)
            {
                RectTransform border = UnityEngine.Object.Instantiate<GameObject>(prefab, borderRoot, false).GetComponent<RectTransform>();
                RectTransformExtensions.SetSize(border, border.rect.size * 2);
                border.gameObject.SetActive(true);
                Vector2 borderSize = border.rect.size;
                ____moveRoot.sizeDelta = borderSize;
                Vector2Int viewSize = AspectRatioController.ViewSize;
                float scale = Mathf.Max((float)viewSize.x / borderSize.x, (float)viewSize.y / borderSize.y);
                scaler.Min = new Vector3(scale, scale, 1f);
                Vector3 currentScale = ____moveRoot.localScale;
                ____moveRoot.localScale = new Vector3(Mathf.Clamp(currentScale.x, scale, scaler.Max.x), Mathf.Clamp(currentScale.y, scale, scaler.Max.y), 1f);
                scaler.Reset();
            }, delegate (string path)
            {
                throw new Exception("Cannot load block effect: (" + path + ")");
            });

            MethodInfo setNotScaleElementMethod = typeof(UI_BuildingArea).GetMethod("SetNotScaleElement", BindingFlags.NonPublic | BindingFlags.Instance);
            setNotScaleElementMethod.Invoke(__instance, null);
        }
    }

    // https://discussions.unity.com/t/modify-the-width-and-height-of-recttransform/551868/22
    public static class RectTransformExtensions
    {
        public static Vector2 GetSize(this RectTransform source) => source.rect.size;
        public static float GetWidth(this RectTransform source) => source.rect.size.x;
        public static float GetHeight(this RectTransform source) => source.rect.size.y;

        /// <summary>
        /// Sets the sources RT size to the same as the toCopy's RT size.
        /// </summary>
        public static void SetSize(this RectTransform source, RectTransform toCopy)
        {
            source.SetSize(toCopy.GetSize());
        }

        /// <summary>
        /// Sets the sources RT size to the same as the newSize.
        /// </summary>
        public static void SetSize(this RectTransform source, Vector2 newSize)
        {
            source.SetSize(newSize.x, newSize.y);
        }

        /// <summary>
        /// Sets the sources RT size to the new width and height.
        /// </summary>
        public static void SetSize(this RectTransform source, float width, float height)
        {
            source.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            source.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        public static void SetWidth(this RectTransform source, float width)
        {
            source.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }

        public static void SetHeight(this RectTransform source, float height)
        {
            source.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
    }
}
