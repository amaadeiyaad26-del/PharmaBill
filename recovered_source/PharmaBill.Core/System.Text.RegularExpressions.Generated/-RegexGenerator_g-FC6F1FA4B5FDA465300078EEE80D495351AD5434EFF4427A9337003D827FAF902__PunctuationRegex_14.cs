using System.CodeDom.Compiler;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__PunctuationRegex_14 : Regex
{
	private sealed class RunnerFactory : RegexRunnerFactory
	{
		private sealed class Runner : RegexRunner
		{
			protected override void Scan(ReadOnlySpan<char> inputSpan)
			{
				while (TryFindNextPossibleStartingPosition(inputSpan) && !TryMatchAtCurrentPosition(inputSpan) && runtextpos != inputSpan.Length)
				{
					runtextpos++;
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
				}
			}

			private bool TryFindNextPossibleStartingPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				if ((uint)num < (uint)inputSpan.Length)
				{
					int num2 = inputSpan.Slice(num).IndexOfNonAsciiOrAny_BFFE8177F92F37F4E241C99748DFE317D23275CB27C8199145CC6E82FE86EF5C();
					if (num2 >= 0)
					{
						runtextpos = num + num2;
						return true;
					}
				}
				runtextpos = inputSpan.Length;
				return false;
			}

			private bool TryMatchAtCurrentPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				int start = num;
				ReadOnlySpan<char> readOnlySpan = inputSpan.Slice(num);
				int i;
				for (i = 0; (uint)i < (uint)readOnlySpan.Length; i++)
				{
					char c;
					if (!(((c = readOnlySpan[i]) < '\u0080') ? (("\uffff\uffff뿟ﰀ\u0001\uf800\u0001\uf800"[(int)c >> 4] & (1 << (c & 0xF))) != 0) : RegexRunner.CharInClass(c, "\u0001\u0004\f%&./\0\u0002\u0004\u0005\u0003\u0001\0\0\t\n\v\0")))
					{
						break;
					}
				}
				if (i == 0)
				{
					return false;
				}
				readOnlySpan = readOnlySpan.Slice(i);
				Capture(0, start, runtextpos = num + i);
				return true;
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__PunctuationRegex_14 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__PunctuationRegex_14();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__PunctuationRegex_14()
	{
		pattern = "[^\\p{L}\\p{N}.%]+";
		roptions = RegexOptions.CultureInvariant;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		capsize = 1;
	}
}
