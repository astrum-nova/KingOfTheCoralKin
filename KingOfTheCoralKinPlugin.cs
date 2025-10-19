using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using TeamCherry.Localization;
using UnityEngine;
using UnityEngine.SceneManagement;
using Silksong.FsmUtil;

namespace KingOfTheCoralKin;

// TODO - adjust the plugin guid as needed
[BepInAutoPlugin(id: "io.github.kingofthecoralkin")]
public partial class KingOfTheCoralKinPlugin : BaseUnityPlugin
{
    private static readonly ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource("King Of The Coral Kin");
    public static void log(string msg) => logger.LogInfo(msg);
    
    public static bool inCoralMemory; //? Bool that checks wether we are in the Memory_Coral_Tower, changes on scene load
    private static bool teleportToBoss;
    public static HeroController hornet;
    public static PlayMakerFSM bossControlFSM;
    private static CoralSpikeState CoralSpikeFSMState;
    public static bool P2;
    public static bool P3;
    private static bool scheduledCoralRain;
    private static int groundHits;
    private static bool p2spikes = false;
    private static class SpikePools
    {
        public static List<GameObject> longSpear = new List<GameObject>();
        public static List<GameObject> uppercutSpear = new List<GameObject>();
        public static List<GameObject> airSpear = new List<GameObject>();

        public static List<GameObject> getReleveantPool()
        {
            return CoralSpikeFSMState switch
            {
                CoralSpikeState.UPPERCUT => uppercutSpear,
                CoralSpikeState.JAB => longSpear,
                CoralSpikeState.AIRJAB => airSpear
            };
        }
    }
    enum CoralSpikeState {
        UPPERCUT = 2,
        JAB = 3,
        AIRJAB = 4,
    }
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
        PlayerData.instance.encounteredCoralKing = true;
        inCoralMemory = true;
        hornet = HeroController.instance;
    }
    private static void setupBossValues()
    {
        Destroy(bossControlFSM.gameObject.LocateMyFSM("Stun Control"));
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P1")!.floatVariable = 0;
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P1")!.floatValue = 0;
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P2")!.floatVariable = 0;
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P2")!.floatValue = 0;
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P3")!.floatVariable = 0;
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P3")!.floatValue = 0;
        bossControlFSM.GetFirstActionOfType<FloatOperator>("Roar Recover")!.float1 = 0.5f;
        bossControlFSM.GetFirstActionOfType<Wait>("Jab 2")!.time = 0.5f;
        bossControlFSM.GetFirstActionOfType<Wait>("Uppercut 2")!.time = 0.5f;
        bossControlFSM.GetFirstActionOfType<Wait>("Cross 2")!.time = 0.5f;
        bossControlFSM.GetFirstActionOfType<Wait>("Air Jab 2")!.time = 0.5f;
        bossControlFSM.GetFirstActionOfType<Wait>("Cross Followup 2")!.time = 0.5f;
        bossControlFSM.GetFirstActionOfType<Wait>("Ground Hit")!.time = 0.5f;
        bossControlFSM.GetLastActionOfType<Wait>("Ground Hit")!.time = 0.5f;
        bossControlFSM.GetFirstActionOfType<Wait>("Followup Pause")!.time = 0f;
    }
    [HarmonyPatch(typeof(ActivateGameObject), nameof(ActivateGameObject.OnEnter))]
    static void Postfix(ActivateGameObject __instance)
    {
        if (!inCoralMemory) return;
        if (__instance.activatedGameObject == null) return;
        var go = __instance.activatedGameObject;
        if (go.name.StartsWith("Coral_spear_long"))
        {
            switch (CoralSpikeFSMState)
            {
                case CoralSpikeState.UPPERCUT:
                    GameManager.instance.StartCoroutine(SpawnSpike(go, P2 ? 8 : 10, Vector3.right, 0.05f));
                    GameManager.instance.StartCoroutine(SpawnSpike(go, P2 ? -8 : -10, Vector3.right, 0.05f));
                    if (P2)
                    {
                        GameManager.instance.StartCoroutine(SpawnSpike(go, 16, Vector3.right, 0.2f));
                        GameManager.instance.StartCoroutine(SpawnSpike(go, -16, Vector3.right, 0.2f));
                        GameManager.instance.StartCoroutine(SpawnSpike(go, 24, Vector3.right, 0.3f));
                        GameManager.instance.StartCoroutine(SpawnSpike(go, -24, Vector3.right, 0.3f));
                    }
                    if (P3) GameManager.instance.StartCoroutine(SpawnSpike(go, new [] {4, -4}.GetRandomElement(), Vector3.right, 0.1f));
                    break;
                case CoralSpikeState.JAB:
                    var fixedOriginalJab = GetNewSpike(go);
                    fixedOriginalJab.transform.position = go.transform.position;
                    fixedOriginalJab.transform.rotation = go.transform.rotation;
                    fixedOriginalJab.transform.localScale = go.transform.localScale;
                    fixedOriginalJab.SetActive(true);
                    GameManager.instance.StartCoroutine(DisableClone(fixedOriginalJab));
                    go.SetActive(false);
                    GameManager.instance.StartCoroutine(SpawnSpike(go, P2 ? new [] {4, 8}.GetRandomElement() : 8, Vector3.up, 0.1f));
                    break;
                case CoralSpikeState.AIRJAB:
                    GameManager.instance.StartCoroutine(SpawnSpike(go, P2 ? 11 : 12, Vector3.right, 0.05f));
                    GameManager.instance.StartCoroutine(SpawnSpike(go, P2 ? -11 : -12, Vector3.right, 0.05f));
                    if (P2)
                    {
                        GameManager.instance.StartCoroutine(SpawnSpike(go, 22, Vector3.right, 0.2f));
                        GameManager.instance.StartCoroutine(SpawnSpike(go, -22, Vector3.right, 0.2f));
                        GameManager.instance.StartCoroutine(SpawnSpike(go, 33, Vector3.right, 0.3f));
                        GameManager.instance.StartCoroutine(SpawnSpike(go, -33, Vector3.right, 0.3f));
                    }
                    if (P3) GameManager.instance.StartCoroutine(SpawnSpike(go, new [] {5, -5}.GetRandomElement(), Vector3.right, 0.1f));
                    break;
            }
        }
    }
    private static GameObject GetNewSpike(GameObject go)
    {
        List<GameObject> pool = SpikePools.getReleveantPool();
        GameObject clone = null!;
        bool found = false;
        for (int i = 0; i < pool.Count; i++)
        {
            if (!pool[i].activeSelf)
            {
                clone = pool[i];
                found = true;
                break;
            }
        }
        if (!found)
        {
            clone = Instantiate(go, go.transform.parent);
            clone.name += "_POOLED";
            pool.Add(clone);
        }
        return clone!;
    }
    private static IEnumerator SpawnSpike(GameObject go, int distance, Vector3 direction, float delay)
    {
        yield return new WaitForSeconds(delay);
        var clone = GetNewSpike(go);
        clone.transform.position = go.transform.position + direction * distance;
        clone.transform.rotation = go.transform.rotation;
        clone.transform.localScale = go.transform.localScale;
        if (CoralSpikeFSMState == CoralSpikeState.JAB)
        {
            clone.transform.FlipLocalScale(x: true);
            clone.transform.position = new Vector3(clone.transform.position.x > 50 ? 16.57f : 94.27f, clone.transform.position.y, clone.transform.position.z);
        }
        clone.SetActive(true);
        GameManager.instance.StartCoroutine(DisableClone(clone));
    }
    private static IEnumerator DisableClone(GameObject clone)
    {
        yield return new WaitForSeconds(3f);
        clone.SetActive(false);
    }
    [HarmonyPostfix]
    [HarmonyPatch(typeof(FsmState), "OnEnter")]
    private static void OnFsmStateEntered(FsmState __instance)
    {
        if (!inCoralMemory) return;
        switch (__instance.name)
        {
            case "Intro Land":
                bossControlFSM = __instance.Fsm.FsmComponent;
                var dmgComponents = bossControlFSM.gameObject.GetComponentsInChildren<DamageHero>(true);
                foreach (var comp in dmgComponents) comp.enabled = false;
                setupBossValues();
                P2 = P3 = false;
                break;
            case "P2":
                if (!P2)
                {
                    P2 = true;
                }
                break;
            case "P3":
                if (!P3)
                {
                    P3 = true;
                }
                break;
            case "P3 Roar":
                GameManager.instance.StartCoroutine(ScheduleNextState("UC Antic", 1.2f));
                break;
            case "UC Antic":
                CoralSpikeFSMState = CoralSpikeState.UPPERCUT;
                break;
            case "Jab Antic":
                CoralSpikeFSMState = CoralSpikeState.JAB;
                break;
            case "Air Jab Antic":
                CoralSpikeFSMState = CoralSpikeState.AIRJAB;
                break;
            case "Cross 2":
                GameManager.instance.StartCoroutine(ForceNextState(__instance, "CROSS CHOP", 0.1f));
                break;
            case "Ground Hit":
                if (groundHits >= 2)
                {
                    groundHits = 0;
                    bossControlFSM.Fsm.manualUpdate = false;
                }
                else
                {
                    bossControlFSM.Fsm.manualUpdate = true;
                    GameManager.instance.StartCoroutine(ScheduleNextState("Ground Hit", 0.3f));
                }
                groundHits++;
                break;
            case "Shoot Pos":
                bossControlFSM.SetState("P3");
                break;
            case "Antic":
                if (__instance.Fsm.FsmComponent.name.StartsWith("Coral Spike"))
                {
                    var coralSpikeFSM = __instance.Fsm.FsmComponent;
                    if (P3)
                    {
                        GameManager.instance.StartCoroutine(ScheduleNextState(new [] {"UC Antic", "Air Jab Aim", "Cross Antic", "Jab Dir"}.GetRandomElement(), 1.3f));
                        if (coralSpikeFSM.transform.position.y > 557) Destroy(coralSpikeFSM.gameObject);
                        if (!scheduledCoralRain)
                        {
                            scheduledCoralRain = true;
                            GameManager.instance.StartCoroutine(ScheduleNextState("Ground Hit", 0.5f));
                            GameManager.instance.StartCoroutine(DisableScheduledCoralRain());
                        }
                    }
                }
                break;
        }
    }
    private static IEnumerator DisableScheduledCoralRain()
    {
        yield return new WaitForSeconds(0.5f);
        scheduledCoralRain = false;
    }
    private static IEnumerator ScheduleNextState(string stateName, float duration)
    {
        yield return new WaitForSeconds(duration);
        bossControlFSM.SetState(stateName);
    }
    private void FixedUpdate()
    {
        if (!inCoralMemory) return;
        if (hornet.cState.superDashing && teleportToBoss)
        {
            if (!(GameCameras.instance.cameraFadeFSM.ActiveStateName is "Scene Fade Out" or "Scene Fade In")) GameManager.instance.StartCoroutine(FadeTeleport());
            if (hornet.transform.position.y is > 40 and < 50) hornet.transform.position = new Vector3(
                x: 55,
                y: 510,
                z: 0.004f
            );
        }
    }
    private static IEnumerator FadeTeleport()
    {
        GameCameras.instance.cameraFadeFSM.SetState("Scene Fade Out");
        yield return new WaitForSeconds(1.5f);
        GameCameras.instance.cameraFadeFSM.SetState("Scene Fade In");
    }
    private static IEnumerator ForceNextState(FsmState state, string eventName, float delay)
    {
        yield return new WaitForSeconds(delay);
        var transition = state.Transitions.FirstOrDefault(t => t.EventName == eventName);
        if (transition != null) state.Fsm.SetState(transition.ToState);
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
