# Scene Editing Guidelines

Guidelines for safely editing TSCN scene files.

## TSCN File Structure (must appear in order)

1. **File descriptor** - `[gd_scene load_steps=X format=3 uid="..."]`
2. **External resources** - `[ext_resource ...]`
3. **Internal resources** - `[sub_resource ...]`
4. **Nodes** - `[node ...]`
5. **Connections** - `[connection ...]`

## Safe Editing Practices

- **Maintain section order** - resources before nodes, nodes before connections
- **Keep load_steps accurate** - increment when adding resources
- **Verify node hierarchy** - ensure parent paths are correct
- **Test scene loading** in Godot editor after changes

## Critical Elements

- External resources must appear before being referenced
- Use correct ID format: `script = ExtResource("1_abc123")`
- Child nodes reference parent with relative path: `parent="."`
- `unique_name_in_owner = true` enables `%NodeName` access in scripts
- `node_paths` arrays must exactly match `NodePath` declarations

## Common Pitfalls

- **Incorrect load_steps** - causes loading bar issues
- **Wrong resource order** - breaks loading
- **Mismatched NodePath references** - breaks script functionality
- **Corrupted UIDs** - use existing UIDs, don't generate manually
- **Control node layouts** - anchor/margin values are interdependent

## Emergency Recovery

1. Check console for specific error messages
2. Verify file descriptor syntax
3. Ensure all referenced resources exist
4. Validate node hierarchy and parent paths

## Project-Specific Notes

- Scripts use exported NodePath properties for UI connections
- Main scenes follow UI/Panel/Container hierarchy pattern
- `unique_name_in_owner` used for script-accessible nodes
- Use editor for complex modifications; manual edits for simple property changes only
