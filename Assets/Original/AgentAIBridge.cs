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
        mapManager = Object.FindFirstObjectByType<MapManager>();
        spikeManager = Object.FindFirstObjectByType<SpikeManager>();
    }

    /// <summary>
    /// ★AIに渡す「現在の世界の状態」を数値の配列としてパックする
    /// </summary>
    public float[] CollectObservations()
    {
        List<float> obs = new List<float>();

        // 1. 自分の情報 (5要素)
        obs.Add(transform.position.x);
        obs.Add(transform.position.y);
        obs.Add(agent.hp / 100f); // 0〜1に正規化
        obs.Add(agent.isDead ? 1f : 0f);
        obs.Add(agent.hasSpike ? 1f : 0f);

        // 2. アビリティ残弾数 (2要素)
        // ※前回巻き戻せるようにした変数です
        obs.Add(agent.paranoiaCharges);
        obs.Add(agent.reconBoltCharges);

        // 3. スパイクの状態 (3要素)
        if (spikeManager != null)
        {
            obs.Add((float)spikeManager.currentState); // Enumを数値化
            obs.Add(spikeManager.transform.position.x);
            obs.Add(spikeManager.transform.position.y);
        }
        else
        {
            obs.Add(0f); obs.Add(0f); obs.Add(0f);
        }

        // 4. 周囲の敵・味方の情報 (簡易版)
        // 本来はループで近くのエージェントの座標などを入れます

        return obs.ToArray();
    }
}