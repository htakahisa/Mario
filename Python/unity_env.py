import json
import socket
import numpy as np
import gymnasium as gym
from gymnasium import spaces

class UnityFPSEnv(gym.Env):
    metadata = {"render_modes": ["human"]}

    def __init__(self, host="127.0.0.1", port=50007):
        super(UnityFPSEnv, self).__init__()
        
        # --- 🌐 通信設定 ---
        self.host = host
        self.port = port
        self.server_socket = None
        self.client_socket = None
        self.data_buffer = ""
        
        # --- 🎮 アクションスペースの定義 (5人分) ---
        self.num_agents = 5 
        
        flat_list = []
        for _ in range(self.num_agents):
            flat_list.extend([8, 3, 3])  # 3は（-1, 0, 1の3択）
            
        self.action_space = spaces.MultiDiscrete(np.array(flat_list, dtype=np.int32))
        
        # --- 👁️ 状態スペース（34次元 × 5人分 = 170次元） ---
        # 【1人あたりの内訳（計34次元）】
        # 自分自身情報 (5): 自分のx, 自分のy, HP比率, 死亡フラグ, スパイク所持フラグ
        # アビリティ (2): paranoia残弾, reconBolt残弾
        # スパイク情報 (7): 現在のstate数値, plantedOrDefusingフラグ, 相対PlantedX, 相対PlantedY, droppedフラグ, 相対DroppedX, 相対DroppedY
        # 他エージェント情報4人分 (5*4=20): 関係フラグ(敵味方), 相対X, 相対Y, HP比率, 死亡フラグ
        
        # 34次元の各下限値 (Low) と上限値 (High)
        single_agent_low = (
            [-100, -100, 0, 0, 0] +                 # 自分自身情報
            [0, 0] +                                # アビリティ
            [0, 0, -200, -200, 0, -200, -200] +     # スパイク情報 (相対座標のため余裕を持ったマイナス値を指定)
            [-1, -200, -200, 0, 0] * 4              # 他キャラクター4人分情報
        )
        single_agent_high = (
            [100, 100, 1, 1, 1] +                   # 自分自身情報
            [2, 2] +                                # アビリティ
            [4, 1, 200, 200, 1, 200, 200] +         # スパイク情報
            [1, 200, 200, 1, 1] * 4                 # 他キャラクター4人分情報
        )
        
        self.observation_space = spaces.Box(
            low=np.array(single_agent_low * self.num_agents, dtype=np.float32),
            high=np.array(single_agent_high * self.num_agents, dtype=np.float32),
            dtype=np.float32
        )
        
        # --- 🛡️ 安全ガード・学習インセンティブ用の内部変数 ---
        self.last_win_probability = 0.5
        self._last_raw_data = {}  
        self.last_action = np.array([], dtype=np.int32)
        self.reward_given = False  
        
        # 🛠️ 各エージェント（個別）の「1Tick前スパイク距離」を管理する辞書
        self.last_individual_distances = {i: 999.0 for i in range(self.num_agents)}
        self.last_avg_distance = 0.0
        
        # 🛠️ 連続解除Tick数を追跡するカウンター
        self.continuous_defuse_ticks = 0
        
        # 💡 【硬直対策】各エージェントの現在地とスタックTick数を追跡する辞書
        self.last_agent_positions = {i: (0, 0) for i in range(self.num_agents)}
        self.agent_stuck_ticks = {i: 0 for i in range(self.num_agents)}
        
        self.last_damage_dealt = 0
        self.last_damage_taken = 0
        
        self.round_reward_breakdown = {}
        self._reset_reward_breakdown()
        
        self.start_server()

    def start_server(self):
        print(f"[*] Python AI Server 起動中... {self.host}:{self.port}")
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server_socket.bind((self.host, self.port))
        self.server_socket.listen(1)
        print("[*] Unityからの接続を待っています...")
        self.client_socket, addr = self.server_socket.accept()
        print(f"[+] Unityとカチッと接続成功！: {addr}")

    def _receive_unity_data(self):
        while "\n" not in self.data_buffer:
            chunk = self.client_socket.recv(16384)
            if not chunk:
                raise ConnectionResetError("Unityが切断されました")
            self.data_buffer += chunk.decode("utf-8")
        
        json_line, self.data_buffer = self.data_buffer.split("\n", 1)
        return json.loads(json_line.strip())

    def _reset_reward_breakdown(self):
        self.round_reward_breakdown = {
            "proximity_reward_or_penalty": 0.0, 
            "damage_dealt": 0.0,
            "damage_taken": 0.0,
            "kills": 0.0,
            "dead_penalty": 0.0,
            "ability_status": 0.0,
            "ability_hit": 0.0,
            "ability_combo": 0.0,
            "defusing": 0.0,
            "defuse_success": 0.0,
            "round_outcome": 0.0,
            "ability_waste_penalty": 0.0
        }

    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        self.reward_given = False
        
        self._reset_reward_breakdown()
        self.last_damage_dealt = 0
        self.last_damage_taken = 0
        self.continuous_defuse_ticks = 0
        
        # 🛠️ 距離履歴の完全初期化
        self.last_individual_distances = {i: 999.0 for i in range(self.num_agents)}
        self.last_avg_distance = 0.0
        
        # 💡 【硬直対策】スタック情報の完全リセット
        self.last_agent_positions = {i: (0, 0) for i in range(self.num_agents)}
        self.agent_stuck_ticks = {i: 0 for i in range(self.num_agents)}
        
        self.data_buffer = "" 
        
        reset_command = [{
            "action_type": "ResetEnvironment",
            "target_agent_id": -1,
            "grid_x": 0,
            "grid_y": 0,
            "ability_target_x": 0,
            "ability_target_y": 0
        }]
        send_data = json.dumps(reset_command) + "\n"
        self.client_socket.sendall(send_data.encode("utf-8"))
        print("[🔄 Reset] Unityに環境リセット命令（ResetEnvironment）を送信しました。")

        raw_data = self._receive_unity_data()
        
        self.experiment_name = raw_data.get("experiment_name", "fps_ai_v2_default")
        self.start_from_scratch = raw_data.get("start_from_scratch", True)
        
        obs = self._parse_observation(raw_data)
        info = {"tick": raw_data.get("current_tick", 0)}
        self.last_win_probability = raw_data.get("duel_info", {}).get("win_probability_expected", 0.5)
        
        # 🛠️ 初期の距離状況を正しくセット
        self.last_avg_distance = self._get_current_avg_distance(raw_data)
        
        return obs, info

    def _get_current_avg_distance(self, raw_data):
        game_state = raw_data.get("game_state", {})
        bspike_planted = game_state.get("bspike_planted", False)
        
        if not bspike_planted:
            return 0.0
            
        spike_pos = game_state.get("spike_grid_pos", {"x": 0, "y": 0})
        spike_x, spike_y = spike_pos.get("x", 0), spike_pos.get("y", 0)
        
        ai_agents = raw_data.get("my_agents", [])
        total_distance = 0
        active_agents = 0
        for agent in ai_agents:
            if not agent.get("is_dead", False):
                pos = agent.get("grid_pos", {"x": 0, "y": 0})
                total_distance += (abs(pos.get("x", 0) - spike_x) + abs(pos.get("y", 0) - spike_y))
                active_agents += 1
        return total_distance / active_agents if active_agents > 0 else 0.0

    def step(self, action):
        self.last_action = np.array(action)

        # --- 🌐 1. Unityに行動(JSON)を送信 ---
        commands = []
        for agent_idx in range(self.num_agents):
            base_idx = agent_idx * 3
            act_type = int(action[base_idx])
            gx = int(action[base_idx + 1])
            gy = int(action[base_idx + 2])
            
            action_string = "Stay"
            if act_type == 0: action_string = "Move"
            elif act_type == 2: action_string = "Paranoia"
            elif act_type == 3: action_string = "ReconBolt"
            elif act_type == 4: action_string = "Defuse"
            else: action_string = "Stay"

            relative_x = gx - 1
            relative_y = gy - 1

            commands.append({
                "action_type": action_string,
                "target_agent_id": int(agent_idx + 5), 
                "grid_x": int(relative_x),
                "grid_y": int(relative_y),
                "ability_target_x": int(relative_x),
                "ability_target_y": int(relative_y)
            })
            
            # 各エージェントへの個別命令確認デバッグ
            print(f"[AI命令] エージェント {agent_idx+5} -> 行動: {action_string}, 送信座標:(X:{relative_x}, Y:{relative_y})")
            
        send_data = json.dumps(commands) + "\n"
        self.client_socket.sendall(send_data.encode("utf-8"))

        # --- 🌐 2. Unityから最新のゲーム状態を受信 ---
        unity_data = self._receive_unity_data()
        game_state = unity_data.get("game_state", {})

        # --- 🎁 3. 報酬計算 ---
        reward = 0.0
        
        base_reward = self._calculate_reward(unity_data)
        reward += base_reward

        # ダメージ報酬の差分計算
        current_damage_dealt = unity_data.get("damage_dealt", 0)
        current_damage_taken = unity_data.get("damage_taken", 0)
        tick_damage_dealt = max(0, current_damage_dealt - self.last_damage_dealt)
        tick_damage_taken = max(0, current_damage_taken - self.last_damage_taken)
        self.last_damage_dealt = current_damage_dealt
        self.last_damage_taken = current_damage_taken

        if tick_damage_dealt > 0:
            val = tick_damage_dealt * 0.1
            reward += val
            self.round_reward_breakdown["damage_dealt"] += val
        if tick_damage_taken > 0:
            val = tick_damage_taken * 0.1
            reward -= val
            self.round_reward_breakdown["damage_taken"] -= val

        # キル・死亡報酬
        if unity_data.get("is_kill", False):
            reward += 50.0
            self.round_reward_breakdown["kills"] += 50.0

        if unity_data.get("is_dead", False):
            if not game_state.get("bspike_planted", False):
                reward -= 50.0
                self.round_reward_breakdown["dead_penalty"] -= 50.0

        # アビリティ状態異常・命中ボーナス
        if unity_data.get("enemy_has_status_blind", False):
            reward += 0.1
            self.round_reward_breakdown["ability_status"] += 0.1
        if unity_data.get("enemy_has_status_reveal", False):
            reward += 0.05
            self.round_reward_breakdown["ability_status"] += 0.05

        if unity_data.get("is_enemy_blinded", False):
            reward += 5.0
            self.round_reward_breakdown["ability_hit"] += 5.0
        if unity_data.get("is_enemy_revealed", False):
            reward += 2.5
            self.round_reward_breakdown["ability_hit"] += 2.5

        if unity_data.get("is_kill", False) and (unity_data.get("enemy_has_status_blind", False) or unity_data.get("enemy_has_status_reveal", False)):
            reward += 20.0
            self.round_reward_breakdown["ability_combo"] += 20.0

        # 🛠️ スパイク解除（Defuse）への加速インセンティブ報酬
        is_anyone_defusing = unity_data.get("is_defusing", False) or any(a.get("is_defusing", False) for a in unity_data.get("my_agents", []))
        
        if is_anyone_defusing:
            self.continuous_defuse_ticks += 1
            defuse_tick_reward = 1.5 + (self.continuous_defuse_ticks * 0.1)
            reward += defuse_tick_reward
            self.round_reward_breakdown["defusing"] += defuse_tick_reward
        else:
            self.continuous_defuse_ticks = 0
            
        if unity_data.get("is_defuse_success", False):
            reward += 100.0  
            self.round_reward_breakdown["defuse_success"] += 100.0

        # 🏆 勝敗報酬判定
        win_team = game_state.get("win_team", 0)
        is_win_val = None 
        
        if not self.reward_given and win_team in [1, 2]:
            if win_team == 2:    # 防衛側（AI）勝利！
                reward += 300.0
                self.round_reward_breakdown["round_outcome"] += 300.0
                is_win_val = 1.0  
                print(f"\n[🏆AI WIN] ---------- 防衛成功！ ----------")
                
            elif win_team == 1:  # 攻撃側勝利
                is_win_val = 0.0  
                current_spike_state = str(game_state.get("spike_state", "")).lower()
                
                if current_spike_state == "exploded":
                    outcome_penalty = -200.0
                    print(f"\n[❌AI LOSE - 💥爆破敗北（ペナルティ緩和:-200）]")
                else:
                    outcome_penalty = -10.0
                    print(f"\n[❌AI LOSE - 💀全滅敗北]")
                
                reward += outcome_penalty
                self.round_reward_breakdown["round_outcome"] += outcome_penalty
                
            self.reward_given = True

        # --- 🧠 4. 状態のパース ---
        obs = self._parse_observation(unity_data)
        terminated = game_state.get("is_round_end", False) or unity_data.get("is_round_end", False)
        truncated = False
        info = {"tick": unity_data.get("current_tick", 0)}
        if is_win_val is not None:
            info["is_win"] = is_win_val


        if terminated:
            total_round_reward = sum(self.round_reward_breakdown.values())
            print("\n" + "="*60)
            print(f"📊 [ROUND END BREAKDOWN] - 実験名: {self.experiment_name}")
            print(f"   Total Round Reward (Sum): {total_round_reward:+.2f}")
            print("-"*60)
            for k, v in self.round_reward_breakdown.items():
                if v != 0.0: 
                    print(f"   {k.ljust(25)} : {v:+.2f}")
            print("="*60 + "\n")

        # ==============================================================================
        # 🔍 [データ整合性検証用デバッグログ]
        # ==============================================================================
        current_tick = info.get("tick", 0)
        if current_tick % 10 == 0 or terminated:
            print(f"\n--- 🛰️ [DATA INTEGRITY CHECK (Tick: {current_tick})] ---")
            
            # 1. スパイク情報の同期チェック
            spike_planted_py = game_state.get("bspike_planted", False)
            spike_pos = game_state.get("spike_grid_pos", {"x": 0, "y": 0})
            print(f"【スパイク状態】 Planted: {spike_planted_py} | 座標: (X:{spike_pos.get('x')}, Y:{spike_pos.get('y')})")
            
            # 2. 代表してエージェント9（5人目のAI, idx=4）の脳内(Observation)と生のデータを比較
            idx = 4
            base = idx * 34  # 👈 1人34次元にスケールアップ！
            
            obs_my_x, obs_my_y = obs[base], obs[base+1]
            obs_rel_sp_x, obs_rel_sp_y = obs[base+9], obs[base+10] # 相対スパイク座標
            
            # Unityから送られてきた生のJSON内の値
            ai_agents = unity_data.get("my_agents", [])
            target_agent = next((a for a in ai_agents if a.get("id") is not None and int(float(a.get("id"))) == 9), None)
            
            if target_agent:
                raw_pos = target_agent.get("grid_pos", {"x": 0, "y": 0})
                raw_dead = target_agent.get("is_dead", False)
                raw_defuse = target_agent.get("is_defusing", False)
                
                print(f"【エージェント9 (生存:{not raw_dead})】")
                print(f"  └ 自分の座標         -> AI認識:({obs_my_x:.0f}, {obs_my_y:.0f}) <=> Unity生:({raw_pos.get('x')}, {raw_pos.get('y')})")
                # 生の絶対座標との差分が、AIが認識している相対座標と合っているか計算
                calc_rel_x = spike_pos.get("x", 0) - raw_pos.get("x", 0) if spike_planted_py else 0.0
                calc_rel_y = spike_pos.get("y", 0) - raw_pos.get("y", 0) if spike_planted_py else 0.0
                print(f"  └ 相対スパイク座標   -> AI認識:({obs_rel_sp_x:.0f}, {obs_rel_sp_y:.0f}) <=> 手元計算値:({calc_rel_x:.0f}, {calc_rel_y:.0f})")
                
            else:
                print("【エージェント9】Unityデータ内にID:9が見つかりません（パースエラーの可能性あり）")

            # 3. リアルタイム報酬のトリガーチェック
            print(f"【リアルタイム報酬】 このTickの獲得報酬: {reward:+.4f} | 現在の平均距離: {self.last_avg_distance:.2f}")
            print("="*60 + "\n")

        return obs, reward, terminated, truncated, info

    def _parse_observation(self, raw_data):
        self._last_raw_data = raw_data  
        game_state = raw_data.get("game_state", {})
        ai_agents = raw_data.get("my_agents", [])         
        enemy_agents = raw_data.get("enemy_agents", [])  

        # スパイクの状態
        bspike_planted = game_state.get("bspike_planted", False)
        spike_state_val = float(game_state.get("spike_state_val", 0))  # C#側から送られてくるcurrentState数値
        
        spike_pos = game_state.get("spike_grid_pos", {"x": 0, "y": 0})
        spike_x, spike_y = spike_pos.get("x", 0), spike_pos.get("y", 0)

        dropped_spike_pos = game_state.get("dropped_spike_grid_pos", {"x": 0, "y": 0})
        dropped_x, dropped_y = dropped_spike_pos.get("x", 0), dropped_spike_pos.get("y", 0)

        total_obs = []

        for agent_idx in range(self.num_agents):
            actual_id = agent_idx + 5  
            
            target_agent = next((a for a in ai_agents if a.get("id") is not None and int(float(a.get("id"))) == actual_id), None)
            
            if target_agent and not target_agent.get("is_dead", False):
                pos = target_agent.get("grid_pos", {"x": 0, "y": 0})
                my_x, my_y = pos.get("x", 0), pos.get("y", 0)
                
                # ==========================================
                # 1. 自分自身の情報 (5要素)
                # ==========================================
                hp_norm = float(target_agent.get("hp", 100)) / 100.0
                dead_flag = 0.0 # 生きているので0.0
                has_spike = 1.0 if target_agent.get("has_spike", False) else 0.0
                
                self_obs = [float(my_x), float(my_y), hp_norm, dead_flag, has_spike]
                
                # ==========================================
                # 2. アビリティ情報 (2要素)
                # ==========================================
                paranoia = float(target_agent.get("paranoia_charges", 0))
                recon_bolt = float(target_agent.get("recon_bolt_charges", 0))
                
                ability_obs = [paranoia, recon_bolt]
                
                # ==========================================
                # 3. スパイクの情報 (7要素: すべて相対座標化)
                # ==========================================
                is_planted_or_defusing = 1.0 if bspike_planted else 0.0
                
                if bspike_planted:
                    rel_spike_x = float(spike_x - my_x)
                    rel_spike_y = float(spike_y - my_y)
                else:
                    rel_spike_x = 0.0
                    rel_spike_y = 0.0
                    
                is_dropped = 1.0 if game_state.get("spike_state", "") == "Dropped" else 0.0
                if is_dropped == 1.0:
                    rel_dropped_x = float(dropped_x - my_x)
                    rel_dropped_y = float(dropped_y - my_y)
                else:
                    rel_dropped_x = 0.0
                    rel_dropped_y = 0.0
                
                spike_obs = [
                    spike_state_val, 
                    is_planted_or_defusing, 
                    rel_spike_x, 
                    rel_spike_y, 
                    is_dropped, 
                    rel_dropped_x, 
                    rel_dropped_y
                ]
                
                # ==========================================
                # 4. 敵・味方の情報 (常に最大4人分固定: 計20要素)
                # ==========================================
                other_agents_obs = []
                added_count = 0
                max_others = 4
                
                # ① 味方エージェントのループ（自分以外）
                for other in ai_agents:
                    if other.get("id") is not None and int(float(other.get("id"))) == actual_id:
                        continue # 自分はスキップ
                    if added_count >= max_others:
                        break
                        
                    other_pos = other.get("grid_pos", {"x": 0, "y": 0})
                    rel_other_x = float(other_pos.get("x", 0) - my_x)
                    rel_other_y = float(other_pos.get("y", 0) - my_y)
                    other_hp = float(other.get("hp", 0)) / 100.0
                    other_dead = 1.0 if other.get("is_dead", False) else 0.0
                    
                    # 味方なので関係は「1.0」
                    other_agents_obs.extend([1.0, rel_other_x, rel_other_y, other_hp, other_dead])
                    added_count += 1
                
                # ② 敵エージェントのループ
                for enemy in enemy_agents:
                    if added_count >= max_others:
                        break
                        
                    enemy_pos = enemy.get("grid_pos", {"x": 0, "y": 0})
                    rel_enemy_x = float(enemy_pos.get("x", 0) - my_x)
                    rel_enemy_y = float(enemy_pos.get("y", 0) - my_y)
                    enemy_hp = float(enemy.get("hp", 0)) / 100.0
                    enemy_dead = 1.0 if enemy.get("is_dead", False) else 0.0
                    
                    # 敵なので関係は「-1.0」
                    other_agents_obs.extend([-1.0, rel_enemy_x, rel_enemy_y, enemy_hp, enemy_dead])
                    added_count += 1
                
                # 不足枠を0.0埋め
                remaining_slots = max_others - added_count
                for _ in range(remaining_slots):
                    other_agents_obs.extend([0.0, 0.0, 0.0, 0.0, 0.0])
                
                # エージェント単体の全観測（5 + 2 + 7 + 20 = 34次元）を結合
                agent_obs = self_obs + ability_obs + spike_obs + other_agents_obs
                
            else:
                # 死亡しているかエージェントが見つからない場合は、34次元すべてを0でパディング
                agent_obs = [0.0] * 34
                if target_agent and target_agent.get("is_dead", False):
                    # 死亡フラグだけは正しく脳へ伝える (自分自身の情報の死亡フラグ部分)
                    agent_obs[3] = 1.0 
                
            total_obs.extend(agent_obs)

        return np.array(total_obs, dtype=np.float32)

    def _calculate_reward(self, raw_data):
        reward = 0.0
        game_state = raw_data.get("game_state", {})
        ai_agents = raw_data.get("my_agents", [])
        
        if not ai_agents: 
            return 0.0
            
        bspike_planted = game_state.get("bspike_planted", False)

        if bspike_planted:
            spike_pos = game_state.get("spike_grid_pos", {"x": 0, "y": 0})
            spike_x, spike_y = spike_pos.get("x", 0), spike_pos.get("y", 0)

            total_distance = 0
            active_agents = 0
            
            # 各個エージェント個別の接近インセンティブを算出
            for agent_idx in range(self.num_agents):
                target_id = agent_idx + 5
                target_agent = next((a for a in ai_agents if a.get("id") is not None and int(float(a.get("id"))) == target_id), None)
                
                if target_agent and not target_agent.get("is_dead", False):
                    pos = target_agent.get("grid_pos", {"x": 0, "y": 0})
                    curr_x, curr_y = pos.get("x", 0), pos.get("y", 0)
                    dist = float(abs(curr_x - spike_x) + abs(curr_y - spike_y))
                    
                    total_distance += dist
                    active_agents += 1
                    
                    # ------------------------------------------------------------------
                    # 💡 【硬直対策】同じ場所に留まり続けているかをチェック
                    # ------------------------------------------------------------------
                    last_pos = self.last_agent_positions.get(agent_idx, (0, 0))
                    if (curr_x, curr_y) == last_pos:
                        self.agent_stuck_ticks[agent_idx] += 1
                    else:
                        self.agent_stuck_ticks[agent_idx] = 0
                        self.last_agent_positions[agent_idx] = (curr_x, curr_y)

                    # ------------------------------------------------------------------
                    # 🚀 個別進行度報酬 (1マス近づいた人全員に巨大ボーナス)
                    # ------------------------------------------------------------------
                    prev_dist = self.last_individual_distances.get(agent_idx, 999.0)
                    if prev_dist < 900.0:
                        delta = prev_dist - dist
                        if delta > 0:
                            prog_rew = delta * 5.0
                        elif delta < 0:
                            prog_rew = delta * 5.0
                        else:
                            # 💡 5Tickを超える硬直にはペナルティを指数関数的に重くする (最大 -5.0)
                            stuck_time = self.agent_stuck_ticks[agent_idx]
                            if stuck_time > 5:
                                prog_rew = -1.0 - min(4.0, (stuck_time - 5) * 0.5)
                            else:
                                prog_rew = -1.0
                            
                        reward += prog_rew
                        self.round_reward_breakdown["proximity_reward_or_penalty"] += prog_rew
                    
                    # 距離履歴の更新
                    self.last_individual_distances[agent_idx] = dist
                    
                    # ------------------------------------------------------------------
                    # 🚀 個別逆二乗（磁力）ボーナス
                    # ------------------------------------------------------------------
                    magnet_rew = 50.0 / ((dist + 1.0) ** 1.5)
                    reward += magnet_rew
                    self.round_reward_breakdown["proximity_reward_or_penalty"] += magnet_rew
                    
                else:
                    self.last_individual_distances[agent_idx] = 999.0
                    self.agent_stuck_ticks[agent_idx] = 0

            # チーム全体の平均距離の算出（ロギング・デバッグ用）
            if active_agents > 0:
                self.last_avg_distance = total_distance / active_agents
            else:
                self.last_avg_distance = 0.0

        # --- アビリティ無駄撃ちペナルティ ---
        if hasattr(self, "last_action") and self.last_action.size > 0:
            for agent_idx in range(self.num_agents):
                base_idx = agent_idx * 3
                if base_idx < len(self.last_action):
                    act_type = self.last_action[base_idx]
                    if act_type in [2, 3]:  
                        reward -= 0.0 
                        self.round_reward_breakdown["ability_waste_penalty"] -= 0.2

        return reward

    def close(self):
        if self.client_socket: self.client_socket.close()
        if self.server_socket: self.server_socket.close()