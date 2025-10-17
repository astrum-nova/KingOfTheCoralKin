using System;
using System.Collections;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TeamCherry.Localization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KingOfTheCoralKin;

// TODO - adjust the plugin guid as needed
[BepInAutoPlugin(id: "io.github.kingofthecoralkin")]
public partial class KingOfTheCoralKinPlugin : BaseUnityPlugin
{
    private static readonly ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource("King Of The Coral Kin");
    public static void log(string msg) => logger.LogInfo(msg);
    
    public static bool inCoralMemory = false; //? Bool that checks wether we are in the Memory_Coral_Tower, changes on scene load
    private static readonly Vector3 CORAL_MEMORY_BOSS = new (
        x: 60.08588f,
        y: 550.6035f,
        z: 0.004f
    );
    private void Awake()
    {
        Harmony.CreateAndPatchAll(typeof(KingOfTheCoralKinPlugin));
        StartCoroutine(Language_Get_Patch.WaitAndPatch());
        SceneManager.sceneLoaded += (Scene scene, LoadSceneMode mode) =>
        {
            inCoralMemory = scene.name == "Memory_Coral_Tower";
        };
        Logger.LogInfo($"Plugin {Name} ({Id}) has loaded!");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayMakerFSM), "OnEnable")]
    private static void OnFsmEnabled(PlayMakerFSM __instance)
    {
        
    }

    private void FixedUpdate()
    {
    }
}
[HarmonyPatch(typeof(Language), "Get")]
[HarmonyPatch(new [] { typeof(string), typeof(string) })]
public static class Language_Get_Patch
{
    public static IEnumerator WaitAndPatch()
    {
        yield return new WaitForSeconds(2f); // Give game time to init Language
        Harmony.CreateAndPatchAll(typeof(Language_Get_Patch));
    }
    private static void Postfix(string key, string sheetTitle, ref string __result)
    {
        if (key == "CORAL_KING_SUPER") __result = "King Of The";
        if (key == "CORAL_KING_MAIN") __result = "Coral Kin";
    }
}