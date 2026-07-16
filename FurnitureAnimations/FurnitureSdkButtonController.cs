using System;
using UnityEngine;
using UnityEngine.UI;

namespace FurnitureAnimationsMod
{
    public class FurnitureSdkButtonController : MonoBehaviour
    {
        private UIFreePose _uiInstance;
        private UnityEngine.UI.Button _mainButton;
        private UnityEngine.UI.Text _buttonText;
        private float _updateTimer = 0f;

        // Инициализация ссылок на компоненты
        public void Setup(UIFreePose ui)
        {
            _uiInstance = ui;
            _mainButton = GetComponent<UnityEngine.UI.Button>();
            _buttonText = GetComponentInChildren<UnityEngine.UI.Text>();

            Plugin.Log.LogWarning("[SDK_Controller] Скрипт автоматического трекинга успешно запущен на кнопке!");
        }

        // Встроенный метод Unity: срабатывает каждый кадр
        private void Update()
        {
            if (_uiInstance == null || _mainButton == null || _buttonText == null) return;

            // Оптимизация: опрашиваем движок игры не каждый кадр, а 5 раз в секунду (каждые 0.2 сек)
            _updateTimer += Time.deltaTime;
            if (_updateTimer < 0.2f) return;
            _updateTimer = 0f;

            if (_uiInstance.selectedCharacter == null) return;

            CharacterCustomization characterComp = _uiInstance.selectedCharacter.GetComponent<CharacterCustomization>();
            if (characterComp == null || characterComp.anim == null) return;

            try
            {
                // 1. АНАЛИЗ СОСТОЯНИЙ НА ОСНОВЕ ФАКТОВ ДВИЖКА
                string currentCtrlName = (characterComp.anim.runtimeAnimatorController?.name ?? "").ToLower();

                // Проверяем наличие гизмоAdvanced Free Pose на сцене
                bool isHandEditingActive = GameObject.Find("TransformGizmo") != null || currentCtrlName == "customjson";

                // Проверяем, находится ли персонаж в дефолтной стойке
                bool isDefaultIdleActive = currentCtrlName.Contains("idle") || currentCtrlName.Contains("unarmed") || string.IsNullOrEmpty(currentCtrlName);

                // Если аниматор заморожен или имя сменилось с дефолтного — поза мебели выбрана!
                bool isAnyPresetPoseActive = characterComp.anim.enabled == false || !isDefaultIdleActive;

                // 2. МГНОВЕННОЕ ОБНОВЛЕНИЕ ВИЗУАЛА КНОПКИ
                if (isHandEditingActive)
                {
                    _buttonText.text = "Save Custom Pose for Furniture";
                    _buttonText.color = Color.cyan; // Бирюзовый SDK
                    _mainButton.interactable = true;
                }
                else if (isAnyPresetPoseActive)
                {
                    _buttonText.text = "Link Preset Pose for Furniture";
                    _buttonText.color = Color.green; // Зеленый цвет связи
                    _mainButton.interactable = true;
                }
                else
                {
                    _buttonText.text = "No Furniture Pose";
                    _buttonText.color = Color.gray; // Серый цвет
                    _mainButton.interactable = false; // Блокируем клик
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SDK_Controller] Ошибка в Update-трекере: {ex.Message}");
            }
        }
    }
}
