using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace StorySkip
{
    internal static class PlayerOverwhelmingAttack
    {
        internal static void InitializePatches(Harmony harmony)
        {
            Type mechanics = typeof(Mission).Assembly.GetType("TaleWorlds.MountAndBlade.MissionCombatMechanicsHelper", true);
            harmony.Patch(AccessTools.Method(mechanics, "GetDefendCollisionResults"),
                postfix: new HarmonyMethod(typeof(PlayerOverwhelmingAttack), nameof(AfterDefend)));
            // Enforce after all native damage models (and the AK helper's multiplier).
            harmony.Patch(AccessTools.Method(typeof(Mission), "GetAttackCollisionResults"),
                postfix: new HarmonyMethod(typeof(PlayerOverwhelmingAttack), nameof(AfterCollision)) { priority = Priority.Last });
            harmony.Patch(AccessTools.Method(typeof(DestructableComponent), "OnHit"),
                transpiler: new HarmonyMethod(typeof(PlayerOverwhelmingAttack), nameof(GateDamageTranspiler)));
        }

        private static bool IsPlayerAttack(Agent attacker)
        {
            Mission mission = Mission.Current;
            return ComfortFeatures.OverwhelmingAttackEnabled && !GameNetwork.IsMultiplayerOrReplay &&
                !GameNetwork.IsSessionActive && mission != null && mission.Mode == MissionMode.Battle &&
                mission.CombatType == Mission.MissionCombatType.Combat && attacker != null &&
                attacker == mission.MainAgent && attacker.Character != null && attacker.Character.IsPlayerCharacter &&
                attacker.IsActive();
        }

        private static bool IsEnemyAttack(Agent attacker, Agent defender)
        {
            return IsPlayerAttack(attacker) && defender != null && defender.IsActive() && attacker.IsEnemyOf(defender);
        }

        private static void AfterDefend(Agent attackerAgent, Agent defenderAgent,
            CombatCollisionResult collisionResult, bool isAlternativeAttack,
            ref bool crushedThrough, ref bool chamber, ref float attackerStunPeriod)
        {
            if (isAlternativeAttack || !IsEnemyAttack(attackerAgent, defenderAgent) ||
                (collisionResult != CombatCollisionResult.Blocked && collisionResult != CombatCollisionResult.Parried &&
                 collisionResult != CombatCollisionResult.ChamberBlocked)) return;
            EquipmentIndex index = defenderAgent.GetOffhandWieldedItemIndex();
            if (index == EquipmentIndex.None) index = defenderAgent.GetPrimaryWieldedItemIndex();
            if (index == EquipmentIndex.None) return;
            MissionWeapon weapon = defenderAgent.Equipment[index];
            if (weapon.IsEmpty || weapon.CurrentUsageItem == null) return;
            if (weapon.CurrentUsageItem.IsShield) { crushedThrough = false; return; }
            // The shared native defend callback covers lateral attacks, thrusts and chamber parries too.
            crushedThrough = true;
            chamber = false;
            attackerStunPeriod = 0f;
        }

        private static void AfterCollision(Agent attackerAgent, Agent victimAgent, WeakGameEntity hitObject,
            ref MissionWeapon attackerWeapon, bool cancelDamage, ref AttackCollisionData attackCollisionData,
            ref CombatLogData combatLog, ref CombatLogData __result)
        {
            if (cancelDamage || !IsEnemyAttack(attackerAgent, victimAgent) ||
                !attackCollisionData.AttackBlockedWithShield || attackCollisionData.IsAlternativeAttack) return;
            var info = new AttackInformation(attackerAgent, victimAgent, hitObject, attackCollisionData, attackerWeapon);
            if (info.IsFriendlyFire || !info.CanGiveDamageToAgentShield || info.VictimShield.IsEmpty) return;
            attackCollisionData.InflictedDamage = ShieldDamage(attackCollisionData.InflictedDamage, info.VictimShield.HitPoints);
            attackCollisionData.IsShieldBroken = true;
            combatLog.InflictedDamage = attackCollisionData.InflictedDamage;
            combatLog.ModifiedDamage = 0;
            __result = combatLog;
            // Native RegisterBlow deducts shield durability and performs the break reaction.
        }

        internal static int ShieldDamage(int original, int hitPoints) => Math.Max(original, Math.Max(1, hitPoints + 1));

        private static int GateDamage(DestructableComponent component, Agent attacker, int damage, ref float modifiedDamage)
        {
            if (damage <= 0 || !IsPlayerAttack(attacker) || component == null ||
                component.GameEntity.GetFirstScriptOfType<CastleGate>() == null) return damage;
            int result = (int)Math.Min(int.MaxValue / 2d,
                Math.Max(damage, Math.Ceiling(Math.Max(component.HitPoint, component.MaxHitPoint)) + 1d));
            modifiedDamage = result;
            return result;
        }

        private static IEnumerable<CodeInstruction> GateDamageTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int matches = 0;
            for (int i = 0; i < code.Count; i++)
            {
                // After native weapon validity checks, before HP subtraction and destruction events.
                if (i + 2 < code.Count && code[i].opcode == OpCodes.Ldarg_2 &&
                    code[i + 1].opcode == OpCodes.Ldc_I4_0 &&
                    (code[i + 2].opcode == OpCodes.Ble || code[i + 2].opcode == OpCodes.Ble_S))
                {
                    matches++;
                    var start = new CodeInstruction(OpCodes.Ldarg_0);
                    start.labels.AddRange(code[i].labels); code[i].labels.Clear();
                    start.blocks.AddRange(code[i].blocks); code[i].blocks.Clear();
                    yield return start;
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Ldarg_S, (byte)9); // ref modifiedDamage
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PlayerOverwhelmingAttack), nameof(GateDamage)));
                    yield return new CodeInstruction(OpCodes.Starg_S, (byte)2);
                }
                yield return code[i];
            }
            if (matches != 1) throw new InvalidOperationException("Native gate damage anchor changed: " + matches);
        }
    }
}
