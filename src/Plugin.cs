using BepInEx;
using HarmonyLib;
using BepInEx.Logging;
using System.Reflection;
using System.Collections.Generic;
using System;
using System.Linq;
using GameNetcodeStuff;
using System.Reflection.Emit;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using UnityEngine.InputSystem;
using BepInEx.Configuration;
using UnityEngine.InputSystem.Controls;
using UnityEngine;

namespace QuickQuitToMenu;

[BepInPlugin(modGUID, modName, modVersion)]
public partial class Plugin : BaseUnityPlugin {
    internal const string modGUID = PluginInfo.PLUGIN_GUID;
    internal const string modName = PluginInfo.PLUGIN_NAME;
    internal const string modVersion = PluginInfo.PLUGIN_VERSION;
    internal readonly Harmony harmony = new Harmony(modGUID);
    internal static ManualLogSource log;
    internal static List<Action> cleanupActions = new List<Action>();
    internal static ConfigEntry<Key> QuitKey = null!;

    static Plugin() {
        log = BepInEx.Logging.Logger.CreateLogSource(modName);
    }

    private void Awake() {
        QuitKey = Config.Bind<Key>("General", nameof(QuitKey), Key.Q, "This key, (when pressed in combination with Ctrl, Shift, and Alt), will return you to the main menu.");
        log.LogInfo($"Loading {modGUID}");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    private void OnDestroy() {
        #if DEBUG
        harmony?.UnpatchSelf();
        foreach (var action in cleanupActions) {
            action();
        }
        log.LogInfo($"Unloading {modGUID}");
        #endif
    }
}

public class QuickQuitToMenuBehaviour : MonoBehaviour {
    internal static QuickQuitToMenuBehaviour? instance;
    static bool pressedNow;
    static bool wasPressed;
    internal int tries = 0;
    void Update() {
        HandleHotkey();
    }

    public void HandleHotkey() {
        pressedNow = false;
        if (
            Keyboard.current.ctrlKey.IsPressed()
            && Keyboard.current.altKey.IsPressed()
            && Keyboard.current.shiftKey.IsPressed()
        ) {
            KeyControl c = Keyboard.current.allKeys.FirstOrDefault((KeyControl key) => {
                return key.keyCode == Plugin.QuitKey.Value;
            });
            if (c != null && c.IsPressed()) {
                pressedNow = true;
                if (!wasPressed) {
                    Plugin.log.LogInfo("QuickQuitToMenu hotkey pressed");
                    if (tries < 5 && NetworkManager.Singleton.IsConnectedClient && SceneManager.GetActiveScene().name != "MainMenu") {
                        // If in-game, try to disconnect cleanly (unless "quit to menu" gets mashed five times in a row)
                        GameNetworkManager.Instance?.Disconnect();
                        ++tries;
                        Plugin.log.LogInfo($"Attempting disconnect (try {tries}/5)");
                    } else {
                        tries = 0;
                        Plugin.log.LogInfo("Returning to title screen");
                        if (GameNetworkManager.Instance != null) {
                            NetworkManager.Singleton.Shutdown();
                            var reset = typeof(GameNetworkManager).GetMethod("ResetGameValuesToDefault", BindingFlags.Instance | BindingFlags.NonPublic);
                            reset.Invoke(GameNetworkManager.Instance, new object[] {});
                        }
                        SceneManager.LoadScene("MainMenu");
                    }
                }
            }
        }
        wasPressed = pressedNow;
    }
}


[HarmonyPatch]
internal class Patches {
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MenuManager), "Awake")]
    public static void MenuAwake(MenuManager __instance) {
        EnsureInstance();
        QuickQuitToMenuBehaviour.instance!.tries = 0;
    }
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MenuManager), "Update")]
    public static void MenuUpdate(MenuManager __instance) {
        EnsureInstance();
    }
    [HarmonyPostfix]
    [HarmonyPatch(typeof(StartOfRound), "LateUpdate")]
    public static void GameUpdate(StartOfRound __instance) {
        EnsureInstance();
    }
    public static void EnsureInstance() {
        if (QuickQuitToMenuBehaviour.instance == null) {
            var go = new GameObject("QuickQuitToMenuBehaviour");
            QuickQuitToMenuBehaviour.instance = go.AddComponent<QuickQuitToMenuBehaviour>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }
    }
}