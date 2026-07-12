using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class TestManager : MonoBehaviour
{
    public static TestManager Instance { get; private set; }

    [SerializeField] private Grid grid;

    // ★強化学習環境を崩さないよう、事前にアタッチするか生成しておいたプール用の参照
    [Header("--- Object Pools ---")]
    [SerializeField] private List<ParanoiaProjectile> paranoiaPool = new List<ParanoiaProjectile>();
    [SerializeField] private List<ReconBoltProjectile> reconBoltPool = new List<ReconBoltProjectile>();

    [Header("--- Selection & Hover UI ---")]
    [SerializeField] private GameObject selectionRing;    // ★事前に用意したゲームオブジェクトをアタッチ（テクスチャ動的生成を排除）
    [SerializeField] private GameObject hoverCursor;     // ★事前に用意したゲームオブジェクトをアタッチ
    private SpriteRenderer ringSr;

    [Header("--- Game Rules ---")]
    public bool isPlayerAttacker = true;

    [HideInInspector] public bool isDuelOccurred = false;
    private Agent selectedAgent;

    // === アビリティ操作の内部変数 ===
    private bool isAimingAbility = false;
    private string selectedAbilityName = "";

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (selectionRing != null) ringSr = selectionRing.GetComponent<SpriteRenderer>();
    }

    void Start()
    {
        // ★ Start() 内の重い Texture2D 生成ループはすべて削除！
        if (hoverCursor != null) hoverCursor.SetActive(true);
        if (selectionRing != null) selectionRing.SetActive(false);
    }

    void Update()
    {
        // 1. 人間用のホバーカーソル表示・UI更新（AIのロジックには影響しない演出）
        UpdateHumanUI();

        // 2. ★【超重要】人間がプレイしている場合のみ入力を受け付けるガード
        HandleHumanInput();

        if (isDuelOccurred && Time.timeScale > 0f)
        {
            CheckBattleEnd();
        }
    }

    /// <summary>
    /// 人間用の見た目の同期処理
    /// </summary>
    private void UpdateHumanUI()
    {
        if (hoverCursor == null || grid == null || Camera.main == null) return;

        // マウス位置の追従
        Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, Camera.main.nearClipPlane));
        Vector3Int currentHoverGrid = grid.WorldToCell(mouseWorldPos);
        hoverCursor.transform.position = new Vector3(currentHoverGrid.x + 0.5f, currentHoverGrid.y + 0.5f, 0f);

        // 選択リングの追従
        if (selectedAgent != null && !selectedAgent.isDead)
        {
            if (selectionRing != null)
            {
                selectionRing.SetActive(true);
                selectionRing.transform.position = new Vector3(selectedAgent.transform.position.x, selectedAgent.transform.position.y, 0f);
            }

            if (ringSr != null)
            {
                ringSr.color = isAimingAbility ? new Color(1f, 0.3f, 0f, 0.8f) : new Color(0f, 0.75f, 1f, 0.8f);
            }
        }
        else
        {
            if (selectionRing != null) selectionRing.SetActive(false);
        }
    }

    /// <summary>
    /// 人間用のキーボード・マウス入力監視（Input Systemに統一）
    /// </summary>
    private void HandleHumanInput()
    {
        // 外部機器の接続状態チェックガード
        if (Keyboard.current == null || Mouse.current == null) return;

        // --- タクティカルポーズ解除 ---
        if (isDuelOccurred && Time.timeScale == 0f && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Time.timeScale = 1f;
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowAnnouncement("--- リアルタイム戦闘開始！ ---", Color.green);
            }
        }

        // --- タイムカプセル巻き戻し（新Input Systemに統一） ---
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
        {
            if (GridTickScheduler.Instance != null && TimeCapsuleManager.Instance != null)
            {
                int currentTick = GridTickScheduler.Instance.GetCurrentTick();
                int targetTick = Mathf.Max(0, currentTick - 15); // 0未満にならないように制限
                TimeCapsuleManager.Instance.LoadTickSnapshot(targetTick);
            }
        }

        // --- アビリティ構えのキャンセル処理（暴発を防ぐためマウスクリック処理より先に評価） ---
        if (isAimingAbility && (Mouse.current.rightButton.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            CancelAbilityAiming();
            return; // キャンセルしたフレームは他のマウス入力をスキップ
        }

        // エージェントが選択されていない、または死亡している場合はこれ以降のコマンド入力を受け付けない
        //if (selectedAgent == null || selectedAgent.isDead) return;

        // --- キーボードショートカット ---
        if (!isAimingAbility)
        {
            if (Keyboard.current.qKey.wasPressedThisFrame) ExecuteSelectAbilityAction(selectedAgent, "Paranoia");
            if (Keyboard.current.eKey.wasPressedThisFrame) ExecuteSelectAbilityAction(selectedAgent, "ReconBolt");

            SpikeManager sm = Object.FindFirstObjectByType<SpikeManager>();
            if (sm != null)
            {
                if (Keyboard.current.gKey.wasPressedThisFrame && selectedAgent.hasSpike) ExecuteDropSpikeAction(selectedAgent, sm);
                if (Keyboard.current.fKey.wasPressedThisFrame && !selectedAgent.hasSpike) ExecutePickupSpikeAction(selectedAgent, sm);
                if (Keyboard.current.digit4Key.wasPressedThisFrame) ExecuteSpikeInteractAction(selectedAgent, sm);
            }
        }

        // --- マウスクリック処理のハンドリング ---
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        if (Time.timeScale > 0 && Mouse.current.leftButton.wasPressedThisFrame && Camera.main != null && grid != null)
        {
            Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
            Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, Camera.main.nearClipPlane));
            Vector3Int clickedGridPos = grid.WorldToCell(mouseWorldPos);
            clickedGridPos.z = 0;

            if (isAimingAbility)
            {
                ExecuteCastAbilityAction(selectedAgent, selectedAbilityName, clickedGridPos);
            }
            else
            {
                HandleSelectionOrMove(mouseWorldPos, clickedGridPos);
            }
        }
    }

    private void HandleSelectionOrMove(Vector3 mouseWorldPos, Vector3Int clickedGridPos)
    {
        RaycastHit2D hit = Physics2D.Raycast(mouseWorldPos, Vector2.zero);
        if (hit.collider != null)
        {
            Agent clickedAgent = hit.collider.GetComponent<Agent>();
            if (clickedAgent != null)
            {
                if (clickedAgent.isEnemy && !clickedAgent.IsVisibleToPlayer) return; // 霧の中の敵

                if (!clickedAgent.isEnemy)
                {
                    selectedAgent = clickedAgent;
                    CancelAbilityAiming();
                    return;
                }
            }
        }

        // 移動先を命令
        ExecuteMoveAction(selectedAgent, clickedGridPos);
    }

    // ===================================================================================
    // ★【AI環境対応】共通ロジック
    // ===================================================================================

    public void ExecuteMoveAction(Agent agent, Vector3Int targetGrid)
    {
        if (agent == null || agent.isDead) return;
        agent.SetTargetGridPosition(targetGrid);
    }

    public void ExecuteSelectAbilityAction(Agent agent, string abilityName)
    {
        if (agent == null || agent.isDead) return;

        if (abilityName == "Paranoia" && agent.paranoiaCharges <= 0) return;
        if (abilityName == "ReconBolt" && agent.reconBoltCharges <= 0) return;

        isAimingAbility = true;
        selectedAbilityName = abilityName;
        selectedAgent = agent;
    }

    public void ExecuteCastAbilityAction(Agent agent, string abilityName, Vector3Int targetGrid)
    {
        if (agent == null || agent.isDead) return;

        if (abilityName == "Paranoia")
        {
            if (agent.paranoiaCharges <= 0) return;
            agent.paranoiaCharges--;
            CastParanoiaFromPool(agent, agent.gridPosition, targetGrid);
        }
        else if (abilityName == "ReconBolt")
        {
            if (agent.reconBoltCharges <= 0) return;
            agent.reconBoltCharges--;
            CastReconBoltFromPool(agent.gridPosition, targetGrid, agent);
        }

        CancelAbilityAiming();
    }

    public void ExecuteDropSpikeAction(Agent agent, SpikeManager sm)
    {
        if (agent == null || sm == null) return;
        sm.DropSpike(agent.gridPosition);
    }

    public void ExecutePickupSpikeAction(Agent agent, SpikeManager sm)
    {
        if (agent == null || sm == null) return;
        if (sm.currentState == SpikeManager.SpikeState.Dropped && agent.gridPosition == sm.droppedGridPos)
        {
            sm.PickupSpike(agent);
        }
    }

    public void ExecuteSpikeInteractAction(Agent agent, SpikeManager sm)
    {
        if (agent == null || sm == null) return;

        if (agent.isEnemy != isPlayerAttacker && agent.hasSpike)
        {
            MapManager mm = Object.FindFirstObjectByType<MapManager>();
            if (mm != null && mm.IsPlantableArea(agent.gridPosition))
            {
                sm.StartPlantSpike(agent.gridPosition, agent);
            }
        }
        else if (agent.isEnemy == isPlayerAttacker && sm.currentState == SpikeManager.SpikeState.Planted)
        {
            if (Vector3Int.Distance(agent.gridPosition, sm.plantedGridPos) <= 1.5f)
            {
                sm.StartDefuseSpike(agent);
            }
        }
    }

    // ===================================================================================
    // 💥 オブジェクトプールを使用したアビリティ投射ロジック
    // ===================================================================================

    private void CastParanoiaFromPool(Agent owner, Vector3Int startPos, Vector3Int targetPos)
    {
        Vector3 startWorld = new Vector3(startPos.x + 0.5f, startPos.y + 0.5f, 0f);
        Vector3 targetWorld = new Vector3(targetPos.x + 0.5f, targetPos.y + 0.5f, 0f);
        Vector3 direction = (targetWorld - startWorld).normalized;
        startWorld += direction * 1.5f;

        ParanoiaProjectile proj = paranoiaPool.Find(p => p != null && !p.gameObject.activeSelf);
        if (proj != null)
        {
            proj.gameObject.SetActive(true);
            proj.owner = owner;
            proj.Launch(startWorld, targetWorld);
        }
    }

    private void CastReconBoltFromPool(Vector3Int startPos, Vector3Int targetPos, Agent agent)
    {
        Vector3 startWorld = new Vector3(startPos.x + 0.5f, startPos.y + 0.5f, 0f);
        Vector3 targetWorld = new Vector3(targetPos.x + 0.5f, targetPos.y + 0.5f, 0f);
        Vector3 direction = (targetWorld - startWorld).normalized;
        startWorld += direction * 0.6f;

        ReconBoltProjectile proj = reconBoltPool.Find(p => p != null && !p.gameObject.activeSelf);
        if (proj != null)
        {
            MapManager mm = Object.FindFirstObjectByType<MapManager>();
            proj.owner = agent;
            proj.gameObject.SetActive(true); // プールから有効化する処理を追加
            proj.Launch(startWorld, targetWorld, mm);
        }
    }

    private void CancelAbilityAiming()
    {
        isAimingAbility = false;
        selectedAbilityName = "";
    }

    public void StartDuel(Agent agentA, Agent agentB)
    {
        // 必要に応じて実装
    }

    private void CheckBattleEnd()
    {
        Agent[] allAgents = Object.FindObjectsByType<Agent>(FindObjectsSortMode.None);
        int playerCount = 0;
        int enemyCount = 0;

        foreach (Agent a in allAgents)
        {
            if (a == null || a.isDead) continue;
            if (!a.isEnemy) playerCount++;
            else enemyCount++;
        }

        if (playerCount == 0 || enemyCount == 0)
        {
            isDuelOccurred = false;
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowAnnouncement("決着がつきました", Color.white);
            }
        }
    }

    // ===================================================================================
    // ⏳ タイムカプセル（状態保存・復元）システム用のロジック
    // ===================================================================================

    public TestManagerStateData SaveState()
    {
        TestManagerStateData state = new TestManagerStateData();
        state.isPlayerAttacker = this.isPlayerAttacker;
        state.isDuelOccurred = this.isDuelOccurred;
        state.selectedAgent = this.selectedAgent;
        state.isAimingAbility = this.isAimingAbility;
        state.selectedAbilityName = this.selectedAbilityName;
        return state;
    }

    public void LoadState(TestManagerStateData state)
    {
        this.isPlayerAttacker = state.isPlayerAttacker;
        this.isDuelOccurred = state.isDuelOccurred;
        this.selectedAgent = state.selectedAgent;
        this.isAimingAbility = state.isAimingAbility;
        this.selectedAbilityName = state.selectedAbilityName;
    }
}