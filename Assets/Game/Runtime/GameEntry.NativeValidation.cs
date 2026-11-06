using System;
using UnityEngine;

namespace Game
{
    // 命令行 Player 验收接线；普通启动不主动退出游戏。
    public sealed partial class GameEntry
    {
        private static void NativeSmokeExit(int code)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--novorizon-native-smoke") >= 0)
            {
                Debug.Log(code == 0 ? "NATIVE_PLAYER_READY" : "NATIVE_PLAYER_FAILED");
                Application.Quit(code);
            }
        }
    }
}
