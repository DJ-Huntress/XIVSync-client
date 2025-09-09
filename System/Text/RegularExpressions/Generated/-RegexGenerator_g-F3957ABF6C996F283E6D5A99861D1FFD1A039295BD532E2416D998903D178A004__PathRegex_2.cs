using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "9.0.12.6610")]
[SkipLocalsInit]
internal sealed class _003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__PathRegex_2 : Regex
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
				int alternation_branch = 0;
				int alternation_starting_pos = 0;
				int lazyloop_pos = 0;
				int lazyloop_pos2 = 0;
				ReadOnlySpan<char> slice = inputSpan.Slice(pos);
				if (pos != 0)
				{
					return false;
				}
				alternation_starting_pos = pos;
				char ch;
				if ((uint)slice.Length >= 3u && char.IsAsciiLetter(slice[0]) && slice.Slice(1).StartsWith(":\\", StringComparison.OrdinalIgnoreCase) && (uint)slice.Length >= 4u && !(((ch = slice[3]) < '\u0080') ? (("㸀\0\u2001Ͽ\ufffe響\ufffe߿"[(int)ch >> 4] & (1 << (ch & 0xF))) == 0) : (!RegexRunner.CharInClass(ch, "\0\u0012\0\t\u000e !-.0:A[\\]_`a{İı"))))
				{
					pos += 4;
					slice = inputSpan.Slice(pos);
					lazyloop_pos = pos;
					goto IL_012f;
				}
				goto IL_0136;
				IL_01b8:
				CheckTimeout();
				pos = lazyloop_pos2;
				slice = inputSpan.Slice(pos);
				if (slice.IsEmpty || (((ch = slice[0]) < '\u0080') ? (("㸀\0ꀁϿ\ufffe蟿\ufffe߿"[(int)ch >> 4] & (1 << (ch & 0xF))) == 0) : (!RegexRunner.CharInClass(ch, "\0\u0010\0\t\u000e !-./:A[_`a{İı"))))
				{
					return false;
				}
				pos++;
				slice = inputSpan.Slice(pos);
				lazyloop_pos2 = pos;
				goto IL_0225;
				IL_0225:
				alternation_branch = 1;
				goto IL_023c;
				IL_0136:
				pos = alternation_starting_pos;
				slice = inputSpan.Slice(pos);
				if (slice.IsEmpty || slice[0] != '/')
				{
					return false;
				}
				if ((uint)slice.Length < 2u || (((ch = slice[1]) < '\u0080') ? (("㸀\0ꀁϿ\ufffe蟿\ufffe߿"[(int)ch >> 4] & (1 << (ch & 0xF))) == 0) : (!RegexRunner.CharInClass(ch, "\0\u0010\0\t\u000e !-./:A[_`a{İı"))))
				{
					return false;
				}
				pos += 2;
				slice = inputSpan.Slice(pos);
				lazyloop_pos2 = pos;
				goto IL_0225;
				IL_012f:
				alternation_branch = 0;
				goto IL_023c;
				IL_023c:
				while (true)
				{
					if (pos < inputSpan.Length - 1 || ((uint)pos < (uint)inputSpan.Length && inputSpan[pos] != '\n'))
					{
						CheckTimeout();
						if (alternation_branch == 0)
						{
							break;
						}
						if (alternation_branch != 1)
						{
							continue;
						}
						goto IL_01b8;
					}
					runtextpos = pos;
					Capture(0, matchStart, pos);
					return true;
				}
				CheckTimeout();
				pos = lazyloop_pos;
				slice = inputSpan.Slice(pos);
				if (!slice.IsEmpty && !(((ch = slice[0]) < '\u0080') ? (("㸀\0\u2001Ͽ\ufffe響\ufffe߿"[(int)ch >> 4] & (1 << (ch & 0xF))) == 0) : (!RegexRunner.CharInClass(ch, "\0\u0012\0\t\u000e !-.0:A[\\]_`a{İı"))))
				{
					pos++;
					slice = inputSpan.Slice(pos);
					lazyloop_pos = pos;
					goto IL_012f;
				}
				goto IL_0136;
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__PathRegex_2 Instance = new _003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__PathRegex_2();

	private _003CRegexGenerator_g_003EF3957ABF6C996F283E6D5A99861D1FFD1A039295BD532E2416D998903D178A004__PathRegex_2()
	{
		pattern = "^(?:[a-zA-Z]:\\\\[\\w\\s\\-\\\\]+?|\\/(?:[\\w\\s\\-\\/])+?)$";
		roptions = RegexOptions.ECMAScript;
		internalMatchTimeout = TimeSpan.FromMilliseconds(5000L, 0L);
		factory = new RunnerFactory();
		capsize = 1;
	}
}
