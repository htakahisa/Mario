using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Splines;

public class ReconBoltProjectile : MonoBehaviour
{
    [Header("エフェクト用参照（Unityエディタ側で白い円の画像をセット）")]
    public SpriteRenderer scanEffectRenderer; // ★子オブジェクト等のSpriteRendererをセットしておく

    private Vector3 direction;
    private float speed = 8f;
    private float scanRadius = 8f; // 索敵半径（8マス）
    private int remainingTicks = 50; // ★寿命をTick管理（5.0秒 = 50Tick）
    private int revealTickDuration = 30; // ★位置暴露時間をTick管理（3.0秒 = 30Tick）
    private bool isInitialized = false;
    private bool isStuck = false; // ★壁に刺さっている（着弾している）状態フラグ

    private MapManager mapManager;
    private int effectRemainingTicks = 0; // ★エフェクトの残り生存Tick数
    private SpriteRenderer projectileRenderer;
    public Agent owner;

    private void Awake()
    {
        projectileRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// 発射時に初期位置と目的地を設定する（初期化・プールからの有効化）
    /// </summary>
    public void Launch(Vector3 startWorldPos, Vector3 targetWorldPos, MapManager manager)
    {

        transform.position = new Vector3(startWorldPos.x, startWorldPos.y, 0f);
        gameObject.SetActive(true);

        // 2. 進行方向のベクトルを計算
        direction = (targetWorldPos - startWorldPos).normalized;

        // ★【追加】進行方向から角度（Z軸の回転）を計算して適用する
        // Mathf.Atan2 でラジアン角を求め、Mathf.Rad2Deg で度数法（度）に変換します
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // 三角形のプレファブの「尖っている先端」が、元々右向き（X軸のプラス方向）で作られている場合：
        transform.rotation = Quaternion.Euler(0, 0, angle - 90f);

        mapManager = manager;

        direction = (targetWorldPos - startWorldPos).normalized;
        direction.z = 0;

        isInitialized = true;
        isStuck = false;
        remainingTicks = 50;
        effectRemainingTicks = 0;

        // 見た目のリセット
        if (projectileRenderer != null) projectileRenderer.enabled = true;
        if (scanEffectRenderer != null) scanEffectRenderer.gameObject.SetActive(false);

        gameObject.SetActive(true);
    }

    /// <summary>
    /// 💡 見た目のぬるっとした直線移動だけをUpdateで処理（描画・補間用）
    /// </summary>
    void Update()
    {
        if (!isInitialized || isStuck) return;

        // 壁に当たっていない間だけ移動
        transform.position += direction * speed * Time.deltaTime;
    }

    /// <summary>
    /// ★GameManagerから1Tick（0.1秒）ごとに呼び出される判定・ロジック
    /// </summary>
    public void UpdateProjectileTick()
    {
        if (!isInitialized) return;

        // 1. すでに壁に刺さって索敵エフェクトが出ている状態の処理
        if (isStuck)
        {
            UpdateEffectTick();
            return;
        }

        // 2. 飛行中の処理（寿命チェック）
        remainingTicks--;
        if (remainingTicks <= 0)
        {
            DeactivateProjectile();
            return;
        }

        // 🌟【改良点】1Tick分の移動ベクトルを計算する
        // moveDirection: 進行方向のベクトル（Vector3.normalized）
        // speed: 1秒間の移動速度。0.1fをかけることで1Tick（0.1秒）の移動距離になります。
        Vector3 currentPos = transform.position;
        Vector3 movement = direction * speed * 0.1f;
        Vector3 nextPos = currentPos + movement;

        // 🌟【改良点】現在地から次の目的地までの「線」の間に壁があるかチェック
        // 現在のGrid座標系（MapManager）に合わせて、移動経路上のすべてのマスを先読みチェックします
        int tickMass = Mathf.CeilToInt(movement.magnitude * 2f); // 1マスを2段階以上に細かく割ってチェック
        bool hitWallDetected = false;
        Vector3 wallHitPosition = nextPos;

        for (int i = 1; i <= tickMass; i++)
        {
            float t = (float)i / tickMass;
            Vector3 checkPos = Vector3.Lerp(currentPos, nextPos, t);
            Vector3Int checkGridPos = Vector3Int.RoundToInt(checkPos);
            checkGridPos.z = 0;

            if (mapManager.IsWall(checkGridPos))
            {
                hitWallDetected = true;
                wallHitPosition = checkPos; // 壁に触れた正確な座標をキープ
                break; // 壁が見つかったのでチェック終了
            }
        }

        // 3. 判定に基づいた移動・衝突の適用
        if (hitWallDetected)
        {
            // 壁に当たった場合は、その壁の表面（またはめり込んだ位置）にワープさせて着弾
            transform.position = wallHitPosition;
            HitWall();
        }
        else
        {
            // 壁がなければ、予定通り次の座標へ移動
            transform.position = nextPos;
        }
    }

    /// <summary>
    /// 壁着弾時の処理
    /// </summary>
    private void HitWall()
    {
        isStuck = true;
        effectRemainingTicks = 22; // 2.2秒分 = 22Tick

        if (projectileRenderer != null) projectileRenderer.enabled = false; // 矢の見た目を消す

        // ★修正：現在の矢の位置（＝着弾点）にエフェクトを移動させる
        if (scanEffectRenderer != null)
        {
            scanEffectRenderer.gameObject.SetActive(true);

            // 🌟 hitPosition の代わりに this.transform.position を使用
            scanEffectRenderer.gameObject.transform.position = this.transform.position;

            scanEffectRenderer.transform.localScale = new Vector3(scanRadius * 2f, scanRadius * 2f, 1f);
            // ソヴァカラー（水色）かつ初期アルファ値を設定
            scanEffectRenderer.color = new Color(0f, 0.75f, 1f, 0.25f);
        }

        Debug.Log("<color=cyan>【リコンボルト】</color> 壁に着弾！索敵パルスを展開。");

        // 敵の索敵処理（物理2Dを使わず、全Agentとの距離判定に変形）
        ScanEnemies();
    }

    /// <summary>
    /// 範囲内の敵をスキャンして位置を暴露（Tick駆動）
    /// </summary>
    private void ScanEnemies()
    {
        Agent[] allAgents = Object.FindObjectsByType<Agent>(FindObjectsSortMode.None);
        foreach (Agent agent in allAgents)
        {
            if (agent == null || agent.isDead) continue;

            // 敵チームのみを対象にする
            if (agent.isEnemy != owner.isEnemy)
            {
                // グリッド座標またはワールド座標の距離を計測
                float distance = Vector3.Distance(transform.position, agent.transform.position);
                if (distance <= scanRadius)
                {
                    agent.RevealAgentTicks(revealTickDuration, owner); // ★Agent側もTickで管理するメソッドを呼ぶ
                    Debug.Log($"<color=cyan><b>[REVEALED]</b></color> リコンボルトが {agent.agentName} を補足した！");
                }
            }
        }
    }

    /// <summary>
    /// エフェクトのフェードアウトと生存管理（Tick駆動）
    /// </summary>
    private void UpdateEffectTick()
    {
        effectRemainingTicks--;
        if (effectRemainingTicks <= 0)
        {
            DeactivateProjectile();
            return;
        }

        // 残りTickに応じてアルファ値を下げる（フェードアウトの再現）
        if (scanEffectRenderer != null)
        {
            float alphaRatio = (float)effectRemainingTicks / 22f;
            scanEffectRenderer.color = new Color(0f, 0.75f, 1f, alphaRatio * 0.25f);
        }
    }

    /// <summary>
    /// 完全に終了したためプールに戻す（非アクティブ化）
    /// </summary>
    private void DeactivateProjectile()
    {
        isInitialized = false;
        isStuck = false;
        gameObject.SetActive(false);
    }

    // --- ★ここから完全再現（ステート保存・復元）のためのコード ---

    /// <summary>
    /// 現在の矢のステートを構造体にして切り出す
    /// </summary>
    public ReconBoltStateData SaveState()
    {
        ReconBoltStateData state = new ReconBoltStateData();
        state.isActive = gameObject.activeSelf;
        state.position = transform.position;
        state.direction = this.direction;
        state.remainingTicks = this.remainingTicks;
        state.effectRemainingTicks = this.effectRemainingTicks;
        state.isInitialized = this.isInitialized;
        state.isStuck = this.isStuck;
        state.owner = this.owner;
        return state;
    }

    /// <summary>
    /// 外部データから状態を寸分狂わずに復元する
    /// </summary>
    public void LoadState(ReconBoltStateData state)
    {
        gameObject.SetActive(state.isActive);
        if (!state.isActive) return;

        transform.position = state.position;
        this.direction = state.direction;
        this.remainingTicks = state.remainingTicks;
        this.effectRemainingTicks = state.effectRemainingTicks;
        this.isInitialized = state.isInitialized;
        this.isStuck = state.isStuck;
        this.owner = state.owner;

        // 状態に応じた見た目の復元
        if (isStuck)
        {
            if (projectileRenderer != null) projectileRenderer.enabled = false;
            if (scanEffectRenderer != null)
            {
                scanEffectRenderer.gameObject.SetActive(true);
                float alphaRatio = (float)effectRemainingTicks / 22f;
                scanEffectRenderer.color = new Color(0f, 0.75f, 1f, alphaRatio * 0.25f);
                scanEffectRenderer.transform.localScale = new Vector3(scanRadius * 2f, scanRadius * 2f, 1f);
            }
        }
        else
        {
            if (projectileRenderer != null) projectileRenderer.enabled = true;
            if (scanEffectRenderer != null) scanEffectRenderer.gameObject.SetActive(false);
        }
    }
}