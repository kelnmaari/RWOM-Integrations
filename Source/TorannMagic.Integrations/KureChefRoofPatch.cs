using System.Reflection;
using AbilityUser;
using HarmonyLib;
using Verse;

namespace TorannMagic.Compatibility
{
    // Kure's XML-only Chef support effects use generic JecsLite carriers.
    // Extend Content's local-impact policy only; overhead flight, collisions
    // and the recipient/payment paths remain owned by their existing systems.
    [HarmonyPatch]
    internal static class KureChefRoofPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(
            typeof(TMAbilityDef).Assembly.GetType("TorannMagic.Compatibility.AbilityImpactRoofPolicy"),
            "IsIndoorEffect", new[] { typeof(Projectile_AbilityBase) });

        private static bool Prepare() => ModsConfig.IsActive("Kure.AROM") && TargetMethod() != null;

        private static void Postfix(Projectile_AbilityBase projectile, ref bool __result)
        {
            if (__result || !(projectile?.localAbilityDef is TMAbilityDef)) return;
            switch (projectile.def?.defName)
            {
                case "Projectile_PlatedFood":
                case "Projectile_Coffee":
                case "Projectile_RedPepper":
                    __result = true;
                    break;
            }
        }
    }
}
