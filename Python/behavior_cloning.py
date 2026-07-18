import argparse
import glob
import os
import joblib
import numpy as np
import pandas as pd
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import DataLoader, Dataset

BATCH_SIZE = 128
EPOCHS = 30
LEARNING_RATE = 0.001

ACTION_MAP = {"Stay": 0, "Move": 1, "Paranoia": 2, "ReconBolt": 3, "Defuse": 4}
NUM_ACTIONS = len(ACTION_MAP)


class BehaviorDataset(Dataset):

    def __init__(self, observations, act_types, act_coords):
        self.observations = torch.FloatTensor(observations)
        self.act_types = torch.LongTensor(act_types)
        self.act_coords = torch.FloatTensor(act_coords)

    def __len__(self):
        return len(self.observations)

    def __getitem__(self, idx):
        return self.observations[idx], self.act_types[idx], self.act_coords[idx]


class BehaviorCloningModel(nn.Module):
    def __init__(self, input_dim, num_classes):
        super(BehaviorCloningModel, self).__init__()
        self.base = nn.Sequential(
            nn.Linear(input_dim, 512),  # 👈 256 から 512 に拡張
            nn.ReLU(),
            nn.Dropout(0.2),
            nn.Linear(512, 256),        # 👈 128 から 256 に拡張
            nn.ReLU(),
            nn.Dropout(0.2),
            nn.Linear(256, 128),        # 👈 情報をマイルドに圧縮する層を1つ追加
            nn.ReLU(),
            nn.Dropout(0.2),
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
        description="FPS AI Behavior Cloning Training"
    )
    parser.add_argument("--data", type=str, default=None)
    parser.add_argument("--dir", type=str, default=None)
    parser.add_argument("--model_output", type=str, default="bc_model.pth")
    parser.add_argument("--scaler_output", type=str, default="scaler.pkg")
    args = parser.parse_args()

    csv_files = []
    if args.data is not None:
        if os.path.exists(args.data):
            csv_files.append(args.data)
        else:
            print(f"❌ エラー: ファイルが見つかりません: {args.data}")
            return
    elif args.dir is not None:
        search_pattern = os.path.join(args.dir, "*.csv")
        csv_files = glob.glob(search_pattern)
        if not csv_files:
            print(f"❌ エラー: フォルダに対象CSVがありません")
            return
    else:
        default_file = "human_play_data.csv"
        if os.path.exists(default_file):
            csv_files.append(default_file)
        else:
            default_cleaned = "human_play_data_cleaned.csv"
            if os.path.exists(default_cleaned):
                csv_files.append(default_cleaned)
            else:
                print("❌ エラー: データファイルが見つかりません。")
                return

    print(f"📁 読み込み対象ファイル ({len(csv_files)}件):")
    dataframes = [pd.read_csv(f) for f in csv_files]
    df = pd.concat(dataframes, ignore_index=True)

    df = df[df["act_type"].isin(ACTION_MAP.keys())].reset_index(drop=True)

    obs_cols = [c for c in df.columns if c.startswith("obs_")]
    X = df[obs_cols].values

    y_type = df["act_type"].map(ACTION_MAP).astype(int).values
    y_grid = df[["act_grid_x", "act_grid_y"]].values
    y_target = df[["act_ability_target_x", "act_ability_target_y"]].values
    y_coords = np.hstack([y_grid, y_target])

    # 指定された scaler ファイルが既に存在する場合は読み込み、なければ新しく作る
    if os.path.exists(args.scaler_output):
        print(f"📦 既存のスケーラーを発見しました。これを使い回します: {args.scaler_output}")
        scaler = joblib.load(args.scaler_output)
        X_scaled = scaler.transform(X) # 🚨 既存の基準で変換（transform）だけ行う！
    else:
        print(f"✨ スケーラーが見つからないため、新規作成（fit）します: {args.scaler_output}")
        scaler = StandardScaler()
        X_scaled = scaler.fit_transform(X)
        joblib.dump(scaler, args.scaler_output)
        
    print(f"[1/4] データの標準化が完了しました。 (総データ: {len(df)}件)")

    X_train, X_val, y_t_train, y_t_val, y_c_train, y_c_val = train_test_split(
        X_scaled, y_type, y_coords, test_size=0.15, random_state=42
    )

    train_dataset = BehaviorDataset(X_train, y_t_train, y_c_train)
    val_dataset = BehaviorDataset(X_val, y_t_val, y_c_val)

    train_loader = DataLoader(
        train_dataset, batch_size=BATCH_SIZE, shuffle=True
    )
    val_loader = DataLoader(val_dataset, batch_size=BATCH_SIZE, shuffle=False)

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    model = BehaviorCloningModel(
        input_dim=X.shape[1], num_classes=NUM_ACTIONS
    ).to(device)

    class_counts = np.bincount(y_type, minlength=NUM_ACTIONS)
    class_weights = 1.0 / (class_counts + 1e-5)
    class_weights = class_weights / class_weights.sum() * NUM_ACTIONS
    class_weights_tensor = torch.FloatTensor(class_weights).to(device)

    criterion_action = nn.CrossEntropyLoss(weight=class_weights_tensor)
    criterion_coord = nn.MSELoss()

    optimizer = optim.Adam(model.parameters(), lr=LEARNING_RATE)

    print(f"[2/4] 学習を開始します。使用デバイス: {device}")

    for epoch in range(EPOCHS):
        model.train()
        train_loss = 0.0
        for obs, act_t, coords in train_loader:
            obs, act_t, coords = (
                obs.to(device),
                act_t.to(device),
                coords.to(device),
            )
            optimizer.zero_grad()
            pred_action, pred_coords = model(obs)

            loss_action = criterion_action(pred_action, act_t)
            coord_mask = (act_t > 0).unsqueeze(1).expand_as(coords)
            loss_coord = (
                criterion_coord(pred_coords * coord_mask, coords * coord_mask)
                if coord_mask.any()
                else torch.tensor(0.0).to(device)
            )

            loss = loss_action + loss_coord * 0.5
            loss.backward()
            optimizer.step()
            train_loss += loss.item() * obs.size(0)

        train_loss /= len(train_loader.dataset)

        model.eval()
        val_loss = 0.0
        correct_actions = 0
        total = 0
        with torch.no_grad():
            for obs, act_t, coords in val_loader:
                obs, act_t, coords = (
                    obs.to(device),
                    act_t.to(device),
                    coords.to(device),
                )
                pred_action, pred_coords = model(obs)

                loss_action = criterion_action(pred_action, act_t)
                coord_mask = (act_t > 0).unsqueeze(1).expand_as(coords)
                loss_coord = (
                    criterion_coord(
                        pred_coords * coord_mask, coords * coord_mask
                    )
                    if coord_mask.any()
                    else torch.tensor(0.0).to(device)
                )

                loss = loss_action + loss_coord * 0.5
                val_loss += loss.item() * obs.size(0)

                _, predicted = torch.max(pred_action, 1)
                correct_actions += (predicted == act_t).sum().item()
                total += act_t.size(0)

        val_loss /= len(val_loader.dataset)
        accuracy = (correct_actions / total) * 100
        print(
            f"Epoch {epoch+1:02d}/{EPOCHS:02d} | Train Loss: {train_loss:.4f} | Val Loss: {val_loss:.4f} | Action Acc: {accuracy:.2f}%"
        )

    torch.save(model.state_dict(), args.model_output)
    print(f"[3/4] モデルを保存しました: {args.model_output}")
    print("[4/4] 模倣学習が正常に終了しました！")


if __name__ == "__main__":
    main()