using System.Collections.Generic;
using FurnitureAnimationsMod;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FurnitureAnimations
{
    // Портретная колонка персонажей в меню мебели UIPose и синхронизация панели анимаций
    [HarmonyPatch]
    public static class FurniturePoseSlotsUI
    {
        private static Transform _slotsContainer;
        private static GameObject _addSlotTemplate;
        private static readonly List<GameObject> _spawnedIcons = new List<GameObject>();

        // Обновляем колонку портретов и синхронизируем панель при открытии UIPose
        [HarmonyPatch(typeof(UIPose), nameof(UIPose.Open))]
        [HarmonyPostfix]
        public static void UIPoseOpen_Postfix(UIPose __instance, Furniture furniture, CharacterCustomization customization)
        {
            if (__instance == null || furniture == null) return;

            FurnitureMultiSeatManager.CurrentUIFurniture = furniture;
            if (customization != null)
            {
                FurnitureMultiSeatManager.RegisterOccupant(furniture, customization);
                FurnitureMultiSeatManager.SelectOccupant(furniture, customization);
            }

            Refresh(__instance);
            SyncAnimationPanel(__instance);
        }

        // Перехватываем клик по нативной иконке (сохраняя mouseover и звук игры)
        [HarmonyPatch(typeof(FreeposeCompanionIcon), nameof(FreeposeCompanionIcon.Click))]
        [HarmonyPrefix]
        public static bool FreeposeIconClick_Prefix(FreeposeCompanionIcon __instance)
        {
            var uiPose = Global.code != null ? Global.code.uiPose : null;
            if (uiPose != null && uiPose.gameObject.activeInHierarchy)
            {
                var furniture = uiPose.curFurniture ?? FurnitureMultiSeatManager.CurrentUIFurniture;
                if (furniture != null)
                {
                    if (__instance != null && __instance.customization != null)
                    {
                        OnPortraitClicked(furniture, __instance.customization);
                    }
                    else
                    {
                        OnAddSlotClicked(furniture);
                    }
                }
                return false;
            }

            return true;
        }

        // В режиме Free Pose при клике по портрету обновляем панель управления анимацией
        [HarmonyPatch(typeof(FreeposeCompanionIcon), nameof(FreeposeCompanionIcon.Click))]
        [HarmonyPostfix]
        public static void FreeposeIconClick_Postfix(FreeposeCompanionIcon __instance)
        {
            if (__instance == null || __instance.customization == null) return;
            var uiFreePose = Global.code != null ? Global.code.uiFreePose : null;
            if (uiFreePose == null || !uiFreePose.gameObject.activeInHierarchy) return;

            var controls = uiFreePose.GetComponentInChildren<AnimationUiControls>(true);
            controls?.ForceRefreshForCurrentCharacter(__instance.customization);
        }

        // Полная перерисовка колонки портретов в UIPose
        public static void Refresh(UIPose uiPose = null)
        {
            if (uiPose == null)
                uiPose = Global.code != null ? Global.code.uiPose : null;
            if (uiPose == null || !uiPose.gameObject.activeInHierarchy) return;

            var furniture = uiPose.curFurniture ?? FurnitureMultiSeatManager.CurrentUIFurniture;
            if (furniture == null) return;

            EnsureSlotsContainer(uiPose);
            if (_slotsContainer == null) return;

            ApplyContainerPosition();

            var uiFreePose = Global.code != null ? Global.code.uiFreePose : null;
            if (uiFreePose == null || uiFreePose.companionIconPrefab == null) return;

            foreach (var go in _spawnedIcons)
            {
                if (go != null) Object.Destroy(go);
            }
            _spawnedIcons.Clear();

            var slots = FurnitureMultiSeatManager.GetSlots(furniture);
            var activeChar = uiPose.curCustomization ?? FurnitureMultiSeatManager.GetActiveOccupant(furniture);

            // 1. Портретные слоты для всех сидящих на мебели персонажей
            foreach (var slot in slots)
            {
                if (slot == null || slot.Character == null) continue;
                var character = slot.Character;

                var iconObj = Object.Instantiate(uiFreePose.companionIconPrefab.gameObject, _slotsContainer);
                iconObj.SetActive(true);
                iconObj.transform.localScale = Vector3.one;
                _spawnedIcons.Add(iconObj);

                var compIcon = iconObj.GetComponent<FreeposeCompanionIcon>();
                if (compIcon != null)
                {
                    compIcon.customization = character;
                    if (compIcon.icon != null)
                    {
                        compIcon.icon.gameObject.SetActive(true);
                        compIcon.icon.texture = character.icon;
                    }
                    if (compIcon.selection != null)
                    {
                        compIcon.selection.SetActive(character == activeChar);
                    }
                    if (compIcon.buttonDelete != null)
                    {
                        compIcon.buttonDelete.SetActive(true);
                        var delBtn = compIcon.buttonDelete.GetComponentInChildren<Button>(true);
                        if (delBtn != null)
                        {
                            DisablePersistentListeners(delBtn);
                            delBtn.onClick.RemoveAllListeners();
                            var capturedCharForDel = character;
                            var capturedFurnForDel = furniture;
                            delBtn.onClick.AddListener(() => OnDeleteSlotClicked(capturedFurnForDel, capturedCharForDel));
                        }
                    }
                }
            }

            // 2. Слот "+" для добавления нового персонажа на мебель
            var addPrefab = _addSlotTemplate != null ? _addSlotTemplate : uiFreePose.companionIconPrefab.gameObject;
            var addObj = Object.Instantiate(addPrefab, _slotsContainer);
            addObj.SetActive(true);
            addObj.transform.localScale = Vector3.one;
            _spawnedIcons.Add(addObj);

            var addIcon = addObj.GetComponent<FreeposeCompanionIcon>();
            if (addIcon != null)
            {
                addIcon.customization = null;
                if (addIcon.selection != null)
                {
                    addIcon.selection.SetActive(false);
                }
                if (addIcon.buttonDelete != null)
                {
                    addIcon.buttonDelete.SetActive(false);
                }
            }
            else
            {
                // Если в шаблоне плюса нет FreeposeCompanionIcon — вешаем клик на его родную кнопку, не трогая EventTrigger
                var addBtn = addObj.GetComponentInChildren<Button>(true);
                if (addBtn != null)
                {
                    DisablePersistentListeners(addBtn);
                    addBtn.onClick.RemoveAllListeners();
                    var capturedFurnitureForAdd = furniture;
                    addBtn.onClick.AddListener(() => OnAddSlotClicked(capturedFurnitureForAdd));
                }
            }
        }

        public static void SyncAnimationPanel(UIPose uiPose = null)
        {
            if (uiPose == null)
                uiPose = Global.code != null ? Global.code.uiPose : null;
            if (uiPose == null) return;

            var controls = uiPose.GetComponentInChildren<AnimationUiControls>(true);
            controls?.ForceRefreshForCurrentCharacter();
        }

        private static void EnsureSlotsContainer(UIPose uiPose)
        {
            if (_slotsContainer != null) return;

            var uiFreePose = Global.code != null ? Global.code.uiFreePose : null;
            if (uiFreePose == null || uiFreePose.companionIconHolder == null) return;

            if (_addSlotTemplate == null && uiFreePose.companionIconHolder.childCount > 1)
            {
                _addSlotTemplate = Object.Instantiate(uiFreePose.companionIconHolder.GetChild(1).gameObject, uiPose.transform);
                _addSlotTemplate.name = "FA_AddSlotTemplate";
                _addSlotTemplate.SetActive(false);
            }

            var clonedHolder = Object.Instantiate(uiFreePose.companionIconHolder.gameObject, uiPose.transform);
            clonedHolder.name = "FA_PoseCompanionSlots";
            clonedHolder.SetActive(true);
            clonedHolder.transform.localScale = Vector3.one;

            for (int i = clonedHolder.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(clonedHolder.transform.GetChild(i).gameObject);
            }

            var vlg = clonedHolder.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.childAlignment = TextAnchor.UpperCenter;
            }

            _slotsContainer = clonedHolder.transform;
        }

        private static void ApplyContainerPosition()
        {
            if (_slotsContainer == null) return;

            _slotsContainer.gameObject.SetActive(true);
            _slotsContainer.localScale = Vector3.one;

            float offsetX = Plugin.SlotsPanelOffsetX != null ? Plugin.SlotsPanelOffsetX.Value : 380f;
            float offsetY = Plugin.SlotsPanelOffsetY != null ? Plugin.SlotsPanelOffsetY.Value : -80f;

            var rect = _slotsContainer.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(90f, 600f);
                rect.anchoredPosition = new Vector2(offsetX, offsetY);
            }
        }
        private static void DisablePersistentListeners(Button button)
        {
            if (button == null) return;
            int count = button.onClick.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                button.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            }
        }
        private static void OnPortraitClicked(Furniture furniture, CharacterCustomization character)
        {
            if (furniture == null || character == null) return;
            FurnitureMultiSeatManager.TakeManualControl(furniture);
            FurnitureMultiSeatManager.SelectOccupant(furniture, character);
            var uiPose = Global.code != null ? Global.code.uiPose : null;
            if (uiPose != null)
            {
                uiPose.curCustomization = character;
                if (uiPose.nameinput != null)
                    uiPose.nameinput.text = character.characterName;
                Refresh(uiPose);
                SyncAnimationPanel(uiPose);
            }
        }
        private static void OnDeleteSlotClicked(Furniture furniture, CharacterCustomization character)
        {
            if (furniture == null || character == null) return;
            FurnitureMultiSeatManager.RemoveOccupant(furniture, character);
            var uiPose = Global.code != null ? Global.code.uiPose : null;
            if (!FurnitureMultiSeatManager.HasOccupants(furniture))
            {
                if (FurnitureMultiSeatManager.IsInspectingFurniture)
                {
                    FurnitureMultiSeatPatches.ExitInspectMode();
                }
                else if (uiPose != null && uiPose.gameObject.activeSelf)
                {
                    uiPose.Close();
                }
                return;
            }
            var next = FurnitureMultiSeatManager.GetActiveOccupant(furniture);
            if (next != null && uiPose != null)
            {
                FurnitureMultiSeatManager.SelectOccupant(furniture, next);
                uiPose.curCustomization = next;
                if (uiPose.nameinput != null)
                    uiPose.nameinput.text = next.characterName;
                Refresh(uiPose);
                SyncAnimationPanel(uiPose);
            }
        }
        private static void OnAddSlotClicked(Furniture furniture)
        {
            Plugin.Log?.LogInfo($"[MultiSeatUI] Add '+' slot clicked for furniture '{furniture?.name}'.");
            if (furniture == null) return;
            FurnitureMultiSeatManager.TakeManualControl(furniture);
            FurnitureMultiSeatManager.IsAddingOccupant = true;
            FurnitureMultiSeatManager.CurrentUIFurniture = furniture;
            if (Global.code != null && Global.code.uiChooseCompanion != null)
            {
                Global.code.uiChooseCompanion.Open();
            }
        }
    }
}