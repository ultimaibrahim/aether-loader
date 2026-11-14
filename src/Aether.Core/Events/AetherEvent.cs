using System;
using System.Collections.Generic;
using System.Linq;

namespace Aether.Core.Events
{
    public enum EventResult
    {
        Pass = 0,
        Interrupt = 1,
        Consume = 2
    }

    /// <summary>
    /// Array-backed, zero-allocation event dispatcher inspired by Fabric API.
    /// Event listeners are compiled into a flat array sorted topologically by phase.
    /// Invocations run with 0 heap allocations on per-frame hot loops.
    /// </summary>
    public sealed class AetherEvent<TDelegate> where TDelegate : Delegate
    {
        public const string EarlyPhase = "early";
        public const string DefaultPhase = "default";
        public const string LatePhase = "late";

        private readonly Func<TDelegate[], TDelegate> _invokerFactory;
        private readonly List<(string Phase, TDelegate Handler)> _rawListeners = new();
        private readonly List<string> _phaseOrder = new() { EarlyPhase, DefaultPhase, LatePhase };

        public TDelegate Invoker { get; private set; }

        public AetherEvent(Func<TDelegate[], TDelegate> invokerFactory)
        {
            _invokerFactory = invokerFactory ?? throw new ArgumentNullException(nameof(invokerFactory));
            Invoker = _invokerFactory(Array.Empty<TDelegate>());
        }

        public void AddPhaseOrdering(string firstPhase, string secondPhase)
        {
            int idxFirst = _phaseOrder.IndexOf(firstPhase);
            int idxSecond = _phaseOrder.IndexOf(secondPhase);

            if (idxFirst == -1) { _phaseOrder.Insert(0, firstPhase); idxFirst = 0; }
            if (idxSecond == -1) { _phaseOrder.Add(secondPhase); idxSecond = _phaseOrder.Count - 1; }

            if (idxFirst > idxSecond)
            {
                _phaseOrder.RemoveAt(idxFirst);
                _phaseOrder.Insert(idxSecond, firstPhase);
            }
            Rebuild();
        }

        public void Register(TDelegate handler) => Register(DefaultPhase, handler);

        public void Register(string phase, TDelegate handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _rawListeners.Add((phase, handler));
            if (!_phaseOrder.Contains(phase))
            {
                _phaseOrder.Add(phase);
            }
            Rebuild();
        }

        private void Rebuild()
        {
            var sorted = _rawListeners
                .OrderBy(l => _phaseOrder.IndexOf(l.Phase))
                .Select(l => l.Handler)
                .ToArray();

            Invoker = _invokerFactory(sorted);
        }
    }
}
