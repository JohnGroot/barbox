@tool
extends PanelContainer

###########################
## SETTINGS
###########################

@export var auto_show:bool = true
@export var animate:bool = true


@export_file var custom_layout_file
@export var set_tool_tip := true
@export_group("Style")
@export var separation:Vector2i = Vector2i(0,0)
var style_background:StyleBoxFlat = null
@export var background:StyleBoxFlat = null:
	set(new_val):
		style_background = new_val
		background = new_val
	get:
		return style_background
var style_normal:StyleBoxFlat = null
@export var normal:StyleBoxFlat = null:
	set(new_val):
		style_normal = new_val
		normal = new_val
	get:
		return style_normal
var style_hover:StyleBoxFlat = null
@export var hover:StyleBoxFlat = null:
	set(new_val):
		style_hover = new_val
		hover = new_val
	get:
		return style_hover
var style_pressed:StyleBoxFlat = null
@export var pressed:StyleBoxFlat = null:
	set(new_val):
		style_pressed = new_val
		pressed = new_val
	get:
		return style_pressed
var style_special_keys:StyleBoxFlat = null
@export var special_keys:StyleBoxFlat = null:
	set(new_val):
		style_special_keys = new_val
		special_keys = new_val
	get:
		return style_special_keys
@export_group("Font")
@export var font:FontFile
@export var font_size:int = 20
@export var font_color_normal:Color = Color(1,1,1)
@export var font_color_hover:Color = Color(1,1,1)
@export var font_color_pressed:Color = Color(1,1,1)

###########################
## SIGNALS
###########################

signal layout_changed

###########################
## PANEL 
###########################

func _enter_tree():
	if not get_tree().get_root().size_changed.is_connected(size_changed):
		get_tree().get_root().size_changed.connect(size_changed)
	_init_keyboard()

func _exit_tree():
	# Disconnect signal to prevent memory leaks
	if get_tree() != null and get_tree().get_root().size_changed.is_connected(size_changed):
		get_tree().get_root().size_changed.disconnect(size_changed)

	# Disconnect field signals
	for field in _monitored_fields:
		if is_instance_valid(field):
			if field.focus_entered.is_connected(_on_field_focus_entered):
				field.focus_entered.disconnect(_on_field_focus_entered)
			if field.focus_exited.is_connected(_on_field_focus_exited):
				field.focus_exited.disconnect(_on_field_focus_exited)
	_monitored_fields.clear()

	# Free the KeyListHandler node if it exists
	if KeyListHandler != null:
		KeyListHandler.queue_free()
		KeyListHandler = null

	# Clear references
	KeyboardButton = null
	layouts.clear()
	keys.clear()
	capslock_keys.clear()

#func _process(delta):
#    pass

func _input(event):
	_update_auto_display_on_input(event)


func size_changed():
	# Recalculate size and reposition
	_update_keyboard_size()

	# Only hide if keyboard was showing
	if auto_show and visible:
		_hide_keyboard()

func _update_keyboard_size():
	var viewport_size = get_viewport().get_visible_rect().size
	var keyboard_width = viewport_size.x * 0.8  # 80% of viewport width
	var keyboard_height = viewport_size.y * 0.12  # 12% of viewport height

	# Update size
	custom_minimum_size = Vector2(keyboard_width, keyboard_height)
	size = Vector2(keyboard_width, keyboard_height)

	# Update position to remain bottom-centered
	var new_x = (viewport_size.x - keyboard_width) / 2.0
	var new_y = viewport_size.y - keyboard_height
	position = Vector2(new_x, new_y)

	# Update hide position for animation
	hide_position = Vector2(new_x, viewport_size.y + 10)

## Re-discover and monitor fields. Call when new fields are added dynamically.
## This is useful for modals that create LineEdit/TextEdit fields at runtime.
func refresh_fields():
	_discover_and_monitor_fields()

func _discover_and_monitor_fields():
	# Use groups for better performance - fields must be added to "keyboard_field" group
	var keyboard_fields = get_tree().get_nodes_in_group("keyboard_field")
	for field in keyboard_fields:
		_connect_field_signals(field)

func _connect_field_signals(field):
	# Skip if already monitored
	if field in _monitored_fields:
		return

	# Connect focus signals
	if not field.focus_entered.is_connected(_on_field_focus_entered):
		field.focus_entered.connect(_on_field_focus_entered.bind(field))

	if not field.focus_exited.is_connected(_on_field_focus_exited):
		field.focus_exited.connect(_on_field_focus_exited.bind(field))

	# Track this field
	_monitored_fields.append(field)

func _on_field_focus_entered(field):
	if auto_show and is_keyboard_focus_object(field):
		# Check for field-specific layout metadata
		if field.has_meta("keyboard_layout_type"):
			var layout_type = field.get_meta("keyboard_layout_type")
			if layout_type != null and layout_type != "":
				# Switch to the requested layout
				set_active_layout_by_name(layout_type)
		else:
			# No metadata - switch back to default layout
			set_active_layout_by_name("standard-characters")

		_show_keyboard()

func _on_field_focus_exited(field):
	# Hide keyboard when field loses focus
	# Unless focus moved to another keyboard field
	await get_tree().process_frame  # Wait for new focus owner to be set

	var new_focus = get_viewport().gui_get_focus_owner()
	if not is_keyboard_focus_object(new_focus):
		_hide_keyboard()


###########################
## INIT
###########################
var KeyboardButton
var KeyListHandler

var layouts = []
var keys = []
var capslock_keys = []
var uppercase = false

var tween_position
var tween_speed = .2

var hide_position = Vector2()
var _monitored_fields = []

func _ready():
	# Calculate initial viewport-relative size
	_update_keyboard_size()

	# Discover and monitor text input fields
	_discover_and_monitor_fields()

func _init_keyboard():
	if custom_layout_file == null:
		var default_layout = preload("default_layout.gd").new()
		_create_keyboard(default_layout.data)
	else:
		_create_keyboard(_load_json(custom_layout_file))

	# init positioning without animation
	var tmp_anim = animate
	animate = false
	if visible:
		_hide_keyboard()
	elif visible:
		_show_keyboard()
	animate = tmp_anim


###########################
## HIDE/SHOW
###########################

var focus_object = null

func show():
	_show_keyboard()

func hide():
	_hide_keyboard()

var released = true
func _update_auto_display_on_input(event):
	if auto_show == false:
		return

	if event is InputEventMouseButton:
		released = !released
		if released == false:
			return

		var focus_object = get_viewport().gui_get_focus_owner()
		if focus_object != null:
			var click_on_input = Rect2(focus_object.global_position, focus_object.size).has_point(get_global_mouse_position())
			var click_on_keyboard = Rect2(global_position, size).has_point(get_global_mouse_position())

			if click_on_input:
				if is_keyboard_focus_object(focus_object):
					_show_keyboard()
			elif click_on_keyboard:
				_show_keyboard()
			else:
				_hide_keyboard()

	if event is InputEventKey:
		var focus_object = get_viewport().gui_get_focus_owner()
		if focus_object != null:
			if event.keycode == KEY_ENTER:
				if is_keyboard_focus_object_complete_on_enter(focus_object):
					focus_object.release_focus()
					_hide_keyboard()


func _hide_keyboard(key_data=null):
	if animate:
		var new_y_pos = get_viewport().get_visible_rect().size.y + 10
		animate_position(Vector2(position.x, new_y_pos), true, false)
	else:
		change_visibility(false)


func _show_keyboard(key_data=null):
	change_visibility(true)
	if animate:
		var new_y_pos = get_viewport().get_visible_rect().size.y - size.y
		animate_position(Vector2(position.x, new_y_pos))

func _update_keyboard_position_for_focus():
	# Called when tab changes to reposition keyboard for current focused field
	var focus_object = get_viewport().gui_get_focus_owner()

	# Only update if focused on a keyboard field
	if is_keyboard_focus_object(focus_object):
		# Keyboard stays visible, just ensure position is correct
		# Animation already handled by _show_keyboard
		if visible and animate:
			var new_y_pos = get_viewport().get_visible_rect().size.y - size.y
			animate_position(Vector2(position.x, new_y_pos))


func animate_position(new_position, trigger_visibility:bool=false, final_visibility:bool=false):
	var tween = get_tree().create_tween()
	if trigger_visibility:
		tween.finished.connect(change_visibility.bind(final_visibility))
	tween.tween_property(
		self,"position",
		Vector2(new_position),
		tween_speed
	).set_trans(Tween.TRANS_SINE)


func change_visibility(value):
	if value:
		super.show()
	else:
		_set_caps_lock(false)
		super.hide()
	visibility_changed.emit()


###########################
##  KEY LAYOUT
###########################

var prev_prev_layout = null
var previous_layout = null
var current_layout = null

func set_active_layout_by_name(name):
	for layout in layouts:
		if layout.get_meta("layout_name") == str(name):
			_show_layout(layout)
		else:
			_hide_layout(layout)


func _show_layout(layout):
	layout.show()
	current_layout = layout


func _hide_layout(layout):
	layout.hide()


func _switch_layout(key_data):
	prev_prev_layout = previous_layout
	previous_layout = current_layout
	layout_changed.emit(key_data.get("layout-name"))

	for layout in layouts:
		_hide_layout(layout)

	if key_data.get("layout-name") == "PREVIOUS-LAYOUT":
		if prev_prev_layout != null:
			_show_layout(prev_prev_layout)
			return

	for layout in layouts:
		if layout.get_meta("layout_name") == key_data.get("layout-name"):
			_show_layout(layout)
			return

	_set_caps_lock(false)


###########################
## KEY EVENTS
###########################

func _set_caps_lock(value: bool):
	uppercase = value
	for key in capslock_keys:
		if value:
			if key.get_draw_mode() != BaseButton.DRAW_PRESSED:
				key.button_pressed = !key.button_pressed
		else:
			if key.get_draw_mode() == BaseButton.DRAW_PRESSED:
				key.button_pressed = !key.button_pressed

	for key in keys:
		key.change_uppercase(value)


func _trigger_uppercase(key_data):
	uppercase = !uppercase
	_set_caps_lock(uppercase)


func _key_released(key_data):
	if key_data.has("output"):
		var key_value = key_data.get("output")

		###########################
		## DISPATCH InputEvent 
		###########################

		var input_event_key = InputEventKey.new()
		input_event_key.shift_pressed = uppercase
		input_event_key.alt_pressed = false
		input_event_key.meta_pressed = false
		input_event_key.ctrl_pressed = false
		input_event_key.pressed = true

		var key = KeyListHandler.get_key_from_string(key_value)
		if !uppercase && KeyListHandler.has_lowercase(key_value):
			key +=32

		input_event_key.keycode = key
		input_event_key.unicode = key

		Input.parse_input_event(input_event_key)

		###########################
		## DISABLE CAPSLOCK AFTER 
		###########################
		_set_caps_lock(false)


###########################
## CONSTRUCT KEYBOARD
###########################

func _set_key_style(style_name:String, key: Control, style:StyleBoxFlat):
	if style != null:
		key.add_theme_stylebox_override(style_name, style)


func _create_keyboard(layout_data):
	if layout_data == null:
		print("ERROR. No layout file found")
		return

	KeyListHandler = preload("keylist.gd").new()
	KeyboardButton = preload("keyboard_button.gd")

	var ICON_DELETE = preload("icons/delete.png")
	var ICON_SHIFT = preload("icons/shift.png")
	var ICON_LEFT = preload("icons/left.png")
	var ICON_RIGHT = preload("icons/right.png")
	var ICON_HIDE = preload("icons/hide.png")
	var ICON_ENTER = preload("icons/enter.png")

	var data = layout_data

	if style_background != null:
		add_theme_stylebox_override('panel', style_background)

	var index = 0
	for layout in data.get("layouts"):

		var layout_container = PanelContainer.new()

		if style_background != null:
			layout_container.add_theme_stylebox_override('panel', style_background)

		# SHOW FIRST LAYOUT ON DEFAULT
		if index > 0:
			layout_container.hide()
		else:
			current_layout = layout_container

		var layout_name = layout.get("name")
		layout_container.set_meta("layout_name", layout_name)
		if set_tool_tip:
			layout_container.tooltip_text = layout_name
		layouts.push_back(layout_container)
		add_child(layout_container)

		var base_vbox = VBoxContainer.new()
		base_vbox.size_flags_horizontal = SIZE_EXPAND_FILL
		base_vbox.size_flags_vertical = SIZE_EXPAND_FILL
		# theme override for spacing
		base_vbox.add_theme_constant_override("separation", separation.y)

		for row in layout.get("rows"):

			var key_row = HBoxContainer.new()
			key_row.size_flags_horizontal = SIZE_EXPAND_FILL
			key_row.size_flags_vertical = SIZE_EXPAND_FILL
			key_row.add_theme_constant_override("separation", separation.x)

			for key in row.get("keys"):
				var new_key = KeyboardButton.new(key)

				_set_key_style("normal",new_key, style_normal)
				_set_key_style("hover",new_key, style_hover)
				_set_key_style("pressed",new_key, style_pressed)

				new_key.set('theme_override_font_sizes/font_size', font_size)
				if font != null:
					new_key.set('theme_override_fonts/font', font)
				if font_color_normal != null:
					new_key.set('theme_override_colors/font_color', font_color_normal)
					new_key.set('theme_override_colors/font_hover_color', font_color_hover)
					new_key.set('theme_override_colors/font_pressed_color', font_color_pressed)
					new_key.set('theme_override_colors/font_disabled_color', font_color_normal)

				new_key.released.connect(_key_released)

				if key.has("type"):
					if key.get("type") == "switch-layout":
						new_key.released.connect(_switch_layout)
						_set_key_style("normal",new_key, style_special_keys)
					elif key.get("type") == "special":
						_set_key_style("normal",new_key, style_special_keys)
					elif key.get("type") == "special-shift":
						new_key.released.connect(_trigger_uppercase)
						new_key.toggle_mode = true
						capslock_keys.push_back(new_key)
						_set_key_style("normal",new_key, style_special_keys)
					elif key.get("type") == "special-hide-keyboard":
						new_key.released.connect(_hide_keyboard)
						_set_key_style("normal",new_key, style_special_keys)

				# SET ICONS
				if key.has("display-icon"):
					var icon_data = str(key.get("display-icon")).split(":")
					# PREDEFINED
					if str(icon_data[0])=="PREDEFINED":
						if str(icon_data[1])=="DELETE":
							new_key.set_icon(ICON_DELETE)
						elif str(icon_data[1])=="SHIFT":
							new_key.set_icon(ICON_SHIFT)
						elif str(icon_data[1])=="LEFT":
							new_key.set_icon(ICON_LEFT)
						elif str(icon_data[1])=="RIGHT":
							new_key.set_icon(ICON_RIGHT)
						elif str(icon_data[1])=="HIDE":
							new_key.set_icon(ICON_HIDE)
						elif str(icon_data[1])=="ENTER":
							new_key.set_icon(ICON_ENTER)
					# CUSTOM
					if str(icon_data[0])=="res":
						var texture = load(key.get("display-icon"))
						new_key.set_icon(texture)

					if font_color_normal != null:
						new_key.set_icon_color(font_color_normal)

				key_row.add_child(new_key)
				keys.push_back(new_key)

			base_vbox.add_child(key_row)

		layout_container.add_child(base_vbox)
		index+=1


###########################
## LOAD SETTINGS
###########################

func _load_json(file_path) -> Variant:
	var json = JSON.new()
	var json_string = _load_file(file_path)
	var error = json.parse(json_string)
	if error == OK:
		var data_received = json.data
		#        print(data_received)
		if typeof(data_received) == TYPE_DICTIONARY:
			return data_received
		else:
			return {"msg": "unexpected data => expected DICTIONARY"}
	else:
		print("JSON Parse Error: ", json.get_error_message(), " in ", json_string, " at line ", json.get_error_line())
		return {"msg":json.get_error_message()}


func _load_file(file_path):
	var file = FileAccess.open(file_path, FileAccess.READ)
	if not file:
		print("Error loading File. Error: ")

	var content = file.get_as_text()
	file.close()
	return content


###########################
## HELPER
###########################

func is_keyboard_focus_object_complete_on_enter(focus_object):
	if focus_object is LineEdit:
		return true
	return false

func is_keyboard_focus_object(focus_object):
	if focus_object is LineEdit or focus_object is TextEdit:
		return true
	return false
