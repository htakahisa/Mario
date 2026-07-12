using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TimeCapsuleManager : MonoBehaviour
{
    public static TimeCapsuleManager Instance { get; private set; }

    [Header("--- 参照コンポーネント ---")]
    [SerializeField] private TestManager testManager;
    [SerializeField] private SpikeManager spikeManager;

    private Dictionary<int, BoardTickSnapshot> timeline = new Dictionary<int, BoardTickSnapshot>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (testManager == null) testManager = Object.FindFirstObjectByType<TestManager>();
        if (spikeManager == null) spikeManager = Object.FindFirstObjectByType<SpikeManager>();
    }

    public void SaveCurrentTickSnapshot(int tickCount)
    {
        BoardTickSnapshot snapshot = new BoardTickSnapshot();
        snapshot.tickCount = tickCount;

        if (testManager != null) snapshot.managerState = testManager.SaveState();

        List<AgentStateData> agentDataList = new List<AgentStateData>();
        foreach (Agent agent in GameManager.Instance.AllAgents)
        {
            if (agent != null) agentDataList.Add(agent.SaveState());
        }
        snapshot.agents = agentDataList.ToArray();

        // 3. スパイクの状態を保存
        // ※もしSpikeManager全体を保存する「SaveState()」がすでにあるなら、snapshot.spikeStateの型をそれに合わせて上書きするのが理想です
        if (spikeManager != null)
        {
            snapshot.spikeState = spikeManager.currentState;
            snapshot.plantTickTimer = spikeManager.plantTickTimer;
        }
        if (GameManager.Instance)
        {
            snapshot.isRoundOver = GameManager.Instance.isRoundOver;
        }


        ReconBoltProjectile[] allRecons = Object.FindObjectsByType<ReconBoltProjectile>(FindObjectsSortMode.None);
        List<ReconBoltStateData> reconDataList = new List<ReconBoltStateData>();
        foreach (var recon in allRecons)
        {
            if (recon != null) reconDataList.Add(recon.SaveState());
        }
        snapshot.reconBolts = reconDataList.ToArray();

        ParanoiaProjectile[] allParas = Object.FindObjectsByType<ParanoiaProjectile>(FindObjectsSortMode.None);
        List<ParanoiaStateData> paraDataList = new List<ParanoiaStateData>();
        foreach (var para in allParas)
        {
            if (para != null) paraDataList.Add(para.SaveState());
        }
        snapshot.paranoias = paraDataList.ToArray();

        if (timeline.ContainsKey(tickCount)) timeline[tickCount] = snapshot;
        else timeline.Add(tickCount, snapshot);
    }

    public bool LoadTickSnapshot(int targetTick)
    {
        if (!timeline.ContainsKey(targetTick))
        {
            Debug.LogWarning($"<color=red>【タイムカプセル】</color> Tick {targetTick} の記録が見つからないため巻き戻せません。");
            return false;
        }

        Debug.Log($"<color=green>【タイムカプセル】</color> 時空を Tick {targetTick} へ巻き戻します。");
        BoardTickSnapshot targetSnapshot = timeline[targetTick];

        if (GridTickScheduler.Instance != null)
        {
            GridTickScheduler.Instance.SyncTickCount(targetTick);
        }

        if (testManager != null) testManager.LoadState(targetSnapshot.managerState);

        foreach (Agent agent in GameManager.Instance.AllAgents)
        {
            if (agent == null) continue;

            AgentStateData foundData = System.Array.Find(targetSnapshot.agents, data => data.agentName == agent.agentName);
            if (foundData.agentName != null)
            {
                agent.LoadState(foundData);
            }
        }

        if (spikeManager != null)
        {
            // 直接のEnum代入から、見た目も同期するLoadState関数呼び出しに切り替え
            spikeManager.LoadState(targetSnapshot);
        }

        if (GameManager.Instance)
        {
            GameManager.Instance.isRoundOver = targetSnapshot.isRoundOver;
        }

        ReconBoltProjectile[] allRecons = Object.FindObjectsByType<ReconBoltProjectile>(FindObjectsSortMode.None);
        for (int i = 0; i < allRecons.Length; i++)
        {
            if (allRecons[i] != null && i < targetSnapshot.reconBolts.Length)
            {
                allRecons[i].LoadState(targetSnapshot.reconBolts[i]);
            }
        }

        List<int> futureTicks = new List<int>();
        foreach (int tick in timeline.Keys)
        {
            if (tick > targetTick) futureTicks.Add(tick);
        }
        foreach (int tick in futureTicks)
        {
            timeline.Remove(tick);
        }

        return true;
    }
}