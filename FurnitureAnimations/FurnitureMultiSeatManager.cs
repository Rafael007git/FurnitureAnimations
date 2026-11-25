using FurnitureAnimationsMod;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FurnitureAnimations
{
    // Менеджер многослотовой посадки персонажей на мебель
    public static class FurnitureMultiSeatManager
    {
        public class SeatSlot
        {
            public CharacterCustomization Character;
            public Vector3 EnterPosition;
            public Transform SeatAnchor;
            public Pose ActiveVanillaPose;
            public bool IsAIControlled;
        }

        // Реестр всех занятых предметов мебели и их слотов
        private static readonly Dictionary<Furniture, List<SeatSlot>> _furnitureSlots = new Dictionary<Furniture, List<SeatSlot>>();

        // Текущий активный (выбранный в UI) персонаж для каждой мебели
        private static readonly Dictionary<Furniture, CharacterCustomization> _activeOccupants = new Dictionary<Furniture, CharacterCustomization>();

        // Флаг, указывающий, что следующая посадка добавляет персонажа к уже сидящим, а не заменяет их
        public static bool IsAddingOccupant { get; set; }

        // Флаг режима инспекции мебели (Inspect)
        public static bool IsInspectingFurniture { get; set; }

        // Флаг внутренней очистки/высадки, чтобы не зациклить патчи
        public static bool IsInternalQuitting { get; private set; }

        // Чтобы все части UI всегда знали, с какой мебелью мы сейчас работаем
        public static Furniture CurrentUIFurniture { get; set; }

        // Возвращает CharacterCustomization главного героя
        public static CharacterCustomization GetPlayerCustomization()
        {
            return Player.code != null ? Player.code.customization : null;
        }

        // Возвращает список слотов для указанной мебели (очищая уничтоженные ссылки)
        public static List<SeatSlot> GetSlots(Furniture furniture)
        {
            if (furniture == null) return new List<SeatSlot>();

            if (!_furnitureSlots.TryGetValue(furniture, out var list))
            {
                list = new List<SeatSlot>();
                _furnitureSlots[furniture] = list;
            }
            else
            {
                list.RemoveAll(s => s == null || s.Character == null);
            }

            return list;
        }

        // Проверяет, есть ли на мебели хотя бы один персонаж
        public static bool HasOccupants(Furniture furniture)
        {
            return GetSlots(furniture).Count > 0;
        }

        // Проверяет, сидит ли на мебели только компаньон(ы) без главного героя
        public static bool HasOnlyCompanions(Furniture furniture)
        {
            var slots = GetSlots(furniture);
            if (slots.Count == 0) return false;

            var player = GetPlayerCustomization();
            foreach (var slot in slots)
            {
                if (player != null && slot.Character == player)
                    return false;
            }
            return true;
        }

        // Проверяет, сидит ли конкретный персонаж на данной мебели
        public static bool IsCharacterSeated(Furniture furniture, CharacterCustomization character)
        {
            if (furniture == null || character == null) return false;
            return GetSlots(furniture).Exists(s => s.Character == character);
        }

        // Возвращает текущего выбранного в UI персонажа для мебели
        public static CharacterCustomization GetActiveOccupant(Furniture furniture)
        {
            if (furniture == null) return null;

            var slots = GetSlots(furniture);
            if (slots.Count == 0) return null;

            if (_activeOccupants.TryGetValue(furniture, out var active) && active != null)
            {
                if (slots.Exists(s => s.Character == active))
                    return active;
            }

            var firstChar = slots.First().Character;
            _activeOccupants[furniture] = firstChar;
            return firstChar;
        }

        // Возвращает слот конкретного персонажа на мебели
        public static SeatSlot GetSlot(Furniture furniture, CharacterCustomization character)
        {
            if (furniture == null || character == null) return null;
            return GetSlots(furniture).Find(s => s.Character == character);
        }

        // Регистрирует или обновляет слот персонажа на мебели и создаёт персональный якорь
        public static SeatSlot RegisterOccupant(Furniture furniture, CharacterCustomization character, Vector3? customEnterPos = null)
        {
            if (!IsWorldFurniture(furniture) || character == null) return null;

            var slots = GetSlots(furniture);
            var existing = slots.Find(s => s.Character == character);
            if (existing != null)
            {
                _activeOccupants[furniture] = character;
                return existing;
            }

            var anchorGo = new GameObject($"FA_SeatAnchor_{character.name}");
            anchorGo.transform.SetParent(furniture.transform, false);
            anchorGo.transform.position = character.transform.position;
            anchorGo.transform.rotation = character.transform.rotation;

            var slot = new SeatSlot
            {
                Character = character,
                EnterPosition = customEnterPos ?? character.transform.position,
                SeatAnchor = anchorGo.transform
            };

            slots.Add(slot);
            _activeOccupants[furniture] = character;
            furniture.user = character;
            character.interactingObject = furniture.transform;
            character.curInteractionLoc = slot.SeatAnchor;

            Plugin.Log?.LogInfo($"[MultiSeat] Registered '{character.name}' on '{furniture.name}'. Total occupants: {slots.Count}");
            return slot;
        }

        // Обновляет якорь активного слота под выбранную ванильную позу, не сбивая позы соседей
        public static void ApplyPoseToActiveSlot(Furniture furniture, Pose pose)
        {
            if (!IsWorldFurniture(furniture) || pose == null) return;

            var active = GetActiveOccupant(furniture);
            var slot = GetSlot(furniture, active);
            if (slot == null) return;

            slot.ActiveVanillaPose = pose;
            if (slot.SeatAnchor != null && pose.loc != null)
            {
                slot.SeatAnchor.position = pose.loc.transform.position;
                slot.SeatAnchor.rotation = pose.loc.transform.rotation;
            }

            if (slot.Character != null)
            {
                slot.Character.interactingObject = furniture.transform;
                slot.Character.curInteractionLoc = slot.SeatAnchor;
            }
        }

        // Переключает фокус UI на указанного персонажа без сброса анимаций других сидящих
        public static void SelectOccupant(Furniture furniture, CharacterCustomization character)
        {
            if (furniture == null || character == null) return;

            var slot = GetSlot(furniture, character);
            if (slot == null) return;

            _activeOccupants[furniture] = character;
            furniture.user = character;

            var uiPose = Global.code != null ? Global.code.uiPose : null;
            if (uiPose != null && uiPose.gameObject.activeInHierarchy)
            {
                uiPose.curCustomization = character;
                if (uiPose.nameinput != null)
                {
                    uiPose.nameinput.text = character.characterName;
                }
            }

            Plugin.Log?.LogInfo($"[MultiSeat] Switched UI focus to '{character.name}' on '{furniture.name}'.");
        }

        // Поддерживает корректную привязку всех сидящих персонажей (защита от срыва AI)
        public static void MaintainAllOccupants()
        {
            if (_furnitureSlots.Count == 0 || IsAnyCharacterScreenOpen()) return;

            List<Furniture> emptyFurnitures = null;

            foreach (var kvp in _furnitureSlots)
            {
                var furniture = kvp.Key;
                var slots = kvp.Value;

                if (furniture == null)
                {
                    if (emptyFurnitures == null) emptyFurnitures = new List<Furniture>();
                    emptyFurnitures.Add(furniture);
                    continue;
                }

                slots.RemoveAll(s => s == null || s.Character == null || (s.IsAIControlled && s.Character.interactingObject != furniture.transform));
                if (slots.Count == 0)
                {
                    if (emptyFurnitures == null) emptyFurnitures = new List<Furniture>();
                    emptyFurnitures.Add(furniture);
                    continue;
                }

                foreach (var slot in slots)
                {
                    if (slot.IsAIControlled) continue;
                    var ch = slot.Character;
                    if (ch.interactingObject != furniture.gameObject)
                        ch.interactingObject = furniture.transform;

                    if (slot.SeatAnchor != null && ch.curInteractionLoc != slot.SeatAnchor)
                        ch.curInteractionLoc = slot.SeatAnchor;
                }
            }

            if (emptyFurnitures != null)
            {
                foreach (var f in emptyFurnitures)
                {
                    _furnitureSlots.Remove(f);
                    _activeOccupants.Remove(f);
                }
            }
        }

        // Высаживает одного конкретного персонажа с мебели
        public static void RemoveOccupant(Furniture furniture, CharacterCustomization character)
        {
            if (furniture == null || character == null) return;

            var slots = GetSlots(furniture);
            var slot = slots.Find(s => s.Character == character);

            IsInternalQuitting = true;
            try
            {
                var playerComp = character.GetComponent<FurnitureAnimationPlayer>();
                if (playerComp != null)
                {
                    Object.Destroy(playerComp);
                }

                Vector3 targetExitPos = slot != null ? slot.EnterPosition : character.transform.position;
                if (Player.code != null && character != Player.code.customization)
                {
                    int slotIndex = slot != null ? slots.IndexOf(slot) : 0;
                    float sideOffset = (slotIndex + 1) * 0.8f;
                    targetExitPos = Player.code.transform.position + Player.code.transform.right * sideOffset;
                }

                if (slot != null)
                {
                    if (slot.SeatAnchor != null)
                    {
                        Object.Destroy(slot.SeatAnchor.gameObject);
                    }
                    slots.Remove(slot);
                }

                // Временно подставляем высаживаемого персонажа в furniture.user и вызываем штатный DoQuitInteraction
                furniture.user = character;
                var enterPosField = HarmonyLib.AccessTools.Field(typeof(Furniture), "enterPosition");
                enterPosField?.SetValue(furniture, targetExitPos);

                var doQuitMethod = HarmonyLib.AccessTools.Method(typeof(Furniture), "DoQuitInteraction");
                doQuitMethod?.Invoke(furniture, null);

                // На случай, если DoQuitInteraction не вернул позицию точно в targetExitPos
                character.transform.position = targetExitPos;

                if (slots.Count > 0)
                {
                    var next = slots.First().Character;
                    _activeOccupants[furniture] = next;
                    furniture.user = next;
                    if (furniture.posesGroup != null) furniture.posesGroup.gameObject.SetActive(true);
                }
                else
                {
                    _furnitureSlots.Remove(furniture);
                    _activeOccupants.Remove(furniture);
                    furniture.user = null;
                }

                Plugin.Log?.LogInfo($"[MultiSeat] Removed '{character.name}' from '{furniture.name}'. Remaining: {slots.Count}");
            }
            finally
            {
                IsInternalQuitting = false;
            }
        }

        // Переводит все слоты мебели под ручной контроль игрока (блокирует таймер выхода CastleAI)
        public static void TakeManualControl(Furniture furniture)
        {
            if (furniture == null) return;
            foreach (var slot in GetSlots(furniture))
            {
                slot.IsAIControlled = false;
            }
        }

        // Полностью освобождает мебель от всех сидящих персонажей
        public static void ClearFurniture(Furniture furniture)
        {
            if (furniture == null) return;

            var slots = new List<SeatSlot>(GetSlots(furniture));
            foreach (var slot in slots)
            {
                if (slot != null && slot.Character != null)
                {
                    RemoveOccupant(furniture, slot.Character);
                }
            }

            _furnitureSlots.Remove(furniture);
            _activeOccupants.Remove(furniture);
            furniture.user = null;

            Plugin.Log?.LogInfo($"[MultiSeat] Cleared all occupants from '{furniture.name}'.");
        }

        // Проверяет, открыт ли полноэкранный UI персонажа (кастомизация, гардероб, макияж, инвентарь)
        public static bool IsAnyCharacterScreenOpen()
        {
            if (Global.code == null) return false;
            if (Global.code.uiFreePose != null && Global.code.uiFreePose.gameObject.activeInHierarchy) return true;
            if (Global.code.uiCustomization != null && Global.code.uiCustomization.gameObject.activeInHierarchy) return true;
            if (Global.code.uiCloset != null && Global.code.uiCloset.gameObject.activeInHierarchy) return true;
            if (Global.code.uiMakeup != null && Global.code.uiMakeup.gameObject.activeInHierarchy) return true;
            if (Global.code.uiCharacter != null && Global.code.uiCharacter.gameObject.activeInHierarchy) return true;
            if (Global.code.uiInventory != null && Global.code.uiInventory.gameObject.activeInHierarchy) return true;
            return false;
        }

        // Отличает реальную мебель в замке от служебных UI-подиумов (кастомизация, макияж, гардероб)
        public static bool IsWorldFurniture(Furniture furniture)
        {
            if (furniture == null) return false;
            if (IsAnyCharacterScreenOpen()) return false;

            if (furniture.GetComponent<MakeupTable>() != null)
                return false;

            if (Global.code != null)
            {
                if (furniture.transform == Global.code.GetCustomizationFurniture() ||
                    furniture.transform == Global.code.GetMakeupTable())
                {
                    return false;
                }

                if (Global.code.uiCloset != null && Global.code.uiCloset.lingerieShop != null)
                {
                    var shop = Global.code.uiCloset.lingerieShop;
                    if (furniture.transform == shop.brafurniture ||
                        furniture.transform == shop.pantiesfurniture ||
                        furniture.transform == shop.stockingfurniture ||
                        furniture.transform == shop.heelsfurniture ||
                        furniture.transform == shop.allfurniture)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

    }
}
