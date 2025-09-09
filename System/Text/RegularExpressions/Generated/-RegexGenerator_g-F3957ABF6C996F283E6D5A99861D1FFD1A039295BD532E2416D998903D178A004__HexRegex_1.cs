using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "9.0.12.6610")]
[SkipLocalsInit]
internal sealed class _003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__HexRegex_1 : Regex
{
	private sealed class RunnerFactory : RegexRunnerFactory
	{
		private sealed class Runner : RegexRunner
		{
			protected override void Scan(ReadOnlySpan<char> inputSpan)
			{
				if (TryFindNextPossibleStartingPosition(inputSpan) && !TryMatchAtCurrentPosition(inputSpan))
				{
					runtextpos = inputSpan.Length;
				}
			}

			private bool TryFindNextPossibleStartingPosition(ReadOnlySpan<char> inputSpan)
			{
				int pos = runtextpos;
				if (pos <= inputSpan.Length - 2 && pos == 0)
				{
					return true;
				}
				runtextpos = inputSpan.Length;
				return false;
			}

			private bool TryMatchAtCurrentPosition(ReadOnlySpan<char> inputSpan)
			{
				int pos = runtextpos;
				int matchStart = pos;
				int loop_iteration = 0;
				int stackpos = 0;
				int startingStackpos = 0;
				ReadOnlySpan<char> slice = inputSpan.Slice(pos);
				if (pos != 0)
				{
					UncaptureUntil(0);
					return false;
				}
				startingStackpos = stackpos;
				loop_iteration = 0;
				while (true)
				{
					_003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__Utilities.StackPush(ref runstack, ref stackpos, Crawlpos(), pos);
					loop_iteration++;
					int capture_starting_pos = pos;
					if ((uint)slice.Length < 2u || !char.IsAsciiHexDigitUpper(slice[0]) || !char.IsAsciiHexDigitUpper(slice[1]))
					{
						break;
					}
					pos += 2;
					slice = inputSpan.Slice(pos);
					Capture(1, capture_starting_pos, pos);
				}
				if (--loop_iteration < 0)
				{
					UncaptureUntil(0);
					return false;
				}
				pos = runstack[--stackpos];
				UncaptureUntil(runstack[--stackpos]);
				slice = inputSpan.Slice(pos);
				if (loop_iteration == 0)
				{
					UncaptureUntil(0);
					return false;
				}
				stackpos = startingStackpos;
				runtextpos = pos;
				Capture(0, matchStart, pos);
				return true;
				[MethodImpl(MethodImplOptions.AggressiveInlining)]
				void UncaptureUntil(int capturePosition)
				{
					while (Crawlpos() > capturePosition)
					{
						Uncapture();
					}
				}
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__HexRegex_1 Instance = new _003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__HexRegex_1();

	private _003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__HexRegex_1()
	{
		pattern = "^([A-F0-9]{2})+";
		roptions = RegexOptions.None;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 2;
	}
}
