using System;

// ==========================================
// 1. UnityからPythonへ送るデータ構造（状態と報酬）
// ==========================================
[Serializable]
public class UnityToPythonState
{
    public int tick;
    public bool isTerminal; // エピソードが終了したか（全員死亡、またはスパイク爆破/解除など）
    public float reward;    // このTickで発生した報酬の合計値
    public PythonAgentObservation[] agents;
    public int spikeState;  // スパイクの状態 (Enumの数値)
}

[Serializable]
public class PythonAgentObservation
{
    public string name;
    public float posX;
    public float posY;
    public int hp;
    public bool isDead;
    public bool hasSpike;
    public int paranoiaCount;
    public int reconBoltCount;
}

// ==========================================
// 2. PythonからUnityへ届くデータ構造（行動命令）
// ==========================================
[Serializable]
public class PythonToUnityActionList
{
    public PythonAgentAction[] actions;
}

[Serializable]
public class PythonAgentAction
{
    public string name;      // 命令対象のエージェント名
    public int moveDirection; // 移動方向 (0:静止, 1:上, 2:下, 3:左, 4:右, 5:右上... など数値で管理)
    public bool useParanoia;  // パラノイアを使うか
    public bool useRecon;     // リコンボルトを使うか
    public int targetX;     // アビリティを撃つ場合の目標X座標
    public int targetY;     // アビリティを撃つ場合の目標Y座標
}