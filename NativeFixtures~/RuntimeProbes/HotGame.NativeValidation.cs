using System;

namespace Game.HotUpdate
{
    // 由验收脚本临时导入验收工程。
    public sealed partial class HotGame
    {
        partial void ValidateNativeSchema(cfg.Tables tables)
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
