using Newtonsoft.Json;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using Formatting = Newtonsoft.Json.Formatting;

public class GameStateSerializer : MonoBehaviour
{
    public static GameStateSerializer Instance { get; private set; }

    [Header("--- AI Training Settings ---")]
    [SerializeField] private string experimentName = "fps_ai_v2_perfect";
    [SerializeField] private bool startFromScratch = true; // 🟩 インスペクターで切り替えるスイッチ！

    [Header("--- Tuning ---")]
    [SerializeField] private int localMapRadius = 5; // エージェント周囲何マス分の地形を送るか

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// 現在の全ゲーム状況を、Python向けに最適化したJSON文字列にシリアライズする
    /// </summary>
    public string SerializeCurrentGameState()
    {
        if (GridTickScheduler.Instance == null) return "{}";

        var spikeManager = SpikeManager.Instance;
        var mapManager = MapManager.Instance;
        var gameManager = GameManager.Instance;

        var allAgents = Object.FindObjectsByType<Agent>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        var root = new RootStateData
        {
            current_tick = GridTickScheduler.Instance.GetCurrentTick(),
            game_state = new GameStateData
            {
                is_duel_occurred = TestManager.Instance.isDuelOccurred,
                spike_state = spikeManager != null ? spikeManager.currentState.ToString() : "None",
                bspike_planted = gameManager.hasSpikePlanted(),
                spike_grid_pos = spikeManager != null ? new Vector2IntData(spikeManager.GetSpikePos().x, spikeManager.GetSpikePos().y) : new Vector2IntData(0, 0),
                is_round_end = GameManager.Instance.isRoundOver,
                win_team = GameManager.Instance.win_team,
                is_defusing = spikeManager.currentState == SpikeManager.SpikeState.Defusing,
                is_defuse_success = spikeManager.currentState == SpikeManager.SpikeState.Defused,
            },
            my_agents = new List<MyAgentData>(),
            enemy_agents = new List<EnemyAgentData>(),

            duel_info = new DuelInfoData
            {
                in_duel = TestManager.Instance.isDuelOccurred,
                attacker_agent_id = -1,
                defender_agent_id = -1,
                win_probability_expected = 0.0f
            },

            // 🔥【初期化】ここから追加
            damage_dealt = 0,
            damage_taken = 0,
            is_kill = false,
            is_dead = false,
            is_enemy_blinded = false,
            is_enemy_revealed = false,
            enemy_has_status_blind = false,
            enemy_has_status_reveal = false,
            experiment_name = experimentName,
            start_from_scratch = startFromScratch
        };

        // ========================================================
        // 🔥【集計】1Tick内でのイベントフラグを各エージェントから回収
        // ========================================================
        // AIキャラ

        foreach (var agent in allAgents)
        {
            if (agent.isControlledByExternalAI)
            {
                continue;
            }
            var pData = new MyAgentData
            {
                id = agent.agentID,
                grid_pos = new Vector2IntData(agent.gridPosition.x, agent.gridPosition.y),
                hp = agent.hp,
                is_dead = agent.isDead,
                has_spike = agent.hasSpike,
                paranoia_charges = agent.paranoiaCharges,
                recon_bolt_charges = agent.reconBoltCharges,
                is_in_plant_zone = MapManager.Instance.IsPlantableArea(agent.gridPosition),

                // 空のリストで初期化（Nullエラー防止）
                visible_enemies_id = new List<int>(),
                local_map = new List<LocalGridData>()
            };

            // 視界にいる敵のIDを詰める（もし実装していれば）
            if (agent.FindVisibleEnemies() != null)
            {
                foreach (var enemy in agent.FindVisibleEnemies())
                {
                    pData.visible_enemies_id.Add(enemy.agentID);
                }
            }

            root.my_agents.Add(pData);
        }

        // 敵キャラ
        foreach (var agent in allAgents)
        {
            if (!agent.isControlledByExternalAI)
            {
                continue;
            }

            var eData = new EnemyAgentData
            {
                id = agent.agentID,
                grid_pos = new Vector2IntData(agent.gridPosition.x, agent.gridPosition.y),
                hp = agent.hp,
                is_dead = agent.isDead,

                // 以下は必要に応じて計算して代入（最初はデフォルト値でもOK）
                is_visible_to_player = agent.IsVisibleToPlayer,
                last_known_grid_pos = new Vector2IntData(agent.lastKnownGridPosition.x, agent.lastKnownGridPosition.y),
            };

            root.enemy_agents.Add(eData);
        }


        foreach (var agent in allAgents)
        {
            if (agent == null) continue;

            if (agent.isEnemy) // AI操作チーム (ID: 5~9)
            {
                // ※「このTickで発生した値」を取得するプロパティがAgentクラスにあると仮定しています。
                // 実際の実装に合わせて、agent.deltaDamageDealt や agent.checkTickKill() 等に書き換えてください。
                root.damage_dealt += agent.lastTickDamageDealt;
                root.damage_taken += agent.lastTickDamageTaken;

                if (agent.lastTickDidKill) root.is_kill = true;
                if (agent.lastTickDidDie) root.is_dead = true;

                if (agent.lastTickDidBlindEnemy) root.is_enemy_blinded = true;
                if (agent.lastTickDidRevealEnemy) root.is_enemy_revealed = true;
            }
            else // プレイヤー・味方チーム (ID: 0~4 = AIから見た敵)
            {
                // 状態異常のシナジーボーナス用に、現在敵がアビリティの影響下にあるかを監視
                // ※こちらも agent.isBlinded などのプロパティ名に合わせて調整してください
                if (agent.lastTickEnemyHasBlind) root.enemy_has_status_blind = true;
                if (agent.lastTickEnemyHasRevealed) root.enemy_has_status_reveal = true;
            }
        }

        string jsonResult = JsonConvert.SerializeObject(root, Formatting.None);

        // ========================================================
        // ♻️【お掃除】送信データを文字化したので、各エージェントの1Tickメモをリセット
        // ========================================================
        foreach (var agent in allAgents)
        {
            if (agent == null) continue;

            agent.lastTickDamageDealt = 0;
            agent.lastTickDamageTaken = 0;
            agent.lastTickDidKill = false;
            agent.lastTickDidDie = false;
            agent.lastTickDidBlindEnemy = false;
            agent.lastTickDidRevealEnemy = false;
        }

        // 最後にJSONを返す
        return jsonResult;
    }

    /// <summary>
    /// エージェント周囲の局所的なマップデータを切り出す
    /// </summary>
    private List<LocalGridData> GetLocalMapData(Vector3Int centerPos, MapManager mm)
    {
        var localSubMap = new List<LocalGridData>();
        if (mm == null) return localSubMap;

        for (int x = -localMapRadius; x <= localMapRadius; x++)
        {
            for (int y = -localMapRadius; y <= localMapRadius; y++)
            {
                Vector3Int targetPos = new Vector3Int(centerPos.x + x, centerPos.y + y, 0);

                // ※MapManagerに壁かどうかの判定メソッド(IsWall等)があると仮定
                bool isWall = false; // mm.IsWall(targetPos); 

                localSubMap.Add(new LocalGridData
                {
                    offset_x = x,
                    offset_y = y,
                    is_wall = isWall,
                    is_cover = isWall // 必要に応じて遮蔽物フラグを細分化
                });
            }
        }
        return localSubMap;
    }

    // ===================================================================================
    // 📦 Newtonsoft.Jsonシリアライズ用の内部データ構造定義 (DTOクラス群)
    // ===================================================================================

    [System.Serializable]
    public class Vector2IntData
    {
        public int x;
        public int y;
        public Vector2IntData(int x, int y) { this.x = x; this.y = y; }
    }

    [System.Serializable]
    public class LocalGridData
    {
        public int offset_x;
        public int offset_y;
        public bool is_wall;
        public bool is_cover;
    }

    [System.Serializable]
    public class GameStateData
    {
        public bool is_duel_occurred;
        public string spike_state;
        public bool bspike_planted;
        public Vector2IntData spike_grid_pos;
        public bool is_round_end;
        public int win_team;
        public bool is_defusing;
        public bool is_defuse_success;
    }

    [System.Serializable]
    public class MyAgentData
    {
        public int id;
        public Vector2IntData grid_pos;
        public int hp;
        public bool has_spike;
        public int paranoia_charges;
        public int recon_bolt_charges;
        public bool is_dead;
        public bool is_in_plant_zone;
        public List<int> visible_enemies_id;
        public List<LocalGridData> local_map;
    }

    [System.Serializable]
    public class EnemyAgentData
    {
        public int id;
        public Vector2IntData grid_pos;
        public int hp;
        public bool is_dead;
        public bool is_visible_to_player;
        public Vector2IntData last_known_grid_pos;
        public float distance_to_me;
        public bool has_line_of_sight;
    }

    [System.Serializable]
    public class DuelInfoData
    {
        public bool in_duel;
        public int attacker_agent_id;
        public int defender_agent_id;
        public float win_probability_expected; // 🔥ここをPython側に報酬ノイズ軽減用として渡す
    }

    [System.Serializable]
    public class RootStateData
    {
        public int current_tick;
        public GameStateData game_state;
        public List<MyAgentData> my_agents;
        public List<EnemyAgentData> enemy_agents;
        public DuelInfoData duel_info;

        // 🔥【追加】Pythonの報酬整形用に1Tick内のイベント通知フラグを仕込む
        public int damage_dealt;             // このTickでAIチームが敵に与えた総ダメージ
        public int damage_taken;             // このTickでAIチームが喰らった総ダメージ
        public bool is_kill;                 // このTickでAIチームが敵をキルしたか
        public bool is_dead;                 // このTickでAIチームの誰かが死亡したか
        public bool is_enemy_blinded;        // このTickでAIが敵をブラインドにしたか
        public bool is_enemy_revealed;       // このTickでAIが敵をリヴィールしたか
        public bool enemy_has_status_blind;  // 現在、敵チームにブラインド中のキャラがいるか
        public bool enemy_has_status_reveal; // 現在、敵チームにリヴィール中のキャラがいるか
                                             // 🔥【追加】Unityのインスペクターの設定をPythonへ同梱するためのプロパティ
        public string experiment_name;
        public bool start_from_scratch;
    }

    [System.Serializable]
    public class PythonActionData
    {
        public string action_type;    // "Move", "UseAbility", "Stay" など
        public int target_agent_id;   // 命令対象のエージェントの InstanceID
        public int grid_x;            // 移動先のX座標
        public int grid_y;            // 移動先のY座標
        public string ability_name;   // アビリティを使う場合の名前 ("Paranoia" など)
        public int ability_target_x;
        public int ability_target_y;
    }
}