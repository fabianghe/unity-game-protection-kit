// Sample: replace the default stolen-copy notice with your own UI, wired up in the Inspector. No code needed.
// 1. Add this component to a GameObject in your first scene (it survives scene loads).
// 2. Hook OnTamperDetected to your own popup, e.g. activate a panel and set a button's URL.

using GameProtection;
using UnityEngine;
using UnityEngine.Events;

public sealed class UnityEventTamperNotice : MonoBehaviour, ITamperNoticePresenter
{
    [System.Serializable]
    public sealed class UrlEvent : UnityEvent<string> { }

    [Tooltip("Called once if this copy is detected as stolen. Receives the official store URL from IntegrityConfig.")]
    public UrlEvent OnTamperDetected = new UrlEvent();

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        GameProtectionKit.NoticePresenter = this;
    }

    public void Show(IntegrityReport report, string officialStoreUrl) => OnTamperDetected.Invoke(officialStoreUrl);

    /// <summary>Convenience for a UI Button's OnClick.</summary>
    public void OpenUrl(string url) => Application.OpenURL(url);
}
