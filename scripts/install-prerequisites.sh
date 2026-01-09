#!/usr/bin/env bash
set -e

echo "Installing BarBox development prerequisites..."
echo ""

# 1. Check/install Homebrew (needed for other tools)
if ! command -v brew &> /dev/null; then
	echo "[1/7] Installing Homebrew..."
	/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
else
	echo "[1/7] Homebrew: already installed"
fi

# 2. Install .NET 9 SDK
if ! dotnet --version 2>/dev/null | grep -q "^9\."; then
	echo "[2/7] Installing .NET 9 SDK..."
	brew install dotnet@9
else
	echo "[2/7] .NET 9 SDK: already installed ($(dotnet --version))"
fi

# 3. Install GodotEnv (Chickensoft tool for Godot version management)
if ! command -v godotenv &> /dev/null; then
	echo "[3/7] Installing GodotEnv..."
	dotnet tool install --global Chickensoft.GodotEnv
else
	echo "[3/7] GodotEnv: already installed"
fi

# 4. Install Godot 4.4.1 with .NET support via GodotEnv
if godotenv godot list 2>/dev/null | grep -q "4.4.1"; then
	echo "[4/7] Godot 4.4.1: already installed"
else
	echo "[4/7] Installing Godot 4.4.1 (with .NET support)..."
	godotenv godot install 4.4.1
fi

# 5. Install Python 3.13 (for backend)
if python3 --version 2>/dev/null | grep -q "3\.13"; then
	echo "[5/7] Python 3.13: already installed ($(python3 --version))"
else
	echo "[5/7] Installing Python 3.13..."
	brew install python@3.13
fi

# 6. Install uv (Python package manager)
if command -v uv &> /dev/null; then
	echo "[6/7] uv: already installed"
else
	echo "[6/7] Installing uv..."
	curl -LsSf https://astral.sh/uv/install.sh | sh
fi

# 7. Install hurl (for backend integration tests)
if command -v hurl &> /dev/null; then
	echo "[7/7] hurl: already installed"
else
	echo "[7/7] Installing hurl..."
	brew install hurl
fi

echo ""
echo "Prerequisites installed!"
echo ""
echo "Next steps:"
echo "  1. Restart your terminal (for PATH updates)"
echo "  2. Run: godotenv godot list  (to verify Godot is installed)"
echo "  3. Follow GETTING_STARTED.md to complete setup"
