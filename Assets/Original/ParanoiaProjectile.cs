using System.Collections.Generic;
using UnityEngine;

public class ParanoiaProjectile : MonoBehaviour
{
    private Vector3 direction; // ★方向ベクトルを保持
    private float speed = 0.3f;
    private float gridRadius = 1.2f;
    private int ticksInitialized = 35;
    private int remainingTicks = 35;
    private int blindTickDuration = 50;
    private bool isInitialized = false;

    public Agent owner = null;
    private Vector3Int lastCheckedGrid = new Vector3Int(-999, -999, -999);

    public void Launch(Vector3 start, Vector3 target)
    {
        remainingTicks = ticksInitialized;
        transform.position = new Vector3(start.x, start.y, 0f);

        // ★ターゲットへの方向ベクトルを計算（Zは0に固定）
        Vector3 targetPos = new Vector3(target.x, target.y, 0f);
        direction = (targetPos - transform.position).normalized;

        isInitialized = true;
    }

    public void UpdateProjectileTick()
    {
        if (!isInitialized) return;

        // ★目的地に関係なく、常にdirection方向に進み続ける
        transform.position += direction * speed;

        remainingTicks--;
        if (remainingTicks <= 0)
        {
            DeactivateProjectile();
            return;
        }

        Vector3Int currentGrid = Vector3Int.RoundToInt(transform.position);
        currentGrid.z = 0;

        if (currentGrid != lastCheckedGrid)
        {
            lastCheckedGrid = currentGrid;
            CheckHitAgents(currentGrid);
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
        state.direction = this.direction; // ★追加

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
        this.direction = state.direction; // ★追加

    }
}