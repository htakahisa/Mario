import os
from stable_baselines3 import PPO
from unity_env import UnityFPSEnv

def main():
    # 1. カスタムしたUnity環境を生成
    env = UnityFPSEnv(host="127.0.0.1", port=50007)

    # 2. 使いたいモデルのパスを指定してロード（例: models/version_1/ppo_fps_agent_model）
    # ※ フォルダ分けの構造に合わせてここを書き換えます
    model_path = "./models/version_1/ppo_fps_agent_model"
    
    if not os.path.exists(f"{model_path}.zip"):
        print(f"[❌] 指定されたモデルが見つかりません: {model_path}.zip")
        env.close()
        return

    print(f"[+] 🧠 モデル【{model_path}】をロード中...（テスト走行モード）")
    model = PPO.load(model_path, env=env)

    print("[+] 🚀 テスト走行を開始します！ Unity側を再生してください。")
    
    # 3. 推論ループ（無限にゲームを回す）
    obs, info = env.reset()
    while True:
        # deterministic=True にすることで、ランダム性を排除した「AIのガチのベスト行動」をさせます
        action, _states = model.predict(obs, deterministic=True)
        
        # 環境に行動を送信
        obs, reward, terminated, truncated, info = env.step(action)
        
        # ラウンドが終了したらリセット
        if terminated or truncated:
            obs, info = env.reset()

    env.close()

if __name__ == "__main__":
    main()