using System;
using System.Threading;
using System.Threading.Tasks;

namespace Game.HotUpdate.TowerDefense.Online
{
    /// <summary>单账号、单局的业务会话。后端必须另外实现鉴权、幂等落库和结算校验。</summary>
    public sealed class AccountSession : IDisposable
    {
        private readonly IGameBackend backend;
        private readonly Func<DateTimeOffset> now;
        private readonly object state = new object();
        private SemaphoreSlim operations = new SemaphoreSlim(1, 1);
        private CancellationTokenSource authLifetime = new CancellationTokenSource();
        private SessionTokens credentials;
        private RunTicket activeRun;
        private SubmitRunRequest pendingReport;
        private Task<RunSettlement> submission;
        private long generation;
        private bool disposed;

        public AccountSession(IGameBackend backend, Func<DateTimeOffset> clock = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            now = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>表示内存中持有可刷新的登录会话，不能证明服务端尚未撤销令牌。</summary>
        public bool IsLoggedIn
        {
            get
            {
                lock (state)
                {
                    return !disposed && credentials != null && credentials.RefreshExpiresAtUtc > now();
                }
            }
        }

        public string PlayerId
        {
            get
            {
                lock (state)
                {
                    return credentials?.PlayerId;
                }
            }
        }

        public RunTicket ActiveRun
        {
            get
            {
                lock (state)
                {
                    return activeRun;
                }
            }
        }

        public async Task LoginAsync(LoginRequest request, CancellationToken token = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var operation = Capture();
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token, operation.Token))
            {
                await operation.Gate.WaitAsync(stop.Token);
                try
                {
                    Check(operation, stop.Token);
                    lock (state)
                    {
                        CheckLocked(operation);
                        if (credentials != null)
                        {
                            throw new InvalidOperationException("Log out before replacing the account session.");
                        }
                    }

                    var result = await backend.LoginAsync(request, stop.Token);
                    Check(operation, stop.Token);
                    ValidateTokens(result);
                    lock (state)
                    {
                        CheckLocked(operation);
                        credentials = result;
                    }
                }
                finally
                {
                    operation.Gate.Release();
                }
            }
        }

        public async Task RefreshSessionAsync(CancellationToken token = default)
        {
            var operation = Capture();
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token, operation.Token))
            {
                await operation.Gate.WaitAsync(stop.Token);
                try
                {
                    await AccessTokenAsync(operation, stop.Token, true);
                }
                finally
                {
                    operation.Gate.Release();
                }
            }
        }

        public async Task<PlayerProfile> GetProfileAsync(CancellationToken token = default)
        {
            var operation = Capture();
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token, operation.Token))
            {
                await operation.Gate.WaitAsync(stop.Token);
                try
                {
                    string access = await AccessTokenAsync(operation, stop.Token);
                    var profile = await backend.GetProfileAsync(access, stop.Token);
                    Check(operation, stop.Token);
                    lock (state)
                    {
                        CheckLocked(operation);
                        if (profile == null || profile.PlayerId != credentials.PlayerId)
                        {
                            throw new InvalidOperationException("The profile does not belong to the authenticated account.");
                        }
                    }

                    return profile;
                }
                finally
                {
                    operation.Gate.Release();
                }
            }
        }

        public async Task<RunTicket> BeginRunAsync(int stageId, string configVersion, CancellationToken token = default)
        {
            if (stageId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stageId));
            }

            LoginRequest.Required(configVersion, nameof(configVersion));
            var operation = Capture();
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token, operation.Token))
            {
                await operation.Gate.WaitAsync(stop.Token);
                try
                {
                    string access = await AccessTokenAsync(operation, stop.Token);
                    lock (state)
                    {
                        CheckLocked(operation);
                        if (activeRun != null && (submission == null || !submission.IsCompletedSuccessfully))
                        {
                            throw new InvalidOperationException("Finish settlement before beginning another online run.");
                        }
                    }

                    var ticket = await backend.BeginRunAsync(access, stageId, configVersion, stop.Token);
                    Check(operation, stop.Token);
                    if (ticket == null || ticket.StageId != stageId || ticket.ConfigVersion != configVersion || ticket.ExpiresAtUtc <= now())
                    {
                        throw new InvalidOperationException("The run ticket is expired or belongs to another stage/configuration.");
                    }

                    lock (state)
                    {
                        CheckLocked(operation);
                        if (activeRun != null && activeRun.RunId == ticket.RunId)
                        {
                            throw new InvalidOperationException("The backend reused a completed run identifier.");
                        }

                        activeRun = ticket;
                        pendingReport = null;
                        submission = null;
                    }

                    return ticket;
                }
                finally
                {
                    operation.Gate.Release();
                }
            }
        }

        /// <summary>取消 token 只取消该调用者等待。结算使用会话 token；退出账号才取消共同请求。</summary>
        public Task<RunSettlement> SubmitRunAsync(RunReport report, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            Task<RunSettlement> task;
            lock (state)
            {
                EnsureUsable();
                if (credentials == null || activeRun == null)
                {
                    throw new InvalidOperationException("An authenticated server run ticket is required.");
                }

                if (pendingReport == null)
                {
                    pendingReport = new SubmitRunRequest(activeRun.RunId, activeRun.ConfigVersion, Guid.NewGuid().ToString("N"), report);
                }
                else if (!pendingReport.Report.SameAs(report))
                {
                    throw new InvalidOperationException("Retry the original immutable report; its previous request may already have committed.");
                }

                if (submission == null || submission.IsFaulted || submission.IsCanceled)
                {
                    submission = SubmitCoreAsync(new Operation(generation, authLifetime.Token, operations), pendingReport);
                    _ = ObserveFailureAsync(submission);
                }

                task = submission;
            }

            return WaitAsync(task, token);
        }

        private async Task<RunSettlement> SubmitCoreAsync(Operation operation, SubmitRunRequest request)
        {
            // Defer until submission is published under state, including synchronous backends.
            await Task.Yield();
            await operation.Gate.WaitAsync(operation.Token);
            try
            {
                string access = await AccessTokenAsync(operation, operation.Token);
                var result = await backend.SubmitRunAsync(access, request, operation.Token);
                Check(operation, operation.Token);
                if (result == null || result.RunId != request.RunId)
                {
                    throw new InvalidOperationException("The settlement does not match the submitted run.");
                }

                return result;
            }
            finally
            {
                operation.Gate.Release();
            }
        }

        private async Task<string> AccessTokenAsync(Operation operation, CancellationToken token, bool forceRefresh = false)
        {
            Check(operation, token);
            SessionTokens current;
            lock (state)
            {
                CheckLocked(operation);
                current = credentials;
            }

            if (current == null || current.RefreshExpiresAtUtc <= now())
            {
                throw new InvalidOperationException("Log in before calling the account backend.");
            }

            if (!forceRefresh && current.AccessExpiresAtUtc > now().AddSeconds(30))
            {
                return current.AccessToken;
            }

            var refreshed = await backend.RefreshSessionAsync(current.RefreshToken, token);
            Check(operation, token);
            ValidateTokens(refreshed);
            if (refreshed.PlayerId != current.PlayerId)
            {
                throw new InvalidOperationException("Refreshing a session cannot change accounts.");
            }

            lock (state)
            {
                CheckLocked(operation);
                credentials = refreshed;
            }

            return refreshed.AccessToken;
        }

        private void ValidateTokens(SessionTokens value)
        {
            if (value == null || value.AccessExpiresAtUtc <= now() || value.RefreshExpiresAtUtc <= now())
            {
                throw new InvalidOperationException("The backend returned expired credentials.");
            }
        }

        private Operation Capture()
        {
            lock (state)
            {
                EnsureUsable();
                return new Operation(generation, authLifetime.Token, operations);
            }
        }

        private void Check(Operation operation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (state)
            {
                CheckLocked(operation);
            }
        }

        private void CheckLocked(Operation operation)
        {
            if (disposed || operation.Generation != generation)
            {
                throw new OperationCanceledException("The account session has ended.", operation.Token);
            }
        }

        private void EnsureUsable()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AccountSession));
            }
        }

        public void Logout()
        {
            EndSession(false);
        }

        public void Dispose()
        {
            EndSession(true);
        }

        private void EndSession(bool endLifetime)
        {
            CancellationTokenSource previous;
            lock (state)
            {
                if (disposed)
                {
                    return;
                }

                disposed = endLifetime;
                generation++;
                credentials = null;
                activeRun = null;
                pendingReport = null;
                submission = null;
                previous = authLifetime;
                authLifetime = endLifetime ? null : new CancellationTokenSource();
                // A backend that ignores cancellation must not block a fresh login behind the old gate.
                operations = endLifetime ? null : new SemaphoreSlim(1, 1);
            }

            try
            {
                previous.Cancel();
            }
            finally
            {
                previous.Dispose();
            }
        }

        private static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken token)
        {
            if (!token.CanBeCanceled)
            {
                return await task;
            }

            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task) != task)
                {
                    token.ThrowIfCancellationRequested();
                }
            }

            token.ThrowIfCancellationRequested();
            return await task;
        }

        private static async Task ObserveFailureAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception)
            {
                // Keep the original task faulted for waiters/retry, even if every waiter cancelled.
            }
        }

        private readonly struct Operation
        {
            internal readonly long Generation;
            internal readonly CancellationToken Token;
            internal readonly SemaphoreSlim Gate;

            internal Operation(long generation, CancellationToken token, SemaphoreSlim gate)
            {
                Generation = generation;
                Token = token;
                Gate = gate;
            }
        }
    }
}
