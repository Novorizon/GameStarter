using UnityEngine;

namespace Game
{
    /// <summary>主包内只保存公钥，私钥由 Editor 保存在工程之外。</summary>
    public sealed class GameUpdateKey : ScriptableObject
    {
        public string keyId;
        public string modulus;
        public string exponent;
    }
}
