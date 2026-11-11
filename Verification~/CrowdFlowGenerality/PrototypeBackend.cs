using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GameStarter.Verification.CrowdFlow;

namespace GameStarter.Verification.CrowdFlowGenerality;

internal sealed class PrototypeBackend
{
    private readonly Scenario scenario;
    private readonly Junction junction;
    private readonly CrowdSteering steering;
    private readonly Dictionary<int, CrowdAgent> state = new();
    private readonly Dictionary<(Vector2 Goal, float Radius), DirectionField> fields = new();

    internal PrototypeBackend(Scenario scenario)
    {
        this.scenario = scenario;
        if (scenario.Crossing is { } crossing)
        {
            var headings = scenario.Bodies.Where(body => body.Finish.HasValue)
                .ToDictionary(body => body.Id, body => Vector2.Normalize(body.Finish.Value - body.Start));
            junction = new Junction(crossing.Center, crossing.HalfLength, headings);
        }

        steering = new CrowdSteering(scenario.CanSweep, true, junction == null ? null : junction.CanAdvance);
    }

    internal int YieldSteps => steering.YieldSteps;

    internal void Advance(IReadOnlyList<BodyState> live)
    {
        var agents = new CrowdAgent[live.Count];
        for (int i = 0; i < live.Count; i++)
        {
            var body = live[i];
            if (!state.TryGetValue(body.Spec.Id, out var agent))
            {
                var spec = body.Spec;
                agent = new CrowdAgent(spec.Id, body.Position, spec.Radius, spec.Speed) { CanYield = spec.CanReposition };
                if (spec.Finish is Vector2 finish)
                {
                    var key = (finish, spec.Radius);
                    if (!fields.TryGetValue(key, out var field))
                    {
                        field = new DirectionField(scenario.FieldMin, scenario.FieldMax, .15f,
#if CROWD_PORTAL_DIRECTION_FIELD
                            (from, to) => scenario.CanSweep(from, to, spec.Radius), new[] { finish }, scenario.Portals);
#else
                            (from, to) => scenario.CanSweep(from, to, spec.Radius), new[] { finish });
#endif
                        fields.Add(key, field);
                    }

                    agent.Field = field;
                }

                state.Add(spec.Id, agent);
            }

            agents[i] = agent;
        }

        junction?.Schedule(agents);
        steering.Advance(agents);
        for (int i = 0; i < live.Count; i++)
        {
            live[i].Position = agents[i].Position;
        }
    }
}
