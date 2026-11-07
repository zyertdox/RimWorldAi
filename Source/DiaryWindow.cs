using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SimplePawnDiary
{
    public sealed class MainTabWindow_Diary : MainTabWindow
    {
        private PawnDiary selectedPawn;
        private DiaryEntry selectedEntry;
        private Vector2 pawnScroll, entryScroll, textScroll;
        private bool showEvents;
        private string search = "";
        private List<PawnDiary> available = new List<PawnDiary>();
        private List<EventLine> pendingLines = new List<EventLine>();
        private int refreshFrame = -100;
        private int lastEntryCount = -1;
        public override Vector2 RequestedTabSize { get { return new Vector2(1100f, 680f); } }

        public override void PreOpen()
        {
            base.PreOpen();
            selectedPawn = null; selectedEntry = null;
            showEvents = false;
            refreshFrame = -100;
            lastEntryCount = -1;
            Refresh(true);
        }

        private void Refresh(bool force = false)
        {
            var component = DiaryGameComponent.Current;
            if (component == null || (!force && Time.frameCount - refreshFrame < 60)) return;
            refreshFrame = Time.frameCount;
            var ids = new HashSet<int>();
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            {
                if (!pawn.RaceProps.Humanlike || pawn.IsShambler) continue;
                component.RegisterPawn(pawn);
                ids.Add(pawn.thingIDNumber);
            }
            available = component.Diaries.Where(d => ids.Contains(d.pawnId) || d.entries.Count != 0)
                .OrderBy(d => ids.Contains(d.pawnId) ? 0 : 1).ThenBy(d => d.name).ToList();
            if (selectedPawn == null)
            {
                Pawn selected = Find.Selector.SingleSelectedThing as Pawn;
                selectedPawn = selected == null ? available.FirstOrDefault() : available.FirstOrDefault(d => d.pawnId == selected.thingIDNumber) ?? available.FirstOrDefault();
                selectedEntry = selectedPawn == null ? null : selectedPawn.entries.LastOrDefault();
            }
            if (selectedPawn != null)
            {
                pendingLines = component.GetLines(selectedPawn);
                if (lastEntryCount != selectedPawn.entries.Count)
                {
                    selectedEntry = selectedPawn.entries.LastOrDefault();
                    lastEntryCount = selectedPawn.entries.Count;
                    textScroll = Vector2.zero;
                }
            }
        }

        public override void DoWindowContents(Rect rect)
        {
            var component = DiaryGameComponent.Current;
            if (component == null) { Widgets.Label(rect, "Дневник доступен в загруженной игре."); return; }
            Refresh();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), "Простой дневник");
            Text.Font = GameFont.Small;
            float top = rect.y + 40f;
            float panelHeight = rect.height - 150f;
            var pawnRect = new Rect(rect.x, top, 205f, panelHeight);
            var entriesRect = new Rect(pawnRect.xMax + 10f, top, 220f, panelHeight);
            var bodyRect = new Rect(entriesRect.xMax + 10f, top, rect.xMax - entriesRect.xMax - 10f, panelHeight);
            DrawPawns(pawnRect);
            DrawEntries(entriesRect);
            DrawText(bodyRect, component);
            float bottom = top + panelHeight + 8f;
            string info = selectedPawn == null ? "Выберите пешку." : selectedPawn.name + ": новых событий — " + pendingLines.Count + ". Всего сохранено событий: " + component.StoredEventCount + ".";
            Widgets.Label(new Rect(rect.x, bottom, rect.width - 210f, 28f), info);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && selectedPawn != null && !component.Busy;
            if (Widgets.ButtonText(new Rect(rect.xMax - 200f, bottom, 200f, 32f), "Сгенерировать"))
            {
                component.Generate(selectedPawn);
                Refresh(true);
            }
            GUI.enabled = wasEnabled;
            if (component.Busy && Widgets.ButtonText(new Rect(rect.xMax - 200f, bottom + 38f, 200f, 28f), "Отменить генерацию")) component.CancelGeneration();
            if (!string.IsNullOrEmpty(component.Status)) Widgets.Label(new Rect(rect.x, bottom + 36f, rect.width - 215f, 72f), component.Status);
        }

        private void DrawPawns(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            search = Widgets.TextField(new Rect(rect.x + 7f, rect.y + 7f, rect.width - 14f, 28f), search);
            var matches = available.Where(d => string.IsNullOrWhiteSpace(search) || d.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Rect outer = new Rect(rect.x + 5f, rect.y + 42f, rect.width - 10f, rect.height - 47f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Math.Max(outer.height, matches.Count * 34f));
            Widgets.BeginScrollView(outer, ref pawnScroll, view);
            float y = 0f;
            foreach (PawnDiary diary in matches)
            {
                var row = new Rect(0f, y, view.width, 32f);
                if (diary == selectedPawn) Widgets.DrawHighlightSelected(row);
                Widgets.Label(row.ContractedBy(4f), diary.name);
                if (Widgets.ButtonInvisible(row))
                {
                    selectedPawn = diary; selectedEntry = diary.entries.LastOrDefault();
                    entryScroll = textScroll = Vector2.zero; showEvents = false; Refresh(true);
                }
                y += 34f;
            }
            Widgets.EndScrollView();
        }

        private void DrawEntries(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 26f), "Записи дневника");
            Rect outer = new Rect(rect.x + 5f, rect.y + 40f, rect.width - 10f, rect.height - 45f);
            int count = selectedPawn == null ? 0 : selectedPawn.entries.Count;
            Rect view = new Rect(0f, 0f, outer.width - 18f, Math.Max(outer.height, (count + 1) * 63f));
            Widgets.BeginScrollView(outer, ref entryScroll, view);
            var pendingRow = new Rect(0f, 0f, view.width, 60f);
            if (selectedEntry == null) Widgets.DrawHighlightSelected(pendingRow);
            Widgets.Label(pendingRow.ContractedBy(5f), "Новые события\nДля следующей записи");
            if (Widgets.ButtonInvisible(pendingRow)) { selectedEntry = null; showEvents = true; textScroll = Vector2.zero; }
            float y = 63f;
            if (selectedPawn != null)
                for (int i = count - 1; i >= 0; i--)
                {
                    DiaryEntry entry = selectedPawn.entries[i];
                    var row = new Rect(0f, y, view.width, 60f);
                    if (entry == selectedEntry) Widgets.DrawHighlightSelected(row);
                    Widgets.Label(row.ContractedBy(5f), DiaryPolicy.TimeLabel(entry.throughTick) + "\nСобытий: " + entry.eventCount);
                    if (Widgets.ButtonInvisible(row)) { selectedEntry = entry; showEvents = false; textScroll = Vector2.zero; }
                    y += 63f;
                }
            Widgets.EndScrollView();
        }

        private void DrawText(Rect rect, DiaryGameComponent component)
        {
            Widgets.DrawMenuSection(rect);
            string label = showEvents ? "Показать дневник" : "Показать исходные события";
            if (Widgets.ButtonText(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 28f), label)) { showEvents = !showEvents; textScroll = Vector2.zero; }
            string body;
            if (selectedPawn == null) body = "Выберите пешку слева.";
            else if (showEvents)
            {
                var lines = selectedEntry == null ? pendingLines : component.GetLines(selectedPawn, selectedEntry.throughSequence, selectedEntry.afterSequence);
                body = lines.Count == 0 ? "В этом периоде нет доступных событий." : DiaryPolicy.BuildSourceText(lines);
            }
            else body = selectedEntry == null ? "Первая запись будет создана по всей доступной истории.\n\nИсходные события можно посмотреть кнопкой выше. Дневник создаётся только по вашему запросу." : selectedEntry.text;
            Rect outer = new Rect(rect.x + 10f, rect.y + 46f, rect.width - 20f, rect.height - 56f);
            Rect view = new Rect(0f, 0f, outer.width - 18f, Math.Max(outer.height, Text.CalcHeight(body, outer.width - 18f) + 15f));
            Widgets.BeginScrollView(outer, ref textScroll, view);
            Widgets.Label(new Rect(0f, 0f, view.width, view.height), body);
            Widgets.EndScrollView();
        }
    }
}
