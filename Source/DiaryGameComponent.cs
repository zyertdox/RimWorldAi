using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace SimplePawnDiary
{
    public sealed class DiaryGameComponent : GameComponent
    {
        private readonly Game owner;
        private List<NativeEvent> events = new List<NativeEvent>();
        private List<PawnDiary> diaries = new List<PawnDiary>();
        private long lastSequence;
        private Dictionary<string, NativeEvent> eventIndex;
        private Dictionary<int, PawnDiary> pawnIndex;
        private readonly Dictionary<LogEntry, bool> pendingLogs = new Dictionary<LogEntry, bool>();
        private readonly Queue<Action> completed = new Queue<Action>();
        private CancellationTokenSource cancellation;
        public bool Busy { get; private set; }
        public string BusyPawn { get; private set; }
        public string Status { get; private set; }
        public IReadOnlyList<PawnDiary> Diaries { get { return diaries; } }
        public int StoredEventCount { get { return events.Count; } }
        public static DiaryGameComponent Current { get { return Verse.Current.Game == null ? null : Verse.Current.Game.GetComponent<DiaryGameComponent>(); } }

        public DiaryGameComponent(Game game) { owner = game; }

        private void EnsureIndices()
        {
            if (eventIndex == null) eventIndex = events.Where(e => e != null && !string.IsNullOrEmpty(e.key)).GroupBy(e => e.key).ToDictionary(g => g.Key, g => g.First());
            if (pawnIndex == null) pawnIndex = diaries.Where(d => d != null).GroupBy(d => d.pawnId).ToDictionary(g => g.Key, g => g.First());
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            ImportAvailable();
            Log.Message("[Простой дневник] Загружено копий штатных событий: " + events.Count + ". Автогенерация отключена.");
        }

        public PawnDiary RegisterPawn(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return null;
            EnsureIndices();
            PawnDiary diary;
            if (!pawnIndex.TryGetValue(pawn.thingIDNumber, out diary))
            {
                diary = new PawnDiary { pawnId = pawn.thingIDNumber };
                pawnIndex.Add(diary.pawnId, diary);
                diaries.Add(diary);
            }
            diary.name = pawn.LabelShortCap.ToString();
            diary.gender = pawn.gender == Gender.Female ? "женский" : pawn.gender == Gender.Male ? "мужской" : "не определён";
            return diary;
        }

        public void QueueLog(LogEntry entry, bool battle)
        {
            if (entry != null) pendingLogs[entry] = battle;
        }

        private void FlushLogs()
        {
            if (pendingLogs.Count == 0) return;
            var pending = pendingLogs.ToArray();
            pendingLogs.Clear();
            foreach (var pair in pending) CaptureLogSafely(pair.Key, pair.Value);
        }

        private static string Clean(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? "" : Regex.Replace(text, "<[^>]+>", "").Trim();
        }

        private void Upsert(NativeEvent record)
        {
            EnsureIndices();
            NativeEvent existing;
            if (eventIndex.TryGetValue(record.key, out existing))
            {
                // Keep the captured text and pawn IDs even if the game later discards native references.
                return;
            }
            record.sequence = ++lastSequence;
            events.Add(record);
            eventIndex.Add(record.key, record);
        }

        private void CaptureLogSafely(LogEntry entry, bool battle)
        {
            try
            {
                if (entry == null) return;
                var record = new NativeEvent {
                    key = (battle ? "battle:" : "social:") + entry.GetUniqueLoadID(),
                    source = battle ? "боевой журнал" : "социальный журнал",
                    gameTick = Math.Max(0, entry.Tick - (Find.TickManager.TicksAbs - Find.TickManager.TicksGame)),
                    text = Clean(entry.ToGameStringFromPOV(null)), colony = false
                };
                foreach (Pawn pawn in entry.GetConcerns().OfType<Pawn>().Distinct())
                {
                    RegisterPawn(pawn);
                    record.perspectives.Add(new EventPerspective { pawnId = pawn.thingIDNumber, text = Clean(entry.ToGameStringFromPOV(pawn)) });
                }
                if (record.text.Length != 0 || record.perspectives.Any(p => p.text.Length != 0)) Upsert(record);
            }
            catch (Exception ex) { Log.WarningOnce("[Простой дневник] Не удалось прочитать запись штатного журнала: " + ex, 18893001); }
        }

        public void CaptureArchiveSafely(IArchivable archived)
        {
            try
            {
                if (archived == null) return;
                string label = Clean(archived.ArchivedLabel);
                string detail = Clean(archived.ArchivedTooltip);
                if (label.Length == 0 && detail.Length == 0) return;
                var reference = archived as ILoadReferenceable;
                string nativeId = reference == null ? StableHash(label + "\n" + detail).ToString("X16") : reference.GetUniqueLoadID();
                Upsert(new NativeEvent {
                    key = "archive:" + archived.GetType().FullName + ":" + archived.CreatedTicksGame + ":" + nativeId,
                    source = "событие колонии", colony = true,
                    gameTick = Math.Max(0, archived.CreatedTicksGame),
                    text = label == detail || label.Length == 0 ? detail : label + ". " + detail
                });
            }
            catch (Exception ex) { Log.WarningOnce("[Простой дневник] Не удалось прочитать уведомление игры: " + ex, 18893002); }
        }

        private static ulong StableHash(string text)
        {
            unchecked { ulong hash = 14695981039346656037UL; foreach (char c in text) { hash ^= c; hash *= 1099511628211UL; } return hash; }
        }

        public void ImportAvailable()
        {
            FlushLogs();
            // Seed oldest first. Subsequent imports preserve existing copies and do not duplicate them.
            if (Find.PlayLog != null)
                foreach (LogEntry entry in Find.PlayLog.AllEntries.OrderBy(e => e.Tick).ToArray()) CaptureLogSafely(entry, false);
            if (Find.BattleLog != null)
                foreach (LogEntry entry in Find.BattleLog.Battles.SelectMany(b => b.Entries).OrderBy(e => e.Tick).ToArray()) CaptureLogSafely(entry, true);
            if (Find.Archive != null)
                foreach (IArchivable archived in Find.Archive.ArchivablesListForReading.OrderBy(a => a.CreatedTicksGame).ToArray()) CaptureArchiveSafely(archived);
            if (Find.LetterStack != null)
                foreach (Letter letter in Find.LetterStack.LettersListForReading.ToArray()) CaptureArchiveSafely(letter);
        }

        public List<EventLine> GetLines(PawnDiary diary, long through = long.MaxValue, long? after = null)
        {
            var result = new List<EventLine>();
            foreach (NativeEvent record in events)
            {
                if (!DiaryPolicy.InRange(record.sequence, after ?? diary.cursor, through)) continue;
                EventPerspective perspective = record.perspectives.FirstOrDefault(p => p.pawnId == diary.pawnId);
                if (!record.colony && perspective == null) continue;
                string text = perspective == null || string.IsNullOrWhiteSpace(perspective.text) ? record.text : perspective.text;
                if (!string.IsNullOrWhiteSpace(text)) result.Add(new EventLine { Sequence = record.sequence, Tick = record.gameTick, Source = record.source, Text = text });
            }
            return result.OrderBy(line => line.Tick).ThenBy(line => line.Sequence).ToList();
        }

        public void Generate(PawnDiary diary)
        {
            if (Busy || diary == null) return;
            ImportAvailable();
            long expectedCursor = diary.cursor;
            long through = lastSequence;
            int throughTick = Find.TickManager.TicksGame;
            List<EventLine> lines = GetLines(diary, through);
            if (lines.Count == 0) { Status = "Нет новых событий для этой пешки."; return; }
            string user = DiaryPolicy.BuildUserPrompt(diary.name, diary.gender, throughTick, lines);
            DiarySettings settings = DiaryMod.Settings;
            string endpoint = settings.endpoint, model = settings.model, key = settings.apiKey;
            cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            Busy = true;
            BusyPawn = diary.name;
            Status = "Создаётся запись: " + diary.name + ". Можно продолжать играть.";
            Task.Run(() => {
                string body = null, error = null;
                try { body = ApiClient.Generate(endpoint, model, key, user, token); }
                catch (OperationCanceledException) { error = "Генерация отменена. События сохранены."; }
                catch (Exception ex) { error = "Не удалось создать запись: " + ex.Message + "\nПериод не изменён; можно повторить запрос."; }
                lock (completed) completed.Enqueue(() => {
                    Busy = false;
                    cancellation.Dispose();
                    cancellation = null;
                    if (!ReferenceEquals(Verse.Current.Game, owner)) return;
                    if (error != null) { Status = error; return; }
                    if (!DiaryPolicy.TryCommit(ref diary.cursor, expectedCursor, through, body)) { Status = "Период изменился; ответ не сохранён. Повторите запрос."; return; }
                    diary.entries.Add(new DiaryEntry { text = body, fromTick = lines[0].Tick, throughTick = throughTick,
                        eventCount = lines.Count, afterSequence = expectedCursor, throughSequence = through });
                    Status = "Запись сохранена: " + diary.name + ". Исходных событий: " + lines.Count + ".";
                    Messages.Message(Status, MessageTypeDefOf.PositiveEvent, false);
                });
            });
        }

        public void CancelGeneration() { if (cancellation != null) cancellation.Cancel(); }

        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            FlushLogs();
            while (true)
            {
                Action action;
                lock (completed) { if (completed.Count == 0) break; action = completed.Dequeue(); }
                action();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving) FlushLogs();
            Scribe_Collections.Look(ref events, "nativeDiaryEvents", LookMode.Deep);
            Scribe_Collections.Look(ref diaries, "nativePawnDiaries", LookMode.Deep);
            Scribe_Values.Look(ref lastSequence, "nativeDiaryLastSequence");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (events == null) events = new List<NativeEvent>();
                if (diaries == null) diaries = new List<PawnDiary>();
                eventIndex = null; pawnIndex = null;
                if (events.Count != 0) lastSequence = Math.Max(lastSequence, events.Max(e => e.sequence));
            }
        }
    }
}
