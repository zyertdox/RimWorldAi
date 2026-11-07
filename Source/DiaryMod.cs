using HarmonyLib;
using UnityEngine;
using Verse;

namespace SimplePawnDiary
{
    public sealed class DiarySettings : ModSettings
    {
        public string endpoint = "http://127.0.0.1:8765/v1/chat/completions";
        public string model = "gpt-6-luna";
        public string apiKey = "";
        public override void ExposeData()
        {
            Scribe_Values.Look(ref endpoint, "endpoint", "http://127.0.0.1:8765/v1/chat/completions");
            Scribe_Values.Look(ref model, "model", "gpt-6-luna");
            Scribe_Values.Look(ref apiKey, "apiKey", "");
        }
    }

    public sealed class DiaryMod : Mod
    {
        internal static DiarySettings Settings;
        public DiaryMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<DiarySettings>();
            new Harmony("andrei.simplepawndiary").PatchAll();
        }
        public override string SettingsCategory() { return "Простой дневник"; }
        public override void DoSettingsWindowContents(Rect rect)
        {
            var listing = new Listing_Standard();
            listing.Begin(rect);
            listing.Label("Адрес Chat Completions");
            Settings.endpoint = listing.TextEntry(Settings.endpoint ?? "");
            listing.Label("Модель");
            Settings.model = listing.TextEntry(Settings.model ?? "");
            listing.Label("Ключ доступа (для локального моста можно оставить пустым)");
            Settings.apiKey = listing.TextEntry(Settings.apiKey ?? "");
            listing.GapLine();
            listing.Label("Дневник создаётся только по кнопке. Первая запись использует всю сохранившуюся историю. Ошибка запроса не изменяет период.\n\nКопии штатных событий и дневники сохраняются вместе с игрой. Память ИИ и автоматические пересказы не используются.");
            listing.End();
        }
    }

    [HarmonyPatch(typeof(PlayLog), nameof(PlayLog.Add))]
    public static class SocialLogPatch
    {
        public static void Postfix(LogEntry entry)
        {
            if (Current.ProgramState == ProgramState.Playing) DiaryGameComponent.Current?.QueueLog(entry, false);
        }
    }

    [HarmonyPatch(typeof(BattleLog), nameof(BattleLog.Add))]
    public static class BattleLogPatch
    {
        public static void Postfix(LogEntry entry)
        {
            if (Current.ProgramState == ProgramState.Playing) DiaryGameComponent.Current?.QueueLog(entry, true);
        }
    }

    [HarmonyPatch(typeof(RimWorld.Archive), nameof(RimWorld.Archive.Add))]
    public static class ArchivePatch
    {
        public static void Postfix(RimWorld.IArchivable archivable, bool __result)
        {
            if (__result && Current.ProgramState == ProgramState.Playing) DiaryGameComponent.Current?.CaptureArchiveSafely(archivable);
        }
    }

    [HarmonyPatch(typeof(Verse.Profile.MemoryUtility), nameof(Verse.Profile.MemoryUtility.ClearAllMapsAndWorld))]
    public static class LeavingGamePatch
    {
        public static void Prefix() { DiaryGameComponent.Current?.CancelGeneration(); }
    }
}
