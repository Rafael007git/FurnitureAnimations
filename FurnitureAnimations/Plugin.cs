using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System.Text;

namespace FurnitureAnimationsMod
{
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.lorifel007.furnitureposefix";
        public const string PluginName = "Furniture Animations Mod";
        public const string PluginVersion = "0.1.0";

        public static ManualLogSource Log;
        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;

            // 1. Сначала инициализируем папки и собираем все JSON-справочники с диска и Воркшопа
            ConfigManager.Initialize();

            // 2. Затем применяем Harmony-патчи
            harmony = new Harmony(PluginGuid);
            harmony.PatchAll();

            Log.LogInfo($"{PluginName} версии {PluginVersion} успешно инициализирован и готов к работе!");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}