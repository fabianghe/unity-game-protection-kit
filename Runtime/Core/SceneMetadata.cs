using UnityEngine;

namespace GameProtection
{
    /// <summary>
    /// Ownership canary. Injected into every scene at build time by the editor (never added by hand).
    /// Asset rippers export scenes with their serialized data intact, so this token travels into rebuilt clones,
    /// where the Suspicious Build scanner finds it. Deliberately boring name and no behaviour.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class SceneMetadata : MonoBehaviour
    {
        [SerializeField, HideInInspector] string data = "";

        internal void Set(string value) => data = value;
    }
}
