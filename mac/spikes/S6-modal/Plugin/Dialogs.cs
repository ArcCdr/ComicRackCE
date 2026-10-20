using System.Windows.Forms;

namespace S6Plugin
{
	/// <summary>What a compiled community plug-in does with WinForms: make a form, ask a question.</summary>
	public static class Dialogs
	{
		public static Form MakeForm()
		{
			Form form = new Form();
			form.Text = "made by the plug-in";
			return form;
		}

		public static string Describe()
		{
			return "plug-in sees " + typeof(Form).Assembly.GetName().FullName;
		}

		public static DialogResult Ask(string text)
		{
			return MessageBox.Show(text, "plug-in", MessageBoxButtons.YesNo);
		}
	}
}
