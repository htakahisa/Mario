using System;
using UnityEngine;

[Serializable]
public struct BoardTickSnapshot
{
    public bool isRoundOver;
    public int tickCount; // 何番目のTickか
    public AgentStateData[] agents; // 全エージェントの状態
    public ReconBoltStateData[] reconBolts;
    public ParanoiaStateData[] paranoias;
    public SpikeManager.SpikeState spikeState; // スパイクの状態（必要に応じて）
    public int plantTickTimer;

    // ★【追加】TimeCapsuleManagerがアクセスできるようにマネージャーの状態を追加
    public TestManagerStateData managerState;
}

[System.Serializable]
public struct ReconBoltStateData
{
    public bool isActive;
    public Vector3 position;
    public Vector3 direction;
    public int remainingTicks;
    public int effectRemainingTicks;
    public bool isInitialized;
    public bool isStuck;
    public Agent owner;
}

[System.Serializable]
public struct ParanoiaStateData
{
    public bool isActive;
    public Vector3 position;
    public Vector3 direction;
    public int remainingTicks;
    public bool isInitialized;
    public bool isStuck;
    public Agent owner;
    public Vector3Int lastCheckedGrid;
}


[System.Serializable]
public struct BulletTracerStateData
{
    public bool isActive;
    public Vector3 startPos;
    public Vector3 endPos;
    public int remainingTicks;
}

[System.Serializable]
public struct TestManagerStateData
{
    public bool isPlayerAttacker;
    public bool isDuelOccurred;
    public bool isAimingAbility;
    public string selectedAbilityName;

    // ★【修正】TestManager側の変数（Agent型）と型を完全に一致させ、
    // タイムカプセルがそのまま参照を保存・復元できるように public に変更します
    public Agent selectedAgent;
}