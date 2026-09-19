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
            public readonly object Auxiliary;
            public State(MapDocument document, long revision, object auxiliary)
            { Document = document; Revision = revision; Auxiliary = auxiliary; }
        }
        private readonly List<State> undo = new List<State>();
        private readonly List<State> redo = new List<State>();
        private long nextRevision;
        private bool continuousEditing, continuousEditRemembered;
        private Func<object> captureAuxiliaryHistory;
        private Action<object> restoreAuxiliaryHistory;
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

        /// <summary>Includes transient editor state in the same chronological history as document edits.</summary>
        public void ConfigureAuxiliaryHistory(Func<object> capture, Action<object> restore)
        {
            captureAuxiliaryHistory = capture ?? throw new ArgumentNullException(nameof(capture));
            restoreAuxiliaryHistory = restore ?? throw new ArgumentNullException(nameof(restore));
        }

        public void Edit(Action<MapDocument> edit)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            var next = document.Copy();
            edit(next);
            next.Validate();
            if (!continuousEditing || !continuousEditRemembered)
            {
                Remember(undo, CaptureState());
                continuousEditRemembered = continuousEditing;
            }
            document = next.Copy(); // The caller cannot retain a writable reference to the session.
            Revision = ++nextRevision;
            redo.Clear();
        }

        /// <summary>Records an editor-only change without changing the document revision.</summary>
        public void EditAuxiliary(Action edit)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            var previous = CaptureState();
            try { edit(); }
            catch
            {
                RestoreAuxiliary(previous.Auxiliary);
                throw;
            }
            if (!continuousEditing || !continuousEditRemembered)
            {
                Remember(undo, previous);
                continuousEditRemembered = continuousEditing;
            }
            redo.Clear();
        }

        public void Replace(MapDocument next)
        {
            EndContinuousEdit();
            if (next == null) throw new ArgumentException(L.T("#EMPTY_SAVE_FILE"));
            next.Validate();
            var copy = next.Copy();
            Remember(undo, CaptureState());
            document = copy;
            Revision = ++nextRevision;
            redo.Clear();
        }

        public bool Undo()
        {
            EndContinuousEdit();
            if (!CanUndo) return false;
            Remember(redo, CaptureState());
            Restore(Pop(undo));
            return true;
        }

        public bool Redo()
        {
            EndContinuousEdit();
            if (!CanRedo) return false;
            Remember(undo, CaptureState());
            Restore(Pop(redo));
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

        private State CaptureState() => new State(document, Revision, captureAuxiliaryHistory?.Invoke());

        private void Restore(State state)
        {
            document = state.Document;
            Revision = state.Revision;
            RestoreAuxiliary(state.Auxiliary);
        }

        private void RestoreAuxiliary(object state)
        {
            if (state != null) restoreAuxiliaryHistory?.Invoke(state);
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
