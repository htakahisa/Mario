# analyze_data.py
import argparse
import pandas as pd
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument("--data", type=str, default="normal_v3.csv")
args = parser.parse_args()

df = pd.read_csv(args.data)

print(f"\n📊 総データ数: {len(df)} 件")

# 行動の分布
print("\n--- 行動の分布 ---")
counts = df["act_type"].value_counts()
pct = df["act_type"].value_counts(normalize=True) * 100
for val, count in counts.items():
    bar = "█" * int(pct[val] / 2)
    print(f"{str(val):<12s}: {count:6d}件 ({pct[val]:5.1f}%) {bar}")

# 移動先の座標範囲
print("\n--- 移動先の座標範囲 ---")
print(f"act_grid_x: {df['act_grid_x'].min():.0f} ～ {df['act_grid_x'].max():.0f}")
print(f"act_grid_y: {df['act_grid_y'].min():.0f} ～ {df['act_grid_y'].max():.0f}")

# 観測値（自分の位置など）の分布
obs_cols = [c for c in df.columns if c.startswith("obs_")]
print(f"\n--- 観測値の列数: {len(obs_cols)} 個 ---")
print(df[obs_cols].describe().round(2).to_string())