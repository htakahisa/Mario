using System.Collections.Generic;
using UnityEngine;
using static GameStateSerializer;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("--- Tick Settings ---")]
    public const int MaxRoundTicks = 900; // 90秒 = 900Tick
    public bool isRoundOver = false;
    public int win_team = 0;

    // 試合の結末ステート
    public enum MatchResult { Ongoing, TimeUp, SpikeExploded, SpikeDefused, AttackersEliminated, DefendersEliminated }
    public MatchResult matchResult = MatchResult.Ongoing;

    [Header("--- 陣営のマスターリスト（統一） ---")]
    // ★ 配列ではなく List<Agent> に完全統一。初期化時に一度だけ取得する
    [SerializeField] private List<Agent> allAgents = new List<Agent>();
    public List<Agent> AllAgents => allAgents;
    public List<Agent> playeredAgents;

    private SpikeManager spikeManager;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        spikeManager = Object.FindFirstObjectByType<SpikeManager>();

        // ★ 開始時にマップ上の全エージェントを一度だけリストに登録・固定する
        allAgents.Clear();
        allAgents.AddRange(Object.FindObjectsByType<Agent>(FindObjectsSortMode.None));
        foreach (var agent in AllAgents)
        {
            if (agent.isPlayer) 
            {
                playeredAgents.Add(agent); 
            }
        }
        OrganizedList.Instance.initialize();
    }

    /// <summary>
    /// ★【重要】GridTickScheduler.cs から「毎Tickの最後」に呼び出される、勝敗判定専門のロジック
    /// </summary>
    public void CheckMatchRules(int tickCount)
    {
        if (isRoundOver) return;
        if (allAgents.Count == 0) return;

        // 1. タイムアップ判定（90秒ルール：スパイクが設置されていない時のみ）
        if (spikeManager != null && spikeManager.currentState != SpikeManager.SpikeState.Planted && spikeManager.currentState != SpikeManager.SpikeState.Defusing)
        {
            if (GridTickScheduler.Instance.GetCurrentTick() >= MaxRoundTicks)
            {
                EndRound(MatchResult.TimeUp, 2, "⏰ 90秒経過！タイムアップによりディフェンダー側の勝利！");
                return;
            }
        }

        // 2. 生存者カウント（Listをループするだけなので超軽量）
        int aliveAttackers = 0;
        int aliveDefenders = 0;

        foreach (Agent agent in allAgents)
        {
            if (agent == null || agent.isDead) continue;

            // 攻撃・防衛のカウント（チームフラグがisEnemyならアタッカー、等のルールに合わせて調整）
            if (agent.isEnemy == TestManager.Instance.isPlayerAttacker) aliveDefenders++;
            else aliveAttackers++;
        }

        // 3. 生存者ゼロによる決着判定
        if (aliveAttackers == 0 && !hasSpikePlanted())
        {
            EndRound(MatchResult.AttackersEliminated, 2, "💀 アタッカーが全滅！ディフェンダー側の勝利！");
        }
        else if (aliveDefenders == 0 && spikeManager != null)
        {
            EndRound(MatchResult.DefendersEliminated, 1, "💀 ディフェンダーが全滅！アタッカー側の勝利！");
        }

        
    }

    public bool hasSpikePlanted()
    {
        return !(spikeManager == null || (spikeManager.currentState == SpikeManager.SpikeState.Dropped || spikeManager.currentState == SpikeManager.SpikeState.OnCarrier || spikeManager.currentState == SpikeManager.SpikeState.Planting));
    }


    public void EndRound(MatchResult result, int winner, string message)
    {
        isRoundOver = true;
        matchResult = result;
        win_team = winner;

        // スケジューラー（時計）を止める
        if (GridTickScheduler.Instance != null) GridTickScheduler.Instance.StopScheduler();

        UIManager.Instance.ShowAnnouncement(message, Color.yellow);
        Debug.Log($"<color=gold>【ラウンド終了】</color> {message} (Total Ticks: {GridTickScheduler.Instance.GetCurrentTick()})");
    }

    // --- ★ここからタイムカプセル用の復元コード ---

    public TestManagerStateData SaveState()
    {
        TestManagerStateData state = new TestManagerStateData();
        state.isDuelOccurred = TestManager.Instance != null ? TestManager.Instance.isDuelOccurred : false;
        // 必要に応じてGameManager独自のフラグ（isRoundOver等）も構造体に含めて戻せるようにします
        return state;
    }

    public void LoadState(TestManagerStateData state)
    {
        // 巻き戻し時にラウンド終了フラグなどをリセットして再開できるようにする
        this.isRoundOver = false;
        this.matchResult = MatchResult.Ongoing;
    }
}