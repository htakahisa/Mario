using UnityEngine;
using TMPro; // TextMeshProを使うために必要

public class UIManager : MonoBehaviour
{
    // どこからでも UIManager.Instance.ShowAnnouncement(...) で呼べるようにする
    public static UIManager Instance { get; private set; }

    [Header("--- UI References ---")]
    [SerializeField] private TextMeshProUGUI announcementText; // 先ほど作った AnnouncementText をアサイン

    private float fadeTimer = 0f;
    private float displayDuration = 3.0f; // メッセージを表示しておく時間（3秒）

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        // メッセージが表示されている場合、タイマーを進めてフワッと消す
        if (fadeTimer > 0f)
        {
            fadeTimer -= Time.deltaTime;
            if (fadeTimer <= 0f)
            {
                announcementText.text = ""; // 時間が経ったら文字を消す
            }
        }
    }

    // ★【最重要】画面にシステムメッセージを表示する関数
    public void ShowAnnouncement(string message, Color textColor)
    {
        if (announcementText == null) return;

        // テキストを書き換えて色を設定
        announcementText.text = message;
        announcementText.color = textColor;

        // 表示タイマーをリセット（3秒間表示）
        fadeTimer = displayDuration;
    }
}