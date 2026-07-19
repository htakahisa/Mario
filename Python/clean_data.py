import argparse
import glob
import os
import pandas as pd


def main():
    # 💡 コマンドライン引数の設定
    parser = argparse.ArgumentParser(
        description="Unity FPS PlayData Stay-Downsampling Cleaner"
    )
    parser.add_argument(
        "--data",
        type=str,
        default=None,
        help="単一のCSVファイルを指定してお掃除する場合のパス (例: human_play_data_3.csv)",
    )
    parser.add_argument(
        "--dir",
        type=str,
        default=None,
        help="フォルダを指定して、その中の全データを合体させてお掃除する場合のパス (例: ./raw_logs)",
    )
    parser.add_argument(
        "--output",
        type=str,
        default="human_play_data_cleaned.csv",
        help="お掃除後の保存ファイル名 (デフォルト: human_play_data_cleaned.csv)",
    )
    parser.add_argument(
        "--ratio",
        type=int,
        default=2,
        help="アクティブ行動に対してStayを何倍残すか (デフォルト: 2倍)",
    )
    args = parser.parse_args()

    # 1. 対象ファイルの収集
    csv_files = []

    if args.data is not None:
        if os.path.exists(args.data):
            csv_files.append(args.data)
        else:
            print(f"❌ エラー: 指定されたファイルが見つかりません: {args.data}")
            return
    elif args.dir is not None:
        search_pattern = os.path.join(args.dir, "*.csv")
        csv_files = glob.glob(search_pattern)
        if not csv_files:
            print(
                f"❌ エラー: 指定されたフォルダに対象のCSVファイルがありません: {search_pattern}"
            )
            return
    else:
        # 引数なしのデフォルト挙動
        default_file = "human_play_data.csv"
        if os.path.exists(default_file):
            csv_files.append(default_file)
        else:
            print(
                f"❌ エラー: 引数が指定されておらず、デフォルトの '{default_file}' も見つかりません。"
            )
            print(
                "使い方: python clean_data.py --data raw_data_1.csv  ... または  python clean_data.py --dir ./raw_logs"
            )
            return

    # 2. データの読み込みと合体
    print(f"📁 読み込み対象ファイル ({len(csv_files)}件):")
    dataframes = []
    for f in csv_files:
        print(f"  - {f}")
        dataframes.append(pd.read_csv(f))

    df = pd.concat(dataframes, ignore_index=True)
    print(f"📊 合体した元の総データ件数: {len(df)} 件")

    # 3. Stay と それ以外（アクティブ行動）に分ける
    df_active = df[df["act_type"] != "Stay"]
    df_stay = df[df["act_type"] == "Stay"]

    active_count = len(df_active)
    print(
        f"🔥 アクティブな行動（Moveやアビリティ等）の件数: {active_count} 件"
    )

    # 4. Stayを何件残すか決める（指定された倍数、デフォルト2倍）
    target_stay_count = active_count * args.ratio
    print(
        f"⏳ 残す予定のStay件数 (アクティブの {args.ratio} 倍): 約 {target_stay_count} 件"
    )

    # Stayデータからランダムに指定件数を抽出
    if len(df_stay) > target_stay_count:
        df_stay_sampled = df_stay.sample(n=target_stay_count, random_state=42)
    else:
        print(
            f"⚠️ 警告: 元のStay件数 ({len(df_stay)}件) が目標件数以下であるため、すべて残します。"
        )
        df_stay_sampled = df_stay

    # 5. データを結合してシャッフル
    # 💡 バグの原因になっていたローカル関数を排除し、直接 drop=True を指定するようにスッキリ修正しました
    df_cleaned = (
        pd.concat([df_active, df_stay_sampled])
        .sample(frac=1, random_state=42)
        .reset_index(drop=True)
    )

    # 6. 綺麗になったデータを別名で保存
    df_cleaned.to_csv(args.output, index=False)

    print("\n--- 📊 お掃除後のデータ分布 ---")
    counts = df_cleaned["act_type"].value_counts()
    pct = df_cleaned["act_type"].value_counts(normalize=True) * 100
    for val, count in counts.items():
        print(f"アクション型 {str(val):<12s} : {count:6d} 件 ({pct[val]:5.1f}%)")

    print(
        f"\n✨ お掃除完了！ '{args.output}' として1つにまとめて保存しました。"
    )


if __name__ == "__main__":
    main()