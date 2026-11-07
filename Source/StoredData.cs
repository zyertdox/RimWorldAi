using System.Collections.Generic;
using Verse;

namespace SimplePawnDiary
{
    public sealed class EventPerspective : IExposable
    {
        public int pawnId;
        public string text;
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref text, "text", "");
        }
    }

    public sealed class NativeEvent : IExposable
    {
        public string key, source, text;
        public long sequence;
        public int gameTick;
        public bool colony;
        public List<EventPerspective> perspectives = new List<EventPerspective>();
        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key", "");
            Scribe_Values.Look(ref source, "source", "");
            Scribe_Values.Look(ref text, "text", "");
            Scribe_Values.Look(ref sequence, "sequence");
            Scribe_Values.Look(ref gameTick, "gameTick");
            Scribe_Values.Look(ref colony, "colony");
            Scribe_Collections.Look(ref perspectives, "perspectives", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && perspectives == null) perspectives = new List<EventPerspective>();
        }
    }

    public sealed class DiaryEntry : IExposable
    {
        public string text;
        public int fromTick, throughTick, eventCount;
        public long afterSequence, throughSequence;
        public void ExposeData()
        {
            Scribe_Values.Look(ref text, "text", "");
            Scribe_Values.Look(ref fromTick, "fromTick");
            Scribe_Values.Look(ref throughTick, "throughTick");
            Scribe_Values.Look(ref eventCount, "eventCount");
            Scribe_Values.Look(ref afterSequence, "afterSequence");
            Scribe_Values.Look(ref throughSequence, "throughSequence");
        }
    }

    public sealed class PawnDiary : IExposable
    {
        public int pawnId;
        public string name, gender = "неизвестен";
        public long cursor;
        public List<DiaryEntry> entries = new List<DiaryEntry>();
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref name, "name", "");
            Scribe_Values.Look(ref gender, "gender", "неизвестен");
            Scribe_Values.Look(ref cursor, "cursor");
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && entries == null) entries = new List<DiaryEntry>();
        }
    }
}
