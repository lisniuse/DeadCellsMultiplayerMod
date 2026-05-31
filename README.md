<div align="center">

English • [Русский](README_ru.md)
  
</div>
<h1>Dead Cells Multiplayer Mod</h1>

**DeadCellsMultiplayerMod** is a **multiplayer / co-op mod for Dead Cells**, built using the **Dead Cells Core Modding API (DCCM)**.

The mod adds **co-op / multiplayer gameplay** via a **local or virtual network**:  
one player hosts a server, another connects — and both players can **play through levels together in real time**.

> ⚠️ The project is currently in development. Many core multiplayer systems are implemented, but full synchronization is still a work in progress.

---

## 🎮 Features

- ✅ Real-time synchronization between two players  
- ✅ Local TCP-based multiplayer server  
- ✅ Host / Client architecture    
- ✅ Automatic game start for connected clients  
- 🧪 Experimental multiplayer gameplay  

---

## ⭐ Support the Project

If you find this project interesting:
- ⭐ Star the repository  
- 🍴 Fork the project and experiment  

Every bit of feedback helps improve multiplayer support for **Dead Cells**.

---

## 🧰 Requirements

- **Dead Cells (PC)**
- **Dead Cells Core Modding API (DCCM)**
- Local network or virtual LAN software (for online play)

---

## 📦 Installation

## 1️⃣ Install Dead Cells Core Modding API (DCCM)

### 🔹 Steam version

If you are using the **Steam version** of the game, follow the official installation guide:

👉 [https://dead-cells-core-modding.github.io/docs/docs/tutorial/install-workshop/](https://dead-cells-core-modding.github.io/docs/docs/tutorial/install-workshop/)

This method will automatically install and keep DCCM up to date.

---

### 🔹 Non-Steam version
If you are using a **non-Steam version** of Dead Cells:

1. Download the latest release of **DCCM** from the official repository:
   👉 [https://github.com/dead-cells-core-modding/core](https://github.com/dead-cells-core-modding/core)

2. Extract the DCCM files to your Dead Cells game directory (the `coremod` folder will be created).

3. Rename `steam.hdll` to `steam.hdll.bak` in the game root directory (this triggers Goldberg emulator auto-detection).

4. Open `coremod\config\modcore.json` and ensure `EnableGoldberg` is set to `true`:
   ```json
   {
     "EnableGoldberg": true
   }
   ```

5. Install [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/current/runtime) (Desktop Runtime, Windows x64) if not already installed.


---

## 2️⃣ Install DeadCellsMultiplayerMod

### 🔹 Steam version

If you are using the **Steam version** of the game:
1. Open [https://steamcommunity.com/sharedfiles/filedetails/?id=3655044722](https://steamcommunity.com/sharedfiles/filedetails/?id=3657857836)
2. Install the mod in one click.

---

### 🔹 Non-Steam version (GOG / other store versions)

If you are using a **non-Steam version** of Dead Cells:

1. Navigate to your **DCCM directory**
2. Create a folder named `mods` (if it doesn't exist)
3. Extract the **DeadCellsMultiplayerMod** folder into the `mods` directory

Example:
```
Your game path/
 └──coremod/
    └── mods/
        └── DeadCellsMultiplayerMod/
```

#### ⚙️ Required configuration for non-Steam versions

1. **Rename `steam.hdll`** in the game root directory to `steam.hdll.bak` — this triggers DCCM to load the Goldberg Steam emulator instead of the real Steam API.

2. **Enable Goldberg** in `coremod\config\modcore.json`:
   ```json
   "EnableGoldberg": true
   ```

3. **Launch the game** via `coremod\core\host\startup\DeadCellsModding.exe` directly (not through SteamStartShell).

> ⚠️ **Limitations on non-Steam versions:**
> - **TCP/LAN multiplayer** works normally (this is the default mode)
> - **Steam P2P mode** is not available (requires a real Steam client with Steam Networking)
> - **Steam overlay "Join Game"** is not available
>
> For online play with friends, use virtual LAN software (Hamachi, Radmin VPN, ZeroTier) and connect via IP address.
Your game path/
 └──coremod/
    └── mods/
        └── DeadCellsMultiplayerMod/
```

---

### 3️⃣ Run the game via DCCM

**Steam version:** Start Dead Cells via Steam as usual. DCCM loads automatically.

**Non-Steam version:** Launch directly via `coremod\core\host\startup\DeadCellsModding.exe`.  
On the first launch, required configuration files will be generated automatically.

---

## 🕹️ How to Play (Multiplayer)

1. Launch the game via **DCCM**
2. Click **Play Multiplayer**
3. Choose **Host** or **Join**
4. Enter **IP address** and **port**
5. When the host starts the game, the client will automatically join the session

🌐 **For online play**, use one of the following virtual LAN tools:
- Hamachi  
- Radmin VPN  
- ZeroTier  

---

## 🧪 Development Status / TODO

- [x] Create second player ghost  
- [x] Synchronize new game world data  
- [x] Add player ghost animations  
- [x] Improve ghost animation quality  
- [x] Synchronize level generation  
- [x] Synchronize enemies
- [x] Implement death handling for ghost player
- [x] Implement more interactions
- [x] Synchronize bosses  

---

## 📜 Credits

- **Dead Cells Core Modding API (DCCM)**  
  https://github.com/dead-cells-core-modding/core

---



<!--
Keywords: Dead Cells multiplayer mod, Dead Cells co-op mod, Dead Cells online, DCCM mod, Dead Cells TCP multiplayer
-->
