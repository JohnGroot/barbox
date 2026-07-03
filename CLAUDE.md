# CLAUDE.md

Guidance for Claude Code agents working on the BarBox project.

## Project

**BarBox** is a physical game terminal platform for bars and venues. Location-based, touchscreen-focused arcade games with user persistence and transactions.

- **BarBoxApp** (`BarBoxApp/`) - Godot 4.7 C# game client
- **BarBoxServices** (`BarBoxServices/`) - Python FastAPI backend API

## Build & Test

```bash
# Frontend
cd BarBoxApp && dotnet build
cd BarBoxApp && bash scripts/run-tests.sh

# Backend
cd BarBoxServices && sh scripts/dev.sh       # Start dev server
cd BarBoxServices && sh scripts/test.sh      # Run integration tests
```

## Response Guidelines

- Be correct and precise; avoid sycophantic fluff
- Only agree with statements you can verify
- Make minimal, surgical changes for easy review

## Code Philosophy

- Read existing code thoroughly before modifying
- Follow the existing codebase's style and patterns
- Keep abstractions simple; avoid premature optimization
- Single responsibility; clear separation of concerns
- Plan changes and assess dependencies before implementation

## Git Guidelines

- Don't use git commands automatically; wait for permission
- No Claude signatures in commits (use repo author identity)
- Conventional commit format; focus on "why" not "what"
