using MelonLoader;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.Events;
using static MelonLoader.MelonLogger;

[assembly: MelonInfo(typeof(VPMoreOptions.VPMoreOptionsMod), "VPMoreOptions", "0.0.1", "ShadowedWilds")]
[assembly: MelonGame("", "")]

namespace VPMoreOptions
{
    public class VPMoreOptionsMod : MelonMod
    {
        private static bool hasPatched = false;
        public static MelonPreferences_Category Config;

        public override void OnInitializeMelon()
        {
            if (!hasPatched)
            {
                Config = MelonPreferences.CreateCategory("VPMoreOptions");
                var harmony = new HarmonyLib.Harmony("com.ShadowedWilds.vpmoreoptions");
                harmony.PatchAll();

                hasPatched = true;
                LoggerInstance.Msg("VPMoreOptions mod loaded. Use Responsibly!");
            }
        }
    }

    [HarmonyPatch]
    public static class VPMoreOptionsPatch
    {
        private static MethodBase TargetMethod()
        {
            Type siegeManagerType = typeof(SiegeManager);

            if (siegeManagerType.GetMethod("SpawnAgents", BindingFlags.Instance | BindingFlags.NonPublic) == null)
            {
                MelonLogger.Error("Could not find method SiegeManager.SpawnAgents.");
                return null;
            }

            foreach (Type nestedType in siegeManagerType.Assembly.GetTypes())
            {
                if (nestedType.IsNestedPrivate && nestedType.Name.Contains("SpawnAgents") && typeof(IEnumerator).IsAssignableFrom(nestedType))
                {
                    MethodInfo moveNextMethod = nestedType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (moveNextMethod != null)
                    {
                        MelonLogger.Msg("Found MoveNext in iterator type: " + nestedType.FullName);
                        return moveNextMethod;
                    }
                }
            }
            MelonLogger.Error("Failed to find MoveNext method for SpawnAgents.");
            return null;
        }


        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            bool replaced = false;

            for (int i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].opcode == OpCodes.Call)
                {
                    MethodInfo methodInfo = codes[i].operand as MethodInfo;
                    if (methodInfo != null &&
                        methodInfo == typeof(Mathf).GetMethod("RoundToInt", new Type[] { typeof(float) }) &&
                        codes[i + 1].opcode == OpCodes.Ldc_I4_1 &&
                        codes[i + 2].opcode == OpCodes.Ldc_I4 && (int)codes[i + 2].operand == 10000 &&
                        CodeInstructionExtensions.Calls(codes[i + 3], typeof(Mathf).GetMethod("Clamp", new Type[] { typeof(int), typeof(int), typeof(int) })))
                    {
                        codes[i + 1] = new CodeInstruction(OpCodes.Ldc_I4_4, null);
                        MelonLogger.Msg("4x AI.");
                        replaced = true;
                        break;
                    }
                }
            }

            if (!replaced)
            {
                MelonLogger.Warning("");
            }

            return codes.AsReadOnly();
        }
    }

    [HarmonyPatch(typeof(SiegeManager), "GetRandomAgentSpawnPoint")]
    public static class SiegeManager_GetRandomAgentSpawnPoint_Patch
    {
        static MethodInfo originalMethod = AccessTools.Method(typeof(SiegeManager), "GetRandomAgentSpawnPoint");

        static bool Prefix(SiegeManager __instance, ref Vector3 __result)
        {
            Vector3 playerPos = LocalPlayer.instance.transform.position;
            float distance = UnityEngine.Random.Range(50f, 80f);
            float angle = UnityEngine.Random.Range(0f, 360f) * 0.0174533f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            RaycastHit hit;

            if (Physics.Raycast(playerPos + offset + Vector3.up * 100f, Vector3.down, out hit, 120f))
            {
                Vector3 spawnPoint = hit.point;
                if (spawnPoint.y - playerPos.y < 7f)
                {
                    __result = spawnPoint + Vector3.up * 0.1f;
                    return false;
                }
            }
            __result = (Vector3)originalMethod.Invoke(__instance, null);
            return false;
        }
    }

    [HarmonyPatch(typeof(LevelSelector), "OpenLeveSelectionWithGamemode")]
    public static class LevelSelector_OpenLeveSelectionWithGamemode_Patch
    {
        static bool Prefix(LevelSelector __instance, string GameMode)
        {
            __instance.GamemodeDescription.text = "";
            __instance.GamemodeSelectionButtons.gameObject.SetActive(false);
            __instance.LevelsScreen.SetActive(true);
            LevelSelector.selectedGamemode = (GameModeManager.GameMode)Enum.Parse(typeof(GameModeManager.GameMode), GameMode);
            __instance.LevelsParent.gameObject.SetActive(true);

            var levelButtonsField = AccessTools.Field(typeof(LevelSelector), "levelButtons");
            if (levelButtonsField == null)
            {
                MelonLogger.Error("Could not find 'levelButtons' field on LevelSelector");
                return true; // fallback to original
            }

            var levelButtons = (List<LevelUiInstance>)levelButtonsField.GetValue(__instance);
            foreach (LevelUiInstance levelUiInstance in levelButtons)
            {
                levelUiInstance.gameObject.SetActive(true);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(LevelSelector), "Awake")]
    public static class LevelSelector_Awake_Patch
    {
        static bool Prefix(LevelSelector __instance)
        {
            LevelSelector.Instance = __instance;

            foreach (Transform child in __instance.LevelsParent)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            var levelButtonsField = AccessTools.Field(typeof(LevelSelector), "levelButtons");
            if (levelButtonsField == null)
            {
                MelonLogger.Error("Could not find 'levelButtons' field on LevelSelector");
                return true;
            }

            var levelButtons = (List<LevelUiInstance>)levelButtonsField.GetValue(__instance);
            levelButtons.Clear();

            foreach (Level level in __instance.levels.levels)
            {
                LevelUiInstance component = UnityEngine.Object.Instantiate(
                    __instance.LevelUiInstance,
                    __instance.LevelsParent
                ).GetComponent<LevelUiInstance>();

                component.Setup(level);
                levelButtons.Add(component);
            }

            levelButtonsField.SetValue(__instance, levelButtons);

            __instance.LevelsScreen.gameObject.SetActive(false);

            return false;
        }
    }

    [HarmonyPatch(typeof(GameModeManager), "GetRandomSiegeSpawnPoint")]
    public static class GameModeManager_GetRandomSiegeSpawnPoint_Patch
    {
        static bool Prefix(GameModeManager __instance, ref Transform __result)
        {
            Vector3 playerPosition = LocalPlayer.instance.transform.position;
            float angle = UnityEngine.Random.Range(0f, 360f) * 0.0174533f;
            Vector2 randomDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float randomDistance = UnityEngine.Random.Range(10f, 35f);
            Vector3 spawnPosition = playerPosition + new Vector3(randomDirection.x, 0f, randomDirection.y) * randomDistance;

            GameObject spawnPoint = new GameObject("RandomSpawnPoint");
            spawnPoint.transform.position = spawnPosition;

            __result = spawnPoint.transform;
            return false;
        }
    }

    [HarmonyPatch(typeof(GameModeManager), "SetupGamemode")]
    public static class GameModeManager_SetupGamemode_Patch
    {
        static void Postfix()
        {
            if (GameModeManager.CurrentGamemode == GameModeManager.GameMode.Siege)
            {
                GameModeManager.Fire = true;
            }
        }
    }

    [HarmonyPatch(typeof(c4), "Explode")]
    public static class c4_Explode_Patch
    {
        static bool Prefix(c4 __instance)
        {
            if (__instance.PlayerCanDetonate)
            {
                Explosion.DoExplosion(
                    __instance.transform.position,
                    __instance.damageData,
                    __instance.damageDataBakes,
                    __instance.ExplosionEffect,
                    true,
                    !__instance.PlayerCanDetonate,
                    false,
                    12);
            }
            else
            {
                Explosion.DoExplosionDirect(
                    __instance.transform.position,
                    __instance.damageData,
                    __instance.damageDataBakes,
                    __instance.ExplosionEffect,
                    !__instance.PlayerCanDetonate);
            }

            Vector3[] directions = new Vector3[]
            {
                new Vector3(1f, 0f, 1f).normalized,
                new Vector3(1f, 0f, -1f).normalized,
                new Vector3(-1f, 0f, 1f).normalized,
                new Vector3(-1f, 0f, -1f).normalized
            };

            float radius = 0.5f;
            float maxDistance = 3.5f;

            foreach (Vector3 dir in directions)
            {
                if (Physics.SphereCast(__instance.transform.position, radius, dir, out RaycastHit hit, maxDistance))
                {
                    if (hit.collider.GetComponentInParent<VoxelDataHolder>() != null && UnityEngine.Random.value <= 0.025f)
                    {
                        Fire.TrySpawn(hit.point, 0.7f, null);
                    }
                }
            }

            UnityEngine.Object.Destroy(__instance.gameObject);

            return false;
        }
    }

    [HarmonyPatch(typeof(Fire), "Reproduce")]
    public static class Fire_Reproduce_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            for (int i = 0; i < codes.Count; i++)
            {
                if (i + 2 < codes.Count)
                {
                    var c0 = codes[i];
                    var c1 = codes[i + 1];
                    var c2 = codes[i + 2];

                    if ((c0.opcode == OpCodes.Call || c0.opcode == OpCodes.Callvirt) &&
                        c0.operand is MethodInfo methodInfo &&
                        methodInfo.Name == "get_Fire" &&
                        methodInfo.DeclaringType == typeof(GameModeManager) &&

                        (c1.opcode == OpCodes.Brtrue || c1.opcode == OpCodes.Brtrue_S ||
                         c1.opcode == OpCodes.Brfalse || c1.opcode == OpCodes.Brfalse_S) &&

                        c2.opcode == OpCodes.Ret)
                    {
                        i += 2;
                        continue;
                    }
                }

                yield return codes[i];
            }
        }
    }

    [HarmonyPatch(typeof(AiBase), "InAttackRange")]
    class Patch_AiBase_InAttackRange
    {
        static bool Prefix(AiBase __instance, Vector3 target, ref bool __result)
        {
            object currentWeapon = AccessTools.Field(typeof(AiBase), "currentWeapon")?.GetValue(__instance);
            if (currentWeapon == null)
            {
                __result = false;
                return false;
            }

            var weaponTypeField = AccessTools.Field(typeof(AiBase), "currentWeaponType");
            var weaponType = (AiBase.AiWeaponType)weaponTypeField.GetValue(__instance);

            if (weaponType == AiBase.AiWeaponType.Gun)
            {
                var weaponScriptField = AccessTools.Field(typeof(AiBase), "currentWeaponScript");
                var weaponScript = weaponScriptField.GetValue(__instance);

                float barrelRange = (float)AccessTools.Field(weaponScript.GetType(), "BarrelRange").GetValue(weaponScript);

                float range = barrelRange * SiegeManager.Settings.AgentSightRange * 1.75f;
                __result = Vector3.Distance(target, __instance.eyes.position) < range;
            }
            else
            {
                __result = Vector3.Distance(target, __instance.eyes.position) < 2.0f;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(TabletManager), "Start")]
    class Patch_TabletManager_Start
    {
        static void Postfix(TabletManager __instance)
        {
            var mode = GameModeManager.CurrentGamemode;
            if (mode == GameModeManager.GameMode.Siege ||
                mode == GameModeManager.GameMode.Arena)
            {
                MethodInfo createTabButtonMethod = typeof(TabletManager).GetMethod("CreateTabButton", BindingFlags.Instance | BindingFlags.NonPublic);

                if (createTabButtonMethod != null)
                {
                    var button = createTabButtonMethod.Invoke(__instance, new object[] { "Spawn Menu", __instance.SpawnMenuTabIcon }) as UnityEngine.UI.Button;

                    if (button != null)
                    {
                        button.onClick.AddListener(new UnityAction(__instance.OpenSpawnMenu));
                    }
                }
                else
                {
                    MelonLogger.Warning("CreateTabButton method not found on TabletManager.");
                }
            }
        }
    }


    [HarmonyPatch(typeof(ArenaNpcBehaviour), "Awake")]
    public static class ArenaNpcBehavior_Awake_Patch
    {
        static void Postfix(ArenaNpcBehaviour __instance)
        {
            var field = typeof(ArenaNpcBehaviour).GetField("keepDistance", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(__instance, 0.67f);
            }
        }
    }

}
