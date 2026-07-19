import os
from collections import deque
import numpy as np
from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import CheckpointCallback, EvalCallback, BaseCallback
from unity_env import UnityFPSEnv

# ⚡ Unityのタイムスケールを制御する高速化コールバック
class UnitySpeedUpCallback(BaseCallback):
    def __init__(self, verbose=0):
        super(UnitySpeedUpCallback, self).__init__(verbose)
        
    def _on_training_start(self) -> None:
        print("[*] ⚡ Unityの高速シミュレーションモードを要求します。")

    def _on_step(self) -> bool:
        return True


# 📊 【新機能】直近の勝率をリアルタイムでTensorBoardに送るコールバック
class TensorBoardWinRateCallback(BaseCallback):
    def __init__(self, window_size=50, verbose=0):
        super(TensorBoardWinRateCallback, self).__init__(verbose)
        # 直近50試合（お好みで変更可）の勝敗を記録するキュー
        self.win_history = deque(maxlen=window_size)
        
    def _on_step(self) -> bool:
        # PPOが環境の step() から受け取った infos 辞書をチェック
        for info in self.locals.get("infos", []):
            # infoに "is_win" が含まれていればキューに保存
            if "is_win" in info:
                self.win_history.append(info["is_win"])
                
                # 直近の平均勝率を計算（例：0.20 = 勝率2割、0.55 = 勝率5割5分）
                current_win_rate = np.mean(self.win_history)
                
                # TensorBoardに 'rollout/win_rate' という独立した名前で記録
                self.logger.record("rollout/win_rate", current_win_rate)
                
        return True


def main():
    # 1. 環境の起動
    env = UnityFPSEnv()
    
    # reset() を一度呼んでUnity側の現在の設定（実験名など）を同期
    env.reset()
    exp_name = env.experiment_name
    start_from_scratch = env.start_from_scratch

    print(f"[🚀 Start] 実験名: '{exp_name}' での学習を開始します。")

    # 📂 保存先フォルダの作成 (models/実験名/)
    save_dir = f"./models/{exp_name}/"
    os.makedirs(save_dir, exist_ok=True)

    # 📊 TensorBoardのログフォルダを実験名ごとに分ける
    tb_log_dir = f"./tb_log/{exp_name}/"

    # ========================================================
    # 📋 TensorBoard起動用コマンドを自動生成して出力
    # ========================================================
    absolute_tb_path = os.path.abspath(tb_log_dir)
    print("\n" + "="*70)
    print("[📊 TensorBoard 起動コマンド]")
    print(" 以下のコマンドをコピーして、別のターミナル（コマンドプロンプト等）で実行してください:")
    print(f" tensorboard --logdir=\"{absolute_tb_path}\"")
    print("="*70 + "\n")

    # 💾 【コールバック1】定期的な自動保存バックアップ（20,000ステップごと）
    checkpoint_callback = CheckpointCallback(
        save_freq=20000, 
        save_path=save_dir,
        name_prefix=f"{exp_name}_checkpoint"
    )

    # 🏆 【コールバック2】最高スコア（最強状態）のモデルを自動キープ
    eval_callback = EvalCallback(
        env, 
        best_model_save_path=save_dir,
        log_path=save_dir, 
        eval_freq=20000,
        deterministic=True, 
        render=False
    )

    # ⚡ 【コールバック3】高速化コールバック
    speed_callback = UnitySpeedUpCallback()

    # 📊 【新機能：コールバック4】勝率ログ収集用コールバック (直近50試合を追跡)
    win_rate_callback = TensorBoardWinRateCallback(window_size=50)

    # 🤝 すべてのコールバックをリストにまとめる（win_rate_callback を追加）
    callbacks = [checkpoint_callback, eval_callback, speed_callback, win_rate_callback]

    # 🧠 2. モデルの初期化（新規 or 続きからロード）
    model_path = f"{save_dir}{exp_name}_final.zip"
    
    # 💡 【共通設定】学習率とエントロピー係数
    hyperparams = {
        "learning_rate": 1e-4,
        "ent_coef": 0.01
    }

    # 💡 5人の複雑な連携を学習するため、ニューラルネットワークの隠れ層を拡張
    policy_kwargs = dict(net_arch=dict(pi=[256, 256], vf=[256, 256]))

    if not start_from_scratch and os.path.exists(model_path):
        print(f"[📂 Load] 既存のモデル '{model_path}' を読み込んで再開します。（ハイパーパラメータを最新に更新）")
        model = PPO.load(model_path, env=env, tensorboard_log=tb_log_dir, custom_objects=hyperparams)
    else:
        print("[🆕 Create] 完全に真っ新な状態からニューラルネットワークモデルを新規作成します。")
        model = PPO(
            "MlpPolicy", 
            env, 
            verbose=1, 
            tensorboard_log=tb_log_dir,
            learning_rate=hyperparams["learning_rate"],  
            ent_coef=hyperparams["ent_coef"],
            n_steps=2048,           # 2048 Tickごとに行動の反省を行う
            batch_size=64,          # ミニバッチサイズ
            policy_kwargs=policy_kwargs, 
            device="cpu"            # 必要に応じて "cuda" に変更してください
        )

    # 🎯 3. 学習の実行（目標: 200万ステップ）
    try:
        # reset_num_timesteps=False でTensorBoardのグラフの線を1本に繋ぎます
        model.learn(total_timesteps=2000000, callback=callbacks, reset_num_timesteps=False)
    except KeyboardInterrupt:
        print("\n[⚠️ Stop] 学習が手動（Ctrl+C）で中断されました。現在の状態を保存します。")
    finally:
        # 🏆 学習終了時、または中断時の最終保存
        final_path = f"{save_dir}{exp_name}_final"
        model.save(final_path)
        print(f"[💾 Saved] 最終モデルを保存しました: {final_path}.zip")
        env.close()

if __name__ == "__main__":
    main()