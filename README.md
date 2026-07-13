# BarBox Platform

The Touchtunes of Modern Arcade Games

## New Developer?

See **[GETTING_STARTED.md](./GETTING_STARTED.md)** for setup instructions.

## Overview

Barbox is a local-first, location-based, game network. We're on constrained, accessible, touchscreen-based games.
BoxTeam is building the infrastructure for user persistence, transactions and game hosting as well as the flagship games for the platform.

## Architecture

### Frontend (BarBoxApp/)
- **Engine**: Godot 4.7 with C# (.NET 9)

### Backend (BarBoxServices/)
- **Framework**: FastAPI (Python)
- **Database**: SQLAlchemy + SQLite
- **API**: RESTful endpoints for accounts, credits, sessions, events

### Session Architecture
The platform uses a **two-tier session model**:
- **UserSession** (Frontend) - Authentication (login → logout)
- **ActivitySession** (Backend) - Gameplay tracking (game launch → exit)

See [Session Architecture Documentation](docs/architecture/sessions.md) for details.

## Quick Start

The BarBox app **automatically starts the backend** if it's not already running when launched from the Godot editor.

### Backend Setup (One-Time)

First time only, set up the backend dependencies:

```bash
cd BarBoxServices
python3 -m venv .venv
source .venv/bin/activate
uv pip install -e .
```

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
│       └── sessions.md    # Session architecture guide
└── GodotDocs_4_6/        # Local Godot documentation (4.6; project is on 4.7)
```

## Key Technologies

### Frontend
- **Godot 4.7** - Game engine with C# support
- **GoDotTest** - Testing framework for Godot by Chickensoft

### Backend
- **FastAPI** - Modern Python web framework
- **SQLAlchemy** - ORM for database operations
- **Pydantic** - Data validation
- **Hurl** - Integration testing tool

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
5. Create test suite in `Games/YourGame/Tests`

See [Session Architecture Guide](docs/architecture/sessions.md) for detailed patterns.

## API Documentation

### Backend Endpoints
```
GET  /alive                          # Health check
POST /player/                        # Create player account
POST /player/validate                # Pre-flight validation
POST /box/                           # Register box
PUT  /box/{box_id}/session/{id}      # Create game session
POST /box/session/{id}               # Add session event
GET  /game/{tag}/leaderboard         # Game leaderboard
```

### Frontend Services
- **SessionManager**: User authentication and session management
- **CreditService**: Balance queries and transactions with smart caching
- **SessionEventService**: Backend communication and event streaming
- **LocationManager**: Box identification and configuration
- **GameHost**: Game orchestration and lifecycle management

## Troubleshooting

### Backend won't start
- **Port 8000 in use**: Kill the process using `lsof -i :8000` to find PID, then `kill <PID>`
- **Database locked**: Close any other backend instances
- **Dependencies missing**: Run `uv pip install -e .` in the virtual environment (see Backend Setup above)
- **Python version**: Requires Python 3.13+

### Tests failing
- **Backend tests**: Ensure test backend is running on port 8001 (not production 8000)
- **Frontend tests**: Run `sh scripts/run-tests.sh` which manages test backend automatically
- **Connection errors**: Check both production (8000) and test (8001) backends are not conflicting
- **Database issues**: Clear test database: `rm /tmp/barbox-test.db`

## License

Proprietary - All rights reserved
