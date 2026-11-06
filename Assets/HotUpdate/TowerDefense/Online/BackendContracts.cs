using System;
using System.Threading;
using System.Threading.Tasks;

namespace Game.HotUpdate.TowerDefense.Online
{
    /// <summary>业务后端边界。实际适配须使用已约定的服务端协议；连接 TCP 不代表登录成功。</summary>
    public interface IGameBackend
    {
        Task<SessionTokens> LoginAsync(LoginRequest request, CancellationToken token);
        Task<SessionTokens> RefreshSessionAsync(string refreshToken, CancellationToken token);
        Task<PlayerProfile> GetProfileAsync(string accessToken, CancellationToken token);
        Task<RunTicket> BeginRunAsync(string accessToken, int stageId, string configVersion, CancellationToken token);
        Task<RunSettlement> SubmitRunAsync(string accessToken, SubmitRunRequest request, CancellationToken token);
    }

    public sealed class LoginRequest
    {
        public string Provider { get; }
        public string Proof { get; }

        public LoginRequest(string provider, string proof)
        {
            Provider = Required(provider, nameof(provider));
            Proof = Required(proof, nameof(proof));
        }

        internal static string Required(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A value is required.", name);
            }

            return value;
        }
    }

    /// <summary>令牌只交给会话和传输适配器，不写存档、日志、PlayerPrefs 或战斗摘要。</summary>
    public sealed class SessionTokens
    {
        public string PlayerId { get; }
        public string AccessToken { get; }
        public string RefreshToken { get; }
        public DateTimeOffset AccessExpiresAtUtc { get; }
        public DateTimeOffset RefreshExpiresAtUtc { get; }

        public SessionTokens(
            string playerId,
            string accessToken,
            string refreshToken,
            DateTimeOffset accessExpiresAtUtc,
            DateTimeOffset refreshExpiresAtUtc)
        {
            PlayerId = LoginRequest.Required(playerId, nameof(playerId));
            AccessToken = LoginRequest.Required(accessToken, nameof(accessToken));
            RefreshToken = LoginRequest.Required(refreshToken, nameof(refreshToken));
            AccessExpiresAtUtc = accessExpiresAtUtc;
            RefreshExpiresAtUtc = refreshExpiresAtUtc;
        }
    }

    public sealed class PlayerProfile
    {
        public string PlayerId { get; }
        public string DisplayName { get; }
        public long Revision { get; }
        public long SoftCurrency { get; }

        public PlayerProfile(string playerId, string displayName, long revision, long softCurrency)
        {
            PlayerId = LoginRequest.Required(playerId, nameof(playerId));
            DisplayName = displayName ?? "";
            if (revision < 0 || softCurrency < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(revision));
            }

            Revision = revision;
            SoftCurrency = softCurrency;
        }
    }

    public sealed class RunTicket
    {
        public string RunId { get; }
        public int StageId { get; }
        public string ConfigVersion { get; }
        public uint Seed { get; }
        public DateTimeOffset ExpiresAtUtc { get; }

        public RunTicket(string runId, int stageId, string configVersion, uint seed, DateTimeOffset expiresAtUtc)
        {
            RunId = LoginRequest.Required(runId, nameof(runId));
            ConfigVersion = LoginRequest.Required(configVersion, nameof(configVersion));
            if (stageId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stageId));
            }

            StageId = stageId;
            Seed = seed;
            ExpiresAtUtc = expiresAtUtc;
        }
    }

    public enum RunOutcome
    {
        Victory = 1,
        Defeat = 2,
        Abandoned = 3
    }

    /// <summary>客户端摘要只是服务器校验输入，不是发奖凭据。不包含奖励金额。</summary>
    public sealed class RunReport
    {
        public RunOutcome Outcome { get; }
        public int ReachedWave { get; }
        public long SimulationTicks { get; }
        public int RemainingBaseHp { get; }
        public int EnemiesKilled { get; }
        public int CommandCount { get; }
        public string CommandsSha256 { get; }

        public RunReport(
            RunOutcome outcome,
            int reachedWave,
            long simulationTicks,
            int remainingBaseHp,
            int enemiesKilled,
            int commandCount,
            string commandsSha256)
        {
            if (!Enum.IsDefined(typeof(RunOutcome), outcome) || reachedWave < 0 || simulationTicks < 0
                || remainingBaseHp < 0 || enemiesKilled < 0 || commandCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(outcome));
            }

            if (commandsSha256 == null || commandsSha256.Length != 64)
            {
                throw new ArgumentException("Expected a SHA256 command log digest.", nameof(commandsSha256));
            }

            foreach (char c in commandsSha256)
            {
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F'))
                {
                    throw new ArgumentException("Expected a SHA256 command log digest.", nameof(commandsSha256));
                }
            }

            Outcome = outcome;
            ReachedWave = reachedWave;
            SimulationTicks = simulationTicks;
            RemainingBaseHp = remainingBaseHp;
            EnemiesKilled = enemiesKilled;
            CommandCount = commandCount;
            CommandsSha256 = commandsSha256.ToLowerInvariant();
        }

        internal bool SameAs(RunReport other)
        {
            return other != null && Outcome == other.Outcome && ReachedWave == other.ReachedWave
                && SimulationTicks == other.SimulationTicks && RemainingBaseHp == other.RemainingBaseHp
                && EnemiesKilled == other.EnemiesKilled && CommandCount == other.CommandCount
                && CommandsSha256 == other.CommandsSha256;
        }
    }

    public sealed class SubmitRunRequest
    {
        public string RunId { get; }
        public string ConfigVersion { get; }
        public string IdempotencyKey { get; }
        public RunReport Report { get; }

        public SubmitRunRequest(string runId, string configVersion, string idempotencyKey, RunReport report)
        {
            RunId = LoginRequest.Required(runId, nameof(runId));
            ConfigVersion = LoginRequest.Required(configVersion, nameof(configVersion));
            IdempotencyKey = LoginRequest.Required(idempotencyKey, nameof(idempotencyKey));
            Report = report ?? throw new ArgumentNullException(nameof(report));
        }
    }

    public sealed class RunSettlement
    {
        public string RunId { get; }
        public long ProfileRevision { get; }
        public long GrantedSoftCurrency { get; }

        public RunSettlement(string runId, long profileRevision, long grantedSoftCurrency)
        {
            RunId = LoginRequest.Required(runId, nameof(runId));
            if (profileRevision < 0 || grantedSoftCurrency < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(profileRevision));
            }

            ProfileRevision = profileRevision;
            GrantedSoftCurrency = grantedSoftCurrency;
        }
    }
}
