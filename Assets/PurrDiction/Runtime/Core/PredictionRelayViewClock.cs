using System;

namespace PurrNet.Prediction
{
    /// <summary>
    /// Presents server-relayed identities on the server's own timeline. Their state changes only when a
    /// verified frame arrives, and frames arrive unevenly: a server that runs two ticks in one frame sends
    /// them together, and the network bunches packets. Presented at the local tick, that unevenness shows as
    /// hold, hold, jump. This clock follows an estimate of the server's current tick, taken from the
    /// earliest-arriving frames, and stays behind it by the deepest the newest frame has recently trailed
    /// that estimate, so the sample after the presented tick has almost always arrived already.
    /// </summary>
    internal sealed class PredictionRelayViewClock
    {
        // How fast the arrival envelope and the delay relax when frames stop being early or late. Both
        // rise at once, so a burst of late frames is covered immediately and released over a few seconds.
        private const double EnvelopeDecayTicksPerSecond = 0.5;
        private const double DepthDecayTicksPerSecond = 1.0;
        private const double DepthMarginTicks = 0.25;

        // The clock closes on its target by running at most this much faster or slower than real time,
        // and jumps only when it is further off than any steady delay would put it.
        private const double MaxRateAdjust = 0.1;
        private const double SnapErrorTicks = 8;

        private readonly int _tickRate;
        private bool _seeded;
        private double _envelope;
        private double _depth;
        private double _lastNow;
        private ulong _newest;

        public PredictionRelayViewClock(int tickRate)
        {
            _tickRate = Math.Max(1, tickRate);
        }

        public int tickRate => _tickRate;

        /// <summary>
        /// Server tick currently being presented, including the fraction between two ticks.
        /// </summary>
        public double viewTick { get; private set; }

        /// <summary>
        /// How many ticks the presented tick stays behind the estimated current server tick.
        /// </summary>
        public double delayTicks => _depth + DepthMarginTicks;

        public void NoteArrival(ulong serverTick, double now)
        {
            if (_seeded && serverTick <= _newest)
                return;

            double offset = serverTick - now * _tickRate;
            if (!_seeded)
            {
                _seeded = true;
                _envelope = offset;
                _depth = 1d;
                _lastNow = now;
                viewTick = serverTick - delayTicks;
            }
            else if (offset > _envelope)
            {
                _envelope = offset;
            }

            _newest = serverTick;
        }

        public double Advance(double now)
        {
            if (!_seeded)
                return viewTick;

            double dt = Math.Max(0d, now - _lastNow);
            _lastNow = now;

            _envelope -= EnvelopeDecayTicksPerSecond * dt;
            double serverNow = now * _tickRate + _envelope;
            double trail = Math.Max(0d, serverNow - _newest);
            _depth = Math.Max(trail, _depth - DepthDecayTicksPerSecond * dt);

            double target = serverNow - delayTicks;
            double step = dt * _tickRate;
            double next = viewTick + step;
            double error = target - next;
            if (Math.Abs(error) > SnapErrorTicks)
            {
                viewTick = target;
                return viewTick;
            }

            double limit = step * MaxRateAdjust;
            next += error > limit ? limit : error < -limit ? -limit : error;
            viewTick = Math.Max(viewTick, next);
            return viewTick;
        }
    }
}
