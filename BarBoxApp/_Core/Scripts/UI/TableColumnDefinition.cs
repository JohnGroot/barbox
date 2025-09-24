using System;
using Godot;

namespace BarBox.Core.UI
{
	/// <summary>
	/// Defines a column for the DataTableView component
	/// </summary>
	public class TableColumnDefinition
	{
		/// <summary>
		/// Column header text
		/// </summary>
		public string Header { get; set; } = "";

		/// <summary>
		/// Function to extract the display value from the data object
		/// </summary>
		public Func<object, string> ValueSelector { get; set; }

		/// <summary>
		/// Column width in pixels (0 = auto-size)
		/// </summary>
		public int Width { get; set; } = 0;

		/// <summary>
		/// Text alignment for this column
		/// </summary>
		public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;

		/// <summary>
		/// Whether this column is sortable
		/// </summary>
		public bool IsSortable { get; set; } = true;

		/// <summary>
		/// Custom sort comparison function (optional)
		/// </summary>
		public Func<object, object, int> SortComparer { get; set; }

		/// <summary>
		/// Custom styling for cells in this column
		/// </summary>
		public Action<Label, object> CellStyler { get; set; }

		/// <summary>
		/// Whether to use monospace font for this column (useful for times/numbers)
		/// </summary>
		public bool UseMonospaceFont { get; set; } = false;

		public TableColumnDefinition(string header, Func<object, string> valueSelector)
		{
			Header = header;
			ValueSelector = valueSelector;
		}

		public TableColumnDefinition(string header, Func<object, string> valueSelector, int width)
			: this(header, valueSelector)
		{
			Width = width;
		}

		public TableColumnDefinition(string header, Func<object, string> valueSelector, int width, HorizontalAlignment alignment)
			: this(header, valueSelector, width)
		{
			Alignment = alignment;
		}

		public TableColumnDefinition(string header, Func<object, string> valueSelector, int width, HorizontalAlignment alignment, bool useMonospace)
			: this(header, valueSelector, width, alignment)
		{
			UseMonospaceFont = useMonospace;
		}
	}
}