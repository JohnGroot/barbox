#!/usr/bin/env bash
set -e

echo "Installing BarBox development prerequisites..."
echo ""

# Helper function to get Homebrew prefix (Apple Silicon vs Intel)
get_brew_prefix() {
	if [[ "$(uname -m)" == "arm64" ]]; then
		echo "/opt/homebrew"
	else
		echo "/usr/local"
	fi
}

# Get the user's shell config file
get_shell_config() {
	if [[ -n "$ZSH_VERSION" ]] || [[ "$SHELL" == *"zsh"* ]]; then
		echo "$HOME/.zshrc"
	else
		echo "$HOME/.bashrc"
	fi
}

# Add a line to shell config if it doesn't already exist
add_to_shell_config() {
	local line="$1"
	local config_file
	config_file=$(get_shell_config)

	# Create config file if it doesn't exist
	if [[ ! -f "$config_file" ]]; then
		touch "$config_file"
	fi

	# Add line if not already present
	if ! grep -qF "$line" "$config_file" 2>/dev/null; then
		echo "" >> "$config_file"
		echo "# Added by BarBox install-prerequisites.sh" >> "$config_file"
		echo "$line" >> "$config_file"
		echo "  Added to $config_file: $line"
	fi
}

BREW_PREFIX=$(get_brew_prefix)

# 1. Check/install Homebrew (needed for other tools)
if ! command -v brew &> /dev/null; then
	echo "[1/9] Installing Homebrew..."
	/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
	# Source Homebrew environment for current session
	eval "$("$BREW_PREFIX/bin/brew" shellenv)"
	# Make Homebrew PATH permanent
	add_to_shell_config "eval \"\$($BREW_PREFIX/bin/brew shellenv)\""
else
	echo "[1/9] Homebrew: already installed"
fi

# 2. Install .NET 9 SDK
# Ensure dotnet@9 is in PATH (brew installs it as keg-only)
if [[ -d "$BREW_PREFIX/opt/dotnet@9/bin" ]]; then
	export PATH="$BREW_PREFIX/opt/dotnet@9/bin:$PATH"
fi

if ! dotnet --version 2>/dev/null | grep -q "^9\."; then
	echo "[2/9] Installing .NET 9 SDK..."
	brew install dotnet@9
	# Add newly installed dotnet to PATH for current session
	export PATH="$BREW_PREFIX/opt/dotnet@9/bin:$PATH"
	# Make dotnet PATH permanent
	add_to_shell_config "export PATH=\"$BREW_PREFIX/opt/dotnet@9/bin:\$PATH\""
else
	echo "[2/9] .NET 9 SDK: already installed ($(dotnet --version))"
fi

# 3. Install GodotEnv (Chickensoft tool for Godot version management)
# Ensure dotnet global tools are in PATH
export PATH="$HOME/.dotnet/tools:$PATH"

if ! command -v godotenv &> /dev/null; then
	echo "[3/9] Installing GodotEnv..."
	dotnet tool install --global Chickensoft.GodotEnv
	# Make dotnet tools PATH permanent
	add_to_shell_config 'export PATH="$HOME/.dotnet/tools:$PATH"'
else
	echo "[3/9] GodotEnv: already installed"
fi

# 4. Install Godot 4.6 with .NET support via GodotEnv
if godotenv godot list 2>/dev/null | grep -q "4.6"; then
	echo "[4/9] Godot 4.6: already installed"
else
	echo "[4/9] Installing Godot 4.6 (with .NET support)..."
	godotenv godot install 4.6.0
fi

# 5. Install Godot 4.6 export templates (not included with GodotEnv)
GODOT_VERSION="4.6"
TEMPLATE_VERSION="4.6.stable.mono"
if [[ "$(uname)" == "Darwin" ]]; then
	TEMPLATE_DIR="$HOME/Library/Application Support/Godot/export_templates/$TEMPLATE_VERSION"
else
	TEMPLATE_DIR="$HOME/.local/share/godot/export_templates/$TEMPLATE_VERSION"
fi

if [[ -d "$TEMPLATE_DIR" ]]; then
	echo "[5/9] Godot export templates: already installed"
else
	echo "[5/9] Installing Godot $GODOT_VERSION export templates (~1.2 GB download)..."
	TEMPLATE_URL="https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/Godot_v${GODOT_VERSION}-stable_mono_export_templates.tpz"
	TEMPLATE_TMP="/tmp/godot_export_templates.tpz"
	curl -L -o "$TEMPLATE_TMP" "$TEMPLATE_URL"
	mkdir -p "$TEMPLATE_DIR"
	unzip -o "$TEMPLATE_TMP" -d /tmp/godot_templates_extract
	mv /tmp/godot_templates_extract/templates/* "$TEMPLATE_DIR/"
	rm -rf "$TEMPLATE_TMP" /tmp/godot_templates_extract
	echo "  Export templates installed to: $TEMPLATE_DIR"
fi

# 6. Install Python 3.13 (for backend)
if python3 --version 2>/dev/null | grep -q "3\.13"; then
	echo "[6/9] Python 3.13: already installed ($(python3 --version))"
else
	echo "[6/9] Installing Python 3.13..."
	brew install python@3.13
fi

# 7. Install uv (Python package manager)
if command -v uv &> /dev/null; then
	echo "[7/9] uv: already installed"
else
	echo "[7/9] Installing uv..."
	curl -LsSf https://astral.sh/uv/install.sh | sh
	# Source uv environment for current session
	if [[ -f "$HOME/.local/bin/env" ]]; then
		source "$HOME/.local/bin/env"
	else
		export PATH="$HOME/.local/bin:$PATH"
	fi
	# Make uv PATH permanent (uv installer may already do this, but ensure it)
	add_to_shell_config 'export PATH="$HOME/.local/bin:$PATH"'
fi

# 8. Install hurl (for backend integration tests)
if command -v hurl &> /dev/null; then
	echo "[8/9] hurl: already installed"
else
	echo "[8/9] Installing hurl..."
	brew install hurl
fi

# 9. Set up Backend Python Environment
echo ""
echo "Setting up BarBoxServices backend..."

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
BACKEND_DIR="$REPO_ROOT/BarBoxServices"
BACKEND_VENV="$BACKEND_DIR/.venv"
BACKEND_ENV="$BACKEND_DIR/.env"

if [[ -d "$BACKEND_VENV" ]]; then
	echo "[9/9] Backend venv: already exists"
else
	echo "[9/9] Creating backend virtual environment..."
	python3 -m venv "$BACKEND_VENV"
fi

# Install dependencies
echo "  Installing backend dependencies..."
source "$BACKEND_VENV/bin/activate"
uv pip install -e "$BACKEND_DIR" --quiet
deactivate

# Create .env from .env.example if not exists
if [[ ! -f "$BACKEND_ENV" ]] && [[ -f "$BACKEND_DIR/.env.example" ]]; then
	cp "$BACKEND_DIR/.env.example" "$BACKEND_ENV"
	echo "  Created backend .env from .env.example"
elif [[ -f "$BACKEND_ENV" ]]; then
	echo "  Backend .env: already exists"
fi

echo ""
echo "Prerequisites installed!"

# Run BarBoxApp environment setup
SETUP_ENV_SCRIPT="$REPO_ROOT/BarBoxApp/scripts/setup-env.sh"

if [[ -f "$SETUP_ENV_SCRIPT" ]]; then
	echo ""
	echo "Running BarBoxApp environment setup..."
	bash "$SETUP_ENV_SCRIPT"
else
	echo ""
	echo "Warning: setup-env.sh not found at: $SETUP_ENV_SCRIPT"
	echo ""
	echo "Next steps:"
	echo "  1. Run: godotenv godot list  (to verify Godot is installed)"
	echo "  2. Run: BarBoxApp/scripts/setup-env.sh  (to configure .env.local)"
	echo "  3. Follow GETTING_STARTED.md to complete setup"
fi

echo ""
echo "Note: PATH changes have been added to $(get_shell_config)."
echo "Run 'source $(get_shell_config)' or restart your terminal to use them."
