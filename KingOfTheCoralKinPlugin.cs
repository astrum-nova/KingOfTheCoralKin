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
    
    public static bool inCoralMemory; //? Bool that checks wether we are in the Memory_Coral_Tower, changes on scene load
    private static bool teleportToBoss;
    private static readonly Vector3 CORAL_MEMORY_BOSS = new (
        x: 55,
        y: 510,
        z: 0.004f
    );

    private static HeroController hornet;
    private void Awake()
    {
        teleportToBoss = Config.Bind(
            "King Of The Coral Kin",
            "TeleportOnSuperDash",
            true,
            "If true, the silksoar will take hornet directly up to the boss, skipping everything else"
        ).Value;
        StartCoroutine(Language_Get_Patch.WaitAndPatch());
        Harmony.CreateAndPatchAll(typeof(KingOfTheCoralKinPlugin));
        SceneManager.sceneLoaded += sceneLoadSetup;
        Logger.LogInfo($"Plugin {Name} ({Id}) has loaded!");
    }
    private static void sceneLoadSetup(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Memory_Coral_Tower")
        {
            inCoralMemory = false;
            return;
        }
        inCoralMemory = true;
        hornet = HeroController.instance;
    }
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayMakerFSM), "OnEnable")]
    private static void OnFsmEnabled(PlayMakerFSM __instance)
    {
        
    }
    private void FixedUpdate()
    {
        if (!inCoralMemory) return;
        
        //TODO: Make this less scuffed
        if (hornet.cState.superDashing && teleportToBoss && hornet.transform.position.y is > 40 and < 50) hornet.transform.position = CORAL_MEMORY_BOSS;
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