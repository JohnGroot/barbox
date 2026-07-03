# Getting Started with BarBox Development

This guide will get you from zero to running the project in Godot.

## Prerequisites

### Automated Installation (Recommended)

Run the installer script:

```bash
./scripts/install-prerequisites.sh
```

This installs:
- .NET 9 SDK
- GodotEnv (Godot version manager)
- Godot 4.7 with .NET support
- Python 3.13
- uv (Python package manager)
- hurl (for backend tests)

### Manual Installation

If you prefer to install manually:

1. **.NET 9 SDK**: https://dot.net/download
2. **GodotEnv**: `dotnet tool install --global Chickensoft.GodotEnv`
3. **Godot 4.7**: `godotenv godot install 4.7.0`
4. **Python 3.13+**: https://python.org or `brew install python@3.13`
5. **uv**: `curl -LsSf https://astral.sh/uv/install.sh | sh`

## Quick Setup

### 1. Backend Setup (One-Time)

```bash
cd BarBoxServices
python3 -m venv .venv
source .venv/bin/activate
uv pip install -e .
cp .env.example .env
```

### 2. Frontend Environment (One-Time)

```bash
cd BarBoxApp
sh scripts/setup-env.sh
```

This creates `.env.local` with default development values.

## Running the Project

### Option A: Auto-Start Backend (Recommended)

The app automatically starts the backend when launched from the editor:

1. Open Godot 4.7: `godotenv godot`
2. Import/open the `BarBoxApp` project
3. Press **F5** to run - backend starts automatically

### Option B: Manual Backend

If you prefer to manage the backend separately:

```bash
# Terminal 1: Start backend
cd BarBoxServices
sh scripts/dev.sh

# Terminal 2: Open Godot
cd BarBoxApp
godotenv godot
```

## Verifying Setup

1. **Backend running**: Visit http://localhost:8000/docs (Swagger UI)
2. **Frontend works**: Run project in Godot, should connect to backend

## GodotEnv Commands

```bash
godotenv godot             # Open active Godot version
godotenv godot list        # List installed versions
godotenv godot use 4.7.0   # Switch active version
godotenv godot install X   # Install version X
```

## Common Issues

### Godot not found or wrong version

```bash
godotenv godot list          # Check installed versions
godotenv godot use 4.7.0     # Switch to correct version
```

### Port 8000 already in use

```bash
lsof -i :8000                # Find what's using it
kill <PID>                   # Stop it
```

### NuGet package restore fails

```bash
cd BarBoxApp
dotnet restore --force
```

### Backend won't start

```bash
cd BarBoxServices
rm app.db                    # Reset database
sh scripts/dev.sh            # Restart
```

### Python version issues

The backend requires Python 3.13+. Check your version:

```bash
python3 --version
```

If too old, install via brew: `brew install python@3.13`

## Project Structure

```
barbox/
├── BarBoxApp/           # Godot 4.7 C# game client
└── BarBoxServices/      # FastAPI Python backend
```

## Next Steps

- Read `BarBoxApp/Tests/README.md` for testing info
