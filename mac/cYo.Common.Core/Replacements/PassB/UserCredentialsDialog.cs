// Pass B replacement for cYo.Common/Win32/UserCredentialsDialog.cs (excluded from the pass B compile).
// The original wraps the Windows credential prompt (credui.dll) and demands a UIPermission (code access security,
// not part of .NET 10). This stand-in keeps the public surface and the property validation; it never shows a prompt:
// RunDialog reports "cancelled", so HttpAccess treats a server that asks for credentials as "no credentials".
// ShowDialog itself is the cYo.Compat.Forms CommonDialog member, which is not supported on the Mac yet.
using System;
using System.ComponentModel;
using System.Drawing;
using System.Security;
using System.Windows.Forms;

namespace cYo.Common.Win32
{
	public class UserCredentialsDialog : CommonDialog
	{
		private const int MaxLength = 100;

		private string user;

		private SecureString password;

		private string domain;

		private string target;

		private string message;

		private string caption;

		private Image banner;

		public string User
		{
			get { return user; }
			set { user = CheckLength(value, "user name", "User"); }
		}

		public SecureString Password
		{
			get { return password; }
			set
			{
				if (value != null && value.Length > MaxLength)
				{
					throw new ArgumentException($"The password has a maximum length of {MaxLength} characters.", "Password");
				}
				password = value;
			}
		}

		public string Domain
		{
			get { return domain; }
			set { domain = CheckLength(value, "domain name", "Domain"); }
		}

		public string Target
		{
			get { return target; }
			set { target = CheckLength(value, "target", "Target"); }
		}

		public string Message
		{
			get { return message; }
			set { message = CheckLength(value, "message", "Message"); }
		}

		public string Caption
		{
			get { return caption; }
			set { caption = CheckLength(value, "caption", "Caption"); }
		}

		public Image Banner
		{
			get { return banner; }
			set
			{
				if (value != null)
				{
					if (value.Width != 320)
					{
						throw new ArgumentException($"The banner image width must be {320} pixels.", "Banner");
					}
					if (value.Height != 60)
					{
						throw new ArgumentException($"The banner image height must be {60} pixels.", "Banner");
					}
				}
				banner = value;
			}
		}

		public bool SaveChecked { get; set; }

		public UserCredentialsDialogFlags Flags { get; set; }

		public UserCredentialsDialog(string target = null, string caption = null, string message = null, Image banner = null)
		{
			Reset();
			Target = target;
			Caption = caption;
			Message = message;
			Banner = banner;
		}

		public void ConfirmCredentials(bool confirm)
		{
			// No credential store on the Mac in the MVP: nothing to confirm.
		}

		public string PasswordToString()
		{
			if (password == null)
			{
				return null;
			}
			IntPtr intPtr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(password);
			try
			{
				return System.Runtime.InteropServices.Marshal.PtrToStringUni(intPtr);
			}
			finally
			{
				System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(intPtr);
			}
		}

		protected override bool RunDialog(IntPtr hwndOwner)
		{
			User = null;
			Password = null;
			return false;
		}

		public override void Reset()
		{
			target = Application.ProductName ?? AppDomain.CurrentDomain.FriendlyName;
			user = null;
			password = null;
			domain = null;
			caption = null;
			message = null;
			banner = null;
			SaveChecked = false;
			Flags = UserCredentialsDialogFlags.Default;
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (password != null)
			{
				password.Dispose();
				password = null;
			}
		}

		private static string CheckLength(string value, string what, string paramName)
		{
			if (value != null && value.Length > MaxLength)
			{
				throw new ArgumentException($"The {what} has a maximum length of {MaxLength} characters.", paramName);
			}
			return value;
		}
	}
}
