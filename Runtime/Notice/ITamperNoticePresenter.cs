namespace GameProtection
{
    /// <summary>
    /// Shows players of a stolen copy where the official game lives. They are victims too, and telling them turns the
    /// thief's distribution into your marketing. Replace the default with your own UI via GameProtectionKit.NoticePresenter,
    /// or use the "UnityEvent Notice" sample to wire it up in the Inspector without code.
    /// </summary>
    public interface ITamperNoticePresenter
    {
        void Show(IntegrityReport report, string officialStoreUrl);
    }
}
