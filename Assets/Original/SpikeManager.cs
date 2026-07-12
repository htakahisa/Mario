using UnityEngine;
using static UnityEditor.ShaderGraph.Internal.KeywordDependentCollection;

public class SpikeManager : MonoBehaviour
{
    public static SpikeManager Instance;

    public enum SpikeState { OnCarrier, Planting, Dropped, Planted, Defusing, Defused, Exploded }
    public SpikeState currentState = SpikeState.Dropped;

    public Agent carrier = null;
    public Vector3Int droppedGridPos;
    public Vector3Int plantedGridPos;

    public Vector3Int spikePos;

    [Header("--- Tick Timer Settings ---")]
    // すべて int（Tick数）で管理。1Tick = 0.1秒
    public int plantTickTimer = 300; // 30秒 = 300Tickカウントダウン

    [Header("--- Hold Tick Timers ---")]
    private int plantHoldTickCount = 0;
    private const int RequiredPlantTicks = 40; // 4秒 = 40Tick
    private Vector3Int temporaryPlantPos;

    private int defuseHoldTickCount = 0;
    private const int RequiredDefuseTicks = 50; // 5秒 = 50Tick
    public Agent defusingAgent = null;          // 現在解除中のエージェント
    public Agent plantingAgent = null;          // 現在解除中のエージェント

    [Header("--- Audio Tick Settings ---")]
    private AudioSource audioSource;
    private int beepTickTimer = 0;
    private int defuseBeepTickTimer = 0;

    public GameObject spikeWorldObject;
    public GameObject spikeDropObject;
    private Grid grid;

    // 現在誰かが解除中かどうかを判定するプロパティ
    public bool IsBeingDefused => currentState == SpikeState.Defusing && defusingAgent != null;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        grid = Object.FindFirstObjectByType<Grid>();
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    // ❌ 従来の Update() は浮動小数点タイマーや非同期処理の原因になるため丸ごと削除、または非表示制御のみにする
    void Update()
    {
        // 描画（レンダラーの有効・無効）の更新だけはUnityの画面描画に合わせるためUpdateに残す
        if (currentState == SpikeState.Dropped && spikeWorldObject != null)
        {
            SpriteRenderer sr = spikeWorldObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                TestManager tm = Object.FindFirstObjectByType<TestManager>();
                bool playerIsAttacker = (tm != null) ? tm.isPlayerAttacker : true;
                sr.enabled = playerIsAttacker; // アタッカーなら見える、防衛なら見えない
            }
        }
    }

    /// <summary>
    /// ★改良：動的生成を廃止し、プレファブから生成（またはアクティブ化）する
    /// </summary>
    // SpikeManager.cs の改良イメージ

    private void CreateSpikeWorldObject(Vector3Int gridPos, Color color, string name)
    {
        Vector3 worldPos = new Vector3(gridPos.x + 0.5f, gridPos.y + 0.5f, 0f);

        // 最初に1回だけ生成（Instantiate）し、2回目以降は使い回す！
        if (spikeWorldObject == null)
        {
            spikeWorldObject.SetActive(true); // ★ONにするだけ！
            spikeWorldObject.name = name;
        }
        else
        {
            spikeWorldObject.SetActive(true); // ★ONにするだけ！
            // すでに存在しているなら、位置だけ変えてアクティブにする
            spikeWorldObject.transform.position = worldPos;
        }

        if (spikeWorldObject.TryGetComponent<SpriteRenderer>(out var sr))
        {
            sr.color = color;
        }
    }

    private void CreateDropSpikeWorldObject(Vector3Int gridPos, Color color, string name)
    {
        Vector3 worldPos = new Vector3(gridPos.x + 0.5f, gridPos.y + 0.5f, 0f);

        // 最初に1回だけ生成（Instantiate）し、2回目以降は使い回す！
        if (spikeDropObject == null)
        {
            spikeDropObject.SetActive(true); // ★ONにするだけ！
            spikeDropObject.name = name;
        }
        else
        {
            spikeDropObject.SetActive(true); // ★ONにするだけ！
            // すでに存在しているなら、位置だけ変えてアクティブにする
            spikeDropObject.transform.position = worldPos;
        }

        if (spikeDropObject.TryGetComponent<SpriteRenderer>(out var sr))
        {
            sr.color = color;
        }
    }

    public void LoadState(BoardTickSnapshot targetState)
    {
        this.currentState = targetState.spikeState;
        this.plantTickTimer = targetState.plantTickTimer;

        // ★状態（Enum）に合わせて、画面上のオブジェクトの表示を強制同期する
        if (spikeWorldObject != null)
        {
            if (this.currentState == SpikeState.Planted || this.currentState == SpikeState.Defusing)
            {
                // 設置済、または解除中なら赤いスパイクを表示
                spikeWorldObject.SetActive(true);
            }
            else
            {
                // それ以外（Droppedやまだ持って歩いている状態）なら赤いスパイクを絶対に消す！
                spikeWorldObject.SetActive(false);
            }
        }
        if(spikeDropObject != null)
        {
            if (this.currentState == SpikeState.Dropped)
            {
                // 設置済、または解除中なら赤いスパイクを表示
                spikeDropObject.SetActive(true);
            }
            else
            {
                // それ以外（Droppedやまだ持って歩いている状態）なら赤いスパイクを絶対に消す！
                spikeDropObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// ★【新設】GameManagerから1Tick（0.1秒）ごとに呼び出される、スパイクの主軸ロジック
    /// </summary>
    public void UpdateSpikeTick()
    {
        switch (currentState)
        {
            // 1. 【設置中（4秒 = 40Tickホールド）】
            case SpikeState.Planting:
                plantHoldTickCount++;
                if (plantHoldTickCount >= RequiredPlantTicks)
                {
                    CompletePlant();
                }
                break;

            // 2. 【解除中（5秒 = 50Tickホールド）】
            case SpikeState.Defusing:
                plantTickTimer--; // 解除中も爆発カウントダウンは進む
                defuseHoldTickCount++;

                // 解除中の「ピリリッ！」という高音リズム音（0.25秒 = 約2Tickか3Tickごと）
                defuseBeepTickTimer++;
                if (defuseBeepTickTimer >= 3) // 0.3秒(3Tick)ごとに調整してシャープに
                {
                    defuseBeepTickTimer = 0;
                    PlayBeepSound(1500f, 0.04f); // 1500Hzの高音
                }

                if (defuseHoldTickCount >= RequiredDefuseTicks)
                {
                    CompleteDefuse();
                    return; // ラウンドが終了するため抜ける
                }

                // 解除中に爆発時間が来たら爆発が優先
                if (plantTickTimer <= 0)
                {
                    ExplodeSpike();
                }
                break;

            // 3. 【設置完了後のカウントダウン（通常状態）】
            case SpikeState.Planted:
                plantTickTimer--;

                // 残り時間に応じたビープ音の間隔調整（Tick換算）
                int beepInterval = 10; // 通常は1.0秒 = 10Tickごと
                if (plantTickTimer <= 50) beepInterval = 2;       // 残り5秒(50Tick)以下は0.2秒ごと
                else if (plantTickTimer <= 150) beepInterval = 4; // 残り15秒(150Tick)以下は0.4秒ごと

                beepTickTimer++;
                if (beepTickTimer >= beepInterval)
                {
                    beepTickTimer = 0;
                    PlayBeepSound(600f, 0.08f); // 設置の「ピッ」音
                }

                if (plantTickTimer <= 0)
                {
                    ExplodeSpike();
                }
                break;
        }
    }

    public void GiveSpikeToAttacker()
    {
        TestManager tm = Object.FindFirstObjectByType<TestManager>();
        bool playerIsAttacker = (tm != null) ? tm.isPlayerAttacker : true;

        Agent[] allAgents = Object.FindObjectsByType<Agent>(FindObjectsSortMode.None);
        foreach (Agent a in allAgents)
        {
            if (a.isEnemy != playerIsAttacker && !a.isDead)
            {
                PickupSpike(a);
                break;
            }
        }
    }

    public void PickupSpike(Agent agent)
    {
        if (currentState == SpikeState.Planted || currentState == SpikeState.Exploded ||
            currentState == SpikeState.Planting || currentState == SpikeState.Defusing) return;

        currentState = SpikeState.OnCarrier;
        carrier = agent;
        agent.hasSpike = true;
        spikeDropObject.SetActive(false);

        string teamName = agent.isEnemy ? "敵" : "味方";
        UIManager.Instance.ShowAnnouncement($"{teamName}：{agent.agentName} がスパイクを拾った！", Color.yellow);
    }

    public void DropSpike(Vector3Int gridPos)
    {
        if (currentState != SpikeState.OnCarrier) return;

        if (carrier != null)
        {
            carrier.hasSpike = false;
            carrier = null;
        }

        currentState = SpikeState.Dropped;
        droppedGridPos = gridPos;
        spikePos = gridPos;

        CreateDropSpikeWorldObject(gridPos, new Color(1f, 0.8f, 0f, 1f), "Dropped_Spike");
        UIManager.Instance.ShowAnnouncement("スパイクがドロップされた！", Color.yellow);
    }

    public void StartPlantSpike(Vector3Int gridPos, Agent agent)
    {
        if (currentState != SpikeState.OnCarrier || carrier == null) return;

        currentState = SpikeState.Planting;
        plantHoldTickCount = 0;
        temporaryPlantPos = gridPos;
        plantingAgent = agent;

        carrier.isMoving = false;
        carrier.isActionLocked = true;

        PlayPlantStartSound();
        UIManager.Instance.ShowAnnouncement($"スパイク設置中... (40 Tickホールド)", Color.orange);
    }
    public void CancelPlant()
    {
        if (currentState == SpikeState.Planting)
        {
            if (plantingAgent != null)
            {
                plantingAgent.isActionLocked = false;
                plantingAgent = null;
            }
            plantHoldTickCount = 0;
            currentState = SpikeState.OnCarrier;
            UIManager.Instance.ShowAnnouncement("⚠️ スパイクの設置が中断された！", Color.orange);
        }
    }

    private void CompletePlant()
    {
        if (plantingAgent != null)
        {
            plantingAgent.hasSpike = false;
            plantingAgent.isActionLocked = false;
            carrier = null;
            plantingAgent = null;
        }

        currentState = SpikeState.Planted;
        plantedGridPos = temporaryPlantPos;
        spikePos = temporaryPlantPos;
        beepTickTimer = 0;

        CreateSpikeWorldObject(plantedGridPos, new Color(1f, 0f, 0f, 1f), "Planted_Spike");
        UIManager.Instance.ShowAnnouncement("スパイクが設置された！爆発まで30秒(300Tick)", new Color(1f, 0.3f, 0.3f));
        spikeWorldObject.SetActive(true);
    }

    public void StartDefuseSpike(Agent agent)
    {
        if (currentState == SpikeState.Defusing || IsBeingDefused) return;
        if (currentState != SpikeState.Planted) return;

        currentState = SpikeState.Defusing;
        defuseHoldTickCount = 0;
        defuseBeepTickTimer = 0;
        defusingAgent = agent;

        agent.isMoving = false;
        agent.isActionLocked = true;

        PlayBeepSound(1200f, 0.05f);
        UIManager.Instance.ShowAnnouncement($"🛠️ {agent.agentName} がスパイク解除中... (50 Tickホールド)", Color.green);
    }

    public void CancelDefuse()
    {
        if (currentState == SpikeState.Defusing)
        {
            if (defusingAgent != null)
            {
                defusingAgent.isActionLocked = false;
                defusingAgent = null;
            }
            currentState = SpikeState.Planted;
            defuseHoldTickCount = 0;
            UIManager.Instance.ShowAnnouncement("⚠️ スパイクの解除が中断された！", Color.orange);
        }
    }

    private void CompleteDefuse()
    {
        currentState = SpikeState.Defused;

        if (defusingAgent != null)
        {
            defusingAgent.isActionLocked = false;
            defusingAgent = null;
        }

        PlayBeepSound(1800f, 0.5f);

        // ★ GameManagerと連動してラウンド終了フラグを立てる
        if (GameManager.Instance != null)
        {
            GameManager.Instance.EndRound(GameManager.MatchResult.SpikeDefused, 2, "🛠️ 解除成功 ディフェンダー側の勝利！");
        }
        else
        {
            UIManager.Instance.ShowAnnouncement("解除成功 ディフェンダー側の勝利！", Color.green);
        }
    }

    public void ForceSetCarrier(Agent newCarrier)
    {
        this.carrier = newCarrier;
        Debug.Log($"<color=yellow>【スパイク】</color> 巻き戻しにより、{newCarrier.agentName} に所有権が復元されました。");
    }
    private void ExplodeSpike()
    {
        currentState = SpikeState.Exploded;

        if (defusingAgent != null)
        {
            defusingAgent.isActionLocked = false;
            defusingAgent = null;
        }

        PlayBeepSound(150f, 1.5f);

        // ★ GameManagerと連動してラウンド終了フラグを立てる
        if (GameManager.Instance != null)
        {
            GameManager.Instance.EndRound(GameManager.MatchResult.SpikeExploded, 1, "💥 爆発 アタッカー側の勝利！");
        }
        else
        {
            UIManager.Instance.ShowAnnouncement(" 爆発  アタッカー側の勝利！", Color.red);
        }
    }

    private void PlayBeepSound(float frequency, float duration)
    {
        int sampleRate = 44100;
        int sampleCount = (int)(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            samples[i] = Mathf.Sin(2 * Mathf.PI * frequency * i / sampleRate);
            if (i > sampleCount - 1000)
            {
                float fade = (float)(sampleCount - i) / 1000f;
                samples[i] *= fade;
            }
        }

        AudioClip clip = AudioClip.Create("Beep", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        audioSource.PlayOneShot(clip);
    }

    private void PlayPlantStartSound()
    {
        PlayBeepSound(800f, 0.05f);
        // Invokeは時間依存ですが、効果音の連続再生の見た目（聞き心地）維持のため残します
        Invoke("PlaySecondStartBeep", 0.08f);
    }
    private void PlaySecondStartBeep() { PlayBeepSound(1000f, 0.05f); }

    public Vector2Int GetSpikePos()
    {
        if(currentState == SpikeState.OnCarrier)
        {
            return ((Vector2Int)carrier.gridPosition);
        }
        else
        {
            return ((Vector2Int)spikePos);
        }
    }

}