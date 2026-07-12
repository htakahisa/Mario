using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class NativeSocketManager : MonoBehaviour
{
    public static NativeSocketManager Instance { get; private set; }

    [Header("--- 通信設定 ---")]
    [SerializeField] private string ipAddress = "127.0.0.1";
    [SerializeField] private int port = 50007;
    public bool isOfflineMode = false;

    // クライアント接続に変更するため、TcpListener(server)は削除
    private TcpClient client;
    private NetworkStream stream;
    private Thread connectionThread;
    private bool isRunning = false;

    private string latestActionJson = "";
    private readonly object lockObject = new object();

    // NativeSocketManager.cs の上部メンバー変数エリア
    private bool isConnectedAndReady = false;

    // 外部から準備ができたか確認するためのプロパティ
    public bool IsConnectedAndReady => isOfflineMode || (client != null && client.Connected && isConnectedAndReady);

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        Application.runInBackground = true;

        // 🔥【オフライン対応】オフラインモードなら通信スレッド自体を立ち上げない
        if (isOfflineMode)
        {
            Debug.Log("<color=yellow>【Socket】</color> オフラインモードで起動しました。Pythonとの通信は行いません。");
            return;
        }

        StartClient();
    }

    private void StartClient()
    {
        isRunning = true;
        // サーバーをリッスンするのではなく、サーバーへ「接続」するスレッドを開始
        connectionThread = new Thread(new ThreadStart(ConnectToPythonServer));
        connectionThread.IsBackground = true;
        connectionThread.Start();
        Debug.Log($"<color=green>【Socket】</color> Python AI Server ({ipAddress}:{port}) への接続を試みています...");
    }

    private void ConnectToPythonServer()
    {
        try
        {
            client = new TcpClient(ipAddress, port);
            stream = client.GetStream();

            // 🔥 接続され、streamが取れたらフラグをtrueにする！
            lock (lockObject)
            {
                isConnectedAndReady = true;
            }
            Debug.Log("<color=green>【Socket】</color> Pythonサーバーと接続されました！学習を開始します。");

            byte[] bytes = new byte[8192];
            while (isRunning)
            {
                int length;
                // フラグがtrueになったので、Pythonがsendallしたデータをここで待ち受けられるようになります
                while (stream != null && stream.CanRead && (length = stream.Read(bytes, 0, bytes.Length)) != 0)
                {
                    string incomingData = Encoding.UTF8.GetString(bytes, 0, length);
                    lock (lockObject)
                    {
                        latestActionJson = incomingData.Trim();
                    }
                }
            }
        }
        catch (Exception e)
        {
            // オフラインモード以外で接続に失敗した場合はエラーを表示
            if (isRunning)
            {
                Debug.LogError($"【Socket Error】 Pythonサーバーに接続できませんでした: {e.Message}");
            }
        }
    }

    /// <summary>
    /// UnityからPythonへデータを送信する
    /// </summary>
    public void SendStateToPython(string jsonState)
    {
        // オフライン、または未接続なら何もしない
        if (isOfflineMode || client == null || stream == null || !client.Connected) return;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(jsonState);
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"【Socket送信エラー】 {e.Message}");
        }
    }

    public string GetLatestAction()
    {
        lock (lockObject)
        {
            return latestActionJson;
        }
    }

    public void ClearLatestAction()
    {
        lock (lockObject)
        {
            latestActionJson = "";
        }
    }

    private void OnDestroy()
    {
        isRunning = false;
        if (stream != null) stream.Close();
        if (client != null) client.Close();
        if (connectionThread != null)
        {
            // Abortは古い.NETで非推奨な場合があるため、スレッドの終了を安全に待つ
            if (connectionThread.IsAlive) connectionThread.Join(100);
        }
    }
}