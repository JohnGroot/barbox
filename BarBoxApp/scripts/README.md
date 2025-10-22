# BarBoxApp Development Scripts

Helper scripts for local BarBox development.

## setup-env.sh

Creates your local `.env.local` configuration for development.

### Usage

```bash
cd /Users/johngroot/Dev/barbox/BarBoxApp
sh scripts/setup-env.sh
```

### What it does

- Generates a unique Box ID (UUID) for your machine
- Prompts for Location ID (defaults to your machine name)
- Prompts for Backend URL (defaults to `http://localhost:8000`)
- Creates `.env.local` in the project root
- Optionally starts the backend service

### Example

```
Enter Location ID (default: johns_macbook): dev_station_1
Enter Backend URL (default: http://localhost:8000): [press Enter]

Created .env.local with:
  Box ID:      a1b2c3d4-e5f6-7890-abcd-ef1234567890
  Location:    dev_station_1
  Backend URL: http://localhost:8000

Start backend service now? (y/N):
```

### Generated .env.local

```bash
BARBOX_BOX_ID=a1b2c3d4-e5f6-7890-abcd-ef1234567890
BARBOX_LOCATION_ID=dev_station_1
BARBOX_BACKEND_URL=http://localhost:8000
```

### Key Features

- Auto-generates unique Box IDs
- Sensible defaults (just press Enter)
- Safe overwrite protection
- Optional backend startup

### Common Issues

**Permission denied**
```bash
chmod +x scripts/setup-env.sh
```

**Script not found**
```bash
# Make sure you're in BarBoxApp directory
cd /Users/johngroot/Dev/barbox/BarBoxApp
```

**UUID generation fails**
- Requires `uuidgen`, `python3`, or `python`

### Manual Setup

If you prefer manual setup:

```bash
# Copy example file
cp .env.example .env.local

# Generate UUID
uuidgen  # or: python3 -c "import uuid; print(uuid.uuid4())"

# Edit .env.local with your values
```
