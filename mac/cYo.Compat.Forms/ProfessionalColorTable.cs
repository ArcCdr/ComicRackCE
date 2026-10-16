using System.Drawing;

namespace System.Windows.Forms
{
	/// <summary>Colour table of the Office-style tool strip renderer. Types-only: the base values are not themed.</summary>
	public class ProfessionalColorTable
	{
		public bool UseSystemColors { get; set; }

		public virtual Color ButtonSelectedHighlight => Color.Empty;
		public virtual Color ButtonSelectedHighlightBorder => Color.Empty;
		public virtual Color ButtonPressedHighlight => Color.Empty;
		public virtual Color ButtonPressedHighlightBorder => Color.Empty;
		public virtual Color ButtonCheckedHighlight => Color.Empty;
		public virtual Color ButtonCheckedHighlightBorder => Color.Empty;
		public virtual Color ButtonPressedBorder => Color.Empty;
		public virtual Color ButtonSelectedBorder => Color.Empty;
		public virtual Color ButtonCheckedGradientBegin => Color.Empty;
		public virtual Color ButtonCheckedGradientMiddle => Color.Empty;
		public virtual Color ButtonCheckedGradientEnd => Color.Empty;
		public virtual Color ButtonSelectedGradientBegin => Color.Empty;
		public virtual Color ButtonSelectedGradientMiddle => Color.Empty;
		public virtual Color ButtonSelectedGradientEnd => Color.Empty;
		public virtual Color ButtonPressedGradientBegin => Color.Empty;
		public virtual Color ButtonPressedGradientMiddle => Color.Empty;
		public virtual Color ButtonPressedGradientEnd => Color.Empty;
		public virtual Color CheckBackground => Color.Empty;
		public virtual Color CheckSelectedBackground => Color.Empty;
		public virtual Color CheckPressedBackground => Color.Empty;
		public virtual Color GripDark => Color.Empty;
		public virtual Color GripLight => Color.Empty;
		public virtual Color ImageMarginGradientBegin => Color.Empty;
		public virtual Color ImageMarginGradientMiddle => Color.Empty;
		public virtual Color ImageMarginGradientEnd => Color.Empty;
		public virtual Color ImageMarginRevealedGradientBegin => Color.Empty;
		public virtual Color ImageMarginRevealedGradientMiddle => Color.Empty;
		public virtual Color ImageMarginRevealedGradientEnd => Color.Empty;
		public virtual Color MenuStripGradientBegin => Color.Empty;
		public virtual Color MenuStripGradientEnd => Color.Empty;
		public virtual Color MenuItemSelected => Color.Empty;
		public virtual Color MenuItemBorder => Color.Empty;
		public virtual Color MenuBorder => Color.Empty;
		public virtual Color MenuItemSelectedGradientBegin => Color.Empty;
		public virtual Color MenuItemSelectedGradientEnd => Color.Empty;
		public virtual Color MenuItemPressedGradientBegin => Color.Empty;
		public virtual Color MenuItemPressedGradientMiddle => Color.Empty;
		public virtual Color MenuItemPressedGradientEnd => Color.Empty;
		public virtual Color RaftingContainerGradientBegin => Color.Empty;
		public virtual Color RaftingContainerGradientEnd => Color.Empty;
		public virtual Color SeparatorDark => Color.Empty;
		public virtual Color SeparatorLight => Color.Empty;
		public virtual Color StatusStripBorder => Color.Empty;
		public virtual Color StatusStripGradientBegin => Color.Empty;
		public virtual Color StatusStripGradientEnd => Color.Empty;
		public virtual Color ToolStripBorder => Color.Empty;
		public virtual Color ToolStripDropDownBackground => Color.Empty;
		public virtual Color ToolStripGradientBegin => Color.Empty;
		public virtual Color ToolStripGradientMiddle => Color.Empty;
		public virtual Color ToolStripGradientEnd => Color.Empty;
		public virtual Color ToolStripContentPanelGradientBegin => Color.Empty;
		public virtual Color ToolStripContentPanelGradientEnd => Color.Empty;
		public virtual Color ToolStripPanelGradientBegin => Color.Empty;
		public virtual Color ToolStripPanelGradientEnd => Color.Empty;
		public virtual Color OverflowButtonGradientBegin => Color.Empty;
		public virtual Color OverflowButtonGradientMiddle => Color.Empty;
		public virtual Color OverflowButtonGradientEnd => Color.Empty;
	}
}
