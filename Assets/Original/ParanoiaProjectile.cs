using System.Collections.Generic;
using UnityEngine;

public class ParanoiaProjectile : MonoBehaviour
{
    private Vector3 targetWorldPos;
    private float speed = 6f; // パラノイアが飛ぶ速度（見た目の補間用）
    private float gridRadius = 1.2f; // ★物理コライダーの代わりに、グリッド距離で巻き込み判定を行う
    private int remainingTicks = 300; // ★LifetimeをTickで管理（3.0秒 = 30Tick）
    private int blindTickDuration = 50; // ★ブラインド時間をTickで管理（5.0秒 = 50Tick）
    private bool isInitialized = false;

    public Agent owner = null;
    private Vector3Int lastCheckedGrid = new Vector3Int(-999, -999, -999);


    /// <summary>
    /// 発射時に初期位置と目的地を設定する関数
    /// </summary>
    public void Launch(Vector3 start, Vector3 target)
    {
        transform.position = start;
        targetWorldPos = target;
        isInitialized = true;

        // Z軸を0に固定して計算のズレを防止
        transform.position = new Vector3(transform.position.x, transform.position.y, 0f);
        targetWorldPos.z = 0f;
    }

    /// <summary>
    /// 💡 見た目のぬるっとした直線移動だけをUpdateで処理（描画用）
    /// </summary>
    void Update()
    {
        if (!isInitialized) return;

        // 目的地に向かって直線移動（壁を貫通）
        transform.position = Vector3.MoveTowards(transform.position, targetWorldPos, speed * Time.deltaTime);
    }

    /// <summary>
    /// ★【新設】GameManagerから1Tick（0.1秒）ごとに呼び出される判定ロジック
    /// </summary>
    public void UpdateProjectileTick()
    {
        if (!isInitialized) return;

        remainingTicks--;
        if (remainingTicks <= 0)
        {
            DeactivateProjectile(); // ★Destroyではなく非アクティブ化
            return;
        }

        Vector3Int currentGrid = Vector3Int.RoundToInt(transform.position);
        currentGrid.z = 0;

        if (currentGrid != lastCheckedGrid)
        {
            lastCheckedGrid = currentGrid;
            CheckHitAgents(currentGrid);
        }

        if (Vector3.Distance(transform.position, targetWorldPos) <= 0.1f)
        {
            DeactivateProjectile(); // ★Destroyではなく非アクティブ化
            return;
        }
    }

    /// <summary>
    /// ★弾をプールに戻す（非アクティブ化）
    /// </summary>
    private void DeactivateProjectile()
    {
        isInitialized = false;
        owner = null;
        lastCheckedGrid = new Vector3Int(-999, -999, -999);
        gameObject.SetActive(false); // 次回発射時に再利用
    }

    /// <summary>
    /// ★物理演算（Physics2D）を使わず、シーン上の全Agentとのグリッド距離で巻き込み判定を行う
    /// </summary>
    private void CheckHitAgents(Vector3Int projectileGrid)
    {
        Agent[] allAgents = Object.FindObjectsByType<Agent>(FindObjectsSortMode.None);
        Agent ownerAgent = owner != null ? owner.GetComponent<Agent>() : null;

        foreach (Agent agent in allAgents)
        {
            if (agent == null || agent.isDead) continue;

            // 発射者（味方ボットなど）は巻き込まないようにするガード
            if (ownerAgent != null && agent.isEnemy == ownerAgent.isEnemy) continue;

            // 弾の現在グリッドと、エージェントの現在グリッドの距離を計測
            float distance = Vector3Int.Distance(projectileGrid, agent.gridPosition);

            // 設定した半径（マス数）以内にエージェントがいればヒット
            if (distance <= gridRadius)
            {
                // まだブラインドになっていないキャラならブラインド（Tick版）を付与
                if (!agent.isBlinded)
                {
                    // Agent側に実装したTick用のApplyBlindを呼び出す
                    agent.ApplyBlindTicks(blindTickDuration, owner);
                    Debug.Log($"<color=purple>【スキル命中】</color> パラノイアが {agent.agentName} に命中！ {blindTickDuration} Ticks ブラインド付与。");
                }
            }
        }
    }

    /// <summary>
    /// 現在のパラノイアの状態をディープコピーして保存する
    /// </summary>
    public ParanoiaStateData SaveState()
    {
        ParanoiaStateData state = new ParanoiaStateData();
        state.isActive = gameObject.activeSelf;
        state.position = transform.position;
        state.remainingTicks = this.remainingTicks;
        state.isInitialized = this.isInitialized;
        state.owner = this.owner;
        state.lastCheckedGrid = this.lastCheckedGrid;

        return state;
    }

    /// <summary>
    /// タイムカプセルからパラノイアの状態を寸分狂わずに復元する
    /// </summary>
    public void LoadState(ParanoiaStateData state)
    {
        gameObject.SetActive(state.isActive);

        transform.position = state.position;
        this.remainingTicks = state.remainingTicks;
        this.isInitialized = state.isInitialized;
        this.owner = state.owner;
        this.lastCheckedGrid = state.lastCheckedGrid;

    }
}