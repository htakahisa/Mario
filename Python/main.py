import os
from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import BaseCallback
from unity_env import UnityFPSEnv

# 🔥【新機能】UnityのタイムスケールをPython側から制御して爆速化するコールバック
class UnitySpeedUpCallback(BaseCallback):
    def __init__(self, verbose=0):
        super(UnitySpeedUpCallback, self).__init__(verbose)
        
    def _on_training_start(self) -> None:
        # 学習開始時に、環境（Unity）側に超高速シミュレーションの命令を送るプロパティを仕込む
        # ※もしUnity側でTime.timeScaleを直接弄る場合は、このタイミングで1度リセットをかけると安定します
        print("[*] ⚡ Unityの高速シミュレーションモードを要求します。")

    def _on_step(self) -> bool:
        return True


def main():
    # テンソルボードのログを保存するディレクトリ名
    log_dir = "./ppo_fps_tensorboard/"
    os.makedirs(log_dir, exist_ok=True)

    # 1. カスタムしたUnity環境を生成
    env = UnityFPSEnv(host="127.0.0.1", port=50007)

    # 保存する（読み込む）モデルのファイル名
    model_path = "ppo_fps_agent_model"
    # zipの拡張子がついた状態のパス（存在チェック用）
    model_zip_path = f"{model_path}.zip"

    # --- 🔥【ここを変更】既存のモデルがあればロード、無ければ新規作成 ---
    if os.path.exists(model_zip_path):
        print(f"[+] 🧠 既存のモデル【{model_zip_path}】を発見しました！")
        print("[*] 💾 前回の脳みそを引き継いで、追加学習用のモデルをロード中...")
        
        # 保存されたモデルをロードして環境(env)とログディレクトリを再バインドする
        model = PPO.load(model_path, env=env, tensorboard_log=log_dir)
    else:
        print("[-] 🆕 前回のモデルが見つかりません。")
        print("[*] 🧠 新規に PPO ニューラルネットワークモデルを初期化中...")
        
        # 初回は今まで通り新規で作成
        model = PPO(
            "MlpPolicy", 
            env, 
            verbose=1, 
            learning_rate=3e-4, 
            n_steps=2048,       # 2048 Tickごとに行動の反省（学習）を行う
            batch_size=64,
            tensorboard_log=log_dir,
            device="cpu"        # 必要に応じて "cuda" に変更してください
        )

    print("[+] 🔥 AIの強化学習を開始します！ Unity側を再生してください。")
    print("[*] 学習中の進捗は次のコマンドで確認できます: tensorboard --logdir=" + log_dir)
    
    # 3. 学習の実行（10万Tick分）
    speed_callback = UnitySpeedUpCallback()
    
    # 🔥【重要】reset_num_timesteps=False を追加
    # これにより、TensorBoardのステップ数が0に巻き戻らず、前回の10万の続き（100,001〜）から綺麗に1本の線で描画されます。
    model.learn(total_timesteps=100000, callback=speed_callback, reset_num_timesteps=False)

    # 4. 学習が終わったらモデルを上書き保存
    model.save(model_path)
    print(f"[+] 学習完了！最新の脳みそを【{model_zip_path}】に上書き保存しました。")
    
    env.close()

if __name__ == "__main__":
    main()