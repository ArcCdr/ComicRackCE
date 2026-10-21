using System;
using System.Windows.Input;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>
	/// A command of the menu bar that works on the book of the current tab (First Page, Fit All, Zoom In ...): it runs the matching command of the current
	/// <see cref="ReaderViewModel"/> and is disabled while there is no book. The menu items are made once; this follows the tab.
	/// </summary>
	public sealed class CurrentReaderCommand : ICommand
	{
		private readonly MainViewModel owner;
		private readonly Func<ReaderViewModel, ICommand> pick;

		public CurrentReaderCommand(MainViewModel owner, Func<ReaderViewModel, ICommand> pick)
		{
			this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
			this.pick = pick ?? throw new ArgumentNullException(nameof(pick));
			owner.ReaderChanged += (sender, e) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
		}

		public event EventHandler CanExecuteChanged;

		private ICommand Target => owner.Reader != null ? pick(owner.Reader) : null;

		public bool CanExecute(object parameter) => Target?.CanExecute(parameter) ?? false;

		public void Execute(object parameter)
		{
			ICommand target = Target;
			if (target != null && target.CanExecute(parameter))
			{
				target.Execute(parameter);
			}
		}
	}
}
