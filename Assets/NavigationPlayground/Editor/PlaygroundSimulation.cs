using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NavigationLab.Core;
using NavigationLab.Oracle;

namespace NavigationLab.UnityPlayground
{
    // Published arrays and collections are never changed by the worker. Readers must not change Units.
    public sealed class PlaygroundSnapshot
    {
        public readonly UnitSnapshot[] Units;
        public readonly WorldDefinition World;
        public readonly IReadOnlyDictionary<int, SpawnRequest> Live;
        public readonly int Microstep;
        public readonly int Issued;
        public readonly int Admitted;
        public readonly int Pending;
        public readonly int Deaths;
        public readonly int StableTicks;
        public readonly string Status;
        public readonly string Detail;
        public readonly double LastStepMilliseconds;
        public readonly string LastSpawnFailure;
        public readonly bool Finished;

        internal PlaygroundSnapshot(
            UnitSnapshot[] units,
            WorldDefinition world,
            IReadOnlyDictionary<int, SpawnRequest> live,
            int microstep,
            int issued,
            int admitted,
            int pending,
            int deaths,
            int stableTicks,
            string status,
            string detail,
            double lastStepMilliseconds,
            string lastSpawnFailure,
            bool finished)
        {
            Units = units;
            World = world;
            Live = live;
            Microstep = microstep;
            Issued = issued;
            Admitted = admitted;
            Pending = pending;
            Deaths = deaths;
            StableTicks = stableTicks;
            Status = status;
            Detail = detail;
            LastStepMilliseconds = lastStepMilliseconds;
            LastSpawnFailure = lastSpawnFailure;
            Finished = finished;
        }
    }

    /// <summary>Local observation only. Candidate calls and continuous validation share one background thread.</summary>
    public sealed class PlaygroundSimulation : IDisposable
    {
        private readonly object control = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly PlaygroundInput input;
        private readonly INavigationFactory factory;
        private readonly WorldDefinition initialWorld;
        private readonly HashSet<int> triggeredWorld = new HashSet<int>();
        private readonly HashSet<int> triggeredDeaths = new HashSet<int>();
        private readonly HashSet<int> oldWaiting = new HashSet<int>();
        private readonly List<RequestInput> pending;
        private readonly StabilityWindow finalHold = new StabilityWindow();
        private readonly Stopwatch clock = new Stopwatch();
        private PlaygroundSnapshot latest;
        private Thread worker;
        private INavigator navigator;
        private ContinuousOracle oracle;
        private UnitSnapshot[] current = Array.Empty<UnitSnapshot>();
        private int stopRequested;
        private int paused;
        private int requestedLogicalSteps;
        private int completed;
        private bool disposed;
        private bool finished;
        private bool initialChecked;
        private int microstep;
        private int issued;
        private int admitted;
        private int deathCount;
        private int nextId;
        private double speed = 1;
        private double lastStepMilliseconds;
        private double lastPublicationMilliseconds;
        private string status = "Idle";
        private string detail = "Local interactive observation; not a formal replay report.";
        private string lastSpawnFailure = "";

        public PlaygroundSimulation(PlaygroundInput input, INavigationFactory factory)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.input = input.Copy();
            Require(this.input.deadlineTicks > 0 && this.input.holdTicks >= 0, "Invalid observation deadline or hold duration.");
            initialWorld = this.input.world.ToWorld();
            pending = this.input.requests.ToList();
            issued = pending.Count;
            nextId = pending.Count == 0 ? 1 : pending.Max(item => item.id) + 1;
            initialChecked = this.input.initialSettledCheckTick < 0;
            Publish();
        }

        public PlaygroundSnapshot Latest => Volatile.Read(ref latest);
        public bool IsCompleted => Volatile.Read(ref completed) != 0;

        public void Start()
        {
            lock (control)
            {
                ThrowIfDisposed();
                if (worker != null)
                {
                    throw new InvalidOperationException("Each observation can be started only once.");
                }
                worker = new Thread(Run) { IsBackground = true, Name = "Navigation playground" };
                worker.Start();
            }
        }

        public void Pause(bool value)
        {
            lock (control)
            {
                ThrowIfDisposed();
                Volatile.Write(ref paused, value ? 1 : 0);
                if (!value)
                {
                    Interlocked.Exchange(ref requestedLogicalSteps, 0);
                }
                wake.Set();
            }
        }

        public void SetSpeed(double value)
        {
            if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            lock (control)
            {
                ThrowIfDisposed();
                Volatile.Write(ref speed, value);
                wake.Set();
            }
        }

        public void StepLogicalTick()
        {
            lock (control)
            {
                ThrowIfDisposed();
                if (Volatile.Read(ref paused) == 0)
                {
                    throw new InvalidOperationException("Pause before requesting a 50ms logical step.");
                }
                Interlocked.Increment(ref requestedLogicalSteps);
                wake.Set();
            }
        }

        public void RequestStop()
        {
            lock (control)
            {
                if (!disposed)
                {
                    Volatile.Write(ref stopRequested, 1);
                    wake.Set();
                }
            }
        }

        public void Join()
        {
            Thread active;
            lock (control)
            {
                active = worker;
            }
            if (active != null && active != Thread.CurrentThread)
            {
                active.Join();
            }
        }

        public void Dispose()
        {
            lock (control)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                Volatile.Write(ref stopRequested, 1);
                wake.Set();
            }
            Join();
            wake.Dispose();
        }

        private void Run()
        {
            clock.Start();
            try
            {
                if (Volatile.Read(ref stopRequested) != 0)
                {
                    Finish("Stopped", "Observation stopped before candidate creation.");
                    return;
                }
                var required = (NavigationFeatures)Enum.Parse(typeof(NavigationFeatures), input.requiredFeatures);
                var missing = required & ~factory.Features;
                if (missing != NavigationFeatures.None)
                {
                    Finish("Unsupported", "Candidate does not advertise required capabilities: " + missing + ".");
                    return;
                }
                status = Volatile.Read(ref paused) != 0 ? "Paused" : "Running";
                detail = "Creating candidate on the background thread; this call may take time.";
                Publish();
                oracle = new ContinuousOracle(initialWorld);
                navigator = factory.Create(initialWorld.Clone(), input.requests.Select(item => item.Profile()).Distinct().ToArray());
                current = Freeze();
                Require(current.Length == 0, "Factory created unrequested bodies.");
                oracle.ValidateTransition(current, current, 0);
                detail = "Local observation with continuous collision, lifecycle and settlement checks.";
                Publish();
                Pump();
            }
            catch (Exception error)
            {
                Finish(Volatile.Read(ref stopRequested) != 0 ? "Stopped" : "Failed", error.ToString());
            }
            finally
            {
                try
                {
                    navigator?.Dispose();
                }
                catch (Exception error)
                {
                    Finish(Volatile.Read(ref stopRequested) != 0 ? "Stopped" : "Failed", "Candidate disposal failed: " + error);
                }
                if (!finished)
                {
                    Finish("Stopped", "Observation stopped. Candidate has been disposed.");
                }
                Publish();
                Volatile.Write(ref completed, 1);
            }
        }

        private void Pump()
        {
            double lastWallMilliseconds = clock.Elapsed.TotalMilliseconds;
            double budgetMilliseconds = 0;
            bool wasPaused = Volatile.Read(ref paused) != 0;
            while (!finished && Volatile.Read(ref stopRequested) == 0)
            {
                bool isPaused = Volatile.Read(ref paused) != 0;
                double now = clock.Elapsed.TotalMilliseconds;
                if (isPaused != wasPaused)
                {
                    status = isPaused ? "Paused" : "Running";
                    budgetMilliseconds = 0;
                    lastWallMilliseconds = now;
                    wasPaused = isPaused;
                    Publish();
                }
                if (isPaused)
                {
                    if (TryTakeLogicalStep())
                    {
                        for (int step = 0; step < SimulationProtocol.Substeps && !finished && Volatile.Read(ref stopRequested) == 0; step++)
                        {
                            Advance();
                        }
                        Publish();
                    }
                    else
                    {
                        wake.WaitOne(20);
                    }
                    budgetMilliseconds = 0;
                    lastWallMilliseconds = clock.Elapsed.TotalMilliseconds;
                    continue;
                }
                double multiplier = Volatile.Read(ref speed);
                budgetMilliseconds += Math.Max(0, now - lastWallMilliseconds) * multiplier;
                lastWallMilliseconds = now;
                if (budgetMilliseconds >= SimulationProtocol.MicrostepMilliseconds)
                {
                    Advance();
                    budgetMilliseconds -= SimulationProtocol.MicrostepMilliseconds;
                }
                else
                {
                    int wait = (int)Math.Min(20, Math.Max(1, Math.Ceiling((SimulationProtocol.MicrostepMilliseconds - budgetMilliseconds) / multiplier)));
                    wake.WaitOne(wait);
                }
                if (clock.Elapsed.TotalMilliseconds - lastPublicationMilliseconds >= 50)
                {
                    Publish();
                }
            }
            if (!finished)
            {
                Finish("Stopped", "Observation stopped by the user. This is not a candidate failure.");
            }
        }

        private void Advance()
        {
            int tick = microstep / SimulationProtocol.Substeps;
            if (microstep % SimulationProtocol.Substeps == 0)
            {
                ApplyWorldEvents(tick);
                ApplyDeaths(tick);
                AdmitRequests(tick);
                CheckInitialBatch(tick);
                bool complete = IsComplete(false) && triggeredDeaths.Count == input.deaths.Length
                    && triggeredWorld.Count == input.worldEvents.Length && initialChecked;
                finalHold.Observe(current, complete, microstep);
                if (complete && finalHold.Ticks(microstep) >= input.holdTicks)
                {
                    Finish("Passed", "All required lifecycle events executed; final stationary crowd held for " + input.holdTicks + " logical ticks.");
                    return;
                }
            }
            if ((long)microstep >= (long)input.deadlineTicks * SimulationProtocol.Substeps)
            {
                Finish("Failed", "Simulation deadline: live=" + current.Length + ", pending=" + pending.Count
                    + ", moving=" + current.Count(unit => unit.State == MotionState.Moving) + ", failure=" + lastSpawnFailure);
                return;
            }
            var before = current;
            long started = Stopwatch.GetTimestamp();
            navigator.Step();
            lastStepMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            current = Freeze();
            oracle.ValidateTransition(before, current, SimulationProtocol.MicrostepMilliseconds);
            microstep++;
            finalHold.Validate(current);
        }

        private void ApplyWorldEvents(int tick)
        {
            for (int index = 0; index < input.worldEvents.Length; index++)
            {
                var planned = input.worldEvents[index];
                if (triggeredWorld.Contains(index) || tick < planned.tick || planned.afterSettlement && !IsComplete(true))
                {
                    continue;
                }
                var before = current;
                var change = planned.Change();
                oracle.ValidateWorldChangeInput(change, before);
                Require(navigator.ApplyWorldChange(change.DeepClone()), "Advertised world change rejected.");
                current = Freeze();
                oracle.ApplyChange(change, before, current);
                triggeredWorld.Add(index);
                ResetStability();
            }
        }

        private void ApplyDeaths(int tick)
        {
            bool settled = IsComplete(true);
            for (int index = 0; index < input.deaths.Length; index++)
            {
                var planned = input.deaths[index];
                if (triggeredDeaths.Contains(index) || tick < planned.tick || planned.afterSettlement && !settled)
                {
                    continue;
                }
                var victims = current.Where(unit => !planned.attackersOnly || unit.State == MotionState.Attacking)
                    .Where(unit => planned.range < 0 || oracle.Live[unit.Id].Profile.AttackRangeMm == planned.range)
                    .Where(unit => !planned.requireMoving || unit.State == MotionState.Moving
                        && Point2.Distance(unit.Position, oracle.World.Center(oracle.World.CellAt(unit.Position))) > SimulationProtocol.GeometryToleranceMm)
                    .OrderBy(unit => unit.Id).Take(planned.count).ToArray();
                if (victims.Length != planned.count)
                {
                    Require(planned.afterSettlement || tick <= planned.tick + 100, "Noncentre moving death never became valid.");
                    continue;
                }
                oldWaiting.UnionWith(current.Where(unit => unit.State == MotionState.Waiting).Select(unit => unit.Id));
                foreach (var victim in victims)
                {
                    var original = oracle.Live[victim.Id];
                    var before = current;
                    Require(navigator.Remove(victim.Id), "Explicit live removal failed.");
                    current = Freeze();
                    oracle.RegisterDeath(victim.Id, before, current);
                    deathCount++;
                    if (planned.replace)
                    {
                        pending.Add(RequestInput.Replacement(nextId++, tick, original));
                        issued++;
                    }
                }
                triggeredDeaths.Add(index);
                ResetStability();
            }
        }

        private void AdmitRequests(int tick)
        {
            var triedEntries = new HashSet<int>();
            foreach (var request in pending.Where(item => item.readyTick <= tick).ToArray())
            {
                if (!triedEntries.Add(request.spawnCell))
                {
                    continue;
                }
                var command = request.Command();
                var before = current;
                bool accepted = navigator.TrySpawn(command);
                current = Freeze();
                lastSpawnFailure = navigator.LastSpawnFailure ?? "";
                if (accepted)
                {
                    oracle.RegisterBirth(command, before, current);
                    pending.Remove(request);
                    admitted++;
                    ResetStability();
                }
                else
                {
                    oracle.VerifyRejectedBirth(before, current);
                }
            }
        }

        private void CheckInitialBatch(int tick)
        {
            if (initialChecked)
            {
                return;
            }
            int due = input.requests.Count(item => item.readyTick <= tick);
            if (tick >= input.initialSettledCheckTick)
            {
                Require(current.Length == due && current.All(unit => unit.State != MotionState.Moving),
                    "Initial crowd did not physically settle before late arrivals.");
                initialChecked = true;
            }
        }

        private bool IsComplete(bool beforeLifecycle)
        {
            int expected = beforeLifecycle && input.expectation == "FullArrival" ? issued - deathCount : input.expectedLive;
            if (current.Length != expected || current.Any(unit => unit.State == MotionState.Moving))
            {
                return false;
            }
            if (input.expectation == "FullArrival" && pending.Count != 0)
            {
                return false;
            }
            if (input.expectation == "StaticUnreachable" && (current.Length != 0 || pending.Count != input.requests.Length))
            {
                return false;
            }
            int attacking = current.Count(unit => unit.State == MotionState.Attacking);
            if (input.expectedAttackers < 0 && current.Length > 0 && input.requests.Any(item => item.assignedGoalCell < 0) && attacking == 0)
            {
                return false;
            }
            if (input.expectedAttackers >= 0 && attacking != input.expectedAttackers)
            {
                return false;
            }
            int promotedOld = current.Count(unit => unit.State == MotionState.Attacking && oldWaiting.Contains(unit.Id));
            if (!beforeLifecycle && promotedOld < input.minimumOldWaitingPromotions)
            {
                return false;
            }
            if (!beforeLifecycle && pending.Any(item => item.readyTick > input.deadlineTicks))
            {
                return false;
            }
            return input.minimumAttackers.All(item => current.Count(unit => unit.State == MotionState.Attacking
                && oracle.Live[unit.Id].Profile.AttackRangeMm == item.range) >= item.count);
        }

        private void ResetStability()
        {
            finalHold.Reset();
        }

        private bool TryTakeLogicalStep()
        {
            int available = Volatile.Read(ref requestedLogicalSteps);
            while (available > 0)
            {
                int observed = Interlocked.CompareExchange(ref requestedLogicalSteps, available - 1, available);
                if (observed == available)
                {
                    return true;
                }
                available = observed;
            }
            return false;
        }

        private UnitSnapshot[] Freeze() => navigator.Snapshot().ToArray();

        private void Finish(string finalStatus, string message)
        {
            status = finalStatus;
            detail = message;
            finished = true;
        }

        private void Publish()
        {
            var live = oracle == null ? new Dictionary<int, SpawnRequest>() : oracle.Live.ToDictionary(pair => pair.Key, pair => pair.Value);
            var snapshot = new PlaygroundSnapshot(
                (UnitSnapshot[])current.Clone(),
                oracle?.World ?? initialWorld,
                new ReadOnlyDictionary<int, SpawnRequest>(live),
                microstep,
                issued,
                admitted,
                pending.Count,
                deathCount,
                finalHold.Ticks(microstep),
                status,
                detail,
                lastStepMilliseconds,
                lastSpawnFailure,
                finished);
            Volatile.Write(ref latest, snapshot);
            lastPublicationMilliseconds = clock.Elapsed.TotalMilliseconds;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(PlaygroundSimulation));
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class StabilityWindow
        {
            private Dictionary<int, UnitSnapshot> anchor;
            private int start = -1;

            public void Observe(UnitSnapshot[] units, bool eligible, int microstep)
            {
                Validate(units);
                if (!eligible)
                {
                    Reset();
                }
                else if (start < 0)
                {
                    anchor = units.ToDictionary(unit => unit.Id);
                    start = microstep;
                }
            }

            public void Validate(UnitSnapshot[] units)
            {
                if (anchor == null)
                {
                    return;
                }
                bool unchanged = units.Length == anchor.Count && units.All(unit => anchor.TryGetValue(unit.Id, out var original)
                    && unit.State != MotionState.Moving && unit.State == original.State && unit.GoalCell == original.GoalCell
                    && unit.TargetId == original.TargetId && Point2.Distance(unit.Position, original.Position) <= SimulationProtocol.GeometryToleranceMm);
                if (!unchanged)
                {
                    Reset();
                }
            }

            public int Ticks(int microstep) => start < 0 ? 0 : (microstep - start) / SimulationProtocol.Substeps;

            public void Reset()
            {
                anchor = null;
                start = -1;
            }
        }
    }
}
