# Viana.Sentinel - Resilient Update Agent

Viana.Sentinel is a robust, clean-architecture update agent designed to manage application updates with atomicity, rollback capabilities, and resilience against network failures.

## 🚀 What It Has (Implemented Features)

The current MVP (Minimum Viable Product) includes the following core components:

### 1. Robust Download Engine (`ResumableDownloader`)

- **Resumable Downloads**: Automatically resumes interrupted downloads using HTTP Range headers.
- **Partial File Handling**: Uses `.partial` files to prevent corrupted states.
- **Resilience**: Smart retry logic with exponential backoff for network glitches.
- **Validation**: Automatic SHA256 hash verification upon completion.
- **Progress Tracking**: Real-time reporting of download speed, percentage, and bytes.

### 2. Update Logic Engine (`ManifestManager`)

- **Differential Updates**: Calculates Delta updates by comparing local file MD5 hashes against a remote `manifest.json`.
- **Efficiency**: Only downloads files that have changed or are missing.

### 3. Safety & Atomicity (`RollbackManager`)

- **Atomic Swaps**: Updates are applied instantly by swapping `Staging` and `Active` directories.
- **Automated Backups**: Creates a timestamped backup before every update.
- **Auto-Rollback**: If an update fails (e.g., service won't start), it automatically reverts the file system to the previous working state.
- **Service Control**: Manages the Windows Service lifecycle (Stop -> Update -> Start).

### 4. Background Worker (`Viana.Sentinel.Service`)

- **Continuous Monitoring**: Checks for updates every 5 minutes.
- **Orchestration**: Coordindates the Check -> Download -> Swap -> Restart lifecycle.

### 5. Verification Tool (`Viana.TestApp`)

- **WPF UI**: A graphical tool to visually test key components without running the full service.
- **Manifest Tester**: Point to a manifest URL and local folder to see which files would update.
- **Rollback Tester**: Manually trigger atomic swaps and backups to verify file system reliability.

---

## 🛠️ What It Needs (Missing/Future Work)

To become a production-ready solution, the following items are required:

### 1. Configuration (`appsettings.json`)

- **Current State**: URLs and Paths are hardcoded in `Worker.cs` and `MainWindow.xaml.cs`.
- **Need**: Move `ManifestUrl`, `BaseUpdateUrl`, and local paths to a configuration file.

### 2. Remote Control (`MqttService`)

- **Current State**: The agent runs on a fixed timer loop.
- **Need**: Implement MQTT connectivity to allow:
  - Real-time "Force Update" commands.
  - Pause/Resume functionality.
  - Reporting usage stats and version info back to the cloud.

### 3. Backend Server

- **Current State**: Expects a server at `http://localhost:5000`.
- **Need**: A static file server (IIS/Nginx/S3) hosting:
  - `manifest.json`: The source of truth for the current version.
  - `/modules/`: Directory containing the actual files.

### 4. Installer

- **Current State**: Runs as a console app or development build.
- **Need**: A WiX or Squirrel installer to register `Viana.Sentinel.Service` as a legitimate Windows Service.

---

## 📖 How To Use

### Prerequisites

- .NET 8.0 SDK
- Windows OS (for ServiceController support)

### Option A: Run the Visual Test App

Use this to verify the download and rollback logic visually.

```powershell
dotnet run --project Viana.TestApp
```

1.  **Resumable Downloader Tab**: Enter a URL (e.g., a large test file) and Start. Stop/Resume to test resilience.
2.  **Manifest Manager Tab**: Enter a Manifest URL (e.g., `http://localhost:5000/manifest.json`) to see what files would update.
3.  **Rollback Manager Tab**: Select "Active" and "Staging" folders to test atomic swaps.

### Option B: Run the Sentinel Service

Run the agent in console mode to simulate the background process.

```powershell
dotnet run --project Viana.Sentinel.Service
```

- The worker will start and check for updates continuously (every 5 mins).
- Logs are output to the console.

### Option C: Mocking a Server (For Testing)

To test update detection, you can run a simple Python HTTP server in a directory containing your update files:

```bash
# In your update-source folder
python -m http.server 5000
```

Ensure a `manifest.json` exists at the root of that folder.

## 📂 Project Structure

- `Viana.Core`: Shared interfaces (`IDownloader`, `IUpdateManager`) and Models (`AppManifest`).
- `Viana.Infrastructure`: Concrete logic implementation (`ResumableDownloader`, `ManifestManager`, `RollbackManager`).
- `Viana.Sentinel.Service`: The Windows Worker Service integration.
- `Viana.TestApp`: WPF GUI for developer testing.
