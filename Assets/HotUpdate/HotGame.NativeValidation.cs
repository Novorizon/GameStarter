using System;

namespace Game.HotUpdate
{
    // 仅命令行原主包补丁验收使用，不属于游戏表读取逻辑。
    public sealed partial class HotGame
    {
        private void ValidateNativeSchema(cfg.Tables tables)
        {
            var arguments = Environment.GetCommandLineArgs();
            int probe = Array.IndexOf(arguments, "--expect-luban-table");
            if (probe >= 0 && probe + 1 < arguments.Length)
            {
                string expected = arguments[probe + 1];
                if (tables.GetType().GetProperty(expected)?.GetValue(tables) == null)
                {
                    throw new InvalidOperationException("Expected hot table was not loaded: " + expected);
                }

                host.Log("NATIVE_SCHEMA_READY " + expected);
            }
        }
    }
}
