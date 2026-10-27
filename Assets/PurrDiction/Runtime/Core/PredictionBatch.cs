using System;
using System.Collections.Generic;

namespace PurrNet.Prediction
{
    /// <summary>
    /// Simulates a group of identities together instead of one after another, so the group can spread its work over
    /// worker threads and batched physics queries. An identity joins a batch by returning it from
    /// <see cref="PredictedIdentity.simulationBatch"/>. In each simulate pass the manager runs every member's modules
    /// and first-tick start, then calls the batch once, at the position of its first member that simulates in this
    /// pass, and skips the members after that. The batch runs each member's tick itself (from <c>currentInput</c> into
    /// <c>currentState</c>), so no member's result may depend on the order members are processed in.
    /// </summary>
    public interface IPredictionBatch
    {
        void SimulateBatch(ulong tick, float delta, IReadOnlyList<PredictedIdentity> members);
    }

    /// <summary>
    /// While alive, anything spawned is attributed to one identity, as it is while that identity simulates alone.
    /// </summary>
    public readonly struct PredictionSpawnCreatorScope : IDisposable
    {
        private readonly PredictionManager _manager;
        private readonly PredictionManager.SpawnCreatorScope _previous;

        internal PredictionSpawnCreatorScope(PredictionManager manager, PredictedComponentID creator)
        {
            _manager = manager;
            _previous = manager ? manager.EnterSpawnCreator(creator) : default;
        }

        public void Dispose()
        {
            if (_manager)
                _manager.ExitSpawnCreator(_previous);
        }
    }

    public abstract partial class PredictedIdentity
    {
        /// <summary>
        /// The batch this identity is simulated in, or null to simulate alone. Read once per simulate pass.
        /// </summary>
        public virtual IPredictionBatch simulationBatch => null;

        /// <summary>
        /// Attributes spawns to this identity until disposed, for work a batch does on this identity's behalf.
        /// </summary>
        public PredictionSpawnCreatorScope EnterSpawnCreatorScope() => new(predictionManager, id);

        internal void RunBatchedPreSimulate(ulong tick, float delta)
        {
            var previousCreator = predictionManager.EnterSpawnCreator(id);
            try
            {
                SimulateModules(tick, delta);
                BatchedSimulationStart();
            }
            finally
            {
                predictionManager.ExitSpawnCreator(previousCreator);
            }
        }

        internal virtual void BatchedSimulationStart() { }
    }
}
