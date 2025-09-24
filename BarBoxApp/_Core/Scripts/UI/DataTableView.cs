using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Core.UI
{
	/// <summary>
	/// Data table view component that can bind to any data type and display it in a table format
	/// Supports sorting, styling, and flexible column configuration
	/// </summary>
	[GlobalClass]
	public partial class DataTableView : Control
	{
		// ================================================================
		// EXPORT PROPERTIES
		// ================================================================

		[ExportCategory("Table Settings")]
		[Export] public bool ShowHeaders { get; set; } = true;
		[Export] public bool AlternatingRowColors { get; set; } = true;
		[Export] public Color HeaderBackgroundColor { get; set; } = new Color(0.2f, 0.2f, 0.3f, 1.0f);
		[Export] public Color HeaderTextColor { get; set; } = Colors.White;
		[Export] public Color RowBackgroundColor1 { get; set; } = new Color(0.1f, 0.1f, 0.15f, 0.8f);
		[Export] public Color RowBackgroundColor2 { get; set; } = new Color(0.15f, 0.15f, 0.2f, 0.8f);
		[Export] public Color RowTextColor { get; set; } = Colors.White;
		[Export] public Color HighlightRowColor { get; set; } = new Color(0.8f, 0.8f, 0.2f, 0.3f);
		[Export] public int DefaultRowHeight { get; set; } = 32;
		[Export] public int HeaderHeight { get; set; } = 40;
		[Export] public int CellPadding { get; set; } = 8;

		// ================================================================
		// SIGNALS
		// ================================================================

		[Signal] public delegate void RowSelectedEventHandler(int selectedIndex);
		[Signal] public delegate void ColumnSortedEventHandler(string columnHeader, bool ascending);

		// ================================================================
		// PRIVATE FIELDS
		// ================================================================

		private readonly List<TableColumnDefinition> _columns = new();
		private List<object> _data = new();
		private GridContainer _gridContainer;
		private ScrollContainer _scrollContainer;
		private string _currentSortColumn = "";
		private bool _sortAscending = true;
		private Func<object, bool> _highlightPredicate;

		// ================================================================
		// INITIALIZATION
		// ================================================================

		public override void _Ready()
		{
			EnsureTableStructure();
		}

		private void EnsureTableStructure()
		{
			// Only set up if not already initialized
			if (_scrollContainer != null && _gridContainer != null)
				return;

			SetupTableStructure();
		}

		private void SetupTableStructure()
		{
			// Clean up existing containers if they exist
			if (_scrollContainer != null)
			{
				_scrollContainer.QueueFree();
				_scrollContainer = null;
			}

			// Set minimum size for the DataTableView control itself
			if (CustomMinimumSize.X <= 0 || CustomMinimumSize.Y <= 0)
			{
				CustomMinimumSize = new Vector2(400, 200);
			}

			// Create scroll container
			_scrollContainer = new ScrollContainer();
			_scrollContainer.AnchorLeft = 0;
			_scrollContainer.AnchorTop = 0;
			_scrollContainer.AnchorRight = 1;
			_scrollContainer.AnchorBottom = 1;
			AddChild(_scrollContainer);

			// Create grid container
			_gridContainer = new GridContainer();
			_gridContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			_gridContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			_scrollContainer.AddChild(_gridContainer);
		}

		// ================================================================
		// COLUMN MANAGEMENT
		// ================================================================

		/// <summary>
		/// Add a column to the table
		/// </summary>
		public DataTableView AddColumn(string header, Func<object, string> valueSelector, int width = 0, HorizontalAlignment alignment = HorizontalAlignment.Left, bool useMonospace = false)
		{
			var column = new TableColumnDefinition(header, valueSelector, width, alignment, useMonospace);
			_columns.Add(column);
			return this;
		}

		/// <summary>
		/// Add a column with custom styling
		/// </summary>
		public DataTableView AddColumn(TableColumnDefinition column)
		{
			_columns.Add(column);
			return this;
		}

		/// <summary>
		/// Add a sortable column with custom comparer
		/// </summary>
		public DataTableView AddSortableColumn(string header, Func<object, string> valueSelector, Func<object, object, int> sortComparer, int width = 0, HorizontalAlignment alignment = HorizontalAlignment.Left)
		{
			var column = new TableColumnDefinition(header, valueSelector, width, alignment)
			{
				SortComparer = sortComparer,
				IsSortable = true
			};
			_columns.Add(column);
			return this;
		}

		/// <summary>
		/// Clear all columns
		/// </summary>
		public void ClearColumns()
		{
			_columns.Clear();
		}

		// ================================================================
		// DATA BINDING
		// ================================================================

		/// <summary>
		/// Bind data to the table and refresh display
		/// </summary>
		public void BindData(IEnumerable<object> data)
		{
			_data = data?.ToList() ?? new List<object>();
			EnsureTableStructure();
			RefreshTable();
		}

		/// <summary>
		/// Get the currently bound data
		/// </summary>
		public IReadOnlyList<object> GetData()
		{
			return _data.AsReadOnly();
		}

		/// <summary>
		/// Set a predicate to highlight specific rows
		/// </summary>
		public void SetHighlightPredicate(Func<object, bool> predicate)
		{
			_highlightPredicate = predicate;
		}

		// ================================================================
		// TABLE RENDERING
		// ================================================================

		/// <summary>
		/// Refresh the entire table display
		/// </summary>
		public void RefreshTable()
		{
			EnsureTableStructure();
			ClearTableContents();

			if (_columns.Count == 0) return;

			// Set grid columns
			_gridContainer.Columns = _columns.Count;

			// Create headers if enabled
			if (ShowHeaders)
			{
				CreateHeaders();
			}

			// Create data rows
			CreateDataRows();
		}

		private void ClearTableContents()
		{
			if (_gridContainer == null) return;

			foreach (Node child in _gridContainer.GetChildren())
			{
				child.QueueFree();
			}
		}

		private void CreateHeaders()
		{
			if (_gridContainer == null) return;

			foreach (var column in _columns)
			{
				var headerButton = new Button();
				headerButton.Text = column.Header;
				headerButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				headerButton.CustomMinimumSize = new Vector2(column.Width > 0 ? column.Width : 0, HeaderHeight);

				// Header styling
				headerButton.AddThemeColorOverride("font_color", HeaderTextColor);
				headerButton.AddThemeColorOverride("font_color_hover", HeaderTextColor);
				headerButton.AddThemeColorOverride("font_color_pressed", HeaderTextColor);
				headerButton.AddThemeColorOverride("font_color_disabled", HeaderTextColor);

				// Add sort indicator if column is sortable
				if (column.IsSortable)
				{
					string sortIndicator = "";
					if (_currentSortColumn == column.Header)
					{
						sortIndicator = _sortAscending ? " ↑" : " ↓";
					}
					headerButton.Text = column.Header + sortIndicator;

					// Connect sort signal
					var columnHeader = column.Header; // Capture for closure
					headerButton.Pressed += () => SortByColumn(columnHeader);
				}
				else
				{
					headerButton.Disabled = true;
				}

				_gridContainer.AddChild(headerButton);
			}
		}

		private void CreateDataRows()
		{
			if (_gridContainer == null) return;

			for (int rowIndex = 0; rowIndex < _data.Count; rowIndex++)
			{
				var item = _data[rowIndex];
				bool shouldHighlight = _highlightPredicate?.Invoke(item) ?? false;
				Color rowBgColor = shouldHighlight ? HighlightRowColor :
					(AlternatingRowColors && rowIndex % 2 == 1) ? RowBackgroundColor2 : RowBackgroundColor1;

				foreach (var column in _columns)
				{
					var cellLabel = CreateCellLabel(item, column, rowBgColor);
					_gridContainer.AddChild(cellLabel);
				}
			}
		}

		private Label CreateCellLabel(object item, TableColumnDefinition column, Color backgroundColor)
		{
			var label = new Label();

			// Get cell value
			string cellValue = column.ValueSelector?.Invoke(item) ?? "";
			label.Text = cellValue;

			// Basic styling
			label.HorizontalAlignment = column.Alignment;
			label.VerticalAlignment = VerticalAlignment.Center;
			label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			label.CustomMinimumSize = new Vector2(column.Width > 0 ? column.Width : 0, DefaultRowHeight);
			label.AddThemeColorOverride("font_color", RowTextColor);

			// Background color
			var background = new ColorRect();
			background.Color = backgroundColor;
			background.AnchorLeft = 0;
			background.AnchorTop = 0;
			background.AnchorRight = 1;
			background.AnchorBottom = 1;
			label.AddChild(background);
			background.ZIndex = -1;

			// Monospace font if requested
			if (column.UseMonospaceFont)
			{
				// Note: In a real implementation, you might want to load a specific monospace font
				label.AddThemeFontSizeOverride("font_size", 14);
			}

			// Apply custom styling if provided
			column.CellStyler?.Invoke(label, item);

			// Add padding
			label.AddThemeConstantOverride("margin_left", CellPadding);
			label.AddThemeConstantOverride("margin_right", CellPadding);

			return label;
		}

		// ================================================================
		// SORTING
		// ================================================================

		private void SortByColumn(string columnHeader)
		{
			var column = _columns.FirstOrDefault(c => c.Header == columnHeader);
			if (column == null || !column.IsSortable) return;

			// Toggle sort direction if same column
			if (_currentSortColumn == columnHeader)
			{
				_sortAscending = !_sortAscending;
			}
			else
			{
				_currentSortColumn = columnHeader;
				_sortAscending = true;
			}

			// Sort the data
			if (column.SortComparer != null)
			{
				// Use custom comparer
				_data = _sortAscending ?
					_data.OrderBy(x => x, Comparer<object>.Create((x, y) => column.SortComparer(x, y))).ToList() :
					_data.OrderByDescending(x => x, Comparer<object>.Create((x, y) => column.SortComparer(x, y))).ToList();
			}
			else
			{
				// Sort by string value
				_data = _sortAscending ?
					_data.OrderBy(x => column.ValueSelector(x)).ToList() :
					_data.OrderByDescending(x => column.ValueSelector(x)).ToList();
			}

			// Refresh table display
			RefreshTable();

			// Emit signal
			EmitSignal(SignalName.ColumnSorted, columnHeader, _sortAscending);
		}

		// ================================================================
		// UTILITY METHODS
		// ================================================================

		/// <summary>
		/// Clear all data from the table
		/// </summary>
		public void ClearData()
		{
			_data.Clear();
			RefreshTable();
		}

		/// <summary>
		/// Add a single item to the table
		/// </summary>
		public void AddItem(object item)
		{
			_data.Add(item);
			RefreshTable();
		}

		/// <summary>
		/// Remove an item from the table
		/// </summary>
		public void RemoveItem(object item)
		{
			_data.Remove(item);
			RefreshTable();
		}

		/// <summary>
		/// Get the number of rows in the table
		/// </summary>
		public int GetRowCount()
		{
			return _data.Count;
		}

		/// <summary>
		/// Get the number of columns in the table
		/// </summary>
		public int GetColumnCount()
		{
			return _columns.Count;
		}
	}
}