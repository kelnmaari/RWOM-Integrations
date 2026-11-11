using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using HarmonyLib;
using TorannMagic.Odyssey;
using Verse;

namespace TorannMagic.Integrations
{
    // The kazenokizu port releases its workers inside MainThreadWaitLoop, after
    // advancing the current game's clock. Prepare before that release, not in
    // any one of its independently scheduled normal/rare/long/map work lists.
    [HarmonyPatch]
    internal static class RimThreadedFieldPreparation
    {
        private const string PackageId = "kazenokizu.rimthreaded";
        private static readonly MethodInfo ContentPrefix = typeof(MagicalFieldTickPatch).GetMethod(
            nameof(MagicalFieldTickPatch.Prefix), BindingFlags.Public | BindingFlags.Static);
        private static PreparationReceipt prepared;
        [ThreadStatic] private static int preparationDepth;

        private sealed class PreparationReceipt
        {
            internal readonly Game Game;
            internal readonly TickManager Manager;
            internal readonly int Tick;

            internal PreparationReceipt(Game game, TickManager manager, int tick)
            {
                Game = game;
                Manager = manager;
                Tick = tick;
            }

            internal bool Matches(Game game, TickManager manager, int tick) =>
                ReferenceEquals(Game, game) && ReferenceEquals(Manager, manager) && Tick == tick;
        }

        private static MethodBase TargetMethod()
        {
            if (!ModsConfig.IsActive(PackageId)) return null;
            var type = AccessTools.TypeByName("RimThreaded.RimThreaded");
            var method = type?.GetMethod("MainThreadWaitLoop", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                null, new[] { typeof(TickManager) }, null);
            return method != null && method.ReturnType == typeof(void) && !method.ContainsGenericParameters ? method : null;
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Prefix(TickManager __0)
        {
            var game = Current.Game;
            if (!ModsConfig.IsActive(PackageId) || !ModsConfig.OdysseyActive || !UnityData.IsInMainThread ||
                game == null || __0 == null || !ReferenceEquals(game.tickManager, __0) || preparationDepth != 0) return;
            int tick = __0.TicksGame;
            if (Volatile.Read(ref prepared)?.Matches(game, __0, tick) == true) return;

            // Invalidate the old receipt before attempting a new pass. A thrown
            // prefix must propagate before dispatch, never certify preparation.
            Volatile.Write(ref prepared, null);
            preparationDepth++;
            try
            {
                // Reflection keeps this call on the patched Content method even
                // when an optimized caller could otherwise inline its body.
                ContentPrefix.Invoke(null, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
            finally
            {
                preparationDepth--;
            }

            if (ReferenceEquals(Current.Game, game) && ReferenceEquals(game.tickManager, __0) && __0.TicksGame == tick)
                Volatile.Write(ref prepared, new PreparationReceipt(game, __0, tick));
        }

        // PatchAll processes this class independently of the optional hook. Its
        // existence is not evidence that the hook installed or has ever run.
        [HarmonyPatch(typeof(MagicalFieldTickPatch), nameof(MagicalFieldTickPatch.Prefix))]
        private static class ContentGate
        {
            private static bool Prepare() => ModsConfig.IsActive(PackageId);

            private static bool Prefix()
            {
                if (preparationDepth != 0 || !ModsConfig.IsActive(PackageId) || UnityData.IsInMainThread) return true;
                var game = Current.Game;
                var manager = game?.tickManager;
                return manager == null || Volatile.Read(ref prepared)?.Matches(game, manager, manager.TicksGame) != true;
            }
        }
    }
}
