using System;
using Godot;

namespace BarBox.Core.UI
{
	/// <summary>
	/// Defines a column for the DataTableView component
	/// </summary>
	public class TableColumnDefinition
	{
		public string Header { get; set; } = "";
		public Func<object, string> ValueSelector { get; set; }
		public int Width { get; set; } = 0;
		public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;
		public bool IsSortable { get; set; } = true;
		public Func<object, object, int> SortComparer { get; set; }
		public Action<Label, object> CellStyler { get; set; }
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