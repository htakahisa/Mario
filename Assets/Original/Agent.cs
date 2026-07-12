using JetBrains.Annotations;
using System.Collections.Generic;
using UnityEngine;
using static Platformer.Core.Simulation;
using static UnityEditor.ShaderGraph.Internal.KeywordDependentCollection;

public class Agent : MonoBehaviour
{
    public int agentID;
    public string agentName;
    public bool isEnemy;
    public Vector3Int gridPosition;
    [HideInInspector] public Vector3Int nextGridPosition;
    public List<Vector3Int> currentPath = new List<Vector3Int>();

    public Vector3Int lastKnownGridPosition;

    public Vector3Int lastAssignedTarget = new Vector3Int(-999, -999, -999); // 目的地の重複設定防止用

    public bool isMoving = false;
    public float moveSpeed = 5f;

    [Header("--- HP Visual Settings ---")]
    [SerializeField] private Transform hpMaskTransform; // インスペクターから「HPMask」をドラッグ＆ドロップ
    public float maxHP = 100f;
    public int hp = 150;

    public int power = 40;          // 1発40ダメージ
    public int weaponTTKIntervalTicks = 2; // ★Time.deltaTimeの代わりにTick間隔で管理 (0.2秒 = 2Tick)
    public bool isDead = false;

    [Header("--- Shooting Settings ---")]
    public float accuracyRate = 0.5f; // そもそもの命中率 (50%)
    public float hsRate = 0.3f;       // ヘッドショット率 (30%)
    public int hsDamage = 150;        // ヘッドショットダメージ (150)
    public float avoidRate = 0.0f;

    [Header("--- Ability Settings ---")]
    public int paranoiaCharges = 1; // パラノイアの所持数（初期値1回）
    public int reconBoltCharges = 1;

    [Header("--- Status Effects ---")]
    public bool isBlinded = false;        // 現在ブラインド状態か
    public int blindTickTimer = 0;       // ★ブラインドの残り時間（Tick管理）
    private SpriteRenderer spriteRenderer; // 見た目の色を変える用

    [Header("--- Enemy AI Settings ---")]
    private int aiDecisionTickTimer = 0;     // ★次行動を考えるまでのTickタイマー
    private int aiDecisionTickInterval = 30; // ★何Tickごとに移動先を考えるか（30Tick = 3.0秒ごと）

    public SpriteRenderer teamRenderer;
    public SpriteRenderer hpRenderer;

    [Header("--- Recon & Reveal Settings ---")]
    public bool InsideRevealed;
    public bool CheatRevealed;

    public bool revealedTrigger = false; 
    public bool isRevealed => revealedTrigger || InsideRevealed;        // 現在マップに映っている（可視状態）か
    private int revealTickTimer = 0;       // ★リヴィールの残り時間（Tick管理）

    [Header("--- Spike Status ---")]
    public bool hasSpike = false;          // 現在スパイクを所持しているか

    [Header("--- State Locks ---")]
    public bool isActionLocked = false; // trueの間は移動・射撃など一切のアクションを行えない

    [Header("--- FX & Audio Settings ---")]
    [SerializeField] private GameObject bulletTracerPrefab;
    [SerializeField] private AudioClip shotSound;

    [HideInInspector] public int lastTickDamageDealt = 0;
    [HideInInspector] public int lastTickDamageTaken = 0;
    [HideInInspector] public bool lastTickDidKill = false;
    [HideInInspector] public bool lastTickDidDie = false;
    [HideInInspector] public bool lastTickDidBlindEnemy = false;
    [HideInInspector] public bool lastTickDidRevealEnemy = false;

    public bool lastTickEnemyHasBlind = false;
    public bool lastTickEnemyHasRevealed = false;

    public Agent revealCauser;
    public Agent blindCauser;

    // 味方側から見て、このエージェントが視覚的に隠されるべきかを判定するプロパティ
    public bool IsVisibleToPlayer => !isEnemy || isRevealed || CheatRevealed;


    private MapManager mapManager;
    private int shotTickTimer = 0; // ★次に弾を撃つまでのTickタイマー

    public enum BotRole { Default, Entry, Lurker }
    [Header("--- Tactical AI Role ---")]
    public BotRole myRole = BotRole.Default;
    public bool isActive = true; // ★追加：GameObjectの活性状態

    private string currentTargetSite = "A";

    // =========================================================
    // ✨【新設】外部AI（Python）制御モード・アビリティ予約バッファ
    // =========================================================
    [Header("--- External RL AI Settings ---")]
    public bool isControlledByExternalAI = false; // 強化学習時、または外部制御時にtrueにする
    public bool isPlayer = false;
    private bool pendingParanoia = false;
    private bool pendingRecon = false;
    private Vector3Int pendingAbilityTargetGrid = Vector3Int.zero;
    public bool isMovingAnimation = false; // 🔥 アニメーション中かどうかのフラグ

    // 外部から「アニメーション中か」を確認できるようにプロパティ化
    public bool IsMovingAnimation => isMovingAnimation;

    public enum AIMoveCommand
    {
        Stay = 0,
        Up = 1,
        Down = 2,
        Left = 3,
        Right = 4,
        UpLeft = 5,
        UpRight = 6,
        DownLeft = 7,
        DownRight = 8
    }

    void Start()
    {
        mapManager = MapManager.Instance;
        spriteRenderer = GetComponent<SpriteRenderer>();

        // 現在のワールド座標からグリッド座標を確定
        gridPosition = Vector3Int.RoundToInt(transform.position);
        gridPosition.z = 0;
        nextGridPosition = gridPosition;

        // ワールド座標のズレを綺麗に真ん中に補正
        transform.position = new Vector3(gridPosition.x + 0.5f, gridPosition.y + 0.5f, transform.position.z);

        if (!isControlledByExternalAI) // ボット側
        {
            if (hasSpike)
            {
                myRole = BotRole.Entry; // スパイク持ちは強制的に本隊
            }
            else
            {
                if (isEnemy != TestManager.Instance.isPlayerAttacker)
                {
                    myRole = (Random.value <= 0.3f) ? BotRole.Lurker : BotRole.Entry;
                }
                else
                {
                    myRole = (Random.value <= 0.3f) ? BotRole.Lurker : BotRole.Default;
                }
            }
            Debug.Log($"<color=cyan>【戦術配分】</color> {agentName} の今回の役割: {myRole}");
        }
    }

    void Update()
    {
        if (isDead) return;
        
        // 1. HPバーのマスク位置を滑らかに更新
        if (hpMaskTransform != null)
        {
            float hpRatio = Mathf.Clamp01((float)hp / maxHP);
            hpMaskTransform.localPosition = new Vector3(0f, -hpRatio, 0f);
        }

        // 2. リヴィール・視界表示処理の見た目（アルファ値）の更新
        UpdateVisualVisibility();

        // 3. スパイクインジケーターの見た目更新
        UpdateSpikeIndicatorVisual();

        //if (isMovingAnimation)
        //{
            // 4. 次のグリッドへ「ぬるっと移動」する表現だけをUpdateで行う
            if (!isActionLocked)
            {
                Vector3 targetWorldPos = new Vector3(nextGridPosition.x + 0.5f, nextGridPosition.y + 0.5f, transform.position.z);
                if (Vector3.Distance(transform.position, targetWorldPos) > 0.01f)
                {
                    transform.position = Vector3.MoveTowards(transform.position, targetWorldPos, moveSpeed * Time.deltaTime);
                    isMovingAnimation = true;
                }
                else
                {
                    isMovingAnimation = false; // 🔥 到着したのでロック解除！
                }
            }
        //}

        UpdateVision();
    }

    private void UpdateVision()
    {
        TestManager tm = Object.FindFirstObjectByType<TestManager>();
        if (mapManager == null || tm == null) return;
        if (GameManager.Instance == null) return;

        List<Agent> allAgents = GameManager.Instance.AllAgents;

        foreach (Agent other in allAgents)
        {
            if (other == null || other == this || other.isDead) continue;

            if (this.isEnemy != other.isEnemy)
            {
                bool canSee = FieldOfViewSystem.HasLineOfSight(this.gridPosition, other.gridPosition, mapManager);

                if (canSee)
                {
                    tm.StartDuel(this, other);
                }
            }
        }
    }

    /// <summary>
    /// ✨【新設】Claude提案の「プレイヤーの入力」からも「Python（AI）の入力」からも、全く均等に操作を受け付ける統一司令窓口。
    /// </summary>
    public void ExecuteCommand(AIMoveCommand moveCmd, bool useParanoia, bool useRecon, Vector3Int targetGridPos)
    {
        if (isDead || isActionLocked) return;

        // 1. 移動コマンドの解釈
        if (moveCmd != AIMoveCommand.Stay)
        {
            Vector3Int directionVector = ConvertCommandToGridDirection(moveCmd);
            Vector3Int targetPos = gridPosition + directionVector;

            // 移動先が侵入可能であれば、既存のcurrentPath構造を「1セル分のタスク」として上書きする
            if (mapManager != null && mapManager.IsWalkableForPathfinding(targetPos, this))
            {
                currentPath.Clear();
                currentPath.Add(targetPos);
            }
        }

        // 2. アビリティフラグの予約（UpdateAgentTickで安全に消費）
        pendingParanoia = useParanoia;
        pendingRecon = useRecon;
        pendingAbilityTargetGrid = targetGridPos;
    }

    private Vector3Int ConvertCommandToGridDirection(AIMoveCommand cmd)
    {
        switch (cmd)
        {
            case AIMoveCommand.Up: return Vector3Int.up;
            case AIMoveCommand.Down: return Vector3Int.down;
            case AIMoveCommand.Left: return Vector3Int.left;
            case AIMoveCommand.Right: return Vector3Int.right;
            case AIMoveCommand.UpLeft: return new Vector3Int(-1, 1, 0);
            case AIMoveCommand.UpRight: return new Vector3Int(1, 1, 0);
            case AIMoveCommand.DownLeft: return new Vector3Int(-1, -1, 0);
            case AIMoveCommand.DownRight: return new Vector3Int(1, -1, 0);
            default: return Vector3Int.zero;
        }
    }

    /// <summary>
    /// ★【重要】GameManager/Schedulerから1Tick（0.1秒）ごとに呼び出される、エージェントの主軸ロジック
    /// </summary>
    public void UpdateAgentTick()
    {
        if (isDead) return;

        // ------------------------------------------
        // A. 状態タイマー（デバフなど）のTick消化
        // ------------------------------------------
        if (isBlinded)
        {
            blindTickTimer--;
            if (blindTickTimer <= 0)
            {
                isBlinded = false;
                spriteRenderer.color = Color.white;
                Debug.Log($"<color=gray>{agentName} の目眩まし（ブラインド）が解けた。</color>");
            }
        }

        if (revealTickTimer > 0)
        {
            revealTickTimer--;
        }

        if (isRevealed)
        {
            lastKnownGridPosition = gridPosition;
            if (revealCauser != null)
            {
                revealCauser.lastTickEnemyHasRevealed = true;
            }
            else
            {
                Debug.Log("who?revealed");
            }
        }
        if (isBlinded)
        {
            if (blindCauser != null)
            {
                blindCauser.lastTickEnemyHasBlind = true;
            }
            else
            {
                Debug.Log("who?blind");
            }
        }

        // ------------------------------------------
        // B. アクションロック時のガードと射撃タイマーリセット
        // ------------------------------------------
        if (isActionLocked)
        {
            shotTickTimer = 0;
            return; // 設置・解除中はこれ以降の「移動」「AI思考」「戦闘」を一切行わない
        }

        // ------------------------------------------
        // ✨【新設】C. 予約されたアビリティコマンドの実行
        // ------------------------------------------
        TestManager tm = Object.FindFirstObjectByType<TestManager>();
        if (tm != null)
        {
            if (pendingParanoia && paranoiaCharges > 0)
            {
                tm.ExecuteCastAbilityAction(this, "Paranoia", pendingAbilityTargetGrid);
                // ※ExecuteCastAbilityActionの内部でchargeが減らない仕様の場合はここで減らしてください
                pendingParanoia = false;
            }
            if (pendingRecon && reconBoltCharges > 0)
            {
                tm.ExecuteCastAbilityAction(this, "Recon", pendingAbilityTargetGrid);
                pendingRecon = false;
            }
        }

        // ------------------------------------------
        // D. 移動ロジック（1歩先のグリッド座標の決定）
        // ------------------------------------------
        if (currentPath.Count > 0)
        {
            isMoving = true;
            Vector3 targetWorldPos = new Vector3(nextGridPosition.x + 0.5f, nextGridPosition.y + 0.5f, transform.position.z);

            // 現在のターゲットマスにほぼ到着しているなら、次の1歩へコマを進める
            if (Vector3.Distance(transform.position, targetWorldPos) <= 0.05f)
            {
                if (mapManager.IsWalkableForPathfinding(currentPath[0], this))
                {
                    if (mapManager.IsThereOnlyTargetPos(currentPath[0], this))
                    {
                        gridPosition = nextGridPosition;
                        nextGridPosition = currentPath[0];
                        currentPath.RemoveAt(0);
                    }
                }
            }
        }
        else
        {
            // パスが空で、かつ次の目的地に到着していたら移動終了
            Vector3 targetWorldPos = new Vector3(nextGridPosition.x + 0.5f, nextGridPosition.y + 0.5f, transform.position.z);
            if (Vector3.Distance(transform.position, targetWorldPos) <= 0.05f)
            {
                gridPosition = nextGridPosition;
                isMoving = false;
            }
        }

        // ------------------------------------------
        // E. 敵AIの思考ロジック更新（外部AI制御フラグがOFFのときだけ動かす）
        // ------------------------------------------
        if (!isControlledByExternalAI)
        {
            UpdateEnemyAILogic();
        }

        // ------------------------------------------
        // F. 自動戦闘・射撃判定ロジック
        // ------------------------------------------
        Agent target = FindVisibleEnemy();

        if (target != null && !isBlinded)
        {
            if (target.isEnemy)
            {
                target.RevealAgentTicks(30, this); // 3秒 = 30Tickリヴィール
            }

            int currentRequiredTicks = weaponTTKIntervalTicks; // 基本は2Tick(0.2秒)

            if (target.isRevealed)
            {
                currentRequiredTicks = Mathf.Max(1, (int)(weaponTTKIntervalTicks * 0.5f));
            }

            shotTickTimer++;

            if (shotTickTimer >= currentRequiredTicks)
            {
                float currentAccuracy = accuracyRate;
                if (isMoving)
                {
                    currentAccuracy = accuracyRate * 0.3f; // 命中率が30%に激減
                }
                if (target.isMoving)
                {
                    currentAccuracy *= 0.5f;
                }
                

                ShootTargetWithAccuracy(target, currentAccuracy);
                shotTickTimer = 0; // タイマーリセット
            }
        }
        else
        {
            shotTickTimer = 0; // 敵が見えなくなったら射撃タイマーリセット
        }
    }

    private void UpdateEnemyAILogic()
    {
        if (isMoving || isActionLocked) return;

        TestManager tm = Object.FindFirstObjectByType<TestManager>();
        SpikeManager sm = Object.FindFirstObjectByType<SpikeManager>();
        if (tm == null || sm == null || mapManager == null) return;

        // アビリティ（パラノイア）の自動発動
        Agent target = FindVisibleEnemy();
        if (target != null && paranoiaCharges > 0 && !target.isBlinded)
        {
            float distanceToTarget = Vector3Int.Distance(this.gridPosition, target.gridPosition);
            if (distanceToTarget <= 10.0f)
            {
                tm.ExecuteCastAbilityAction(this, "Paranoia", target.gridPosition);
            }
        }

        // ルールA：ボットが防衛側 ＆ スパイクが設置済みのとき
        if (isEnemy == tm.isPlayerAttacker)
        {
            if (sm.currentState == SpikeManager.SpikeState.Planted || sm.currentState == SpikeManager.SpikeState.Defusing)
            {
                if (sm.IsBeingDefused && sm.defusingAgent != this)
                {
                    if (currentPath.Count > 0) currentPath.Clear();
                    isMoving = false;
                    return;
                }

                if (Vector3Int.Distance(gridPosition, sm.plantedGridPos) > 1.1f)
                {
                    if (lastAssignedTarget != sm.plantedGridPos)
                    {
                        SetTargetGridPosition(sm.plantedGridPos);
                        lastAssignedTarget = sm.plantedGridPos;
                        Debug.Log($"<blue>【防衛AI】</blue> {agentName} が解除のために {sm.plantedGridPos} へ急行中。");
                    }
                }
                else
                {
                    sm.StartDefuseSpike(this);
                }
                return;
            }
        }
        // ルールB：ボットが攻撃側（動的サイト探索）
        else
        {
            if (hasSpike)
            {
                if (mapManager.IsPlantableArea(gridPosition))
                {
                    sm.StartPlantSpike(gridPosition, this);
                    lastAssignedTarget = new Vector3Int(-999, -999, -999);
                    return;
                }
                else
                {
                    if (currentPath.Count == 0)                   
                    {

                        Vector3Int plantPos = GetBestPlantPosition();

                        if (GetBestPlantPosition().x != -999)
                        {
                            SetTargetGridPosition(plantPos);
                            lastAssignedTarget = plantPos;
                            Debug.Log($"<color=red>【攻撃AI】</color> キャリア {agentName} が設置エリア {plantPos} を自動検出して進軍開始！");
                        }
                    }
                }
                return;
            }
            else if (sm.currentState == SpikeManager.SpikeState.Dropped)
            {
                if (lastAssignedTarget != sm.droppedGridPos || currentPath.Count == 0)
                {
                    SetTargetGridPosition(sm.droppedGridPos);
                    lastAssignedTarget = sm.droppedGridPos;
                }
                if (gridPosition == sm.droppedGridPos)
                {
                    sm.PickupSpike(this);
                    lastAssignedTarget = new Vector3Int(-999, -999, -999);
                }
                return;
            }
        }

        // ルールC：通常時のランダム徘徊（Tickベース）
        aiDecisionTickTimer++;
        if (aiDecisionTickTimer >= aiDecisionTickInterval)
        {
            aiDecisionTickTimer = 0;
            aiDecisionTickInterval = Random.Range(10, 20);

            if (currentPath.Count > 0) return;

            if (myRole == BotRole.Entry)
            {
                Agent carrier = null;
                List<Agent> allAgents = GameManager.Instance.AllAgents;
                foreach (Agent a in allAgents)
                {
                    if (a != null && a.isEnemy == this.isEnemy && a.hasSpike)
                    {
                        carrier = a;
                        break;
                    }
                }

                if (carrier != null && carrier != this)
                {
                    Vector3Int offset = new Vector3Int(Random.Range(-2, 3), Random.Range(-2, 3), 0);
                    Vector3Int followPos = carrier.gridPosition + offset;
                    if (mapManager.IsWalkableForPathfinding(followPos, this))
                    {
                        SetTargetGridPosition(followPos);
                        lastAssignedTarget = followPos;
                    }
                }
            }
            else if (myRole == BotRole.Lurker)
            {
                int lurkX = Random.Range(0, 8);
                int lurkY = Random.Range(0, 8);
                Vector3Int lurkPos = new Vector3Int(lurkX, lurkY, 0);

                if (mapManager.IsWalkableForPathfinding(lurkPos, this))
                {
                    SetTargetGridPosition(lurkPos);
                    lastAssignedTarget = lurkPos;
                    Debug.Log($"<color=magenta>【ラーク行動】</color> {agentName} は単独でエリア {lurkPos} へ潜伏に向かっています。");
                }
            }
        }
    }

    public void DecideTargetSite()
    {
        currentTargetSite = (Random.value > 0.5f) ? "A" : "B";
        Debug.Log($"[LogicBot] 🎯 このラウンドの目標サイトは 【{currentTargetSite}】 に決定しました！");
    }

    public Vector3Int GetBestPlantPosition()
    {
        Vector3Int bestPlantPos = new Vector3Int(-999, -999, -999);
        float closestDist = float.MaxValue;

        for (int x = -20; x < 20; x++)
        {
            // 🔥 設定したターゲットサイトに応じて、探索するX座標の範囲を制限する
            if (currentTargetSite == "A" && x >= 0) continue; // Aサイトなら右半分（10〜19）は無視
            if (currentTargetSite == "B" && x < 0) continue;  // Bサイトなら左半分（0〜9）は無視

            for (int y = -20; y < 20; y++)
            {
                Vector3Int checkPos = new Vector3Int(x, y, 0);
                if (mapManager.IsPlantableArea(checkPos) && mapManager.IsWalkable(checkPos))
                {
                    float dist = Vector3Int.Distance(gridPosition, checkPos);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        bestPlantPos = checkPos;
                    }
                }
            }
        }

        return bestPlantPos;
    }

    private Agent FindVisibleEnemy()
    {
        if (GameManager.Instance == null) return null;
        List<Agent> allAgents = GameManager.Instance.AllAgents;

        Agent closestEnemy = null;
        float minDistance = float.MaxValue;

        foreach (Agent other in allAgents)
        {
            if (other != null && other.isEnemy != this.isEnemy && !other.isDead)
            {
                if (FieldOfViewSystem.HasLineOfSight(this.gridPosition, other.gridPosition, mapManager))
                {
                    float dist = Vector3.Distance(transform.position, other.transform.position);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestEnemy = other;
                    }
                }
            }
        }
        return closestEnemy;
    }

    public List<Agent> FindVisibleEnemies()
    {
        if (GameManager.Instance == null) return null;
        List<Agent> allAgents = GameManager.Instance.AllAgents;

        List<Agent> enemies = new List<Agent>();

        foreach (Agent other in allAgents)
        {
            if (other != null && other.isEnemy != this.isEnemy && !other.isDead)
            {
                if (FieldOfViewSystem.HasLineOfSight(this.gridPosition, other.gridPosition, mapManager))
                {
                    float dist = Vector3.Distance(transform.position, other.transform.position);
                    enemies.Add(other);
                }
            }
        }
        return enemies;
    }

    public void SetTargetGridPosition(Vector3Int targetPos)
    {
        // 🔥【重要】すでに見た目の移動中なら、新しい命令は絶対に受け付けない！
        if (isMovingAnimation) return;
        if (isDead || isActionLocked) return;
        if (mapManager != null && !mapManager.IsWall(targetPos))
        {
            currentPath = FindPath(gridPosition, targetPos);
        }
    }

    public void ShootTargetWithAccuracy(Agent target, float dynamicAccuracy)
    {
        if (target == null || target.isDead || isActionLocked) return;

        if (shotSound != null)
        {
            AudioSource.PlayClipAtPoint(shotSound, transform.position, 0.5f);
        }

        bool isShoot = true;

        if (Random.value <= dynamicAccuracy && Random.value >= avoidRate)
        {
            if (Random.value <= hsRate)
            {
                Debug.Log($"<color=red><b>[CRITICAL HEADSHOT!!]</b></color> {agentName} ➔ {target.agentName} (💥 {hsDamage} Dmg!)");
                target.TakeDamage(hsDamage, this);
                this.lastTickDamageDealt += hsDamage;
            }
            else
            {
                Debug.Log($"<color=orange>[命中の胴体撃ち]</color> {agentName} ➔ {target.agentName} ({power} Dmg)");
                target.TakeDamage(power, this);
                this.lastTickDamageDealt += power;
            }
        }
        else
        {
            isShoot = false;
            string reason = isMoving ? "（走り撃ちによるブレ）" : "";
            Debug.Log($"<color=gray>[失弾 - MISS]</color> {agentName} の放った弾は {target.agentName} の横をすり抜けた！{reason}");
        }

        if (isShoot && bulletTracerPrefab != null)
        {
            Vector3 spawnPos = new Vector3(transform.position.x, transform.position.y, 0f);
            Vector3 targetPos = new Vector3(target.transform.position.x, target.transform.position.y, 0f);

            GameObject tracerGo = Instantiate(bulletTracerPrefab, spawnPos, Quaternion.identity);
            BulletTracer tracer = tracerGo.GetComponent<BulletTracer>();
            if (tracer != null)
            {
                tracer.Init(spawnPos, targetPos);
                if (GridTickScheduler.Instance != null)
                {
                    GridTickScheduler.Instance.RegisterBulletTracer(tracer);
                }
            }
        }
    }



    public void TakeDamage(int damage, Agent causer)
    {
        hp -= damage;
        this.lastTickDamageTaken += damage;
        Debug.Log($"{agentName} HP: {Mathf.Max(0, hp)}/150");
        if (hp <= 0)
        {
            isDead = true;
            this.lastTickDidDie = true; // 🔥「今、死んだよ！」とメモ
            causer.lastTickDidKill = true;
            spriteRenderer.color = Color.gray;
            SpikeManager sm = Object.FindFirstObjectByType<SpikeManager>();

            if (sm != null && sm.defusingAgent == this)
            {
                sm.CancelDefuse();
            }
            if (sm != null && sm.plantingAgent == this)
            {
                sm.CancelPlant();
            }
            if (hasSpike && sm != null)
            {
                sm.DropSpike(gridPosition);
            }

            UIManager.Instance.ShowAnnouncement($"【死亡】 {agentName} がキルされました。", Color.yellow);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.CheckMatchRules(GridTickScheduler.Instance.GetCurrentTick());
            }

            gameObject.SetActive(false);
        }
    }

    public void SyncState(Vector3Int pos, int currentHp, bool deadStatus, bool spikeStatus, BotRole role)
    {
        gridPosition = pos;
        nextGridPosition = pos;
        transform.position = new Vector3(pos.x + 0.5f, pos.y + 0.5f, transform.position.z);

        hp = currentHp;
        isDead = deadStatus;
        hasSpike = spikeStatus;
        myRole = role;

        isMoving = false;
        isActionLocked = false;
        isBlinded = false;
        revealedTrigger = false;
        currentPath.Clear();
        shotTickTimer = 0;

        spriteRenderer.color = isDead ? Color.gray : Color.white;
        gameObject.SetActive(!isDead);
    }

    public AgentStateData SaveState()
    {
        AgentStateData state = new AgentStateData();
        state.agentName = this.agentName;
        state.gridPosition = this.gridPosition;
        state.hp = this.hp;
        state.isDead = this.isDead;
        state.hasSpike = this.hasSpike;
        state.paranoiaCharges = this.paranoiaCharges;
        state.reconBoltCharges = this.reconBoltCharges;
        state.myRole = this.myRole;
        state.isActive = this.isActive;
        state.isBlinded = this.isBlinded;
        state.blindTickTimer = this.blindTickTimer;
        // 🔥【重要】移動関連のステリートをキャプチャ
        state.nextGridPosition = this.nextGridPosition;
        state.lastAssignedTarget = this.lastAssignedTarget;
        state.isMoving = this.isMoving;
        state.isActionLocked = this.isActionLocked;
        // List型はそのまま代入すると参照が同じになってしまう（ディープコピーが必要）
        state.currentPath = new List<Vector3Int>(this.currentPath);

        return state;
    }

    public void LoadState(AgentStateData state)
    {
        this.agentName = state.agentName;
        this.gridPosition = state.gridPosition;
        this.nextGridPosition = state.gridPosition;
        this.hp = state.hp;
        this.isDead = state.isDead;
        this.hasSpike = state.hasSpike;
        this.paranoiaCharges = state.paranoiaCharges;
        this.reconBoltCharges = state.reconBoltCharges;
        this.myRole = state.myRole;
        // 🔥【重要】移動関連のステートを寸分狂わずに復元
        this.nextGridPosition = state.nextGridPosition;
        this.lastAssignedTarget = state.lastAssignedTarget;
        this.isMoving = state.isMoving;
        this.isActionLocked = state.isActionLocked;
        this.isBlinded = state.isBlinded;
        this.blindTickTimer = state.blindTickTimer;
        // セーブデータから新しくリストを生成して復元
        this.currentPath = new List<Vector3Int>(state.currentPath);
        this.gameObject.SetActive(state.isActive);

        if (this.hasSpike)
        {
            SpikeManager sm = Object.FindFirstObjectByType<SpikeManager>();
            if (sm != null)
            {
                sm.ForceSetCarrier(this);
            }
        }

        transform.position = new Vector3(state.gridPosition.x + 0.5f, state.gridPosition.y + 0.5f, transform.position.z);
        spriteRenderer.color = isDead ? Color.gray : Color.white;

        isActionLocked = false;
        currentPath.Clear();
    }

    public void ApplyBlindTicks(int durationTicks, Agent causer)
    {
        if (isDead) return;
        isBlinded = true;
        blindTickTimer = durationTicks;
        spriteRenderer.color = new Color(0.1f, 0.1f, 0.1f, spriteRenderer.color.a);
        causer.lastTickDidBlindEnemy = true;
        blindCauser = causer;
    }

    public void RevealAgentTicks(int durationTicks, Agent causer)
    {
        if (isDead) return;
        if (durationTicks > revealTickTimer)
        {
            revealTickTimer = durationTicks;
        }
        Color currentColor = spriteRenderer.color;
        spriteRenderer.color = new Color(currentColor.r, currentColor.g, currentColor.b, 1f);
        causer.lastTickDidRevealEnemy = true;
        revealCauser = causer;
    }

    private void UpdateVisualVisibility()
    {
        if (revealTickTimer > 0)
        {
            revealedTrigger = true;
        }
        else
        {
            revealedTrigger = false;
            
        }

        if (IsVisibleToPlayer)
        {
            Color currentColor = spriteRenderer.color;
            teamRenderer.color = new Color(teamRenderer.color.r, teamRenderer.color.g, teamRenderer.color.b, 1f);
            hpRenderer.color = new Color(hpRenderer.color.r, hpRenderer.color.g, hpRenderer.color.b, 1f);
            spriteRenderer.color = new Color(currentColor.r, currentColor.g, currentColor.b, 1f);
        }
        else
        {
            teamRenderer.color = new Color(teamRenderer.color.r, teamRenderer.color.g, teamRenderer.color.b, 0f);
            hpRenderer.color = new Color(hpRenderer.color.r, hpRenderer.color.g, hpRenderer.color.b, 0f);
            spriteRenderer.color = new Color(spriteRenderer.color.r, spriteRenderer.color.g, spriteRenderer.color.b, 0f);
        }
    }

    private void UpdateSpikeIndicatorVisual()
    {
        if (hasSpike)
        {
            Transform indicator = transform.Find("SpikeIndicator");
            if (indicator == null)
            {
                GameObject indGo = new GameObject("SpikeIndicator");
                indGo.transform.SetParent(this.transform);
                indGo.transform.localPosition = new Vector3(0f, 0.7f, 0f);

                SpriteRenderer sr = indGo.AddComponent<SpriteRenderer>();
                int size = 16;
                Texture2D tex = new Texture2D(size, size);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        if (Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f)) <= size / 2f)
                            tex.SetPixel(x, y, Color.white);
                        else
                            tex.SetPixel(x, y, Color.clear);
                    }
                }
                tex.Apply();
                sr.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                sr.color = new Color(1f, 0.8f, 0f, 1f);
                sr.sortingOrder = 2;
                indGo.transform.localScale = new Vector3(3f, 3f, 1f);
            }

            Transform currentIndicator = transform.Find("SpikeIndicator");
            if (currentIndicator != null)
            {
                SpriteRenderer sr = currentIndicator.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    TestManager tm = Object.FindFirstObjectByType<TestManager>();
                    bool playerIsAttacker = (tm != null) ? tm.isPlayerAttacker : true;

                    if (!playerIsAttacker && isEnemy) sr.enabled = false;
                    else sr.enabled = true;

                    if (CheatRevealed) sr.enabled = true;
                }
            }
        }
        else
        {
            Transform indicator = transform.Find("SpikeIndicator");
            if (indicator != null) Destroy(indicator.gameObject);
        }
    }

    private List<Vector3Int> FindPath(Vector3Int start, Vector3Int end)
    {
        List<Vector3Int> path = new List<Vector3Int>();

        if (mapManager != null && !mapManager.IsWalkableForPathfinding(end, this)) return path;
        if (mapManager != null && !mapManager.IsWalkableForPathfinding(start, this)) return path;
        if (start == end) return path;

        Queue<Vector3Int> queue = new Queue<Vector3Int>();
        Dictionary<Vector3Int, Vector3Int> cameFrom = new Dictionary<Vector3Int, Vector3Int>();

        queue.Enqueue(start);
        cameFrom[start] = start;

        Vector3Int[] directions = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        bool found = false;
        int safetyCounter = 0;

        while (queue.Count > 0)
        {
            safetyCounter++;
            if (safetyCounter > 5000) break;

            Vector3Int current = queue.Dequeue();
            if (current == end)
            {
                found = true;
                break;
            }

            foreach (Vector3Int dir in directions)
            {
                Vector3Int next = current + dir;
                if (mapManager.IsWalkableForPathfinding(next, this))
                {
                    if (!cameFrom.ContainsKey(next))
                    {
                        cameFrom[next] = current;
                        queue.Enqueue(next);
                    }
                }
            }
        }

        if (!found) return path;

        Vector3Int curr = end;
        while (curr != start)
        {
            path.Add(curr);
            curr = cameFrom[curr];
        }
        path.Reverse();
        return path;
    }
}