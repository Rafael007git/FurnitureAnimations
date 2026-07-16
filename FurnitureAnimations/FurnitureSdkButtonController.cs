using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RuntimeGizmos; // Подключаем пространство имен гизмо из нашего PDF!

namespace FurnitureAnimationsMod
{
    public class FurnitureSdkButtonController : MonoBehaviour
    {
        private UIFreePose _uiInstance;
        private UnityEngine.UI.Button _mainButton;
        private UnityEngine.UI.Text _buttonText;
        private float _updateTimer = 0f;

        public void Setup(UIFreePose ui)
        {
            _uiInstance = ui;
            _mainButton = GetComponent<UnityEngine.UI.Button>();
            _buttonText = GetComponentInChildren<UnityEngine.UI.Text>();
            Plugin.Log.LogInfo("[SDK_Controller] Нативный Update-трекер успешно запущен напрямую.");
        }

        private void Update()
        {
            if (_uiInstance == null || _mainButton == null || _buttonText == null) return;

            // Опрашиваем состояние 5 раз в секунду для идеальной оптимизации
            _updateTimer += Time.deltaTime;
            if (_updateTimer < 0.2f) return;
            _updateTimer = 0f;

            if (_uiInstance.selectedCharacter == null) return;

            CharacterCustomization characterComp = _uiInstance.selectedCharacter.GetComponent<CharacterCustomization>();
            if (characterComp == null || characterComp.anim == null) return;

            try
            {
                string currentCtrlName = (characterComp.anim.runtimeAnimatorController?.name ?? "").ToLower();

                // 1. ЖЕЛЕЗОБЕТОННЫЙ ПЕРЕХВАТ НА ОСНОВЕ ДАННЫХ ИЗ PDF
                bool isGizmoActiveOnScene = false;
                bool isUserActivelyRotating = false;

                // Обращаемся напрямую к статическому синглтону из строки 1521 нашего PDF!
                if (TransformGizmo.transformGizmo_ != null)
                {
                    // Проверяем, запущен ли режим гизмо для костей (строка 1476)
                    isGizmoActiveOnScene = TransformGizmo.transformGizmo_.runTransformGizmo;

                    // Проверяем рантайм-событие: крутит ли пользователь кость прямо СЕЙЧАС (строка 22)
                    isUserActivelyRotating = TransformGizmo.transformGizmo_.isTransforming;
                }

                // Взводим режим кастомной позы, если гизмо активны на экране или имя контроллера сменилось
                bool isHandEditingActive = currentCtrlName.Contains("custom") ||
                                           _uiInstance.isCustomPoseMode == true ||
                                           isGizmoActiveOnScene;

                // Базовое состояние Idle покоя персонажа
                bool isDefaultIdleActive = currentCtrlName.Contains("idle") ||
                                           currentCtrlName.Contains("unarmed") ||
                                           string.IsNullOrEmpty(currentCtrlName);

                // Поза мебели активна, если аниматор выключен игрой или контроллер ушел с Idle стойки
                bool isAnyPresetPoseActive = characterComp.anim.enabled == false || !isDefaultIdleActive;

                // Если поймали событие физического вращения осей мышкою — логируем!
                if (isUserActivelyRotating)
                {
                    Plugin.Log.LogWarning("[SDK_Gizmo] LIVE EVENT: Обнаружено вращение кости куклы в реальном времени!");
                }

                // 2. ДИНАМИЧЕСКОЕ ИЗМЕНЕНИЕ ИНТЕРФЕЙСА КНОПКИ SDK
                if (isHandEditingActive)
                {
                    // СОСТОЯНИЕ 3: АКТИВЕН РЕЖИМ ADVANCED FREE POSE
                    _buttonText.text = "Save Custom Pose for Furniture";
                    _buttonText.color = Color.cyan; // Фирменный бирюзовый SDK
                    _mainButton.interactable = true;
                }
                else if (isAnyPresetPoseActive)
                {
                    // СОСТОЯНИЕ 2 и 4: ВЫБРАНА ВАНИЛЬНАЯ ПОЗА ИЗ СПИСКА
                    _buttonText.text = "Link Preset Pose for Furniture";
                    _buttonText.color = Color.green; // Зеленый цвет связи
                    _mainButton.interactable = true;
                }
                else
                {
                    // СОСТОЯНИЕ 1: МЕНЮ ПУСТОЕ (Поза мебели не выбрана)
                    _buttonText.text = "No Furniture Pose";
                    _buttonText.color = Color.gray; // Серый цвет блокировки
                    _mainButton.interactable = false;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SDK_Controller] Ошибка прямого трекинга: {ex.Message}");
            }
        }
    }
}
