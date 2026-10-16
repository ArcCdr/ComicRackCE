// Replacements for the theme types of cYo.Common.Windows (cYo.Common.Windows/Forms/Theme/*.cs) that
// ComicRack.Plugins still references (IPluginEnvironment.Theme, Command.GetThemedImage).
using System.Windows.Forms;

namespace cYo.Common.Windows.Forms.Theme
{
	public enum Themes
	{
		Default,
		Dark
	}

	public enum UIComponent
	{
		None,
		SidePanel,
		Content,
		Window
	}

	public interface ITheme
	{
		UIComponent UIComponent
		{
			get;
		}

		void ApplyTheme(Control control);
	}
}
