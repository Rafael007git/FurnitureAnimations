using HarmonyLib;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityStandardAssets.Cameras;
using UnityStandardAssets.Utility;

namespace FurnitureAnimations
{
    [HarmonyPatch]
    public static class FurnitureMultiSeatPatches
    {
        private static Button _btnInspect;
        private static Button _btnClear;
        private static Furniture _inspectedFurniture;
        private static readonly System.Collections.Generic.Dictionary<CharacterCustomization, Vector3> _preEnterPositions
    = new System.Collections.Generic.Dictionary<CharacterCustomization, Vector3>();

        // 1. Посадка персонажа на мебель (поддержка добавления без высадки предыдущих)
        [HarmonyPatch(typeof(Furniture), nameof(Furniture.InitiateInteract))]
        [HarmonyPrefix]
        public static bool InitiateInteract_Prefix(Furniture __instance, CharacterCustomization customization)
        {
            if (__instance == null || customization == null) return true;

            // Запоминаем позицию персонажа ДО того, как игра/мод телепортируют его на мебель
            _preEnterPositions[customization] = customization.transform.position;

            if (FurnitureMultiSeatManager.IsAddingOccupant && FurnitureMultiSeatManager.HasOccupants(__instance))
            {
                FurnitureMultiSeatManager.IsAddingOccupant = false;
                FurnitureMultiSeatManager.CurrentUIFurniture = __instance;

                Vector3? prePos = _preEnterPositions.TryGetValue(customization, out var savedPos) ? savedPos : (Vector3?)null;
                _preEnterPositions.Remove(customization);

                FurnitureMultiSeatManager.RegisterOccupant(__instance, customization, prePos);

                if (__instance.poses != null && __instance.poses.items != null && __instance.poses.items.Count > 0)
                {
                    var firstItem = __instance.poses.items.FirstOrDefault(t => t != null);
                    var firstPose = firstItem != null ? firstItem.GetComponent<Pose>() : null;
                    if (firstPose != null)
                    {
                        FurnitureMultiSeatManager.ApplyPoseToActiveSlot(__instance, firstPose);
                        if (firstPose.loc != null)
                        {
                            customization.transform.position = firstPose.loc.transform.position;
                            customization.transform.rotation = firstPose.loc.transform.rotation;
                        }
                    }
                }

                FurnitureMultiSeatManager.SelectOccupant(__instance, customization);
                FurniturePoseSlotsUI.Refresh();
                FurniturePoseSlotsUI.SyncAnimationPanel();
                return false;
            }

            if (__instance.user != null && __instance.user != customization && !FurnitureMultiSeatManager.HasOccupants(__instance))
            {
                FurnitureMultiSeatManager.RegisterOccupant(__instance, __instance.user);
            }

            return true;
        }

        [HarmonyPatch(typeof(Furniture), nameof(Furniture.InitiateInteract))]
        [HarmonyPostfix]
        public static void InitiateInteract_Postfix(Furniture __instance, CharacterCustomization customization)
        {
            if (__instance == null || customization == null) return;

            FurnitureMultiSeatManager.IsAddingOccupant = false;
            FurnitureMultiSeatManager.CurrentUIFurniture = __instance;

            Vector3? prePos = _preEnterPositions.TryGetValue(customization, out var savedPos) ? savedPos : (Vector3?)null;
            _preEnterPositions.Remove(customization);

            FurnitureMultiSeatManager.RegisterOccupant(__instance, customization, prePos);

            if (__instance.poses != null && __instance.poses.items != null && __instance.poses.items.Count > 0)
            {
                var poses = __instance.poses.items
                    .Where(t => t != null)
                    .Select(t => t.GetComponent<Pose>())
                    .Where(p => p != null)
                    .ToList();

                if (poses.Count > 0)
                {
                    var activePose = poses.FirstOrDefault(p => p.gameObject.activeSelf) ?? poses.First();
                    FurnitureMultiSeatManager.ApplyPoseToActiveSlot(__instance, activePose);
                }
            }
        }

        // 2. Выбор ванильной позы обновляет якорь только активного персонажа
        [HarmonyPatch(typeof(Furniture), nameof(Furniture.DoPose))]
        [HarmonyPostfix]
        public static void DoPose_Postfix(Furniture __instance, Pose code)
        {
            if (__instance == null || code == null) return;
            FurnitureMultiSeatManager.ApplyPoseToActiveSlot(__instance, code);
        }

        // 3. Выход с мебели: компаньонки остаются сидеть, выходит только сам игрок
        [HarmonyPatch(typeof(Furniture), nameof(Furniture.QuitInteract))]
        [HarmonyPrefix]
        public static bool DoQuitInteraction_Prefix(Furniture __instance)
        {
            if (FurnitureMultiSeatManager.IsInternalQuitting) return true;
            if (__instance == null || !FurnitureMultiSeatManager.HasOccupants(__instance)) return true;

            var player = FurnitureMultiSeatManager.GetPlayerCustomization();
            if (player != null && FurnitureMultiSeatManager.IsCharacterSeated(__instance, player))
            {
                FurnitureMultiSeatManager.IsInspectingFurniture = false;
                var slots = FurnitureMultiSeatManager.GetSlots(__instance);
                if (slots.Count > 1)
                {
                    // Если вместе с игроком сидят компаньонки — высаживаем только игрока и возвращаем камеру/управление
                    FurnitureMultiSeatManager.RemoveOccupant(__instance, player);
                    ExitInspectMode();
                    return false;
                }
                else
                {
                    // Если сидел только сам игрок — очищаем наш слот и отдаём управление штатному Furniture.QuitInteract()
                    var slot = FurnitureMultiSeatManager.GetSlot(__instance, player);
                    if (slot != null && slot.SeatAnchor != null)
                    {
                        Object.Destroy(slot.SeatAnchor.gameObject);
                    }
                    slots.Clear();
                    __instance.user = player;
                    return true;
                }
            }

            if (FurnitureMultiSeatManager.IsInspectingFurniture)
            {
                ExitInspectMode();
                return false;
            }

            // Если сидят только компаньонки (без игрока) — оставляем их на мебели и просто закрываем UI
            ExitInspectMode();
            return false;
        }

        [HarmonyPatch(typeof(Global), nameof(Global.ButtonEscape))]
        [HarmonyPostfix]
        public static void GlobalButtonEscape_Postfix()
        {
            if (!FurnitureMultiSeatManager.IsInspectingFurniture) return;
            ExitInspectMode();
        }

        // 4. Поддержание слотов и настройка меню взаимодействия у занятой компаньонками мебели
        [HarmonyPatch(typeof(Player), "Update")]
        [HarmonyPostfix]
        public static void PlayerUpdate_Postfix(Player __instance)
        {
            FurnitureMultiSeatManager.MaintainAllOccupants();

            if (__instance == null || Global.code == null || Global.code.uiCombat == null) return;
            var uiCombat = Global.code.uiCombat;
            if (uiCombat.interactionOptionsPanel == null || !uiCombat.interactionOptionsPanel.activeSelf) return;

            var furniture = __instance.focusedInteraction != null
                ? __instance.focusedInteraction.GetComponent<Furniture>()
                : null;

            bool isCompanionSeated = furniture != null &&
                (FurnitureMultiSeatManager.HasOnlyCompanions(furniture) ||
                 (furniture.user != null && furniture.user != FurnitureMultiSeatManager.GetPlayerCustomization()));

            if (isCompanionSeated)
            {
                if (!FurnitureMultiSeatManager.HasOccupants(furniture) && furniture.user != null)
                {
                    var slot = FurnitureMultiSeatManager.RegisterOccupant(furniture, furniture.user);
                    if (slot != null) slot.IsAIControlled = true;
                }
                ConfigureOccupiedFurnitureMenu(uiCombat, furniture);
            }
            else
            {
                SetCustomButtonsVisible(false);
            }
        }

        // 5. Наполняем окно выбора компаньона при нажатии "+" в UIPose
        [HarmonyPatch(typeof(UIChooseCompanion), nameof(UIChooseCompanion.Open))]
        [HarmonyPostfix]
        public static void UIChooseCompanionOpen_Postfix(UIChooseCompanion __instance)
        {
            if (!FurnitureMultiSeatManager.IsAddingOccupant) return;
            var furniture = FurnitureMultiSeatManager.CurrentUIFurniture;
            if (furniture == null || __instance == null || __instance.iconHolderSelectionPanel == null || __instance.iconPrefab == null) return;

            if (__instance.iconHolderSelectionPanel != null && __instance.iconHolderSelectionPanel.parent != null)
            {
                __instance.iconHolderSelectionPanel.parent.gameObject.SetActive(true);
            }

            int addedCount = 0;

            // Добавляем главного героя, если он ещё не сидит на этой мебели
            var player = FurnitureMultiSeatManager.GetPlayerCustomization();
            if (player != null && !FurnitureMultiSeatManager.IsCharacterSeated(furniture, player))
            {
                var iconTransform = Object.Instantiate(__instance.iconPrefab, __instance.iconHolderSelectionPanel);
                iconTransform.localScale = Vector3.one;
                iconTransform.GetComponent<CompanionSelectionIcon>()?.Initiate(player.transform, true);
                addedCount++;
            }

            // Добавляем всех живых компаньонов, которые ещё не сидят на этой мебели
            if (Global.code != null && Global.code.companions != null && Global.code.companions.items != null)
            {
                foreach (Transform compTransform in Global.code.companions.items)
                {
                    if (compTransform == null) continue;
                    var id = compTransform.GetComponent<ID>();
                    if (id != null && id.isDead) continue;

                    var cc = compTransform.GetComponent<CharacterCustomization>();
                    if (cc == null || FurnitureMultiSeatManager.IsCharacterSeated(furniture, cc)) continue;

                    var iconTransform = Object.Instantiate(__instance.iconPrefab, __instance.iconHolderSelectionPanel);
                    iconTransform.localScale = Vector3.one;
                    iconTransform.GetComponent<CompanionSelectionIcon>()?.Initiate(compTransform, true);
                    addedCount++;
                }
            }

            if (__instance.txtNoAvailable != null)
            {
                __instance.txtNoAvailable.text = addedCount > 0 ? "" : "No Available Succubus.";
            }
        }

        private static void ConfigureOccupiedFurnitureMenu(UICombat uiCombat, Furniture furniture)
        {
            EnsureCustomButtons(uiCombat);
            _inspectedFurniture = furniture;

            if (uiCombat.btnInteractionInteract != null)
                uiCombat.btnInteractionInteract.SetActive(false);

            // Скрываем кнопку Destroy на занятой мебели, оставляя Relocate
            foreach (Transform child in uiCombat.interactionOptionsPanel.transform)
            {
                if (child.name.IndexOf("Destroy", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    child.gameObject.SetActive(false);
                }
            }

            SetCustomButtonsVisible(true);
        }

        public static void ExitInspectMode()
        {
            FurnitureMultiSeatManager.IsInspectingFurniture = false;
            var furniture = FurnitureMultiSeatManager.CurrentUIFurniture;

            if (Global.code != null)
            {
                if (Global.code.freeCamera != null) Global.code.freeCamera.SetActive(false);
                if (Global.code.freeCameraCollider != null) Global.code.freeCameraCollider.SetActive(false);
                if (Global.code.uiPose != null)
                {
                    if (Global.code.uiPose.freecameraTips != null)
                        Global.code.uiPose.freecameraTips.SetActive(false);
                    if (Global.code.uiPose.gameObject.activeSelf)
                        Global.code.uiPose.Close();
                }
                if (Global.code.uiCombat != null)
                {
                    if (Global.code.uiCombat.hud != null) Global.code.uiCombat.hud.SetActive(true);
                    if (Global.code.uiCombat.buffGroup != null) Global.code.uiCombat.buffGroup.gameObject.SetActive(true);
                }
            }

            if (Player.code != null)
            {
                Player.code.ResetEmotion();
                var rotator = Player.code.GetComponent<SimpleMouseRotator>();
                if (rotator != null)
                {
                    rotator.enabled = false;
                    rotator.resetRotation();
                }
            }

            if (furniture != null)
            {
                if (!furniture.donthideMainCamera && FreeLookCam.code != null)
                    FreeLookCam.code.gameObject.SetActive(true);
                if (furniture.camerasGroup != null)
                    furniture.camerasGroup.gameObject.SetActive(false);
            }
            else if (FreeLookCam.code != null)
            {
                FreeLookCam.code.gameObject.SetActive(true);
            }
        }

        private static void EnsureCustomButtons(UICombat uiCombat)
        {
            if (_btnInspect != null && _btnClear != null) return;
            if (uiCombat.btnInteractionInteract == null || uiCombat.interactionOptionsPanel == null) return;

            var template = uiCombat.btnInteractionInteract;
            var parent = uiCombat.interactionOptionsPanel.transform;

            var inspectGo = Object.Instantiate(template, parent);
            inspectGo.name = "Btn_FA_Inspect";
            inspectGo.transform.SetSiblingIndex(0);
            _btnInspect = inspectGo.GetComponent<Button>();
            SetButtonLabel(inspectGo, "_FA_Inspect_", "Inspect", "Осмотреть");
            DisablePersistentListeners(_btnInspect);
            _btnInspect.onClick.RemoveAllListeners();
            _btnInspect.onClick.AddListener(OnInspectClicked);

            var clearGo = Object.Instantiate(template, parent);
            clearGo.name = "Btn_FA_Clear";
            clearGo.transform.SetSiblingIndex(1);
            _btnClear = clearGo.GetComponent<Button>();
            SetButtonLabel(clearGo, "_FA_Clear_", "Clear", "Освободить");
            DisablePersistentListeners(_btnClear);
            _btnClear.onClick.RemoveAllListeners();
            _btnClear.onClick.AddListener(OnClearClicked);
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

        private static void SetButtonLabel(GameObject buttonGo, string key, string enLabel, string ruLabel)
        {
            if (!Localization.LocalizationDic.ContainsKey(key))
            {
                Localization.LocalizationDic[key] = new System.Collections.Generic.List<string>
                {
                    enLabel, enLabel, ruLabel, enLabel
                };
            }

            var locText = buttonGo.GetComponentInChildren<LocalizationText>(true);
            if (locText != null)
            {
                locText.KEY = key;
            }

            var text = buttonGo.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = Localization.GetContent(key);
            }
        }

        private static void SetCustomButtonsVisible(bool visible)
        {
            if (_btnInspect != null) _btnInspect.gameObject.SetActive(visible);
            if (_btnClear != null) _btnClear.gameObject.SetActive(visible);
        }

        private static void OnInspectClicked()
        {
            var uiCombat = Global.code != null ? Global.code.uiCombat : null;
            if (uiCombat != null && uiCombat.interactionOptionsPanel != null)
            {
                uiCombat.interactionOptionsPanel.SetActive(false);
            }

            SetCustomButtonsVisible(false);

            var furniture = _inspectedFurniture;
            if (furniture == null || !FurnitureMultiSeatManager.HasOccupants(furniture)) return;

            var active = FurnitureMultiSeatManager.GetActiveOccupant(furniture);
            if (active == null) return;

            FurnitureMultiSeatManager.IsInspectingFurniture = true;
            FurnitureMultiSeatManager.TakeManualControl(furniture);
            FurnitureMultiSeatManager.CurrentUIFurniture = furniture;
            furniture.user = active;

            // Переводим камеру и HUD в режим осмотра мебели (зеркально ExitInspectMode)
            if (uiCombat != null)
            {
                if (uiCombat.hud != null) uiCombat.hud.SetActive(false);
                if (uiCombat.buffGroup != null) uiCombat.buffGroup.gameObject.SetActive(false);
            }

            if (!furniture.donthideMainCamera && FreeLookCam.code != null)
            {
                FreeLookCam.code.gameObject.SetActive(false);
            }

            if (furniture.camerasGroup != null)
            {
                furniture.camerasGroup.gameObject.SetActive(true);
            }

            if (Player.code != null)
            {
                var rotator = Player.code.GetComponent<SimpleMouseRotator>();
                if (rotator != null)
                {
                    rotator.enabled = true;
                }
            }

            if (Global.code != null && Global.code.uiPose != null)
            {
                Global.code.uiPose.Open(furniture, active);
            }
        }

        private static void OnClearClicked()
        {
            var uiCombat = Global.code != null ? Global.code.uiCombat : null;
            if (uiCombat != null && uiCombat.interactionOptionsPanel != null)
            {
                uiCombat.interactionOptionsPanel.SetActive(false);
            }

            SetCustomButtonsVisible(false);

            if (_inspectedFurniture != null)
            {
                FurnitureMultiSeatManager.ClearFurniture(_inspectedFurniture);
                _inspectedFurniture = null;
            }
        }
    }
}
