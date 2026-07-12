using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BulletTracer : MonoBehaviour
{
    private LineRenderer lr;

    // ★Time.deltaTimeではなく、GameManagerのTickに同期して消滅させる
    // 0.1秒で見えなくなる＝1Tickだけ表示して消える設定にする
    private int remainingTicks = 5;
    private bool isInitialized = false;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
    }

    /// <summary>
    /// 弾道の始点と終点をセットし、プールから有効化する
    /// </summary>
    public void Init(Vector3 start, Vector3 end)
    {
        // Z軸のズレでラインが描画されないのを防ぐ
        Vector3 startPos = new Vector3(start.x, start.y, -0.1f);
        Vector3 endPos = new Vector3(end.x, end.y, -0.1f);

        lr.SetPosition(0, startPos);
        lr.SetPosition(1, endPos);

        isInitialized = true;
        gameObject.SetActive(true);
    }

    /// <summary>
    /// ★【新設】GameManagerから毎Tick呼ばれる処理
    /// </summary>
    public void UpdateTracerTick()
    {
        if (!isInitialized) return;

        remainingTicks--;
        if (remainingTicks <= 0)
        {
            DeactivateTracer();
        }
    }

    /// <summary>
    /// Destroyせず、非アクティブにしてプールに戻す
    /// </summary>
    private void DeactivateTracer()
    {
        isInitialized = false;
        gameObject.SetActive(false);
    }

    // --- もし「弾道も完全にシリアライズ（巻き戻し）対象」にする場合、以下を有効化します ---
    /*
    public BulletTracerStateData SaveState()
    {
        BulletTracerStateData state = new BulletTracerStateData();
        state.isActive = gameObject.activeSelf;
        if (state.isActive)
        {
            state.startPos = lr.GetPosition(0);
            state.endPos = lr.GetPosition(1);
            state.remainingTicks = this.remainingTicks;
        }
        return state;
    }

    public void LoadState(BulletTracerStateData state)
    {
        gameObject.SetActive(state.isActive);
        if (!state.isActive) return;

        lr.SetPosition(0, state.startPos);
        lr.SetPosition(1, state.endPos);
        this.remainingTicks = state.remainingTicks;
        this.isInitialized = true;
    }
    */
}