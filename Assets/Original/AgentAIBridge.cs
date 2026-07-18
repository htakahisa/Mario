using UnityEngine;
using System.Collections.Generic;

public class AgentAIBridge : MonoBehaviour
{
    private Agent agent;
    private MapManager mapManager;
    private SpikeManager spikeManager;

    private void Awake()
    {
        agent = GetComponent<Agent>();
        mapManager = MapManager.Instance;
        spikeManager = SpikeManager.Instance;
    }

    /// <summary>
    /// ★AIに渡す「現在の世界の状態」を数値の配列としてパックする
    /// </summary>
    public float[] CollectObservations()
    {
        List<float> obs = new List<float>();

        // ==========================================
        // 1. 自分の情報 (5要素)
        // ==========================================
        obs.Add(transform.position.x);
        obs.Add(transform.position.y);
        obs.Add(agent.hp / 100f); // 0〜1に正規化
        obs.Add(agent.isDead ? 1f : 0f);
        obs.Add(agent.hasSpike ? 1f : 0f);

        // ==========================================
        // 2. アビリティ残弾数 (2要素)
        // ==========================================
        obs.Add(agent.paranoiaCharges);
        obs.Add(agent.reconBoltCharges);

        // ==========================================
        // 3. スパイクの状態 (6要素) ★相対座標に修正
        // ==========================================
        if (spikeManager != null)
        {
            // ① 現在のステート（Enum数値）
            obs.Add((float)spikeManager.currentState);

            // ② スパイクが設置済み（Planted）、または解除中（Defusing）かどうかのフラグ（1f or 0f）
            bool isPlantedOrDefusing = GameManager.Instance.hasSpikePlanted();
            obs.Add(isPlantedOrDefusing ? 1f : 0f);

            // ③ 設置されたスパイクの「自分からの相対座標」
            if (isPlantedOrDefusing)
            {
                // 自分（Agent）の位置からスパイク位置へのベクトルを計算
                Vector3 relativePlantedPos = (Vector3)spikeManager.plantedGridPos - transform.position;
                obs.Add(relativePlantedPos.x);
                obs.Add(relativePlantedPos.y);
            }
            else
            {
                obs.Add(0f); // 設置されていない時は0埋め（相対座標なので0fが安全）
                obs.Add(0f);
            }

            // ④ スパイクが床に落ちているか（Dropped）のフラグと、その時の「相対座標」
            bool isDropped = spikeManager.currentState == SpikeManager.SpikeState.Dropped;
            obs.Add(isDropped ? 1f : 0f);

            if (isDropped)
            {
                Vector3 relativeDroppedPos = (Vector3)spikeManager.droppedGridPos - transform.position;
                obs.Add(relativeDroppedPos.x);
                obs.Add(relativeDroppedPos.y); // ★ここを追加して、実動作時もY座標を入れる必要があります！
            }
            else
            {
                obs.Add(0f);
                obs.Add(0f); // ★else側も0fを2つにして、要素数のズレを防ぎます。
            }
        }
        else
        {
            // SpikeManagerがない場合のダミー（計7要素分ゼロ埋め）
            obs.Add(0f); // State
            obs.Add(0f); // isPlanted
            obs.Add(0f); // relativePlanted X
            obs.Add(0f); // relativePlanted Y
            obs.Add(0f); // isDropped
            obs.Add(0f); // relativeDropped X
            obs.Add(0f); // relativeDropped Y (セーフティ用に追加)
        }

        // ==========================================
        // 4. 周囲の敵・味方の情報 (常に最大4人分に固定して送信: 20要素)
        // ==========================================
        int maxOtherAgents = 4;
        int addedCount = 0;

        if (GameManager.Instance != null)
        {
            List<Agent> allAgents = GameManager.Instance.AllAgents;
            foreach (Agent other in allAgents)
            {
                if (other == null || other == agent) continue;
                if (addedCount >= maxOtherAgents) break;

                // ① 敵味方関係 (味方: 1f, 敵: -1f)
                float relation = (other.isEnemy == agent.isEnemy) ? 1f : -1f;
                obs.Add(relation);

                // ② 自分から見た相手への相対座標
                Vector3 relativePos = other.transform.position - transform.position;
                obs.Add(relativePos.x);
                obs.Add(relativePos.y);

                // ③ 相手のステータス (HP, 生存フラグ)
                obs.Add(other.hp / 100f);
                obs.Add(other.isDead ? 1f : 0f);

                addedCount++;
            }
        }

        // ── 不足分を0埋め ──
        int remainingSlots = maxOtherAgents - addedCount;
        for (int i = 0; i < remainingSlots; i++)
        {
            obs.Add(0f);
            obs.Add(0f);
            obs.Add(0f);
            obs.Add(0f);
            obs.Add(0f);
        }

        return obs.ToArray();
    }
}