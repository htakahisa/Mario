using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class MapManager : MonoBehaviour
{
    public static MapManager Instance;
    [Header("--- Plantable Area Settings ---")]
    public List<TileBase> plantableTiles; // ★ここに「Aサイト」「Bサイト」として使いたい床タイルのアセットを登録する

    [SerializeField] private Tilemap wallTilemap;  // インスペクターからWall_Tilemapを割り当てる
    [SerializeField] private Tilemap floorTilemap; // インスペクターからFloor_Tilemapを割り当てる

    // マップの範囲（今回のマップの大きさに合わせて調整してください）
    public int minX = -30;
    public int maxX = 30;
    public int minY = -20;
    public int maxY = 20;

    // ゲーム内で使う盤面データ（Grid上の座標をキーにして、そこが壁かどうかを保持する）
    // 本格的には2次元配列や専用の構造体が良いですが、まずは簡単な仕組みから
    private System.Collections.Generic.HashSet<Vector3Int> wallPositions = new System.Collections.Generic.HashSet<Vector3Int>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        ScanMap();
    }

    void ScanMap()
    {
        // 指定した範囲のマスをすべてチェック
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector3Int tilePos = new Vector3Int(x, y, 0);

                // Wall_Tilemapにタイルがあるか確認
                if (wallTilemap.HasTile(tilePos))
                {
                    wallPositions.Add(tilePos);
                }
            }
        }

        Debug.Log($"マップのスキャン完了：壁の数 {wallPositions.Count} 個");
    }

    // ★【新設】指定されたマスがスパイク設置可能エリア（サイト内）かどうかを判定する関数
    public bool IsPlantableArea(Vector3Int gridPos)
    {
        if (floorTilemap == null) return false;

        // 指定されたマスのタイルを取得
        TileBase currentTile = floorTilemap.GetTile(gridPos);

        if (currentTile == null) return false;

        // ★登録された「設置可能タイルリスト」の中に、今踏んでいるタイルが含まれているかチェック
        if (plantableTiles != null && plantableTiles.Contains(currentTile))
        {
            return true; // サイト内なので設置OK！
        }

        return false; // サイト外なので設置NG
    }

    // ===================================================================
    // ★【新設】ルート探索（FindPath）専用の、絶対に無限ループしない安全な判定
    // ===================================================================
    public bool IsWalkableForPathfinding(Vector3Int targetGridPos, Agent searchingAgent)
    {
        // 1. そこが壁のタイルなら絶対に歩けない
        if (wallTilemap.HasTile(targetGridPos)) return false;

        // 2. 全キャラをチェック
        Agent[] allAgents = Object.FindObjectsByType<Agent>(FindObjectsSortMode.None);
        foreach (Agent agent in allAgents)
        {
            if (agent != null && !agent.isDead && agent != searchingAgent && !(agent.isEnemy != searchingAgent.isEnemy && !agent.isRevealed)) // 自分自身は絶対に除外！
            {
                // ★完全に立ち止まっている敵・味方のマスだけを「障害物」として扱う
                if (!agent.isMoving && agent.gridPosition == targetGridPos)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public bool IsThereOnlyTargetPos(Vector3Int targetGridPos, Agent searchingAgent)
    {
        // 1. そこが壁のタイルなら絶対に歩けない
        if (wallTilemap.HasTile(targetGridPos)) return false;

        // 2. 全キャラをチェック
        Agent[] allAgents = Object.FindObjectsByType<Agent>(FindObjectsSortMode.None);
        foreach (Agent agent in allAgents)
        {
            if (agent != null && !agent.isDead && agent != searchingAgent && !(agent.isEnemy != searchingAgent.isEnemy && !agent.isRevealed)) // 自分自身は絶対に除外！
            {
                // ★完全に立ち止まっている敵・味方のマスだけを「障害物」として扱う
                if (agent.nextGridPosition == targetGridPos)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public bool IsWall(Vector3Int targetGridPos)
    {
        // 1. そこが壁のタイルなら絶対に歩けない
        return (wallTilemap.HasTile(targetGridPos));

    }

    // 通常の物理的な移動可能チェック（既存のもの、または以前のシンプルな形に戻す）
    public bool IsWalkable(Vector3Int targetGridPos)
    {
        if (wallTilemap.HasTile(targetGridPos)) return false;

        Agent[] allAgents = Object.FindObjectsByType<Agent>(FindObjectsSortMode.None);
        foreach (Agent agent in allAgents)
        {
            if (agent != null && !agent.isDead)
            {
                if (agent.gridPosition == targetGridPos) return false;
            }
        }
        return true;
    }

    // 指定した2つの座標の間に「壁」がないか（射線が通るか）を判定する関数
    public bool HasLineOfSight(Vector3Int start, Vector3Int end)
    {
        int x = start.x;
        int y = start.y;

        int dx = Mathf.Abs(end.x - start.x);
        int dy = Mathf.Abs(end.y - start.y);

        int sx = start.x < end.x ? 1 : -1;
        int sy = start.y < end.y ? 1 : -1;

        int err = dx - dy;

        while (true)
        {
            // スタート位置以外で、現在の座標に壁があるかチェック
            if (new Vector3Int(x, y, 0) != start)
            {
                // wallPositionsは前回作った壁のHashSet
                if (wallPositions.Contains(new Vector3Int(x, y, 0)))
                {
                    return false; // 壁に当たったので射線は通らない
                }
            }

            // ゴール（敵のマス）に到達したら、射線が通っている
            if (x == end.x && y == end.y)
            {
                return true;
            }

            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y += sy;
            }
        }
    }
}