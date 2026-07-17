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

            _updateTimer += Time.deltaTime;
            if (_updateTimer < 0.2f) return;
            _updateTimer = 0f;

            if (_uiInstance.selectedCharacter == null) return;

            CharacterCustomization characterComp = _uiInstance.selectedCharacter.GetComponent<CharacterCustomization>();
            if (characterComp == null || characterComp.anim == null) return;

            try
            {
                string currentCtrlName = (characterComp.anim.runtimeAnimatorController?.name ?? "").ToLower();

                // Проверяем состояние гизмо
                bool isGizmoActiveOnScene = false;
                bool isUserActivelyRotating = false;

                if (TransformGizmo.transformGizmo_ != null)
                {
                    isGizmoActiveOnScene = TransformGizmo.transformGizmo_.runTransformGizmo;
                    isUserActivelyRotating = TransformGizmo.transformGizmo_.isTransforming;
                }

                // ВЫВОДИМ ДИАГНОСТИКУ В МОМЕНТ РАБОТЫ ИНТЕРФЕЙСА СВОБОДНОЙ ПОЗЫ
                Plugin.Log.LogInfo($"[SDK_LOOP] Character: {characterComp.name} | Controller: {currentCtrlName} | AnimEnabled: {characterComp.anim.enabled} | GizmoActive: {isGizmoActiveOnScene}");

                bool isHandEditingActive = currentCtrlName.Contains("custom") ||
                                           _uiInstance.isCustomPoseMode == true ||
                                           isGizmoActiveOnScene;

                bool isDefaultIdleActive = currentCtrlName.Contains("idle") ||
                                           currentCtrlName.Contains("unarmed") ||
                                           string.IsNullOrEmpty(currentCtrlName);

                bool isAnyPresetPoseActive = characterComp.anim.enabled == false || !isDefaultIdleActive;

                if (isHandEditingActive)
                {
                    _buttonText.text = "Save Custom Pose for Furniture";
                    _buttonText.color = Color.cyan;
                    _mainButton.interactable = true;
                }
                else if (isAnyPresetPoseActive)
                {
                    _buttonText.text = "Link Preset Pose for Furniture";
                    _buttonText.color = Color.green;
                    _mainButton.interactable = true;
                }
                else
                {
                    _buttonText.text = "No Furniture Pose";
                    _buttonText.color = Color.gray;
                    _mainButton.interactable = false;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SDK_Controller] Ошибка в Update: {ex.Message}");
            }
        }

    }
}
