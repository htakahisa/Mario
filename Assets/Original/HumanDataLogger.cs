using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

public class HumanDataLogger : MonoBehaviour
{
    [Header("Dependencies")]
    private SpikeManager spikeManager;
    private MapManager mapManager;

    [Header("Settings")]
    [SerializeField] private string fileName = "universal_play_data.csv";

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
        if (mapManager == null) mapManager = UnityEngine.Object.FindFirstObjectByType<MapManager>();

        csvContent.AppendLine(CreateCsvHeader());

        if (TestManager.Instance != null)
        {
            TestManager.Instance.OnActionExecuted += OnAgentActionCallback;
            isRecording = true;
            Debug.Log($"[Logger] ロギング開始: {filePath}");
        }
        else
        {
            Debug.LogError("[Logger] TestManager インスタンスが見つかりません。");
        }
    }

    private void OnDestroy()
    {
        if (TestManager.Instance != null)
            TestManager.Instance.OnActionExecuted -= OnAgentActionCallback;
    }

    // ボットでも人間でもアクション実行時に呼ばれる
    private void OnAgentActionCallback(Agent agent, string actionType, Vector3Int gridPos, Vector3Int targetGridPos)
    {
        if (!isRecording) return;
        tiggeredActions.Add(new ActionSnapshot
        {
            agent = agent,
            actionType = actionType,
            gridPos = gridPos,
            targetGridPos = targetGridPos
        });
    }

    void FixedUpdate()
    {
        if (!isRecording) return;

        if (tiggeredActions.Count > 0)
        {
            foreach (var action in tiggeredActions)
            {
                if (action.agent == null || action.agent.isDead) continue;

                float[] obs = GetCurrentObservations(action.agent);
                List<string> row = new List<string>();
                foreach (float v in obs) row.Add(v.ToString("F4"));

                row.Add(action.actionType);
                row.Add(action.gridPos.x.ToString());
                row.Add(action.gridPos.y.ToString());
                row.Add(action.targetGridPos.x.ToString());
                row.Add(action.targetGridPos.y.ToString());

                csvContent.AppendLine(string.Join(",", row));
            }
            tiggeredActions.Clear();
        }
        else
        {
            // 10フレームに1回、生存エージェントの Stay をサンプリング
            if (Time.frameCount % 10 == 0 && GameManager.Instance != null)
            {
                foreach (Agent a in GameManager.Instance.AllAgents)
                {
                    if (a == null || a.isDead) continue;

                    float[] obs = GetCurrentObservations(a);
                    List<string> row = new List<string>();
                    foreach (float v in obs) row.Add(v.ToString("F4"));

                    row.Add("Stay");
                    row.Add("0"); row.Add("0");
                    row.Add("0"); row.Add("0");

                    csvContent.AppendLine(string.Join(",", row));
                }
            }
        }
    }

    private string GetUniqueFilePath(string baseName)
    {
        string dir = Path.Combine(Application.dataPath, "../");
        string name = Path.GetFileNameWithoutExtension(baseName);
        string ext = Path.GetExtension(baseName);
        string path = Path.Combine(dir, baseName);
        int counter = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(dir, $"{name}_{counter}{ext}");
            counter++;
        }
        return path;
    }

    private void OnApplicationQuit()
    {
        if (!isRecording) return;
        isRecording = false;
        try
        {
            File.WriteAllText(filePath, csvContent.ToString());
            Debug.Log($"[Logger] 保存完了: {filePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Logger] 保存失敗: {e.Message}");
        }
    }

    private string CreateCsvHeader()
    {
        var h = new List<string>();

        // 自分自身情報 (7)
        h.AddRange(new[] {
            "obs_self_x", "obs_self_y", "obs_self_hp", "obs_self_isDead",
            "obs_self_hasSpike",
            "obs_self_isPlanting",     // ★ アタッカー: スパイク設置中フラグ
            "obs_self_isDefusing",
            "obs_self_inPlantZone"     // ★ アタッカー: プラントゾーン内フラグ
        });
        // アビリティ (2)
        h.AddRange(new[] { "obs_paranoia_charges", "obs_recon_charges" });
        // スパイク情報 (7)
        h.AddRange(new[] {
            "obs_spike_state", "obs_spike_isPlanted",
            "obs_spike_relPlanted_x",  "obs_spike_relPlanted_y",
            "obs_spike_isDropped",
            "obs_spike_relDropped_x",  "obs_spike_relDropped_y"
        });
        // 他エージェント4人分 (20)
        for (int i = 0; i < 4; i++)
            h.AddRange(new[] {
                $"obs_agent{i}_relation", $"obs_agent{i}_rel_x",
                $"obs_agent{i}_rel_y",   $"obs_agent{i}_hp",
                $"obs_agent{i}_isDead"
            });
        // 壁マップ 5×5=25
        for (int i = 0; i < 25; i++)
            h.Add($"obs_wall_{i}");

        // アクション情報
        h.AddRange(new[] {
            "act_type", "act_grid_x", "act_grid_y",
            "act_ability_target_x", "act_ability_target_y"
        });

        return string.Join(",", h);
        // 合計次元: 7+2+7+20+25 = 61
    }

    // 周囲5×5の壁マップ (0=通れる, 1=壁/マップ外)
    // 並び順: dy=-2..+2, dx=-2..+2 の行優先（左上→右下）
    private float[] GetLocalWallMap(Agent selfAgent, int size = 5)
    {
        int half = size / 2;
        float[] map = new float[size * size];
        int index = 0;
        Vector2Int center = (Vector2Int)selfAgent.gridPosition; // Vector2Int

        for (int dy = -half; dy <= half; dy++)
        {
            for (int dx = -half; dx <= half; dx++)
            {
                var cell = new Vector3Int(center.x + dx, center.y + dy, 0);
                bool isWall = (mapManager != null) && !mapManager.IsWalkable(cell);
                map[index++] = isWall ? 1f : 0f;
            }
        }
        return map;
    }

    private float[] GetCurrentObservations(Agent selfAgent)
    {
        var obs = new List<float>();

        // 1. 自分自身情報 (7要素)
        obs.Add(selfAgent.gridPosition.x);
        obs.Add(selfAgent.gridPosition.y);
        obs.Add(selfAgent.hp / 100f);
        obs.Add(selfAgent.isDead ? 1f : 0f);
        obs.Add(selfAgent.hasSpike ? 1f : 0f);

        // ★ プラント中フラグ（アタッカー専用）
        bool isPlanting = spikeManager != null
            && spikeManager.currentState == SpikeManager.SpikeState.Planting
            && selfAgent.hasSpike;
        obs.Add(isPlanting ? 1f : 0f);

        bool isDefusing = spikeManager != null && spikeManager.defusingAgent == selfAgent;
        obs.Add(isDefusing ? 1f : 0f);

        // ★ プラントゾーン内フラグ（アタッカー専用）
        bool inPlantZone = mapManager != null
            && mapManager.IsPlantableArea(new Vector3Int(selfAgent.gridPosition.x, selfAgent.gridPosition.y, 0));
        obs.Add(inPlantZone ? 1f : 0f);

        // 2. アビリティ情報 (2要素)
        obs.Add(selfAgent.paranoiaCharges);
        obs.Add(selfAgent.reconBoltCharges);

        // 3. スパイク情報 (7要素)
        if (spikeManager != null)
        {
            obs.Add((float)spikeManager.currentState);

            bool planted = GameManager.Instance.hasSpikePlanted();
            obs.Add(planted ? 1f : 0f);

            if (planted)
            {
                Vector2Int relPlanted = (Vector2Int)(spikeManager.plantedGridPos - selfAgent.gridPosition);
                obs.Add(relPlanted.x);
                obs.Add(relPlanted.y);
            }
            else { obs.Add(0f); obs.Add(0f); }

            bool isDropped = spikeManager.currentState == SpikeManager.SpikeState.Dropped;
            obs.Add(isDropped ? 1f : 0f);

            if (isDropped)
            {
                Vector2Int relDropped = (Vector2Int)(spikeManager.droppedGridPos - selfAgent.gridPosition);
                obs.Add(relDropped.x);
                obs.Add(relDropped.y);
            }
            else { obs.Add(0f); obs.Add(0f); }
        }
        else
        {
            obs.AddRange(new float[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f });
        }

        // 4. 他エージェント4人分 (20要素)
        int maxOthers = 4;
        int addedCount = 0;

        if (GameManager.Instance != null)
        {
            foreach (Agent other in GameManager.Instance.AllAgents)
            {
                if (other == null || other == selfAgent) continue;
                if (addedCount >= maxOthers) break;

                float relation = (other.isEnemy == selfAgent.isEnemy) ? 1f : -1f;
                Vector2Int relPos = (Vector2Int)(other.gridPosition - selfAgent.gridPosition);

                obs.Add(relation);
                obs.Add(relPos.x);
                obs.Add(relPos.y);
                obs.Add(other.hp / 100f);
                obs.Add(other.isDead ? 1f : 0f);

                addedCount++;
            }
        }

        for (int i = 0; i < maxOthers - addedCount; i++)
            obs.AddRange(new float[] { 0f, 0f, 0f, 0f, 0f });

        // 5. 壁マップ (25要素)
        obs.AddRange(GetLocalWallMap(selfAgent, 5));

        // 合計: 7 + 2 + 7 + 20 + 25 = 61次元
        return obs.ToArray();
    }
}