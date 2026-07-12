using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static Agent;
using static GameStateSerializer;
using static Platformer.Core.Simulation;
using Object = UnityEngine.Object;

public class GridTickScheduler : MonoBehaviour
{
    public float timeScale = 1f;

    public static GridTickScheduler Instance { get; private set; }

    private bool isSimulationRunning = false;

    [Header("--- 駆動対象のリスト ---")]
    [SerializeField] private List<Agent> allAgents = new List<Agent>();
    [SerializeField] private List<ParanoiaProjectile> paranoiaPool = new List<ParanoiaProjectile>();
    [SerializeField] private List<ReconBoltProjectile> reconBoltPool = new List<ReconBoltProjectile>();

    // ★【追加】弾道のプールを管理するリスト
    [SerializeField] private List<BulletTracer> bulletTracerPool = new List<BulletTracer>();

    [Header("--- モード設定 ---")]
    public bool isAiTrainingMode = false;

    public int currentTickCount = 0;
    private const float TickInterval = 0.1f; // 0.1秒
    private bool isRunning = false;
    private Coroutine tickCoroutine;

    private SpikeManager spikeManager;
    private TimeCapsuleManager timeCapsuleManager;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        spikeManager = Object.FindFirstObjectByType<SpikeManager>();
    }

    private void Start()
    {
        Time.timeScale = timeScale;
        if (timeCapsuleManager == null) timeCapsuleManager = Object.FindFirstObjectByType<TimeCapsuleManager>();
        // 学習ループをコルーチンとして開始
        StartCoroutine(AILearningLoop());
    }

    /// <summary>
    /// ★強化学習用の同期メインループ
    /// </summary>
    private IEnumerator AILearningLoop()
    {

        isSimulationRunning = true;
        // 🔥【追加】Pythonサーバーと完全に接続が確立するまでここで安全に待機する
        yield return new WaitUntil(() => NativeSocketManager.Instance.IsConnectedAndReady);
        Debug.Log("<color=cyan>【Scheduler】</color> 通信路の確立を確認。ファーストTickを送信します。");
        // 🔥【新設】ループが始まる前に、今の完全な初期状態を「0 Tick目」として強制セーブ！
        if (timeCapsuleManager != null)
        {
            timeCapsuleManager.SaveCurrentTickSnapshot(0);
            Debug.Log("[TimeCapsule] 🚀 起動時の完全初期状態（0 Tick目）を保存しました。");
        }

        while (isSimulationRunning)
        {
            float tickStartTime = Time.time;

            // =================================================================
            // 1. 【差し替え】最新の盤面状態をシリアライズしてPythonに送信
            // =================================================================
            string gameStateJson = "";
            if (GameStateSerializer.Instance != null)
            {
                // 作成したリッチなJSONデータを取得
                gameStateJson = GameStateSerializer.Instance.SerializeCurrentGameState();
            }
            else
            {
                // 万が一シリアライザーがない場合のセーフティ
                gameStateJson = "{}";
            }

            // 行終わりの識別子として改行を付与して送信
            NativeSocketManager.Instance.SendStateToPython(gameStateJson + "\n");

            // =================================================================
            // 2. Pythonから次の行動データが届くまでここで待機（ウエイト）
            // =================================================================
            string rawActionData = "";
            yield return new WaitUntil(() =>
            {
                if (NativeSocketManager.Instance.isOfflineMode)
                {
                    rawActionData = "offline_mode";
                    return true;
                }

                // 最新のデータを取得
                rawActionData = NativeSocketManager.Instance.GetLatestAction();

                // 文字列が空でなければ、条件クリア（待機を抜ける）
                return !string.IsNullOrEmpty(rawActionData);
            });

            // 🔥【重要】待機を抜けたら、次のTickのために即座にソケット側のバッファをクリアする！
            NativeSocketManager.Instance.ClearLatestAction();

            // 3. 届いた行動データをパースして各オブジェクト（Agentなど）に適用
            ApplyPythonActions(rawActionData);

            // 4. 行動データ消費後のクリアロジック（もし実装されていれば有効化）
            NativeSocketManager.Instance.ClearLatestAction();

            // 5. 1Tick分ゲームの時間を進める
            currentTickCount++;
            ExecuteTickObjects();

            // 5. 勝敗ルールのチェック
            if (GameManager.Instance != null)
            {
                GameManager.Instance.CheckMatchRules(currentTickCount);
            }

            // 6. 巻き戻し用にこのTickの状態をセーブ
            if (timeCapsuleManager != null)
            {
                timeCapsuleManager.SaveCurrentTickSnapshot(currentTickCount);
            }

            // 🔥 1Tickが最低でも0.1秒（100ms）を消費するように調整
            float elapsed = Time.time - tickStartTime;
            float remainingTime = TickInterval - elapsed;

            if (remainingTime > 0f)
            {
                yield return new WaitForSeconds(remainingTime);
            }
            else
            {
                yield return null;
            }
        }
    }



    private string CollectAndSerializeGlobalState()
    {
        UnityToPythonState packet = new UnityToPythonState();
        packet.tick = currentTickCount;

        // 💀 エピソード終了判定（例：片方のチームが全滅したか、など。プロジェクトのルールに合わせて調整）
        packet.isTerminal = CheckEpisodeTerminal();

        // 💰 報酬の計算（※このTickでのキルやスパイク設置などの報酬を合算）
        packet.reward = CalculateCurrentTickReward();

        // 全エージェントの情報を集約
        Agent[] allAgents = Object.FindObjectsByType<Agent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        List<PythonAgentObservation> agentObsList = new List<PythonAgentObservation>();

        foreach (Agent agent in allAgents)
        {
            if (agent == null) continue;
            PythonAgentObservation obs = new PythonAgentObservation
            {
                name = agent.agentName,
                posX = agent.transform.position.x,
                posY = agent.transform.position.y,
                hp = agent.hp,
                isDead = agent.isDead,
                hasSpike = agent.hasSpike,
                paranoiaCount = agent.paranoiaCharges,
                reconBoltCount = agent.reconBoltCharges
            };
            agentObsList.Add(obs);
        }
        packet.agents = agentObsList.ToArray();

        // スパイク状態の取得
        SpikeManager spikeManager = Object.FindFirstObjectByType<SpikeManager>();
        if (spikeManager != null)
        {
            packet.spikeState = (int)spikeManager.currentState;
        }

        // JSON文字列に変換して返す
        return JsonUtility.ToJson(packet);
    }

    /// <summary>
    /// Pythonから届いた行動データをパースし、Unity内のAgentに実行させる
    /// </summary>
    private void ApplyPythonActions(string rawActionData)
    {
        // 1. Pythonから届いた5人分のJSON配列をリストに変換
        List<PythonActionData> actionsList = JsonConvert.DeserializeObject<List<PythonActionData>>(rawActionData);

        if (actionsList == null) return;

        // 2. ループで届いた命令を1個ずつ処理する
        foreach (PythonActionData action in actionsList)
        {
            if (action.action_type == "ResetEnvironment")
            {
                ResetRound();
                continue;
            }

            // 🔥【超重要】Pythonから指定された target_agent_id を使って、動かす対象のキャラクターを特定する！
            // （例：マネージャーからIDが一致するエージェントを引っ張ってくる）
            Agent targetAgent = GetAgentByID(action.target_agent_id);

            if (targetAgent == null || targetAgent.isDead) continue;

            Vector3Int abilityTarget = new Vector3Int(action.ability_target_x, action.ability_target_y, targetAgent.gridPosition.z);

            // 3. 特定したエージェントに対して命令を適用する
            switch (action.action_type)
            {
                case "Move":
                    Vector3Int currentPos = targetAgent.gridPosition;
                    Vector3Int targetGridPos = new Vector3Int(
                        currentPos.x + action.grid_x,
                        currentPos.y + action.grid_y,
                        currentPos.z
                    );
                    if (MapManager.Instance != null && !MapManager.Instance.IsWall(targetGridPos))
                    {
                        targetAgent.SetTargetGridPosition(targetGridPos);
                    }
                    break;

                case "Defuse":
                    TestManager.Instance.ExecuteSpikeInteractAction(targetAgent, spikeManager);
                    break;

                case "Paranoia":
                    TestManager.Instance.ExecuteCastAbilityAction(targetAgent, "Paranoia", abilityTarget);
                    break;

                case "ReconBolt":
                    TestManager.Instance.ExecuteCastAbilityAction(targetAgent, "ReconBolt", abilityTarget);
                    break;

                case "Stay":
                    // 待機
                    break;
            }
        }
    }

    public Agent GetAgentByID(int id)
    {
        foreach(var agent in allAgents)
        {
           if(agent.agentID == id)
            {
                return agent;
            }
        }
        return null;
    }

    public void ResetRound()
    {
        // 1. 状態を完全に巻き戻す
        timeCapsuleManager.LoadTickSnapshot(0);
        GameManager.Instance.isRoundOver = false;
        spikeManager.GiveSpikeToAttacker();
        BulletTracer[] bullets = Object.FindObjectsByType<BulletTracer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var bullet in bullets)
        {
            Destroy(bullet.gameObject);
        }
        foreach(var agent in allAgents)
        {
            agent.DecideTargetSite();
        }

        // ⚡ 2.【重要】巻き戻した直後の「Tick 0 の最新状態」のJSONをその場で即座に作成する
        string initialJson = GameStateSerializer.Instance.SerializeCurrentGameState(); // 普段UnityからPythonに送っているJSONを作るメソッド

    }

    public void StopScheduler()
    {
        if (!isRunning) return;
        isRunning = false;
        if (tickCoroutine != null) StopCoroutine(tickCoroutine);
    }

    public void RegisterBulletTracer(BulletTracer tracer)
    {
        if (!bulletTracerPool.Contains(tracer))
        {
            bulletTracerPool.Add(tracer);
        }
    }

    // 簡易的な終了判定のサンプル
    private bool CheckEpisodeTerminal()
    {
        // ここでゲームオーバーやラウンド終了（スパイク爆破、タイムアップなど）を検知して true を返します
        return false;
    }

    // 簡易的な報酬計算のサンプル
    private float CalculateCurrentTickReward()
    {
        float r = 0f;
        // ここで「このTickで敵にダメージを与えたら +1.0」「死んだら -5.0」のような一時的な報酬を返します
        return r;
    }

    private void ExecuteTickObjects()
    {

        if (spikeManager != null) spikeManager.UpdateSpikeTick();

        // 1. 全エージェントのロジック更新
        foreach (Agent agent in allAgents)
        {
            if (agent != null && agent.gameObject.activeSelf)
            {
                agent.UpdateAgentTick();
            }
        }

        // 2. パラノイア（弾丸）更新
        foreach (ParanoiaProjectile paranoia in paranoiaPool)
        {
            if (paranoia != null && paranoia.gameObject.activeSelf)
            {
                paranoia.UpdateProjectileTick();
            }
        }

        // 3. リコンボルト（矢）更新
        foreach (ReconBoltProjectile recon in reconBoltPool)
        {
            if (recon != null && recon.gameObject.activeSelf)
            {
                recon.UpdateProjectileTick();
            }
        }

        // 🌟【追加】4. 弾道（トレーサー）を毎Tick更新してカウントを進める
        foreach (BulletTracer tracer in bulletTracerPool)
        {
            if (tracer != null && tracer.gameObject.activeSelf)
            {
                tracer.UpdateTracerTick(); // 👈 ここで呼び出します！
            }
        }

    }

    public int GetCurrentTick() => currentTickCount;
    public void SyncTickCount(int tick) => currentTickCount = tick;

    public interface ITickUpdateable
    {
        void OnTickUpdate(int currentTick);
        bool IsFinished { get; } // 演出が終わったら true にしてリストから除外してもらう
    }
}