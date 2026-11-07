namespace Game.HotUpdate.TowerDefense.Domain
{
    /// <summary>暂停来源分别保留；关闭确认框不能清除玩家暂停或系统挂起。</summary>
    public sealed class BattlePauseState
    {
        private bool applicationPaused;
        private bool applicationUnfocused;
        public bool ManualPaused { get; private set; }
        public bool ReturnPending { get; private set; }
        public bool IsPaused => ManualPaused || ReturnPending || applicationPaused || applicationUnfocused;

        public void SetManualPaused(bool value)
        {
            ManualPaused = value;
        }

        public void SetApplicationPaused(bool value)
        {
            applicationPaused = value;
        }

        public void SetApplicationFocused(bool value)
        {
            applicationUnfocused = !value;
        }

        public void BeginReturn()
        {
            ReturnPending = true;
        }

        public void CancelReturn()
        {
            ReturnPending = false;
        }

        public void ResetForNewRun()
        {
            ManualPaused = false;
            ReturnPending = false;
        }
    }
}
