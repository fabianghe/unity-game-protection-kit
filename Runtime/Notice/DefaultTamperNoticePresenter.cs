using UnityEngine;

namespace GameProtection
{
    /// <summary>Zero-dependency fallback notice (IMGUI). Works with any render pipeline and UI system.</summary>
    public sealed class DefaultTamperNoticePresenter : ITamperNoticePresenter
    {
        public string Message { get; set; } =
            "This copy was not published by the original developer and may be unsafe.\n" +
            "Support the creator by getting the official version.";

        public void Show(IntegrityReport report, string officialStoreUrl)
        {
            var host = new GameObject("[GameProtection Notice]") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(host);
            host.AddComponent<TamperNoticeView>().Init(Message, officialStoreUrl);
        }
    }

    [AddComponentMenu("")]
    sealed class TamperNoticeView : MonoBehaviour
    {
        string _message;
        string _url;
        GUIStyle _labelStyle;

        public void Init(string message, string url)
        {
            _message = message;
            _url = url;
        }

        void OnGUI()
        {
            if (_labelStyle == null) _labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = Mathf.RoundToInt(Screen.height / 40f) };

            var width = Mathf.Min(Screen.width - 32f, 640f);
            var height = Mathf.Min(Screen.height - 32f, 260f);
            var rect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);

            GUI.depth = -1000;
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 16, rect.width - 32, rect.height - 32));
            GUILayout.Label(_message, _labelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(_url) && GUILayout.Button("Get the official game", GUILayout.Height(height / 5f)))
                Application.OpenURL(_url);
            if (GUILayout.Button("Close", GUILayout.Height(height / 5f)))
                Destroy(gameObject);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
