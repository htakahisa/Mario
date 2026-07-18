import argparse
import os
import joblib
import numpy as np
import torch
import torch.nn as nn
from unity_env import UnityFPSEnv


class BehaviorCloningModel(nn.Module):

    def __init__(self, input_dim=34, num_classes=5):
        super(BehaviorCloningModel, self).__init__()
        self.base = nn.Sequential(
            nn.Linear(input_dim, 256),
            nn.ReLU(),
            nn.Dropout(p=0.2),
            nn.Linear(256, 128),
            nn.ReLU(),
        )
        self.action_head = nn.Sequential(
            nn.Linear(128, 64), nn.ReLU(), nn.Linear(64, num_classes)
        )
        self.coord_head = nn.Sequential(
            nn.Linear(128, 64), nn.ReLU(), nn.Linear(64, 4)
        )

    def forward(self, x):
        features = self.base(x)
        action_logits = self.action_head(features)
        coords = self.coord_head(features)
        return action_logits, coords


def main():
    parser = argparse.ArgumentParser(
        description="Run Unity FPS BC Agent with custom model/scaler"
    )
    parser.add_argument("--model", type=str, default="bc_model.pth")
    parser.add_argument("--scaler", type=str, default="scaler.pkg")
    args = parser.parse_args()

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

    if not os.path.exists(args.model) or not os.path.exists(args.scaler):
        print("❌ エラー: モデルかスケーラーファイルが見つかりません。")
        return

    print(f"📦 ロード中: モデル -> {args.model} | スケーラー -> {args.scaler}")

    model = BehaviorCloningModel(input_dim=34, num_classes=5).to(device)
    model.load_state_dict(torch.load(args.model, map_location=device))
    model.eval()

    scaler = joblib.load(args.scaler)
    print(f"[+] ロード完了。使用デバイス: {device}")

    env = UnityFPSEnv()
    action_names = {
        0: "Stay",
        1: "Move",
        2: "Paranoia",
        3: "ReconBolt",
        4: "Defuse",
    }

    try:
        obs, info = env.reset()
        print("[+] Unityとの同期を開始します。AI操作開始...")

        while True:
            obs_reshaped = obs.reshape(5, 34)
            obs_scaled = scaler.transform(obs_reshaped)
            obs_tensor = torch.FloatTensor(obs_scaled).to(device)

            with torch.no_grad():
                action_logits, pred_coords = model(obs_tensor)

                # 💡 スタック防止策: 確率ベースで行動を選択
                probabilities = torch.softmax(action_logits, dim=1)
                pred_action_indices = np.zeros(5, dtype=np.int32)
                for idx in range(5):
                    prob = probabilities[idx].cpu().numpy()
                    pred_action_indices[idx] = np.random.choice(
                        len(prob), p=prob
                    )

                coords = pred_coords.cpu().numpy()

            action_to_send = []
            debug_logs = []

            for i in range(5):
                agent_action_idx = pred_action_indices[i]
                agent_coords = coords[i]

                # 💡 [大修正] 0,1,2に制限(clip)するのをやめ、AIの予測値をそのまま整数化します
                # これによりCSVにあった -21 や 20 といった遠くの目標マスがそのままUnityへ渡ります
                grid_x_final = int(np.round(agent_coords[0]))
                grid_y_final = int(np.round(agent_coords[1]))

                # 行動タイプのマッピング
                gym_act_type = 1  # デフォルト: Stay
                if agent_action_idx == 1:
                    gym_act_type = 0  # Move
                elif agent_action_idx == 0:
                    gym_act_type = 1  # Stay
                elif agent_action_idx == 2:
                    gym_act_type = 2  # Paranoia
                elif agent_action_idx == 3:
                    gym_act_type = 3  # ReconBolt
                elif agent_action_idx == 4:
                    gym_act_type = 4  # Defuse

                # Unity側（C#）が受け取る配列のルール [行動型, X座標, Y座標] に合わせて追加
                action_to_send.extend([gym_act_type, grid_x_final, grid_y_final])

                act_name = action_names.get(agent_action_idx, "Unknown")
                debug_logs.append(
                    f"Agent {i}: {act_name:<10} (Grid: {grid_x_final}, {grid_y_final})"
                )

            print(" | ".join(debug_logs), end="\r", flush=True)

            action_array = np.array(action_to_send, dtype=np.int32)
            obs, reward, terminated, truncated, info = env.step(action_array)

            if terminated or truncated:
                print("\n[🔄 Round End] 環境をリセットします。")
                obs, info = env.reset()

    except KeyboardInterrupt:
        print("\n[-] ユーザーによって終了されました。")
    finally:
        env.close()
        print("[*] ソケットをクローズしました。")


if __name__ == "__main__":
    main()