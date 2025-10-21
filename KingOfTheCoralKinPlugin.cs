using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using TeamCherry.Localization;
using UnityEngine;
using UnityEngine.SceneManagement;
using Silksong.FsmUtil;
using Random = UnityEngine.Random;

//! DEBUG
//! using BepInEx.Logging;
//! DEBUG

namespace KingOfTheCoralKin;

[BepInAutoPlugin(id: "io.github.kingofthecoralkin")]
public partial class KingOfTheCoralKinPlugin : BaseUnityPlugin
{
    //! DEBUG
    //! private static readonly ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource("King Of The Coral Kin");
    //! public static void log(string msg) => logger.LogInfo(msg);
    //! DEBUG
    
    private static bool inCoralMemory;                  //? Bool that checks wether we are in the Memory_Coral_Tower, changes on scene load
    private static bool teleportToBoss;                 //? Bool that checks wether the player wants to teleport to the boss with silksoar or not
    private static bool disableContactDamage;           //? Bool that checks wether the player wants to keep the boss hitbox disabled or not
    private static HeroController? hornet;              //? Player hero controller
    private static PlayMakerFSM? bossControlFSM;        //? Boss control fsm
    private static CoralSpikeState coralSpikeFSMState;  //? Instance of an enum that keeps track of which coral spear attack the boss is doing
    private static bool P2;                             //? Bool that keeps track of phase 2 trigger
    private static bool P3;                             //? Bool that keeps track of phase 3 trigger
    private static bool scheduledCoralRain;             //? Bool that enables or disables more ground hit attacks, should allow no more than 2
    private static int groundHits;                      //? Counter for ground hit attacks, the bool above should be enough but i wanted additional safety to fix janky interactions
    private static bool crossed;                        //? Bool that keeps track of wether the boss did the cross attack or not, to fix some jank
    private static bool threeSpiked;                    //? Prevents the spike duplication in phase 3 from happening twice creating 5 spikes instead of 3
    private static bool crossCooldown;                  //? Bool that prevents the boss from doing the cross too often, fixes disappearing cross (team cherrys pool system sucks)
    private static bool jabbed;                         //? Bool that prevents the boss from doing the jab twice in a row
    
    private static KingOfTheCoralKinPlugin Instance { get; set; } = null!;
    private static class SpikePools
    {
        //? These 3 types of attacks need their own dedicated pool to work
        private static readonly List<GameObject> longSpear = [];
        private static readonly List<GameObject> uppercutSpear = [];
        private static readonly List<GameObject> airSpear = [];
        
        //? Gets the pool based on the coralSpikeFSMState
        public static List<GameObject> getReleveantPool()
        {
            return coralSpikeFSMState switch
            {
                CoralSpikeState.UPPERCUT => uppercutSpear,
                CoralSpikeState.JAB => longSpear,
                CoralSpikeState.AIRJAB => airSpear,
                _ => null! //? Theoretically unreachable block, its there to ignore warnings about a possible null case
            };
        }
        public static void resetPools()
        {
            longSpear.Clear();
            uppercutSpear.Clear();
            airSpear.Clear();
        }
    }
    private enum CoralSpikeState {
        UPPERCUT = 0,
        JAB = 1,
        AIRJAB = 2
    }
    
    //* MONOBEHAVIOURS
    private void Awake()
    {
        Instance = this;
        //? Reads the config option for teleporting to the boss room
        teleportToBoss = Config.Bind(
            "King Of The Coral Kin",
            "TeleportOnSuperDash",
            true,
            "If true, the silksoar will take hornet directly up to the boss, skipping everything else"
        ).Value;
        disableContactDamage = Config.Bind(
            "King Of The Coral Kin",
            "DisableContactDamage",
            true,
            "If true, the boss contact damage will be disabled"
        ).Value;
        Harmony.CreateAndPatchAll(typeof(KingOfTheCoralKinPlugin));
        SceneManager.sceneLoaded += sceneLoadSetup;
        Logger.LogInfo($"Plugin {Name} ({Id}) has loaded!");
    }
    private void FixedUpdate()
    {
        if (!inCoralMemory) return;
        
        //? Teleports hornet straight to the boss room from the starting room in the coral memory
        if (hornet!.cState.superDashing && teleportToBoss)
        {
            if (GameCameras.instance.cameraFadeFSM.ActiveStateName is not ("Scene Fade Out" or "Scene Fade In")) Instance.StartCoroutine(FadeTeleport());
            if (hornet.transform.position.y is > 40 and < 50) hornet.transform.position = new Vector3(
                x: 55.2f,
                y: 500,
                z: 0.004f
            );
        }
    }
    public IEnumerator Start()
    {
        yield return new WaitForSeconds(2f);
        Harmony.CreateAndPatchAll(typeof(Language_Get_Patch));
    }
    
    //* PATCHES
    [HarmonyPatch(typeof(ActivateGameObject), nameof(ActivateGameObject.OnEnter))]
    private static void Postfix(ActivateGameObject __instance)
    {
        if (!inCoralMemory) return;
        if (__instance.activatedGameObject == null) return;
        var go = __instance.activatedGameObject;
        if (go.name.StartsWith("Coral_spear_long"))
        {
            //? Handles the spawning of more spikes
            switch (coralSpikeFSMState)
            {
                case CoralSpikeState.UPPERCUT:
                    Instance.StartCoroutine(SpawnSpike(go, P2 ? 8 : 10, Vector3.right, 0.05f));
                    Instance.StartCoroutine(SpawnSpike(go, P2 ? -8 : -10, Vector3.right, 0.05f));
                    if (P2)
                    {
                        Instance.StartCoroutine(SpawnSpike(go, 16, Vector3.right, 0.2f));
                        Instance.StartCoroutine(SpawnSpike(go, -16, Vector3.right, 0.2f));
                        Instance.StartCoroutine(SpawnSpike(go, 24, Vector3.right, 0.3f));
                        Instance.StartCoroutine(SpawnSpike(go, -24, Vector3.right, 0.3f));
                    }
                    if (P3 && !threeSpiked)
                    {
                        threeSpiked = true;
                        Instance.StartCoroutine(SpawnSpike(go, new[] { 4, -4 }.GetRandomElement(), Vector3.right, 0.1f));
                        Instance.StartCoroutine(DisableThreeSpiked());
                    }
                    break;
                case CoralSpikeState.JAB:
                    //? Sometimes the original spike would not show up at all, so to be safe i get a new one and remove the original one
                    var fixedOriginalJab = GetNewSpike(go);
                    fixedOriginalJab.transform.position = go.transform.position;
                    fixedOriginalJab.transform.rotation = go.transform.rotation;
                    fixedOriginalJab.transform.localScale = go.transform.localScale;
                    fixedOriginalJab.SetActive(true);
                    Instance.StartCoroutine(DisableClone(fixedOriginalJab));
                    go.SetActive(false);
                    if (!crossed) Instance.StartCoroutine(SpawnSpike(go, P2 ? new [] {4, 8}.GetRandomElement() : 8, Vector3.up, 0.1f));
                    else
                    {
                        crossed = false;
                        Instance.StartCoroutine(SpawnSpike(go, 8, Vector3.up, 0.1f));
                    }
                    break;
                case CoralSpikeState.AIRJAB:
                    //? Sometimes the original spike would not show up at all, so to be safe i get a new one and remove the original one
                    var fixedOriginalAirJab = GetNewSpike(go);
                    fixedOriginalAirJab.transform.position = go.transform.position;
                    fixedOriginalAirJab.transform.rotation = go.transform.rotation;
                    fixedOriginalAirJab.transform.localScale = go.transform.localScale;
                    fixedOriginalAirJab.SetActive(true);
                    Instance.StartCoroutine(DisableClone(fixedOriginalAirJab));
                    Instance.StartCoroutine(SpawnSpike(go, P2 ? 11 : 12, Vector3.right, 0.05f));
                    Instance.StartCoroutine(SpawnSpike(go, P2 ? -11 : -12, Vector3.right, 0.05f));
                    if (P2)
                    {
                        Instance.StartCoroutine(SpawnSpike(go, 22, Vector3.right, 0.2f));
                        Instance.StartCoroutine(SpawnSpike(go, -22, Vector3.right, 0.2f));
                        Instance.StartCoroutine(SpawnSpike(go, 33, Vector3.right, 0.3f));
                        Instance.StartCoroutine(SpawnSpike(go, -33, Vector3.right, 0.3f));
                    }
                    if (P3) Instance.StartCoroutine(SpawnSpike(go, new [] {5, -5}.GetRandomElement(), Vector3.right, 0.1f));
                    break;
            }
        }
    }
    [HarmonyPostfix]
    [HarmonyPatch(typeof(FsmState), "OnEnter")]
    private static void OnFsmStateEntered(FsmState __instance)
    {
        if (!inCoralMemory) return;
        switch (__instance.name)
        {
            case "Drop In":
                bossControlFSM = __instance.Fsm.FsmComponent;
                if (disableContactDamage) foreach (var dmgHero in bossControlFSM.gameObject.GetComponentsInChildren<DamageHero>(true)) Destroy(dmgHero);
                setupBossValues();
                P2 = P3 = false;
                scheduledCoralRain = false;
                groundHits = 0;
                SpikePools.resetPools();
                break;
            //? We set these on Intro Land too, but thats in case the player restarts the fight without dying, this ensures the values are reset always
            case "Hornet Dead":
                P2 = P3 = false;
                scheduledCoralRain = false;
                groundHits = 0;
                crossed = false;
                SpikePools.resetPools();
                break;
            case "Death Stagger":
                inCoralMemory = false;
                P2 = P3 = false;
                scheduledCoralRain = false;
                groundHits = 0;
                crossed = false;
                SpikePools.resetPools();
                Instance.StopAllCoroutines();
                break;
            case "P2":
                if (!P2)
                {
                    P2 = true;
                    Instance.StartCoroutine(ForceNextState(__instance, "PHASE ROAR", 0));
                }
                break;
            case "P3":
                if (!P3)
                {
                    P3 = true;
                    Instance.StartCoroutine(ForceNextState(__instance, "PHASE ROAR", 0));
                }
                break;
            case "P3 Roar":
                //? Prevent the boss from doing the P3 roar which leads into Shoot Antic, one of the states we want to avoid
                Instance.StartCoroutine(ScheduleNextState(new [] {"UC Antic", "Air Jab Aim", "Cross Antic", "Jab Dir"}.GetRandomElement(), 1f));
                break;
            case "UC Antic":
                //? Fix failed uppercut
                if (coralSpikeFSMState == CoralSpikeState.UPPERCUT) bossControlFSM!.SetState("Air Jab Aim");
                else coralSpikeFSMState = CoralSpikeState.UPPERCUT;
                break;
            case "Jab Antic":
                //? Prevent double jabs since they create unfair patterns
                if (coralSpikeFSMState == CoralSpikeState.JAB) bossControlFSM!.SetState(new[] {"Cross Antic", "Air Jab Aim", "UC Antic"}.GetRandomElement());
                else coralSpikeFSMState = CoralSpikeState.JAB;
                break;
            case "Air Jab Antic":
                coralSpikeFSMState = CoralSpikeState.AIRJAB;
                break;
            case "Cross 2":
                crossed = true;
                crossCooldown = true;
                Instance.StartCoroutine(DisableCrossed());
                Instance.StartCoroutine(DisableCrossCooldown());
                Instance.StartCoroutine(ForceNextState(__instance, "CROSS CHOP", 0.05f));
                break;
            case "Cross Antic":
                //? Fix failed cross
                if (crossCooldown)
                {
                    bossControlFSM!.SetState(coralSpikeFSMState switch
                    {
                        CoralSpikeState.UPPERCUT => Random.value < 0.5f ? "Air Jab Aim" : "Jab Dir",
                        CoralSpikeState.JAB => Random.value < 0.5f ? "Air Jab Aim" : "UC Antic",
                        CoralSpikeState.AIRJAB => Random.value < 0.5f ? "UC Antic" : "Jab Dir",
                        _ => null!
                    });
                    crossCooldown = false;
                }
                break;
            case "Ground Hit":
                if (groundHits >= 2) groundHits = 0;
                else Instance.StartCoroutine(ScheduleNextState("Ground Hit", 0.35f));
                groundHits++;
                break;
            case "Shoot Pos":
            case "Shoot Antic":
                //? Prevent the boss from doing this attack on its own since it can create unfair patterns
                bossControlFSM!.SetState("P3");
                break;
            case "Antic":
                //? In phase 3 adds ceiling spike rain together with the ground spikes 
                if (__instance.Fsm.FsmComponent.name.StartsWith("Coral Spike"))
                {
                    var coralSpikeFSM = __instance.Fsm.FsmComponent;
                    if (P3)
                    {
                        //? Prevents the boss from doing this attack twice in a row, because it runs out of pooled prefabs and it looks janky
                        Instance.StartCoroutine(ScheduleNextState("Air Jab Aim", 1.2f));
                        if (coralSpikeFSM.transform.position.y > 557) Destroy(coralSpikeFSM.gameObject);
                        if (!scheduledCoralRain)
                        {
                            scheduledCoralRain = true;
                            Instance.StartCoroutine(ScheduleNextState("Ground Hit", 0.2f));
                            Instance.StartCoroutine(DisableScheduledCoralRain());
                        }
                    }
                }
                break;
        }
    }
    
    //* UTILITIES
    private static void sceneLoadSetup(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Memory_Coral_Tower")
        {
            inCoralMemory = false;
            return;
        }
        inCoralMemory = true;
        hornet = HeroController.instance;
        PlayerData.instance.encounteredCoralKing = true;
    }
    private static GameObject GetNewSpike(GameObject go)
    {
        List<GameObject> pool = SpikePools.getReleveantPool();
        GameObject clone = null!;
        
        //? Using a bool here is far less expensive than a null comparison on clone
        bool found = false;
        
        //? Simple for loop is also less expensive than a FirstOrDefault or foreach
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i].activeSelf) continue;
            clone = pool[i];
            found = true;
            break;
        }
        //? Instantiate a new spike if none are available
        if (!found)
        {
            clone = Instantiate(go, go.transform.parent);
            clone.name += "_POOLED";
            pool.Add(clone);
        }
        return clone!;
    }
    private static void setupBossValues()
    {
        Destroy(bossControlFSM!.gameObject.LocateMyFSM("Stun Control"));
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P1")!.floatVariable = 0;
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P1")!.floatValue = 0;
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P2")!.floatVariable = 0;
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P2")!.floatValue = 0;
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P3")!.floatVariable = 0f;
        bossControlFSM.GetFirstActionOfType<SetFloatValue>("P3")!.floatValue = 0f;
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
    
    //* COROUTINES
    //? Spawns a spike, sets it up in the correct position, and schedules its deactivation
    private static IEnumerator SpawnSpike(GameObject go, int distance, Vector3 direction, float delay)
    {
        yield return new WaitForSeconds(delay);
        var clone = GetNewSpike(go);
        clone.transform.position = go.transform.position + direction * distance;
        clone.transform.rotation = go.transform.rotation;
        clone.transform.localScale = go.transform.localScale;
        if (coralSpikeFSMState == CoralSpikeState.JAB)
        {
            clone.transform.FlipLocalScale(x: true);
            clone.transform.position = new Vector3(clone.transform.position.x > 50 ? 16.57f : 94.27f, clone.transform.position.y, clone.transform.position.z);
        }
        clone.SetActive(true);
        Instance.StartCoroutine(DisableClone(clone));
    }
    private static IEnumerator DisableClone(GameObject clone)
    {
        yield return new WaitForSeconds(3f);
        clone.SetActive(false);
    }
    //? Effect used to hide the jarring teleportation to the boss room
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
    private static IEnumerator ScheduleNextState(string stateName, float duration)
    {
        yield return new WaitForSeconds(duration);
        bossControlFSM!.SetState(stateName);
    }
    private static IEnumerator DisableCrossed()
    {
        yield return new WaitForSeconds(1.5f);
        crossed = false;
    }
    private static IEnumerator DisableCrossCooldown()
    {
        yield return new WaitForSeconds(3f);
        crossCooldown = false;
    }
    private static IEnumerator DisableThreeSpiked()
    {
        yield return new WaitForSeconds(0.5f);
        threeSpiked = false;
    }
    private static IEnumerator DisableScheduledCoralRain()
    {
        yield return new WaitForSeconds(1f);
        scheduledCoralRain = false;
    }
}
[HarmonyPatch(typeof(Language), "Get")]
[HarmonyPatch([typeof(string), typeof(string)])]
public static class Language_Get_Patch
{
    private static void Postfix(string key, string sheetTitle, ref string __result)
    {
        if (key == "CORAL_KING_SUPER") __result = "King Of The";
        if (key == "CORAL_KING_MAIN") __result = "Coral Kin";
    }
}
