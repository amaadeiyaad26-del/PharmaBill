using System.CodeDom.Compiler;
using System.Collections;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__LabelledDateRegex_6 : Regex
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
				if (num <= inputSpan.Length - 8)
				{
					int num2 = inputSpan.Slice(num).IndexOfAny(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_indexOfAnyStrings_OrdinalIgnoreCase_9524FCE8E4AC33AF9A2FA6F2B7ACD0D708966E284FA44062557B6F0A8B966F45);
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
				int num2 = 0;
				int num3 = 0;
				int num4 = 0;
				int num5 = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if (!_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPreWordCharBoundary(inputSpan, num))
				{
					UncaptureUntil(0);
					return false;
				}
				if (span.IsEmpty || (span[0] | 0x20) != 100)
				{
					UncaptureUntil(0);
					return false;
				}
				if ((uint)span.Length < 2u)
				{
					UncaptureUntil(0);
					return false;
				}
				switch (span[1])
				{
				case 'A':
				case 'a':
					if ((uint)span.Length < 4u || !span.Slice(2).StartsWith("te", StringComparison.OrdinalIgnoreCase))
					{
						UncaptureUntil(0);
						return false;
					}
					num += 4;
					span = inputSpan.Slice(num);
					break;
				case 'T':
				case 't':
					num += 2;
					span = inputSpan.Slice(num);
					break;
				default:
					UncaptureUntil(0);
					return false;
				}
				if (!_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPostWordCharBoundary(inputSpan, num))
				{
					UncaptureUntil(0);
					return false;
				}
				num4 = num;
				int num6 = span.IndexOfAnyExcept('\t', ' ');
				if (num6 < 0)
				{
					num6 = span.Length;
				}
				span = span.Slice(num6);
				num += num6;
				num5 = num;
				while (true)
				{
					num3 = Crawlpos();
					char c;
					if (!span.IsEmpty && (((c = span[0]) == '-') | (c == '.') | (c == ':')))
					{
						span = span.Slice(1);
						num++;
					}
					int num7 = span.IndexOfAnyExcept('\t', ' ');
					if (num7 < 0)
					{
						num7 = span.Length;
					}
					span = span.Slice(num7);
					num += num7;
					num2 = num;
					int i;
					for (i = 0; i < 2 && (uint)i < (uint)span.Length && char.IsDigit(span[i]); i++)
					{
					}
					if (i != 0)
					{
						span = span.Slice(i);
						num += i;
						int num8 = num;
						uint num9;
						if (!span.IsEmpty && (int)((uint)(-2147024896 << (int)(short)(num9 = (ushort)(span[0] - 32))) & (num9 - 32)) < 0)
						{
							num++;
							span = inputSpan.Slice(num);
							int j;
							for (j = 0; j < 2 && (uint)j < (uint)span.Length && char.IsDigit(span[j]); j++)
							{
							}
							if (j != 0)
							{
								span = span.Slice(j);
								num += j;
								if (!span.IsEmpty && (int)((uint)(-2147024896 << (int)(short)(num9 = (ushort)(span[0] - 32))) & (num9 - 32)) < 0)
								{
									num++;
									span = inputSpan.Slice(num);
									int k;
									for (k = 0; k < 4 && (uint)k < (uint)span.Length && char.IsDigit(span[k]); k++)
									{
									}
									if (k >= 2)
									{
										span = span.Slice(k);
										num += k;
										break;
									}
								}
							}
						}
						num = num8;
						span = inputSpan.Slice(num);
						if (!span.IsEmpty && (((c = span[0]) == ' ') | (c == '-') | (c == '/')))
						{
							span = span.Slice(1);
							num++;
						}
						int l;
						for (l = 0; l < 9 && (uint)l < (uint)span.Length; l++)
						{
							if (!(((c = span[l]) < '\u0080') ? char.IsAsciiLetter(c) : RegexRunner.CharInClass(c, "\0\u0006\0A[a{KÅ")))
							{
								break;
							}
						}
						if (l >= 3)
						{
							span = span.Slice(l);
							num += l;
							int num10 = span.IndexOfAnyExcept(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_ascii_1F000000000000000000000);
							if (num10 < 0)
							{
								num10 = span.Length;
							}
							span = span.Slice(num10);
							num += num10;
							int m;
							for (m = 0; m < 4 && (uint)m < (uint)span.Length && char.IsDigit(span[m]); m++)
							{
							}
							if (m >= 2)
							{
								span = span.Slice(m);
								num += m;
								break;
							}
						}
					}
					UncaptureUntil(num3);
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num4 >= num5)
					{
						UncaptureUntil(0);
						return false;
					}
					num = --num5;
					span = inputSpan.Slice(num);
				}
				Capture(1, num2, num);
				runtextpos = num;
				Capture(0, start, num);
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

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__LabelledDateRegex_6 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__LabelledDateRegex_6();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__LabelledDateRegex_6()
	{
		pattern = "\\b(?:Date|Dt)\\b[ \\t]*[:.\\-]?[ \\t]*(?<d>\\d{1,2}[ /.\\-]\\d{1,2}[ /.\\-]\\d{2,4}|\\d{1,2}[ \\-/]?[A-Za-z]{3,9}[ ,.\\-/]*\\d{2,4})";
		roptions = RegexOptions.IgnoreCase;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		CapNames = new Hashtable
		{
			{ "0", 0 },
			{ "d", 1 }
		};
		capslist = new string[2] { "0", "d" };
		capsize = 2;
	}
}
