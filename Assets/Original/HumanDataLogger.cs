using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

public class HumanDataLogger : MonoBehaviour
{
    [Header("Dependencies")]
    private SpikeManager spikeManager;
    private MapManager mapManager; // 🚨 壁判定のために追加

    [Header("Settings")]
    [SerializeField] private string fileName = "universal_play_data.csv";
    [SerializeField] private int localMapRadius = 5; // 🚨 エージェント周囲何マス分の地形を送るか (5の場合11x11=121マス)

    private string filePath;
    private StringBuilder csvContent = new StringBuilder();
    private bool isRecording = false;

    private struct ActionSnapshot
    {
        public Agent agent;
        public string actionType;
        public Vector3Int gridPos;
        public Vector3Int targetGridPos;
    }

    private List<ActionSnapshot> tiggeredActions = new List<ActionSnapshot>();

    void Start()
    {
        filePath = GetUniqueFilePath(fileName);

        if (spikeManager == null) spikeManager = UnityEngine.Object.FindFirstObjectByType<SpikeManager>();
        if (mapManager == null) mapManager = UnityEngine.Object.FindFirstObjectByType<MapManager>(); // 🚨 追加

        // CSVヘッダー作成
        string header = CreateCsvHeader();
        csvContent.AppendLine(header);

        if (TestManager.Instance != null)
        {
            TestManager.Instance.OnActionExecuted += OnAgentActionCallback;
            isRecording = true;
            Debug.Log($"[Logger] TestManager結線完了。内部アクションの全自動ロギングを開始しました: {filePath}");
        }
        else
        {
            Debug.LogError("[Logger] TestManager インスタンスが見つかりません。ログを開始できません。");
        }
    }

    private void OnDestroy()
    {
        if (TestManager.Instance != null)
        {
            TestManager.Instance.OnActionExecuted -= OnAgentActionCallback;
        }
    }

    private void OnAgentActionCallback(Agent agent, string actionType, Vector3Int gridPos, Vector3Int targetGridPos)
    {
        if (!isRecording) return;

        ActionSnapshot snap = new ActionSnapshot
        {
            agent = agent,
            actionType = actionType,
            gridPos = gridPos,
            targetGridPos = targetGridPos
        };
        tiggeredActions.Add(snap);
    }

    void FixedUpdate()
    {
        if (!isRecording) return;

        if (tiggeredActions.Count > 0)
        {
            foreach (var action in tiggeredActions)
            {
                if (action.agent == null || action.agent.isDead) continue;

                float[] currentObs = GetCurrentObservations(action.agent);

                List<string> rowData = new List<string>();
                foreach (float val in currentObs) rowData.Add(val.ToString("F4"));

                rowData.Add(action.actionType);
                rowData.Add(action.gridPos.x.ToString());
                rowData.Add(action.gridPos.y.ToString());
                rowData.Add(action.targetGridPos.x.ToString());
                rowData.Add(action.targetGridPos.y.ToString());

                csvContent.AppendLine(string.Join(",", rowData));
            }
            tiggeredActions.Clear();
        }
        else
        {
            if (Time.frameCount % 10 == 0 && GameManager.Instance != null)
            {
                foreach (Agent a in GameManager.Instance.AllAgents)
                {
                    if (a == null || a.isDead) continue;

                    float[] currentObs = GetCurrentObservations(a);
                    List<string> rowData = new List<string>();
                    foreach (float val in currentObs) rowData.Add(val.ToString("F4"));

                    rowData.Add("Stay");
                    rowData.Add("0"); rowData.Add("0");
                    rowData.Add("0"); rowData.Add("0");

                    csvContent.AppendLine(string.Join(",", rowData));
                }
            }
        }
    }

    private string GetUniqueFilePath(string baseName)
    {
        string directory = Path.Combine(Application.dataPath, "../");
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(baseName);
        string extension = Path.GetExtension(baseName);
        string fullPath = Path.Combine(directory, baseName);
        int counter = 1;
        while (File.Exists(fullPath))
        {
            fullPath = Path.Combine(directory, $"{nameWithoutExtension}_{counter}{extension}");
            counter++;
        }
        return fullPath;
    }

    private void OnApplicationQuit()
    {
        if (!isRecording) return;
        isRecording = false;
        try
        {
            File.WriteAllText(filePath, csvContent.ToString());
            Debug.Log($"[Logger] 内部マネージャー同期データを保存しました！ 保存先: {filePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Logger] 保存に失敗しました: {e.Message}");
        }
    }

    private string CreateCsvHeader()
    {
        List<string> headers = new List<string>();
        headers.AddRange(new[] { "obs_self_x", "obs_self_y", "obs_self_hp", "obs_self_isDead", "obs_self_hasSpike" });
        headers.AddRange(new[] { "obs_paranoia_charges", "obs_recon_charges" });
        headers.AddRange(new[] { "obs_spike_state", "obs_spike_isPlanted", "obs_spike_relPlanted_x", "obs_spike_relPlanted_y", "obs_spike_isDropped", "obs_spike_relDropped_x", "obs_spike_relDropped_y" });
        for (int i = 0; i < 4; i++)
        {
            headers.AddRange(new[] { $"obs_agent{i}_relation", $"obs_agent{i}_rel_x", $"obs_agent{i}_rel_y", $"obs_agent{i}_hp", $"obs_agent{i}_isDead" });
        }

        // 🚨【拡張】周辺の壁状況ヘッダーを追加
        for (int x = -localMapRadius; x <= localMapRadius; x++)
        {
            for (int y = -localMapRadius; y <= localMapRadius; y++)
            {
                headers.Add($"obs_wall_offset_x{x}_y{y}");
            }
        }

        headers.AddRange(new[] { "act_type", "act_grid_x", "act_grid_y", "act_ability_target_x", "act_ability_target_y" });
        return string.Join(",", headers);
    }

    private float[] GetCurrentObservations(Agent selfAgent)
    {
        List<float> obs = new List<float>();

        obs.Add(selfAgent.transform.position.x);
        obs.Add(selfAgent.transform.position.y);
        obs.Add(selfAgent.hp / 100f);
        obs.Add(selfAgent.isDead ? 1f : 0f);
        obs.Add(selfAgent.hasSpike ? 1f : 0f);

        obs.Add(selfAgent.paranoiaCharges);
        obs.Add(selfAgent.reconBoltCharges);

        if (spikeManager != null)
        {
            obs.Add((float)spikeManager.currentState);
            bool isPlantedOrDefusing = GameManager.Instance.hasSpikePlanted();
            obs.Add(isPlantedOrDefusing ? 1f : 0f);

            if (isPlantedOrDefusing)
            {
                Vector3 relativePlantedPos = (Vector3)spikeManager.plantedGridPos - selfAgent.transform.position;
                obs.Add(relativePlantedPos.x);
                obs.Add(relativePlantedPos.y);
            }
            else
            {
                obs.Add(0f); obs.Add(0f);
            }

            bool isDropped = spikeManager.currentState == SpikeManager.SpikeState.Dropped;
            obs.Add(isDropped ? 1f : 0f);

            if (isDropped)
            {
                Vector3 relativeDroppedPos = (Vector3)spikeManager.droppedGridPos - selfAgent.transform.position;
                obs.Add(relativeDroppedPos.x);
                obs.Add(relativeDroppedPos.y);
            }
            else
            {
                obs.Add(0f); obs.Add(0f);
            }
        }
        else
        {
            obs.AddRange(new float[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f });
        }

        int maxOtherAgents = 4;
        int addedCount = 0;

        if (GameManager.Instance != null)
        {
            List<Agent> allAgents = GameManager.Instance.AllAgents;
            foreach (Agent other in allAgents)
            {
                if (other == null || other == selfAgent) continue;
                if (addedCount >= maxOtherAgents) break;

                float relation = (other.isEnemy == selfAgent.isEnemy) ? 1f : -1f;
                obs.Add(relation);

                Vector3 relativePos = other.transform.position - selfAgent.transform.position;
                obs.Add(relativePos.x);
                obs.Add(relativePos.y);

                obs.Add(other.hp / 100f);
                obs.Add(other.isDead ? 1f : 0f);

                addedCount++;
            }
        }

        int remainingSlots = maxOtherAgents - addedCount;
        for (int i = 0; i < remainingSlots; i++)
        {
            obs.AddRange(new float[] { 0f, 0f, 0f, 0f, 0f });
        }

        // 🚨【拡張】周辺の壁情報を末尾に追加
        // selfAgent.gridPosition が Vector3Int であると想定しています
        Vector3Int centerPos = selfAgent.gridPosition;

        for (int x = -localMapRadius; x <= localMapRadius; x++)
        {
            for (int y = -localMapRadius; y <= localMapRadius; y++)
            {
                if (mapManager != null)
                {
                    Vector3Int targetPos = new Vector3Int(centerPos.x + x, centerPos.y + y, 0);
                    // 前述のロジック通り、IsWalkable（歩ける床）が False の場所を 壁(1.0f) と判定する
                    bool isWall = !mapManager.IsWalkable(targetPos);
                    obs.Add(isWall ? 1f : 0f);
                }
                else
                {
                    // MapManagerが外れている場合の安全策として床(0)で埋める
                    obs.Add(0f);
                }
            }
        }

        return obs.ToArray();
    }
}