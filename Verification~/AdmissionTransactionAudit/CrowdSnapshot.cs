using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace AdmissionTransactionAudit;

internal sealed class CrowdSnapshot
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly long tick;
    private readonly int goalsRevision;
    private readonly int trafficRevision;
    private readonly bool recoveryActive;
    private readonly object? recovery;
    private readonly KeyValuePair<int, int>[] claims;
    private readonly Dictionary<int, OccupantSnapshot> occupants;
    private readonly Dictionary<long, ObjectFields> profiles;
    private readonly ReservationValue[] futureReservations;

    private CrowdSnapshot(NavigationCrowd crowd)
    {
        tick = crowd.CurrentTick;
        goalsRevision = Field<int>(crowd, "goalRevision");
        trafficRevision = Field<int>(crowd, "trafficRevision");
        recoveryActive = crowd.RecoveryActive;
        recovery = Field<object?>(crowd, "vacancyJob");
        claims = Claims(crowd);
        occupants = Entries(Field<IDictionary>(crowd, "occupants")).ToDictionary(entry => (int)entry.Key, entry => new OccupantSnapshot(entry.Value!));
        profiles = Entries(Field<IDictionary>(crowd, "profiles")).ToDictionary(entry => (long)entry.Key, entry => new ObjectFields(entry.Value!));
        futureReservations = Reservations(crowd);
    }

    internal static CrowdSnapshot Capture(NavigationCrowd crowd)
    {
        return new CrowdSnapshot(crowd);
    }

    internal static string InitialStateHash(NavigationCrowd crowd)
    {
        var state = new
        {
            crowd.CurrentTick,
            goalRevision = Field<int>(crowd, "goalRevision"),
            trafficRevision = Field<int>(crowd, "trafficRevision"),
            claims = Claims(crowd),
            reservations = Reservations(crowd),
            agents = crowd.Agents.OrderBy(actor => actor.Id).Select(actor => new
            {
                actor.Id,
                actor.GoalCell,
                actor.Position.X,
                actor.Position.Y,
                actor.Status,
                actor.Radius,
                actor.AttackRange,
                actor.Speed,
                actor.ArrivalTick,
                actor.DistanceTravelled,
                actor.RemainingDistance
            }).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state)))).ToLowerInvariant();
    }

    internal void AssertUnchanged(NavigationCrowd crowd, string name)
    {
        Audit.Require(crowd.CurrentTick == tick, name + ": simulation clock changed");
        Audit.Require(Field<int>(crowd, "goalRevision") == goalsRevision, name + ": goal revision changed");
        Audit.Require(Field<int>(crowd, "trafficRevision") == trafficRevision, name + ": traffic revision changed");
        Audit.Require(crowd.RecoveryActive == recoveryActive && ReferenceEquals(Field<object?>(crowd, "vacancyJob"), recovery), name + ": recovery changed");
        Audit.Require(claims.SequenceEqual(Claims(crowd)), name + ": goal ownership changed");
        Audit.Require(futureReservations.SequenceEqual(Reservations(crowd)), name + ": future reservation content changed");
        var current = Field<IDictionary>(crowd, "occupants");
        Audit.Require(current.Count == occupants.Count && crowd.Agents.Count == occupants.Count, name + ": actual actor count changed");
        foreach (var pair in occupants)
        {
            Audit.Require(current.Contains(pair.Key), name + ": occupant disappeared " + pair.Key);
            pair.Value.AssertUnchanged(current[pair.Key]!, name + ": occupant " + pair.Key);
        }

        var currentProfiles = Field<IDictionary>(crowd, "profiles");
        Audit.Require(currentProfiles.Count == profiles.Count, name + ": profile count changed");
        foreach (var pair in profiles)
        {
            Audit.Require(currentProfiles.Contains(pair.Key), name + ": profile disappeared");
            pair.Value.AssertUnchanged(currentProfiles[pair.Key]!, name + ": profile metadata " + pair.Key);
        }

        AssertNoGhosts(crowd);
    }

    internal static void AssertNoGhosts(NavigationCrowd crowd)
    {
        var actors = crowd.Agents.ToDictionary(actor => actor.Id);
        var claims = Field<IDictionary>(crowd, "goalOwners");
        var occupants = Field<IDictionary>(crowd, "occupants");
        Audit.Require(claims.Count == actors.Count && occupants.Count == actors.Count, "Ghost claim/occupant count");
        foreach (DictionaryEntry occupant in occupants)
        {
            int id = (int)occupant.Key;
            bool bound = actors.TryGetValue(id, out var actor) && ReferenceEquals(actor, Field<object>(occupant.Value!, "Agent"));
            Audit.Require(bound, "Public actor/occupant mismatch");
        }

        foreach (DictionaryEntry claim in claims)
        {
            int owner = (int)claim.Value!;
            Audit.Require(actors.TryGetValue(owner, out var actor) && actor.GoalCell == (int)claim.Key, "Ghost or inconsistent goal claim");
        }

        foreach (DictionaryEntry cell in Field<IDictionary>(crowd, "reservations"))
        {
            foreach (object reservation in (IEnumerable)cell.Value!)
            {
                Audit.Require(actors.ContainsKey(Field<int>(reservation, "Owner")), "Reservation belongs to a removed or hidden actor");
            }
        }
    }

    internal static int GoalCount(NavigationCrowd crowd)
    {
        return Field<IDictionary>(crowd, "goalOwners").Count;
    }

    internal static T Field<T>(object target, string name)
    {
        return (T)FindField(target, name).GetValue(target)!;
    }

    internal static void SetField(object target, string name, object value)
    {
        FindField(target, name).SetValue(target, value);
    }

    private static FieldInfo FindField(object target, string name)
    {
        return target.GetType().GetField(name, Fields) ?? throw new InvalidOperationException("Audit adapter field missing: " + name);
    }

    private static IEnumerable<DictionaryEntry> Entries(IDictionary source)
    {
        foreach (DictionaryEntry entry in source)
        {
            yield return entry;
        }
    }

    private static KeyValuePair<int, int>[] Claims(NavigationCrowd crowd)
    {
        return Entries(Field<IDictionary>(crowd, "goalOwners"))
            .Select(entry => new KeyValuePair<int, int>((int)entry.Key, (int)entry.Value!)).OrderBy(pair => pair.Key).ToArray();
    }

    private static ReservationValue[] Reservations(NavigationCrowd crowd)
    {
        var values = new List<ReservationValue>();
        foreach (DictionaryEntry cell in Field<IDictionary>(crowd, "reservations"))
        {
            foreach (object reservation in (IEnumerable)cell.Value!)
            {
                long end = Field<long>(reservation, "End");
                if (end > crowd.CurrentTick)
                {
                    values.Add(new ReservationValue((int)cell.Key, Field<int>(reservation, "Owner"), Field<long>(reservation, "Start"), end));
                }
            }
        }

        return values.OrderBy(value => value.Cell).ThenBy(value => value.Owner).ThenBy(value => value.Start).ThenBy(value => value.End).ToArray();
    }

    private sealed class OccupantSnapshot
    {
        private readonly ObjectFields occupant;
        private readonly ObjectFields[] pathNodes;
        private readonly PropertyInfo[] properties;
        private readonly object?[] propertyValues;
        private readonly object agent;

        internal OccupantSnapshot(object source)
        {
            occupant = new ObjectFields(source);
            pathNodes = ((IEnumerable)Field<object>(source, "Path")).Cast<object>().Select(node => new ObjectFields(node)).ToArray();
            agent = Field<object>(source, "Agent");
            properties = agent.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(property => property.Name).ToArray();
            propertyValues = properties.Select(property => property.GetValue(agent)).ToArray();
        }

        internal void AssertUnchanged(object current, string name)
        {
            // These are real reference comparisons, including original Agent/Profile/Path; no hash-code identity approximation.
            occupant.AssertUnchanged(current, name);
            var currentPath = ((IEnumerable)Field<object>(current, "Path")).Cast<object>().ToArray();
            Audit.Require(currentPath.Length == pathNodes.Length, name + ": timed path length changed");
            for (int index = 0; index < pathNodes.Length; index++)
            {
                pathNodes[index].AssertUnchanged(currentPath[index], name + ": timed path node " + index);
            }

            for (int index = 0; index < properties.Length; index++)
            {
                Audit.Require(Equals(properties[index].GetValue(agent), propertyValues[index]), name + ": Agent." + properties[index].Name + " changed");
            }
        }
    }

    private sealed class ObjectFields
    {
        private readonly object source;
        private readonly FieldInfo[] fields;
        private readonly object?[] values;

        internal ObjectFields(object source)
        {
            this.source = source;
            fields = source.GetType().GetFields(Fields).OrderBy(field => field.Name).ToArray();
            values = fields.Select(field => field.GetValue(source)).ToArray();
        }

        internal void AssertUnchanged(object current, string name)
        {
            Audit.Require(ReferenceEquals(source, current), name + ": original object reference changed");
            for (int index = 0; index < fields.Length; index++)
            {
                object? currentValue = fields[index].GetValue(current);
                bool equal = fields[index].FieldType.IsValueType ? Equals(values[index], currentValue) : ReferenceEquals(values[index], currentValue);
                Audit.Require(equal, name + ": " + fields[index].Name + " changed");
            }
        }
    }

    private readonly record struct ReservationValue(int Cell, int Owner, long Start, long End);
}
