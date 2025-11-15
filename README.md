# BarBox Platform

A multiplayer gaming platform combining Godot 4.4 (C#) frontend with FastAPI backend for coin-operated arcade bar experiences.

## Overview

BarBox enables bars and entertainment venues to offer pay-per-play digital games on shared hardware. Players log in with their phone number, load credits, and play various games including racing, carrom, and mining.

## Architecture

### Frontend (BarBoxApp/)
- **Engine**: Godot 4.4.1 with C# (.NET)
- **Games**: Racing, Carrom (multiplayer), Mining
- **Core Services**:
  - SessionManager (authentication)
  - CreditService (balance & transactions)
  - EventService (backend communication)
  - LocationManager (box identification)

### Backend (BarBoxServices/)
- **Framework**: FastAPI (Python)
- **Database**: SQLAlchemy + SQLite/PostgreSQL
- **API**: RESTful endpoints for accounts, credits, sessions, events

### Session Architecture
The platform uses a **two-tier session model**:
- **UserSession** (Frontend) - Authentication (login → logout)
- **ActivitySession** (Backend) - Gameplay tracking (game launch → exit)

See [Session Architecture Documentation](docs/architecture/sessions.md) for details.

## Quick Start

The BarBox app **automatically starts the backend** if it's not already running. Just launch the frontend and the backend will start in the background.

### Backend Setup (One-Time)

First time only, set up the backend dependencies:

```bash
cd BarBoxServices
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

### Launch Application

1. Install Godot 4.4.1
2. Open `BarBoxApp/project.godot` in Godot Editor
3. Build C# solution (Build → Build Solution)
4. Run the project (F5)

**The backend will auto-start** on port 8000 if not already running. You'll see:
- "Starting backend via: .../scripts/dev.sh"
- "Backend started successfully at 127.0.0.1:8000"

### Manual Backend Control (Optional)

If you prefer to manage the backend manually:

```bash
cd BarBoxServices

# Start backend (runs on http://127.0.0.1:8000)
sh scripts/dev.sh
```

The backend persists after app shutdown for faster subsequent launches.

## Development Workflow

### Running Tests

#### Backend Tests (Hurl)
```bash
cd BarBoxServices
sh scripts/test-backend.sh  # Starts test backend on port 8001
```

#### Frontend Tests (GoDotTest)
```bash
cd BarBoxApp
sh scripts/run-tests.sh      # All tests
sh scripts/run-tests.sh backend    # Backend integration tests only
sh scripts/run-tests.sh frontend   # Frontend unit tests only
```

See [Testing Documentation](BarBoxApp/Tests/README.md) for comprehensive testing guide.

### Building
```bash
# Backend
cd BarBoxServices
pytest  # Run backend unit tests

# Frontend
cd BarBoxApp
dotnet build BarBox.csproj  # Build C# project
# OR use Godot Editor: Project → Tools → C# → Build Solution
```

## Project Structure

```
barbox/
├── BarBoxApp/              # Godot frontend
│   ├── _Core/             # Core systems & services
│   ├── _Games/            # Individual game implementations
│   ├── Tests/             # GoDotTest test suites
│   └── scripts/           # Build & test scripts
├── BarBoxServices/        # FastAPI backend
│   ├── src/bxctl/         # Main application code
│   ├── test/              # Backend tests (Hurl)
│   └── scripts/           # Dev & test scripts
├── docs/                  # Architecture documentation
│   └── architecture/
│       ├── sessions.md    # Session architecture guide
│       └── session-refactoring-2024-11.md  # Recent refactoring
└── GodotDocs_4_4_1/      # Local Godot documentation
```

## Key Technologies

### Frontend
- **Godot 4.4.1** - Game engine with C# support
- **ZLinq** - Zero-allocation LINQ alternative (use sparingly, only for hot paths)
- **GoDotTest** - Testing framework for Godot
- **Odin Inspector** - Enhanced editor serialization

### Backend
- **FastAPI** - Modern Python web framework
- **SQLAlchemy** - ORM for database operations
- **Pydantic** - Data validation
- **Hurl** - Integration testing tool

## Recent Changes

### Session Architecture Refactoring (Nov 2024)
Major architectural improvement separating authentication from gameplay:
- Implemented two-tier session model (UserSession + ActivitySession)
- Extracted CreditService from SessionManager for single responsibility
- Added comprehensive testing infrastructure (GoDotTest + Hurl)
- Added game_tag field for efficient leaderboard queries

See [Session Refactoring Summary](docs/architecture/session-refactoring-2024-11.md) for full details.

## Game Integration

### Adding a New Game
1. Create game scene and scripts in `BarBoxApp/_Games/YourGame/`
2. Implement ActivitySession lifecycle:
   ```csharp
   // On game start
   var sessionId = await _eventService.CreateActivitySessionAsync(
       boxId: _locationManager.BoxId,
       playerId: userSession.PlayerId,
       gameTag: "your_game"
   );

   // On game end
   await _eventService.CloseActivitySessionAsync(sessionId);
   ```
3. Integrate credit checks using CreditService
4. Add game to GameHost orchestrator
5. Create test suite in `Tests/Games/YourGame/`

See [Session Architecture Guide](docs/architecture/sessions.md) for detailed patterns.

## Contributing

### Code Style
- **C#**: PascalCase for classes/methods, camelCase for private fields with underscore prefix
- **Python**: PEP 8 style guide
- **Performance**: Prefer System.Linq for readability; use ZLinq only for proven bottlenecks in hot paths
- **Testing**: All new features require tests (GoDotTest for frontend, pytest for backend)

See [CLAUDE.md](CLAUDE.md) for comprehensive coding guidelines.

### Commit Guidelines
- Write clear, concise commit messages
- Separate commits by feature/concern for easy review
- Include relevant test updates in feature commits
- Update documentation when adding new features

## API Documentation

### Backend Endpoints
```
GET  /alive                          # Health check
POST /account/create                 # Create account
GET  /account/{player_id}/credits    # Get credit balance
POST /account/{player_id}/credits    # Add/spend credits
PUT  /box/{box_id}/session/{id}?game_tag={tag}  # Create game session
POST /box/session/{id}/close         # Close game session
POST /events                         # Emit gameplay event
```

### Frontend Services
- **SessionManager**: User authentication and session management
- **CreditService**: Balance queries and transactions with smart caching
- **EventService**: Backend communication and event streaming
- **LocationManager**: Box identification and configuration
- **GameHost**: Game orchestration and lifecycle management

## Troubleshooting

### "Backend Service Not Running" Error

If the backend fails to auto-start, you'll see this error:

```
╔════════════════════════════════════════════════════════╗
║  Backend Service Not Running                           ║
╠════════════════════════════════════════════════════════╣
║  Backend auto-start failed.                            ║
║                                                        ║
║  Possible causes:                                      ║
║    - Backend script not found                          ║
║    - Port 8000 blocked by firewall                     ║
║    - Python dependencies missing                       ║
╚════════════════════════════════════════════════════════╝
```

**Solution**:
1. Click "Retry Connection" button in the app (auto-start will try again)
2. If retry fails, check Godot console for detailed error messages
3. Manually start backend to troubleshoot: `cd BarBoxServices && sh scripts/dev.sh`

**Common causes**:
- **Port 8000 occupied**: Another process using the port (backend auto-kills stale processes but may miss some)
- **Python dependencies missing**: Run `pip install -r requirements.txt` in BarBoxServices
- **Backend script not found**: Verify `BarBoxServices/scripts/dev.sh` exists
- **Permissions**: Script may not be executable (`chmod +x BarBoxServices/scripts/dev.sh`)

### Backend won't start
- **Port 8000 in use**: Kill the process using `lsof -i :8000` to find PID, then `kill <PID>`
- **Database locked**: Close any other backend instances
- **Dependencies missing**: Run `pip install -r requirements.txt` in virtual environment
- **Python version**: Requires Python 3.13+

### Frontend build errors
- Rebuild C# solution: `dotnet clean && dotnet build`
- Clear Godot cache: Delete `.godot/` directory
- Verify Godot version: Should be 4.4.1

### Tests failing
- **Backend tests**: Ensure test backend is running on port 8001 (not production 8000)
- **Frontend tests**: Run `sh scripts/run-tests.sh` which manages test backend automatically
- **Connection errors**: Check both production (8000) and test (8001) backends are not conflicting
- **Database issues**: Clear test database: `rm /tmp/barbox-test.db`

## License

Proprietary - All rights reserved

## Contact

For questions or support, contact the development team.
