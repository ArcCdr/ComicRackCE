using System;
using System.ComponentModel;
using System.Drawing;

// Label, the buttons and the text boxes (P4.9a): their state and the events that a change of the state raises, in the order of dotnet/winforms. The peers (P4.10a to c) show the
// state and tell the facade what the user did; a click on a check box changes CheckState and then raises Click, a click on a button sets the DialogResult of its form first.
namespace System.Windows.Forms
{
	public partial class Label : Control
	{
		private BorderStyle borderStyle;
		private FlatStyle flatStyle = FlatStyle.Standard;
		private Image image;
		private ContentAlignment imageAlign = ContentAlignment.MiddleCenter;
		private ContentAlignment textAlign = ContentAlignment.TopLeft;
		private int imageIndex = -1;
		private string imageKey = string.Empty;
		private ImageList imageList;
		private bool useMnemonic = true;
		private bool autoEllipsis;
		private bool useCompatibleTextRendering;

		public Label()
		{
			SetStyle(ControlStyles.Selectable, false);
			SetStyle(ControlStyles.SupportsTransparentBackColor, true);
			TabStop = false;
		}

		protected override Size DefaultSize => new Size(100, 23);

		protected override Padding DefaultMargin => new Padding(3, 0, 3, 0);

		internal override Size NonClientSize => UserControl.BorderSize(borderStyle);

		internal override Point ClientOffset => new Point(UserControl.BorderSize(borderStyle).Width / 2, UserControl.BorderSize(borderStyle).Height / 2);

		public virtual BorderStyle BorderStyle
		{
			get => borderStyle;
			set
			{
				if (borderStyle != value)
				{
					Size client = ClientSize;
					borderStyle = value;
					Size = SizeFromClientSize(client);
					Notify("BorderStyle");
				}
			}
		}

		public FlatStyle FlatStyle
		{
			get => flatStyle;
			set
			{
				flatStyle = value;
				Notify("FlatStyle");
			}
		}

		public Image Image
		{
			get => image;
			set
			{
				image = value;
				Notify("Image");
			}
		}

		public ContentAlignment ImageAlign
		{
			get => imageAlign;
			set
			{
				imageAlign = value;
				Notify("ImageAlign");
			}
		}

		public int ImageIndex
		{
			get => imageIndex;
			set => imageIndex = value;
		}

		public string ImageKey
		{
			get => imageKey;
			set => imageKey = value ?? string.Empty;
		}

		public ImageList ImageList
		{
			get => imageList;
			set => imageList = value;
		}

		public virtual ContentAlignment TextAlign
		{
			get => textAlign;
			set
			{
				if (textAlign != value)
				{
					textAlign = value;
					Notify("TextAlign");
					OnTextAlignChanged(EventArgs.Empty);
				}
			}
		}

		public bool UseMnemonic
		{
			get => useMnemonic;
			set
			{
				useMnemonic = value;
				Notify("UseMnemonic");
			}
		}

		public bool AutoEllipsis
		{
			get => autoEllipsis;
			set
			{
				autoEllipsis = value;
				Notify("AutoEllipsis");
			}
		}

		public bool UseCompatibleTextRendering
		{
			get => useCompatibleTextRendering;
			set => useCompatibleTextRendering = value;
		}

		protected virtual void OnTextAlignChanged(EventArgs e)
		{
			TextAlignChanged?.Invoke(this, e);
		}

		public event EventHandler TextAlignChanged;

		public override string ToString()
		{
			string text = Text;
			if (text.Length > 40)
			{
				text = text.Substring(0, 40) + "...";
			}
			return base.ToString() + ", Text: " + text;
		}
	}

	public abstract partial class ButtonBase : Control
	{
		private FlatStyle flatStyle = FlatStyle.Standard;
		private Image image;
		private ContentAlignment imageAlign = ContentAlignment.MiddleCenter;
		private ContentAlignment textAlign = ContentAlignment.MiddleCenter;
		private TextImageRelation textImageRelation = TextImageRelation.Overlay;
		private int imageIndex = -1;
		private string imageKey = string.Empty;
		private ImageList imageList;
		private bool useMnemonic = true;
		private bool autoEllipsis;
		private bool useCompatibleTextRendering;
		private bool useVisualStyleBackColor;
		private bool isDefault;

		protected ButtonBase()
		{
			SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.Opaque | ControlStyles.ResizeRedraw | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick | ControlStyles.UserMouse, true);
			SetStyle(ControlStyles.UserPaint, true);
		}

		protected override ImeMode DefaultImeMode => ImeMode.Disable;

		protected override Size DefaultSize => new Size(75, 23);

		public FlatStyle FlatStyle
		{
			get => flatStyle;
			set
			{
				flatStyle = value;
				Notify("FlatStyle");
			}
		}

		public Image Image
		{
			get => image;
			set
			{
				image = value;
				Notify("Image");
			}
		}

		public ContentAlignment ImageAlign
		{
			get => imageAlign;
			set
			{
				imageAlign = value;
				Notify("ImageAlign");
			}
		}

		public int ImageIndex
		{
			get => imageIndex;
			set => imageIndex = value;
		}

		public string ImageKey
		{
			get => imageKey;
			set => imageKey = value ?? string.Empty;
		}

		public ImageList ImageList
		{
			get => imageList;
			set => imageList = value;
		}

		public virtual ContentAlignment TextAlign
		{
			get => textAlign;
			set
			{
				textAlign = value;
				Notify("TextAlign");
			}
		}

		public TextImageRelation TextImageRelation
		{
			get => textImageRelation;
			set
			{
				textImageRelation = value;
				Notify("TextImageRelation");
			}
		}

		public bool UseMnemonic
		{
			get => useMnemonic;
			set
			{
				useMnemonic = value;
				Notify("UseMnemonic");
			}
		}

		public bool AutoEllipsis
		{
			get => autoEllipsis;
			set
			{
				autoEllipsis = value;
				Notify("AutoEllipsis");
			}
		}

		public bool UseCompatibleTextRendering
		{
			get => useCompatibleTextRendering;
			set => useCompatibleTextRendering = value;
		}

		public bool UseVisualStyleBackColor
		{
			get => useVisualStyleBackColor;
			set
			{
				useVisualStyleBackColor = value;
				Notify("UseVisualStyleBackColor");
			}
		}

		protected internal bool IsDefault
		{
			get => isDefault;
			set
			{
				if (isDefault != value)
				{
					isDefault = value;
					Notify("IsDefault");
				}
			}
		}

		public override Color BackColor
		{
			get => base.BackColor;
			set
			{
				if (!value.IsEmpty)
				{
					useVisualStyleBackColor = false;
				}
				base.BackColor = value;
			}
		}
	}

	public partial class Button : ButtonBase, IButtonControl
	{
		private DialogResult dialogResult;
		private AutoSizeMode buttonAutoSizeMode = AutoSizeMode.GrowOnly;

		public Button()
		{
			SetStyle(ControlStyles.StandardDoubleClick, false);
		}

		public virtual DialogResult DialogResult
		{
			get => dialogResult;
			set
			{
				if (!Enum.IsDefined(typeof(DialogResult), value))
				{
					throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DialogResult));
				}
				dialogResult = value;
			}
		}

		public AutoSizeMode AutoSizeMode
		{
			get => buttonAutoSizeMode;
			set
			{
				if (buttonAutoSizeMode != value)
				{
					buttonAutoSizeMode = value;
					SetAutoSizeMode(value);
					Parent?.PerformLayout(this, "AutoSize");
				}
			}
		}

		public virtual void NotifyDefault(bool value)
		{
			IsDefault = value;
		}

		/// <summary>Generates a Click event, as a click by the user does (a button that cannot be selected does nothing).</summary>
		public void PerformClick()
		{
			if (CanSelect)
			{
				OnClick(EventArgs.Empty);
			}
		}

		/// <summary>A click sets the DialogResult of the form to the one of the button, then raises Click.</summary>
		protected override void OnClick(EventArgs e)
		{
			Form form = FindForm();
			if (form != null)
			{
				form.DialogResult = dialogResult;
			}
			base.OnClick(e);
		}

		public override string ToString() => base.ToString() + ", Text: " + Text;
	}

	public partial class CheckBox : ButtonBase
	{
		private bool autoCheck = true;
		private bool threeState;
		private CheckState checkState;
		private Appearance appearance = Appearance.Normal;
		private ContentAlignment checkAlign = ContentAlignment.MiddleLeft;

		public CheckBox()
		{
			TextAlign = ContentAlignment.MiddleLeft;
		}

		protected override Size DefaultSize => new Size(104, 24);

		public bool AutoCheck
		{
			get => autoCheck;
			set => autoCheck = value;
		}

		public bool ThreeState
		{
			get => threeState;
			set => threeState = value;
		}

		public Appearance Appearance
		{
			get => appearance;
			set
			{
				if (appearance != value)
				{
					appearance = value;
					Notify("Appearance");
					OnAppearanceChanged(EventArgs.Empty);
				}
			}
		}

		public ContentAlignment CheckAlign
		{
			get => checkAlign;
			set
			{
				checkAlign = value;
				Notify("CheckAlign");
			}
		}

		public bool Checked
		{
			get => checkState != CheckState.Unchecked;
			set
			{
				if (value != Checked)
				{
					CheckState = value ? CheckState.Checked : CheckState.Unchecked;
				}
			}
		}

		/// <summary>A change raises CheckedChanged (when Checked changes too) and then CheckStateChanged, whether it comes from the script or from the user.</summary>
		public CheckState CheckState
		{
			get => checkState;
			set
			{
				if (!Enum.IsDefined(typeof(CheckState), value))
				{
					throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(CheckState));
				}
				if (checkState != value)
				{
					bool oldChecked = Checked;
					checkState = value;
					Notify("CheckState");
					if (oldChecked != Checked)
					{
						OnCheckedChanged(EventArgs.Empty);
					}
					OnCheckStateChanged(EventArgs.Empty);
				}
			}
		}

		protected virtual void OnAppearanceChanged(EventArgs e)
		{
			AppearanceChanged?.Invoke(this, e);
		}

		protected virtual void OnCheckedChanged(EventArgs e)
		{
			CheckedChanged?.Invoke(this, e);
		}

		protected virtual void OnCheckStateChanged(EventArgs e)
		{
			CheckStateChanged?.Invoke(this, e);
		}

		/// <summary>A click toggles the state (unchecked, checked, then indeterminate with ThreeState) before Click is raised.</summary>
		protected override void OnClick(EventArgs e)
		{
			if (autoCheck)
			{
				switch (CheckState)
				{
				case CheckState.Unchecked:
					CheckState = CheckState.Checked;
					break;
				case CheckState.Checked:
					CheckState = threeState ? CheckState.Indeterminate : CheckState.Unchecked;
					break;
				default:
					CheckState = CheckState.Unchecked;
					break;
				}
			}
			base.OnClick(e);
		}

		public event EventHandler AppearanceChanged;

		public event EventHandler CheckedChanged;

		public event EventHandler CheckStateChanged;

		public override string ToString() => base.ToString() + ", CheckState: " + (int)CheckState;
	}

	public partial class RadioButton : ButtonBase
	{
		private bool isChecked;
		private bool autoCheck = true;
		private Appearance appearance = Appearance.Normal;
		private ContentAlignment checkAlign = ContentAlignment.MiddleLeft;

		public RadioButton()
		{
			TextAlign = ContentAlignment.MiddleLeft;
			TabStop = false;
			SetStyle(ControlStyles.StandardClick, false);
		}

		protected override Size DefaultSize => new Size(104, 24);

		public bool AutoCheck
		{
			get => autoCheck;
			set => autoCheck = value;
		}

		public Appearance Appearance
		{
			get => appearance;
			set
			{
				if (appearance != value)
				{
					appearance = value;
					Notify("Appearance");
					AppearanceChanged?.Invoke(this, EventArgs.Empty);
				}
			}
		}

		public ContentAlignment CheckAlign
		{
			get => checkAlign;
			set
			{
				checkAlign = value;
				Notify("CheckAlign");
			}
		}

		/// <summary>Checking a button unchecks the other buttons of its container first (their CheckedChanged is raised before the one of this button).</summary>
		public bool Checked
		{
			get => isChecked;
			set
			{
				if (isChecked == value)
				{
					return;
				}
				isChecked = value;
				Notify("Checked");
				if (value)
				{
					UncheckOthers();
				}
				OnCheckedChanged(EventArgs.Empty);
			}
		}

		private void UncheckOthers()
		{
			Control p = ParentInternal;
			if (p == null)
			{
				return;
			}
			foreach (Control sibling in p.Children())
			{
				if (sibling != this && sibling is RadioButton other && other.isChecked)
				{
					other.Checked = false;
				}
			}
		}

		public void PerformClick()
		{
			if (CanSelect)
			{
				OnClick(EventArgs.Empty);
			}
		}

		protected virtual void OnCheckedChanged(EventArgs e)
		{
			CheckedChanged?.Invoke(this, e);
		}

		protected override void OnClick(EventArgs e)
		{
			if (autoCheck)
			{
				Checked = true;
			}
			base.OnClick(e);
		}

		public event EventHandler AppearanceChanged;

		public event EventHandler CheckedChanged;

		public override string ToString() => base.ToString() + ", Checked: " + Checked;
	}

	public abstract partial class TextBoxBase : Control
	{
		private bool multiline;
		private bool readOnly;
		private bool acceptsTab;
		private bool wordWrap = true;
		private bool hideSelection = true;
		private bool modified;
		private bool shortcutsEnabled = true;
		private int maxLength = 32767;
		private int selectionStart;
		private int selectionLength;
		private BorderStyle borderStyle = BorderStyle.Fixed3D;

		internal TextBoxBase()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick | ControlStyles.UserMouse, false);
			SetStyle(ControlStyles.FixedHeight, true);
			AutoSize = true;
		}

		protected override Size DefaultSize => new Size(100, 20);

		internal override Size NonClientSize => UserControl.BorderSize(borderStyle);

		internal override Point ClientOffset => new Point(UserControl.BorderSize(borderStyle).Width / 2, UserControl.BorderSize(borderStyle).Height / 2);

		public override string Text
		{
			get => base.Text;
			set
			{
				value = value ?? string.Empty;
				if (value == base.Text)
				{
					return;
				}
				base.Text = value;
				selectionStart = 0;
				selectionLength = 0;
				SetModified(false);
			}
		}

		public override Color BackColor
		{
			get
			{
				if (HasOwnBackColor)
				{
					return base.BackColor;
				}
				return readOnly ? DefaultBackColor : Color.FromArgb(255, 255, 255);
			}
			set => base.BackColor = value;
		}

		public override Color ForeColor
		{
			get => HasOwnForeColor ? base.ForeColor : Color.FromArgb(0, 0, 0);
			set => base.ForeColor = value;
		}

		/// <summary>The keys the text box takes itself, so that they are no dialog keys (TextBoxBase.IsInputKey): Tab with AcceptsTab, Backspace, Page and Home/End keys.</summary>
		protected override bool IsInputKey(Keys keyData)
		{
			if ((keyData & Keys.Alt) == Keys.Alt)
			{
				return false;
			}
			switch (keyData & Keys.KeyCode)
			{
			case Keys.Tab:
				return multiline && acceptsTab && (keyData & Keys.Control) == Keys.None;
			case Keys.Escape:
				if (multiline)
				{
					return false;
				}
				break;
			case Keys.Back:
				if (!readOnly)
				{
					return true;
				}
				break;
			case Keys.PageUp:
			case Keys.PageDown:
			case Keys.Home:
			case Keys.End:
				return true;
			}
			return base.IsInputKey(keyData);
		}

		public virtual bool Multiline
		{
			get => multiline;
			set
			{
				if (multiline != value)
				{
					multiline = value;
					Notify("Multiline");
					OnMultilineChanged(EventArgs.Empty);
				}
			}
		}

		public bool ReadOnly
		{
			get => readOnly;
			set
			{
				if (readOnly != value)
				{
					readOnly = value;
					Notify("ReadOnly");
					OnReadOnlyChanged(EventArgs.Empty);
				}
			}
		}

		public bool AcceptsTab
		{
			get => acceptsTab;
			set
			{
				if (acceptsTab != value)
				{
					acceptsTab = value;
					OnAcceptsTabChanged(EventArgs.Empty);
				}
			}
		}

		public bool WordWrap
		{
			get => wordWrap;
			set
			{
				wordWrap = value;
				Notify("WordWrap");
			}
		}

		public bool HideSelection
		{
			get => hideSelection;
			set
			{
				if (hideSelection != value)
				{
					hideSelection = value;
					OnHideSelectionChanged(EventArgs.Empty);
				}
			}
		}

		public bool Modified
		{
			get => modified;
			set => SetModified(value);
		}

		private void SetModified(bool value)
		{
			if (modified != value)
			{
				modified = value;
				OnModifiedChanged(EventArgs.Empty);
			}
		}

		public virtual bool ShortcutsEnabled
		{
			get => shortcutsEnabled;
			set => shortcutsEnabled = value;
		}

		public virtual int MaxLength
		{
			get => maxLength;
			set
			{
				if (value < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "'" + value + "' is not a valid value for 'MaxLength'. 'MaxLength' must be greater than or equal to 0.");
				}
				maxLength = value;
				Notify("MaxLength");
			}
		}

		public BorderStyle BorderStyle
		{
			get => borderStyle;
			set
			{
				if (borderStyle != value)
				{
					borderStyle = value;
					Notify("BorderStyle");
					OnBorderStyleChanged(EventArgs.Empty);
				}
			}
		}

		public bool CanUndo => false;

		public virtual int TextLength => Text.Length;

		public string[] Lines
		{
			get
			{
				string text = Text;
				if (text.Length == 0)
				{
					return Array.Empty<string>();
				}
				return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			}
			set => Text = value == null ? string.Empty : string.Join("\r\n", value);
		}

		public int SelectionStart
		{
			get => Math.Min(selectionStart, Text.Length);
			set
			{
				if (value < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "'" + value + "' is not a valid value for 'SelectionStart'. 'SelectionStart' must be greater than or equal to 0.");
				}
				Select(value, SelectionLength);
			}
		}

		public virtual int SelectionLength
		{
			get => Math.Min(selectionLength, Text.Length - SelectionStart);
			set
			{
				if (value < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(value), value, "'" + value + "' is not a valid value for 'SelectionLength'. 'SelectionLength' must be greater than or equal to 0.");
				}
				Select(SelectionStart, value);
			}
		}

		public virtual string SelectedText
		{
			get => Text.Substring(SelectionStart, SelectionLength);
			set
			{
				value = value ?? string.Empty;
				string text = Text;
				int start = SelectionStart;
				int length = SelectionLength;
				string replaced = text.Substring(0, start) + value + text.Substring(start + length);
				base.Text = replaced;
				selectionStart = start + value.Length;
				selectionLength = 0;
				SetModified(true);
			}
		}

		public void Select(int start, int length)
		{
			if (start < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(start), start, "'" + start + "' is not a valid value for 'start'. 'start' must be greater than or equal to 0.");
			}
			int textLength = Text.Length;
			start = Math.Min(start, textLength);
			if (length < 0)
			{
				length = 0;
			}
			length = Math.Min(length, textLength - start);
			selectionStart = start;
			selectionLength = length;
			Notify("Selection");
		}

		public void SelectAll()
		{
			Select(0, Text.Length);
		}

		public void DeselectAll()
		{
			Select(SelectionStart, 0);
		}

		public void AppendText(string text)
		{
			if (!string.IsNullOrEmpty(text))
			{
				base.Text = Text + text;
				selectionStart = Text.Length;
				selectionLength = 0;
				SetModified(true);
			}
		}

		public void Clear()
		{
			Text = string.Empty;
		}

		public void ClearUndo()
		{
		}

		public void ScrollToCaret()
		{
			Notify("ScrollToCaret");
		}

		/// <summary>Called by the peer when the user typed: the text changes, the selection follows the caret and Modified becomes true.</summary>
		internal void UserChangedText(string text, int selectionStart, int selectionLength)
		{
			text = text ?? string.Empty;
			bool changed = text != base.Text;
			if (changed)
			{
				base.Text = text;
				SetModified(true);
			}
			this.selectionStart = Math.Min(selectionStart, text.Length);
			this.selectionLength = Math.Min(selectionLength, text.Length - this.selectionStart);
		}

		protected virtual void OnAcceptsTabChanged(EventArgs e)
		{
			AcceptsTabChanged?.Invoke(this, e);
		}

		protected virtual void OnBorderStyleChanged(EventArgs e)
		{
			BorderStyleChanged?.Invoke(this, e);
		}

		protected virtual void OnHideSelectionChanged(EventArgs e)
		{
			HideSelectionChanged?.Invoke(this, e);
		}

		protected virtual void OnModifiedChanged(EventArgs e)
		{
			ModifiedChanged?.Invoke(this, e);
		}

		protected virtual void OnMultilineChanged(EventArgs e)
		{
			MultilineChanged?.Invoke(this, e);
		}

		protected virtual void OnReadOnlyChanged(EventArgs e)
		{
			ReadOnlyChanged?.Invoke(this, e);
		}

		public event EventHandler AcceptsTabChanged;

		public event EventHandler BorderStyleChanged;

		public event EventHandler HideSelectionChanged;

		public event EventHandler ModifiedChanged;

		public event EventHandler MultilineChanged;

		public event EventHandler ReadOnlyChanged;

		public override string ToString()
		{
			string text = Text;
			if (text.Length > 40)
			{
				text = text.Substring(0, 40) + "...";
			}
			return base.ToString() + ", Text: " + text;
		}
	}

	public partial class TextBox : TextBoxBase
	{
		private bool acceptsReturn;
		private char passwordChar;
		private bool useSystemPasswordChar;
		private CharacterCasing characterCasing = CharacterCasing.Normal;
		private ScrollBars scrollBars = ScrollBars.None;
		private HorizontalAlignment textAlign = HorizontalAlignment.Left;
		private string placeholderText = string.Empty;
		private AutoCompleteMode autoCompleteMode = AutoCompleteMode.None;
		private AutoCompleteSource autoCompleteSource = AutoCompleteSource.None;

		public TextBox()
		{
		}

		public bool AcceptsReturn
		{
			get => acceptsReturn;
			set => acceptsReturn = value;
		}

		/// <summary>A multi-line text box with AcceptsReturn takes Enter itself, so the form's default button does not get it.</summary>
		protected override bool IsInputKey(Keys keyData)
		{
			if (Multiline && (keyData & Keys.Alt) == Keys.None && (keyData & Keys.KeyCode) == Keys.Return)
			{
				return acceptsReturn;
			}
			return base.IsInputKey(keyData);
		}

		public char PasswordChar
		{
			get => useSystemPasswordChar ? '*' : passwordChar;
			set
			{
				if (passwordChar != value)
				{
					passwordChar = value;
					Notify("PasswordChar");
				}
			}
		}

		public bool UseSystemPasswordChar
		{
			get => useSystemPasswordChar;
			set
			{
				if (useSystemPasswordChar != value)
				{
					useSystemPasswordChar = value;
					Notify("PasswordChar");
				}
			}
		}

		public CharacterCasing CharacterCasing
		{
			get => characterCasing;
			set
			{
				characterCasing = value;
				Notify("CharacterCasing");
			}
		}

		public ScrollBars ScrollBars
		{
			get => scrollBars;
			set
			{
				scrollBars = value;
				Notify("ScrollBars");
			}
		}

		public HorizontalAlignment TextAlign
		{
			get => textAlign;
			set
			{
				if (textAlign != value)
				{
					textAlign = value;
					Notify("TextAlign");
					OnTextAlignChanged(EventArgs.Empty);
				}
			}
		}

		public virtual string PlaceholderText
		{
			get => placeholderText;
			set
			{
				placeholderText = value ?? string.Empty;
				Notify("PlaceholderText");
			}
		}

		public AutoCompleteMode AutoCompleteMode
		{
			get => autoCompleteMode;
			set => autoCompleteMode = value;
		}

		public AutoCompleteSource AutoCompleteSource
		{
			get => autoCompleteSource;
			set => autoCompleteSource = value;
		}

		protected virtual void OnTextAlignChanged(EventArgs e)
		{
			TextAlignChanged?.Invoke(this, e);
		}

		public event EventHandler TextAlignChanged;
	}
}
