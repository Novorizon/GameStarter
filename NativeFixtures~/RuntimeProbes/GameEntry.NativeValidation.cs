using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game
{
    // 由验收脚本临时导入验收工程。
    public sealed partial class GameEntry
    {
        async partial void ObserveStartup(Task startup, CancellationToken token)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--novorizon-native-smoke") < 0)
            {
                return;
            }

            int code;
            try
            {
                await startup;
                code = State == StartupState.Ready ? 0 : 1;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                code = 1;
            }

            if (!token.IsCancellationRequested)
            {
                Debug.Log(code == 0 ? "NATIVE_PLAYER_READY" : "NATIVE_PLAYER_FAILED");
                Application.Quit(code);
            }
        }
    }
}
