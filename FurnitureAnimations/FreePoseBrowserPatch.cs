using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FurnitureAnimationsMod
{
    [HarmonyPatch]
    public static class FreePoseBrowserPatch
    {
        private static readonly HashSet<string> _registeredAnimations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _activePreviewAnimName = null;
        private static CharacterCustomization _activePreviewChar = null;

        private static string AnimationsFolder => Path.Combine(BepInEx.Paths.PluginPath, "PoseAnimations");

        private static void EnsureAnimationsLoaded()
        {
            if (RM.code == null || RM.code.allFreePoses == null || RM.code.allFreePoses.items == null) return;
            if (!Directory.Exists(AnimationsFolder)) return;

            Transform template = RM.code.allFreePoses.items.FirstOrDefault();
            if (template == null) return;

            string[] files = Directory.GetFiles(AnimationsFolder, "*.json");
            int addedCount = 0;

            foreach (string file in files)
            {
                string animName = Path.GetFileNameWithoutExtension(file);
                if (_registeredAnimations.Contains(animName)) continue;

                try
                {
                    GameObject poseObj = UnityEngine.Object.Instantiate(template.gameObject);
                    poseObj.name = animName;
                    UnityEngine.Object.DontDestroyOnLoad(poseObj);

                    Pose poseComp = poseObj.GetComponent<Pose>();
                    if (poseComp != null)
                    {
                        poseComp.categoryName = "Animations";

                        string iconPath = Path.Combine(AnimationsFolder, "icons", $"{animName}.png");
                        if (!File.Exists(iconPath))
                            iconPath = Path.Combine(AnimationsFolder, $"{animName}.png");

                        if (File.Exists(iconPath))
                        {
                            byte[] bytes = File.ReadAllBytes(iconPath);
                            Texture2D tex = new Texture2D(2, 2);
                            if (tex.LoadImage(bytes))
                            {
                                poseComp.icon = tex;
                            }
                        }
                    }

                    RM.code.allFreePoses.AddItem(poseObj.transform);
                    _registeredAnimations.Add(animName);
                    addedCount++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[FreePoseBrowser] Ошибка регистрации '{animName}': {ex.Message}");
                }
            }

            if (addedCount > 0)
            {
                Plugin.Log.LogInfo($"[FreePoseBrowser] Загружено анимаций в Free Pose: {addedCount}");
            }
        }

        // 1. При открытии UIFreePose загружаем файлы и добавляем категорию "Animations"
        [HarmonyPatch(typeof(UIFreePose), "Open")]
        [HarmonyPrefix]
        public static void BeforeFreePoseOpen(UIFreePose __instance)
        {
            EnsureAnimationsLoaded();

            if (__instance != null && __instance.categoryDropdown != null)
            {
                bool hasOption = __instance.categoryDropdown.options.Any(o => o.text == "Animations");
                if (!hasOption)
                {
                    __instance.categoryDropdown.options.Add(new Dropdown.OptionData("Animations"));
                }
            }
        }

        // 2. После отрисовки списка иконок в UIFreePose привязываем клик к нашим анимациям (и стоп к обычным позам)
        [HarmonyPatch(typeof(UIFreePose), "Refresh")]
        [HarmonyPostfix]
        public static void AfterFreePoseRefresh(UIFreePose __instance)
        {
            if (__instance == null || __instance.poseIconPrefab == null || __instance.poseIconPrefab.parent == null) return;

            Button[] buttons = __instance.poseIconPrefab.parent.GetComponentsInChildren<Button>(true);
            foreach (Button btn in buttons)
            {
                string itemName = btn.gameObject.name.Replace("(Clone)", "").Trim();

                if (_registeredAnimations.Contains(itemName))
                {
                    btn.onClick.RemoveAllListeners();
                    string capturedName = itemName;
                    btn.onClick.AddListener(() => HandleAnimationIconClick(__instance, capturedName));
                }
            }
        }

        private static void HandleAnimationIconClick(UIFreePose uiFreePose, string animName)
        {
            Transform selectedCharTrans = uiFreePose.selectedCharacter;
            CharacterCustomization targetChar = selectedCharTrans != null ? selectedCharTrans.GetComponent<CharacterCustomization>() : null;
            if (targetChar == null) return;

            // Повторный клик по той же иконке — стоп
            if (_activePreviewChar == targetChar &&
                string.Equals(_activePreviewAnimName, animName, StringComparison.OrdinalIgnoreCase) &&
                FurnitureAnimationPlayer.Instance != null)
            {
                FurnitureAnimAPI.StopPreview(targetChar);
                _activePreviewChar = null;
                _activePreviewAnimName = null;

                var ctrl = uiFreePose.gameObject.GetComponent<AnimationUiControls>();
                if (ctrl != null) ctrl.HidePanel();
                return;
            }

            // Иначе запускаем превью
            string jsonPath = Path.Combine(AnimationsFolder, $"{animName}.json");
            if (!File.Exists(jsonPath)) return;

            try
            {
                string json = File.ReadAllText(jsonPath);
                PoseAnimationData data = JsonConvert.DeserializeObject<PoseAnimationData>(json);
                if (data == null || data.deltas == null || data.deltas.Count == 0) return;

                data.name = animName;
                FurnitureAnimAPI.StartPreview(targetChar, data, 1.0f);
                _activePreviewChar = targetChar;
                _activePreviewAnimName = animName;

                var controls = uiFreePose.gameObject.GetComponent<AnimationUiControls>()
                               ?? uiFreePose.gameObject.AddComponent<AnimationUiControls>();
                controls.InitializeFreePose(uiFreePose);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[FreePoseBrowser] Ошибка запуска '{animName}': {ex.Message}");
            }
        }

        public static void ReloadAnimationsFromDisk()
        {
            if (RM.code != null && RM.code.allFreePoses != null && RM.code.allFreePoses.items != null)
            {
                for (int i = RM.code.allFreePoses.items.Count - 1; i >= 0; i--)
                {
                    Transform item = RM.code.allFreePoses.items[i] as Transform;
                    if (item != null && _registeredAnimations.Contains(item.name))
                    {
                        RM.code.allFreePoses.items.RemoveAt(i);
                        UnityEngine.Object.Destroy(item.gameObject);
                    }
                }
            }

            _registeredAnimations.Clear();
            EnsureAnimationsLoaded();
            Plugin.Log.LogInfo("[FreePoseBrowser] Reloaded custom animations from disk.");
        }

    }
}
