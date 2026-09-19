using System;
using System.Collections.Generic;

namespace ReMap.Standalone.Core
{
    /// <summary>Transactional edits: a rejected edit never alters the document or history.</summary>
    public sealed class MapSession
    {
        private MapDocument document = new MapDocument();
        private readonly struct State {
            public readonly MapDocument Document;
            public readonly long Revision;
            public State(MapDocument document, long revision) { Document = document; Revision = revision; }
        }
        private readonly List<State> undo = new List<State>();
        private readonly List<State> redo = new List<State>();
        private long nextRevision;
        private bool continuousEditing, continuousEditRemembered;
        public long Revision { get; private set; }
        private readonly int historyLimit;
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public MapSession(int historyLimit = 64)
        {
            if (historyLimit < 1) throw new ArgumentOutOfRangeException(nameof(historyLimit));
            this.historyLimit = historyLimit;
        }

        public MapDocument Snapshot() => document.Copy();

        public void Edit(Action<MapDocument> edit)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            var next = document.Copy();
            edit(next);
            next.Validate();
            if (!continuousEditing || !continuousEditRemembered)
            {
                Remember(undo, new State(document, Revision));
                continuousEditRemembered = continuousEditing;
            }
            document = next.Copy(); // The caller cannot retain a writable reference to the session.
            Revision = ++nextRevision;
            redo.Clear();
        }

        public void Replace(MapDocument next)
        {
            EndContinuousEdit();
            if (next == null) throw new ArgumentException(L.T("#EMPTY_SAVE_FILE"));
            next.Validate();
            var copy = next.Copy();
            Remember(undo, new State(document, Revision));
            document = copy;
            Revision = ++nextRevision;
            redo.Clear();
        }

        public bool Undo()
        {
            EndContinuousEdit();
            if (!CanUndo) return false;
            Remember(redo, new State(document, Revision));
            var previous = Pop(undo); document = previous.Document; Revision = previous.Revision;
            return true;
        }

        public bool Redo()
        {
            EndContinuousEdit();
            if (!CanRedo) return false;
            Remember(undo, new State(document, Revision));
            var next = Pop(redo); document = next.Document; Revision = next.Revision;
            return true;
        }

        public void BeginContinuousEdit()
        {
            if (continuousEditing) return;
            continuousEditing = true;
            continuousEditRemembered = false;
        }

        public void EndContinuousEdit()
        {
            continuousEditing = false;
            continuousEditRemembered = false;
        }

        private void Remember(List<State> history, State value)
        {
            history.Add(value);
            if (history.Count > historyLimit) history.RemoveAt(0);
        }

        private static State Pop(List<State> history)
        {
            var value = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            return value;
        }

        public static float Snap(float value, float step)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || float.IsNaN(step) ||
                float.IsInfinity(step) || step <= 0)
                throw new ArgumentOutOfRangeException(nameof(step));
            return (float)(Math.Round(value / (double)step, MidpointRounding.AwayFromZero) * step);
        }
    }
}
