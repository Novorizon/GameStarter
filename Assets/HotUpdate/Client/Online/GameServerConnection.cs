using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network;
using Game.HotUpdate.Client.Protocol;

namespace Game.HotUpdate.Client
{
    /// <summary>营地拥有的服务器会话。服务器读取结果只用于独立展示。</summary>
    internal sealed class GameServerConnection : IDisposable
    {
        private const string HostVariable = "GAMESTARTER_SERVER_HOST";
        private const string PortVariable = "GAMESTARTER_SERVER_PORT";
        private const string DeviceVariable = "GAMESTARTER_SERVER_DEVICE_ID";
        private const string TokenVariable = "GAMESTARTER_SERVER_ACCESS_TOKEN";
        private const string KeyVariable = "GAMESTARTER_SERVER_AES_KEY_BASE64";
        private readonly TcpClientOptions transport;
        private readonly string deviceId;
        private readonly Func<CancellationToken, Task<string>> getAccessToken;
        private readonly Action<string, bool> show;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private NetworkManager manager;
        private long playerId;
        private bool refreshing;
        private bool reconnectRequired;
        private bool disposed;

        private GameServerConnection(
            TcpClientOptions transport,
            string deviceId,
            Func<CancellationToken, Task<string>> getAccessToken,
            Action<string, bool> show)
        {
            this.transport = transport;
            this.deviceId = deviceId;
            this.getAccessToken = getAccessToken;
            this.show = show;
        }

        internal static GameServerConnection FromEnvironment(Action<string, bool> show)
        {
            string host = Environment.GetEnvironmentVariable(HostVariable);
            string device = Environment.GetEnvironmentVariable(DeviceVariable);
            string key = Environment.GetEnvironmentVariable(KeyVariable);
            string portText = Environment.GetEnvironmentVariable(PortVariable) ?? "22370";
            if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(device) && string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(device) || string.IsNullOrWhiteSpace(key)
                || !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
            {
                show("服务器地址或认证配置不完整", false);
                return null;
            }

            var transport = new TcpClientOptions { Host = host.Trim(), Port = port, Encryption = PacketEncryptionMode.AesGcm, KeyBase64 = key };
            return new GameServerConnection(transport, device, ReadAccessTokenAsync, show);
        }

        internal async Task RefreshAsync()
        {
            if (refreshing || disposed)
            {
                return;
            }

            refreshing = true;
            var token = lifetime.Token;
            show(manager?.IsConnected == true ? "正在读取服务器进度…" : "正在连接游戏服务器…", false);
            try
            {
                if (manager == null)
                {
                    NetworkManager.Configure(transport, new NetworkClientOptions { AutoReconnect = false }, authenticate: AuthenticateAsync);
                    manager = NetworkManager.Instance;
                    manager.Disconnected += OnDisconnected;
                }

                if (!manager.IsConnected || reconnectRequired)
                {
                    await manager.DisconnectAsync();
                    token.ThrowIfCancellationRequested();
                    await manager.ConnectAsync(token);
                    reconnectRequired = false;
                }

                long connection = manager.ConnectionId;
                var progress = await manager.RequestAsync<ProgressResponse>(ServerMessageId.ProgressRead, new ProgressReadRequest(), token);
                if (!disposed)
                {
                    string status = manager.IsConnected && manager.ConnectionId == connection
                        ? "服务器在线 · 玩家 " + Interlocked.Read(ref playerId) + " · 旧进度 " + progress.Progress
                        : Describe(manager.LastError);
                    show(status, true);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!disposed)
                {
                    reconnectRequired |= error is RemoteRequestException remote && (remote.ResultCode == 2 || remote.ResultCode == 9);
                    show(Describe(error), !(error is ArgumentException));
                }
            }
            finally
            {
                refreshing = false;
            }
        }

        private async Task AuthenticateAsync(NetworkManager.AuthenticationContext context, CancellationToken token)
        {
            string accessToken = await getAccessToken(token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ConnectionRejectedException("A short-lived login token is required.");
            }

            var request = new LoginRequest { DeviceId = deviceId, AccessToken = accessToken };
            var reply = await context.RequestAsync<LoginResponse>(ServerMessageId.Login, request, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (reply.PlayerId <= 0 || !string.Equals(reply.DeviceId, deviceId, StringComparison.Ordinal))
            {
                throw new ConnectionRejectedException("The login response did not match the configured device.");
            }

            long previous = Interlocked.CompareExchange(ref playerId, reply.PlayerId, 0);
            if (previous != 0 && previous != reply.PlayerId)
            {
                throw new ConnectionRejectedException("The game account changed during this session.");
            }
        }

        private static Task<string> ReadAccessTokenAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Environment.GetEnvironmentVariable(TokenVariable));
        }

        private void OnDisconnected(Exception error)
        {
            if (!disposed && !refreshing && !manager.IsConnected)
            {
                show(Describe(error), true);
            }
        }

        private string Describe(Exception error)
        {
            return error switch
            {
                RemoteRequestException remote when remote.ResultCode == 9 => "服务器会话已被替换",
                RemoteRequestException remote when remote.ResultCode == 100 || remote.ResultCode == 2 => "登录已失效，需要有效令牌",
                ConnectionRejectedException => "登录未通过，请核对账号凭据",
                ArgumentException => "服务器连接参数无效",
                _ when manager?.IsConnected == true => "服务器在线 · 进度暂未读到",
                _ => "服务器连接已断开或不可用"
            };
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetime.Cancel();
            if (manager != null)
            {
                manager.Disconnected -= OnDisconnected;
                manager.Dispose();
            }

            lifetime.Dispose();
        }
    }
}
