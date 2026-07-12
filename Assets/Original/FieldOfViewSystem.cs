using UnityEngine;
using System.Collections.Generic;

public static class FieldOfViewSystem
{
    /// <summary>
    /// ★【中心ロジック】startマスからtargetマスの間に壁がないか（視線が通るか）を判定する
    /// </summary>
    public static bool HasLineOfSight(Vector3Int start, Vector3Int target, MapManager mapManager)
    {
        // 同一マスなら当然視線は通る
        if (start == target) return true;

        int x0 = start.x;
        int y0 = start.y;
        int x1 = target.x;
        int y1 = target.y;

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);

        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;

        int err = dx - dy;

        while (true)
        {
            // スタートとゴール以外の、途中のマスが壁かどうかをチェック
            if (new Vector3Int(x0, y0, 0) != start && new Vector3Int(x0, y0, 0) != target)
            {
                // MapManagerの既存の壁判定関数を叩く
                if (mapManager.IsWall(new Vector3Int(x0, y0, 0)))
                {
                    return false; // 壁に遮られた！
                }
            }

            // 目的地に到達したらループ終了（視線が通った）
            if (x0 == x1 && y0 == y1) break;

            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }

        return true;
    }
}