using System;
using System.Collections.Generic;
using UnityEngine;

// ★Unityのエディタ上で中身を見たり、シリアライズ（データ化）できるようにする
[Serializable]
public struct AgentStateData
{
    public string agentName;
    public Vector3Int gridPosition;
    public int hp;
    public bool isDead;
    public bool hasSpike;
    public int paranoiaCharges;
    public int reconBoltCharges;
    public Agent.BotRole myRole;
    public bool isActive; // ★追加：GameObjectの活性状態
    public bool isBlinded;
    public int blindTickTimer;
    public Vector3Int nextGridPosition; // 🔥 追加：1歩先の目的地
    public List<Vector3Int> currentPath; // 🔥 追加：残りの移動経路リスト
    public Vector3Int lastAssignedTarget; // 🔥 追加：重複防止用ターゲット
    public bool isMoving;               // 🔥 追加：移動中フラグ
    public bool isActionLocked;

    // 補足：構造体の中にコンストラクタ（初期化用の関数）を作っておくと便利です
    public AgentStateData(Agent agent)
    {
        this.agentName = agent.agentName;
        this.gridPosition = agent.gridPosition;
        this.hp = agent.hp;
        this.isDead = agent.isDead;
        this.hasSpike = agent.hasSpike;
        this.paranoiaCharges = agent.paranoiaCharges;
        this.reconBoltCharges = agent.reconBoltCharges;
        this.myRole = agent.myRole;
        this.isActive = agent.isActive;
        this.isBlinded = agent.isBlinded;
        this.blindTickTimer = agent.blindTickTimer;
        this.nextGridPosition = agent.nextGridPosition;
        this.currentPath = agent.currentPath;
        this.lastAssignedTarget = agent.lastAssignedTarget;
        this.isMoving = agent.isMoving;
        this.isActionLocked = agent.isActionLocked;
    }
}

[Serializable]
public struct ProjectileStateData
{
    public bool isActive;
    public Vector3 position;
    public Vector3 targetWorldPos;
    public int remainingTicks;
    public Agent Owner;
}