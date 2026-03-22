using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace j_red.Patches
{
    [HarmonyPatch(typeof(PlayerControllerB))]
    internal static class ToggleSprintPatch
    {
        private static readonly CodeSearch SprintLookupSearch = new CodeSearch(
            new CodeSearchDescriptor(i => i.opcode == OpCodes.Call && i.operand != null && i.operand.ToString().Contains("IngamePlayerSettings")),
            new CodeSearchDescriptor(i => i.opcode == OpCodes.Stloc_0),
            new[]
            {
                new CodeSearchDescriptor(i => i.opcode == OpCodes.Ldstr && i.operand != null && i.operand.ToString().Contains("Sprint")),
                new CodeSearchDescriptor(i => i.opcode == OpCodes.Callvirt && i.operand != null && i.operand.ToString().Contains("FindAction")),
                new CodeSearchDescriptor(i => i.opcode == OpCodes.Callvirt && i.operand != null && i.operand.ToString().Contains("ReadValue"))
            });

        private static readonly Dictionary<PlayerControllerB, bool> SprintToggled = new Dictionary<PlayerControllerB, bool>();
        private static readonly Dictionary<PlayerControllerB, bool> WasPressedLastFrame = new Dictionary<PlayerControllerB, bool>();

        private static float GetSprintInput(PlayerControllerB instance)
        {
            if (instance == null)
            {
                return 0f;
            }

            if (!ModBase.config.toggleSprint.Value || !SprintToggled.TryGetValue(instance, out bool toggled) || !toggled)
            {
                return instance.playerActions.Movement.Sprint.ReadValue<float>();
            }

            return 1f;
        }

        private static bool SprintIsTogglable(PlayerControllerB player)
        {
            if (player == null || IngamePlayerSettings.Instance == null)
            {
                return false;
            }

            Vector2 move = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Move", false).ReadValue<Vector2>();
            if (move.sqrMagnitude < 0.05f)
            {
                return false;
            }

            if (player.inTerminalMenu || player.isTypingChat || player.inSpecialInteractAnimation || player.playingQuickSpecialAnimation)
            {
                return false;
            }

            return true;
        }

        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        private static void StartPostfix(PlayerControllerB __instance)
        {
            SprintToggled[__instance] = false;
            WasPressedLastFrame[__instance] = false;
        }

        [HarmonyPatch("OnDisable")]
        [HarmonyPostfix]
        private static void OnDisablePostfix(PlayerControllerB __instance)
        {
            SprintToggled.Remove(__instance);
            WasPressedLastFrame.Remove(__instance);
        }

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        private static void UpdatePrefix(PlayerControllerB __instance)
        {
            if (__instance == null || !__instance.isPlayerControlled)
            {
                return;
            }

            if (!SprintToggled.ContainsKey(__instance))
            {
                SprintToggled[__instance] = false;
            }

            if (!WasPressedLastFrame.ContainsKey(__instance))
            {
                WasPressedLastFrame[__instance] = false;
            }

            if (!ModBase.config.toggleSprint.Value)
            {
                SprintToggled[__instance] = false;
                WasPressedLastFrame[__instance] = false;
                return;
            }

            if (!SprintIsTogglable(__instance))
            {
                if (SprintToggled[__instance])
                {
                    SprintToggled[__instance] = false;
                    WasPressedLastFrame[__instance] = false;
                }

                return;
            }

            float sprintInput = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Sprint", false).ReadValue<float>();
            if (sprintInput > 0.3f)
            {
                if (!WasPressedLastFrame[__instance])
                {
                    SprintToggled[__instance] = !SprintToggled[__instance];
                    WasPressedLastFrame[__instance] = true;
                    ModBase.Log?.LogInfo("Toggle Sprint -> " + SprintToggled[__instance]);
                }
            }
            else
            {
                WasPressedLastFrame[__instance] = false;
            }
        }

        [HarmonyPatch("Update")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> UpdateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            Tuple<int, int> match = SprintLookupSearch.FindPatch(list);

            if (match == null)
            {
                ModBase.Log?.LogWarning("Toggle Sprint transpiler could not find sprint lookup in PlayerControllerB.Update.");
                return list;
            }

            for (int i = match.Item1; i < match.Item2; i++)
            {
                list[i].opcode = OpCodes.Nop;
                list[i].operand = null;
            }

            list[match.Item2 - 2] = new CodeInstruction(OpCodes.Ldarg_0);
            list[match.Item2 - 1] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ToggleSprintPatch), nameof(GetSprintInput)));
            ModBase.Log?.LogInfo("Toggle Sprint transpiler patched PlayerControllerB.Update.");
            return list;
        }
    }

    internal sealed class CodeSearchDescriptor
    {
        private readonly Func<CodeInstruction, bool> matchFunction;

        internal CodeSearchDescriptor(Func<CodeInstruction, bool> matchFunction)
        {
            this.matchFunction = matchFunction ?? throw new ArgumentNullException(nameof(matchFunction));
        }

        internal bool Matches(CodeInstruction instruction)
        {
            return matchFunction(instruction);
        }
    }

    internal sealed class CodeSearch
    {
        private readonly CodeSearchDescriptor start;
        private readonly CodeSearchDescriptor end;
        private readonly IReadOnlyList<CodeSearchDescriptor> validators;

        internal CodeSearch(CodeSearchDescriptor start, CodeSearchDescriptor end, IReadOnlyList<CodeSearchDescriptor> validators)
        {
            this.start = start ?? throw new ArgumentNullException(nameof(start));
            this.end = end ?? throw new ArgumentNullException(nameof(end));
            this.validators = validators;
        }

        internal Tuple<int, int> FindPatch(List<CodeInstruction> instructions)
        {
            int? startIndex = null;
            int? endIndex = null;
            int matchedValidators = 0;
            if (instructions == null)
            {
                return null;
            }

            for (int i = 0; i < instructions.Count; i++)
            {
                CodeInstruction instruction = instructions[i];
                if (instruction == null)
                {
                    continue;
                }

                if (start.Matches(instruction))
                {
                    startIndex = i;
                    matchedValidators = 0;
                }

                if (startIndex.HasValue && validators != null && matchedValidators < validators.Count && validators[matchedValidators].Matches(instruction))
                {
                    matchedValidators++;
                }

                if (end.Matches(instruction) && startIndex.HasValue && (validators == null || matchedValidators >= validators.Count))
                {
                    endIndex = i;
                    break;
                }
            }

            return startIndex.HasValue && endIndex.HasValue ? new Tuple<int, int>(startIndex.Value, endIndex.Value) : null;
        }
    }
}
