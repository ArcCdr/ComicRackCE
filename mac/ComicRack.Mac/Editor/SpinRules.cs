using System;
using System.Globalization;

namespace ComicRack.Mac.Editor
{
	/// <summary>
	/// What the little up and down buttons next to a number box do (<c>SpinButton.AddUpDown</c> and <c>SpinButton.SetTextBoxValue</c>): the number in the box (or, when the box is empty,
	/// the prompt text it shows) plus or minus the step, kept between the limits; a box without a number starts at <c>start</c>.
	/// </summary>
	public static class SpinRules
	{
		/// <summary>The step of a number box: the first value, the limits and the step of one press.</summary>
		public readonly struct Config
		{
			public Config(int start, int min, int max, int increment)
			{
				Start = start;
				Min = min;
				Max = max;
				Increment = increment;
			}

			/// <summary>One step from 1 without limits (<c>AddUpDown(textBox)</c>).</summary>
			public static Config Default => new Config(1, int.MinValue, int.MaxValue, 1);

			public int Start { get; }

			public int Min { get; }

			public int Max { get; }

			public int Increment { get; }
		}

		/// <summary>The text after one press: <paramref name="up"/> adds the step, else subtracts it.</summary>
		public static string Step(string text, string prompt, Config config, bool up)
		{
			bool parsed = TryParse(text, out int n);
			if (!parsed)
			{
				parsed = TryParse(prompt, out n);
			}
			if (!parsed)
			{
				return config.Start.ToString(CultureInfo.InvariantCulture);
			}
			long value = (long)n + (up ? config.Increment : -config.Increment);
			return ((int)Math.Clamp(value, config.Min, config.Max)).ToString(CultureInfo.InvariantCulture);
		}

		private static bool TryParse(string text, out int value)
		{
			return int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
		}

		/// <summary>The steps of the Details tab's number boxes (<c>ComicBookDialog</c>'s constructor), by the name of the box; the day starts at the month, as the original has it.</summary>
		public static Config ForField(string name, DateTime now)
		{
			switch (name)
			{
				case "txCount":
				case "txAlternateCount":
					return new Config(1, 0, int.MaxValue, 1);
				case "txYear":
					return new Config(now.Year, int.MinValue, int.MaxValue, 1);
				case "txMonth":
					return new Config(now.Month, 1, 12, 1);
				case "txDay":
					return new Config(now.Month, 1, 31, 1);
				default:
					return Config.Default;
			}
		}
	}
}
