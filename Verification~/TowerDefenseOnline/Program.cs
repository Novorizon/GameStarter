using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.HotUpdate.TowerDefense.Online;

internal static class Program
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    private static int passed;

    private static async Task Main()
    {
        await Check("backend failure never establishes a local login", async () =>
        {
            var backend = new FakeBackend { LoginError = new IOException("offline") };
            using var session = Session(backend);
            await Throws<IOException>(() => session.LoginAsync(new LoginRequest("test", "proof")));
            True(!session.IsLoggedIn && session.PlayerId == null);
            await Throws<InvalidOperationException>(() => session.BeginRunAsync(1, "v1"));
            True(backend.BeginCalls == 0);
        });
        await Check("server ticket binds the requested stage, version and seed", async () =>
        {
            var backend = new FakeBackend();
            using var session = Session(backend);
            await Login(session);
            var ticket = await session.BeginRunAsync(1, "v1");
            True(ticket.Seed == 174 && ticket.ConfigVersion == "v1" && ticket.RunId == "run-1");
            await Throws<InvalidOperationException>(() => session.BeginRunAsync(1, "v1"));
            True(backend.BeginCalls == 1);
        });
        await Check("foreign or expired server tickets never create a run", async () =>
        {
            foreach (var ticket in new[]
            {
                new RunTicket("wrong-stage", 2, "v1", 1, Start.AddHours(1)),
                new RunTicket("wrong-version", 1, "v2", 1, Start.AddHours(1)),
                new RunTicket("expired", 1, "v1", 1, Start)
            })
            {
                var backend = new FakeBackend { Ticket = ticket };
                using var session = Session(backend);
                await Login(session);
                await Throws<InvalidOperationException>(() => session.BeginRunAsync(1, "v1"));
                True(session.ActiveRun == null);
            }
        });
        await Check("duplicate settlement shares one request and completed result", async () =>
        {
            var backend = new FakeBackend { SubmitGate = Gate<RunSettlement>() };
            using var session = Session(backend);
            await Begin(session);
            var first = session.SubmitRunAsync(Report());
            var second = session.SubmitRunAsync(Report());
            await Until(() => backend.SubmitCalls == 1);
            True(backend.Requests[0].RunId == "run-1" && backend.Requests[0].ConfigVersion == "v1");
            backend.SubmitGate.SetResult(new RunSettlement("run-1", 2, 17));
            var result = await first;
            True(ReferenceEquals(result, await second));
            True(ReferenceEquals(result, await session.SubmitRunAsync(Report())));
            True(backend.SubmitCalls == 1 && result.GrantedSoftCurrency == 17);
        });
        await Check("failed settlement retries the same key and immutable report", async () =>
        {
            var backend = new FakeBackend { SubmitFailures = 1 };
            using var session = Session(backend);
            await Begin(session);
            await Throws<IOException>(() => session.SubmitRunAsync(Report()));
            await Throws<InvalidOperationException>(() => session.SubmitRunAsync(Report(21)));
            var result = await session.SubmitRunAsync(Report());
            True(backend.SubmitCalls == 2 && result.RunId == "run-1");
            True(ReferenceEquals(backend.Requests[0], backend.Requests[1]));
            True(backend.Requests[0].IdempotencyKey == backend.Requests[1].IdempotencyKey);
        });
        await Check("a new server run gets a fresh settlement identity", async () =>
        {
            var backend = new FakeBackend();
            using var session = Session(backend);
            await Begin(session);
            await session.SubmitRunAsync(Report());
            await Throws<InvalidOperationException>(() => session.BeginRunAsync(1, "v1"));
            backend.Ticket = new RunTicket("run-2", 1, "v1", 175, Start.AddHours(1));
            backend.SettlementRun = "run-2";
            var ticket = await session.BeginRunAsync(1, "v1");
            True(ticket.Seed == 175);
            await session.SubmitRunAsync(Report(21));
            True(backend.SubmitCalls == 2 && backend.Requests[1].RunId == "run-2");
            True(backend.Requests[0].IdempotencyKey != backend.Requests[1].IdempotencyKey);
        });
        await Check("cancelling one settlement waiter does not cancel shared submission", async () =>
        {
            var backend = new FakeBackend { SubmitGate = Gate<RunSettlement>() };
            using var session = Session(backend);
            await Begin(session);
            using var stop = new CancellationTokenSource();
            var first = session.SubmitRunAsync(Report(), stop.Token);
            var second = session.SubmitRunAsync(Report());
            await Until(() => backend.SubmitCalls == 1);
            stop.Cancel();
            await Throws<OperationCanceledException>(() => first);
            True(!backend.SubmitToken.IsCancellationRequested);
            backend.SubmitGate.SetResult(new RunSettlement("run-1", 2, 1));
            True((await second).RunId == "run-1" && backend.SubmitCalls == 1);
        });
        await Check("logout clears credentials and rejects late settlement even if backend ignores cancellation", async () =>
        {
            var backend = new FakeBackend { SubmitGate = Gate<RunSettlement>() };
            using var session = Session(backend);
            await Begin(session);
            var pending = session.SubmitRunAsync(Report());
            await Until(() => backend.SubmitCalls == 1);
            session.Logout();
            True(!session.IsLoggedIn && session.PlayerId == null && session.ActiveRun == null);
            True(backend.SubmitToken.IsCancellationRequested);
            await Login(session).WaitAsync(TimeSpan.FromSeconds(2));
            True(session.IsLoggedIn);
            backend.SubmitGate.SetResult(new RunSettlement("run-1", 3, 999));
            await Throws<OperationCanceledException>(() => pending);
            True(session.IsLoggedIn && session.PlayerId == "player-1" && session.ActiveRun == null);
            session.Logout();
            await Throws<InvalidOperationException>(() => session.GetProfileAsync());
            await Login(session);
            True(session.IsLoggedIn);
        });
        await Check("logout during login cannot publish late credentials", async () =>
        {
            var backend = new FakeBackend { LoginGate = Gate<SessionTokens>() };
            using var session = Session(backend);
            var pending = Login(session);
            session.Logout();
            backend.LoginGate.SetResult(Tokens());
            await Throws<OperationCanceledException>(() => pending);
            True(!session.IsLoggedIn && session.PlayerId == null);
        });
        await Check("expiring access token refreshes before business requests", async () =>
        {
            var backend = new FakeBackend { LoginTokens = Tokens(Start.AddSeconds(1)) };
            using var session = Session(backend);
            await Login(session);
            await session.GetProfileAsync();
            True(backend.RefreshCalls == 1 && backend.ProfileToken == "refreshed-access");
        });
        await Check("refresh cannot change authenticated account", async () =>
        {
            var backend = new FakeBackend
            {
                LoginTokens = Tokens(Start.AddSeconds(1)),
                RefreshTokens = new SessionTokens("other", "x", "y", Start.AddHours(1), Start.AddDays(1))
            };
            using var session = Session(backend);
            await Login(session);
            await Throws<InvalidOperationException>(() => session.GetProfileAsync());
            True(session.PlayerId == "player-1" && backend.ProfileCalls == 0);
        });
        await Check("foreign profile and settlement are rejected", async () =>
        {
            var backend = new FakeBackend { ProfilePlayer = "other", SettlementRun = "other-run" };
            using var session = Session(backend);
            await Login(session);
            await Throws<InvalidOperationException>(() => session.GetProfileAsync());
            await session.BeginRunAsync(1, "v1");
            await Throws<InvalidOperationException>(() => session.SubmitRunAsync(Report()));
        });
        await Check("pre-cancelled settlement never reaches backend and dispose ends the session", async () =>
        {
            var backend = new FakeBackend();
            var session = Session(backend);
            await Begin(session);
            await Throws<OperationCanceledException>(() => session.SubmitRunAsync(Report(), new CancellationToken(true)));
            True(backend.SubmitCalls == 0);
            session.Dispose();
            True(!session.IsLoggedIn && session.PlayerId == null);
            await Throws<ObjectDisposedException>(() => session.GetProfileAsync());
        });
        Console.WriteLine($"PASS {passed} account/session scenarios (fake backend only; no live server login or economic validation).");
    }

    private static AccountSession Session(FakeBackend backend) => new AccountSession(backend, () => Start);
    private static Task Login(AccountSession session) => session.LoginAsync(new LoginRequest("test", "proof"));
    private static async Task Begin(AccountSession session)
    {
        await Login(session);
        await session.BeginRunAsync(1, "v1");
    }

    private static SessionTokens Tokens(DateTimeOffset? accessExpiry = null)
    {
        return new SessionTokens("player-1", "access", "refresh", accessExpiry ?? Start.AddHours(1), Start.AddDays(1));
    }

    private static RunReport Report(int kills = 20)
    {
        return new RunReport(RunOutcome.Victory, 3, 500, 8, kills, 12, new string('a', 64));
    }

    private static TaskCompletionSource<T> Gate<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Assertion failed.");
        }
    }

    private static async Task Check(string name, Func<Task> test)
    {
        await test();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static async Task Throws<T>(Func<Task> operation) where T : Exception
    {
        try
        {
            await operation();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static async Task Until(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow > timeout)
            {
                throw new TimeoutException();
            }

            await Task.Delay(1);
        }
    }

    private sealed class FakeBackend : IGameBackend
    {
        internal Exception LoginError;
        internal TaskCompletionSource<SessionTokens> LoginGate;
        internal SessionTokens LoginTokens;
        internal SessionTokens RefreshTokens;
        internal TaskCompletionSource<RunSettlement> SubmitGate;
        internal int SubmitFailures;
        internal int BeginCalls;
        internal int SubmitCalls;
        internal int RefreshCalls;
        internal int ProfileCalls;
        internal string ProfileToken;
        internal string ProfilePlayer = "player-1";
        internal string SettlementRun = "run-1";
        internal RunTicket Ticket;
        internal CancellationToken SubmitToken;
        internal readonly List<SubmitRunRequest> Requests = new List<SubmitRunRequest>();

        public Task<SessionTokens> LoginAsync(LoginRequest request, CancellationToken token)
        {
            if (LoginError != null)
            {
                return Task.FromException<SessionTokens>(LoginError);
            }

            return LoginGate?.Task ?? Task.FromResult(LoginTokens ?? Tokens());
        }

        public Task<SessionTokens> RefreshSessionAsync(string refreshToken, CancellationToken token)
        {
            RefreshCalls++;
            return Task.FromResult(RefreshTokens ?? new SessionTokens("player-1", "refreshed-access", "next-refresh", Start.AddHours(1), Start.AddDays(1)));
        }

        public Task<PlayerProfile> GetProfileAsync(string accessToken, CancellationToken token)
        {
            ProfileCalls++;
            ProfileToken = accessToken;
            return Task.FromResult(new PlayerProfile(ProfilePlayer, "Test", 1, 0));
        }

        public Task<RunTicket> BeginRunAsync(string accessToken, int stageId, string configVersion, CancellationToken token)
        {
            BeginCalls++;
            return Task.FromResult(Ticket ?? new RunTicket("run-1", stageId, configVersion, 174, Start.AddHours(1)));
        }

        public Task<RunSettlement> SubmitRunAsync(string accessToken, SubmitRunRequest request, CancellationToken token)
        {
            Requests.Add(request);
            SubmitToken = token;
            SubmitCalls++;
            if (SubmitFailures-- > 0)
            {
                return Task.FromException<RunSettlement>(new IOException("Response lost; server may have already committed"));
            }

            return SubmitGate?.Task ?? Task.FromResult(new RunSettlement(SettlementRun, 2, 17));
        }
    }
}
